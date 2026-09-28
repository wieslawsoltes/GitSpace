namespace GitSpace.Git;

internal static class Tutorial
{
    internal const string Original = "using GitSpace.Core;\n\nnamespace GitSpace.App;\n\npublic sealed class RepositoryWorkspace\n{\n    private readonly IGitBackend _backend;\n\n    public RepositoryWorkspace(IGitBackend backend)\n    {\n        _backend = backend;\n    }\n\n    public async Task RefreshAsync()\n    {\n        var result = await _backend.ExecuteAsync(\n            new GitRequest(\"refresh\"));\n\n        Render(result.Snapshot);\n    }\n\n    private void Render(GitSnapshot? snapshot)\n    {\n        Console.WriteLine(snapshot?.Branch);\n    }\n}\n";
    internal const string Updated = "using GitSpace.Core;\nusing System.Diagnostics;\n\nnamespace GitSpace.App;\n\npublic sealed class RepositoryWorkspace\n{\n    private readonly IGitBackend _backend;\n    private readonly SemaphoreSlim _refreshGate = new(1);\n\n    public RepositoryWorkspace(IGitBackend backend)\n    {\n        _backend = backend;\n    }\n\n    public async Task RefreshAsync(CancellationToken cancellation)\n    {\n        await _refreshGate.WaitAsync(cancellation);\n        try\n        {\n            var watch = Stopwatch.StartNew();\n            var result = await _backend.ExecuteAsync(\n                new GitRequest(\"refresh\"), cancellation);\n\n            Render(result.Snapshot);\n            Debug.WriteLine($\"Refreshed in {watch.ElapsedMilliseconds} ms\");\n        }\n        finally\n        {\n            _refreshGate.Release();\n        }\n    }\n\n    private void Render(GitSnapshot? snapshot)\n    {\n        Console.WriteLine(snapshot?.Branch);\n    }\n}\n";
    internal static readonly (string Path, string Text, string Message)[] Files =
    [
        ("README.md", "# GitSpace\n\nA native Git workspace.\n", "Create the GitSpace workspace"),
        ("src/RepositoryWorkspace.cs", Original, "Add shared repository commands"),
        ("src/theme.json", "{\n  \"theme\": \"light\",\n  \"accent\": \"#0969da\"\n}\n", "Introduce application theme tokens"),
        ("docs/architecture.md", "# Architecture\n\nCore → Git → Controls → App\n", "Document reusable library boundaries")
    ];
    internal static readonly (string Path, string Text)[] Changes =
    [
        ("src/RepositoryWorkspace.cs", Updated),
        ("README.md", "# GitSpace\n\nA native Git workspace for desktop and browser.\n\nBuilt with Uno Platform and GPU-backed Skia rendering.\n"),
        ("src/theme.json", "{\n  \"theme\": \"dark\",\n  \"accent\": \"#0969da\",\n  \"density\": \"compact\"\n}\n"),
        ("docs/roadmap.md", "# Next up\n\n- Review your changes\n- Create a feature branch\n- Make a real Git commit\n")
    ];
}
