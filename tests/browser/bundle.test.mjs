import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { execFileSync } from 'node:child_process';
import { createBackend } from '../../src/GitSpace.BrowserGit/dist/backend.js';

test('distributed permissive bundle performs real Git partial commits', async () => {
  for (const file of ['backend.js', 'worker.js']) {
    const source = fs.readFileSync(new URL('../../src/GitSpace.BrowserGit/dist/' + file, import.meta.url), 'utf8');
    assert.doesNotMatch(source, /LGPL|@zenfs\/core\/path|function normalizeString\(/);
  }
  const base = fs.mkdtempSync(path.join(os.tmpdir(), 'gitspace-distributed-'));
  const backend = createBackend(fs, undefined, { base });
  const run = (operation, fields = {}) => backend.execute({ operation, author: 'Bundle test', email: 'bundle@example.com', ...fields });
  try {
    await run('init', { root: 'Project' }); await run('write', { path: 'folder/test.txt', message: 'first\nsecond\n' });
    await run('commit', { paths: ['folder/test.txt'], message: 'Initial' });
    await run('write', { path: 'folder/test.txt', message: 'FIRST\nSECOND\n' });
    const review = await run('review', { path: 'folder/test.txt' });
    const changed = await run('stageText', { path: 'folder/test.txt', beforeHash: review.beforeHash, afterHash: review.afterHash, message: 'FIRST\nsecond\n' });
    await run('commitStaged', { indexHash: changed.snapshot.indexHash, message: 'First line only' });
    assert.equal((await run('diff', { path: 'folder/test.txt' })).before, 'FIRST\nsecond\n');
    assert.equal((await run('read', { path: 'folder/test.txt' })).text, 'FIRST\nSECOND\n');
    execFileSync('git', ['fsck', '--full', '--no-dangling'], { cwd: path.join(base, 'Project'), stdio: 'pipe' });
  } finally { fs.rmSync(base, { recursive: true, force: true }); }
});
