using System.Text;

namespace GitSpace.Diff;

public sealed record DiffHunk(int FirstRow, int LastRow, int Additions, int Deletions);

/// <summary>Reconstructs a partial change from exact source segments, without rewriting the worktree.</summary>
public static class PartialDiff
{
    public static IReadOnlyList<DiffHunk> Hunks(DiffDocument document)
    {
        var result = new List<DiffHunk>();
        for (var i = 0; i < document.Lines.Count;)
        {
            if (document.Lines[i].Kind == DiffKind.Context) { i++; continue; }
            var start = i; var added = 0; var removed = 0;
            while (i < document.Lines.Count && document.Lines[i].Kind != DiffKind.Context)
            {
                if (document.Lines[i++].Kind == DiffKind.Addition) added++; else removed++;
            }
            result.Add(new(start, i - 1, added, removed));
        }
        return result;
    }
    public static string Build(string before, string after, IReadOnlySet<int> selectedRows, bool includeLineEndings = false)
    {
        var document = DiffEngine.Compare(before, after);
        if (document.Coarse) throw new InvalidOperationException("Partial staging requires an exact diff. Stage the complete file instead.");
        if (selectedRows.Any(i => i < 0 || i >= document.Lines.Count || document.Lines[i].Kind == DiffKind.Context))
            throw new ArgumentException("Only changed diff rows can be selected.", nameof(selectedRows));
        var old = Segments(before); var updated = Segments(after); var output = new List<string>();
        for (var i = 0; i < document.Lines.Count; i++)
        {
            var line = document.Lines[i];
            if (line.Kind == DiffKind.Context) output.Add(includeLineEndings ? updated[line.NewLine - 1] : old[line.OldLine - 1]);
            else if (line.Kind == DiffKind.Deletion && !selectedRows.Contains(i)) output.Add(old[line.OldLine - 1]);
            else if (line.Kind == DiffKind.Addition && selectedRows.Contains(i)) output.Add(updated[line.NewLine - 1]);
        }
        var newline = before.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var result = new StringBuilder();
        for (var i = 0; i < output.Count; i++)
        {
            result.Append(output[i]);
            if (i + 1 < output.Count && !output[i].EndsWith('\r') && !output[i].EndsWith('\n')) result.Append(newline);
        }
        return result.ToString();
    }
    private static string[] Segments(string value)
    {
        var result = new List<string>(); var start = 0;
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] is not ('\r' or '\n')) continue;
            if (value[i] == '\r' && i + 1 < value.Length && value[i + 1] == '\n') i++;
            result.Add(value[start..(i + 1)]); start = i + 1;
        }
        if (start < value.Length) result.Add(value[start..]);
        return result.ToArray();
    }
}
