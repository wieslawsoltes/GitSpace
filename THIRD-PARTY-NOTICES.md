# Third-party notices

GitSpace source code is MIT licensed. Dependencies retain their own copyright and license notices; distributing an application does not relicense them under GitSpace's MIT license.

| Component | Use | Upstream license/reference |
| --- | --- | --- |
| Uno Platform / Uno.Sdk | Shared desktop and browser UI framework | Apache-2.0; https://github.com/unoplatform/uno |
| SkiaSharp | Managed graphics API | MIT; https://github.com/mono/SkiaSharp |
| Skia | Graphics implementation below SkiaSharp | BSD-style license and third-party notices; https://skia.googlesource.com/skia/ |
| .NET | Runtime and libraries | MIT and distribution notices; https://github.com/dotnet/runtime |
| isomorphic-git 1.42.3 | Browser Git implementation, with its embedded LGPL path helper removed from distributed bundles | Remaining bundled code MIT; https://github.com/isomorphic-git/isomorphic-git |
| LightningFS 4.6.0 | Browser filesystem and IndexedDB persistence | MIT; https://github.com/isomorphic-git/lightning-fs |
| fflate 0.8.2 | Browser ZIP export | MIT; https://github.com/101arrowz/fflate |
| buffer 6.0.3 | Worker-local Buffer compatibility through the locked dependency graph | MIT; https://github.com/feross/buffer |
| esbuild | Build-time worker bundling | MIT; https://github.com/evanw/esbuild |
| Playwright | Development/CI browser tests | Apache-2.0; https://github.com/microsoft/playwright |
| Source Code Pro Regular | Embedded application diff typeface | SIL Open Font License 1.1; https://github.com/adobe-fonts/source-code-pro |
| Open Sans supplied by Uno.Fonts.OpenSans | Uno UI text fallback | Retain the font and package's supplied upstream font-license notices; https://www.nuget.org/packages/Uno.Fonts.OpenSans |

Source Code Pro is acquired from immutable upstream commit `803b7e23ec97ae58b6232ea76519a76d428ba268`. The unmodified font is embedded into the application; its copyright and complete OFL license are in `src/GitSpace.App/Fonts/LICENSE-SourceCodePro.txt` and the application resources. See [typography provenance](docs/typography.md). No host-machine font files are copied into this repository or its distributions.

The npm bundle retains dependency license comments. NuGet and npm transitive dependencies have their own licenses and notices; preserve those from the resolved dependency distributions. A full legal review of a downstream product's distribution remains the distributor's responsibility.

System Git is a separately installed executable, licensed under GPL-2.0. GitSpace invokes it as an external process, does not bundle it, and does not link GPL Git libraries into its own reusable assemblies. Users install Git separately under its license.

GitHub and GitHub Desktop are names and marks of GitHub, Inc. They identify the workflow inspiration, not an affiliation or endorsement. GitSpace uses its own name and does not distribute GitHub account credentials, logos or signed GitHub Desktop binaries.

## Browser bundle path-helper replacement

Upstream isomorphic-git 1.42.3 includes an LGPL-3.0-or-later `path.join` helper despite the package-level MIT declaration. GitSpace does not relicense that helper. `build.mjs` removes the complete upstream helper from distributed code and substitutes `path-join.mjs`, an independent MIT implementation tested against Node POSIX path semantics and drive-root cases. The build refuses unexpected layouts or remaining LGPL markers. Upstream npm packages are development-only; downstream GitSpace npm consumers receive the self-contained replacement bundles. Building from source still downloads upstream development dependencies under their own licenses.
