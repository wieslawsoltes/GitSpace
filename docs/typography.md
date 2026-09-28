# Typeface provenance and text review

The application embeds unmodified Source Code Pro Regular for its custom diff surface on both desktop and WebAssembly. The build downloads it once per target from Adobe's immutable source-code-pro commit `803b7e23ec97ae58b6232ea76519a76d428ba268`; startup verifies Git blob ID `10c73d7d901cf0dfa8ba03e45e3271d466291520` before loading it into Skia. It is not fetched from a CDN at runtime, and no host-machine font file is copied.

The font retains its SIL Open Font License. Its full notice is in `src/GitSpace.App/Fonts/LICENSE-SourceCodePro.txt`, is embedded in the application, and accompanies the output. It is not relicensed under GitSpace's MIT license.

`GitSpace.Rendering.Skia` remains independent of this application choice. Hosts can inject their own licensed `SKTypeface` through `DiffRenderer.DefaultTypeface`. Standalone renderer instances use a system monospace family when no typeface is injected. Uno manages ordinary UI text and its bundled Open Sans fallback separately.

This replaces the initial browser diff's proportional Open Sans fallback. The renderer now has consistent monospace metrics on the application targets. Comprehensive shaping, bidirectional-code presentation, fallback for every script, ligature preferences, color emoji and per-platform rasterization equivalence still require dedicated qualification.

A first application build needs access to the pinned public font URL as well as NuGet. Subsequent builds reuse the downloaded object under `obj/fonts/<target-framework>`. The library packages themselves do not download or contain this font.
