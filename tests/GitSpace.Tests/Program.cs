using GitSpace.Core;
using GitSpace.Diff;
using GitSpace.Git;

var passed = 0; var failed = 0;
void Check(bool value, string message = "Assertion failed") { if (!value) throw new Exception(message); }
void Equal<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}; actual {actual}"); }
void Reject(Action action) { try { action(); } catch (ArgumentException) { return; } throw new Exception("Expected argument rejection"); }
async Task Test(string name, Func<Task> action)
{
    try { await action(); passed++; Console.WriteLine("PASS " + name); }
    catch (Exception e) { failed++; Console.Error.WriteLine("FAIL " + name + "\n" + e); }
}
Task Sync(Action a) { a(); return Task.CompletedTask; }
await Test("path traversal and metadata are rejected", () => Sync(() =>
{
    foreach (var path in new[] { "", "../file", "a/../file", "/etc/passwd", ".git/config", "a/.GIT/x", "a//b", "C:/x", "a\\b", "a\0b", "a\nb" }) Reject(() => GitSafety.RelativePath(path));
}));
await Test("literal Unicode and leading-dash filenames are valid", () => Sync(() =>
{
    foreach (var path in new[] { "src/a b.cs", "zażółć.txt", "--help.txt", "[literal]*.txt" }) Equal(path, GitSafety.RelativePath(path));
}));
await Test("ref injection is rejected", () => Sync(() =>
{
    foreach (var reference in new[] { "-D", "a..b", "a b", "HEAD~1", "a:foo", "a@{1}", "a.lock", ".hidden", "x//y", "x\ny" }) Reject(() => GitSafety.Ref(reference));
    Equal("feature/ui", GitSafety.Ref("feature/ui"));
}));
await Test("remote credentials and insecure URLs are rejected", () => Sync(() =>
{
    foreach (var url in new[] { "file:///tmp/r", "http://github.com/a/b", "https://token@github.com/a/b", "ssh://git@github.com/a/b" }) Reject(() => GitSafety.HttpsRemote(url));
    Equal("https://github.com/a/b", GitSafety.HttpsRemote("https://github.com/a/b"));
}));
await Test("porcelain NUL rename and newline paths", () => Sync(() =>
{
    var files = StatusParser.Parse(" M file with spaces.txt\0R  new.txt\0old.txt\0?? new\nline.txt\0UU conflict.txt\0");
    Equal(4, files.Length); Equal("old.txt", files[1].OriginalPath); Equal("new.txt", files[1].Path); Check(files[1].Staged); Check(files[3].Conflict); Equal("new\nline.txt", files[2].Path);
}));
await Test("porcelain deletion and staged modification", () => Sync(() =>
{
    var files = StatusParser.Parse(" D deleted\0M  staged\0AM added\0"); Equal("D", files[0].Status); Check(files[1].Staged); Equal("A", files[2].Status);
}));
await Test("commit log preserves body and parents", () => Sync(() =>
{
    var log = StatusParser.Log("abc\0Author\0a@b\02026-09-28\0def ghi\0Summary\n\nBody\n\0");
    Equal(1, log.Length); Equal("Summary", log[0].Summary); Equal(2, log[0].Parents.Length);
}));
await Test("JSON contract round trip", () => Sync(() =>
{
    var r = new GitRequest("commit") { Paths = ["a b", "x"], Message = "quote \" and newline\n", Confirm = true };
    var clone = GitJson.Deserialize<GitRequest>(GitJson.Serialize(r)); Equal(r.Message, clone.Message); Check(clone.Confirm); Equal(2, clone.Paths.Length);
}));
await Test("empty and identical diff", () => Sync(() =>
{
    Equal(0, DiffEngine.Compare("", "").Lines.Count); var d = DiffEngine.Compare("a\nb\n", "a\nb\n"); Equal(2, d.Lines.Count); Equal(0, d.Additions); Equal(0, d.Deletions);
}));
await Test("addition deletion final newline and CRLF", () => Sync(() =>
{
    Equal(2, DiffEngine.Compare("", "a\nb\n").Additions); Equal(2, DiffEngine.Compare("a\nb", "").Deletions);
    var d = DiffEngine.Compare("a\r\nb\r\n", "a\nb"); Check(d.OldHasFinalNewline && !d.NewHasFinalNewline); Equal(0, d.Additions);
}));
await Test("intraline span and split alignment", () => Sync(() =>
{
    Equal((2, 1, 3), DiffEngine.ChangedSpan("abcd", "abXYZd"));
    var d = DiffEngine.Compare("a\nb\nc\n", "a\nx\ny\nc\n"); Equal(4, d.Split().Count); Equal(2, d.Additions); Equal(1, d.Deletions);
}));
await Test("whitespace and visible window", () => Sync(() =>
{
    Equal(0, DiffEngine.Compare("a b\n", "ab\n", true).Additions); Equal((50, 61), DiffEngine.VisibleRange(1_000_000, 1000, 200, 20));
}));
await Test("bounded diff remains reconstructable", () => Sync(() =>
{
    var before = string.Join('\n', Enumerable.Range(0, 4000).Select(i => "old " + i)); var after = before.Replace("old", "new");
    var d = DiffEngine.Compare(before, after, workBudget: 1); Check(d.Coarse); Equal(4000, d.Deletions); Equal(4000, d.Additions);
}));
await Test("1000 deterministic Myers differential trials", () => Sync(() =>
{
    var random = new Random(1709);
    for (var test = 0; test < 1000; test++)
    {
        var a = Enumerable.Range(0, random.Next(15)).Select(_ => random.Next(5).ToString()).ToArray();
        var b = Enumerable.Range(0, random.Next(15)).Select(_ => random.Next(5).ToString()).ToArray();
        string Text(string[] v) => v.Length == 0 ? "" : string.Join('\n', v) + "\n";
        var d = DiffEngine.Compare(Text(a), Text(b));
        Equal(Text(a), Text(d.Lines.Where(l => l.Kind != DiffKind.Addition).Select(l => l.Text).ToArray()));
        Equal(Text(b), Text(d.Lines.Where(l => l.Kind != DiffKind.Deletion).Select(l => l.Text).ToArray()));
        var dp = new int[a.Length + 1, b.Length + 1];
        for (var i = 0; i <= a.Length; i++) dp[i, 0] = i;
        for (var j = 0; j <= b.Length; j++) dp[0, j] = j;
        for (var i = 1; i <= a.Length; i++) for (var j = 1; j <= b.Length; j++) dp[i, j] = a[i - 1] == b[j - 1] ? dp[i - 1, j - 1] : Math.Min(dp[i - 1, j], dp[i, j - 1]) + 1;
        Equal(dp[a.Length, b.Length], d.Additions + d.Deletions);
    }
}));
var root = Path.Combine(Path.GetTempPath(), "gitspace-tests-" + Guid.NewGuid().ToString("N"));
await using var backend = new DesktopGitBackend();
using var process = new GitProcess();
GitSnapshot snapshot = new();
async Task<GitResult> Do(string operation, string path = "", string value = "", string message = "", string[]? paths = null, bool confirm = false)
{
    var result = await backend.ExecuteAsync(new(operation) { Root = root, Path = path, Value = value, Message = message, Paths = paths ?? [], Confirm = confirm, Author = "Test User", Email = "test@example.com" });
    if (result.Snapshot is not null) snapshot = result.Snapshot;
    return result;
}
await Test("native init", async () => { snapshot = (await backend.ExecuteAsync(new("init") { Root = root })).Snapshot!; Equal("main", snapshot.Branch); });
await Test("native write and status", async () => { await Do("write", "a.txt", message: "one\n"); Equal(1, snapshot.Changes.Length); Equal("A", snapshot.Changes[0].Status); });
await Test("native first selected-file commit", async () => { await Do("commit", message: "Initial", paths: ["a.txt"]); Equal(1, snapshot.Commits.Length); Equal("Initial", snapshot.Commits[0].Summary); Equal(0, snapshot.Changes.Length); });
await Test("native modified diff", async () => { await Do("write", "a.txt", message: "two\n"); var d = await Do("diff", "a.txt"); Equal("one\n", d.Before); Equal("two\n", d.After); });
await Test("native read", async () => { Equal("two\n", (await Do("read", "a.txt")).Text); });
await Test("native staging and unstaging", async () => { await Do("stage", paths: ["a.txt"]); Check(snapshot.Changes[0].Staged); await Do("unstage", paths: ["a.txt"]); Check(!snapshot.Changes[0].Staged); });
await Test("native selected commit preserves unrelated staged changes", async () =>
{
    await Do("write", "b.txt", message: "unrelated\n"); await Do("stage", paths: ["b.txt"]); await Do("commit", message: "Only a", paths: ["a.txt"]);
    Equal(1, snapshot.Changes.Length); Equal("b.txt", snapshot.Changes[0].Path); Check(snapshot.Changes[0].Staged);
    var files = await Do("commitFiles", value: snapshot.Head); Equal(1, files.Changes.Length); Equal("a.txt", files.Changes[0].Path);
});
await Test("native literal pathspec prevents glob staging", async () =>
{
    await Do("write", "[x]*.txt", message: "literal"); await Do("write", "xmatch.txt", message: "not selected");
    await Do("commit", message: "Literal only", paths: ["[x]*.txt"]);
    var files = await Do("commitFiles", value: snapshot.Head); Equal(1, files.Changes.Length); Equal("[x]*.txt", files.Changes[0].Path);
});
await Test("native revision check rejects stale writes", async () =>
{
    try { await backend.ExecuteAsync(new("write") { Path = "a.txt", Message = "wrong", ExpectedHead = new string('a', 40) }); }
    catch (InvalidOperationException) { Equal("two\n", (await Do("read", "a.txt")).Text); return; }
    throw new Exception("Stale head accepted");
});
await Test("native destructive operations require confirmation", async () =>
{
    try { await Do("discard", paths: ["a.txt"]); } catch (InvalidOperationException) { return; } throw new Exception("Missing confirmation accepted");
});
await Test("native branch checkout rename and safe delete", async () =>
{
    await Do("branch", value: "feature/test"); Equal("feature/test", snapshot.Branch);
    await Do("renameBranch", value: "feature/renamed"); Equal("feature/renamed", snapshot.Branch);
    await Do("checkout", value: "main"); await Do("deleteBranch", value: "feature/renamed", confirm: true); Check(!snapshot.Branches.Contains("feature/renamed"));
});
await Test("native tags", async () => { await Do("tag", value: "v-test"); Check(snapshot.Tags.Contains("v-test")); await Do("deleteTag", value: "v-test", confirm: true); Check(!snapshot.Tags.Contains("v-test")); });
await Test("native stash apply preserves stash", async () =>
{
    await process.RunAsync(root, ["config", "user.name", "Test"]); await process.RunAsync(root, ["config", "user.email", "test@example.com"]);
    await Do("stash", message: "Saved test changes"); Equal(0, snapshot.Changes.Length); Equal(1, snapshot.Stashes.Length);
    await Do("stashApply", value: "stash@{0}"); Check(snapshot.Changes.Length > 0); Equal(1, snapshot.Stashes.Length);
    await Do("stashDrop", value: "stash@{0}", confirm: true); Equal(0, snapshot.Stashes.Length);
});
await Test("native discard tracked file", async () => { await Do("write", "a.txt", message: "discard me"); await Do("discard", paths: ["a.txt"], confirm: true); Equal("two\n", (await Do("read", "a.txt")).Text); });
await Test("native symlink editor escape blocked", async () =>
{
    if (OperatingSystem.IsWindows()) return;
    var outside = Path.Combine(Path.GetTempPath(), "gitspace-outside-" + Guid.NewGuid().ToString("N")); await File.WriteAllTextAsync(outside, "safe");
    try
    {
        File.CreateSymbolicLink(Path.Combine(root, "escape.txt"), outside);
        try { await Do("write", "escape.txt", message: "bad"); } catch (IOException) { Equal("safe", await File.ReadAllTextAsync(outside)); return; }
        throw new Exception("Symlink write allowed");
    }
    finally { File.Delete(Path.Combine(root, "escape.txt")); File.Delete(outside); }
});
await Test("native push and fetch with a temporary bare remote", async () =>
{
    var bare = root + "-bare"; Directory.CreateDirectory(bare);
    try
    {
        await process.RunAsync(bare, ["init", "--bare"]); await process.RunAsync(root, ["remote", "add", "origin", bare]);
        await Do("push"); await Do("fetch"); Equal(1, snapshot.Remotes.Length); Equal(0, snapshot.Ahead);
        Equal(snapshot.Head, (await process.RunAsync(bare, ["rev-parse", "refs/heads/main"])).Output.Trim());
    }
    finally { try { Directory.Delete(bare, true); } catch { } }
});
Console.WriteLine($"RESULT: {passed} passed, {failed} failed; 1000 randomized differential trials included.");
Environment.ExitCode = failed == 0 ? 0 : 1;
try { Directory.Delete(root, true); } catch { }
