using GitSpace.Core;

namespace GitSpace.Workbench.Uno;

/// <summary>Only non-secret preferences are persisted. Access tokens live in a workbench instance.</summary>
public sealed record WorkspacePreferences
{
    public string Author { get; init; } = "GitSpace User";
    public string Email { get; init; } = "user@example.com";
    public string LastRepository { get; init; } = "";
    public bool Dark { get; init; } = true;
    public bool SplitDiff { get; init; }
    public bool IgnoreWhitespace { get; init; }
}
public interface IWorkbenchPlatform
{
    IGitBackend Backend { get; }
    bool IsBrowser { get; }
    Task<WorkspacePreferences> LoadPreferencesAsync();
    Task SavePreferencesAsync(WorkspacePreferences preferences);
    Task OpenExternalAsync(string httpsUrl);
    Task DownloadAsync(string fileName, string base64Content);
}
