# @gitspace/browser-git

MIT-licensed, reusable real Git backend. `createBackend(fs, http)` accepts an isomorphic-git filesystem and HTTP client and exposes a serialized `execute(request)` API. `worker.mjs` installs the same backend on an isolated worker, with LightningFS/IndexedDB persistence and an origin-wide Web Lock.

```js
import { createBackend } from '@gitspace/browser-git';
const backend = createBackend(fs, http);
await backend.execute({ operation: 'init', root: 'MyProject' });
await backend.execute({ operation: 'write', path: 'README.md', message: '# Hello\n' });
const result = await backend.execute({
  operation: 'commit', paths: ['README.md'], message: 'Initial commit',
  author: 'Your Name', email: 'you@example.com'
});
console.log(result.snapshot.head);
```

Run `npm ci`, `npm test`, and `npm run build`. The npm entry point is the independently bundled `dist/backend.js`; downstream consumers need no runtime dependencies. The built worker includes dependencies; it makes no CDN requests. `dist/worker.js` must be hosted on the application's origin. GitHub HTTPS clone/fetch/push generally require a separately configured trusted CORS proxy. There is no default public proxy. A proxy can see both repository contents and any token sent through it. Tokens are held only in memory, never in Git configuration or IndexedDB.

Only the advertised `capabilities` are supported. Browser merge/pull are fast-forward-only. Browser stash is restricted to tracked files in loose-object repositories and restores only into a clean worktree. No rebase/cherry-pick/revert, SSH, Git LFS, submodule UI, credential-manager integration or filesystem-directory mounting is claimed. Export creates a ZIP containing real `.git` objects and working files, limited to 64 MiB, without symlinks. Browser storage can be evicted: export or push important work.

## Guarded line staging and performance

`review` with `value: "unstaged"` returns index/working content and content-version hashes. `stageText` supplies the reviewed hashes and desired index text; it never rewrites the worktree. `unstageText` performs the inverse from a `value: "staged"` review. Set `remove: true` only when the reviewed target deletes the file. `commitStaged` accepts the latest snapshot `indexHash` and commits the current index without restaging working changes. UI-independent line/hunk selection is in `GitSpace.Diff.LineSelection`.

Snapshots cache immutable history by repository, HEAD, shallow boundary and history limit. Status is still refreshed. `history` increases the requested history limit up to 2,000 commits with lookahead. Source and distributed-bundle tests check real index/commit contents.

The build replaces the LGPL path helper embedded in isomorphic-git 1.42.3 with GitSpace's independently implemented MIT helper. It fails closed if the audited upstream layout changes or the removed helper remains. Upstream dependencies are build-only; distributed backend/worker bundles retain remaining permissive license notices.
