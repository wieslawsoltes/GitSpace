import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { performance } from 'node:perf_hooks';
import { createBackend } from '../../src/GitSpace.BrowserGit/backend.mjs';

const base = fs.mkdtempSync(path.join(os.tmpdir(), 'gitspace-performance-'));
const backend = createBackend(fs, undefined, { base });
const run = (operation, fields = {}) => backend.execute({ operation, author: 'Benchmark', email: 'bench@example.com', ...fields });
try {
  await run('init', { root: 'Benchmark' });
  for (let n = 0; n < 40; n++) {
    await run('write', { path: 'README.md', message: 'commit ' + n + '\n' });
    await run('commit', { paths: ['README.md'], message: 'Commit ' + n });
  }
  for (let n = 0; n < 500; n++) fs.writeFileSync(path.join(base, 'Benchmark', 'file-' + n + '.txt'), 'unchanged fixture\n');
  await run('stage', { paths: Array.from({ length: 500 }, (_, i) => 'file-' + i + '.txt') });
  await run('commitStaged', { indexHash: (await run('refresh')).snapshot.indexHash, message: 'Fixture files' });
  const samples = [];
  const historyBefore = backend.diagnostics.historyReads;
  for (let n = 0; n < 30; n++) { const start = performance.now(); await run('refresh'); samples.push(performance.now() - start); }
  samples.sort((a, b) => a - b);
  const report = { environment: 'Node ' + process.version + ' with filesystem adapter; not a browser/GPU benchmark', files: 501, commits: 41, refreshes: 30, medianMs: samples[15], p95Ms: samples[28], extraHistoryReads: backend.diagnostics.historyReads - historyBefore };
  if (report.extraHistoryReads !== 0) throw new Error('Warm refresh reread immutable commit history.');
  console.log(JSON.stringify(report, null, 2));
} finally { fs.rmSync(base, { recursive: true, force: true }); }
