<div align="center">

# GitSpace

**A familiar Git desktop workflow. One shared Uno application.**

[![Build and test](https://github.com/wieslawsoltes/GitSpace/actions/workflows/build.yml/badge.svg)](https://github.com/wieslawsoltes/GitSpace/actions/workflows/build.yml)
[![Browser and Pages](https://github.com/wieslawsoltes/GitSpace/actions/workflows/pages.yml/badge.svg)](https://github.com/wieslawsoltes/GitSpace/actions/workflows/pages.yml)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
[![Uno Platform](https://img.shields.io/badge/Uno-6.7.30-7a67f8.svg)](https://www.nuget.org/packages/Uno.Sdk/6.7.30)
[![NuGet](https://img.shields.io/nuget/vpre/GitSpace.Core.svg?label=NuGet)](https://www.nuget.org/packages/GitSpace.Core)
[![Downloads](https://img.shields.io/nuget/dt/GitSpace.Core.svg)](https://www.nuget.org/packages/GitSpace.Core)

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

## NuGet packages

GitSpace ships as seven MIT-licensed .NET packages on [NuGet.org](https://www.nuget.org/packages?q=GitSpace), versioned together and published by `v*` tags with symbol packages (`.snupkg`) and SourceLink. The five engine packages target `net10.0` and have no UI dependency (only `GitSpace.Rendering.Skia` needs SkiaSharp); the two Uno packages target `net10.0-desktop` and `net10.0-browserwasm`. None of them depends on `GitSpace.App`. The browser Git backend (`src/GitSpace.BrowserGit`, `@gitspace/browser-git`, built on isomorphic-git, LightningFS and fflate) is not a NuGet package and is not on the npm registry; it is attached to each GitHub Release as an archive.

```sh
dotnet add package GitSpace.Core
```

| Package | Version | Downloads | Description |
| --- | --- | --- | --- |
| [GitSpace.Core](https://www.nuget.org/packages/GitSpace.Core) | [![NuGet](https://img.shields.io/nuget/vpre/GitSpace.Core.svg)](https://www.nuget.org/packages/GitSpace.Core) | [![Downloads](https://img.shields.io/nuget/dt/GitSpace.Core.svg)](https://www.nuget.org/packages/GitSpace.Core) | Repository contracts, request/result models, JSON and path/ref/remote safety validation |
| [GitSpace.Diff](https://www.nuget.org/packages/GitSpace.Diff) | [![NuGet](https://img.shields.io/nuget/vpre/GitSpace.Diff.svg)](https://www.nuget.org/packages/GitSpace.Diff) | [![Downloads](https://img.shields.io/nuget/dt/GitSpace.Diff.svg)](https://www.nuget.org/packages/GitSpace.Diff) | Bounded Myers line diff, split alignment, line/hunk selection, conflict parsing and viewport arithmetic |
| [GitSpace.Git](https://www.nuget.org/packages/GitSpace.Git) | [![NuGet](https://img.shields.io/nuget/vpre/GitSpace.Git.svg)](https://www.nuget.org/packages/GitSpace.Git) | [![Downloads](https://img.shields.io/nuget/dt/GitSpace.Git.svg)](https://www.nuget.org/packages/GitSpace.Git) | Asynchronous, shell-free system Git backend with cancellation, serialization and bounded output |
| [GitSpace.Hosting.GitHub](https://www.nuget.org/packages/GitSpace.Hosting.GitHub) | [![NuGet](https://img.shields.io/nuget/vpre/GitSpace.Hosting.GitHub.svg)](https://www.nuget.org/packages/GitSpace.Hosting.GitHub) | [![Downloads](https://img.shields.io/nuget/dt/GitSpace.Hosting.GitHub.svg)](https://www.nuget.org/packages/GitSpace.Hosting.GitHub) | Bounded GitHub pull-request HTTP client with session-scoped credentials |
| [GitSpace.Rendering.Skia](https://www.nuget.org/packages/GitSpace.Rendering.Skia) | [![NuGet](https://img.shields.io/nuget/vpre/GitSpace.Rendering.Skia.svg)](https://www.nuget.org/packages/GitSpace.Rendering.Skia) | [![Downloads](https://img.shields.io/nuget/dt/GitSpace.Rendering.Skia.svg)](https://www.nuget.org/packages/GitSpace.Rendering.Skia) | Framework-independent, viewport-only Skia unified/split diff renderer |
| [GitSpace.Controls.Uno](https://www.nuget.org/packages/GitSpace.Controls.Uno) | [![NuGet](https://img.shields.io/nuget/vpre/GitSpace.Controls.Uno.svg)](https://www.nuget.org/packages/GitSpace.Controls.Uno) | [![Downloads](https://img.shields.io/nuget/dt/GitSpace.Controls.Uno.svg)](https://www.nuget.org/packages/GitSpace.Controls.Uno) | Uno toolbar tiles, changed-file and history lists, commit composer, conflict resolver, diff viewer and splitter |
| [GitSpace.Workbench.Uno](https://www.nuget.org/packages/GitSpace.Workbench.Uno) | [![NuGet](https://img.shields.io/nuget/vpre/GitSpace.Workbench.Uno.svg)](https://www.nuget.org/packages/GitSpace.Workbench.Uno) | [![Downloads](https://img.shields.io/nuget/dt/GitSpace.Workbench.Uno.svg)](https://www.nuget.org/packages/GitSpace.Workbench.Uno) | Complete embeddable Git client workspace and dialogs with an injected platform and backend |

Dependencies follow the real project references: `Core ← Git`, `Diff ← Rendering.Skia`, `Core + Rendering.Skia ← Controls.Uno`, `Controls.Uno + Hosting.GitHub ← Workbench.Uno`; `Core`, `Diff` and `Hosting.GitHub` have no GitSpace dependencies. Individual controls can be used without the workbench; see [architecture and embedding](docs/architecture.md).

### GitSpace.Core

The backend-neutral contract shared by the desktop backend, the browser worker and the UI: operation requests, results and repository snapshots, the `IGitBackend` interface, web-style JSON helpers and validation for relative paths, ref names, commit IDs and HTTPS remotes. Reference it to write another backend or to drive any backend headlessly. No dependencies and no UI.

```sh
dotnet add package GitSpace.Core
```

**Key types**

- `IGitBackend` — `ExecuteAsync(GitRequest)`, `Capabilities` (supported operation names) and `DisplayName`.
- `GitRequest` — an operation (`"open"`, `"diff"`, `"commit"`, …) with `Root`, `Path`, `Paths`, `Message`, `Value`, `ExpectedHead`.
- `GitResult` / `GitSnapshot` — diff text and the repository state (`Branch`, `Changes`, `Commits`, `Branches`, `Remotes`, `Ahead`/`Behind`).
- `GitChange`, `GitCommit`, `GitRemote`, `GitStash` — snapshot records.
- `GitSafety` / `GitJson` / `GitText` — validation, serialization and line-ending helpers.

**Usage**

```csharp
using GitSpace.Core;

var request = new GitRequest("commit")
{
    Message = "Update documentation",
    Paths = [GitSafety.RelativePath("docs/guide.md")],   // rejects absolute and ../ paths
    Author = "Ada Lovelace",
    Email = "ada@example.com"
};
string branch = GitSafety.Ref("feature/review");          // validated ref name
string remote = GitSafety.HttpsRemote("https://github.com/wieslawsoltes/GitSpace.git");

string json = GitJson.Serialize(request);                  // same wire format as the browser worker
GitRequest roundTrip = GitJson.Deserialize<GitRequest>(json);
var commit = new GitCommit("0123456789abcdef", "Ada", "ada@example.com", "2026-01-01", "Initial commit\n\nBody", []);
Console.WriteLine($"{commit.ShortId} {commit.Summary}");
```

### GitSpace.Diff

Pure text algorithms behind the review UI: a bounded Myers line diff that reports a coarse replacement instead of exceeding its work budget, split-view alignment, whitespace-insensitive comparison, exact line/hunk selection for partial staging, merge/diff3 conflict parsing and resolution, and viewport row arithmetic. No dependencies and no UI.

```sh
dotnet add package GitSpace.Diff
```

**Key types**

- `DiffEngine.Compare` — returns a `DiffDocument` (`Lines`, `Additions`, `Deletions`, `Coarse`, `Split()`).
- `DiffLine` / `SplitLine` / `DiffKind` — unified rows and aligned left/right pairs.
- `LineSelection` — `HunkAt(row)` and `Apply(rows)` to build partially staged text, preserving line endings.
- `ConflictDocument` — parses conflict markers into `ConflictBlock`s and `Resolve`s them.
- `DiffEngine.VisibleRange` — first/last visible row for a scroll position.

**Usage**

```csharp
using GitSpace.Diff;

var before = "alpha\nbeta\ngamma\n";
var after = "alpha\nBETA\ngamma\ndelta\n";

DiffDocument diff = DiffEngine.Compare(before, after, ignoreWhitespace: false);
Console.WriteLine($"+{diff.Additions} -{diff.Deletions}, coarse: {diff.Coarse}");
foreach (var line in diff.Lines)
    Console.WriteLine($"{line.Kind,-8} {line.OldLine,3} {line.NewLine,3}  {line.Text}");
IReadOnlyList<SplitLine> sideBySide = diff.Split();

// Stage only the hunk that touches row 1 (beta -> BETA), not the appended line.
var selection = new LineSelection(before, after);
string staged = selection.Apply(selection.HunkAt(1));

var conflict = new ConflictDocument("<<<<<<< ours\nA\n=======\nB\n>>>>>>> theirs\n");
string resolved = conflict.Resolve([ConflictChoice.Incoming]);   // "B\n"
```

### GitSpace.Git

The desktop backend: runs the installed Git (2.30+) as an asynchronous, cancellable process with argument lists (no shell), hooks disabled, one command at a time per backend and bounded output, and parses porcelain status and history. It implements `IGitBackend` for review, staging, commits, history, branches, tags, stashes, remotes, synchronization, merge/rebase/revert/cherry-pick and conflicts. Depends on `GitSpace.Core`; no UI. Only open repositories you trust: Git configuration, filters and helpers can still run programs.

```sh
dotnet add package GitSpace.Git
```

**Key types**

- `DesktopGitBackend` — `IGitBackend` over system Git; check `Capabilities` for supported operations.
- `GitProcess` — low-level `RunAsync(directory, arguments)` returning `GitProcessResult`.
- `StatusParser` — `Parse` porcelain status into `GitChange[]`, `Log` into `GitCommit[]`.
- `GitCommandException` — a failed Git command with its `ExitCode`.
- `RepositoryPath` — canonical repository directory comparison.

**Usage**

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
Console.WriteLine(diff.Binary ? "binary file" : $"{diff.Before.Length} -> {diff.After.Length} chars");

await git.ExecuteAsync(new GitRequest("commit")
{
    Message = "Update README",
    Paths = ["README.md"],
    ExpectedHead = opened.Snapshot.Head          // refuse if HEAD moved meanwhile
});
```

### GitSpace.Hosting.GitHub

A small GitHub REST client for pull requests: list the first 100 open pull requests and create one, with explicit API version headers, a 4 MiB response limit and a token passed per call (never stored). Bring your own `HttpClient`. No GitSpace dependencies and no UI.

```sh
dotnet add package GitSpace.Hosting.GitHub
```

**Key types**

- `GitHubClient` — `ListPullRequestsAsync`, `CreatePullRequestAsync` over a caller-owned `HttpClient`.
- `GitHubClient.TryParseRemote` — extracts owner/repository from a GitHub remote URL.
- `PullRequest` — number, title, author, head/base branches, URL and draft flag.

**Usage**

```csharp
using GitSpace.Hosting.GitHub;

using var http = new HttpClient();
var github = new GitHubClient(http);

if (GitHubClient.TryParseRemote("https://github.com/wieslawsoltes/GitSpace.git", out var owner, out var repository))
{
    foreach (PullRequest pr in await github.ListPullRequestsAsync(owner, repository))
        Console.WriteLine($"#{pr.Number} {pr.Title} ({pr.Head} -> {pr.Base}){(pr.Draft ? " draft" : "")}");

    string token = Environment.GetEnvironmentVariable("GITHUB_TOKEN") ?? "";
    // PullRequest created = await github.CreatePullRequestAsync(owner, repository, "Title", "feature/x", "main", "Body", token);
}
```

### GitSpace.Rendering.Skia

Draws a `DiffDocument` onto any `SKCanvas` as a unified or split diff with line-number gutters, addition/deletion colors, intraline emphasis, row selection and light/dark palettes. Only visible rows are visited; paints, fonts, text runs and split alignment are cached. Use it to render diffs without Uno. Depends on `GitSpace.Diff` and SkiaSharp 3.119; no UI framework.

```sh
dotnet add package GitSpace.Rendering.Skia
```

**Key types**

- `DiffRenderer` — `SetDocument`, `Draw(canvas, bounds, viewport)`, `RowCount(split)`, frame statistics; `DefaultTypeface`.
- `DiffViewport` — scroll offsets, `FontSize`/`RowHeight`, `Split`, selected rows and `Palette`.
- `DiffPalette` — `Dark` and `Light` color sets, or your own.

**Usage**

```csharp
using GitSpace.Diff;
using GitSpace.Rendering.Skia;
using SkiaSharp;

var diff = DiffEngine.Compare("alpha\nbeta\n", "alpha\nBETA\ngamma\n");

using var renderer = new DiffRenderer();
renderer.SetDocument(diff);
var view = new DiffViewport { Split = true, FontSize = 13, Palette = DiffPalette.Light };

using var surface = SKSurface.Create(new SKImageInfo(1000, 240));
renderer.Draw(surface.Canvas, new SKRect(0, 0, 1000, 240), view);
Console.WriteLine($"{renderer.RowCount(view.Split)} rows, {renderer.LastVisibleRows} visible");

using var png = surface.Snapshot().Encode(SKEncodedImageFormat.Png, 100);
File.WriteAllBytes("diff.png", png.ToArray());
```

### GitSpace.Controls.Uno

Compact custom Uno Platform controls for Git clients: an `SKCanvasElement`-based `DiffViewer` with scrollbars, selection, find and copy; changed-file list with inclusion checkboxes; paged history list; commit composer; conflict resolver; repository tiles, buttons, segmented selectors, icons, pane splitter and a light/dark `GitTheme`. Depends on Core and Rendering.Skia; requires Uno Platform (Skia renderer).

```sh
dotnet add package GitSpace.Controls.Uno
```

**Key types**

- `DiffViewer` — `SetDocument`, `SetSplit`, `Zoom`, `FindNext`, `CopySelection`, `SelectedChangedRows`.
- `ChangedFilesView` — `SetFiles`, `SelectedPaths`, `FileSelected`.
- `HistoryView` — `SetCommits`, `CommitSelected`, `LoadMoreRequested`.
- `CommitComposer` — summary/description boxes, `Message`, `CommitRequested`.
- `ConflictResolver`, `RepositoryTile`, `GitButton`, `PaneSplitter`, `GitTheme` — dialogs and chrome.

**Usage**

```csharp
using GitSpace.Controls.Uno;
using GitSpace.Core;
using GitSpace.Diff;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

var files = new ChangedFilesView { Width = 280 };
files.SetFiles([new GitChange("README.md"), new GitChange("docs/guide.md", "A")], active: "README.md");

var diff = new DiffViewer();
diff.SetDocument(DiffEngine.Compare("alpha\nbeta\n", "alpha\nBETA\n"));
diff.SetSplit(true);
files.FileSelected += (_, path) => Console.WriteLine("show diff for " + path);

var root = new Grid();
root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
root.ColumnDefinitions.Add(new ColumnDefinition());
root.Children.Add(files);
root.Children.Add(diff);
Grid.SetColumn(diff, 1);

var window = new Window { Title = "Changes", Content = root };
window.Activate();
```

### GitSpace.Workbench.Uno

The complete GitHub Desktop-style workspace as one `Grid`: toolbar tiles, Changes/History tabs, commit composer, diff workspace, dialogs for repositories, remotes, stashes, pull requests, conflicts and preferences, and busy/error handling. Hosts inject an `IWorkbenchPlatform` that supplies the `IGitBackend` (for example `DesktopGitBackend`), preference storage, external navigation and downloads; `DisposeAsync` also disposes the backend. Depends on Controls.Uno and Hosting.GitHub; requires Uno Platform.

```sh
dotnet add package GitSpace.Workbench.Uno
```

**Key types**

- `WorkbenchView` — `InitializeAsync`, `Snapshot`, `IsReady`/`IsBusy`, `Diff`, `StateChanged`, `RefreshOnActivationAsync`, `DisposeAsync`.
- `IWorkbenchPlatform` — `Backend`, `IsBrowser`, preferences, `OpenExternalAsync`, `DownloadAsync`.
- `WorkspacePreferences` — author identity, recent repositories, theme and diff options (non-secret).

**Usage**

```csharp
using GitSpace.Core;
using GitSpace.Git;
using GitSpace.Workbench.Uno;
using Microsoft.UI.Xaml;

sealed class DesktopPlatform : IWorkbenchPlatform
{
    private WorkspacePreferences _preferences = new() { Author = "Ada Lovelace", Email = "ada@example.com" };
    public IGitBackend Backend { get; } = new DesktopGitBackend();
    public bool IsBrowser => false;
    public Task<WorkspacePreferences> LoadPreferencesAsync() => Task.FromResult(_preferences);
    public Task SavePreferencesAsync(WorkspacePreferences preferences) { _preferences = preferences; return Task.CompletedTask; }
    public Task OpenExternalAsync(string httpsUrl) => Task.CompletedTask;          // launch a browser here
    public Task DownloadAsync(string fileName, string base64Content) =>
        File.WriteAllBytesAsync(fileName, Convert.FromBase64String(base64Content));
}

// In Application.OnLaunched:
var window = new Window { Title = "GitSpace" };
var workbench = new WorkbenchView(new DesktopPlatform());
window.Content = workbench;
window.Activate();
await workbench.InitializeAsync();
window.Closed += async (_, _) => await workbench.DisposeAsync();
```

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
