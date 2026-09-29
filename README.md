<div align="center">

# GitSpace

**A familiar Git desktop workflow. One shared Uno application.**

[![Build and test](https://github.com/wieslawsoltes/GitSpace/actions/workflows/build.yml/badge.svg)](https://github.com/wieslawsoltes/GitSpace/actions/workflows/build.yml)
[![Browser and Pages](https://github.com/wieslawsoltes/GitSpace/actions/workflows/pages.yml/badge.svg)](https://github.com/wieslawsoltes/GitSpace/actions/workflows/pages.yml)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
[![Uno Platform](https://img.shields.io/badge/Uno-6.7.30-7a67f8.svg)](https://www.nuget.org/packages/Uno.Sdk/6.7.30)

[Open browser app](https://wieslawsoltes.github.io/GitSpace/) · [Architecture](docs/architecture.md) · [Compatibility](docs/compatibility.md) · [Build & release](docs/development.md)

</div>

GitSpace is an independent, MIT-licensed Git client with a GitHub Desktop-style interface, built in **C# and Uno Platform**. The desktop and WebAssembly applications share the same workbench and custom controls. The browser application is the actual compiled Uno application—not an HTML imitation of it.

Review changed files, compare unified or split diffs, select files for a commit, browse history, switch branches, and synchronize repositories. Desktop commands run your installed Git. Browser commands run a bundled, isolated JavaScript Git worker against persistent browser storage. Both backends write real Git repositories.

> **Status: 0.1.0 development preview.** GitSpace implements a substantial working foundation, not complete or pixel-qualified GitHub Desktop parity. Desktop and browser capabilities differ deliberately. See the [explicit compatibility matrix](docs/compatibility.md) before using important repositories.

## The workspace

![GitSpace running as a real Uno WebAssembly application](https://wieslawsoltes.github.io/GitSpace/screenshots/01-changes-dark.png)

The familiar layout includes a compact menu bar; repository, branch and synchronization tiles; Changes and History tabs; file inclusion checkboxes; a commit summary and description composer; a resizable sidebar; and a diff workspace with line gutters, addition/deletion colors, intraline split highlights and light/dark appearances. The screenshot is produced by the real Chromium interaction suite, not a design mockup.

The tutorial is a **real, editable repository** with four commits and a small set of uncommitted changes. Making a commit changes its Git history. Reloading the browser reopens the persisted repository.

## Get started

### Browser

Open **[GitSpace on GitHub Pages](https://wieslawsoltes.github.io/GitSpace/)**. The tutorial opens on the first launch. Use **File → New repository** to create an isolated repository, or **File → Clone repository** to clone an HTTPS remote. Use **Preferences** to set your Git author name and email.

Browser repositories are held in IndexedDB through LightningFS. They are **not your computer's ordinary working directories**. Storage can be evicted or cleared. Use **File → Export repository ZIP** or push to a trusted remote to preserve important work. The ZIP includes `.git` and the working files; it is limited to 64 MiB and currently rejects symbolic links.

GitHub's Git transport generally requires a trusted CORS proxy for browser clone/fetch/push. GitSpace does not select a public proxy automatically. Configure a proxy you control or trust in Preferences. **A proxy can see repository contents and any access token sent through it.** Tokens and proxy settings are session-only. GitHub REST pull-request operations use the GitHub API directly and do not use the Git transport proxy.

### Desktop from source

Install the **.NET 10 SDK** and **Git 2.30 or newer** on `PATH`, then:

```sh
git clone https://github.com/wieslawsoltes/GitSpace.git
cd GitSpace
dotnet run --project src/GitSpace.App -f net10.0-desktop \
  -p:GitSpaceTargetFrameworks=net10.0-desktop
```

The Uno desktop host selects Win32 on Windows, macOS on macOS, and X11 on Linux. Desktop Git uses your installed credential helper. GitSpace does not bundle Git, store system credentials, or replace the operating system's credential manager.

Only open repositories you trust. GitSpace disables repository hooks for its commands, but Git configuration, filters, external helpers and platform tools can still execute programs. This is not a repository sandbox.

## Implemented workflows

| Area | What is implemented |
| --- | --- |
| Repository work | Open, initialize, HTTPS clone, refresh, real persistent tutorial, add HTTPS remotes |
| Review | Changed files, file inclusion, stage/unstage, unified and split diffs, line numbers, intraline split spans, search, whitespace-ignore view, copy, text zoom |
| Editing | Bounded UTF-8 file editor, new files, external-change check before saving, tracked-file discard with confirmation |
| Commits | Required summary, optional description, author identity, selected-file commit, amend with confirmation |
| History | Paged history (up to 2,000 commits), filter, author/date/message, per-commit file changes and parent comparison |
| Branches | Create, switch, rename, safe merged-branch deletion, lightweight tags |
| Synchronization | Fetch, fast-forward pull, non-force push; explicit trusted browser proxy configuration |
| Integration | Desktop merge/rebase/revert/cherry-pick and continue/abort; browser fast-forward merge only |
| Stashes | Desktop tracked/untracked stash, apply without dropping, confirmed drop; guarded browser subset |
| GitHub | List the first 100 open pull requests, create a pull request, open repository/PR in browser; session-token authentication |
| Workspace | Light/dark appearance, keyboard shortcuts, persisted non-secret preferences, resizable sidebar, explicit errors and busy state |

There is no force-push button, automatic destructive conflict resolution, or fake successful operation behind an unsupported menu item.

## Download

Every [release](https://github.com/wieslawsoltes/GitSpace/releases/latest) ships a self-contained, single-file desktop app — no .NET install needed:

| OS | x64 | Arm64 |
| --- | --- | --- |
| Windows | `GitSpace-<version>-win-x64.zip` | `GitSpace-<version>-win-arm64.zip` |
| macOS | `GitSpace-<version>-osx-x64.tar.gz` | `GitSpace-<version>-osx-arm64.tar.gz` |
| Linux | `GitSpace-<version>-linux-x64.tar.gz` | `GitSpace-<version>-linux-arm64.tar.gz` |

Extract and run `GitSpace` (`GitSpace.exe` on Windows). **Git 2.30 or newer** must still be on `PATH`. Builds are not code-signed yet: on macOS clear the quarantine flag with `xattr -d com.apple.quarantine GitSpace`; on Windows choose **More info → Run anyway** in SmartScreen. Verify downloads against `SHA256SUMS`.

The .NET libraries below are published to [NuGet.org](https://www.nuget.org/packages?q=GitSpace), e.g. `dotnet add package GitSpace.Diff`.

## Independently reusable components

The application composes seven NuGet libraries and one npm library. None of the reusable libraries depends on `GitSpace.App`.

| Package | Responsibility | Depends on |
| --- | --- | --- |
| `GitSpace.Core` | Repository contracts, command/result models, JSON and path/ref validation | .NET |
| `GitSpace.Diff` | Bounded Myers line diff, split alignment, intraline spans, viewport arithmetic | .NET |
| `GitSpace.Git` | Asynchronous system Git process backend, status/history parsing, file safety | Core |
| `GitSpace.Hosting.GitHub` | Bounded GitHub HTTP pull-request client | .NET HTTP/JSON |
| `GitSpace.Rendering.Skia` | Framework-independent, viewport-only Skia diff renderer | Diff, SkiaSharp |
| `GitSpace.Controls.Uno` | Custom buttons, toolbar tiles, file/history lists, commit composer, diff viewer and splitter | Core, Rendering.Skia, Uno |
| `GitSpace.Workbench.Uno` | Complete embeddable client workspace and dialogs with injected platform/backend | Controls.Uno, Hosting.GitHub |
| `@gitspace/browser-git` | Real worker-ready Git backend over an injected filesystem and HTTP client | isomorphic-git, LightningFS, fflate |

Example without the application or Uno:

```csharp
using GitSpace.Core;
using GitSpace.Git;

await using var git = new DesktopGitBackend();
var opened = await git.ExecuteAsync(new GitRequest("open")
{
    Root = "/absolute/path/to/repository"
});

foreach (var file in opened.Snapshot!.Changes)
    Console.WriteLine($"{file.Status} {file.Path}");

var diff = await git.ExecuteAsync(new GitRequest("diff")
{
    Path = "README.md"
});
```

To embed the complete UI, provide an `IWorkbenchPlatform` with an `IGitBackend`, preferences persistence and external-navigation/download adapters, construct `WorkbenchView`, attach it to a window, then await `InitializeAsync()`. Individual controls can also be used without the workbench. See [architecture and embedding](docs/architecture.md).

## Rendering and performance

GitSpace pins **Uno.Sdk 6.7.30**, **.NET 10**, and SDK-compatible **SkiaSharp 3.119.2**. Uno's Skia renderer draws the shared visual tree using the platform's supported graphics backend. The diff renderer uses `SKCanvasElement`, so it participates in the same compositor rather than introducing a competing graphics surface.

Skia is the practical cross-platform rendering choice here; this is **not a custom WebGPU engine**, and hardware acceleration depends on the host and device. CI browser tests use headless Chromium/SwiftShader, not physical-GPU qualification.

The diff renderer visits only visible rows, reuses paints/fonts, caches split alignment and maximum line width, and does not animate while idle. The line-diff algorithm limits work and trace allocation and explicitly reports a coarse replacement when its budget is exceeded. Browser Git runs off the UI thread in a worker. Desktop commands are asynchronous, cancellable, shell-free and serialized per backend. See [performance boundaries](docs/architecture.md#performance-boundaries) for remaining scale work.

## Build, test and distribution

```sh
# Portable algorithms and real system-Git integration tests
dotnet run --project tests/GitSpace.Tests -c Release

# Browser backend tests and bundled same-origin worker
npm install --prefix src/GitSpace.BrowserGit
npm test --prefix src/GitSpace.BrowserGit
npm run build --prefix src/GitSpace.BrowserGit

# Actual Uno WebAssembly application
dotnet workload install wasm-tools --skip-manifest-update
dotnet publish src/GitSpace.App -f net10.0-browserwasm -c Release \
  -p:GitSpaceTargetFrameworks=net10.0-browserwasm \
  -p:WasmShellWebAppBasePath=/GitSpace/ -o artifacts/browser
python3 tools/prepare-pages.py artifacts/browser artifacts/site
```

**Build and test** compiles the desktop app on Linux, Windows and macOS, and runs portable/system-Git/browser-backend tests. **Browser and Pages** builds WebAssembly, executes real Chromium UI workflows, packages the libraries, and deploys the tested output. It verifies the public `build-info.json` commit and the worker asset after deployment. **Release** runs the portable/Git tests on Windows, Linux and macOS, publishes self-contained single-file desktop executables for Windows, macOS and Linux (x64 and arm64), packs the libraries with symbols and emits `SHA256SUMS`. `v*` tags attach all assets to a GitHub Release and publish the NuGet packages to NuGet.org with [Trusted Publishing](https://learn.microsoft.com/nuget/nuget-org/trusted-publishing) (OIDC, no stored API key) from the protected `nuget` environment; manual runs are dry runs that only upload workflow artifacts. Release binaries are unsigned development artifacts; notarization and installer/auto-update infrastructure are not implemented.

Successful workflow runs expose source, browser, screenshots/test reports, and reusable-package artifacts. Version tags publish the seven NuGet libraries to NuGet.org; the `@gitspace/browser-git` npm package is attached to the release as an archive and is not published to npm. The [build guide](docs/development.md) describes local hosting and release use.

## Safety, compatibility and contribution

Read [SECURITY.md](SECURITY.md) and the [compatibility matrix](docs/compatibility.md). Particularly important remaining work includes OAuth/account management; further conflict-resolution and staging qualification; Git LFS/submodule/worktree UI; interactive rebase and richer history operations; native shell/editor integration; comprehensive accessibility/localization; installers/updaters; and exhaustive UI parity/performance qualification.

Contributions should preserve the package boundaries, use capability-gated operations, and add a real backend or UI regression test. Never replace unavailable functionality with a simulated success result.

## License and attribution

GitSpace code is **MIT licensed**. Uno Platform, SkiaSharp, browser dependencies and fonts retain their own licenses; see [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md). System Git is an external, separately installed tool and is not bundled or linked into GitSpace libraries.

GitHub Desktop is an inspiration for the workflow and layout. GitSpace is an independent project, not affiliated with or endorsed by GitHub, Inc. GitHub and GitHub Desktop names and marks belong to their respective owners.

### Review and responsiveness improvements

Unstaged/Staged diff modes now support exact line/hunk selection, guarded index-only updates and staged-only commits. Native conflicts have a current/incoming/result editor with explicit resolution. History pages, recent repositories, focus refresh, recycled row templates and retained unchanged diff state reduce repeated UI work. Newline/BOM handling is covered by regression tests. See the compatibility matrix for the remaining platform-specific limits.
