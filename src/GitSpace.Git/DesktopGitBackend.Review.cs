using GitSpace.Core;

namespace GitSpace.Git;

public sealed partial class DesktopGitBackend
{
    private sealed record VersionedFile(bool Exists, string Mode, string Text, string? RawHash = null)
    {
        public string Hash => RawHash ?? GitText.Version(Exists, Mode, Text);
    }
    private int _historyLimit = 200;
    private string _cachedHistoryKey = "";
    private GitCommit[] _cachedHistory = [];
    public long HistoryReadCount { get; private set; }
    private static void ValidateAuthor(GitRequest r)
    {
        if (string.IsNullOrWhiteSpace(r.Author) || string.IsNullOrWhiteSpace(r.Email) || (r.Author + r.Email).IndexOfAny(['\r', '\n', '\0', '<', '>']) >= 0)
            throw new ArgumentException("A valid Git author name and email are required.");
    }
    private Task<GitProcessResult> RunAsAuthor(GitRequest r, CancellationToken ct, params string[] args)
    {
        ValidateAuthor(r);
        return Run(ct, new[] { "-c", "user.name=" + r.Author, "-c", "user.email=" + r.Email }.Concat(args).ToArray());
    }
    private async Task<string> IndexPath(CancellationToken ct) => Path.GetFullPath(await Optional(ct, "rev-parse", "--git-path", "index").ConfigureAwait(false), _root);
    private async Task<string> IndexHash(CancellationToken ct)
    {
        var path = await IndexPath(ct).ConfigureAwait(false);
        return File.Exists(path) ? Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(path, ct).ConfigureAwait(false))).ToLowerInvariant() : GitText.Hash("");
    }
    private async Task<GitCommit[]> LoadHistory(Task<string> head, CancellationToken ct)
    {
        var id = await head.ConfigureAwait(false);
        // Shallow boundaries and replacement refs can change graph traversal without changing HEAD.
        var shallow = await Optional(ct, "rev-parse", "--git-path", "shallow").ConfigureAwait(false);
        var shallowPath = Path.GetFullPath(shallow, _root);
        var replacements = await Optional(ct, "for-each-ref", "--format=%(objectname)", "refs/replace/").ConfigureAwait(false);
        var key = _root + "\0" + id + "\0" + _historyLimit + "\0" + replacements + "\0" + (File.Exists(shallowPath) ? await File.ReadAllTextAsync(shallowPath, ct).ConfigureAwait(false) : "");
        if (_cachedHistoryKey == key) return _cachedHistory;
        HistoryReadCount++;
        var log = id.Length == 0 ? "" : await Optional(ct, "log", "-z", "-n", (_historyLimit + 1).ToString(System.Globalization.CultureInfo.InvariantCulture), "--format=%H%x00%an%x00%ae%x00%aI%x00%P%x00%B").ConfigureAwait(false);
        _cachedHistory = StatusParser.Log(log); _cachedHistoryKey = key; return _cachedHistory;
    }
    private async Task<VersionedFile> IndexFile(string path, CancellationToken ct)
    {
        var result = await Run(ct, "ls-files", "--stage", "-z", "--", path).ConfigureAwait(false);
        var entries = result.Output.Split('\0', StringSplitOptions.RemoveEmptyEntries);
        if (entries.Length == 0) return new(false, "100644", "");
        if (entries.Length != 1) throw new InvalidOperationException("Resolve this file's merge conflicts before partial staging.");
        var fields = entries[0][..entries[0].IndexOf('\t')].Split(' ');
        if (fields[2] != "0" || fields[0] is not ("100644" or "100755")) throw new InvalidOperationException("Partial staging requires a regular, resolved text file.");
        return new(true, fields[0], await ObjectText(fields[1], ct).ConfigureAwait(false));
    }
    private async Task<VersionedFile> HeadFile(string path, CancellationToken ct)
    {
        var result = await Optional(ct, "ls-tree", "-z", "HEAD", "--", path).ConfigureAwait(false);
        if (result.Length == 0) return new(false, "100644", "");
        var fields = result[..result.IndexOf('\t')].Split(' ');
        if (fields[0] is not ("100644" or "100755")) throw new InvalidOperationException("Partial staging requires a regular text file.");
        return new(true, fields[0], await ObjectText(fields[2], ct).ConfigureAwait(false));
    }
    private async Task<string> ObjectText(string id, CancellationToken ct)
    {
        var size = await Run(ct, "cat-file", "-s", id).ConfigureAwait(false);
        if (!long.TryParse(size.Output.Trim(), out var length) || length > GitSafety.MaximumTextBytes) throw new InvalidDataException("Blob exceeds the 2 MiB text limit.");
        var text = (await Run(ct, "cat-file", "blob", id).ConfigureAwait(false)).Output; GitSafety.Text(text); return text;
    }
    private async Task<(VersionedFile Before, VersionedFile After)> ReviewFiles(GitRequest r, CancellationToken ct)
    {
        var path = GitSafety.RelativePath(r.Path); var index = await IndexFile(path, ct).ConfigureAwait(false);
        if (r.Value == "staged") return (await HeadFile(path, ct).ConfigureAwait(false), index);
        if (r.Value is not ("" or "unstaged")) throw new ArgumentException("Unknown review mode.");
        var full = SafeFile(path); var exists = File.Exists(full); var mode = index.Mode;
        if (exists && !OperatingSystem.IsWindows() && (File.GetUnixFileMode(full) & UnixFileMode.UserExecute) != 0) mode = "100755";
        var raw = exists ? await ReadWorktree(path, ct).ConfigureAwait(false) : "";
        var content = await NormalizeIndexText(path, raw, ct).ConfigureAwait(false);
        return (index, new(exists, mode, content, GitText.Version(exists, mode, raw)));
    }
    private async Task<string> NormalizeIndexText(string path, string text, CancellationToken ct)
    {
        var output = (await Run(ct, "check-attr", "-z", "text", "eol", "filter", "working-tree-encoding", "--", path).ConfigureAwait(false)).Output.Split('\0');
        var attributes = new Dictionary<string, string>();
        for (var i = 0; i + 2 < output.Length; i += 3) attributes[output[i + 1]] = output[i + 2];
        bool Active(string name) => attributes.GetValueOrDefault(name, "unspecified") is not ("unspecified" or "unset");
        if (Active("filter") || Active("working-tree-encoding")) throw new InvalidOperationException("Custom Git filters/working-tree encodings require whole-file staging. Partial staging is disabled to avoid lossy conversion.");
        // Ask the installed Git to apply its real clean/EOL rules. In particular,
        // text=auto, lone CRs, mixed endings and an existing CRLF index entry do
        // not behave like a blanket Replace. Only an unreachable blob is written;
        // neither the index nor the working file is modified by this preview.
        var id = (await _git.RunAsync(_root, ["hash-object", "-w", "--stdin", "--path=" + path], ct, text).ConfigureAwait(false)).Output.Trim();
        return await ObjectText(id, ct).ConfigureAwait(false);
    }
    private async Task<GitResult> Review(GitRequest r, CancellationToken ct)
    {
        try
        {
            var (before, after) = await ReviewFiles(r, ct).ConfigureAwait(false);
            return new() { Before = before.Text, After = after.Text, BeforeExists = before.Exists, AfterExists = after.Exists, BeforeHash = before.Hash, AfterHash = after.Hash };
        }
        catch (Exception e) when (e is InvalidDataException or System.Text.DecoderFallbackException) { return new() { Binary = true, Text = e.Message }; }
    }
    private async Task UpdateSelectedText(GitRequest r, CancellationToken ct)
    {
        GitSafety.Text(r.Message); GitSafety.RelativePath(r.Path);
        var indexPath = await IndexPath(ct).ConfigureAwait(false); var lockPath = indexPath + ".lock";
        // Standard Git lock excludes other cooperating Git writers during validation + replacement.
        await using var guard = new FileStream(lockPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        var temporary = indexPath + ".gitspace-" + Guid.NewGuid().ToString("N");
        try
        {
            var staged = r.Operation == "unstageText";
            var (before, after) = await ReviewFiles(r with { Value = staged ? "staged" : "unstaged" }, ct).ConfigureAwait(false);
            GitText.Verify(r.BeforeHash, before.Hash); GitText.Verify(r.AfterHash, after.Hash);
            var target = staged ? before : after;
            if (r.Remove && (target.Exists || r.Message.Length != 0)) throw new InvalidOperationException("The reviewed target is not a deletion.");
            if (File.Exists(indexPath)) File.Copy(indexPath, temporary);
            var env = new Dictionary<string, string> { ["GIT_INDEX_FILE"] = temporary };
            if (r.Remove) await _git.RunAsync(_root, ["update-index", "--force-remove", "--", r.Path], ct, environment: env).ConfigureAwait(false);
            else
            {
                var id = (await _git.RunAsync(_root, ["hash-object", "-w", "--stdin"], ct, r.Message).ConfigureAwait(false)).Output.Trim();
                // Preserve the existing index mode for content-only partial changes.
                var mode = staged ? after.Mode : before.Exists ? before.Mode : after.Mode;
                await _git.RunAsync(_root, ["update-index", "--add", "--cacheinfo", mode, id, r.Path], ct, environment: env).ConfigureAwait(false);
            }
            ct.ThrowIfCancellationRequested(); File.Move(temporary, indexPath, true);
        }
        finally
        {
            await guard.DisposeAsync().ConfigureAwait(false);
            File.Delete(lockPath); File.Delete(temporary); File.Delete(temporary + ".lock");
        }
    }
    private async Task<GitResult> ReadConflict(string path, CancellationToken ct)
    {
        GitSafety.RelativePath(path); var entries = (await Run(ct, "ls-files", "--unmerged", "-z", "--", path).ConfigureAwait(false)).Output;
        if (entries.Length == 0) throw new InvalidOperationException("This file has no unresolved index conflict.");
        var versions = new Dictionary<int, string>();
        foreach (var entry in entries.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            var fields = entry[..entry.IndexOf('\t')].Split(' ');
            if (fields[0] is not ("100644" or "100755")) throw new InvalidOperationException("Resolve binary, symlink and submodule conflicts with an external tool.");
            versions[int.Parse(fields[2])] = await ObjectText(fields[1], ct).ConfigureAwait(false);
        }
        var work = await ReadWorktree(path, ct).ConfigureAwait(false);
        return new() { BaseText = versions.GetValueOrDefault(1, ""), Before = versions.GetValueOrDefault(2, ""), After = versions.GetValueOrDefault(3, ""), Text = work,
            BaseExists = versions.ContainsKey(1), BeforeExists = versions.ContainsKey(2), AfterExists = versions.ContainsKey(3), BeforeHash = GitText.Hash(entries), AfterHash = GitText.Version(File.Exists(SafeFile(path)), "work", work) };
    }
    private async Task ResolveConflict(GitRequest r, CancellationToken ct)
    {
        GitSafety.Confirm(r); GitSafety.Text(r.Message);
        var current = await ReadConflict(r.Path, ct).ConfigureAwait(false);
        GitText.Verify(r.BeforeHash, current.BeforeHash); GitText.Verify(r.AfterHash, current.AfterHash);
        if (r.Remove && current.BeforeExists && current.AfterExists) throw new InvalidOperationException("Neither conflict side deletes the file.");
        if (r.Remove) File.Delete(SafeFile(r.Path));
        else
        {
            if (GitText.ToLf(r.Message).Split('\n').Any(l => l.StartsWith("<<<<<<<", StringComparison.Ordinal) || l.StartsWith(">>>>>>>", StringComparison.Ordinal)))
                throw new InvalidOperationException("Remove all unresolved conflict markers before staging the resolution.");
            await File.WriteAllTextAsync(SafeFile(r.Path), r.Message, GitText.Utf8, ct).ConfigureAwait(false);
        }
        await Stage([r.Path], ct).ConfigureAwait(false);
    }
}
