namespace GitSpace.Diff;

public enum ConflictChoice { Current, Incoming, Both }
public sealed record ConflictBlock(int Start, int Length, string Current, string Incoming, string Base);

/// <summary>Parses Git merge/diff3 markers without discarding surrounding text or line endings.</summary>
public sealed class ConflictDocument
{
    private readonly string[] _lines;
    public IReadOnlyList<ConflictBlock> Blocks { get; }
    public ConflictDocument(string text)
    {
        _lines = LineSelection.Tokens(text); var blocks = new List<ConflictBlock>();
        for (var i = 0; i < _lines.Length; i++)
        {
            if (!_lines[i].StartsWith("<<<<<<<", StringComparison.Ordinal)) continue;
            var start = i; var current = new List<string>(); var incoming = new List<string>(); var original = new List<string>();
            var target = current; var divider = false; var closed = false;
            while (++i < _lines.Length)
            {
                var line = _lines[i];
                if (line.StartsWith("<<<<<<<", StringComparison.Ordinal)) throw new InvalidDataException("Nested conflict markers require manual resolution.");
                if (line.StartsWith("|||||||", StringComparison.Ordinal)) { if (divider) throw new InvalidDataException("Invalid diff3 conflict markers."); target = original; continue; }
                if (line.TrimEnd('\r', '\n') == "=======") { if (divider) throw new InvalidDataException("Repeated conflict separator."); divider = true; target = incoming; continue; }
                if (line.StartsWith(">>>>>>>", StringComparison.Ordinal)) { closed = true; break; }
                target.Add(line);
            }
            if (!closed || !divider) throw new InvalidDataException("Incomplete conflict markers; resolve this file manually.");
            blocks.Add(new(start, i - start + 1, string.Concat(current), string.Concat(incoming), string.Concat(original)));
        }
        Blocks = blocks;
    }
    public string Resolve(IReadOnlyList<ConflictChoice> choices)
    {
        if (choices.Count != Blocks.Count) throw new ArgumentException("Choose a resolution for every conflict.");
        var result = new System.Text.StringBuilder(); var position = 0;
        for (var i = 0; i < Blocks.Count; i++)
        {
            var block = Blocks[i]; while (position < block.Start) result.Append(_lines[position++]);
            result.Append(choices[i] switch { ConflictChoice.Current => block.Current, ConflictChoice.Incoming => block.Incoming, ConflictChoice.Both => block.Current + block.Incoming, _ => throw new ArgumentOutOfRangeException(nameof(choices)) });
            position += block.Length;
        }
        while (position < _lines.Length) result.Append(_lines[position++]);
        return result.ToString();
    }
}
