import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { createBackend } from '../../src/GitSpace.BrowserGit/backend.mjs';

const base = fs.mkdtempSync(path.join(os.tmpdir(), 'gitspace-review-'));
const backend = createBackend(fs, undefined, { base });
let snapshot;
async function run(operation, fields = {}) {
  const result = await backend.execute({ operation, author: 'Review Test', email: 'review@example.com', ...fields });
  if (result.snapshot) snapshot = result.snapshot;
  return result;
}
async function review(path, value = 'unstaged') { return run('review', { path, value }); }
async function update(operation, path, viewed, message, remove = false) {
  return run(operation, { path, beforeHash: viewed.beforeHash, afterHash: viewed.afterHash, message, remove });
}
test('guarded partial index editing and immutable history caching', async t => {
  try {
    await run('init', { root: 'Review' });
    await run('write', { path: 'a.txt', message: '\uFEFFone\r\ntwo\r\n' });
    await run('commit', { paths: ['a.txt'], message: 'Initial' });
    await t.test('BOM and CRLF survive read, diff and staged review', async () => {
      assert.equal((await run('read', { path: 'a.txt' })).text, '\uFEFFone\r\ntwo\r\n');
      assert.equal((await review('a.txt', 'staged')).after, '\uFEFFone\r\ntwo\r\n');
    });
    await t.test('partial staging never changes worktree or unrelated index entries', async () => {
      await run('write', { path: 'other.txt', message: 'unrelated\n' });
      await run('stage', { paths: ['other.txt'] });
      await run('write', { path: 'a.txt', message: '\uFEFFONE\r\nTWO\r\n' });
      const viewed = await review('a.txt');
      await update('stageText', 'a.txt', viewed, '\uFEFFONE\r\ntwo\r\n');
      assert.equal((await review('a.txt', 'staged')).after, '\uFEFFONE\r\ntwo\r\n');
      assert.equal((await run('read', { path: 'a.txt' })).text, '\uFEFFONE\r\nTWO\r\n');
      assert.equal((await review('other.txt', 'staged')).after, 'unrelated\n');
    });
    await t.test('stale worktree content is refused', async () => {
      const viewed = await review('a.txt');
      await run('write', { path: 'a.txt', message: 'newer\n' });
      await assert.rejects(update('stageText', 'a.txt', viewed, 'wrong\n'), /changed/);
      assert.equal((await review('a.txt', 'staged')).after, '\uFEFFONE\r\ntwo\r\n');
    });
    await t.test('stale index content is refused independently of HEAD', async () => {
      const viewed = await review('a.txt'); await run('stage', { paths: ['a.txt'] });
      await assert.rejects(update('stageText', 'a.txt', viewed, 'wrong\n'), /changed/);
    });
    await t.test('partial unstage keeps the working tree intact', async () => {
      await run('write', { path: 'a.txt', message: '\uFEFFONE\r\nTWO\r\n' }); await run('stage', { paths: ['a.txt'] });
      await update('unstageText', 'a.txt', await review('a.txt', 'staged'), '\uFEFFone\r\nTWO\r\n');
      assert.equal((await review('a.txt', 'staged')).after, '\uFEFFone\r\nTWO\r\n');
      assert.equal((await run('read', { path: 'a.txt' })).text, '\uFEFFONE\r\nTWO\r\n');
    });
    await t.test('staged commit does not restage excluded working edits', async () => {
      await run('unstage', { paths: ['other.txt'] });
      await run('commitStaged', { message: 'Selected lines only', indexHash: snapshot.indexHash });
      assert.equal((await run('diff', { path: 'a.txt' })).before, '\uFEFFone\r\nTWO\r\n');
      assert.equal((await run('read', { path: 'a.txt' })).text, '\uFEFFONE\r\nTWO\r\n');
    });
    await t.test('staged commit rejects a changed index', async () => {
      const hash = snapshot.indexHash; await run('stage', { paths: ['other.txt'] });
      await assert.rejects(run('commitStaged', { message: 'Stale', indexHash: hash }), /changed/);
      await run('unstage', { paths: ['other.txt'] });
    });
    await t.test('unstaging an added file removes only its index entry', async () => {
      await run('stage', { paths: ['other.txt'] });
      await update('unstageText', 'other.txt', await review('other.txt', 'staged'), '', true);
      assert.equal((await review('other.txt', 'staged')).afterExists, false);
      assert.equal((await run('read', { path: 'other.txt' })).text, 'unrelated\n');
    });
    await t.test('empty existing files are distinct from deletion', async () => {
      await run('write', { path: 'empty.txt', message: '' });
      const viewed = await review('empty.txt'); assert.equal(viewed.afterExists, true); assert.equal(viewed.beforeExists, false);
      await assert.rejects(update('stageText', 'empty.txt', viewed, '', true), /not a deletion/);
      await update('stageText', 'empty.txt', viewed, ''); assert.equal((await review('empty.txt', 'staged')).afterExists, true);
    });
    await t.test('warm refresh reuses history but still detects working edits', async () => {
      const count = backend.diagnostics.historyReads;
      await run('refresh'); await run('refresh');
      assert.equal(backend.diagnostics.historyReads, count);
      fs.writeFileSync(path.join(base, 'Review', 'external.txt'), 'External editor\n');
      await run('refresh'); assert.ok(snapshot.changes.some(f => f.path === 'external.txt'));
      assert.equal(backend.diagnostics.historyReads, count);
    });
    await t.test('history load limit and lookahead report more commits accurately', async () => {
      await run('history', { limit: 1 }); assert.equal(snapshot.commits.length, 1); assert.equal(snapshot.hasMoreHistory, true);
      await run('history', { limit: 200 }); assert.equal(snapshot.commits.length, 2); assert.equal(snapshot.hasMoreHistory, false);
    });
    await t.test('binary review cannot become a lossy text edit', async () => {
      fs.writeFileSync(path.join(base, 'Review', 'binary.txt'), Buffer.from([255, 0, 1]));
      const result = await review('binary.txt'); assert.equal(result.binary, true);
      await assert.rejects(update('stageText', 'binary.txt', result, 'lossy'), /UTF-8/);
    });
  } finally { fs.rmSync(base, { recursive: true, force: true }); }
});
