using System.ComponentModel;
using GitSpace.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;

namespace GitSpace.Controls.Uno;

/// <summary>Data-only row model; visual elements are created by ListView only when realized.</summary>
public sealed class ChangeRow : INotifyPropertyChanged
{
    private bool _included;
    private readonly Action<ChangeRow> _changed;
    public GitChange File { get; private set; }
    public string Path => File.Path;
    public string CheckName => "Include " + Path + " in commit";
    public string Status => File.Conflict ? "!" : File.Status == "A" ? "+" : File.Status == "D" ? "−" : "•";
    public Brush StatusBrush => GitTheme.Brush(File.Conflict || File.Status == "D" ? "#f85149" : File.Status == "A" ? "#3fb950" : "#d29922");
    public bool Included { get => _included; set { if (_included == value) return; _included = value; Notify(nameof(Included)); _changed(this); } }
    public event PropertyChangedEventHandler? PropertyChanged;
    public ChangeRow(GitChange file, bool included, Action<ChangeRow> changed) { File = file; _included = included; _changed = changed; }
    public void Update(GitChange file) { if (file == File) return; File = file; Notify(nameof(Status)); Notify(nameof(StatusBrush)); }
    private void Notify(string property) => PropertyChanged?.Invoke(this, new(property));
}

public sealed class ChangedFilesView : Grid
{
    private readonly ListView _list = new() { SelectionMode = ListViewSelectionMode.Single, Padding = new Thickness(0) };
    private readonly TextBlock _count;
    private readonly CheckBox _all;
    private readonly TextBox _filter = GitTheme.Input("Filter changed files");
    private readonly Dictionary<string, ChangeRow> _rows = new(StringComparer.Ordinal);
    private ChangeRow[] _visible = [];
    private GitChange[] _files = [];
    private string _active = "";
    private bool _updating;
    public event EventHandler<string>? FileSelected;
    public event EventHandler? SelectionChanged;
    public string[] SelectedPaths => _files.Where(f => _rows[f.Path].Included).Select(f => f.Path).ToArray();
    public string Filter { get => _filter.Text; set => _filter.Text = value; }
    public ChangedFilesView()
    {
        RowDefinitions.Add(new() { Height = new GridLength(35) }); RowDefinitions.Add(new() { Height = new GridLength(38) }); RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        var header = new Grid { Padding = new Thickness(10, 0, 10, 0), Background = GitTheme.Brush(GitTheme.Current.Panel) };
        header.ColumnDefinitions.Add(new() { Width = new GridLength(28) }); header.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        _all = new CheckBox { MinWidth = 24, MinHeight = 24, IsThreeState = false, VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(_all, "Select all changed files"); header.Children.Add(_all);
        _count = GitTheme.Label("0 changed files", 12, bold: true); Grid.SetColumn(_count, 1); header.Children.Add(_count); Children.Add(header);
        _filter.Margin = new Thickness(10, 2, 10, 5); Grid.SetRow(_filter, 1); Children.Add(_filter); AutomationProperties.SetName(_filter, "Filter changed files");
        _list.ItemTemplate = (DataTemplate)XamlReader.Load("""
            <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
              <Grid MinHeight="30" ColumnSpacing="6" Padding="2,0,4,0">
                <Grid.ColumnDefinitions><ColumnDefinition Width="24"/><ColumnDefinition Width="*"/><ColumnDefinition Width="18"/></Grid.ColumnDefinitions>
                <CheckBox IsChecked="{Binding Included, Mode=TwoWay}" Padding="0" VerticalAlignment="Center" MinWidth="24" MinHeight="24" AutomationProperties.Name="{Binding CheckName}"/>
                <TextBlock Grid.Column="1" Text="{Binding Path}" FontSize="12" FontWeight="Normal" VerticalAlignment="Center" TextTrimming="CharacterEllipsis"/>
                <TextBlock Grid.Column="2" Text="{Binding Status}" Foreground="{Binding StatusBrush}" FontSize="15" VerticalAlignment="Center"/>
              </Grid>
            </DataTemplate>
            """);
        _list.ItemContainerStyle = RowStyle(30);
        Grid.SetRow(_list, 2); Children.Add(_list); AutomationProperties.SetName(_list, "Changed files");
        _filter.TextChanged += (_, _) => ReconcileVisible();
        _all.Checked += (_, _) => ToggleAll(true); _all.Unchecked += (_, _) => ToggleAll(false);
        _list.SelectionChanged += (_, _) => { if (!_updating && _list.SelectedItem is ChangeRow row) { _active = row.Path; FileSelected?.Invoke(this, row.Path); } };
    }
    internal static Style RowStyle(double height)
    {
        var style = new Style(typeof(ListViewItem));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(6, 0, 6, 0)));
        style.Setters.Add(new Setter(FrameworkElement.MinHeightProperty, height));
        style.Setters.Add(new Setter(FrameworkElement.HeightProperty, height));
        style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
        return style;
    }
    public void RestoreSelection(IEnumerable<string> selected)
    {
        var set = selected.ToHashSet(StringComparer.Ordinal); _updating = true;
        foreach (var row in _rows.Values) row.Included = set.Contains(row.Path);
        _updating = false; UpdateHeader(); SelectionChanged?.Invoke(this, EventArgs.Empty);
    }
    private void ToggleAll(bool value)
    {
        if (_updating) return; _updating = true;
        foreach (var row in _rows.Values) row.Included = value;
        _updating = false; UpdateHeader(); SelectionChanged?.Invoke(this, EventArgs.Empty);
    }
    private void RowChanged(ChangeRow row) { if (_updating) return; UpdateHeader(); SelectionChanged?.Invoke(this, EventArgs.Empty); }
    private void UpdateHeader()
    {
        var selected = _rows.Values.Count(r => r.Included); _updating = true;
        _all.IsChecked = selected == 0 ? false : selected == _rows.Count ? true : null;
        _count.Text = _files.Length + " changed " + (_files.Length == 1 ? "file" : "files"); _updating = false;
    }
    public void SetFiles(GitChange[] files, string active, bool newRepository = false)
    {
        _updating = true; if (newRepository) _rows.Clear();
        var paths = files.Select(f => f.Path).ToHashSet(StringComparer.Ordinal);
        foreach (var path in _rows.Keys.Where(k => !paths.Contains(k)).ToArray()) _rows.Remove(path);
        foreach (var file in files)
        {
            if (_rows.TryGetValue(file.Path, out var row)) row.Update(file);
            else _rows[file.Path] = new(file, true, RowChanged);
        }
        _files = files; _active = active; _updating = false; ReconcileVisible(); UpdateHeader();
    }
    private void ReconcileVisible()
    {
        var visible = _files.Where(f => f.Path.Contains(_filter.Text, StringComparison.OrdinalIgnoreCase)).Select(f => _rows[f.Path]).ToArray();
        _updating = true;
        // No ItemsSource reset for a status-only refresh: preserve containers, scroll and focus.
        if (!_visible.SequenceEqual(visible)) { _visible = visible; _list.ItemsSource = visible; }
        _list.SelectedItem = _rows.GetValueOrDefault(_active); _updating = false;
    }
}

