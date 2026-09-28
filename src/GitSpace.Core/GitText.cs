using System.Security.Cryptography;
using System.Text;

namespace GitSpace.Core;

public static class GitText
{
    public static string ToLf(string value) => value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
    public static string Newline(string text)
    {
        var i = text.IndexOfAny(['\r', '\n']);
        return i < 0 || text[i] == '\n' ? "\n" : i + 1 < text.Length && text[i + 1] == '\n' ? "\r\n" : "\r";
    }
    public static string FromEditor(string edited, string original)
    {
        var value = ToLf(edited);
        if (value == ToLf(original)) return original;
        return value.Replace("\n", Newline(original), StringComparison.Ordinal);
    }
    public static string Fingerprint(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
}
