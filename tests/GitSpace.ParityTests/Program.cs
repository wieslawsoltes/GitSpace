using GitSpace.Core;
using GitSpace.Diff;
using GitSpace.Git;

var result = await ParityTests.RunAsync();
Console.WriteLine($"PARITY: {result.Passed} passed, {result.Failed} failed");
Environment.ExitCode = result.Failed == 0 ? 0 : 1;

internal static class ParityTests
{
    public static async Task<(int Passed, int Failed)> RunAsync()
    {
        var passed = 0; var failed = 0;
        void Equal<T>(T a, T b) { if (!EqualityComparer<T>.Default.Equals(a, b)) throw new Exception($"Expected [{a}], got [{b}]"); }
        async Task Test(string name, Func<Task> run) { try { await run(); passed++; Console.WriteLine("PASS " + name); } catch (Exception e) { failed++; Console.Error.WriteLine("FAIL " + name + "\n" + e); } }
        Task Sync(Action run) { run(); return Task.CompletedTask; }
        async Task Reject(Func<Task> run) { try { await run(); } catch (Exception e) when (e is InvalidOperationException or IOException or ArgumentException) { return; } throw new Exception("Unsafe operation accepted"); }
        await Test("Git messages normalize all newline conventions", () => Sync(() => Equal("a\nb\nc\n", GitText.ToLf("a\rb\r\nc\n"))));
        await Test("new files use LF", () => Sync(() => Equal("a\nb\n", GitText.FromEditor("a\rb\r", ""))));
        await Test("existing CRLF files keep CRLF", () => Sync(() => Equal("a\r\nc\r\n", GitText.FromEditor("a\rc\r", "a\r\nb\r\n"))));
        await Test("unchanged mixed newlines are lossless", () => Sync(() => Equal("a\r\nb\nc\r", GitText.FromEditor("a\rb\rc\r", "a\r\nb\nc\r"))));
        await Test("CR-only lines render as separate rows", () => Sync(() => Equal(2, DiffEngine.Compare("", "a\rb\r").Lines.Count)));
        await Test("partial replacement applies only selected edits", () => Sync(() =>
        {
            var before = "one\ntwo\nthree\n"; var after = "ONE\ntwo\nTHREE\n";
            var d = DiffEngine.Compare(before, after); var first = PartialDiff.Hunks(d)[0];
            Equal("ONE\ntwo\nthree\n", PartialDiff.Build(before, after, Enumerable.Range(first.FirstRow, first.LastRow - first.FirstRow + 1).ToHashSet()));
        }));
        await Test("partial additions retain interior line separators", () => Sync(() => Equal("first\nsecond\n", PartialDiff.Build("first", "first\nsecond\n", new HashSet<int> { 1 }))));
        await Test("partial selection preserves CRLF and EOF", () => Sync(() =>
        {
            var a = "a\r\nb"; var b = "A\r\nb"; var d = DiffEngine.Compare(a, b);
            Equal(b, PartialDiff.Build(a, b, Enumerable.Range(0, d.Lines.Count).Where(i => d.Lines[i].Kind != DiffKind.Context).ToHashSet()));
        }));
        await Test("500 partial selection reconstruction trials", () => Sync(() =>
        {
            var random = new Random(9827);
            for (var trial = 0; trial < 500; trial++)
            {
                string Text() => string.Join('\n', Enumerable.Range(0, random.Next(1, 12)).Select(_ => random.Next(5).ToString())) + (random.Next(2) == 0 ? "\n" : "");
                var a = Text(); var b = Text(); var d = DiffEngine.Compare(a, b);
                Equal(a, PartialDiff.Build(a, b, new HashSet<int>()));
                Equal(b, PartialDiff.Build(a, b, Enumerable.Range(0, d.Lines.Count).Where(i => d.Lines[i].Kind != DiffKind.Context).ToHashSet(), true));
            }
        }));
        var marked = "prefix\r\n<<<<<<< HEAD\r\nours\r\n||||||| base\r\nold\r\n=======\r\ntheirs\r\n>>>>>>> feature\r\nsuffix";
        await Test("diff3 conflict parsing preserves context and base", () => Sync(() =>
        {
            var doc = ConflictDocument.Parse(marked); Equal(1, doc.Count); Equal("old\r\n", doc.Sections.Single(s => s.IsConflict).Base);
            Equal("prefix\r\nours\r\nsuffix", doc.Resolve(new Dictionary<int, ConflictChoice> { [0] = ConflictChoice.Ours }));
            Equal("prefix\r\ntheirs\r\nsuffix", doc.Resolve(new Dictionary<int, ConflictChoice> { [0] = ConflictChoice.Theirs }));
        }));
        await Test("unresolved and nested conflicts are rejected", async () =>
        {
            await Reject(() => Sync(() => ConflictDocument.Parse(marked).Resolve(new Dictionary<int, ConflictChoice>())));
            await Reject(() => Sync(() => ConflictDocument.Parse("<<<<<<< HEAD\n<<<<<<< nested\n")));
        });
        await Test("conflict marker detection includes CRLF delimiters", () => Sync(() => Equal(true, ConflictDocument.HasMarkers("a\r\n=======\r\nb"))));
        var root = Path.Combine(Path.GetTempPath(), "gitspace-parity-" + Guid.NewGuid().ToString("N"));
        await using var backend = new DesktopGitBackend(); using var git = new GitProcess();
        GitSnapshot snapshot = new();
        async Task<GitResult> Do(GitRequest request)
        {
            var result = await backend.ExecuteAsync(request with { Author = "Parity Test", Email = "parity@example.com" });
            if (result.Snapshot is not null) snapshot = result.Snapshot;
            return result;
        }
        try
        {
            await Do(new("init") { Root = root }); await git.RunAsync(root, ["config", "core.autocrlf", "false"]);
            await Do(new("write") { Path = "file.txt", Message = "one\ntwo\nthree\n" });
            await Do(new("commit") { Paths = ["file.txt"], Message = "Initial" });
            await Test("native partial stage does not alter the worktree", async () =>
            {
                const string working = "ONE\ntwo\nTHREE\n";
                await Do(new("write") { Path = "file.txt", Message = working });
                var diff = await Do(new("indexDiff") { Path = "file.txt" });
                await Do(new("stageContent") { Path = "file.txt", Message = "ONE\ntwo\nthree\n", Value = diff.IndexId, ExpectedFileId = diff.FileId });
                Equal(working, await File.ReadAllTextAsync(Path.Combine(root, "file.txt")));
                Equal("ONE\ntwo\nthree\n", (await git.RunAsync(root, ["show", ":file.txt"])).Output);
            });
            await Test("native stale partial stage is rejected", async () =>
            {
                var diff = await Do(new("indexDiff") { Path = "file.txt" });
                await Do(new("write") { Path = "file.txt", Message = "changed externally\n" });
                await Reject(() => Do(new("stageContent") { Path = "file.txt", Message = "wrong", Value = diff.IndexId, ExpectedFileId = diff.FileId }));
                Equal(false, File.Exists(Path.Combine(root, ".git/index.lock")));
            });
            await Test("native staged commit leaves unstaged content intact", async () =>
            {
                await Do(new("commitStaged") { Message = "Partial" });
                Equal("ONE\ntwo\nthree\n", (await git.RunAsync(root, ["show", "HEAD:file.txt"])).Output);
                Equal("changed externally\n", await File.ReadAllTextAsync(Path.Combine(root, "file.txt")));
            });
            await Test("native undo preserves worktree and a recovery ref", async () =>
            {
                var old = snapshot.Head; await Do(new("undoCommit") { Confirm = true, ExpectedHead = old });
                Equal(old, (await git.RunAsync(root, ["rev-parse", "refs/gitspace/undo"])).Output.Trim());
                Equal("changed externally\n", await File.ReadAllTextAsync(Path.Combine(root, "file.txt")));
                Equal(1, snapshot.Commits.Length);
                await Do(new("commit") { Paths = ["file.txt"], Message = "Second" });
            });
            await Test("native anchored history pages", async () =>
            {
                var page = await Do(new("history") { Value = snapshot.Head, Skip = 1, Limit = 1 });
                Equal(1, page.Commits.Length); Equal("Initial", page.Commits[0].Summary);
            });
            await Test("native index lock belonging to another Git is untouched", async () =>
            {
                var diff = await Do(new("indexDiff") { Path = "file.txt" }); var lockPath = Path.Combine(root, ".git/index.lock");
                await File.WriteAllTextAsync(lockPath, "owned externally");
                try { await Reject(() => Do(new("stageContent") { Path = "file.txt", Message = "wrong", Value = diff.IndexId, ExpectedFileId = diff.FileId })); Equal("owned externally", await File.ReadAllTextAsync(lockPath)); }
                finally { File.Delete(lockPath); }
            });
            await Test("native real merge conflict resolution and continuation", async () =>
            {
                await Do(new("branch") { Value = "feature/conflict" });
                await Do(new("write") { Path = "file.txt", Message = "feature side\n" });
                await Do(new("commit") { Paths = ["file.txt"], Message = "Feature" });
                await Do(new("checkout") { Value = "main" });
                await Do(new("write") { Path = "file.txt", Message = "main side\n" });
                await Do(new("commit") { Paths = ["file.txt"], Message = "Main" });
                await Reject(() => Do(new("merge") { Value = "feature/conflict", Confirm = true }));
                var conflict = await Do(new("conflict") { Path = "file.txt" });
                Equal("main side\n", conflict.Before); Equal("feature side\n", conflict.After);
                var resolved = ConflictDocument.Parse(conflict.Text).Resolve(new Dictionary<int, ConflictChoice> { [0] = ConflictChoice.Both });
                await Do(new("resolveConflict") { Path = "file.txt", Message = resolved, Value = conflict.IndexId, ExpectedFileId = conflict.FileId, Confirm = true });
                await Do(new("continue") { Confirm = true });
                Equal("", snapshot.Operation); Equal(2, snapshot.Commits[0].Parents.Length);
                Equal("main side\nfeature side\n", await File.ReadAllTextAsync(Path.Combine(root, "file.txt")));
            });
            await Test("native worktree add list and safe remove", async () =>
            {
                await git.RunAsync(root, ["branch", "worktree-test"]); var destination = root + "-worktree";
                await Do(new("addWorktree") { Path = destination, Value = "worktree-test" });
                Equal(true, (await Do(new("worktrees"))).Text.Contains("worktree-test", StringComparison.Ordinal));
                await Do(new("removeWorktree") { Path = destination, Confirm = true }); Equal(false, Directory.Exists(destination));
            });
        }
        finally { try { Directory.Delete(root, true); } catch { } }
        return (passed, failed);
    }
}
