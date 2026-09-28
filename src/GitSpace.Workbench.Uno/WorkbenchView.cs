using GitSpace.Core;
using GitSpace.Controls.Uno;
using GitSpace.Diff;
using GitSpace.Hosting.GitHub;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace GitSpace.Workbench.Uno;

/// <summary>Embeddable repository workbench. Backends and platform integration are injected; no platform-specific process or JavaScript access is used here.</summary>
public sealed partial class WorkbenchView : Grid, IAsyncDisposable
{
    private readonly IWorkbenchPlatform _platform;
    private readonly IGitBackend _backend;
    private readonly GitHubClient _github = new(new HttpClient());
    private readonly CancellationTokenSource _lifetime = new();
    private WorkspacePreferences _preferences = new();
    private string _token = "", _proxy = "";
    private bool _busy, _showHistory, _updating, _dialogOpen, _disposed;
    private int _fileGeneration;
    private string _activePath = "";
    private GitCommit? _selectedCommit;
    private GitResult? _lastDiff;
    private RepositoryTile _repository = null!, _branch = null!, _sync = null!;
    private ChangedFilesView _changes = null!;
    private HistoryView _history = null!;
    private CommitComposer _composer = null!;
    private DiffViewer _diff = null!;
    private GitButton _changesTab = null!, _historyTab = null!, _splitButton = null!, _editButton = null!;
    private TextBlock _fileLabel = null!, _stats = null!, _status = null!, _backendLabel = null!, _commitInfo = null!, _errorLabel = null!;
    private Border _error = null!, _empty = null!;
    private Grid _sidebar = null!, _commitHeader = null!, _body = null!;
    private ComboBox _commitFiles = null!;
    private ProgressBar _progress = null!;
    public GitSnapshot Snapshot { get; private set; } = new();
    public bool IsReady { get; private set; }
    public bool IsBusy => _busy;
    public string ActivePath => _activePath;
    public bool IsHistory => _showHistory;
    public DiffViewer Diff => _diff;
    public event EventHandler? StateChanged;
    public WorkbenchView(IWorkbenchPlatform platform)
    {
        _platform = platform; _backend = platform.Backend; MinWidth = 560; Build();
    }
    public async Task InitializeAsync()
    {
        _preferences = await _platform.LoadPreferencesAsync(); GitTheme.Current = _preferences.Dark ? GitTheme.Dark : GitTheme.Light; Build();
        if (_preferences.LastRepository.Length != 0)
        {
            try { await ExecuteAsync(new("open") { Root = _preferences.LastRepository }); }
            catch (Exception e) { ShowError("Could not reopen the previous repository: " + e.Message + " Open another repository from File."); }
        }
        else await Guard(() => ExecuteAsync(new("demo")));
        IsReady = true; StateChanged?.Invoke(this, EventArgs.Empty);
    }
    private void Build()
    {
        var summary = _composer?.Summary.Text ?? ""; var description = _composer?.Description.Text ?? "";
        var selected = _changes?.SelectedPaths; var filter = _changes?.Filter ?? ""; var stagedOnly = _composer?.StagedOnly ?? false;
        _renderedKey = null; _diff?.Dispose(); Children.Clear(); RowDefinitions.Clear(); KeyboardAccelerators.Clear();
        RequestedTheme = _preferences.Dark ? ElementTheme.Dark : ElementTheme.Light;
        Background = GitTheme.Brush(GitTheme.Current.Surface);
        RowDefinitions.Add(new() { Height = new GridLength(30) });
        RowDefinitions.Add(new() { Height = new GridLength(60) });
        RowDefinitions.Add(new() { Height = new GridLength(2) });
        RowDefinitions.Add(new() { Height = GridLength.Auto });
        RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        RowDefinitions.Add(new() { Height = new GridLength(24) });
        BuildMenu();
        var toolbar = new Grid { Background = GitTheme.Brush("#24292e") };
        toolbar.ColumnDefinitions.Add(new() { Width = new GridLength(300) }); toolbar.ColumnDefinitions.Add(new() { Width = new GridLength(280) }); toolbar.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        _repository = new RepositoryTile("▣", "Current repository", "GitSpace", () => _ = Guard(RepositoryPickerAsync));
        _branch = new RepositoryTile("⑂", "Current branch", "main", () => _ = Guard(BranchPickerAsync)); Grid.SetColumn(_branch, 1);
        _sync = new RepositoryTile("↻", "Fetch origin", "No remote configured", () => _ = Guard(SynchronizeAsync)); Grid.SetColumn(_sync, 2);
        toolbar.Children.Add(_repository); toolbar.Children.Add(_branch); toolbar.Children.Add(_sync); Grid.SetRow(toolbar, 1); Children.Add(toolbar);
        _progress = new ProgressBar { IsIndeterminate = false, Minimum = 0, Maximum = 1, Value = 0, Height = 2, Foreground = GitTheme.Brush("#0969da"), Background = GitTheme.Brush(GitTheme.Current.Border) }; Grid.SetRow(_progress, 2); Children.Add(_progress);
        var errorRow = new Grid { Padding = new Thickness(12, 7, 8, 7), ColumnSpacing = 8 }; errorRow.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); errorRow.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); errorRow.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        _errorLabel = GitTheme.Label("", 12); _errorLabel.TextWrapping = TextWrapping.Wrap; _errorLabel.MaxHeight = 70; _errorLabel.Foreground = GitTheme.Brush("#ffd8d3"); errorRow.Children.Add(_errorLabel);
        var details = new GitButton("Details", () => _ = Guard(() => NoticeAsync("Git operation", _errorLabel.Text))); Grid.SetColumn(details, 1); errorRow.Children.Add(details);
        var closeError = new GitButton("×", () => _error.Visibility = Visibility.Collapsed, accessibleName: "Dismiss error"); Grid.SetColumn(closeError, 2); errorRow.Children.Add(closeError);
        _error = new Border { Background = GitTheme.Brush("#602a2a"), Child = errorRow, Visibility = Visibility.Collapsed }; Grid.SetRow(_error, 3); Children.Add(_error);
        _body = new Grid(); var sidebarColumn = new ColumnDefinition { Width = new GridLength(300) }; _body.ColumnDefinitions.Add(sidebarColumn); _body.ColumnDefinitions.Add(new() { Width = new GridLength(4) }); _body.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        _sidebar = new Grid { Background = GitTheme.Brush(GitTheme.Current.Panel) };
        _sidebar.RowDefinitions.Add(new() { Height = new GridLength(36) }); _sidebar.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) }); _sidebar.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var tabs = new Grid { BorderBrush = GitTheme.Brush(GitTheme.Current.Border), BorderThickness = new Thickness(0, 0, 0, 1) }; tabs.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); tabs.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        _changesTab = new GitButton("Changes", () => _ = Guard(() => SetHistoryAsync(false))) { HorizontalAlignment = HorizontalAlignment.Stretch, Height = 36 }; _changesTab.SetFlat(); tabs.Children.Add(_changesTab);
        _historyTab = new GitButton("History", () => _ = Guard(() => SetHistoryAsync(true))) { HorizontalAlignment = HorizontalAlignment.Stretch, Height = 36 }; _historyTab.SetFlat(); Grid.SetColumn(_historyTab, 1); tabs.Children.Add(_historyTab); _sidebar.Children.Add(tabs);
        _changes = new ChangedFilesView(); Grid.SetRow(_changes, 1); _sidebar.Children.Add(_changes); _changes.FileSelected += (_, path) => _ = Guard(() => SelectFileAsync(path)); _changes.SelectionChanged += (_, _) => UpdateComposer();
        _history = new HistoryView { Visibility = Visibility.Collapsed }; Grid.SetRow(_history, 1); _sidebar.Children.Add(_history); _history.CommitSelected += (_, commit) => _ = Guard(() => SelectCommitAsync(commit));
        _composer = new CommitComposer(); _composer.Summary.Text = summary; _composer.Description.Text = description; _composer.CommitRequested += (_, _) => _ = Guard(CommitAsync); Grid.SetRow(_composer, 2); _sidebar.Children.Add(_composer); _body.Children.Add(_sidebar);
        var splitter = new PaneSplitter(sidebarColumn); Grid.SetColumn(splitter, 1); _body.Children.Add(splitter);
        var content = new Grid(); content.RowDefinitions.Add(new() { Height = GridLength.Auto }); content.RowDefinitions.Add(new() { Height = GridLength.Auto }); content.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        _commitHeader = new Grid { Padding = new Thickness(18, 12, 18, 12), Background = GitTheme.Brush(GitTheme.Current.Panel), Visibility = Visibility.Collapsed, RowSpacing = 8 };
        _commitHeader.RowDefinitions.Add(new() { Height = GridLength.Auto }); _commitHeader.RowDefinitions.Add(new() { Height = GridLength.Auto });
        _commitInfo = GitTheme.Label("", 13, bold: true); _commitInfo.TextWrapping = TextWrapping.Wrap; _commitInfo.MaxHeight = 90; _commitHeader.Children.Add(_commitInfo);
        _commitFiles = new ComboBox { FontSize = 12, MinHeight = 30, HorizontalAlignment = HorizontalAlignment.Stretch, DisplayMemberPath = "Path", PlaceholderText = "Choose a changed file" };
        AutomationProperties.SetName(_commitFiles, "Files in selected commit"); Grid.SetRow(_commitFiles, 1); _commitHeader.Children.Add(_commitFiles); content.Children.Add(_commitHeader);
        _commitFiles.SelectionChanged += (_, _) => { if (!_updating && _commitFiles.SelectedItem is GitChange file) _ = Guard(() => SelectFileAsync(file.Path)); };
        var fileToolbar = new Grid { Background = GitTheme.Brush(GitTheme.Current.Panel), BorderBrush = GitTheme.Brush(GitTheme.Current.Border), BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(14, 0, 8, 0), ColumnSpacing = 10 };
        fileToolbar.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); for (var i = 0; i < 4; i++) fileToolbar.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        _fileLabel = GitTheme.Label("Select a file to review", 12, bold: true); fileToolbar.Children.Add(_fileLabel);
        _stats = GitTheme.Label("", 11, true); Grid.SetColumn(_stats, 1); fileToolbar.Children.Add(_stats);
        _editButton = new GitButton("Edit", () => _ = Guard(EditFileAsync), accessibleName: "Edit selected file"); Grid.SetColumn(_editButton, 2); fileToolbar.Children.Add(_editButton);
        _splitButton = new GitButton(_preferences.SplitDiff ? "Split" : "Unified", () => _ = Guard(ToggleSplitAsync), accessibleName: "Toggle split diff"); Grid.SetColumn(_splitButton, 3); fileToolbar.Children.Add(_splitButton);
        var diffOptions = new GitButton("⋯", accessibleName: "Diff options"); var diffMenu = new MenuFlyout(); AddMenuItem(diffMenu, "Ignore whitespace", () => _ = Guard(ToggleWhitespaceAsync)); AddMenuItem(diffMenu, "Find in diff…", () => _ = Guard(FindAsync)); AddMenuItem(diffMenu, "Copy entire diff", () => _diff.CopySelection(true)); AddMenuItem(diffMenu, "Increase text size", () => _diff.Zoom(1)); AddMenuItem(diffMenu, "Decrease text size", () => _diff.Zoom(-1)); diffOptions.Flyout = diffMenu; Grid.SetColumn(diffOptions, 4); fileToolbar.Children.Add(diffOptions);
        InstallReviewToolbar(fileToolbar);
        Grid.SetRow(fileToolbar, 1); content.Children.Add(fileToolbar);
        _diff = new DiffViewer(); _diff.SelectionChanged += (_, _) => UpdateReviewCommands(); _diff.Rendered += (_, _) => StateChanged?.Invoke(this, EventArgs.Empty); _diff.ApplyTheme(); _diff.SetSplit(_preferences.SplitDiff); Grid.SetRow(_diff, 2); content.Children.Add(_diff);
        var emptyStack = new StackPanel { Spacing = 16, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, MaxWidth = 440, Padding = new Thickness(28) };
        var emptyIcon = GitTheme.Label("✓", 52, true); emptyIcon.HorizontalAlignment = HorizontalAlignment.Center; emptyStack.Children.Add(emptyIcon);
        var emptyTitle = GitTheme.Label("No local changes", 22, bold: true); emptyTitle.HorizontalAlignment = HorizontalAlignment.Center; emptyStack.Children.Add(emptyTitle);
        var emptyText = GitTheme.Label("Your working directory is clean. Edit a file, create a branch, or fetch the latest changes from your remote.", 13, true); emptyText.TextWrapping = TextWrapping.Wrap; emptyText.TextAlignment = TextAlignment.Center; emptyStack.Children.Add(emptyText);
        var newFile = new GitButton("Create a new file", () => _ = Guard(NewFileAsync), true) { HorizontalAlignment = HorizontalAlignment.Center }; emptyStack.Children.Add(newFile);
        _empty = new Border { Background = GitTheme.Brush(GitTheme.Current.Surface), Child = emptyStack, Visibility = Visibility.Collapsed }; Grid.SetRow(_empty, 2); content.Children.Add(_empty);
        Grid.SetColumn(content, 2); _body.Children.Add(content); Grid.SetRow(_body, 4); Children.Add(_body);
        var statusRow = new Grid { Background = GitTheme.Brush(GitTheme.Current.Panel), BorderBrush = GitTheme.Brush(GitTheme.Current.Border), BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(12, 0, 12, 0) };
        statusRow.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); statusRow.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        _status = GitTheme.Label("Opening repository…", 10, true); statusRow.Children.Add(_status);
        _backendLabel = GitTheme.Label(_platform.IsBrowser ? "Browser Git · stored in this browser" : "System Git · Skia renderer", 10, true); Grid.SetColumn(_backendLabel, 1); statusRow.Children.Add(_backendLabel); Grid.SetRow(statusRow, 5); Children.Add(statusRow);
        Shortcut(VirtualKey.Number1, VirtualKeyModifiers.Control, () => _ = Guard(() => SetHistoryAsync(false)));
        Shortcut(VirtualKey.Number2, VirtualKeyModifiers.Control, () => _ = Guard(() => SetHistoryAsync(true)));
        Shortcut(VirtualKey.O, VirtualKeyModifiers.Control, () => _ = Guard(RepositoryPickerAsync));
        Shortcut(VirtualKey.P, VirtualKeyModifiers.Control, () => _ = Guard(BranchPickerAsync));
        Shortcut(VirtualKey.N, VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift, () => _ = Guard(NewBranchAsync));
        Shortcut(VirtualKey.Enter, VirtualKeyModifiers.Control, () => _ = Guard(CommitAsync));
        Shortcut(VirtualKey.F5, VirtualKeyModifiers.None, () => _ = Guard(() => ExecuteAsync(new("refresh"))));
        Shortcut(VirtualKey.F, VirtualKeyModifiers.Control, () => _ = Guard(FindAsync));
        UpdateSnapshot(Snapshot);
        if (selected is not null) _changes.RestoreSelection(selected); _changes.Filter = filter; _composer.StagedOnly = stagedOnly;
        _history.LoadMoreRequested += (_, _) => _ = Guard(() => ExecuteAsync(new("history") { Limit = Math.Min(2000, Snapshot.HistoryLimit + 200) }));
        ApplyTabState();
        if (_lastDiff is not null) ApplyDiff(_lastDiff);
    }
    private void Shortcut(VirtualKey key, VirtualKeyModifiers modifiers, Action action)
    {
        var accelerator = new KeyboardAccelerator { Key = key, Modifiers = modifiers }; accelerator.Invoked += (_, e) => { action(); e.Handled = true; }; KeyboardAccelerators.Add(accelerator);
    }
    private void BuildMenu()
    {
        var menu = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 0, Background = GitTheme.Brush(GitTheme.Current.Panel), Padding = new Thickness(7, 0, 0, 0) };
        var brand = GitTheme.Label("◈", 16); brand.Margin = new Thickness(4, 0, 10, 0); menu.Children.Add(brand);
        Menu("File", f => { AddMenuItem(f, "Open repository…", () => _ = Guard(RepositoryPickerAsync)); AddMenuItem(f, "New repository…", () => _ = Guard(() => OpenRepositoryAsync("init"))); AddMenuItem(f, "Clone repository…", () => _ = Guard(() => OpenRepositoryAsync("clone"))); AddMenuItem(f, "New file…", () => _ = Guard(NewFileAsync)); AddMenuItem(f, "Open tutorial repository", () => _ = Guard(() => ExecuteAsync(new("demo")))); AddMenuItem(f, "Export repository ZIP…", () => _ = Guard(ExportAsync), _backend.Capabilities.Contains("export")); AddMenuItem(f, "Preferences…", () => _ = Guard(PreferencesAsync)); });
        Menu("Edit", f => { AddMenuItem(f, "Edit selected file…", () => _ = Guard(EditFileAsync)); AddMenuItem(f, "Copy entire diff", () => _diff.CopySelection(true)); AddMenuItem(f, "Find in diff…", () => _ = Guard(FindAsync)); AddMenuItem(f, "Discard selected changes…", () => _ = Guard(DiscardAsync)); });
        Menu("View", f => { AddMenuItem(f, "Changes", () => _ = Guard(() => SetHistoryAsync(false))); AddMenuItem(f, "History", () => _ = Guard(() => SetHistoryAsync(true))); AddMenuItem(f, "Unified / split diff", () => _ = Guard(ToggleSplitAsync)); AddMenuItem(f, "Ignore whitespace", () => _ = Guard(ToggleWhitespaceAsync)); AddMenuItem(f, "Toggle light / dark appearance", () => _ = Guard(ToggleThemeAsync)); AddMenuItem(f, "Refresh", () => _ = Guard(() => ExecuteAsync(new("refresh")))); });
        Menu("Repository", f => { CommandItem(f, "Fetch origin", "fetch"); CommandItem(f, "Pull (fast-forward only)", "pull"); CommandItem(f, "Push origin", "push"); AddMenuItem(f, "Add remote…", () => _ = Guard(AddRemoteAsync)); AddMenuItem(f, "Pull requests…", () => _ = Guard(PullRequestsAsync)); AddMenuItem(f, "Create pull request…", () => _ = Guard(CreatePullRequestAsync)); AddMenuItem(f, "View on GitHub", () => _ = Guard(ViewOnGitHubAsync)); AddMenuItem(f, "Stash changes…", () => _ = Guard(StashAsync)); AddMenuItem(f, "Manage stashes…", () => _ = Guard(StashesAsync)); AddMenuItem(f, "Stage selected files", () => _ = Guard(() => ExecuteAsync(new("stage") { Paths = _changes.SelectedPaths }))); AddMenuItem(f, "Unstage selected files", () => _ = Guard(() => ExecuteAsync(new("unstage") { Paths = _changes.SelectedPaths }))); });
        Menu("Branch", f => { AddMenuItem(f, "New branch…", () => _ = Guard(NewBranchAsync)); AddMenuItem(f, "Switch branch…", () => _ = Guard(BranchPickerAsync)); AddMenuItem(f, "Rename current branch…", () => _ = Guard(RenameBranchAsync)); AddMenuItem(f, "Delete branch…", () => _ = Guard(DeleteBranchAsync)); AddMenuItem(f, "Merge into current branch…", () => _ = Guard(() => IntegrateAsync("merge"))); AddMenuItem(f, "Rebase current branch…", () => _ = Guard(() => IntegrateAsync("rebase")), _backend.Capabilities.Contains("rebase")); AddMenuItem(f, "Continue operation…", () => _ = Guard(() => ConfirmCommandAsync("continue", "Continue the current Git operation? Resolve and stage conflicts first.")), _backend.Capabilities.Contains("continue")); AddMenuItem(f, "Abort operation…", () => _ = Guard(() => ConfirmCommandAsync("abort", "Abort the current Git operation?")), _backend.Capabilities.Contains("abort")); AddMenuItem(f, "Create tag…", () => _ = Guard(TagAsync)); AddMenuItem(f, "Amend latest commit…", () => _ = Guard(AmendAsync)); AddMenuItem(f, "Revert selected commit…", () => _ = Guard(() => HistoryActionAsync("revert")), _backend.Capabilities.Contains("revert")); AddMenuItem(f, "Cherry-pick selected commit…", () => _ = Guard(() => HistoryActionAsync("cherryPick")), _backend.Capabilities.Contains("cherryPick")); });
        Menu("Help", f => { AddMenuItem(f, "Keyboard shortcuts", () => _ = Guard(() => NoticeAsync("Keyboard shortcuts", "Ctrl+1  Changes\nCtrl+2  History\nCtrl+O  Open repository\nCtrl+P  Switch branch\nCtrl+Shift+N  New branch\nCtrl+Enter  Commit selected files\nCtrl+F  Find in diff\nF5  Refresh\n\nDiff: arrow keys, Page Up/Down, Home/End; Ctrl+C copies selected line. Use menus on systems that reserve browser shortcuts."))); AddMenuItem(f, "About GitSpace", () => _ = Guard(() => NoticeAsync("GitSpace 0.1.0", "An independent, MIT-licensed Git client built with Uno Platform and Skia.\n\nDesktop uses your installed Git. Browser repositories are real Git repositories held in browser storage. Browser network operations may require a trusted CORS proxy. Export or push important browser work: storage can be evicted.\n\nThis is a development preview, not complete or pixel-qualified GitHub Desktop parity. GitHub and GitHub Desktop are trademarks of GitHub, Inc. GitSpace is not affiliated with GitHub."))); });
        Children.Add(menu);
        void Menu(string title, Action<MenuFlyout> populate) { var button = new GitButton(title) { Height = 30, MinHeight = 30, Padding = new Thickness(9, 3, 9, 3) }; button.SetFlat(); var flyout = new MenuFlyout(); populate(flyout); button.Flyout = flyout; menu.Children.Add(button); }
    }
    private static void AddMenuItem(MenuFlyout menu, string title, Action action, bool enabled = true)
    {
        var item = new MenuFlyoutItem { Text = title, IsEnabled = enabled }; item.Click += (_, _) => action(); menu.Items.Add(item);
    }
    private void CommandItem(MenuFlyout menu, string title, string operation) => AddMenuItem(menu, title, () => _ = Guard(() => ExecuteAsync(new(operation))), _backend.Capabilities.Contains(operation));
    private async Task Guard(Func<Task> action)
    {
        if (_disposed) return;
        try { await action(); }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception e) { ShowError(e.Message); }
    }
    private void ShowError(string message)
    {
        if (_disposed) return; if (_token.Length != 0) message = message.Replace(_token, "[redacted]", StringComparison.Ordinal);
        _errorLabel.Text = message; _error.Visibility = Visibility.Visible; _status.Text = "Operation did not complete. Review the error before retrying."; StateChanged?.Invoke(this, EventArgs.Empty);
    }
    private async Task ExecuteAsync(GitRequest request)
    {
        if (_busy) throw new InvalidOperationException("Another repository operation is running.");
        _busy = true; _progress.IsIndeterminate = true; _error.Visibility = Visibility.Collapsed; UpdateComposer();
        _repository.IsEnabled = _branch.IsEnabled = _sync.IsEnabled = false;
        _status.Text = request.Operation + "…"; StateChanged?.Invoke(this, EventArgs.Empty);
        var target = request.Operation is "open" or "init" or "clone" or "demo";
        request = request with { Root = target ? request.Root : Snapshot.Root, ExpectedHead = target ? "" : Snapshot.Head, Author = _preferences.Author, Email = _preferences.Email, Token = _token, Proxy = _proxy };
        try
        {
            var result = await _backend.ExecuteAsync(request, _lifetime.Token);
            if (result.Snapshot is not null) UpdateSnapshot(result.Snapshot);
            _preferences = _preferences with { LastRepository = Snapshot.Root, RecentRepositories = new[] { Snapshot.Root }.Concat(_preferences.RecentRepositories).Where(r => r.Length != 0).Distinct(StringComparer.Ordinal).Take(12).ToArray() }; await _platform.SavePreferencesAsync(_preferences);
            if (_showHistory && Snapshot.Commits.Length != 0) await SelectCommitAsync(_selectedCommit is { } selected && Snapshot.Commits.Any(c => c.Id == selected.Id) ? selected : Snapshot.Commits[0]);
            else if (_activePath.Length != 0) await SelectFileAsync(_activePath);
            else { _lastDiff = null; _diff.SetDocument(null); _stats.Text = ""; }
        }
        catch
        {
            // Failed merges and partially completed commands still change repository state.
            // Refresh it without converting the original failure into a success notification.
            if (Snapshot.Root.Length != 0 && !target)
            {
                try { var current = await _backend.ExecuteAsync(new("refresh"), _lifetime.Token); if (current.Snapshot is not null) UpdateSnapshot(current.Snapshot); } catch { }
            }
            throw;
        }
        finally
        {
            _busy = false; _progress.IsIndeterminate = false; _repository.IsEnabled = _branch.IsEnabled = _sync.IsEnabled = true;
            UpdateComposer(); StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }
    private void UpdateSnapshot(GitSnapshot snapshot)
    {
        var changedRepository = Snapshot.Root != snapshot.Root; Snapshot = snapshot;
        if (changedRepository) { _selectedCommit = null; _activePath = ""; _lastDiff = null; }
        if (!_showHistory && !snapshot.Changes.Any(f => f.Path == _activePath)) _activePath = snapshot.Changes.FirstOrDefault(f => f.Path.EndsWith("RepositoryWorkspace.cs", StringComparison.Ordinal))?.Path ?? snapshot.Changes.FirstOrDefault()?.Path ?? "";
        _repository.SetValue(snapshot.Name.Length == 0 ? "Open a repository" : snapshot.Name);
        _branch.SetValue(snapshot.Branch);
        _sync.SetValue(snapshot.Remotes.Length == 0 ? "Publish repository" : "Fetch origin", snapshot.Remotes.Length == 0 ? "No remote configured" : _platform.IsBrowser ? "Remote synchronization" : $"{snapshot.Ahead} ahead · {snapshot.Behind} behind");
        _changes.SetFiles(snapshot.Changes, _activePath, changedRepository); _history.SetCommits(snapshot.Commits, snapshot.HasMoreHistory);
        _status.Text = snapshot.Root.Length == 0 ? "Open a repository to begin" : snapshot.Operation.Length != 0 ? snapshot.Operation + " in progress · resolve and stage conflicts, then continue or abort" : snapshot.Changes.Length + " changed files  ·  " + snapshot.Branch + "  ·  " + snapshot.Commits.Length + (snapshot.HasMoreHistory ? "+" : "") + " commits loaded";
        _empty.Visibility = !_showHistory && snapshot.Changes.Length == 0 && snapshot.Root.Length != 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateComposer(); StateChanged?.Invoke(this, EventArgs.Empty);
    }
    private void UpdateComposer()
    {
        _composer?.SetState(Snapshot.Branch, _changes?.SelectedPaths.Length ?? 0, _busy || Snapshot.Operation.Length != 0, _preferences.Author, Snapshot.Changes.Count(f => f.Staged));
        if (_editButton is not null) _editButton.IsEnabled = !_showHistory && !_busy && _activePath.Length != 0;
        UpdateReviewCommands();
    }
    private void ApplyTabState()
    {
        _changes.Visibility = _showHistory ? Visibility.Collapsed : Visibility.Visible; _history.Visibility = _showHistory ? Visibility.Visible : Visibility.Collapsed;
        _composer.Visibility = _showHistory ? Visibility.Collapsed : Visibility.Visible; _commitHeader.Visibility = _showHistory ? Visibility.Visible : Visibility.Collapsed;
        _changesTab.SetSelected(!_showHistory); _historyTab.SetSelected(_showHistory); _empty.Visibility = !_showHistory && Snapshot.Changes.Length == 0 && Snapshot.Root.Length != 0 ? Visibility.Visible : Visibility.Collapsed; UpdateComposer();
    }
    private async Task SetHistoryAsync(bool show)
    {
        _showHistory = show; ApplyTabState();
        if (show && Snapshot.Commits.Length != 0) await SelectCommitAsync(_selectedCommit ?? Snapshot.Commits[0]);
        else if (!show) { _activePath = Snapshot.Changes.FirstOrDefault()?.Path ?? ""; _changes.SetFiles(Snapshot.Changes, _activePath); if (_activePath.Length != 0) await SelectFileAsync(_activePath); else _diff.SetDocument(null); }
        StateChanged?.Invoke(this, EventArgs.Empty);
    }
    private async Task SelectCommitAsync(GitCommit commit)
    {
        _selectedCommit = commit; _commitInfo.Text = commit.Summary + "\n" + commit.Author + "  ·  " + commit.ShortId + "  ·  " + commit.Date;
        var result = await _backend.ExecuteAsync(new("commitFiles") { Value = commit.Id }, _lifetime.Token);
        if (_selectedCommit.Id != commit.Id) return;
        _updating = true; _commitFiles.ItemsSource = result.Changes; _commitFiles.SelectedItem = result.Changes.FirstOrDefault(); _updating = false;
        if (result.Changes.Length != 0) await SelectFileAsync(result.Changes[0].Path);
        else { _activePath = ""; _fileLabel.Text = "No file changes in this commit"; _diff.SetDocument(null); }
    }
    private async Task SelectFileAsync(string path)
    {
        var generation = ++_fileGeneration; var commit = _showHistory ? _selectedCommit?.Id ?? "" : "";
        _loadingDiff = true; _activePath = path; _fileLabel.Text = path; _stats.Text = "Loading…"; UpdateComposer();
        try
        {
            var result = await _backend.ExecuteAsync(new(_showHistory || _reviewMode == "all" ? "diff" : "review") { Root = Snapshot.Root, Path = path, Value = _showHistory ? commit : _reviewMode == "all" ? "" : _reviewMode }, _lifetime.Token);
            if (generation != _fileGeneration || _disposed) return;
            _loadingDiff = false; _lastDiff = result; ApplyDiff(result); StateChanged?.Invoke(this, EventArgs.Empty);
        }
        catch
        {
            if (generation == _fileGeneration && !_disposed)
            {
                // An unsuccessful preview must not leave actions tied to an older file's diff.
                _selection = null; _lastDiff = null; _renderedKey = null;
                _diff.SetDocument(null); _stats.Text = "Unable to load diff";
            }
            throw;
        }
        finally { if (generation == _fileGeneration) { _loadingDiff = false; UpdateReviewCommands(); } }
    }
    private void ApplyDiff(GitResult result)
    {
        if (result.Binary) { _selection = null; _renderedKey = null; _diff.SetDocument(null); _stats.Text = "Binary / large file"; _fileLabel.Text = _activePath + "  ·  " + (result.Text.Length == 0 ? "Binary content is not shown" : result.Text); UpdateReviewCommands(); return; }
        var mode = _showHistory ? "history:" + _selectedCommit?.Id : _reviewMode;
        var key = (Snapshot.Root, _activePath, mode, result.Before, result.After, _preferences.IgnoreWhitespace);
        if (_renderedKey != key)
        {
            var exact = !_showHistory && _reviewMode != "all";
            _selection = exact ? new LineSelection(result.Before, result.After, _lifetime.Token) : null;
            var document = _selection?.Document ?? DiffEngine.Compare(result.Before, result.After, _preferences.IgnoreWhitespace, cancellation: _lifetime.Token);
            _diff.SetDocument(document); _diff.SetSplit(!exact && _preferences.SplitDiff);
            _renderedStatistics = $"+{document.Additions}  −{document.Deletions}" + (document.Coarse ? "  ·  coarse diff" : "") + (document.OldHasFinalNewline != document.NewHasFinalNewline ? "  ·  final newline changed" : "");
            _renderedKey = key;
        }
        _stats.Text = _renderedStatistics; _empty.Visibility = Visibility.Collapsed; UpdateReviewCommands();
    }
    private async Task CommitAsync()
    {
        if (_busy || !_composer.CommitButton.IsEnabled) return;
        await ExecuteAsync(new(_composer.StagedOnly ? "commitStaged" : "commit") { Paths = _composer.StagedOnly ? [] : _changes.SelectedPaths, Message = _composer.Message, IndexHash = Snapshot.IndexHash }); _composer.Clear();
    }
    private async Task ToggleSplitAsync() { if (!_showHistory && _reviewMode != "all") return; _preferences = _preferences with { SplitDiff = !_preferences.SplitDiff }; _diff.SetSplit(_preferences.SplitDiff); _splitButton.Content = _preferences.SplitDiff ? "Split" : "Unified"; await _platform.SavePreferencesAsync(_preferences); StateChanged?.Invoke(this, EventArgs.Empty); }
    private async Task ToggleWhitespaceAsync() { _preferences = _preferences with { IgnoreWhitespace = !_preferences.IgnoreWhitespace }; if (_lastDiff is not null) ApplyDiff(_lastDiff); await _platform.SavePreferencesAsync(_preferences); }
    private async Task ToggleThemeAsync() { _preferences = _preferences with { Dark = !_preferences.Dark }; GitTheme.Current = _preferences.Dark ? GitTheme.Dark : GitTheme.Light; Build(); await _platform.SavePreferencesAsync(_preferences); StateChanged?.Invoke(this, EventArgs.Empty); }
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return; _disposed = true; _lifetime.Cancel(); _diff.Dispose(); await _backend.DisposeAsync(); _lifetime.Dispose();
    }
}
