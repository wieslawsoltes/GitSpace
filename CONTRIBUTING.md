# Contributing

Keep each reusable package independent of `GitSpace.App`. Put repository semantics in a backend, visual primitives in Controls/Rendering, and orchestration in Workbench. Avoid adding process or JavaScript calls to reusable UI components.

Every new Git command needs validation, explicit capability advertisement, meaningful failures and regression tests over a real temporary repository. Destructive operations need confirmation. Do not print tokens, request objects with tokens, or private repository contents into CI diagnostics.

For UI work, add a real Chromium interaction test and inspect its screenshot. A diagnostic-state assertion alone is not proof that a rendered control works. Keep app startup, idle rendering and large-file behavior observable; do not add perpetual render loops or unbounded file/diff operations.

Run the tests and build the affected desktop/browser targets as described in [the development guide](docs/development.md). Update [the compatibility matrix](docs/compatibility.md) when adding or changing behavior. Do not describe unqualified functionality as full upstream parity.
