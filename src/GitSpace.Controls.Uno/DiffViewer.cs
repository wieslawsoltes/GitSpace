using GitSpace.Diff;
using GitSpace.Rendering.Skia;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using SkiaSharp;
using Uno.WinUI.Graphics2DSK;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.System;

namespace GitSpace.Controls.Uno;

public sealed class DiffViewer : Grid, IDisposable
{
    private sealed class Surface(DiffViewer owner) : SKCanvasElement
    {
        protected override void RenderOverride(SKCanvas canvas, Size area)
        {
            owner.Renderer.Draw(canvas, new SKRect(0, 0, (float)area.Width, (float)area.Height), owner.Viewport);
            owner.Rendered?.Invoke(owner, EventArgs.Empty);
        }
    }
    private readonly Surface _surface;
    private readonly ScrollBar _vertical, _horizontal;
    private bool _updating;
    private int _searchRow = -1, _longestLine;
    private readonly HashSet<int> _selected = [];
    private int _anchor = -1;
    public event EventHandler? SelectionChanged;
    public int[] SelectedChangedRows => _selected.Where(i => Renderer.Document is { } d && i >= 0 && i < d.Lines.Count && d.Lines[i].Kind != DiffKind.Context).Order().ToArray();
    public DiffRenderer Renderer { get; } = new();
    public DiffViewport Viewport { get; } = new();
    public event EventHandler? Rendered;
    public DiffViewer()
    {
        Background = GitTheme.Brush(GitTheme.Current.Surface); IsTabStop = true; Viewport.SelectedRows = _selected;
        AutomationProperties.SetName(this, "Diff viewer"); AutomationProperties.SetHelpText(this, "Use arrow keys, Page Up, Page Down, Home and End to navigate. Copy selected line with Control+C. Full screen-reader text navigation is not yet provided.");
        ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); ColumnDefinitions.Add(new() { Width = new GridLength(12) });
        RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) }); RowDefinitions.Add(new() { Height = new GridLength(12) });
        _surface = new Surface(this); Children.Add(_surface);
        _vertical = new ScrollBar { Orientation = Orientation.Vertical, Width = 12, SmallChange = 40, Minimum = 0 }; Grid.SetColumn(_vertical, 1); Children.Add(_vertical);
        _horizontal = new ScrollBar { Orientation = Orientation.Horizontal, Height = 12, SmallChange = 40, Minimum = 0 }; Grid.SetRow(_horizontal, 1); Children.Add(_horizontal);
        _vertical.ValueChanged += (_, e) => { if (!_updating) { Viewport.ScrollY = e.NewValue; _surface.Invalidate(); } };
        _horizontal.ValueChanged += (_, e) => { if (!_updating) { Viewport.ScrollX = e.NewValue; _surface.Invalidate(); } };
        SizeChanged += (_, _) => UpdateScrollbars();
        _surface.PointerWheelChanged += (_, e) =>
        {
            var p = e.GetCurrentPoint(_surface); Viewport.ScrollY -= p.Properties.MouseWheelDelta / 120d * Viewport.RowHeight * 3;
            UpdateScrollbars(); e.Handled = true;
        };
        _surface.PointerPressed += (_, e) =>
        {
            Focus(FocusState.Pointer); var point = e.GetCurrentPoint(_surface).Position;
            var row = Math.Clamp((int)((point.Y + Viewport.ScrollY) / Viewport.RowHeight), 0, Math.Max(0, Renderer.RowCount(Viewport.Split) - 1));
            if ((e.KeyModifiers & VirtualKeyModifiers.Shift) != 0 && _anchor >= 0)
            {
                _selected.Clear(); for (var i = Math.Min(_anchor, row); i <= Math.Max(_anchor, row); i++) _selected.Add(i);
            }
            else if ((e.KeyModifiers & VirtualKeyModifiers.Control) != 0) { if (!_selected.Add(row)) _selected.Remove(row); _anchor = row; }
            else { _selected.Clear(); _selected.Add(row); _anchor = row; }
            Viewport.SelectedRow = row; SelectionChanged?.Invoke(this, EventArgs.Empty);
            _surface.Invalidate(); e.Handled = true;
        };
        KeyDown += (_, e) =>
        {
            var delta = e.Key switch { VirtualKey.Down => Viewport.RowHeight, VirtualKey.Up => -Viewport.RowHeight, VirtualKey.PageDown => (float)ActualHeight - Viewport.RowHeight, VirtualKey.PageUp => -(float)ActualHeight + Viewport.RowHeight, _ => 0 };
            if (delta != 0) { Viewport.ScrollY += delta; UpdateScrollbars(); e.Handled = true; }
            else if (e.Key is VirtualKey.Home or VirtualKey.End) { Viewport.ScrollY = e.Key == VirtualKey.Home ? 0 : double.MaxValue; UpdateScrollbars(); e.Handled = true; }
        };
        var copy = new KeyboardAccelerator { Key = VirtualKey.C, Modifiers = VirtualKeyModifiers.Control };
        copy.Invoked += (_, e) => { CopySelection(); e.Handled = true; }; KeyboardAccelerators.Add(copy);
        Loaded += (_, _) => UpdateScrollbars();
    }
    public void SetDocument(DiffDocument? document)
    {
        _selected.Clear(); _anchor = -1; Renderer.SetDocument(document); _longestLine = document is { Lines.Count: > 0 } ? document.Lines.Max(l => l.Text.Length) : 0;
        Viewport.ScrollX = Viewport.ScrollY = 0; Viewport.SelectedRow = -1; _searchRow = -1; UpdateScrollbars();
        AutomationProperties.SetHelpText(this, document is null ? "No diff loaded" : $"{document.Additions} additions, {document.Deletions} deletions, {document.Lines.Count} lines. Use Copy entire diff for accessible text.");
    }
    public void SetSplit(bool value) { Viewport.Split = value; Viewport.SelectedRow = -1; UpdateScrollbars(); }
    public void ApplyTheme() { Viewport.Palette = GitTheme.Current.IsDark ? DiffPalette.Dark : DiffPalette.Light; Background = GitTheme.Brush(GitTheme.Current.Surface); _surface.Invalidate(); }
    public void Zoom(float delta) { Viewport.FontSize = Math.Clamp(Viewport.FontSize + delta, 9, 24); UpdateScrollbars(); }
    public bool FindNext(string query)
    {
        if (string.IsNullOrEmpty(query) || Renderer.Document is not { } document) return false;
        for (var n = 1; n <= document.Lines.Count; n++)
        {
            var row = (_searchRow + n) % document.Lines.Count;
            if (!document.Lines[row].Text.Contains(query, StringComparison.OrdinalIgnoreCase)) continue;
            _searchRow = row; SetSplit(false); Viewport.SelectedRow = row; Viewport.ScrollY = row * Viewport.RowHeight; UpdateScrollbars(); return true;
        }
        return false;
    }
    public void CopySelection(bool all = false)
    {
        if (Renderer.Document is not { } document) return;
        var rows = document.Lines;
        var text = all || Viewport.Split || Viewport.SelectedRow < 0 || Viewport.SelectedRow >= rows.Count
            ? string.Join('\n', rows.Select(l => (l.Kind == DiffKind.Addition ? "+" : l.Kind == DiffKind.Deletion ? "-" : " ") + l.Text))
            : rows[Viewport.SelectedRow].Text;
        var data = new DataPackage(); data.SetText(text); Clipboard.SetContent(data);
    }
    private void UpdateScrollbars()
    {
        _updating = true;
        var height = Math.Max(0, ActualHeight - 12); var width = Math.Max(0, ActualWidth - 12);
        _vertical.Maximum = Math.Max(0, Renderer.RowCount(Viewport.Split) * Viewport.RowHeight - height);
        _vertical.ViewportSize = height; _vertical.LargeChange = Math.Max(20, height - 20);
        Viewport.ScrollY = Math.Clamp(Viewport.ScrollY, 0, _vertical.Maximum); _vertical.Value = Viewport.ScrollY;
        // Width is derived once per document, not by scanning the file on every wheel event.
        _horizontal.Maximum = Math.Max(0, Math.Min(_longestLine, 12000) * Viewport.FontSize * .65 + 110 - (Viewport.Split ? width / 2 : width));
        _horizontal.ViewportSize = width; Viewport.ScrollX = Math.Clamp(Viewport.ScrollX, 0, _horizontal.Maximum); _horizontal.Value = Viewport.ScrollX;
        _updating = false; _surface.Invalidate();
    }
    public new void Dispose() => Renderer.Dispose();
}
