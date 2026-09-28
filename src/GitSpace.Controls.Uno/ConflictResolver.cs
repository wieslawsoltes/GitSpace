using GitSpace.Core;
using GitSpace.Diff;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace GitSpace.Controls.Uno;

/// <summary>Reusable three-way conflict editor. It does not read files or invoke Git.</summary>
public sealed class ConflictResolver : Grid
{
    private readonly GitResult _source;
    private readonly TextBox _result = GitTheme.Input("Resolved file contents", true);
    private readonly CheckBox _delete = new() { Content = "Resolve by deleting this file", MinHeight = 28, FontSize = 12 };
    public string ResolvedText => GitText.FromEditor(_result.Text, _source.Text);
    public bool DeleteFile => _delete.IsChecked == true;
    public ConflictResolver(GitResult source)
    {
        _source = source; MinWidth = 500; MaxWidth = 760; RowSpacing = 10;
        RowDefinitions.Add(new() { Height = GridLength.Auto }); RowDefinitions.Add(new() { Height = new GridLength(145) });
        RowDefinitions.Add(new() { Height = GridLength.Auto }); RowDefinitions.Add(new() { Height = new GridLength(190) }); RowDefinitions.Add(new() { Height = GridLength.Auto });
        var label = GitTheme.Label("Compare the index's current and incoming versions. Review the result before marking resolved.", 12, true); label.TextWrapping = TextWrapping.Wrap; Children.Add(label);
        var sides = new Grid { ColumnSpacing = 12 }; sides.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); sides.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        void Side(string title, string text, int column)
        {
            var panel = new Grid { RowSpacing = 4 }; panel.RowDefinitions.Add(new() { Height = GridLength.Auto }); panel.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
            panel.Children.Add(GitTheme.Label(title, 12, bold: true)); var box = GitTheme.Input(title, true); box.Text = text; box.IsReadOnly = true; box.TextWrapping = TextWrapping.NoWrap; AutomationProperties.SetName(box, title);
            Grid.SetRow(box, 1); panel.Children.Add(box); Grid.SetColumn(panel, column); sides.Children.Add(panel);
        }
        Side(source.BeforeExists ? "Current version" : "Current: file deleted", source.Before, 0);
        Side(source.AfterExists ? "Incoming version" : "Incoming: file deleted", source.After, 1); Grid.SetRow(sides, 1); Children.Add(sides);
        var commands = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        commands.Children.Add(new GitButton("Use current", () => Choose(ConflictChoice.Current)));
        commands.Children.Add(new GitButton("Use incoming", () => Choose(ConflictChoice.Incoming)));
        commands.Children.Add(new GitButton("Use both", () => Choose(ConflictChoice.Both)));
        Grid.SetRow(commands, 2); Children.Add(commands);
        _result.Text = source.Text; _result.TextWrapping = TextWrapping.NoWrap; AutomationProperties.SetName(_result, "Resolved file contents"); Grid.SetRow(_result, 3); Children.Add(_result);
        _delete.Visibility = !source.BeforeExists || !source.AfterExists ? Visibility.Visible : Visibility.Collapsed; Grid.SetRow(_delete, 4); Children.Add(_delete);
        _delete.Checked += (_, _) => _result.IsEnabled = false; _delete.Unchecked += (_, _) => _result.IsEnabled = true;
    }
    private void Choose(ConflictChoice choice)
    {
        // Preserve common surrounding content instead of concatenating complete files.
        var document = new ConflictDocument(_source.Text);
        var resolved = document.Blocks.Count > 0 ? document.Resolve(Enumerable.Repeat(choice, document.Blocks.Count).ToArray())
            : choice == ConflictChoice.Current ? _source.Before : choice == ConflictChoice.Incoming ? _source.After : _source.Before + _source.After;
        _result.Text = resolved;
        _delete.IsChecked = choice == ConflictChoice.Current && !_source.BeforeExists || choice == ConflictChoice.Incoming && !_source.AfterExists;
    }
}
