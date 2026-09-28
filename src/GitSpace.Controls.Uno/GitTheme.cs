using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;

namespace GitSpace.Controls.Uno;

public sealed record GitTheme(bool IsDark, string Surface, string Panel, string Elevated, string Text, string Muted, string Border, string Hover, string Selection)
{
    public static GitTheme Dark { get; } = new(true, "#1e1e1e", "#252526", "#2d2d30", "#e5e5e5", "#a3a3a3", "#3c3c3c", "#383838", "#094771");
    public static GitTheme Light { get; } = new(false, "#ffffff", "#f6f8fa", "#ffffff", "#24292f", "#656d76", "#d0d7de", "#eaeef2", "#ddf4ff");
    public static GitTheme Current { get; set; } = Dark;
    public static FontFamily UiFont { get; } = new("ms-appx:///Uno.Fonts.OpenSans/Fonts/OpenSans-Regular.ttf");
    public static SolidColorBrush Brush(string color) => new(Windows.UI.Color.FromArgb(255, Convert.ToByte(color[1..3], 16), Convert.ToByte(color[3..5], 16), Convert.ToByte(color[5..7], 16)));
    public static TextBlock Label(string text, double size = 12, bool muted = false, bool bold = false) => new()
    {
        Text = text, FontSize = size, FontFamily = UiFont, Foreground = Brush(muted ? Current.Muted : Current.Text),
        FontWeight = bold ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal,
        VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis
    };
    public static TextBox Input(string placeholder, bool multiline = false) => new()
    {
        PlaceholderText = placeholder, FontFamily = UiFont, FontSize = 12, MinHeight = 30, Padding = new Thickness(8, 5, 8, 5),
        Background = Brush(Current.Surface), Foreground = Brush(Current.Text), BorderBrush = Brush(Current.Border), BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(3), AcceptsReturn = multiline, TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap,
        IsSpellCheckEnabled = false, HorizontalAlignment = HorizontalAlignment.Stretch
    };
    private static ControlTemplate? _buttonTemplate;
    internal static ControlTemplate ButtonTemplate() => _buttonTemplate ??= (ControlTemplate)XamlReader.Load("""
        <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="Button">
          <Border Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="{TemplateBinding BorderThickness}" CornerRadius="3">
            <ContentPresenter Content="{TemplateBinding Content}" ContentTemplate="{TemplateBinding ContentTemplate}" Foreground="{TemplateBinding Foreground}" FontFamily="{TemplateBinding FontFamily}" FontSize="{TemplateBinding FontSize}" Margin="{TemplateBinding Padding}" HorizontalAlignment="{TemplateBinding HorizontalContentAlignment}" VerticalAlignment="{TemplateBinding VerticalContentAlignment}" />
          </Border>
        </ControlTemplate>
        """);
}
