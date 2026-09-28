using System.Diagnostics;
using GitSpace.Diff;
using SkiaSharp;

namespace GitSpace.Rendering.Skia;

public sealed record DiffPalette(SKColor Background, SKColor Text, SKColor Muted, SKColor Border, SKColor Added, SKColor Removed, SKColor AddedEmphasis, SKColor RemovedEmphasis, SKColor Selection)
{
    public static DiffPalette Dark { get; } = new(C("#1e1e1e"), C("#d4d4d4"), C("#858585"), C("#353535"), C("#203b2d"), C("#482b2e"), C("#2e6540"), C("#873d44"), C("#264f78"));
    public static DiffPalette Light { get; } = new(C("#ffffff"), C("#24292f"), C("#6e7781"), C("#d8dee4"), C("#e6ffec"), C("#ffebe9"), C("#abf2bc"), C("#ffcecb"), C("#ddf4ff"));
    private static SKColor C(string hex) => SKColor.Parse(hex);
}
public sealed class DiffViewport
{
    public double ScrollY { get; set; }
    public double ScrollX { get; set; }
    public float FontSize { get; set; } = 12;
    public float RowHeight => MathF.Ceiling(FontSize * 1.62f);
    public bool Split { get; set; }
    public int SelectedRow { get; set; } = -1;
    public DiffPalette Palette { get; set; } = DiffPalette.Dark;
}
/// <summary>Draws only visible rows. No UI framework, process access or repository state is required.</summary>
public sealed class DiffRenderer : IDisposable
{
    public static SKTypeface? DefaultTypeface { get; set; }
    private readonly SKPaint _fill = new() { IsAntialias = false };
    private readonly SKPaint _text = new() { IsAntialias = true };
    private readonly SKPaint _rule = new() { IsAntialias = false, StrokeWidth = 1 };
    private SKFont? _font;
    private SKTypeface? _ownedTypeface;
    private DiffDocument? _document;
    private IReadOnlyList<SplitLine> _split = [];
    public long FramesRendered { get; private set; }
    public int LastVisibleRows { get; private set; }
    public double LastFrameMilliseconds { get; private set; }
    public DiffDocument? Document => _document;
    public void SetDocument(DiffDocument? document) { _document = document; _split = document?.Split() ?? []; }
    public int RowCount(bool split) => split ? _split.Count : _document?.Lines.Count ?? 0;
    public void Draw(SKCanvas canvas, SKRect bounds, DiffViewport view)
    {
        var watch = Stopwatch.StartNew(); FramesRendered++; var p = view.Palette;
        canvas.Save(); canvas.ClipRect(bounds); canvas.Clear(p.Background);
        _font ??= new SKFont(DefaultTypeface ?? (_ownedTypeface = SKTypeface.FromFamilyName("monospace")), view.FontSize) { Subpixel = true, Edging = SKFontEdging.Antialias };
        _font.Size = view.FontSize;
        var count = RowCount(view.Split); var (first, last) = DiffEngine.VisibleRange(count, view.ScrollY, bounds.Height, view.RowHeight); LastVisibleRows = last - first;
        for (var row = first; row < last; row++)
        {
            var top = bounds.Top + row * view.RowHeight - (float)view.ScrollY;
            if (view.Split)
            {
                var split = _split[row]; var half = bounds.Width / 2;
                Cell(canvas, split.Left, new(bounds.Left, top, bounds.Left + half, top + view.RowHeight), view, true, row == view.SelectedRow);
                Cell(canvas, split.Right, new(bounds.Left + half, top, bounds.Right, top + view.RowHeight), view, false, row == view.SelectedRow);
                if (split.Left?.Kind == DiffKind.Deletion && split.Right?.Kind == DiffKind.Addition)
                {
                    var span = DiffEngine.ChangedSpan(split.Left.Text, split.Right.Text);
                    Emphasis(canvas, split.Left.Text, span.Start, span.OldLength, bounds.Left + 65 - (float)view.ScrollX, top, bounds.Left + 65, bounds.Left + half, p.RemovedEmphasis, view);
                    Emphasis(canvas, split.Right.Text, span.Start, span.NewLength, bounds.Left + half + 65 - (float)view.ScrollX, top, bounds.Left + half + 65, bounds.Right, p.AddedEmphasis, view);
                    // Draw the foreground again over the intraline highlight without rebuilding shaped row data.
                    TextOnly(canvas, split.Left, new(bounds.Left, top, bounds.Left + half, top + view.RowHeight), view, 65);
                    TextOnly(canvas, split.Right, new(bounds.Left + half, top, bounds.Right, top + view.RowHeight), view, 65);
                }
            }
            else Cell(canvas, _document!.Lines[row], new(bounds.Left, top, bounds.Right, top + view.RowHeight), view, null, row == view.SelectedRow);
        }
        _rule.Color = p.Border;
        if (view.Split) canvas.DrawLine(bounds.MidX, bounds.Top, bounds.MidX, bounds.Bottom, _rule);
        if (count == 0) { _text.Color = p.Muted; canvas.DrawText("No text changes to display", bounds.Left + 24, bounds.Top + 38, SKTextAlign.Left, _font, _text); }
        canvas.Restore(); LastFrameMilliseconds = watch.Elapsed.TotalMilliseconds;
    }
    private void Cell(SKCanvas canvas, DiffLine? line, SKRect rect, DiffViewport view, bool? oldSide, bool selected)
    {
        var p = view.Palette; var gutter = oldSide.HasValue ? 65f : 100f;
        _fill.Color = selected ? p.Selection : line?.Kind switch { DiffKind.Addition => p.Added, DiffKind.Deletion => p.Removed, _ => p.Background };
        canvas.DrawRect(rect, _fill);
        if (line is null) return;
        _text.Color = p.Muted; var baseline = rect.Top + (view.RowHeight + view.FontSize) / 2 - 2;
        if (oldSide is null)
        {
            if (line.OldLine != 0) canvas.DrawText(line.OldLine.ToString(), rect.Left + 37, baseline, SKTextAlign.Right, _font!, _text);
            if (line.NewLine != 0) canvas.DrawText(line.NewLine.ToString(), rect.Left + 76, baseline, SKTextAlign.Right, _font!, _text);
            canvas.DrawText(line.Kind == DiffKind.Addition ? "+" : line.Kind == DiffKind.Deletion ? "−" : "", rect.Left + 87, baseline, SKTextAlign.Left, _font!, _text);
        }
        else
        {
            var number = oldSide.Value ? line.OldLine : line.NewLine;
            if (number != 0) canvas.DrawText(number.ToString(), rect.Left + 39, baseline, SKTextAlign.Right, _font!, _text);
            canvas.DrawText(line.Kind == DiffKind.Addition ? "+" : line.Kind == DiffKind.Deletion ? "−" : "", rect.Left + 49, baseline, SKTextAlign.Left, _font!, _text);
        }
        TextOnly(canvas, line, rect, view, gutter);
    }
    private void TextOnly(SKCanvas canvas, DiffLine line, SKRect rect, DiffViewport view, float gutter)
    {
        canvas.Save(); canvas.ClipRect(new SKRect(rect.Left + gutter, rect.Top, rect.Right, rect.Bottom));
        _text.Color = view.Palette.Text;
        var value = line.Text.Length > 12000 ? line.Text[..12000] + "  … [line clipped]" : line.Text;
        canvas.DrawText(value.Replace("\t", "    "), rect.Left + gutter - (float)view.ScrollX, rect.Top + (view.RowHeight + view.FontSize) / 2 - 2, SKTextAlign.Left, _font!, _text);
        canvas.Restore();
    }
    private void Emphasis(SKCanvas canvas, string value, int start, int length, float x, float y, float left, float right, SKColor color, DiffViewport view)
    {
        if (length == 0 || start + length > 12000) return;
        var prefix = value[..start].Replace("\t", "    "); var part = value.Substring(start, length).Replace("\t", "    ");
        var widthBefore = _font!.MeasureText(prefix); var width = _font.MeasureText(part);
        canvas.Save(); canvas.ClipRect(new SKRect(left, y, right, y + view.RowHeight)); _fill.Color = color;
        canvas.DrawRect(x + widthBefore, y, width, view.RowHeight, _fill); canvas.Restore();
    }
    public void Dispose() { _font?.Dispose(); _ownedTypeface?.Dispose(); _fill.Dispose(); _text.Dispose(); _rule.Dispose(); }
}
