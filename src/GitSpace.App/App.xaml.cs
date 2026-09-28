using GitSpace.Workbench.Uno;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace GitSpace.App;

public sealed partial class App : Application
{
    private Window? _window;
    private WorkbenchView? _workbench;
    public App()
    {
        // Keep UI typography distinct from the embedded monospace diff typeface.
        // A concrete static font avoids a host fallback when a manifest is not ready.
        Uno.UI.FeatureConfiguration.Font.DefaultTextFontFamily = "ms-appx:///Uno.Fonts.OpenSans/Fonts/OpenSans-Regular.ttf";
        InitializeComponent();
        UnhandledException += (_, e) => Console.Error.WriteLine("[GitSpace] " + e.Exception);
    }
    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new Window { Title = "GitSpace" };
        _window.Content = new TextBlock { Text = "Opening GitSpace…", FontSize = 20, Margin = new Thickness(28) }; _window.Activate();
        try
        {
            await ApplicationFonts.InitializeAsync();
            var platform = new AppPlatform(); _workbench = new WorkbenchView(platform); _window.Content = _workbench;
            await _workbench.InitializeAsync();
            _workbench.StateChanged += (_, _) => PublishDiagnostics(); PublishDiagnostics();
            _window.Activated += async (_, e) => { if (e.WindowActivationState != WindowActivationState.Deactivated && _workbench is not null) await _workbench.RefreshOnActivationAsync(); };
            _window.Closed += async (_, _) => { if (_workbench is not null) await _workbench.DisposeAsync(); };
            Console.WriteLine("[GitSpace] Repository workbench ready.");
        }
        catch (Exception e)
        {
            Console.Error.WriteLine("[GitSpace] Startup failed: " + e);
            _window.Content = new ScrollViewer { Content = new TextBlock { Text = "GitSpace could not start.\n\n" + e.Message, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true, Margin = new Thickness(24) } };
        }
    }
    private void PublishDiagnostics()
    {
#if __WASM__
        if (_workbench is null) return;
        var info = GitSpace.Core.GitJson.Serialize(new { ready = _workbench.IsReady, busy = _workbench.IsBusy, repository = _workbench.Snapshot.Name, branch = _workbench.Snapshot.Branch, changes = _workbench.Snapshot.Changes.Length, commits = _workbench.Snapshot.Commits.Length, head = _workbench.Snapshot.Head, activePath = _workbench.ActivePath, history = _workbench.IsHistory, reviewMode = _workbench.ReviewMode, stagedFiles = _workbench.Snapshot.Changes.Count(f => f.Staged), selectedRows = _workbench.Diff.SelectedChangedRows.Length, scrollY = _workbench.Diff.Viewport.ScrollY, split = _workbench.Diff.Viewport.Split, frames = _workbench.Diff.Renderer.FramesRendered, visibleRows = _workbench.Diff.Renderer.LastVisibleRows, rowCount = _workbench.Diff.Renderer.RowCount(_workbench.Diff.Viewport.Split) });
        Uno.Foundation.WebAssemblyRuntime.InvokeJS("globalThis.gitspaceDiagnostics = " + info + "; 'updated'");
#endif
    }
}
