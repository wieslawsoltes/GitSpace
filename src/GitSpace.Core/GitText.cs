using System.Security.Cryptography;
using System.Text;

namespace GitSpace.Core;

public static class GitText
{
    public static readonly UTF8Encoding Utf8 = new(false, true);
    public static string ToLf(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
    public static string FromEditor(string edited, string original)
    {
        var normalized = ToLf(edited);
        if (original.StartsWith('\uFEFF') && !normalized.StartsWith('\uFEFF')) normalized = "\uFEFF" + normalized;
        if (normalized == ToLf(original)) return original;
        var first = original.IndexOfAny(['\r', '\n']);
        var newline = first < 0 || original[first] == '\n' ? "\n" : first + 1 < original.Length && original[first + 1] == '\n' ? "\r\n" : "\r";
        // TextBox may strip a leading BOM; retain the original encoding marker.
        if (original.StartsWith('\uFEFF') && !normalized.StartsWith('\uFEFF')) normalized = "\uFEFF" + normalized;
        return newline == "\n" ? normalized : normalized.Replace("\n", newline, StringComparison.Ordinal);
    }
    public static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Utf8.GetBytes(text))).ToLowerInvariant();
    public static string Version(bool exists, string mode, string text) => Hash((exists ? "1" : "0") + ":" + mode + ":" + text);
    public static void Verify(string expected, string actual)
    {
        if (expected.Length != 64 || !string.Equals(expected, actual, StringComparison.Ordinal))
            throw new InvalidOperationException("The reviewed content changed. Refresh, review it again, and retry; nothing was staged.");
    }
}
