using GitSpace.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace GitSpace.Controls.Uno;

public sealed class ChangedFilesView : Grid
{
    private readonly ListView _list = new() { SelectionMode = ListViewSelectionMode.Single, IsItemClickEnabled = true, Padding = new Thickness(0) };
    private readonly TextBlock _count;
    private readonly CheckBox _all;
    private readonly TextBox _filter = GitTheme.Input("Filter changed files");
    private GitChange[] _files = [];
    private readonly HashSet<string> _selected = new(StringComparer.Ordinal);
    private string _active = "";
    private bool _updating;
    public event EventHandler<string>? FileSelected;
    public event EventHandler? SelectionChanged;
    public string[] SelectedPaths => _files.Where(f => _selected.Contains(f.Path)).Select(f => f.Path).ToArray();
    public ChangedFilesView()
    {
        RowDefinitions.Add(new() { Height = new GridLength(35) }); RowDefinitions.Add(new() { Height = new GridLength(38) }); RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        var header = new Grid { Padding = new Thickness(10, 0, 10, 0), Background = GitTheme.Brush(GitTheme.Current.Panel) };
        header.ColumnDefinitions.Add(new() { Width = new GridLength(28) }); header.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        _all = new CheckBox { MinWidth = 24, MinHeight = 24, IsChecked = true, VerticalAlignment = VerticalAlignment.Center }; AutomationProperties.SetName(_all, "Select all changed files"); header.Children.Add(_all);
        _count = GitTheme.Label("0 changed files", 12, bold: true); Grid.SetColumn(_count, 1); header.Children.Add(_count); Children.Add(header);
        _filter.Margin = new Thickness(10, 2, 10, 5); Grid.SetRow(_filter, 1); Children.Add(_filter); AutomationProperties.SetName(_filter, "Filter changed files");
        Grid.SetRow(_list, 2); Children.Add(_list); AutomationProperties.SetName(_list, "Changed files");
        _filter.TextChanged += (_, _) => Rebuild();
        _all.Checked += (_, _) => ToggleAll(true); _all.Unchecked += (_, _) => ToggleAll(false);
        _list.SelectionChanged += (_, _) => { if (_updating || _list.SelectedItem is not ListViewItem { Tag: GitChange file }) return; _active = file.Path; FileSelected?.Invoke(this, file.Path); };
    }
    private void ToggleAll(bool value)
    {
        if (_updating) return;
        foreach (var file in _files) { if (value) _selected.Add(file.Path); else _selected.Remove(file.Path); }
        Rebuild(); SelectionChanged?.Invoke(this, EventArgs.Empty);
    }
    public void SetFiles(GitChange[] files, string active, bool newRepository = false)
    {
        var existing = _files.Select(f => f.Path).ToHashSet(StringComparer.Ordinal); if (newRepository) { existing.Clear(); _selected.Clear(); }
        foreach (var file in files) if (!existing.Contains(file.Path)) _selected.Add(file.Path);
        _selected.IntersectWith(files.Select(f => f.Path)); _files = files; _active = active; Rebuild();
    }
    private void Rebuild()
    {
        _updating = true; _list.Items.Clear(); _count.Text = _files.Length + " changed " + (_files.Length == 1 ? "file" : "files");
        _all.IsChecked = _files.Length > 0 && _files.All(f => _selected.Contains(f.Path));
        // ListView virtualizes the item containers' arrangement and scrolling. Row contents are bounded by the snapshot limit.
        foreach (var file in _files.Where(f => f.Path.Contains(_filter.Text, StringComparison.OrdinalIgnoreCase)))
        {
            var row = new Grid { MinHeight = 30, Padding = new Thickness(8, 0, 9, 0), HorizontalAlignment = HorizontalAlignment.Stretch };
            row.ColumnDefinitions.Add(new() { Width = new GridLength(29) }); row.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); row.ColumnDefinitions.Add(new() { Width = new GridLength(20) });
            var check = new CheckBox { MinWidth = 24, MinHeight = 24, IsChecked = _selected.Contains(file.Path), VerticalAlignment = VerticalAlignment.Center };
            AutomationProperties.SetName(check, "Include " + file.Path + " in commit"); row.Children.Add(check);
            check.Checked += (_, _) => { if (!_updating) { _selected.Add(file.Path); SelectionChanged?.Invoke(this, EventArgs.Empty); } };
            check.Unchecked += (_, _) => { if (!_updating) { _selected.Remove(file.Path); SelectionChanged?.Invoke(this, EventArgs.Empty); } };
            var name = GitTheme.Label(file.Path, 12); Grid.SetColumn(name, 1); row.Children.Add(name);
            var status = GitTheme.Label(file.Status == "A" ? "+" : file.Status == "D" ? "−" : file.Conflict ? "!" : "•", 15, bold: true);
            status.Foreground = GitTheme.Brush(file.Status == "A" ? "#3fb950" : file.Status == "D" || file.Conflict ? "#f85149" : "#d29922"); Grid.SetColumn(status, 2); row.Children.Add(status);
            var item = new ListViewItem { Content = row, Tag = file, Padding = new Thickness(0), Margin = new Thickness(0), MinHeight = 30, HorizontalContentAlignment = HorizontalAlignment.Stretch };
            AutomationProperties.SetName(item, file.Path + " · " + file.Status); _list.Items.Add(item);
            if (file.Path == _active) _list.SelectedItem = item;
        }
        _updating = false;
    }
}

