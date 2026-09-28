using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace GitSpace.Controls.Uno;

public sealed class PaneSplitter : Thumb
{
    public PaneSplitter(ColumnDefinition column)
    {
        Width = 4; HorizontalAlignment = HorizontalAlignment.Stretch; VerticalAlignment = VerticalAlignment.Stretch;
        Background = GitTheme.Brush(GitTheme.Current.Border); MinWidth = 4;
        AutomationProperties.SetName(this, "Resize repository sidebar");
        DragDelta += (_, e) => column.Width = new GridLength(Math.Clamp(column.ActualWidth + e.HorizontalChange, 230, 520));
    }
}
