using System.Text;

namespace GitSpace.Diff;

/// <summary>An exact, line-ending-preserving edit script, with UI-independent line and hunk selection.</summary>
public sealed class LineSelection
{
    private readonly DiffDocument _exact;
    public DiffDocument Document { get; }
    public bool CanSelect => !_exact.Coarse;
    public LineSelection(string before, string after, CancellationToken cancellation = default)
    {
        _exact = DiffEngine.CompareTokens(Tokens(before), Tokens(after), Ends(before), Ends(after), false, 2_000_000, cancellation);
        Document = new(_exact.Lines.Select(l => l with { Text = l.Text.TrimEnd('\r', '\n') }).ToArray(), _exact.Coarse, Ends(before), Ends(after));
    }
    private static bool Ends(string text) => text.EndsWith('\r') || text.EndsWith('\n');
    public static string[] Tokens(string text)
    {
        var result = new List<string>(); var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] is not ('\r' or '\n')) continue;
            if (text[i] == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
            result.Add(text[start..(i + 1)]); start = i + 1;
        }
        if (start < text.Length) result.Add(text[start..]);
        return result.ToArray();
    }
    public int[] HunkAt(int row)
    {
        if (row < 0 || row >= Document.Lines.Count || Document.Lines[row].Kind == DiffKind.Context) return [];
        var start = row; var end = row;
        while (start > 0 && Document.Lines[start - 1].Kind != DiffKind.Context) start--;
        while (end + 1 < Document.Lines.Count && Document.Lines[end + 1].Kind != DiffKind.Context) end++;
        return Enumerable.Range(start, end - start + 1).ToArray();
    }
    /// <param name="removeSelected">When unstaging, keep all changes except the selected edits.</param>
    public string Apply(IEnumerable<int> rows, bool removeSelected = false)
    {
        if (!CanSelect) throw new InvalidOperationException("Partial staging is unavailable for a coarse diff. Stage the entire file instead.");
        var selected = rows.ToHashSet();
        if (selected.Count == 0 || selected.Any(i => i < 0 || i >= _exact.Lines.Count || _exact.Lines[i].Kind == DiffKind.Context))
            throw new ArgumentException("Select changed lines, not unchanged context.", nameof(rows));
        var result = new StringBuilder(); var previousWasTerminated = true;
        for (var i = 0; i < _exact.Lines.Count; i++)
        {
            var line = _exact.Lines[i]; var apply = selected.Contains(i) != removeSelected;
            if (line.Kind == DiffKind.Addition && !apply || line.Kind == DiffKind.Deletion && apply) continue;
            if (!previousWasTerminated) throw new InvalidOperationException("Include the adjacent end-of-file newline change in this selection.");
            result.Append(line.Text); previousWasTerminated = Ends(line.Text);
        }
        return result.ToString();
    }
}
