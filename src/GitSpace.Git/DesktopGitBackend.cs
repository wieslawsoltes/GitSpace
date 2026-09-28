using System.Text;
using GitSpace.Core;

namespace GitSpace.Git;

public sealed partial class DesktopGitBackend : IGitBackend
{
    private readonly GitProcess _git = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private string _root = "";
    public string DisplayName => "System Git · desktop";
    public IReadOnlySet<string> Capabilities { get; } = new HashSet<string>(StringComparer.Ordinal)
    { "demo", "open", "init", "clone", "refresh", "diff", "commitFiles", "read", "write", "stage", "unstage", "commit", "amend", "discard", "branch", "checkout", "renameBranch", "deleteBranch", "fetch", "pull", "push", "remote", "stash", "stashApply", "stashDrop", "merge", "rebase", "continue", "abort", "revert", "cherryPick", "tag", "deleteTag" };
    private Task<GitProcessResult> Run(CancellationToken ct, params string[] args) => _git.RunAsync(_root, args, ct);
    private async Task<string> Optional(CancellationToken ct, params string[] args)
    {
        var result = await _git.RunAsync(_root, args, ct, allowFailure: true).ConfigureAwait(false);
        return result.ExitCode == 0 ? result.Output.TrimEnd('\r', '\n') : "";
    }
    public async Task<GitResult> ExecuteAsync(GitRequest request, CancellationToken cancellation = default)
    {
        if (!Capabilities.Contains(request.Operation)) throw new NotSupportedException("Unsupported desktop operation: " + request.Operation);
        await _gate.WaitAsync(cancellation).ConfigureAwait(false);
        try
        {
            var op = request.Operation;
            if (op == "demo") { await Demo(cancellation).ConfigureAwait(false); return new() { Snapshot = await Snapshot(cancellation).ConfigureAwait(false) }; }
            if (op is "open" or "init" or "clone")
            {
                var target = Path.GetFullPath(request.Root);
                if (op != "open")
                {
                    if (Directory.Exists(target) && Directory.EnumerateFileSystemEntries(target).Any()) throw new IOException("Choose a new or empty destination directory.");
                    Directory.CreateDirectory(target);
                    if (op == "init") await _git.RunAsync(target, ["init", "--initial-branch=main"], cancellation).ConfigureAwait(false);
                    else await _git.RunAsync(target, ["clone", "--", GitSafety.HttpsRemote(request.Value), "."], cancellation).ConfigureAwait(false);
                }
                var result = await _git.RunAsync(target, ["rev-parse", "--show-toplevel"], cancellation).ConfigureAwait(false);
                _root = RepositoryPath.CanonicalDirectory(result.Output.Trim());
                return new() { Snapshot = await Snapshot(cancellation).ConfigureAwait(false) };
            }
            if (_root.Length == 0) throw new InvalidOperationException("Open a repository first.");
            if (request.Root.Length != 0 && !RepositoryPath.SameDirectory(request.Root, _root)) throw new InvalidOperationException("Repository changed; refresh before continuing.");
            if (op is not ("refresh" or "diff" or "read" or "commitFiles") && request.ExpectedHead.Length != 0 && request.ExpectedHead != await Optional(cancellation, "rev-parse", "--verify", "HEAD").ConfigureAwait(false))
                throw new InvalidOperationException("HEAD changed outside GitSpace. Refresh and review before retrying.");
            switch (op)
            {
                case "refresh": break;
                case "read": return new() { Text = await ReadWorktree(request.Path, cancellation).ConfigureAwait(false) };
                case "write":
                    GitSafety.Text(request.Message); var full = SafeFile(request.Path); Directory.CreateDirectory(Path.GetDirectoryName(full)!);
                    await File.WriteAllTextAsync(full, request.Message, new UTF8Encoding(false), cancellation).ConfigureAwait(false); break;
                case "diff": return await Diff(request, cancellation).ConfigureAwait(false);
                case "commitFiles": return new() { Changes = await CommitFiles(GitSafety.CommitId(request.Value), cancellation).ConfigureAwait(false) };
                case "stage": await Stage(request.Paths, cancellation).ConfigureAwait(false); break;
                case "unstage":
                    foreach (var path in request.Paths)
                    {
                        GitSafety.RelativePath(path);
                        if ((await Optional(cancellation, "rev-parse", "--verify", "HEAD").ConfigureAwait(false)).Length != 0) await Run(cancellation, "restore", "--staged", "--", path).ConfigureAwait(false);
                        else await Run(cancellation, "rm", "--cached", "--", path).ConfigureAwait(false);
                    }
                    break;
                case "commit": case "amend": await Commit(request, cancellation).ConfigureAwait(false); break;
                case "discard":
                    GitSafety.Confirm(request);
                    foreach (var path in request.Paths)
                    {
                        GitSafety.RelativePath(path); await Run(cancellation, "ls-files", "--error-unmatch", "--", path).ConfigureAwait(false);
                        await Run(cancellation, "restore", "--source=HEAD", "--staged", "--worktree", "--", path).ConfigureAwait(false);
                    }
                    break;
                case "branch": await Run(cancellation, "switch", "-c", GitSafety.Ref(request.Value)).ConfigureAwait(false); break;
                case "checkout": await Run(cancellation, "switch", "--", GitSafety.Ref(request.Value)).ConfigureAwait(false); break;
                case "renameBranch": await Run(cancellation, "branch", "-m", GitSafety.Ref(request.Value)).ConfigureAwait(false); break;
                case "deleteBranch": GitSafety.Confirm(request); await Run(cancellation, "branch", "-d", GitSafety.Ref(request.Value)).ConfigureAwait(false); break;
                case "remote": await Run(cancellation, "remote", "add", GitSafety.Ref(request.Path), GitSafety.HttpsRemote(request.Value)).ConfigureAwait(false); break;
                case "fetch": await Run(cancellation, "fetch", "--prune", request.Value.Length == 0 ? "origin" : GitSafety.Ref(request.Value)).ConfigureAwait(false); break;
                case "pull": await Run(cancellation, "pull", "--ff-only").ConfigureAwait(false); break;
                case "push":
                    var branch = await Optional(cancellation, "symbolic-ref", "--short", "HEAD").ConfigureAwait(false);
                    if (branch.Length == 0) throw new InvalidOperationException("Create a branch before pushing a detached HEAD.");
                    await Run(cancellation, "push", "--set-upstream", "origin", GitSafety.Ref(branch)).ConfigureAwait(false); break;
                case "stash": await Run(cancellation, "stash", "push", "--include-untracked", "-m", request.Message.Length == 0 ? "Saved in GitSpace" : request.Message).ConfigureAwait(false); break;
                case "stashApply": case "stashDrop":
                    if (!System.Text.RegularExpressions.Regex.IsMatch(request.Value, "^stash@\\{[0-9]+\\}$")) throw new ArgumentException("Invalid stash reference.");
                    if (op == "stashDrop") GitSafety.Confirm(request);
                    await Run(cancellation, "stash", op == "stashApply" ? "apply" : "drop", request.Value).ConfigureAwait(false); break;
                case "merge": GitSafety.Confirm(request); await Run(cancellation, "merge", "--no-edit", GitSafety.Ref(request.Value)).ConfigureAwait(false); break;
                case "rebase": GitSafety.Confirm(request); await Run(cancellation, "rebase", GitSafety.Ref(request.Value)).ConfigureAwait(false); break;
                case "revert": case "cherryPick":
                    GitSafety.Confirm(request); await Run(cancellation, op == "revert" ? "revert" : "cherry-pick", "--no-edit", GitSafety.CommitId(request.Value)).ConfigureAwait(false); break;
                case "continue": case "abort":
                    GitSafety.Confirm(request); var operation = await Operation(cancellation).ConfigureAwait(false);
                    if (operation.Length == 0) throw new InvalidOperationException("No merge, rebase, cherry-pick or revert is in progress.");
                    await Run(cancellation, operation, "--" + op).ConfigureAwait(false); break;
                case "tag": await Run(cancellation, "tag", GitSafety.Ref(request.Value)).ConfigureAwait(false); break;
                case "deleteTag": GitSafety.Confirm(request); await Run(cancellation, "tag", "-d", GitSafety.Ref(request.Value)).ConfigureAwait(false); break;
            }
            return new() { Snapshot = await Snapshot(cancellation).ConfigureAwait(false) };
        }
        finally { _gate.Release(); }
    }
    private async Task Commit(GitRequest request, CancellationToken ct)
    {
        var amend = request.Operation == "amend";
        if (string.IsNullOrWhiteSpace(request.Message)) throw new ArgumentException("A commit summary is required.");
        if (amend) GitSafety.Confirm(request);
        if (request.Paths.Length == 0 && !amend) throw new ArgumentException("Select at least one changed file.");
        if (request.Author.IndexOfAny(['\n', '\r', '<', '>']) >= 0 || request.Email.IndexOfAny(['\n', '\r', '<', '>']) >= 0) throw new ArgumentException("Invalid author identity.");
        if (await Operation(ct).ConfigureAwait(false) != "") throw new InvalidOperationException("Finish or abort the active Git operation before a selected-file commit.");
        if (request.Paths.Length != 0) await Stage(request.Paths, ct).ConfigureAwait(false);
        var args = new List<string> { "-c", "user.name=" + request.Author, "-c", "user.email=" + request.Email, "commit", "--only", "-m", request.Message };
        if (amend) args.Add("--amend");
        if (request.Paths.Length != 0) { args.Add("--pathspec-from-file=-"); args.Add("--pathspec-file-nul"); }
        await _git.RunAsync(_root, args, ct, request.Paths.Length == 0 ? null : PathInput(request.Paths)).ConfigureAwait(false);
    }
    public ValueTask DisposeAsync() { _git.Dispose(); _gate.Dispose(); return ValueTask.CompletedTask; }
}
