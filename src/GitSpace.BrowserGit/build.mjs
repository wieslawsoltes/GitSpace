import { build } from 'esbuild';
import { readFile, mkdir } from 'node:fs/promises';
import path from 'node:path';

// isomorphic-git 1.42.3 labels the package MIT but contains an LGPL path helper.
// Replace that complete helper in distributed bundles, not merely its notice.
export async function replacePathHelper(source) {
  const marker = source.indexOf('This code for `path.join` is directly copied from @zenfs/core/path');
  if (marker < 0 || source.indexOf('This code for `path.join` is directly copied from @zenfs/core/path', marker + 1) >= 0) throw new Error('Unexpected upstream path-helper layout. Re-audit before upgrading.');
  const start = source.lastIndexOf('/*!', marker);
  const join = source.indexOf('function join(', marker);
  const end = source.indexOf('\n}', join) + 2;
  if (start < 0 || join < 0 || end <= join) throw new Error('Cannot safely isolate upstream helper.');
  const replacement = (await readFile(new URL('./path-join.mjs', import.meta.url), 'utf8')).replace('export function join', 'function join');
  const result = source.slice(0, start) + replacement + source.slice(end);
  if (/LGPL|@zenfs\/core\/path|function normalizeString\(/.test(result)) throw new Error('Upstream copyleft helper remains: refusing to distribute.');
  return result;
}
const plugin = {
  name: 'gitspace-permissive-path-helper',
  setup(build) {
    build.onLoad({ filter: /isomorphic-git[/\\]index\.(js|cjs)$/ }, async args => ({ contents: await replacePathHelper(await readFile(args.path, 'utf8')), loader: 'js', resolveDir: path.dirname(args.path) }));
  }
};
await mkdir('dist', { recursive: true });
for (const [entry, output] of [['worker.mjs', 'worker.js'], ['backend.mjs', 'backend.js']]) {
  await build({ entryPoints: [entry], outfile: 'dist/' + output, bundle: true, format: 'esm', platform: 'browser', target: 'es2022', inject: ['./buffer-shim.mjs'], plugins: [plugin], legalComments: 'inline' });
  const distributed = await readFile('dist/' + output, 'utf8');
  if (/LGPL|@zenfs\/core\/path/.test(distributed)) throw new Error('Non-permissive path helper appeared in output.');
}
