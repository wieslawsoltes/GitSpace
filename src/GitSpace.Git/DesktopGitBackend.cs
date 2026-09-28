using System.Text;
using GitSpace.Core;

namespace GitSpace.Git;

public sealed class DesktopGitBackend : IGitBackend
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
            if (string.IsNullOrEmpty(_root)) throw new InvalidOperationException("Open a repository first.");
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
                case "commit": case "amend":
                    if (string.IsNullOrWhiteSpace(request.Message)) throw new ArgumentException("A commit summary is required.");
                    if (op == "amend") GitSafety.Confirm(request);
                    if (request.Paths.Length == 0 && op != "amend") throw new ArgumentException("Select at least one changed file.");
                    if (request.Author.IndexOfAny(['\n', '\r', '<', '>']) >= 0 || request.Email.IndexOfAny(['\n', '\r', '<', '>']) >= 0) throw new ArgumentException("Invalid author identity.");
                    if (await Operation(cancellation).ConfigureAwait(false) != "") throw new InvalidOperationException("Finish or abort the active Git operation before a selected-file commit.");
                    if (request.Paths.Length != 0) await Stage(request.Paths, cancellation).ConfigureAwait(false);
                    var args = new List<string> { "-c", "user.name=" + request.Author, "-c", "user.email=" + request.Email, "commit", "--only", "-m", request.Message };
                    if (op == "amend") args.Add("--amend");
                    if (request.Paths.Length != 0) { args.Add("--pathspec-from-file=-"); args.Add("--pathspec-file-nul"); }
                    await _git.RunAsync(_root, args, cancellation, request.Paths.Length == 0 ? null : PathInput(request.Paths)).ConfigureAwait(false); break;
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
        var text = await File.ReadAllTextAsync(full, new UTF8Encoding(false, true), ct).ConfigureAwait(false); GitSafety.Text(text); return text;
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
            if (request.Value.Length == 0)
            {
                before = await Blob("HEAD", path, ct).ConfigureAwait(false); after = await ReadWorktree(path, ct).ConfigureAwait(false);
            }
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
        foreach (var (file, operation) in new[] { ("rebase-merge", "rebase"), ("rebase-apply", "rebase"), ("MERGE_HEAD", "merge"), ("CHERRY_PICK_HEAD", "cherry-pick"), ("REVERT_HEAD", "revert") })
        {
            var path = await Optional(ct, "rev-parse", "--git-path", file).ConfigureAwait(false);
            if (path.Length != 0 && (File.Exists(Path.GetFullPath(path, _root)) || Directory.Exists(Path.GetFullPath(path, _root)))) return operation;
        }
        return "";
    }
    private async Task<GitSnapshot> Snapshot(CancellationToken ct)
    {
        var status = Run(ct, "status", "--porcelain=v1", "-z", "--untracked-files=all");
        var head = Optional(ct, "rev-parse", "--verify", "HEAD");
        var branch = Optional(ct, "symbolic-ref", "--short", "HEAD");
        var log = Optional(ct, "log", "-z", "-n", "200", "--format=%H%x00%an%x00%ae%x00%aI%x00%P%x00%B");
        var branches = Optional(ct, "branch", "--format=%(refname:short)");
        var tags = Optional(ct, "tag", "--list");
        var stashes = Optional(ct, "stash", "list", "--format=%gd%x09%gs");
        var remoteNames = Optional(ct, "remote");
        var counts = Optional(ct, "rev-list", "--left-right", "--count", "HEAD...@{upstream}");
        await Task.WhenAll(status, head, branch, log, branches, tags, stashes, remoteNames, counts).ConfigureAwait(false);
        var remotes = new List<GitRemote>();
        foreach (var name in Split(await remoteNames.ConfigureAwait(false))) remotes.Add(new(name, await Optional(ct, "remote", "get-url", name).ConfigureAwait(false)));
        var countValues = (await counts.ConfigureAwait(false)).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return new()
        {
            Root = _root, Name = Path.GetFileName(_root), Head = await head.ConfigureAwait(false), Branch = (await branch.ConfigureAwait(false)) is { Length: > 0 } b ? b : "Detached HEAD",
            Changes = StatusParser.Parse((await status.ConfigureAwait(false)).Output), Commits = StatusParser.Log(await log.ConfigureAwait(false)),
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
    public ValueTask DisposeAsync() { _git.Dispose(); _gate.Dispose(); return ValueTask.CompletedTask; }
