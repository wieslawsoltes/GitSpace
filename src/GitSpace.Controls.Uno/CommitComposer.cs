using GitSpace.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace GitSpace.Controls.Uno;

/// <summary>Reusable commit authoring component. It never runs Git itself.</summary>
public sealed class CommitComposer : Grid
{
    public TextBox Summary { get; } = GitTheme.Input("Summary (required)");
    public TextBox Description { get; } = GitTheme.Input("Description", true);
    public GitButton CommitButton { get; }
    private readonly TextBlock _identity;
    public event EventHandler? CommitRequested;
    public CommitComposer()
    {
        Padding = new Thickness(12); RowSpacing = 8; Background = GitTheme.Brush(GitTheme.Current.Panel);
        RowDefinitions.Add(new() { Height = GridLength.Auto }); RowDefinitions.Add(new() { Height = GridLength.Auto });
        RowDefinitions.Add(new() { Height = new GridLength(72) }); RowDefinitions.Add(new() { Height = GridLength.Auto }); RowDefinitions.Add(new() { Height = GridLength.Auto });
        _identity = GitTheme.Label("●  GitSpace User", 11, true); Children.Add(_identity);
        Grid.SetRow(Summary, 1); Children.Add(Summary); AutomationProperties.SetName(Summary, "Commit summary");
        Description.MinHeight = 64; Grid.SetRow(Description, 2); Children.Add(Description); AutomationProperties.SetName(Description, "Commit description");
        CommitButton = new GitButton("Commit to main", () => CommitRequested?.Invoke(this, EventArgs.Empty), true) { HorizontalAlignment = HorizontalAlignment.Stretch, MinHeight = 30 };
        Grid.SetRow(StagedToggle, 3); Children.Add(StagedToggle);
        AutomationProperties.SetName(StagedToggle, "Commit staged changes only");
        StagedToggle.Checked += (_, _) => UpdateAvailability(); StagedToggle.Unchecked += (_, _) => UpdateAvailability();
        Grid.SetRow(CommitButton, 4); Children.Add(CommitButton); CommitButton.IsEnabled = false;
        Summary.TextChanged += (_, _) => UpdateAvailability();
    }
    private bool _allowed;
    private bool _busy;
    private int _selected, _staged;
    private string _branch = "main";
    public CheckBox StagedToggle { get; } = new() { Content = "Commit staged changes only", FontSize = 11, MinHeight = 26 };
    public bool StagedOnly { get => StagedToggle.IsChecked == true; set => StagedToggle.IsChecked = value; }
    public string Message => GitText.ToLf(Summary.Text.Trim() + (string.IsNullOrWhiteSpace(Description.Text) ? "" : "\n\n" + Description.Text.Trim()));
    public void SetState(string branch, int selected, bool busy, string author, int staged = 0)
    {
        CommitButton.Content = "Commit to " + branch; AutomationProperties.SetName(CommitButton, "Commit to " + branch);
        _identity.Text = "●  " + author + (selected == 0 ? "  ·  No files selected" : "");
        _selected = selected; _staged = staged; _busy = busy; _branch = branch; UpdateAvailability();
    }
    private void UpdateAvailability()
    {
        _allowed = !_busy && (StagedOnly ? _staged > 0 : _selected > 0);
        CommitButton.Content = (StagedOnly ? "Commit staged to " : "Commit to ") + _branch;
        AutomationProperties.SetName(CommitButton, (string)CommitButton.Content);
        CommitButton.IsEnabled = _allowed && !string.IsNullOrWhiteSpace(Summary.Text);
    }
    public void Clear() { Summary.Text = ""; Description.Text = ""; }
}
