using GitSpace.Core;
using GitSpace.Controls.Uno;
using GitSpace.Diff;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace GitSpace.Workbench.Uno;

public sealed partial class WorkbenchView
{
    public string ReviewMode => _reviewMode;
    private bool _loadingDiff;
    private string _reviewMode = "all";
    private LineSelection? _selection;
    private StackPanel _reviewBar = null!;
    private GitButton _stageSelection = null!, _stageHunk = null!, _resolve = null!;
    private TextBlock _selectionLabel = null!;
    private (string Root, string Path, string Mode, string Before, string After, bool Ignore)? _renderedKey;
    private string _renderedStatistics = "";
    private void InstallReviewToolbar(Grid toolbar)
    {
        toolbar.RowDefinitions.Add(new() { Height = new GridLength(39) }); toolbar.RowDefinitions.Add(new() { Height = GridLength.Auto });
        _reviewBar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Padding = new Thickness(0, 2, 0, 5) };
        var mode = new GitSegmentedSelector("Diff review mode", ("All", "All changes"), ("Unstaged", "Unstaged changes"), ("Staged", "Staged changes"))
            { SelectedIndex = _reviewMode == "all" ? 0 : _reviewMode == "unstaged" ? 1 : 2 };
        _reviewBar.Children.Add(mode);
        mode.SelectionChanged += (_, _) => _ = Guard(async () =>
        {
            _reviewMode = mode.SelectedIndex == 0 ? "all" : mode.SelectedIndex == 1 ? "unstaged" : "staged";
            _renderedKey = null; _splitButton.IsEnabled = _reviewMode == "all";
            if (_activePath.Length != 0) await SelectFileAsync(_activePath);
        });
        _stageSelection = new GitButton("Stage selection", () => _ = Guard(() => StageSelectionAsync(false))) { IsEnabled = false };
        _stageHunk = new GitButton("Stage hunk", () => _ = Guard(() => StageSelectionAsync(true))) { IsEnabled = false };
        _resolve = new GitButton("Resolve conflicts…", () => _ = Guard(ResolveConflictAsync)) { Visibility = Visibility.Collapsed };
        _selectionLabel = GitTheme.Label("", 10, true);
        _reviewBar.Children.Add(_stageSelection); _reviewBar.Children.Add(_stageHunk); _reviewBar.Children.Add(_resolve); _reviewBar.Children.Add(_selectionLabel);
        Grid.SetRow(_reviewBar, 1); Grid.SetColumnSpan(_reviewBar, toolbar.ColumnDefinitions.Count); toolbar.Children.Add(_reviewBar);
    }
    private void UpdateReviewCommands()
    {
        if (_stageSelection is null || _diff is null) return;
        // A theme rebuild keeps the diff but constructs new header controls.
        // Restore the active path without replacing a binary-preview explanation.
        if (_activePath.Length != 0 && _lastDiff?.Binary != true) _fileLabel.Text = _activePath;
        var partial = !_showHistory && _reviewMode != "all";
        var allowed = partial && !_loadingDiff && !_busy && _selection?.CanSelect == true && _lastDiff?.Binary != true;
        var staged = _reviewMode == "staged";
        _stageSelection.Content = staged ? "Unstage selection" : "Stage selection";
        _stageHunk.Content = staged ? "Unstage hunk" : "Stage hunk";
        AutomationProperties.SetName(_stageSelection, (string)_stageSelection.Content); AutomationProperties.SetName(_stageHunk, (string)_stageHunk.Content);
        _stageSelection.IsEnabled = allowed && _diff.SelectedChangedRows.Length > 0;
        _stageHunk.IsEnabled = allowed && _selection!.HunkAt(_diff.Viewport.SelectedRow).Length > 0;
        _stageSelection.Visibility = _stageHunk.Visibility = partial ? Visibility.Visible : Visibility.Collapsed;
        _selectionLabel.Text = partial ? "Click changed lines · Shift selects a range · Ctrl toggles" : "";
        var conflict = Snapshot.Changes.Any(f => f.Path == _activePath && f.Conflict);
        _resolve.Visibility = !_showHistory && conflict && _backend.Capabilities.Contains("conflict") ? Visibility.Visible : Visibility.Collapsed;
        _resolve.IsEnabled = !_busy;
        _reviewBar.Visibility = _showHistory ? Visibility.Collapsed : Visibility.Visible;
        _splitButton.IsEnabled = _reviewMode == "all" || _showHistory;
    }
    private async Task StageSelectionAsync(bool hunk)
    {
        if (_busy || _loadingDiff || _selection is null || _lastDiff is null || _showHistory || _reviewMode == "all") return;
        var rows = hunk ? _selection.HunkAt(_diff.Viewport.SelectedRow) : _diff.SelectedChangedRows;
        var staged = _reviewMode == "staged"; var text = _selection.Apply(rows, staged);
        await ExecuteAsync(new(staged ? "unstageText" : "stageText")
        {
            Path = _activePath, BeforeHash = _lastDiff.BeforeHash, AfterHash = _lastDiff.AfterHash,
            Message = text, Remove = text.Length == 0 && !(staged ? _lastDiff.BeforeExists : _lastDiff.AfterExists)
        });
        // Never re-stage excluded working changes when the user commits a partial selection.
        _composer.StagedOnly = true; UpdateComposer();
    }
    private async Task ResolveConflictAsync()
    {
        if (_activePath.Length == 0 || _busy) return;
        var path = _activePath; var originalRoot = Snapshot.Root; var originalHead = Snapshot.Head;
        var result = await _backend.ExecuteAsync(new("conflict") { Root = originalRoot, Path = path }, _lifetime.Token);
        var editor = new ConflictResolver(result);
        if (!await ShowAsync(Dialog("Resolve " + path, editor, "Mark resolved"))) return;
        if (Snapshot.Root != originalRoot || Snapshot.Head != originalHead) throw new InvalidOperationException("Repository changed while the conflict editor was open. Reopen the file.");
        await ExecuteAsync(new("resolveConflict") { Path = path, Message = editor.ResolvedText, Remove = editor.DeleteFile, BeforeHash = result.BeforeHash, AfterHash = result.AfterHash, Confirm = true });
    }
    public Task RefreshOnActivationAsync() => IsReady && !_busy && !_dialogOpen && Snapshot.Root.Length != 0 ? Guard(() => ExecuteAsync(new("refresh"))) : Task.CompletedTask;
}
