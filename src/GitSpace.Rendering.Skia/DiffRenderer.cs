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
    public IReadOnlySet<int>? SelectedRows { get; set; }
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
    private SKTypeface? _ownedTypeface, _configuredTypeface;
    private readonly TextRunCache _textRuns = new();
    private bool _disposed;
    public long TextPreparationCount => _textRuns.Preparations;
    public long TextCacheHits => _textRuns.Hits;
    public int CachedTextRuns => _textRuns.Count;
    public int CachedTextCharacters => _textRuns.Characters;
    public int LastTextDraws { get; private set; }
    private DiffDocument? _document;
    private IReadOnlyList<SplitLine>? _split;
    public long FramesRendered { get; private set; }
    public int LastVisibleRows { get; private set; }
    public double LastFrameMilliseconds { get; private set; }
    public DiffDocument? Document => _document;
    public void SetDocument(DiffDocument? document)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (ReferenceEquals(_document, document)) return;
        _textRuns.Clear(); _document = document; _split = null;
    }
    public int RowCount(bool split) => split ? (_split ??= _document?.Split() ?? []).Count : _document?.Lines.Count ?? 0;
    public void Draw(SKCanvas canvas, SKRect bounds, DiffViewport view)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(canvas); ArgumentNullException.ThrowIfNull(view);
        if (!float.IsFinite(view.FontSize) || view.FontSize <= 0) throw new ArgumentOutOfRangeException(nameof(view));
        EnsureFont(view.FontSize);
        var watch = Stopwatch.StartNew(); FramesRendered++; LastTextDraws = 0; var p = view.Palette;
        canvas.Save();
        try
        {
            canvas.ClipRect(bounds); canvas.Clear(p.Background);
            var count = RowCount(view.Split); var (first, last) = DiffEngine.VisibleRange(count, view.ScrollY, bounds.Height, view.RowHeight); LastVisibleRows = last - first;
            for (var row = first; row < last; row++)
            {
                var top = bounds.Top + row * view.RowHeight - (float)view.ScrollY;
                if (view.Split)
                {
                    var split = _split![row]; var half = bounds.Width / 2;
                    var selected = view.SelectedRows?.Contains(row) == true || row == view.SelectedRow;
                    var span = split.Left?.Kind == DiffKind.Deletion && split.Right?.Kind == DiffKind.Addition
                        ? DiffEngine.ChangedSpan(split.Left.Text, split.Right.Text) : (0, 0, 0);
                    Cell(canvas, split.Left, new(bounds.Left, top, bounds.Left + half, top + view.RowHeight), view, true, selected, span.Item1, span.Item2);
                    Cell(canvas, split.Right, new(bounds.Left + half, top, bounds.Right, top + view.RowHeight), view, false, selected, span.Item1, span.Item3);
                }
                else Cell(canvas, _document!.Lines[row], new(bounds.Left, top, bounds.Right, top + view.RowHeight), view, null, (view.SelectedRows?.Contains(row) == true || row == view.SelectedRow));
            }
            _rule.Color = p.Border;
            if (view.Split) canvas.DrawLine(bounds.MidX, bounds.Top, bounds.MidX, bounds.Bottom, _rule);
            if (count == 0) { _text.Color = p.Muted; canvas.DrawText("No text changes to display", bounds.Left + 24, bounds.Top + 38, SKTextAlign.Left, _font, _text); }
        }
        finally { canvas.Restore(); LastFrameMilliseconds = watch.Elapsed.TotalMilliseconds; }
    }
    private void EnsureFont(float size)
    {
        if (_font is not null && ReferenceEquals(_configuredTypeface, DefaultTypeface))
        {
            if (_font.Size != size) { _textRuns.Clear(); _font.Size = size; }
            return;
        }
        _textRuns.Clear(); _font?.Dispose(); _ownedTypeface?.Dispose(); _ownedTypeface = null;
        _configuredTypeface = DefaultTypeface;
        _font = new SKFont(_configuredTypeface ?? (_ownedTypeface = SKTypeface.FromFamilyName("monospace")), size)
            { Subpixel = true, Edging = SKFontEdging.Antialias };
    }
    private void Cell(SKCanvas canvas, DiffLine? line, SKRect rect, DiffViewport view, bool? oldSide, bool selected, int spanStart = 0, int spanLength = 0)
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
        var run = _textRuns.Get(line.Text, _font!);
        canvas.Save();
        try
        {
            canvas.ClipRect(new SKRect(rect.Left + gutter, rect.Top, rect.Right, rect.Bottom));
            var x = rect.Left + gutter - (float)view.ScrollX;
            if (spanLength > 0)
            {
                var span = run.MeasureSpan(_font!, spanStart, spanLength);
                _fill.Color = oldSide == true ? p.RemovedEmphasis : p.AddedEmphasis;
                canvas.DrawRect(x + span.Left, rect.Top, span.Width, view.RowHeight, _fill);
            }
            _text.Color = p.Text;
            if (run.Blob is not null) { canvas.DrawText(run.Blob, x, baseline, _text); LastTextDraws++; }
        }
        finally { canvas.Restore(); }
    }
    public void Dispose()
    {
        if (_disposed) return; _disposed = true;
        _textRuns.Dispose(); _font?.Dispose(); _ownedTypeface?.Dispose(); _fill.Dispose(); _text.Dispose(); _rule.Dispose();
        _document = null; _split = null;
    }
}
