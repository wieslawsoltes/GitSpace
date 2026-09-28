using System.Text;
using System.Text.RegularExpressions;

namespace GitSpace.Core;

public enum ConflictChoice { Unresolved, Ours, Theirs, Both }
public sealed record ConflictSection(int Index, string Context, string Ours, string Base, string Theirs, bool IsConflict);

/// <summary>Lossless parser for standard and diff3 conflict markers. Malformed/nested markers are rejected.</summary>
public sealed partial class ConflictDocument
{
    public IReadOnlyList<ConflictSection> Sections { get; }
    public int Count { get; }
    private ConflictDocument(IReadOnlyList<ConflictSection> sections) { Sections = sections; Count = sections.Count(s => s.IsConflict); }
    public static bool HasMarkers(string text) => Marker().IsMatch(text);
    public static ConflictDocument Parse(string text)
    {
        var sections = new List<ConflictSection>(); var context = new StringBuilder();
        var ours = new StringBuilder(); var ancestor = new StringBuilder(); var theirs = new StringBuilder();
        var state = 0; var width = 0; var count = 0;
        foreach (Match match in Line().Matches(text))
        {
            var line = match.Value; if (line.Length == 0) continue;
            var body = line.TrimEnd('\r', '\n'); var marker = Marker().Match(body);
            if (!marker.Success) { (state == 0 ? context : state == 1 ? ours : state == 2 ? ancestor : theirs).Append(line); continue; }
            var run = marker.Groups[1].Value;
            if (run[0] == '<')
            {
                if (state != 0) throw new InvalidDataException("Nested conflict markers require manual editing.");
                if (context.Length > 0) { sections.Add(new(-1, context.ToString(), "", "", "", false)); context.Clear(); }
                width = run.Length; state = 1;
            }
            else if (state == 0 || run.Length != width) throw new InvalidDataException("Unbalanced conflict markers require manual editing.");
            else if (run[0] == '|' && state == 1) state = 2;
            else if (run[0] == '=' && state is 1 or 2) state = 3;
            else if (run[0] == '>' && state == 3)
            {
                sections.Add(new(count++, "", ours.ToString(), ancestor.ToString(), theirs.ToString(), true));
                ours.Clear(); ancestor.Clear(); theirs.Clear(); state = 0;
            }
            else throw new InvalidDataException("Invalid conflict marker order.");
        }
        if (state != 0) throw new InvalidDataException("The final conflict is not closed.");
        if (context.Length > 0) sections.Add(new(-1, context.ToString(), "", "", "", false));
        return new(sections);
    }
    public string Resolve(IReadOnlyDictionary<int, ConflictChoice> choices)
    {
        var result = new StringBuilder();
        foreach (var s in Sections)
        {
            if (!s.IsConflict) { result.Append(s.Context); continue; }
            if (!choices.TryGetValue(s.Index, out var choice) || choice == ConflictChoice.Unresolved)
                throw new InvalidOperationException("Choose a resolution for every conflict.");
            result.Append(choice switch { ConflictChoice.Ours => s.Ours, ConflictChoice.Theirs => s.Theirs, ConflictChoice.Both => s.Ours + s.Theirs, _ => throw new ArgumentOutOfRangeException(nameof(choices)) });
        }
        return result.ToString();
    }
    [GeneratedRegex("^(<{7,}|={7,}|>{7,}|\\|{7,})(?:[ \t\r]|$)", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex Marker();
    [GeneratedRegex("[^\\r\\n]*(?:\\r\\n|\\r|\\n|$)", RegexOptions.CultureInvariant)]
    private static partial Regex Line();
}
