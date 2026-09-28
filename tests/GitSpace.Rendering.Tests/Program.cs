using System.Diagnostics;
using System.Text.Json;
using GitSpace.Diff;
using GitSpace.Rendering.Skia;
using SkiaSharp;

var passed = 0;
void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
void Test(string name, Action action) { action(); passed++; Console.WriteLine("PASS " + name); }
using var surface = SKSurface.Create(new SKImageInfo(800, 240)) ?? throw new Exception("Raster surface initialization failed.");
using var renderer = new DiffRenderer();
var viewport = new DiffViewport();
var bounds = new SKRect(0, 0, 800, 240);
void Draw() => renderer.Draw(surface.Canvas, bounds, viewport);
byte[] Pixels() { using var image = surface.Snapshot(); using var png = image.Encode(SKEncodedImageFormat.Png, 100); return png.ToArray(); }

Test("empty documents do not create code text runs", () =>
{
    renderer.SetDocument(DiffEngine.Compare("", "")); Draw();
    Check(renderer.CachedTextRuns == 0 && renderer.LastTextDraws == 0, "An empty document prepared code text.");
});
var document = DiffEngine.Compare("", string.Join('\n', Enumerable.Range(0, 1000).Select(i => $"row {i}:\treturn value + {i};")));
Test("only viewport rows prepare glyph runs", () =>
{
    renderer.SetDocument(document); Draw();
    Check(renderer.LastVisibleRows <= 13 && renderer.CachedTextRuns <= 13, "Offscreen rows were prepared.");
    Check(renderer.LastTextDraws == renderer.LastVisibleRows, "Each visible nonempty row must draw once.");
});
Test("twenty warm frames reuse prepared text and reproduce identical pixels", () =>
{
    var pixels = Pixels(); var before = renderer.TextPreparationCount;
    for (var i = 0; i < 20; i++) Draw();
    Check(renderer.TextPreparationCount == before, "Warm frames rebuilt glyph runs.");
    Check(renderer.TextCacheHits >= 20 * renderer.LastVisibleRows, "Warm frames did not hit the cache.");
    Check(pixels.SequenceEqual(Pixels()), "Cached rendering changed the raster output.");
});
Test("theme and selection repaint without preparing text again", () =>
{
    var before = renderer.TextPreparationCount; var pixels = Pixels();
    viewport.Palette = DiffPalette.Light; viewport.SelectedRows = new HashSet<int> { 1, 2 }; Draw();
    Check(renderer.TextPreparationCount == before, "Color changes rebuilt text.");
    Check(!pixels.SequenceEqual(Pixels()), "Theme/selection did not affect output.");
});
Test("zoom invalidates the font generation and preserves raster determinism", () =>
{
    var before = renderer.TextPreparationCount; viewport.FontSize = 18; Draw();
    Check(renderer.TextPreparationCount > before, "Zoom reused old-sized glyphs.");
    var pixels = Pixels(); before = renderer.TextPreparationCount; Draw();
    Check(renderer.TextPreparationCount == before && pixels.SequenceEqual(Pixels()), "Zoomed cache is unstable.");
});
Test("scrolling evicts old runs and keeps count and text storage bounded", () =>
{
    viewport.FontSize = 12; viewport.SelectedRows = null;
    for (var row = 0; row < 1000; row += 10)
    {
        viewport.ScrollY = row * viewport.RowHeight; Draw();
        Check(renderer.CachedTextRuns <= 512 && renderer.CachedTextCharacters <= 262144, "Cache exceeded its bounds.");
    }
    Check(renderer.CachedTextRuns == 512, "The LRU capacity was not exercised.");
});
Test("new documents discard cached rows and repeated identity retains them", () =>
{
    viewport.ScrollY = 0; var other = DiffEngine.Compare("old\n", "new\n");
    renderer.SetDocument(other); Check(renderer.CachedTextRuns == 0 && renderer.CachedTextCharacters == 0, "Old document retained text.");
    Draw(); var before = renderer.TextPreparationCount; renderer.SetDocument(other); Draw();
    Check(before == renderer.TextPreparationCount, "An unchanged document cleared cached text.");
});
Test("split intraline highlighting draws each foreground only once", () =>
{
    renderer.SetDocument(DiffEngine.Compare("alpha\tx\n", "alpha\ty\n")); viewport.Split = true; Draw();
    Check(renderer.LastTextDraws == 2, "Split intraline text was drawn more than once per side.");
    var pixels = Pixels(); var before = renderer.TextPreparationCount; Draw();
    Check(before == renderer.TextPreparationCount && pixels.SequenceEqual(Pixels()), "Split cache changed text or highlight output.");
});
Test("tab-heavy long lines cannot exceed the cache character budget", () =>
{
    viewport.Split = false;
    renderer.SetDocument(DiffEngine.Compare("", string.Join('\n', Enumerable.Range(0, 20).Select(i => i + new string('\t', 12000)))));
    Draw();
    Check(renderer.CachedTextCharacters <= 262144 && renderer.CachedTextRuns < 13, "Expanded tabs evaded the memory budget.");
});
Test("replacing an injected typeface invalidates prepared glyphs", () =>
{
    using var face = SKTypeface.FromFamilyName("serif");
    var old = DiffRenderer.DefaultTypeface;
    try
    {
        viewport.Split = false; renderer.SetDocument(DiffEngine.Compare("", "Typeface generation")); Draw();
        var before = renderer.TextPreparationCount; DiffRenderer.DefaultTypeface = face; Draw();
        Check(renderer.TextPreparationCount > before, "Typeface replacement used stale glyphs.");
    }
    finally { DiffRenderer.DefaultTypeface = old; Draw(); }
});
Test("idempotent disposal clears native text resources and rejects later drawing", () =>
{
    using var temporary = new DiffRenderer(); temporary.SetDocument(document); temporary.Draw(surface.Canvas, bounds, viewport);
    temporary.Dispose(); temporary.Dispose(); Check(temporary.CachedTextRuns == 0, "Disposed renderer retained runs.");
    try { temporary.Draw(surface.Canvas, bounds, viewport); } catch (ObjectDisposedException) { return; }
    throw new Exception("Drawing after disposal was accepted.");
});

// Reproducible CPU-raster microbenchmark; intentionally not a hardware-GPU or browser claim.
viewport.Split = false; viewport.ScrollY = 0; viewport.FontSize = 12;
renderer.SetDocument(document); Draw();
var prepared = renderer.TextPreparationCount; var times = new List<double>();
for (var i = 0; i < 100; i++) { var watch = Stopwatch.StartNew(); Draw(); times.Add(watch.Elapsed.TotalMilliseconds); }
times.Sort();
Check(renderer.TextPreparationCount == prepared, "Benchmark warmed text was rebuilt.");
var report = new { environment = "Linux Skia CPU raster, 800x240, 1000-line diff", frames = 100, visibleRows = renderer.LastVisibleRows,
    medianMs = times[50], p95Ms = times[95], extraTextPreparations = renderer.TextPreparationCount - prepared,
    cachedRuns = renderer.CachedTextRuns, cachedCharacters = renderer.CachedTextCharacters, testsPassed = passed };
Directory.CreateDirectory("artifacts");
File.WriteAllText("artifacts/rendering-benchmark.json", JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine(JsonSerializer.Serialize(report));
Console.WriteLine($"RESULT: {passed} renderer checks passed.");
