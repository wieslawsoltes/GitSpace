# Build, test and release

## Toolchain

- .NET 10 SDK. `global.json` rolls forward within stable .NET 10 feature bands.
- Uno.Sdk 6.7.30. The selected SDK supplies compatible Uno and graphics packages.
- Git 2.30+ on PATH for desktop use and integration tests.
- Node.js 22+ for the browser Git worker and Chromium tests.
- `wasm-tools` workload for WebAssembly publishing.

```sh
dotnet workload install wasm-tools --skip-manifest-update
npm install --prefix src/GitSpace.BrowserGit
npm run build --prefix src/GitSpace.BrowserGit
```

## Desktop

```sh
dotnet build src/GitSpace.App -c Release -f net10.0-desktop \
  -p:GitSpaceTargetFrameworks=net10.0-desktop
dotnet run --project src/GitSpace.App -f net10.0-desktop \
  -p:GitSpaceTargetFrameworks=net10.0-desktop
```

Choose a target framework explicitly to avoid restoring the browser toolchain for desktop-only development. Linux desktop hosting requires a graphical X11 session and the platform libraries required by Uno's desktop host. The release workflow produces unsigned self-contained archives, not installers.

## Browser

```sh
dotnet publish src/GitSpace.App -c Release -f net10.0-browserwasm \
  -p:GitSpaceTargetFrameworks=net10.0-browserwasm \
  -p:WasmShellWebAppBasePath=/GitSpace/ -o artifacts/browser
python3 tools/prepare-pages.py artifacts/browser artifacts/site
mkdir -p artifacts/serve
ln -s "$PWD/artifacts/site" artifacts/serve/GitSpace
python3 -m http.server 4173 --directory artifacts/serve
```

Open `http://127.0.0.1:4173/GitSpace/`. Do not open `index.html` through `file://`: WebAssembly assets, workers and storage require a suitable origin. For a different hosting path, change `WasmShellWebAppBasePath` and serve the same path.

`prepare-pages.py` locates the actual published Uno application, verifies WebAssembly binaries, copies the bundled Git worker to a stable same-origin URL, and records the commit in `build-info.json`. There is no alternate HTML-only application shell.

## Tests

```sh
dotnet run --project tests/GitSpace.Tests -c Release
npm test --prefix src/GitSpace.BrowserGit
npm install --no-save playwright@1.55.1
npx playwright install --with-deps chromium
BASE_URL=http://127.0.0.1:4173/GitSpace/ node tests/browser/smoke.mjs
```

Portable tests include a deterministic Myers-vs-dynamic-programming oracle, reconstruction checks, safety validation, status/log parsing and real Git operations in temporary repositories. Browser backend tests use actual Git objects and refs, not a mock backend. UI tests drive rendered Uno controls in Chromium, commit tutorial changes, create a branch/file, toggle views/theme and verify persistence after reload. Screenshots, console output, DOM and non-secret diagnostic state are captured on failure.

The UI's diagnostic object contains repository state and renderer counts only. It does not execute commands, inject fake data, disclose access tokens, or replace real pointer/text interaction tests.

## Packages

```sh
mkdir -p artifacts/packages
for project in Core Diff Git Hosting.GitHub Rendering.Skia Controls.Uno Workbench.Uno; do
  dotnet pack "src/GitSpace.$project/GitSpace.$project.csproj" -c Release -o artifacts/packages
done
npm pack ./src/GitSpace.BrowserGit --pack-destination artifacts/packages
python3 tools/verify-packages.py artifacts/packages
```

The two Uno libraries target desktop and WebAssembly. The portable libraries target .NET 10. Packaging both Uno targets requires the browser workload. NuGet/npm registry publishing is not performed automatically; test local packages before publishing under your own package ownership.

## Workflows

`build.yml` runs portable/native/browser-backend tests and a Linux/Windows/macOS desktop build matrix. `pages.yml` publishes the real WebAssembly app, runs Chromium interactions, packages libraries, uploads artifacts, and deploys GitHub Pages only from `main`, never from a pull request. The deployment verifies the public commit and worker. The Pages source is GitHub Actions; automatic enablement is attempted through the official configure-pages action and remains subject to repository/organization permissions.

`release.yml` runs for a `v*` tag or manual dispatch. A tag such as `v0.1.0` builds self-contained Windows x64, Linux x64 and macOS arm64 application archives, plus the source and reusable packages, then creates a GitHub Release. Manual dispatch produces artifacts without publishing a release. A release cannot bypass its build/test jobs. No signing credentials, package registry credentials or personal access token are embedded in the repository.

Release archives are unsigned development builds. Installer generation, macOS notarization, Windows signing, automatic updates and deployment to package registries are future work.
