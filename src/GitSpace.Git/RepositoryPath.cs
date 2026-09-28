namespace GitSpace.Git;

/// <summary>Resolves directory aliases, including macOS /var to /private/var, before identity comparisons.</summary>
public static class RepositoryPath
{
    public static string CanonicalDirectory(string path)
    {
        var full = Path.GetFullPath(path);
        var root = Path.GetPathRoot(full) ?? throw new ArgumentException("A rooted directory is required.", nameof(path));
        var current = root;
        foreach (var part in full[root.Length..].Split(new char[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, part);
            var directory = new DirectoryInfo(current);
            if (directory.Exists && directory.LinkTarget is not null)
                current = directory.ResolveLinkTarget(true)?.FullName ?? throw new IOException("Repository directory alias could not be resolved.");
        }
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(current));
    }
    public static bool SameDirectory(string left, string right)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)), Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)), comparison)) return true;
        return string.Equals(CanonicalDirectory(left), CanonicalDirectory(right), comparison);
    }
}
