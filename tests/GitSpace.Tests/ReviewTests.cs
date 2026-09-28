using GitSpace.Core;
using GitSpace.Diff;
using GitSpace.Git;

internal static class ReviewTests
{
    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}, got {actual}");
    }
    private static async Task Reject(Func<Task> action)
    {
        try { await action(); } catch (Exception e) when (e is InvalidOperationException or IOException or ArgumentException or GitCommandException) { return; }
        throw new Exception("An unsafe operation was accepted.");
    }
    internal static async Task Run(Func<string, Func<Task>, Task> test)
    {
        await test("editor newline and BOM preservation", () =>
        {
            Equal("a\nb\n", GitText.FromEditor("a\rb\r", ""));
            Equal("a\r\nc\r\n", GitText.FromEditor("a\rc\r", "a\r\nb\r\n"));
            Equal("a\r\nb\nc\r", GitText.FromEditor("a\rb\rc\r", "a\r\nb\nc\r"));
            Equal("\uFEFFa\nc\n", GitText.FromEditor("a\rc\r", "\uFEFFa\nb\n"));
            Equal(2, DiffEngine.Compare("", "a\rb\r").Lines.Count); return Task.CompletedTask;
        });
        await test("exact line selection preserves excluded lines and endings", () =>
        {
            var selection = new LineSelection("a\r\nb\r\nc\r\n", "A\r\nb\r\nC\r\n");
            var rows = selection.HunkAt(0);
            Equal("A\r\nb\r\nc\r\n", selection.Apply(rows));
            Equal("a\r\nb\r\nC\r\n", selection.Apply(rows, true));
            return Task.CompletedTask;
        });
        await test("partial EOF changes reject accidental line concatenation", async () =>
        {
            var selection = new LineSelection("a", "a\nb\n");
            var index = selection.Document.Lines.Select((l, i) => (l, i)).First(x => x.l.Text == "b").i;
            await Reject(() => Task.FromResult(selection.Apply([index])));
        });
        await test("1000 exact-selection reconstruction trials", () =>
        {
            var random = new Random(6417);
            for (var n = 0; n < 1000; n++)
            {
                string Make() => string.Concat(Enumerable.Range(0, random.Next(0, 20)).Select(_ => random.Next(5) + (random.Next(2) == 0 ? "\r\n" : "\n")));
                var before = Make(); var after = Make(); var selection = new LineSelection(before, after);
                var rows = selection.Document.Lines.Select((l, i) => (l, i)).Where(x => x.l.Kind != DiffKind.Context).Select(x => x.i).ToArray();
                if (rows.Length == 0) continue;
                Equal(after, selection.Apply(rows)); Equal(before, selection.Apply(rows, true));
            }
            return Task.CompletedTask;
        });
        await test("diff3 conflict choices preserve unaffected lines", () =>
        {
            var doc = new ConflictDocument("first\r\n<<<<<<< HEAD\r\nours\r\n||||||| base\r\nbase\r\n=======\r\ntheirs\r\n>>>>>>> feature\r\nlast\r\n");
            Equal(1, doc.Blocks.Count); Equal("base\r\n", doc.Blocks[0].Base);
            Equal("first\r\nours\r\nlast\r\n", doc.Resolve([ConflictChoice.Current]));
            Equal("first\r\nours\r\ntheirs\r\nlast\r\n", doc.Resolve([ConflictChoice.Both])); return Task.CompletedTask;
        });
        await test("viewport handles infinity and huge scroll values", () =>
        {
            Equal((100, 100), DiffEngine.VisibleRange(100, double.PositiveInfinity, 100, 20));
            Equal((0, 100), DiffEngine.VisibleRange(100, double.NaN, double.PositiveInfinity, 20)); return Task.CompletedTask;
        });
        var root = Path.Combine(Path.GetTempPath(), "gitspace-review-" + Guid.NewGuid().ToString("N"));
        await using var backend = new DesktopGitBackend(); using var process = new GitProcess();
        GitSnapshot state = new();
        async Task<GitResult> Run(GitRequest request)
        {
            var result = await backend.ExecuteAsync(request with { Root = root, Author = "Review Test", Email = "review@example.com" });
            if (result.Snapshot is not null) state = result.Snapshot;
            return result;
        }
        try
        {
            await Run(new("init")); await process.RunAsync(root, ["config", "core.autocrlf", "false"]);
            await Run(new("write") { Path = "a.txt", Message = "\uFEFFone\r\ntwo\r\n" });
            await Run(new("commit") { Paths = ["a.txt"], Message = "Initial" });
            await test("native BOM is retained by pipe and filesystem previews", async () =>
            {
                var result = await Run(new("review") { Path = "a.txt" });
                Equal("\uFEFFone\r\ntwo\r\n", result.Before); Equal(result.Before, result.After);
            });
            await test("native partial index update preserves unrelated staged files", async () =>
            {
                await Run(new("write") { Path = "other.txt", Message = "unrelated\n" }); await Run(new("stage") { Paths = ["other.txt"] });
                await Run(new("write") { Path = "a.txt", Message = "\uFEFFONE\r\nTWO\r\n" });
                var viewed = await Run(new("review") { Path = "a.txt" });
                await Run(new("stageText") { Path = "a.txt", BeforeHash = viewed.BeforeHash, AfterHash = viewed.AfterHash, Message = "\uFEFFONE\r\ntwo\r\n" });
                Equal("\uFEFFONE\r\ntwo\r\n", (await Run(new("review") { Path = "a.txt", Value = "staged" })).After);
                Equal("\uFEFFONE\r\nTWO\r\n", (await Run(new("read") { Path = "a.txt" })).Text);
                Equal("unrelated\n", (await Run(new("review") { Path = "other.txt", Value = "staged" })).After);
            });
            await test("native partial staging rejects stale worktree and locks", async () =>
            {
                var viewed = await Run(new("review") { Path = "a.txt" });
                await Run(new("write") { Path = "a.txt", Message = "newer\n" });
                var request = new GitRequest("stageText") { Path = "a.txt", Message = "wrong", BeforeHash = viewed.BeforeHash, AfterHash = viewed.AfterHash };
                await Reject(() => Run(request));
                Equal(false, File.Exists(Path.Combine(root, ".git", "index.lock")));
                await File.WriteAllTextAsync(Path.Combine(root, ".git", "index.lock"), "external lock");
                try { await Reject(() => Run(request)); Equal("external lock", await File.ReadAllTextAsync(Path.Combine(root, ".git", "index.lock"))); }
                finally { File.Delete(Path.Combine(root, ".git", "index.lock")); }
            });
            await test("native staged-only commit leaves excluded working edits", async () =>
            {
                await Run(new("unstage") { Paths = ["other.txt"] });
                await Run(new("commitStaged") { IndexHash = state.IndexHash, Message = "Only staged content" });
                Equal("\uFEFFONE\r\ntwo\r\n", (await Run(new("diff") { Path = "a.txt" })).Before);
                Equal("newer\n", (await Run(new("read") { Path = "a.txt" })).Text);
            });
            await test("native commit guards index changes independently of HEAD", async () =>
            {
                var oldHash = state.IndexHash; await Run(new("stage") { Paths = ["other.txt"] });
                await Reject(() => Run(new("commitStaged") { IndexHash = oldHash, Message = "Should fail" }));
            });
            await test("native refresh caches immutable history; paging uses lookahead", async () =>
            {
                var count = backend.HistoryReadCount; await Run(new("refresh")); await Run(new("refresh")); Equal(count, backend.HistoryReadCount);
                await Run(new("history") { Limit = 1 }); Equal(1, state.Commits.Length); Equal(true, state.HasMoreHistory);
                await Run(new("history") { Limit = 200 }); Equal(2, state.Commits.Length); Equal(false, state.HasMoreHistory);
            });
            await test("native conflict review and confirmed resolution complete a merge", async () =>
            {
                await Run(new("commit") { Paths = ["a.txt", "other.txt"], Message = "Prepare merge" });
                await Run(new("branch") { Value = "conflicting" });
                await Run(new("write") { Path = "a.txt", Message = "incoming\n" }); await Run(new("commit") { Paths = ["a.txt"], Message = "Incoming" });
                await Run(new("checkout") { Value = "main" }); await Run(new("write") { Path = "a.txt", Message = "current\n" });
                await Run(new("commit") { Paths = ["a.txt"], Message = "Current" });
                await Reject(() => Run(new("merge") { Value = "conflicting", Confirm = true }));
                var conflict = await Run(new("conflict") { Path = "a.txt" }); Equal("current\n", conflict.Before); Equal("incoming\n", conflict.After);
                await Run(new("resolveConflict") { Path = "a.txt", BeforeHash = conflict.BeforeHash, AfterHash = conflict.AfterHash, Message = "resolved\n", Confirm = true });
                await Run(new("continue") { Confirm = true }); Equal("", state.Operation); Equal(2, state.Commits[0].Parents.Length);
                Equal(1, (await Run(new("commitFiles") { Value = state.Head })).Changes.Length);
            });
        }
        finally { try { Directory.Delete(root, true); } catch (IOException) { } }
    }
}
