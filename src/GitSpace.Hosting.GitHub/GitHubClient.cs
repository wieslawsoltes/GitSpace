using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GitSpace.Hosting.GitHub;

public sealed record PullRequest(int Number, string Title, string Author, string Head, string Base, string Url, bool Draft);
/// <summary>Does not persist tokens, set HttpClient default authorization, or follow API URLs supplied by repository content.</summary>
public sealed class GitHubClient(HttpClient http)
{
    private static string Segment(string value)
    {
        if (!Regex.IsMatch(value, "^[A-Za-z0-9_.-]+$", RegexOptions.CultureInvariant)) throw new ArgumentException("Invalid GitHub owner or repository name.");
        return Uri.EscapeDataString(value);
    }
    private async Task<JsonDocument> Send(HttpMethod method, string path, string token, object? body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, "https://api.github.com/" + path);
        request.Headers.UserAgent.ParseAdd("GitSpace/0.1.0"); request.Headers.Accept.ParseAdd("application/vnd.github+json");
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        if (!string.IsNullOrEmpty(token)) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null) request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) throw new HttpRequestException($"GitHub returned {(int)response.StatusCode} ({response.ReasonPhrase}). Check repository access, token permissions, or the API rate limit.");
        if (response.Content.Headers.ContentLength > 4 * 1024 * 1024) throw new InvalidDataException("GitHub response exceeds the 4 MiB limit.");
        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var output = new MemoryStream(); var buffer = new byte[8192];
        while (true)
        {
            var count = await stream.ReadAsync(buffer, ct).ConfigureAwait(false); if (count == 0) break;
            if (output.Length + count > 4 * 1024 * 1024) throw new InvalidDataException("GitHub response exceeds the 4 MiB limit.");
            output.Write(buffer, 0, count);
        }
        return JsonDocument.Parse(output.ToArray());
    }
    private static PullRequest Parse(JsonElement item) => new(item.GetProperty("number").GetInt32(), item.GetProperty("title").GetString() ?? "", item.GetProperty("user").GetProperty("login").GetString() ?? "", item.GetProperty("head").GetProperty("ref").GetString() ?? "", item.GetProperty("base").GetProperty("ref").GetString() ?? "", item.GetProperty("html_url").GetString() ?? "", item.TryGetProperty("draft", out var draft) && draft.GetBoolean());
    public async Task<PullRequest[]> ListPullRequestsAsync(string owner, string repository, string token = "", CancellationToken ct = default)
    {
        using var json = await Send(HttpMethod.Get, $"repos/{Segment(owner)}/{Segment(repository)}/pulls?state=open&per_page=100", token, null, ct).ConfigureAwait(false);
        return json.RootElement.EnumerateArray().Select(Parse).ToArray();
    }
    public async Task<PullRequest> CreatePullRequestAsync(string owner, string repository, string title, string head, string @base, string body, string token, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(token)) throw new ArgumentException("A title and session token are required.");
        using var json = await Send(HttpMethod.Post, $"repos/{Segment(owner)}/{Segment(repository)}/pulls", token, new { title, head, @base, body }, ct).ConfigureAwait(false);
        return Parse(json.RootElement);
    }
    public static bool TryParseRemote(string url, out string owner, out string repository)
    {
        owner = repository = "";
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https" || !uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) || uri.UserInfo.Length != 0) return false;
        var pieces = uri.AbsolutePath.Trim('/').Split('/'); if (pieces.Length != 2) return false;
        owner = pieces[0]; repository = pieces[1].EndsWith(".git", StringComparison.OrdinalIgnoreCase) ? pieces[1][..^4] : pieces[1];
        return owner.Length != 0 && repository.Length != 0;
    }
}
