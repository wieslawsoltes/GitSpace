// esbuild injects this import only where a dependency uses an unbound Buffer.
// buffer 6.0.3 is pinned in the resolved isomorphic-git dependency graph.
// This keeps the worker self-contained without adding globals to the host page.
export { Buffer } from 'buffer';
