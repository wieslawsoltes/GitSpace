using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace GitSpace.Controls.Uno;

/// <summary>A keyboard-accessible, compact button with Git-client chrome rather than the default Fluent visual treatment.</summary>
public class GitButton : Button
{
    private Brush? _normal;
    public bool Primary { get; }
    public GitButton(string text, Action? action = null, bool primary = false, string? accessibleName = null)
    {
        Primary = primary; Template = GitTheme.ButtonTemplate(); Content = text; FontSize = 12; MinHeight = 28; MinWidth = 28;
        Padding = new Thickness(10, 4, 10, 4); BorderThickness = new Thickness(1); CornerRadius = new CornerRadius(3);
        HorizontalContentAlignment = HorizontalAlignment.Center; VerticalContentAlignment = VerticalAlignment.Center;
        Background = GitTheme.Brush(primary ? "#0969da" : GitTheme.Current.Elevated); _normal = Background;
        Foreground = GitTheme.Brush(primary ? "#ffffff" : GitTheme.Current.Text); BorderBrush = GitTheme.Brush(primary ? "#0874ed" : GitTheme.Current.Border);
        AutomationProperties.SetName(this, accessibleName ?? text);
        PointerEntered += (_, _) => { if (IsEnabled) Background = GitTheme.Brush(primary ? "#1f7ae0" : GitTheme.Current.Hover); };
        PointerExited += (_, _) => Background = _normal;
        IsEnabledChanged += (_, _) => Opacity = IsEnabled ? 1 : 0.45;
        if (action is not null) Click += (_, _) => action();
    }
    public void SetFlat(string? background = null)
    {
        Background = GitTheme.Brush(background ?? GitTheme.Current.Panel); _normal = Background; BorderThickness = new Thickness(0);
    }
    public void SetSelected(bool selected)
    {
        Background = GitTheme.Brush(selected ? GitTheme.Current.Selection : GitTheme.Current.Panel); _normal = Background;
    }
}

/// <summary>Reusable two-line repository, branch, or synchronization tile.</summary>
public sealed class RepositoryTile : GitButton
{
    private readonly TextBlock _caption, _value;
    public RepositoryTile(string icon, string caption, string value, Action action, double width = double.NaN) : base(caption, action, accessibleName: caption)
    {
        Height = 60; Width = width; Padding = new Thickness(16, 8, 12, 8); HorizontalContentAlignment = HorizontalAlignment.Stretch;
        HorizontalAlignment = HorizontalAlignment.Stretch; VerticalAlignment = VerticalAlignment.Stretch;
        SetFlat("#24292e"); BorderThickness = new Thickness(0, 0, 1, 0); BorderBrush = GitTheme.Brush("#15191d");
        var grid = new Grid(); grid.ColumnDefinitions.Add(new() { Width = new GridLength(28) }); grid.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); grid.ColumnDefinitions.Add(new() { Width = new GridLength(14) });
        var glyph = new GitIcon(icon == "⑂" ? GitIconKind.Branch : icon == "↻" ? GitIconKind.Sync : GitIconKind.Repository) { Width = 20, Height = 20, Foreground = GitTheme.Brush("#ffffff"), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left }; grid.Children.Add(glyph);
        var labels = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center }; Grid.SetColumn(labels, 1);
        _caption = GitTheme.Label(caption, 10); _caption.Foreground = GitTheme.Brush("#b8bdc4");
        _value = GitTheme.Label(value, 14, bold: true); _value.Foreground = GitTheme.Brush("#ffffff"); labels.Children.Add(_caption); labels.Children.Add(_value); grid.Children.Add(labels);
        var arrow = GitTheme.Label("⌄", 15); arrow.Foreground = GitTheme.Brush("#b8bdc4"); Grid.SetColumn(arrow, 2); grid.Children.Add(arrow); Content = grid;
    }
    public void SetValue(string value, string? caption = null)
    {
        _value.Text = value; if (caption is not null) _caption.Text = caption;
        AutomationProperties.SetName(this, _caption.Text + ": " + value);
    }
}
