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
        RowDefinitions.Add(new() { Height = new GridLength(72) }); RowDefinitions.Add(new() { Height = GridLength.Auto });
        _identity = GitTheme.Label("●  GitSpace User", 11, true); Children.Add(_identity);
        Grid.SetRow(Summary, 1); Children.Add(Summary); AutomationProperties.SetName(Summary, "Commit summary");
        Description.MinHeight = 64; Grid.SetRow(Description, 2); Children.Add(Description); AutomationProperties.SetName(Description, "Commit description");
        CommitButton = new GitButton("Commit to main", () => CommitRequested?.Invoke(this, EventArgs.Empty), true) { HorizontalAlignment = HorizontalAlignment.Stretch, MinHeight = 30 };
        Grid.SetRow(CommitButton, 3); Children.Add(CommitButton); CommitButton.IsEnabled = false;
        Summary.TextChanged += (_, _) => UpdateAvailability();
    }
    private bool _allowed;
    public string Message => Summary.Text.Trim() + (string.IsNullOrWhiteSpace(Description.Text) ? "" : "\n\n" + Description.Text.Trim());
    public void SetState(string branch, int selected, bool busy, string author)
    {
        CommitButton.Content = "Commit to " + branch; AutomationProperties.SetName(CommitButton, "Commit to " + branch);
        _identity.Text = "●  " + author + (selected == 0 ? "  ·  No files selected" : "");
        _allowed = selected > 0 && !busy; UpdateAvailability();
    }
    private void UpdateAvailability() => CommitButton.IsEnabled = _allowed && !string.IsNullOrWhiteSpace(Summary.Text);
    public void Clear() { Summary.Text = ""; Description.Text = ""; }
}
