# Diff rendering performance

## Bounded prepared-text cache

The Skia diff renderer now retains an LRU of at most 512 prepared `SKTextBlob` runs and 262,144 counted UTF-16 source/expanded-text characters. Tabs are expanded when a run is created, not on every repaint. Font size/typeface or document changes invalidate the cache; selection and palette changes reuse geometry. Native glyph blobs are disposed on eviction and renderer disposal. These limits bound the cache's text/glyph inputs, not total Skia or application memory.

Split intraline emphasis is painted before the text, removing the previous duplicate foreground draw. The Linux raster regression project checks identical cold/warm pixels, viewport-only preparation, eviction, typeface/zoom invalidation and single-draw split foregrounds. Its recorded timings are CPU-raster measurements, not browser or physical-GPU results. Complex-script shaping/fallback qualification remains separate work.
