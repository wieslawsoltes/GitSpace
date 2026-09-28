# Security

GitSpace is an early development preview. Do not treat it as a security boundary or the only copy of important work.

## Repository trust

Open only repositories you trust. Desktop Git commands use argument lists, never shell-concatenated user input. Repository hooks are disabled through an empty hooks path. Nevertheless, installed Git configuration, clean/smudge filters, credential helpers, SSH configuration and other external tools can execute programs. GitSpace does not sandbox Git or make an untrusted working tree safe.

The text editor rejects traversal, `.git` metadata and symlink/reparse-point paths, and checks for intervening file changes. These are safety guards, not race-proof filesystem isolation. External processes can still change repositories between checks.

## Secrets and networking

Session GitHub tokens are not written to preferences, localStorage, IndexedDB or Git configuration. They are passed per request, redacted from backend errors and omitted from diagnostic state. Desktop Git credentials remain the responsibility of your installed Git credential helper. Do not embed credentials in remote URLs; new remote and clone URLs reject them.

Browser Git transport may require a CORS proxy. No public proxy is enabled by default. A configured proxy can see private repository data and any token sent through it. Use only a proxy you control or explicitly trust, and use narrowly scoped tokens. The application does not implement OAuth, secure OS token storage, enterprise policy enforcement or independent penetration-test qualification.

## Browser storage

Browser repositories are stored in origin-scoped IndexedDB, not an encrypted vault. Applications hosted under the same origin may access the same storage. GitHub Pages project paths do not provide separate origin isolation. Host private workflows on a dedicated trusted origin. Browser data may be evicted, and closing the tab during a Git write can interrupt it. Export or push important work regularly.

The ZIP export includes `.git`, working files and configuration. Treat it as sensitive. The export currently rejects symlinks and caps input at 64 MiB.

## Destructive operations

Discard, amend, stash drop, branch deletion and history-changing commands require explicit confirmation. Discard never deletes untracked files. Force push, forced branch deletion and automatic conflict-resolution shortcuts are not implemented. Read the operation's result: a failed Git command can leave a merge/rebase/conflict in progress.

## Reporting

Report vulnerabilities privately through the repository's private vulnerability reporting feature when available, rather than posting credentials or private repository contents in a public issue. Include the commit/version, platform, minimal reproduction and impact. Never attach real tokens or a private `.git` directory to public reports.
