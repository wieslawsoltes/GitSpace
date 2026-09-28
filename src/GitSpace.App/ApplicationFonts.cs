using System.Security.Cryptography;
using System.Text;
using GitSpace.Rendering.Skia;
using SkiaSharp;
using Windows.Storage;
using Windows.Storage.Streams;

namespace GitSpace.App;

internal static class ApplicationFonts
{
    private static SKTypeface? _typeface, _uiTypeface;
    public static async Task InitializeAsync()
    {
        if (_typeface is not null) return;
#if __WASM__
        // Register the UI fallback before the code face. Otherwise the minimal
        // WebAssembly font manager can use the first registered (monospace) face
        // for ordinary UI text while Uno loads its default font manifest.
        var uiFile = await StorageFile.GetFileFromApplicationUriAsync(new Uri("ms-appx:///Uno.Fonts.OpenSans/Fonts/OpenSans-Regular.ttf"));
        var uiBuffer = await FileIO.ReadBufferAsync(uiFile); var uiBytes = new byte[uiBuffer.Length];
        using (var reader = DataReader.FromBuffer(uiBuffer)) reader.ReadBytes(uiBytes);
        using var uiData = SKData.CreateCopy(uiBytes); _uiTypeface = SKTypeface.FromData(uiData);
#endif
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
