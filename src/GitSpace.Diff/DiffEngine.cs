namespace GitSpace.Diff;

public enum DiffKind { Context, Addition, Deletion }
public sealed record DiffLine(DiffKind Kind, string Text, int OldLine, int NewLine);
public sealed record SplitLine(DiffLine? Left, DiffLine? Right);
public sealed record DiffDocument(IReadOnlyList<DiffLine> Lines, bool Coarse, bool OldHasFinalNewline, bool NewHasFinalNewline)
{
    public int Additions => Lines.Count(x => x.Kind == DiffKind.Addition);
    public int Deletions => Lines.Count(x => x.Kind == DiffKind.Deletion);
    public IReadOnlyList<SplitLine> Split()
    {
        var result = new List<SplitLine>();
        for (var i = 0; i < Lines.Count;)
        {
            if (Lines[i].Kind == DiffKind.Context) { result.Add(new(Lines[i], Lines[i])); i++; continue; }
            var removed = new List<DiffLine>(); var added = new List<DiffLine>();
            while (i < Lines.Count && Lines[i].Kind != DiffKind.Context)
            {
                var line = Lines[i++]; (line.Kind == DiffKind.Deletion ? removed : added).Add(line);
            }
            for (var j = 0; j < Math.Max(removed.Count, added.Count); j++) result.Add(new(j < removed.Count ? removed[j] : null, j < added.Count ? added[j] : null));
        }
        return result;
    }
}
public static class DiffEngine
{
    private readonly record struct Edit(DiffKind Kind, string Text);
    /// <summary>Myers shortest edit script with a hard work/trace budget; a coarse replacement is returned rather than freezing the UI.</summary>
    public static DiffDocument Compare(string before, string after, bool ignoreWhitespace = false, int workBudget = 2_000_000, CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(before); ArgumentNullException.ThrowIfNull(after);
        return CompareTokens(Lines(before), Lines(after), EndsInNewline(before), EndsInNewline(after), ignoreWhitespace, workBudget, cancellation);
    }
    internal static DiffDocument CompareTokens(string[] a, string[] b, bool oldNewline, bool newNewline, bool ignoreWhitespace, int workBudget, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        bool Equal(string x, string y) => ignoreWhitespace ? WhitespaceEqual(x, y) : x == y;
        var prefix = 0; while (prefix < a.Length && prefix < b.Length && Equal(a[prefix], b[prefix])) prefix++;
        var suffix = 0; while (suffix < a.Length - prefix && suffix < b.Length - prefix && Equal(a[^(suffix + 1)], b[^(suffix + 1)])) suffix++;
        var edits = new List<Edit>(a.Length + b.Length);
        for (var i = 0; i < prefix; i++) edits.Add(new(DiffKind.Context, b[i]));
        var n = a.Length - prefix - suffix; var m = b.Length - prefix - suffix;
        var coarse = false;
        if (n == 0) { for (var j = 0; j < m; j++) edits.Add(new(DiffKind.Addition, b[prefix + j])); }
        else if (m == 0) { for (var j = 0; j < n; j++) edits.Add(new(DiffKind.Deletion, a[prefix + j])); }
        else
        {
            var max = n + m; var offset = max + 1; var v = new int[2 * max + 3];
            var trace = new List<int[]>(); var distance = -1; var work = 0L;
            for (var d = 0; d <= max; d++)
            {
                cancellation.ThrowIfCancellationRequested();
                // Trace memory is also bounded (roughly 32 MiB by default).
                if (work + d * 2L > workBudget || (long)(trace.Count + 1) * v.Length > 8_000_000) { coarse = true; break; }
                trace.Add((int[])v.Clone());
                for (var k = -d; k <= d; k += 2)
                {
                    var ix = offset + k;
                    var x = k == -d || (k != d && v[ix - 1] < v[ix + 1]) ? v[ix + 1] : v[ix - 1] + 1;
                    var y = x - k;
                    while (x < n && y < m && Equal(a[prefix + x], b[prefix + y])) { x++; y++; work++; }
                    v[ix] = x; work++;
                    if (x >= n && y >= m) { distance = d; break; }
                }
                if (distance >= 0) break;
            }
            if (coarse || distance < 0)
            {
                coarse = true;
                for (var i = 0; i < n; i++) edits.Add(new(DiffKind.Deletion, a[prefix + i]));
                for (var i = 0; i < m; i++) edits.Add(new(DiffKind.Addition, b[prefix + i]));
            }
            else
            {
                var reverse = new List<Edit>(); var x = n; var y = m;
                for (var d = distance; d >= 0; d--)
                {
                    var previous = trace[d]; var k = x - y; var ix = offset + k;
                    var previousK = k == -d || (k != d && previous[ix - 1] < previous[ix + 1]) ? k + 1 : k - 1;
                    var previousX = previous[offset + previousK]; var previousY = previousX - previousK;
                    while (x > previousX && y > previousY) { reverse.Add(new(DiffKind.Context, b[prefix + y - 1])); x--; y--; }
                    if (d == 0) break;
                    if (x == previousX) { reverse.Add(new(DiffKind.Addition, b[prefix + --y])); }
                    else { reverse.Add(new(DiffKind.Deletion, a[prefix + --x])); }
                }
                reverse.Reverse(); edits.AddRange(reverse);
            }
        }
        for (var i = b.Length - suffix; i < b.Length; i++) edits.Add(new(DiffKind.Context, b[i]));
        var oldLine = 1; var newLine = 1; var result = new List<DiffLine>(edits.Count);
        foreach (var edit in edits) result.Add(new(edit.Kind, edit.Text, edit.Kind == DiffKind.Addition ? 0 : oldLine++, edit.Kind == DiffKind.Deletion ? 0 : newLine++));
        return new(result, coarse, oldNewline, newNewline);
    }
    public static string[] Lines(string text)
    {
        if (text.Length == 0) return [];
        var result = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
        return EndsInNewline(text) ? result[..^1] : result;
    }
    private static bool EndsInNewline(string text) => text.EndsWith('\r') || text.EndsWith('\n');
    private static bool WhitespaceEqual(string left, string right)
    {
        var a = 0; var b = 0;
        while (true)
        {
            while (a < left.Length && char.IsWhiteSpace(left[a])) a++;
            while (b < right.Length && char.IsWhiteSpace(right[b])) b++;
            if (a == left.Length || b == right.Length) return a == left.Length && b == right.Length;
            if (left[a++] != right[b++]) return false;
        }
    }
    public static (int Start, int OldLength, int NewLength) ChangedSpan(string before, string after)
    {
        var start = 0; while (start < before.Length && start < after.Length && before[start] == after[start]) start++;
        var end = 0; while (end < before.Length - start && end < after.Length - start && before[^(end + 1)] == after[^(end + 1)]) end++;
        return (start, before.Length - start - end, after.Length - start - end);
    }
    public static (int First, int Last) VisibleRange(int count, double scroll, double height, double rowHeight)
    {
        if (rowHeight <= 0 || !double.IsFinite(rowHeight)) throw new ArgumentOutOfRangeException(nameof(rowHeight));
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
        if (double.IsNaN(scroll)) scroll = 0;
        if (double.IsNaN(height)) height = 0;
        var first = (int)Math.Clamp(Math.Floor(Math.Max(0, scroll) / rowHeight), 0, count);
        var visible = (int)Math.Clamp(Math.Ceiling(Math.Max(0, height) / rowHeight) + 1, 0, count - first);
        return (first, first + visible);
    }
}
