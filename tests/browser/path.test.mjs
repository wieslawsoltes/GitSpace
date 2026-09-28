import test from 'node:test';
import assert from 'node:assert/strict';
import path from 'node:path';
import { join } from '../../src/GitSpace.BrowserGit/path-join.mjs';

test('independent path helper: normalized Git paths and drive roots', () => {
  for (const args of [[], [''], ['a', 'b'], ['/repo', '.git', 'objects'], ['a', '..', 'b'], ['/', '..', '..', 'x'], ['..', 'a', '..'], ['/repo', '/nested'], ['a', 'b/']]) assert.equal(join(...args), path.posix.join(...args));
  assert.equal(join('C:\\repo', '.git', 'objects'), 'C:/repo/.git/objects');
  assert.equal(join('C:/repo', 'D:/other', '..', 'result'), 'D:/result');
  assert.equal(join('C:relative', 'file'), 'C:relative/file');
  assert.throws(() => join(undefined), TypeError);
});

test('5000 randomized POSIX path differential trials', () => {
  let state = 415;
  const random = n => { state = (Math.imul(state, 1664525) + 1013904223) >>> 0; return state % n; };
  const parts = ['', '.', '..', '/', 'a', 'x/', 'b//c', '../z'];
  for (let i = 0; i < 5000; i++) {
    const values = Array.from({ length: random(6) }, () => parts[random(parts.length)]);
    assert.equal(join(...values), path.posix.join(...values), JSON.stringify(values));
  }
});