public sealed class HistoryRow(GitCommit commit)
{
    public GitCommit Commit { get; } = commit;
    public string Summary => Commit.Summary;
    public string Metadata { get; } = commit.Author + " · " + (DateTimeOffset.TryParse(commit.Date, out var date) ? date.ToLocalTime().ToString("MMM d, HH:mm") : commit.Date);
    public string Search { get; } = commit.Message + " " + commit.Author + " " + commit.Id;
}
public sealed class HistoryView : Grid
{
    private readonly ListView _list = new() { SelectionMode = ListViewSelectionMode.Single, Padding = new Thickness(0) };
    private readonly TextBox _filter = GitTheme.Input("Filter history");
    private readonly GitButton _more;
    private HistoryRow[] _rows = [], _visible = [];
    private bool _updating;
    public event EventHandler<GitCommit>? CommitSelected;
    public event EventHandler? LoadMoreRequested;
    public HistoryView()
    {
        RowDefinitions.Add(new() { Height = new GridLength(40) }); RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) }); RowDefinitions.Add(new() { Height = GridLength.Auto });
        _filter.Margin = new Thickness(10, 5, 10, 5); Children.Add(_filter); Grid.SetRow(_list, 1); Children.Add(_list);
        _more = new GitButton("Load 200 more commits", () => LoadMoreRequested?.Invoke(this, EventArgs.Empty)) { Margin = new Thickness(10, 5, 10, 5), HorizontalAlignment = HorizontalAlignment.Stretch, Visibility = Visibility.Collapsed };
        Grid.SetRow(_more, 2); Children.Add(_more);
        _list.ItemTemplate = (DataTemplate)XamlReader.Load("""
            <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
              <StackPanel Padding="6,7" Spacing="5">
                <TextBlock Text="{Binding Summary}" FontSize="12" FontWeight="SemiBold" TextTrimming="CharacterEllipsis"/>
                <TextBlock Text="{Binding Metadata}" FontSize="10" Opacity="0.7" TextTrimming="CharacterEllipsis"/>
              </StackPanel>
            </DataTemplate>
            """);
        _list.ItemContainerStyle = ChangedFilesView.RowStyle(54);
        AutomationProperties.SetName(_filter, "Filter commit history"); AutomationProperties.SetName(_list, "Commit history");
        _filter.TextChanged += (_, _) => Reconcile();
        _list.SelectionChanged += (_, _) => { if (!_updating && _list.SelectedItem is HistoryRow row) CommitSelected?.Invoke(this, row.Commit); };
    }
    public void SetCommits(GitCommit[] commits, bool hasMore = false)
    {
        _more.Visibility = hasMore && commits.Length < 2000 ? Visibility.Visible : Visibility.Collapsed;
        if (_rows.Select(r => r.Commit.Id).SequenceEqual(commits.Select(c => c.Id))) return;
        var existing = _rows.ToDictionary(r => r.Commit.Id); _rows = commits.Select(c => existing.GetValueOrDefault(c.Id) ?? new HistoryRow(c)).ToArray(); Reconcile();
    }
    private void Reconcile()
    {
        var selected = _list.SelectedItem as HistoryRow; var visible = _rows.Where(r => r.Search.Contains(_filter.Text, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (_visible.SequenceEqual(visible)) return;
        _updating = true; _visible = visible; _list.ItemsSource = visible;
        _list.SelectedItem = selected is not null ? visible.FirstOrDefault(r => r.Commit.Id == selected.Commit.Id) : null; _updating = false;
    }
}
