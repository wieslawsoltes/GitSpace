using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace GitSpace.Controls.Uno;

public enum GitIconKind { Repository, Branch, Sync }

/// <summary>Small vector icons with no dependency on a platform icon font or trademark artwork.</summary>
public sealed class GitIcon : Grid
{
    private readonly Canvas _canvas = new() { Width = 20, Height = 20 };
    private Brush _foreground = GitTheme.Brush("#ffffff");
    public Brush Foreground { get => _foreground; set { _foreground = value; ApplyColor(); } }
    public GitIcon(GitIconKind kind)
    {
        Children.Add(new Viewbox { Child = _canvas, Stretch = Stretch.Uniform });
        if (kind == GitIconKind.Repository)
        {
            Line(4, 3, 16, 3); Line(16, 3, 16, 17); Line(16, 17, 4, 17); Line(4, 17, 4, 3); Line(7, 3, 7, 17); Line(10, 7, 13, 7); Line(10, 10, 13, 10);
        }
        else if (kind == GitIconKind.Branch)
        {
            Line(6, 5, 6, 15); Line(6, 12, 14, 8); Line(14, 8, 14, 5); Circle(6, 3.5); Circle(6, 16.5); Circle(14, 3.5);
        }
        else
        {
            Line(4, 8, 4, 5); Line(4, 5, 8, 3); Line(8, 3, 14, 4); Line(14, 4, 17, 8); Line(17, 8, 17, 4); Line(17, 8, 13, 8);
            Line(16, 12, 16, 15); Line(16, 15, 12, 17); Line(12, 17, 6, 16); Line(6, 16, 3, 12); Line(3, 12, 3, 16); Line(3, 12, 7, 12);
        }
        ApplyColor();
    }
    private void Line(double x1, double y1, double x2, double y2) => _canvas.Children.Add(new Microsoft.UI.Xaml.Shapes.Line { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, StrokeThickness = 1.5, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round });
    private void Circle(double x, double y)
    {
        var circle = new Ellipse { Width = 4, Height = 4, StrokeThickness = 1.5 }; Canvas.SetLeft(circle, x - 2); Canvas.SetTop(circle, y - 2); _canvas.Children.Add(circle);
    }
    private void ApplyColor() { foreach (var shape in _canvas.Children.OfType<Shape>()) shape.Stroke = _foreground; }
}
