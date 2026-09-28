using GitSpace.Core;

namespace GitSpace.Git;

public static class StatusParser
{
    /// <summary>Parses NUL-delimited porcelain v1. Rename destination precedes its original path.</summary>
    public static GitChange[] Parse(string text)
    {
        var tokens = text.Split('\0'); var files = new List<GitChange>();
        for (var i = 0; i < tokens.Length; i++)
        {
            var token = tokens[i]; if (token.Length < 4) continue;
            var x = token[0]; var y = token[1]; var path = token[3..]; var original = "";
            if (x is 'R' or 'C' || y is 'R' or 'C') { if (++i < tokens.Length) original = tokens[i]; }
            var conflict = x == 'U' || y == 'U' || (x == 'A' && y == 'A') || (x == 'D' && y == 'D');
            var status = conflict ? "U" : x == '?' ? "A" : x == 'R' || y == 'R' ? "R" : x == 'D' || y == 'D' ? "D" : x == 'A' ? "A" : "M";
            files.Add(new(path, status, x is not (' ' or '?'), conflict, original));
        }
        return files.ToArray();
    }
    public static GitCommit[] Log(string text)
    {
        var fields = text.Split('\0'); var result = new List<GitCommit>();
        for (var i = 0; i + 5 < fields.Length; i += 6)
        {
            var id = fields[i].Trim(); if (id.Length == 0) continue;
            result.Add(new(id, fields[i + 1], fields[i + 2], fields[i + 3], fields[i + 5].TrimEnd('\r', '\n'), fields[i + 4].Split(' ', StringSplitOptions.RemoveEmptyEntries)));
        }
        return result.ToArray();
    }
}