public sealed class HistoryView : Grid
{
    private readonly ListView _list = new() { SelectionMode = ListViewSelectionMode.Single, Padding = new Thickness(0) };
    private readonly TextBox _filter = GitTheme.Input("Filter history");
    private GitCommit[] _commits = [];
    private bool _updating;
    public event EventHandler<GitCommit>? CommitSelected;
    public HistoryView()
    {
        RowDefinitions.Add(new() { Height = new GridLength(40) }); RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        _filter.Margin = new Thickness(10, 5, 10, 5); Children.Add(_filter); Grid.SetRow(_list, 1); Children.Add(_list);
        AutomationProperties.SetName(_filter, "Filter commit history"); AutomationProperties.SetName(_list, "Commit history");
        _filter.TextChanged += (_, _) => Rebuild();
        _list.SelectionChanged += (_, _) => { if (!_updating && _list.SelectedItem is ListViewItem { Tag: GitCommit commit }) CommitSelected?.Invoke(this, commit); };
    }
    public void SetCommits(GitCommit[] commits) { _commits = commits; Rebuild(); }
    private void Rebuild()
    {
        var selected = (_list.SelectedItem as ListViewItem)?.Tag as GitCommit; _updating = true; _list.Items.Clear();
        foreach (var commit in _commits.Where(c => (c.Message + c.Author + c.Id).Contains(_filter.Text, StringComparison.OrdinalIgnoreCase)))
        {
            var panel = new StackPanel { Spacing = 5, Padding = new Thickness(12, 8, 12, 8) };
            panel.Children.Add(GitTheme.Label(commit.Summary, 12, bold: true));
            var date = DateTimeOffset.TryParse(commit.Date, out var when) ? when.ToLocalTime().ToString("MMM d, HH:mm") : commit.Date;
            panel.Children.Add(GitTheme.Label("●  " + commit.Author + "  ·  " + date, 10, true));
            var item = new ListViewItem { Content = panel, Tag = commit, Padding = new Thickness(0), MinHeight = 54, HorizontalContentAlignment = HorizontalAlignment.Stretch };
            AutomationProperties.SetName(item, commit.Summary + " by " + commit.Author); _list.Items.Add(item);
            if (selected?.Id == commit.Id) _list.SelectedItem = item;
        }
        _updating = false;
    }
}
