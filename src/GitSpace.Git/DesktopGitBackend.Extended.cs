using GitSpace.Core;

namespace GitSpace.Git;

public sealed partial class DesktopGitBackend
{
    private Task<GitProcessResult> RunAs(GitRequest r, CancellationToken ct, params string[] args)
    {
        if (string.IsNullOrWhiteSpace(r.Author) || string.IsNullOrWhiteSpace(r.Email) || (r.Author + r.Email).IndexOfAny(['\r', '\n', '<', '>']) >= 0)
            throw new ArgumentException("A valid author name and email are required.");
        return _git.RunAsync(_root, new[] { "-c", "user.name=" + r.Author, "-c", "user.email=" + r.Email }.Concat(args), ct);
    }
    private async Task<GitResult?> ExecuteExtended(GitRequest r, CancellationToken ct)
    {
        switch (r.Operation)
        {
            case "indexDiff": return await IndexDiff(r.Path, ct).ConfigureAwait(false);
            case "stageContent": await StageContent(r, ct).ConfigureAwait(false); break;
            case "commitStaged":
                if (string.IsNullOrWhiteSpace(r.Message)) throw new ArgumentException("A commit summary is required.");
                if (await Operation(ct).ConfigureAwait(false) != "") throw new InvalidOperationException("Continue the active operation instead of making another commit.");
                await RunAs(r, ct, "commit", "-m", GitText.ToLf(r.Message)).ConfigureAwait(false); break;
            case "undoCommit":
                GitSafety.Confirm(r);
                if (await Operation(ct).ConfigureAwait(false) != "") throw new InvalidOperationException("Finish the current operation first.");
                var id = await Optional(ct, "rev-parse", "--verify", "HEAD").ConfigureAwait(false);
                var parents = (await Run(ct, "rev-list", "--parents", "-n", "1", "HEAD").ConfigureAwait(false)).Output.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                if (parents.Length != 2) throw new InvalidOperationException("Undo supports a non-root, non-merge latest commit. Revert other commits instead.");
                if ((await Run(ct, "for-each-ref", "--contains=" + id, "--format=%(refname)", "refs/remotes/").ConfigureAwait(false)).Output.Trim().Length != 0)
                    throw new InvalidOperationException("This commit is on a known remote branch. Revert it rather than rewriting shared history.");
                await Run(ct, "update-ref", "refs/gitspace/undo", id).ConfigureAwait(false);
                await Run(ct, "reset", "--soft", parents[1]).ConfigureAwait(false); break;
            case "history":
                if (r.Skip < 0 || r.Limit is < 1 or > 500) throw new ArgumentOutOfRangeException(nameof(r), "History pages contain 1–500 commits.");
                return new() { Commits = StatusParser.Log((await Run(ct, "log", "-z", "--skip=" + r.Skip, "-n", r.Limit.ToString(), "--format=%H%x00%an%x00%ae%x00%aI%x00%P%x00%B", GitSafety.CommitId(r.Value)).ConfigureAwait(false)).Output) };
            case "conflict": return await Conflict(r.Path, ct).ConfigureAwait(false);
            case "resolveConflict": await ResolveConflict(r, ct).ConfigureAwait(false); break;
            case "worktrees": return new() { Text = (await Run(ct, "worktree", "list", "--porcelain").ConfigureAwait(false)).Output };
            case "addWorktree":
                var destination = Path.GetFullPath(r.Path);
                if (Directory.Exists(destination) && Directory.EnumerateFileSystemEntries(destination).Any()) throw new IOException("Choose an empty or new worktree directory.");
                await Run(ct, "worktree", "add", "--", destination, GitSafety.Ref(r.Value)).ConfigureAwait(false); break;
            case "removeWorktree":
                GitSafety.Confirm(r);
                if (RepositoryPath.SameDirectory(r.Path, _root)) throw new InvalidOperationException("The open worktree cannot remove itself.");
                await Run(ct, "worktree", "remove", "--", Path.GetFullPath(r.Path)).ConfigureAwait(false); break;
            case "submodules": return new() { Text = (await Run(ct, "submodule", "status", "--recursive").ConfigureAwait(false)).Output };
            case "updateSubmodules":
                GitSafety.Confirm(r); await Run(ct, "-c", "protocol.file.allow=never", "submodule", "update", "--init", "--recursive").ConfigureAwait(false); break;
            case "addSubmodule":
                GitSafety.Confirm(r); await Run(ct, "-c", "protocol.file.allow=never", "submodule", "add", "--", GitSafety.HttpsRemote(r.Value), GitSafety.RelativePath(r.Path)).ConfigureAwait(false); break;
            case "lfsStatus": return new() { Text = (await Run(ct, "lfs", "status").ConfigureAwait(false)).Output };
            case "lfsPull": GitSafety.Confirm(r); await Run(ct, "lfs", "pull").ConfigureAwait(false); break;
            case "remoteUrl": await Run(ct, "remote", "set-url", GitSafety.Ref(r.Path), GitSafety.HttpsRemote(r.Value)).ConfigureAwait(false); break;
            case "removeRemote": GitSafety.Confirm(r); await Run(ct, "remote", "remove", GitSafety.Ref(r.Value)).ConfigureAwait(false); break;
            case "pushTag": await Run(ct, "push", "origin", "refs/tags/" + GitSafety.Ref(r.Value)).ConfigureAwait(false); break;
            case "reflog": return new() { Text = (await Run(ct, "reflog", "-n", "100", "--date=iso").ConfigureAwait(false)).Output };
            default: return null;
        }
        return new() { Snapshot = await Snapshot(ct).ConfigureAwait(false) };
    }
}
