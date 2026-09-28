# Architecture and embedding

## Dependency direction

```text
GitSpace.App (desktop + WebAssembly platform adapters)
  └─ GitSpace.Workbench.Uno
       ├─ GitSpace.Controls.Uno
       │    ├─ GitSpace.Rendering.Skia → GitSpace.Diff
       │    └─ GitSpace.Core
       └─ GitSpace.Hosting.GitHub

Desktop adapter → GitSpace.Git → GitSpace.Core → installed git process
Browser adapter → same-origin Worker → @gitspace/browser-git → IndexedDB
```

The application project contains only startup, platform persistence/navigation/downloads, browser RPC and font initialization. Workbench code does not execute processes or JavaScript. The backend API is independently reusable; a different backend can implement the same `IGitBackend` contract without changing the diff renderer or controls.

## Command contract

`GitRequest` names an explicit operation and carries typed, bounded data: repository root, path, value, paths, message, author, expected HEAD and confirmation. It never carries a shell command. `GitResult` contains a snapshot, before/after text, file changes or text payload. `IGitBackend.Capabilities` is the authority for operation availability. Unsupported operations throw; they do not modify status to imitate success.

Repository writes are serialized by each backend. Desktop requests use a semaphore, asynchronous process I/O and an argument list. Browser requests use a promise queue inside a worker, plus a Web Lock across tabs when supported. Browser calls poll only while a command is outstanding, rather than maintaining a permanently ticking UI timer.

`ExpectedHead` detects intervening HEAD changes before mutations. It does not lock out arbitrary external Git processes or detect every working-file race. File editing separately rereads the file before saving and refuses to overwrite a changed buffer. Filesystem race-proof sandboxing and cross-process transaction isolation are not claimed.

## Desktop Git

GitSpace executes the installed `git` process directly with `UseShellExecute=false`. User paths and messages are separate `ArgumentList` entries. Explicit file pathspecs are literal, and large selected-file sets are passed as NUL-delimited standard input. Literal-path mode is deliberately not global: applying it to Git's internal stash helpers leaves untracked files behind on supported Git versions.

Repository hooks are redirected to an empty, per-backend directory. Git configuration and filters remain powerful; repositories must be trusted. Native credentials are delegated to installed Git and its credential helper. Interactive terminal prompting is disabled. The process wrapper cancels the process tree, enforces a ten-minute timeout and bounds each captured output stream to 16 MiB.

Snapshots include porcelain-v1 NUL-delimited status, latest 200 commits, local branches/tags/remotes/stashes, upstream ahead/behind counts, and active merge/rebase/cherry-pick/revert state. Multiple independent read commands are started concurrently. Writes remain serialized.

Selected-file commits use `commit --only` after staging the selection, preserving unrelated staged changes. In contrast, the browser backend refuses to commit a selection while unselected content is already staged, because that backend's index commit API cannot preserve the native command's semantics safely.

## Browser Git

`@gitspace/browser-git` accepts a filesystem and HTTP client. The default worker uses LightningFS, backed by IndexedDB. The filesystem contains actual Git objects, refs, index and working files. The tutorial and new repositories are not in-memory Git simulations.

The bundled worker is hosted at `git/worker.js` on the application's origin, with no CDN dependency or cross-origin worker redirect. Browser networking uses HTTPS; no public proxy is preconfigured. Access tokens are transported only within the in-memory command and are redacted from worker error messages. Tokens are never written into Git configuration or persisted preferences.

Browser pull and merge are fast-forward-only. Stash is guarded to tracked files and loose-object repositories; apply requires a clean worktree because the underlying stash implementation does not perform full conflict-safe restoration. Browser ahead/behind accounting is not implemented; the UI deliberately omits native count labels in that backend.

Storage is origin-scoped, not a secure repository vault. Other trusted applications under the same origin can potentially access the same browser storage. A dedicated origin is recommended for private work. Export/push before clearing storage or relying on long-term persistence.

## Diff and rendering

`GitSpace.Diff` has no UI or Skia dependency. It computes a line-level Myers shortest edit script, strips common prefixes/suffixes, and bounds work/trace storage. The exact path is checked against an independent dynamic-programming edit-distance oracle in 1,000 deterministic randomized trials. Coarse fallback is explicit and still reconstructs both sides.

`GitSpace.Rendering.Skia` consumes a `DiffDocument` and `DiffViewport`. It owns cached paints/fonts and split alignment, draws only the visible range, and uses clipping rather than a control per line. The control layer owns scrolling, keyboard/pointer input and clipboard integration. Embedders can inject a licensed `SKTypeface` through `DiffRenderer.DefaultTypeface`; the bundled browser app initially uses the font asset supplied by Uno, while desktop resolves its system monospace family.

No separate render loop competes with Uno. `SKCanvasElement.Invalidate()` schedules drawing through Uno's compositor. The UI does not claim a proprietary WebGPU engine or guaranteed hardware acceleration. The headless browser suite uses SwiftShader; real-device Metal/OpenGL/Vulkan/WebGL qualification is separate work.

## Performance boundaries

Text preview/editing is limited to 2 MiB per side. Git output is bounded to 16 MiB per stream. Individual rendered lines are clipped after 12,000 characters. History currently loads 200 commits rather than offering unbounded pagination. ZIP export caps input at 64 MiB. The exact diff trace budget is approximately 32 MiB; algorithm input, output, string and row allocations are additional.

The current file/history list implementation uses ListView scrolling/container virtualization but constructs row contents from the snapshot. Full data-driven recycling for very large status sets, background incremental diffing, repository filesystem watchers, patch/hunk streaming, font shaping/fallback qualification and large-monorepo benchmarks remain work. These boundaries are intentional disclosures, not claims of already completed scaling.

## Embedding

```csharp
// A desktop host can reuse GitSpace.Git and provide preferences/navigation adapters.
IWorkbenchPlatform platform = new MyPlatformAdapter();
var view = new WorkbenchView(platform);
window.Content = view;
window.Activate();
await view.InitializeAsync();
// Dispose the view when closing; it owns the injected backend's lifetime.
```

Individual controls communicate through events and explicit state updates. `CommitComposer` raises `CommitRequested`; `ChangedFilesView` exposes selected paths and selection events; `HistoryView` raises `CommitSelected`; `DiffViewer` accepts a computed diff document. They do not access repositories or network services themselves.

The portable GitHub HTTP client accepts an injected `HttpClient`, applies authorization per request rather than through global default headers, caps responses, and uses a fixed GitHub API origin. Enterprise-server support and OAuth flows are not part of this preview.
