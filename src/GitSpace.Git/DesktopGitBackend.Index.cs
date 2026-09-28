using System.Text;
using GitSpace.Core;

namespace GitSpace.Git;

public sealed partial class DesktopGitBackend
{
    private async Task<(string Oid, string Mode)> IndexEntry(string path, CancellationToken ct)
    {
        var records = (await Run(ct, "ls-files", "--stage", "-z", "--", GitSafety.RelativePath(path)).ConfigureAwait(false)).Output.Split('\0', StringSplitOptions.RemoveEmptyEntries);
        if (records.Length == 0) return ("absent", "100644");
        if (records.Length != 1) throw new InvalidOperationException("Resolve this file's index conflicts before partial staging.");
        var parts = records[0][..records[0].IndexOf('\t')].Split(' ');
        if (parts[2] != "0" || parts[0] is not ("100644" or "100755")) throw new InvalidOperationException("Only ordinary, non-conflicted text files support partial staging.");
        return (parts[1], parts[0]);
    }
    private async Task<GitResult> IndexDiff(string path, CancellationToken ct)
    {
        var entry = await IndexEntry(path, ct).ConfigureAwait(false);
        var after = await ReadWorktree(path, ct).ConfigureAwait(false);
        var before = entry.Oid == "absent" ? "" : await Blob("", path, ct).ConfigureAwait(false);
        GitSafety.Text(before);
        return new() { Before = before, After = after, IndexId = entry.Oid, FileId = File.Exists(SafeFile(path)) ? GitText.Fingerprint(after) : "absent" };
    }
    private async Task StageContent(GitRequest r, CancellationToken ct)
    {
        GitSafety.RelativePath(r.Path); GitSafety.Text(r.Message);
        if (r.Value.Length == 0 || r.ExpectedFileId.Length == 0) throw new ArgumentException("Reload the index diff before staging selected lines.");
        var indexPath = Path.GetFullPath((await Run(ct, "rev-parse", "--git-path", "index").ConfigureAwait(false)).Output.Trim(), _root);
        var lockPath = indexPath + ".lock";
        var temporary = indexPath + ".gitspace-" + Guid.NewGuid().ToString("N");
        var ownsLock = false;
        try
        {
            // Hold Git's actual index lock while checking and replacing the index.
            await using (var locked = new FileStream(lockPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                ownsLock = true;
                var current = await IndexDiff(r.Path, ct).ConfigureAwait(false);
                if (current.IndexId != r.Value || current.FileId != r.ExpectedFileId) throw new InvalidOperationException("The file or index changed. Reload the diff and review your selection.");
                var entry = await IndexEntry(r.Path, ct).ConfigureAwait(false);
                var attributes = (await Run(ct, "check-attr", "-z", "filter", "working-tree-encoding", "--", r.Path).ConfigureAwait(false)).Output.Split('\0');
                for (var i = 2; i < attributes.Length; i += 3)
                    if (attributes[i] is not ("unspecified" or "unset" or "")) throw new InvalidOperationException("Partial staging is disabled for filtered or re-encoded files; use whole-file staging.");
                if (File.Exists(indexPath)) File.Copy(indexPath, temporary);
                var oid = (await _git.RunAsync(_root, ["hash-object", "-w", "--path=" + r.Path, "--stdin"], ct, r.Message).ConfigureAwait(false)).Output.Trim();
                await _git.RunAsync(_root, ["update-index", "--add", "--cacheinfo", entry.Mode, GitSafety.CommitId(oid), r.Path], ct,
                    environment: new Dictionary<string, string> { ["GIT_INDEX_FILE"] = temporary }).ConfigureAwait(false);
                await using var generated = File.OpenRead(temporary);
                await generated.CopyToAsync(locked, ct).ConfigureAwait(false);
                await locked.FlushAsync(ct).ConfigureAwait(false); locked.Flush(true);
            }
            File.Move(lockPath, indexPath, true); ownsLock = false;
        }
        finally
        {
            if (ownsLock) File.Delete(lockPath);
            if (File.Exists(temporary)) File.Delete(temporary);
            if (File.Exists(temporary + ".lock")) File.Delete(temporary + ".lock");
        }
    }
    private async Task<GitResult> Conflict(string path, CancellationToken ct)
    {
        GitSafety.RelativePath(path);
        var stages = (await Run(ct, "ls-files", "--unmerged", "-z", "--", path).ConfigureAwait(false)).Output;
        if (stages.Length == 0) throw new InvalidOperationException("This file has no unresolved index conflict.");
        var modes = stages.Split('\0', StringSplitOptions.RemoveEmptyEntries).Select(s => s.Split(' ')[0]);
        if (modes.Any(m => m is not ("100644" or "100755"))) throw new InvalidOperationException("This conflict requires manual Git resolution.");
        var worktree = await ReadWorktree(path, ct).ConfigureAwait(false);
        return new() { Base = await Blob(":1", path, ct).ConfigureAwait(false), Before = await Blob(":2", path, ct).ConfigureAwait(false),
            After = await Blob(":3", path, ct).ConfigureAwait(false), Text = worktree, FileId = GitText.Fingerprint(worktree), IndexId = GitText.Fingerprint(stages) };
    }
    private async Task ResolveConflict(GitRequest r, CancellationToken ct)
    {
        GitSafety.Confirm(r); GitSafety.Text(r.Message);
        if (ConflictDocument.HasMarkers(r.Message)) throw new InvalidOperationException("Resolve all conflict markers before saving.");
        var current = await Conflict(r.Path, ct).ConfigureAwait(false);
        if (r.Value != current.IndexId || r.ExpectedFileId != current.FileId) throw new InvalidOperationException("The conflict changed. Reopen it before resolving.");
        var backupDir = Path.GetFullPath((await Run(ct, "rev-parse", "--git-path", "gitspace-recovery").ConfigureAwait(false)).Output.Trim(), _root);
        Directory.CreateDirectory(backupDir);
        await File.WriteAllTextAsync(Path.Combine(backupDir, Guid.NewGuid().ToString("N") + ".txt"), current.Text, new UTF8Encoding(false), ct).ConfigureAwait(false);
        await File.WriteAllTextAsync(SafeFile(r.Path), r.Message, new UTF8Encoding(false), ct).ConfigureAwait(false);
        await Stage([r.Path], ct).ConfigureAwait(false);
    }
}
