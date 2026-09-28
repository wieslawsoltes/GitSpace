using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Windows.System;

namespace GitSpace.Controls.Uno;

/// <summary>Resizable pane boundary using composition; WinUI's Thumb is sealed.</summary>
public sealed class PaneSplitter : Grid
{
    public PaneSplitter(ColumnDefinition column)
    {
        Width = 4; HorizontalAlignment = HorizontalAlignment.Stretch; VerticalAlignment = VerticalAlignment.Stretch;
        Background = GitTheme.Brush(GitTheme.Current.Border); MinWidth = 4; IsTabStop = true;
        AutomationProperties.SetName(this, "Resize repository sidebar");
        AutomationProperties.SetHelpText(this, "Drag or use the Left and Right arrow keys to resize.");
        var thumb = new Thumb { HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch };
        thumb.DragDelta += (_, e) => column.Width = new GridLength(Math.Clamp(column.ActualWidth + e.HorizontalChange, 230, 520));
        Children.Add(thumb);
        KeyDown += (_, e) =>
        {
            if (e.Key is not (VirtualKey.Left or VirtualKey.Right)) return;
            column.Width = new GridLength(Math.Clamp(column.ActualWidth + (e.Key == VirtualKey.Left ? -10 : 10), 230, 520)); e.Handled = true;
        };
    }
}
