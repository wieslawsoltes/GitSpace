using System.Security.Cryptography;
using System.Text;
using GitSpace.Rendering.Skia;
using SkiaSharp;

namespace GitSpace.App;

internal static class ApplicationFonts
{
    private static SKTypeface? _typeface;
    public static async Task InitializeAsync()
    {
        if (_typeface is not null) return;
        // The build acquires this OFL-licensed font from an immutable upstream commit.
        // It is embedded into the app, so startup requires no font/CDN network request.
        await using var resource = typeof(ApplicationFonts).Assembly.GetManifestResourceStream("GitSpace.SourceCodePro.Regular")
            ?? throw new InvalidDataException("The bundled monospace typeface is missing.");
        using var output = new MemoryStream(); await resource.CopyToAsync(output); var bytes = output.ToArray();
        var header = Encoding.ASCII.GetBytes("blob " + bytes.Length + "\0");
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA1); hash.AppendData(header); hash.AppendData(bytes);
        if (!Convert.ToHexString(hash.GetHashAndReset()).Equals("10c73d7d901cf0dfa8ba03e45e3271d466291520", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The bundled typeface does not match the pinned upstream object.");
        using var data = SKData.CreateCopy(bytes); _typeface = SKTypeface.FromData(data) ?? throw new InvalidDataException("The bundled typeface could not be loaded.");
        DiffRenderer.DefaultTypeface = _typeface;
    }
}
