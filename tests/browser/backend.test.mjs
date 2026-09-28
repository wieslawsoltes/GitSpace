import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { createBackend, relativePath, reference } from '../../src/GitSpace.BrowserGit/backend.mjs';

const temporary = fs.mkdtempSync(path.join(os.tmpdir(), 'gitspace-browser-test-'));
const backend = createBackend(fs, undefined, { base: temporary });
let snapshot;
async function run(operation, fields = {}) {
  const result = await backend.execute({ operation, author: 'Test User', email: 'test@example.com', ...fields });
  if (result.snapshot) snapshot = result.snapshot;
  return result;
}

test('browser backend contract and real Git operations', async t => {
  try {
    await t.test('path and ref safety', () => {
      for (const p of ['', '../x', 'a/../b', '.git/config', 'a/.GIT/x', '/root/x', 'C:/x', 'a\\b', 'a\0b']) assert.throws(() => relativePath(p));
      assert.equal(relativePath('[literal]*.txt'), '[literal]*.txt');
      for (const r of ['-D', 'x..y', 'a b', 'HEAD~1', 'x.lock', 'x@{1}']) assert.throws(() => reference(r));
      assert.equal(reference('feature/test'), 'feature/test');
    });
    await t.test('initialize a real repository', async () => {
      await run('init', { root: 'Project' }); assert.equal(snapshot.branch, 'main');
      assert.ok(fs.existsSync(path.join(temporary, 'Project', '.git', 'HEAD')));
    });
    await t.test('write, stage and first commit', async () => {
      await run('write', { path: 'a.txt', message: 'one\n' }); assert.equal(snapshot.changes[0].status, 'A');
      await run('commit', { paths: ['a.txt'], message: 'Initial commit' }); assert.equal(snapshot.commits.length, 1); assert.equal(snapshot.changes.length, 0);
    });
    await t.test('working tree and commit diff', async () => {
      await run('write', { path: 'a.txt', message: 'two\n' }); const d = await run('diff', { path: 'a.txt' });
      assert.equal(d.before, 'one\n'); assert.equal(d.after, 'two\n');
      const files = await run('commitFiles', { value: snapshot.head }); assert.equal(files.changes[0].path, 'a.txt');
      const initial = await run('diff', { path: 'a.txt', value: snapshot.head }); assert.equal(initial.before, ''); assert.equal(initial.after, 'one\n');
    });
    await t.test('stage and unstage', async () => {
      await run('stage', { paths: ['a.txt'] }); assert.ok(snapshot.changes[0].staged);
      await run('unstage', { paths: ['a.txt'] }); assert.equal(snapshot.changes[0].staged, false);
    });
    await t.test('unselected staged content cannot leak into a commit', async () => {
      await run('write', { path: 'b.txt', message: 'unrelated\n' }); await run('stage', { paths: ['b.txt'] });
      await assert.rejects(run('commit', { paths: ['a.txt'], message: 'Only a' }), /Unselected/);
      await run('unstage', { paths: ['b.txt'] }); await run('commit', { paths: ['a.txt'], message: 'Only a' });
      const files = await run('commitFiles', { value: snapshot.head }); assert.deepEqual(files.changes.map(x => x.path), ['a.txt']);
    });
    await t.test('stale revisions and missing confirmations are refused', async () => {
      await assert.rejects(run('write', { path: 'a.txt', message: 'wrong', expectedHead: 'a'.repeat(40) }), /HEAD changed/);
      await assert.rejects(run('discard', { paths: ['a.txt'] }), /confirmation/);
    });
    await t.test('browser stash refuses untracked files', async () => {
      await assert.rejects(run('stash', { message: 'unsafe' }), /untracked/);
      await run('commit', { paths: ['b.txt'], message: 'Add b' });
    });
    await t.test('branch, rename, checkout and safe deletion', async () => {
      await run('branch', { value: 'feature/test' }); assert.equal(snapshot.branch, 'feature/test');
      await run('renameBranch', { value: 'feature/renamed' }); assert.equal(snapshot.branch, 'feature/renamed');
      await run('checkout', { value: 'main' }); await run('deleteBranch', { value: 'feature/renamed', confirm: true });
      assert.ok(!snapshot.branches.includes('feature/renamed'));
    });
    await t.test('lightweight tags and confirmation', async () => {
      await run('tag', { value: 'v-test' }); assert.ok(snapshot.tags.includes('v-test'));
      await run('deleteTag', { value: 'v-test', confirm: true }); assert.ok(!snapshot.tags.includes('v-test'));
    });
    await t.test('tracked-only stash saves and applies real changes', async () => {
      await run('write', { path: 'a.txt', message: 'stash this\n' });
      await run('stash', { message: 'Saved changes' }); assert.equal(snapshot.changes.length, 0); assert.equal(snapshot.stashes.length, 1);
      await run('stashApply', { value: 'stash@{0}' }); assert.equal((await run('read', { path: 'a.txt' })).text, 'stash this\n');
      assert.equal(snapshot.stashes.length, 1); await run('stashDrop', { value: 'stash@{0}', confirm: true }); assert.equal(snapshot.stashes.length, 0);
    });
    await t.test('discard restores a tracked file', async () => {
      await run('discard', { paths: ['a.txt'], confirm: true }); assert.equal((await run('read', { path: 'a.txt' })).text, 'two\n');
    });
    await t.test('binary preview is explicit', async () => {
      fs.writeFileSync(path.join(temporary, 'Project', 'binary.dat'), Buffer.from([1, 0, 255]));
      const d = await run('diff', { path: 'binary.dat' }); assert.equal(d.binary, true);
      fs.unlinkSync(path.join(temporary, 'Project', 'binary.dat'));
    });
    await t.test('symbolic-link escape is rejected', async () => {
      const outside = path.join(temporary, 'outside.txt'); fs.writeFileSync(outside, 'safe');
      fs.symlinkSync(outside, path.join(temporary, 'Project', 'escape.txt'));
      await assert.rejects(run('write', { path: 'escape.txt', message: 'bad' }), /Symbolic/);
      assert.equal(fs.readFileSync(outside, 'utf8'), 'safe'); fs.unlinkSync(path.join(temporary, 'Project', 'escape.txt'));
    });
    await t.test('export includes a real ZIP signature', async () => {
      const zip = Buffer.from((await run('export')).text, 'base64'); assert.equal(zip.subarray(0, 2).toString(), 'PK');
    });
    await t.test('reopen persists history and the worktree', async () => {
      const reopened = createBackend(fs, undefined, { base: temporary });
      const result = await reopened.execute({ operation: 'open', root: 'Project' }); assert.equal(result.snapshot.head, snapshot.head);
      assert.equal(result.snapshot.commits.length, 3);
    });
    await t.test('unsupported browser operations never simulate success', async () => {
      await assert.rejects(run('rebase', { value: 'main' }), /not supported/);
    });
  } finally { fs.rmSync(temporary, { recursive: true, force: true }); }
});
