using SkiaSharp;

namespace GitSpace.Rendering.Skia;

/// <summary>Render-thread-only LRU for prepared code lines. The renderer owns the font generation.</summary>
internal sealed class TextRunCache : IDisposable
{
    internal const int MaximumEntries = 512;
    internal const int MaximumCharacters = 262_144;
    internal const int MaximumLineLength = 12_000;
    private readonly Dictionary<string, LinkedListNode<Run>> _runs = new(StringComparer.Ordinal);
    private readonly LinkedList<Run> _recent = new();
    public long Preparations { get; private set; }
    public long Hits { get; private set; }
    public int Count => _runs.Count;
    public int Characters { get; private set; }

    internal sealed class Run(string source, string display, SKTextBlob? blob)
    {
        public string Source { get; } = source;
        public SKTextBlob? Blob { get; } = blob;
        // Count both key and expanded text conservatively, even when they share a string.
        public int Cost { get; } = source.Length + display.Length;
        private (int Start, int Length) _span = (-1, -1);
        private (float Left, float Width) _measure;
        public (float Left, float Width) MeasureSpan(SKFont font, int start, int length)
        {
            if (start < 0 || length <= 0 || start > Math.Min(Source.Length, MaximumLineLength) - length) return (0, 0);
            if (_span != (start, length))
            {
                _measure = (font.MeasureText(Source[..start].Replace("\t", "    ", StringComparison.Ordinal)),
                    font.MeasureText(Source.Substring(start, length).Replace("\t", "    ", StringComparison.Ordinal)));
                _span = (start, length);
            }
            return _measure;
        }
    }

    public Run Get(string text, SKFont font)
    {
        if (text.Length > MaximumLineLength)
        {
            var end = MaximumLineLength;
            if (char.IsHighSurrogate(text[end - 1]) && char.IsLowSurrogate(text[end])) end--;
            text = text[..end] + "  … [line clipped]";
        }
        if (_runs.TryGetValue(text, out var existing))
        {
            Hits++; _recent.Remove(existing); _recent.AddFirst(existing); return existing.Value;
        }
        var display = text.Replace("\t", "    ", StringComparison.Ordinal);
        var blob = display.Length == 0 ? null : SKTextBlob.Create(display, font)
            ?? throw new InvalidOperationException("Skia could not prepare a text run.");
        var run = new Run(text, display, blob);
        while (_recent.Last is { } oldest && (Count >= MaximumEntries || Characters + run.Cost > MaximumCharacters))
        {
            _recent.RemoveLast(); _runs.Remove(oldest.Value.Source);
            Characters -= oldest.Value.Cost; oldest.Value.Blob?.Dispose();
        }
        _runs.Add(text, _recent.AddFirst(run)); Characters += run.Cost; Preparations++; return run;
    }
    public void Clear()
    {
        foreach (var run in _recent) run.Blob?.Dispose();
        _recent.Clear(); _runs.Clear(); Characters = 0;
    }
    public void Dispose() => Clear();
}
