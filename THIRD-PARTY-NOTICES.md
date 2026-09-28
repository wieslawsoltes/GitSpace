# Third-party notices

GitSpace source code is MIT licensed. Dependencies retain their own copyright and license notices; distributing an application does not relicense them under GitSpace's MIT license.

| Component | Use | Upstream license/reference |
| --- | --- | --- |
| Uno Platform / Uno.Sdk | Shared desktop and browser UI framework | Apache-2.0; https://github.com/unoplatform/uno |
| SkiaSharp | Managed graphics API | MIT; https://github.com/mono/SkiaSharp |
| Skia | Graphics implementation below SkiaSharp | BSD-style license and third-party notices; https://skia.googlesource.com/skia/ |
| .NET | Runtime and libraries | MIT and distribution notices; https://github.com/dotnet/runtime |
| isomorphic-git 1.42.3 | Browser Git implementation | MIT; https://github.com/isomorphic-git/isomorphic-git |
| LightningFS 4.6.0 | Browser filesystem and IndexedDB persistence | MIT; https://github.com/isomorphic-git/lightning-fs |
| fflate 0.8.2 | Browser ZIP export | MIT; https://github.com/101arrowz/fflate |
| esbuild | Build-time worker bundling | MIT; https://github.com/evanw/esbuild |
| Playwright | Development/CI browser tests | Apache-2.0; https://github.com/microsoft/playwright |
| Open Sans supplied by Uno.Fonts.OpenSans | Browser text fallback | Retain the font and package's supplied upstream font-license notices; https://www.nuget.org/packages/Uno.Fonts.OpenSans |

The npm bundle retains its dependency license comments. NuGet and npm transitive dependencies have their own licenses and notices; preserve those from the resolved dependency distributions. A full legal review of a downstream product's distribution remains the distributor's responsibility.

System Git is a separately installed executable, licensed under GPL-2.0. GitSpace invokes it as an external process, does not bundle it, and does not link GPL Git libraries into its own reusable assemblies. Users install Git separately under its license.

No host-machine font files are copied into the source repository. The browser font is supplied by the Uno dependency package. A host can inject another appropriately licensed typeface into the rendering library.

GitHub and GitHub Desktop are names and marks of GitHub, Inc. They identify the workflow inspiration, not an affiliation or endorsement. GitSpace uses its own name and does not distribute GitHub account credentials, logos or signed GitHub Desktop binaries.
