using System.Text;
using GitSpace.Core;

namespace GitSpace.Git;

public sealed partial class DesktopGitBackend
{
    private static string PathInput(string[] paths)
    {
        if (paths.Length > 10000) throw new ArgumentException("Select at most 10,000 files per operation.");
        return string.Join('\0', paths.Select(GitSafety.RelativePath)) + '\0';
    }
    private Task<GitProcessResult> Stage(string[] paths, CancellationToken ct)
    {
        if (paths.Length == 0) throw new ArgumentException("Select files first.");
        return _git.RunAsync(_root, ["add", "-A", "--pathspec-from-file=-", "--pathspec-file-nul"], ct, PathInput(paths));
    }
    private string SafeFile(string path)
    {
        GitSafety.RelativePath(path); var current = _root;
        foreach (var segment in path.Split('/'))
        {
            current = Path.Combine(current, segment);
            if (new FileInfo(current).LinkTarget is not null || new DirectoryInfo(current).LinkTarget is not null)
                throw new IOException("Symbolic links and reparse points are not editable in the text editor.");
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Symbolic links and reparse points are not editable in the text editor.");
        }
        return current;
    }
    private async Task<string> ReadWorktree(string path, CancellationToken ct)
    {
        var full = SafeFile(path);
        if (!File.Exists(full)) return "";
        if (new FileInfo(full).Length > GitSafety.MaximumTextBytes) throw new InvalidDataException("File exceeds the 2 MiB text preview limit.");
        var text = new UTF8Encoding(false, true).GetString(await File.ReadAllBytesAsync(full, ct).ConfigureAwait(false)); GitSafety.Text(text); return text;
    }
    private async Task<string> Blob(string revision, string path, CancellationToken ct)
    {
        var exists = await _git.RunAsync(_root, ["cat-file", "-e", revision + ":" + path], ct, allowFailure: true).ConfigureAwait(false);
        if (exists.ExitCode != 0) return "";
        var size = await Run(ct, "cat-file", "-s", revision + ":" + path).ConfigureAwait(false);
        if (!long.TryParse(size.Output.Trim(), out var length) || length > GitSafety.MaximumTextBytes) throw new InvalidDataException("Blob exceeds the 2 MiB text preview limit.");
        return (await Run(ct, "show", revision + ":" + path).ConfigureAwait(false)).Output;
    }
    private async Task<GitResult> Diff(GitRequest request, CancellationToken ct)
    {
        var path = GitSafety.RelativePath(request.Path);
        try
        {
            string before, after;
            if (request.Value.Length == 0) { before = await Blob("HEAD", path, ct).ConfigureAwait(false); after = await ReadWorktree(path, ct).ConfigureAwait(false); }
            else
            {
                var id = GitSafety.CommitId(request.Value); var parent = await Optional(ct, "rev-parse", id + "^1").ConfigureAwait(false);
                before = parent.Length == 0 ? "" : await Blob(parent, path, ct).ConfigureAwait(false); after = await Blob(id, path, ct).ConfigureAwait(false);
            }
            return new() { Before = before, After = after, Binary = before.Contains('\0') || after.Contains('\0') };
        }
        catch (Exception e) when (e is InvalidDataException or DecoderFallbackException) { return new() { Binary = true, Text = e.Message }; }
    }
    private async Task<GitChange[]> CommitFiles(string id, CancellationToken ct)
    {
        var output = await Run(ct, "diff-tree", "--root", "--no-commit-id", "-r", "--name-status", "-z", id).ConfigureAwait(false);
        var tokens = output.Output.Split('\0'); var result = new List<GitChange>();
        for (var i = 0; i + 1 < tokens.Length; i += 2) if (tokens[i].Length != 0) result.Add(new(tokens[i + 1], tokens[i][..1]));
        return result.ToArray();
    }
    private async Task<string> Operation(CancellationToken ct)
    {
        var directory = (await Run(ct, "rev-parse", "--absolute-git-dir").ConfigureAwait(false)).Output.Trim();
        foreach (var (file, operation) in new[] { ("rebase-merge", "rebase"), ("rebase-apply", "rebase"), ("MERGE_HEAD", "merge"), ("CHERRY_PICK_HEAD", "cherry-pick"), ("REVERT_HEAD", "revert") })
        {
            var path = Path.Combine(directory, file);
            if (File.Exists(path) || Directory.Exists(path)) return operation;
        }
        return "";
    }
    private string _historyKey = "";
    private GitCommit[] _historyCache = [];
    private async Task<GitSnapshot> Snapshot(CancellationToken ct)
    {
        var status = Run(ct, "status", "--porcelain=v1", "-z", "--untracked-files=all");
        var headId = await Optional(ct, "rev-parse", "--verify", "HEAD").ConfigureAwait(false);
        var historyKey = _root + "|" + headId;
        var branch = Optional(ct, "symbolic-ref", "--short", "HEAD");
        var log = historyKey == _historyKey ? Task.FromResult("") : Optional(ct, "log", "-z", "-n", "200", "--format=%H%x00%an%x00%ae%x00%aI%x00%P%x00%B");
        var branches = Optional(ct, "branch", "--format=%(refname:short)");
        var tags = Optional(ct, "tag", "--list");
        var stashes = Optional(ct, "stash", "list", "--format=%gd%x09%gs");
        var remoteNames = Optional(ct, "remote");
        var counts = Optional(ct, "rev-list", "--left-right", "--count", "HEAD...@{upstream}");
        await Task.WhenAll(status, branch, log, branches, tags, stashes, remoteNames, counts).ConfigureAwait(false);
        if (_historyKey != historyKey) { _historyCache = StatusParser.Log(await log.ConfigureAwait(false)); _historyKey = historyKey; }
        var remotes = new List<GitRemote>();
        foreach (var name in Split(await remoteNames.ConfigureAwait(false))) remotes.Add(new(name, await Optional(ct, "remote", "get-url", name).ConfigureAwait(false)));
        var countValues = (await counts.ConfigureAwait(false)).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return new()
        {
            Root = _root, Name = Path.GetFileName(_root), Head = headId, Branch = (await branch.ConfigureAwait(false)) is { Length: > 0 } b ? b : "Detached HEAD",
            Changes = StatusParser.Parse((await status.ConfigureAwait(false)).Output), Commits = _historyCache,
            Branches = Split(await branches.ConfigureAwait(false)), Tags = Split(await tags.ConfigureAwait(false)), Remotes = remotes.ToArray(),
            Stashes = Split(await stashes.ConfigureAwait(false)).Select(line => { var i = line.IndexOf('\t'); return new GitStash(i < 0 ? line : line[..i], i < 0 ? "" : line[(i + 1)..]); }).ToArray(),
            Ahead = countValues.Length == 2 && int.TryParse(countValues[0], out var a) ? a : 0,
            Behind = countValues.Length == 2 && int.TryParse(countValues[1], out var d) ? d : 0,
            Operation = await Operation(ct).ConfigureAwait(false)
        };
    }
    private static string[] Split(string text) => text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    private async Task Demo(CancellationToken ct)
    {
        _root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GitSpace", "Tutorial");
        if (Directory.Exists(Path.Combine(_root, ".git"))) { _root = RepositoryPath.CanonicalDirectory(_root); return; }
        if (Directory.Exists(_root) && Directory.EnumerateFileSystemEntries(_root).Any()) throw new IOException("Tutorial folder exists but is not a Git repository. Choose a different repository.");
        Directory.CreateDirectory(_root); _root = RepositoryPath.CanonicalDirectory(_root); await Run(ct, "init", "--initial-branch=main").ConfigureAwait(false);
        await Run(ct, "config", "user.name", "GitSpace Team").ConfigureAwait(false); await Run(ct, "config", "user.email", "team@gitspace.example").ConfigureAwait(false);
        foreach (var (path, text, message) in Tutorial.Files)
        {
            var full = SafeFile(path); Directory.CreateDirectory(Path.GetDirectoryName(full)!); await File.WriteAllTextAsync(full, text, ct).ConfigureAwait(false);
            await Stage([path], ct).ConfigureAwait(false); await Run(ct, "commit", "-m", message).ConfigureAwait(false);
        }
        await Run(ct, "branch", "feature/command-palette").ConfigureAwait(false);
        foreach (var (path, text) in Tutorial.Changes)
        {
            var full = SafeFile(path); Directory.CreateDirectory(Path.GetDirectoryName(full)!); await File.WriteAllTextAsync(full, text, ct).ConfigureAwait(false);
        }
    }
}
