using System.Text.Json;
using System.Text.RegularExpressions;

namespace GitSpace.Core;

public sealed record GitChange(string Path, string Status = "M", bool Staged = false, bool Conflict = false, string OriginalPath = "");
public sealed record GitCommit(string Id, string Author, string Email, string Date, string Message, string[] Parents)
{
    public string Summary => GitText.ToLf(Message).Split('\n')[0];
    public string ShortId => Id.Length > 7 ? Id[..7] : Id;
}
public sealed record GitStash(string Id, string Message);
public sealed record GitRemote(string Name, string Url);
public sealed record GitSnapshot
{
    public string Root { get; init; } = "";
    public string Name { get; init; } = "";
    public string Branch { get; init; } = "main";
    public string Head { get; init; } = "";
    public GitChange[] Changes { get; init; } = [];
    public GitCommit[] Commits { get; init; } = [];
    public string[] Branches { get; init; } = [];
    public string[] Tags { get; init; } = [];
    public GitRemote[] Remotes { get; init; } = [];
    public GitStash[] Stashes { get; init; } = [];
    public string Operation { get; init; } = "";
    public int Ahead { get; init; }
    public int Behind { get; init; }
}

/// <summary>Explicit, transport-neutral commands. No command contains a shell fragment.</summary>
public sealed record GitRequest(string Operation)
{
    public string Root { get; init; } = "";
    public string Path { get; init; } = "";
    public string Value { get; init; } = "";
    public string Message { get; init; } = "";
    public string Author { get; init; } = "GitSpace User";
    public string Email { get; init; } = "user@example.com";
    public string[] Paths { get; init; } = [];
    public string ExpectedHead { get; init; } = "";
    public bool Confirm { get; init; }
    public string Proxy { get; init; } = "";
    public string Token { get; init; } = "";
    public string ExpectedFileId { get; init; } = "";
    public int Skip { get; init; }
    public int Limit { get; init; } = 200;
}
public sealed record GitResult
{
    public GitSnapshot? Snapshot { get; init; }
    public GitChange[] Changes { get; init; } = [];
    public string Before { get; init; } = "";
    public string After { get; init; } = "";
    public string Text { get; init; } = "";
    public bool Binary { get; init; }
    public string Base { get; init; } = "";
    public string IndexId { get; init; } = "";
    public string FileId { get; init; } = "";
    public GitCommit[] Commits { get; init; } = [];
}
public interface IGitBackend : IAsyncDisposable
{
    string DisplayName { get; }
    IReadOnlySet<string> Capabilities { get; }
    Task<GitResult> ExecuteAsync(GitRequest request, CancellationToken cancellation = default);
}
public static class GitJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = true };
    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);
    public static T Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, Options) ?? throw new InvalidDataException("Empty Git response.");
}
public static partial class GitSafety
{
    public const int MaximumTextBytes = 2 * 1024 * 1024;
    public static string RelativePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length > 4096 || path.IndexOfAny(['\0', '\r', '\n', ':', '\\']) >= 0 || path.StartsWith('/'))
            throw new ArgumentException("A repository-relative file path is required.", nameof(path));
        if (path.Split('/').Any(s => s is "" or "." or ".." || s.Equals(".git", StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("Repository metadata and path traversal are not editable.", nameof(path));
        return path;
    }
    public static string Ref(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.StartsWith('-') || value.EndsWith('/') || value.EndsWith('.') || value.EndsWith(".lock", StringComparison.OrdinalIgnoreCase)
            || value.Contains("..") || value.Contains("@{") || value.Contains("//") || value.IndexOfAny([' ', '\t', '\r', '\n', '\0', '~', '^', ':', '?', '*', '[', '\\']) >= 0
            || value.Split('/').Any(s => s.StartsWith('.') || s.EndsWith(".lock", StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("Invalid branch, tag or revision name.", nameof(value));
        return value;
    }
    public static string CommitId(string value) => ObjectId().IsMatch(value) ? value : throw new ArgumentException("A full commit object ID is required.");
    public static string HttpsRemote(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.UserInfo.Length != 0 || uri.Fragment.Length != 0)
            throw new ArgumentException("Use an HTTPS remote URL without embedded credentials.");
        return uri.AbsoluteUri;
    }
    public static void Confirm(GitRequest request)
    {
        if (!request.Confirm) throw new InvalidOperationException("This operation requires explicit confirmation.");
    }
    public static void Text(string value)
    {
        if (value.Contains('\0') || System.Text.Encoding.UTF8.GetByteCount(value) > MaximumTextBytes)
            throw new InvalidDataException("Only UTF-8 text files up to 2 MiB can be edited here.");
    }
    [GeneratedRegex("^[a-fA-F0-9]{40}([a-fA-F0-9]{24})?$", RegexOptions.CultureInvariant)]
    private static partial Regex ObjectId();
}
