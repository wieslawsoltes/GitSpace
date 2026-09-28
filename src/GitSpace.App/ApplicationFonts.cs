using GitSpace.Rendering.Skia;
using SkiaSharp;
using Windows.Storage;
using Windows.Storage.Streams;

namespace GitSpace.App;

internal static class ApplicationFonts
{
    private static SKTypeface? _browserTypeface;
    public static async Task InitializeAsync()
    {
#if __WASM__
        // Uno distributes this licensed asset. No host-machine fonts are copied.
        // Embedders can inject any appropriately licensed SKTypeface into DiffRenderer.
        var file = await StorageFile.GetFileFromApplicationUriAsync(new Uri("ms-appx:///Uno.Fonts.OpenSans/Fonts/OpenSans-Regular.ttf"));
        var buffer = await FileIO.ReadBufferAsync(file); var bytes = new byte[buffer.Length];
        using (var reader = DataReader.FromBuffer(buffer)) reader.ReadBytes(bytes);
        using var data = SKData.CreateCopy(bytes); _browserTypeface = SKTypeface.FromData(data); DiffRenderer.DefaultTypeface = _browserTypeface;
#else
        await Task.CompletedTask;
#endif
    }
}
