using System.Text;
using GitSpace.Core;
using GitSpace.Workbench.Uno;

namespace GitSpace.App;

internal sealed class AppPlatform : IWorkbenchPlatform
{
#if __WASM__
    public IGitBackend Backend { get; } = new BrowserGitBackend();
    public bool IsBrowser => true;
#else
    public IGitBackend Backend { get; } = new GitSpace.Git.DesktopGitBackend();
    public bool IsBrowser => false;
#endif
    private static string PreferencesPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GitSpace", "preferences.json");
    public async Task<WorkspacePreferences> LoadPreferencesAsync()
    {
        try
        {
#if __WASM__
            var json = Uno.Foundation.WebAssemblyRuntime.InvokeJS("localStorage.getItem('GitSpace.preferences.v1') || ''");
            await Task.CompletedTask;
#else
            var json = File.Exists(PreferencesPath) ? await File.ReadAllTextAsync(PreferencesPath) : "";
#endif
            return json.Length == 0 ? new() : GitJson.Deserialize<WorkspacePreferences>(json);
        }
        catch (Exception e) when (e is IOException or System.Text.Json.JsonException or UnauthorizedAccessException) { Console.Error.WriteLine("[GitSpace] Preferences were not loaded: " + e.Message); return new(); }
    }
    public async Task SavePreferencesAsync(WorkspacePreferences preferences)
    {
        var json = GitJson.Serialize(preferences);
#if __WASM__
        Uno.Foundation.WebAssemblyRuntime.InvokeJS("localStorage.setItem('GitSpace.preferences.v1', " + System.Text.Json.JsonSerializer.Serialize(json) + "); 'saved'");
        await Task.CompletedTask;
#else
        Directory.CreateDirectory(Path.GetDirectoryName(PreferencesPath)!); var temporary = PreferencesPath + ".tmp";
        await File.WriteAllTextAsync(temporary, json, new UTF8Encoding(false)); File.Move(temporary, PreferencesPath, true);
#endif
    }
    public Task OpenExternalAsync(string httpsUrl)
    {
        var validated = GitSafety.HttpsRemote(httpsUrl);
#if __WASM__
        Uno.Foundation.WebAssemblyRuntime.InvokeJS("window.open(" + System.Text.Json.JsonSerializer.Serialize(validated) + ", '_blank', 'noopener,noreferrer'); 'opened'");
#else
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(validated) { UseShellExecute = true });
#endif
        return Task.CompletedTask;
    }
    public Task DownloadAsync(string fileName, string base64Content)
    {
#if __WASM__
        Uno.Foundation.WebAssemblyRuntime.InvokeJS("(() => { const binary = atob(" + System.Text.Json.JsonSerializer.Serialize(base64Content) + "); const bytes = Uint8Array.from(binary, c => c.charCodeAt(0)); const url = URL.createObjectURL(new Blob([bytes], {type:'application/zip'})); const a = document.createElement('a'); a.href=url; a.download=" + System.Text.Json.JsonSerializer.Serialize(fileName) + "; a.click(); setTimeout(() => URL.revokeObjectURL(url), 30000); return 'downloaded'; })()");
        return Task.CompletedTask;
#else
        throw new NotSupportedException("Repository ZIP export is supplied by the browser backend. Desktop repositories are already ordinary folders.");
#endif
    }
}
