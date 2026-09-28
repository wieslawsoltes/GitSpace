# Compatibility and parity boundaries

GitSpace 0.1.0 is a functioning preview, not a complete or pixel-exact GitHub Desktop replacement. This matrix distinguishes the implemented code from platform limitations and unqualified behavior. A green build is not certification of every advertised upstream Git feature.

| Capability | Desktop | Browser |
| --- | --- | --- |
| Shared custom Uno UI | Yes | Same compiled Uno UI |
| Repository storage | Ordinary local Git working tree | LightningFS/IndexedDB repository |
| Open/init | Yes | Isolated named browser repositories |
| HTTPS clone | Installed Git | Worker; usually requires configured CORS proxy for GitHub |
| Existing SSH remotes | Delegated to installed Git configuration | No SSH transport |
| New SSH remote/clone UI | Not implemented | Not implemented |
| Status and file selection | Yes | Yes |
| File stage/unstage | Yes | Yes |
| Selected-file commits | Preserve unrelated staged files using native Git | Refuse unrelated staged content before committing selection |
| Amend latest commit | Yes, confirmed | Yes, confirmed |
| Partial-line/hunk staging | Not implemented | Not implemented |
| Unified/split text review | Yes | Yes |
| Intraline split highlights | Common-prefix/suffix span | Same renderer |
| Binary/image diff | Explicit unsupported preview | Explicit unsupported preview |
| Text encoding | Bounded UTF-8 editor/preview | Bounded UTF-8 editor/preview |
| Local branches/tags | Create/switch/rename/safe delete; create/delete tag in backend | Equivalent advertised subset |
| Fetch/pull/push | Installed Git; pull ff-only, no force push | HTTPS; pull ff-only, no force push |
| Ahead/behind | Native upstream counts | Not implemented; omitted from toolbar labels |
| Merge | Native Git; conflicts remain unresolved for review | Fast-forward-only |
| Rebase, revert, cherry-pick | Native commands, confirmation, continue/abort | Not implemented |
| Graphical conflict resolver | Not implemented; edit markers and stage manually | Not implemented |
| Interactive rebase/squash/reorder | Not implemented | Not implemented |
| Stash | Includes tracked and untracked files; apply retains backup | Tracked/loose-object subset, clean worktree required for apply |
| History | Latest 200 commits; per-commit file comparison | Latest 200 commits; per-commit file comparison |
| Pull requests | GitHub REST list/create/open | Same REST client |
| OAuth/account switching | Not implemented; Git helper/session REST token | Not implemented; session token |
| GitHub Enterprise | Not implemented in hosting UI | Not implemented |
| LFS/submodule/worktree UI | Not implemented; native Git may encounter configured functionality | Not implemented |
| Hooks | Disabled for GitSpace commands | No native hook execution |
| Native filesystem/editor/shell integration | Path-based open and text editor; richer integration pending | No direct local-folder mounting |
| ZIP backup | Repository already exists as normal folder | Real .git + worktree, 64 MiB input cap, symlinks rejected |
| Preferences | Local JSON, no tokens | LocalStorage, no tokens |
| Accessibility | Accessible control names and keyboard navigation; incomplete diff text peer | Same; browser mapping must be enabled where requested by Uno |
| Localization | English | English |
| Installer/signing/updater | Not implemented; build/release archives | Static GitHub Pages deployment |

## UI fidelity

The layout follows GitHub Desktop's repository/branch toolbar, Changes/History sidebar, changed-file inclusion workflow and bottom commit composer. Light/dark themes, compact custom buttons, a splitter, and a custom-rendered diff surface are implemented. Native window decorations, menus, font metrics, icons, animations, context menus, preferences breadth and every original dialog are not pixel-qualified equivalents.

No GitHub artwork or account identity is used to imply affiliation. GitSpace is independently branded.

## Safety and data loss boundaries

Discard, amend, branch deletion, stash drop, merge/rebase and history-changing operations require explicit confirmation in their backend contract. Untracked files are not deleted by Discard. Force push and forced branch deletion are absent. The backend validates repository-relative paths and disallows editing `.git`, traversal paths and symbolic links.

These checks do not make arbitrary repositories safe. Native Git can invoke configured filters/helpers. External applications can race repository changes. Browser operations are not a full transactional filesystem; closing the page during a write can interrupt it. Back up important work, and use a dedicated browser origin for sensitive repositories.

## Qualification

The workflows contain portable algorithm/safety tests, real system-Git integration tests, browser-backend tests over real Git files, desktop compilation and real Chromium interactions against the compiled Uno app. Consult the exact workflow run and commit for the current results. Physical-GPU tests, manual Windows/macOS/Linux interaction qualification, external GitHub authentication/push tests, broad Unicode/font-shaping qualification, accessibility audits and large-monorepo performance qualification are not represented by these gates.
