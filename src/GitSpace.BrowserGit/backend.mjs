import git from 'isomorphic-git';
import { zipSync } from 'fflate';
import { createReview, reviewOperations } from './review.mjs';

const encoder = new TextEncoder();
const decoder = new TextDecoder('utf-8', { fatal: true, ignoreBOM: true });
const TEXT_LIMIT = 2 * 1024 * 1024;
export const capabilities = Object.freeze([...reviewOperations, 'history', 'demo', 'init', 'open', 'clone', 'refresh', 'diff', 'commitFiles', 'read', 'write', 'stage', 'unstage', 'commit', 'amend', 'discard', 'branch', 'checkout', 'renameBranch', 'deleteBranch', 'remote', 'fetch', 'pull', 'push', 'merge', 'tag', 'deleteTag', 'stash', 'stashApply', 'stashDrop', 'export']);
export function relativePath(path) {
  if (typeof path !== 'string' || !path || path.length > 4096 || /[\0\r\n:\\]/.test(path) || path.startsWith('/') || path.split('/').some(x => !x || x === '.' || x === '..' || x.toLowerCase() === '.git')) throw new Error('Use a repository-relative path, not metadata or traversal.');
  return path;
}
export function reference(value) {
  if (typeof value !== 'string' || !value || value.startsWith('-') || /[\s\0~^:?*\[\\]/.test(value) || value.includes('..') || value.includes('@{') || value.includes('//') || value.endsWith('/') || value.endsWith('.') || value.split('/').some(x => x.startsWith('.') || x.toLowerCase().endsWith('.lock'))) throw new Error('Invalid Git reference.');
  return value;
}
function oid(value) { if (!/^[a-f0-9]{40}$/i.test(value)) throw new Error('A full commit object ID is required.'); return value; }
function https(value) { const url = new URL(value); if (url.protocol !== 'https:' || url.username || url.password || url.hash) throw new Error('Use HTTPS without embedded credentials.'); return url.href; }
function confirmed(r) { if (!r.confirm) throw new Error('This operation requires explicit confirmation.'); }
function isMissing(error) { return ['ENOENT', 'NotFoundError', 'ResolveRefError'].includes(error.code); }
function text(bytes) { if (bytes.length > TEXT_LIMIT || bytes.includes(0)) throw new Error('Only UTF-8 text files up to 2 MiB can be previewed.'); return decoder.decode(bytes); }

/** A stateful, serialized repository backend. Supply an fs-compatible object and an isomorphic-git HTTP client. */
export function createBackend(fs, http, { base = '/repositories' } = {}) {
  const pfs = fs.promises;
  let dir = ''; let queue = Promise.resolve();
  let historyLimit = 200, historyKey = '', historyRows = [], historyReads = 0;
  const review = createReview({ fs, git, getDir: () => dir, safeFile, read });
  const options = () => ({ fs, dir });
  const head = async () => { try { return await git.resolveRef({ ...options(), ref: 'HEAD' }); } catch (e) { if (isMissing(e)) return ''; throw e; } };
  const matrix = () => git.statusMatrix(options());
  async function exists(path) { try { await pfs.stat(path); return true; } catch (e) { if (isMissing(e)) return false; throw e; } }
  async function mkdir(path) {
    let current = '';
    for (const piece of path.split('/').filter(Boolean)) { current += '/' + piece; if (!(await exists(current))) await pfs.mkdir(current); }
  }
  async function safeFile(path) {
    relativePath(path); let full = dir;
    for (const segment of path.split('/')) {
      full += '/' + segment;
      try { if ((await pfs.lstat(full)).isSymbolicLink()) throw new Error('Symbolic links are not editable in the text editor.'); }
      catch (e) { if (!isMissing(e)) throw e; }
    }
    return full;
  }
  async function write(path, value) {
    const bytes = encoder.encode(value); text(bytes);
    const full = await safeFile(path); await mkdir(full.slice(0, full.lastIndexOf('/'))); await pfs.writeFile(full, bytes);
  }
  async function read(path) {
    const full = await safeFile(path);
    try { const stat = await pfs.stat(full); if (stat.size > TEXT_LIMIT) throw new Error('File exceeds the 2 MiB preview limit.'); return text(await pfs.readFile(full)); }
    catch (e) { if (isMissing(e)) return ''; throw e; }
  }
  async function blob(ref, path) {
    if (!ref) return null;
    try { return (await git.readBlob({ ...options(), oid: ref, filepath: path })).blob; }
    catch (e) { if (isMissing(e)) return null; throw e; }
  }
  async function stage(paths) {
    if (!paths?.length || paths.length > 10000) throw new Error('Select between 1 and 10,000 files.');
    for (const path of paths) {
      const full = await safeFile(path);
      if (await exists(full)) await git.add({ ...options(), filepath: path });
      else await git.remove({ ...options(), filepath: path });
    }
  }
  async function clean() { if ((await matrix()).some(([, h, w, s]) => h !== w || h !== s)) throw new Error('Commit or stash all changes before this browser operation.'); }
  async function stashList() {
    try {
      const data = await pfs.readFile(dir + '/.git/logs/refs/stash', 'utf8');
      return String(data).trim().split('\n').filter(Boolean).reverse().map((line, i) => ({ id: `stash@{${i}}`, message: line.slice(line.indexOf('\t') + 1) }));
    } catch (e) { if (isMissing(e)) return []; throw e; }
  }
  async function loadHistory(id) {
    let shallow = '';
    try { shallow = String(await pfs.readFile(dir + '/.git/shallow', 'utf8')); } catch (e) { if (!isMissing(e)) throw e; }
    const key = dir + '\0' + id + '\0' + historyLimit + '\0' + shallow;
    if (key === historyKey) return historyRows;
    const rows = id ? await git.log({ ...options(), depth: historyLimit + 1 }) : [];
    historyReads++; historyRows = rows; historyKey = key; return rows;
  }
  async function snapshot() {
    const id = await head();
    const [rows, history, branches, tags, remotes, branch, stashes] = await Promise.all([
      matrix(), loadHistory(id), git.listBranches(options()), git.listTags(options()),
      git.listRemotes(options()), git.currentBranch(options()), stashList()
    ]);
    return {
      root: dir, name: dir.split('/').pop(), head: id, branch: branch || 'Detached HEAD', branches, tags, remotes, stashes,
      changes: rows.filter(([, h, w, s]) => h !== w || h !== s).map(([path, h, w, s]) => ({ path, status: h === 0 ? 'A' : w === 0 ? 'D' : 'M', staged: h !== s, conflict: false, originalPath: '' })),
      commits: history.slice(0, historyLimit).map(({ oid, commit }) => ({ id: oid, author: commit.author.name, email: commit.author.email, date: new Date(commit.author.timestamp * 1000).toISOString(), message: commit.message.trimEnd(), parents: commit.parent })),
      ahead: 0, behind: 0, operation: '', historyLimit, hasMoreHistory: history.length > historyLimit, indexHash: await review.indexHash()
    };
  }
  async function commitFiles(id) {
    const { commit } = await git.readCommit({ ...options(), oid: oid(id) });
    const refs = [id, commit.parent[0]];
    const trees = refs.filter(Boolean).map(ref => git.TREE({ ref }));
    const rows = await git.walk({ ...options(), trees, map: async (path, entries) => {
      if (path === '.') return;
      const [after, before] = entries;
      const at = after ? await after.type() : undefined; const bt = before ? await before.type() : undefined;
      if (at === 'tree' || bt === 'tree') return;
      const a = after ? await after.oid() : undefined; const b = before ? await before.oid() : undefined;
      if (a !== b) return { path, status: !b ? 'A' : !a ? 'D' : 'M', staged: false, conflict: false, originalPath: '' };
    } });
    return rows.filter(Boolean);
  }
  async function looseStashOnly() {
    const path = dir + '/.git/objects/pack';
    if (await exists(path) && (await pfs.readdir(path)).some(p => p.endsWith('.pack'))) throw new Error('Browser stash currently supports loose-object repositories only. Use the desktop client for this repository.');
  }
  async function demo() {
    dir = base + '/Tutorial';
    if (await exists(dir + '/.git')) return;
    await mkdir(dir); await git.init({ ...options(), defaultBranch: 'main' });
    const author = { name: 'GitSpace Team', email: 'team@gitspace.example' };
    await git.setConfig({ ...options(), path: 'user.name', value: author.name }); await git.setConfig({ ...options(), path: 'user.email', value: author.email });
    const original = 'using GitSpace.Core;\n\nnamespace GitSpace.App;\n\npublic sealed class RepositoryWorkspace\n{\n    private readonly IGitBackend _backend;\n\n    public RepositoryWorkspace(IGitBackend backend)\n    {\n        _backend = backend;\n    }\n\n    public async Task RefreshAsync()\n    {\n        var result = await _backend.ExecuteAsync(\n            new GitRequest("refresh"));\n\n        Render(result.Snapshot);\n    }\n\n    private void Render(GitSnapshot? snapshot)\n    {\n        Console.WriteLine(snapshot?.Branch);\n    }\n}\n';
    const initial = [
      ['README.md', '# GitSpace\n\nA native Git workspace.\n', 'Create the GitSpace workspace'],
      ['src/RepositoryWorkspace.cs', original, 'Add shared repository commands'],
      ['src/theme.json', '{\n  "theme": "light",\n  "accent": "#0969da"\n}\n', 'Introduce application theme tokens'],
      ['docs/architecture.md', '# Architecture\n\nCore → Git → Controls → App\n', 'Document reusable library boundaries']
    ];
    for (const [path, value, message] of initial) { await write(path, value); await stage([path]); await git.commit({ ...options(), author, message }); }
    await git.branch({ ...options(), ref: 'feature/command-palette' });
    const updated = original.replace('using GitSpace.Core;', 'using GitSpace.Core;\nusing System.Diagnostics;')
      .replace('private readonly IGitBackend _backend;', 'private readonly IGitBackend _backend;\n    private readonly SemaphoreSlim _refreshGate = new(1);')
      .replace('public async Task RefreshAsync()\n    {\n        var result = await _backend.ExecuteAsync(\n            new GitRequest("refresh"));\n\n        Render(result.Snapshot);\n    }', 'public async Task RefreshAsync(CancellationToken cancellation)\n    {\n        await _refreshGate.WaitAsync(cancellation);\n        try\n        {\n            var watch = Stopwatch.StartNew();\n            var result = await _backend.ExecuteAsync(\n                new GitRequest("refresh"), cancellation);\n\n            Render(result.Snapshot);\n            Debug.WriteLine($"Refreshed in {watch.ElapsedMilliseconds} ms");\n        }\n        finally\n        {\n            _refreshGate.Release();\n        }\n    }');
    await write('src/RepositoryWorkspace.cs', updated);
    await write('README.md', '# GitSpace\n\nA native Git workspace for desktop and browser.\n\nBuilt with Uno Platform and GPU-backed Skia rendering.\n');
    await write('src/theme.json', '{\n  "theme": "dark",\n  "accent": "#0969da",\n  "density": "compact"\n}\n');
    await write('docs/roadmap.md', '# Next up\n\n- Review your changes\n- Create a feature branch\n- Make a real Git commit\n');
  }
  async function execute(r) {
    const op = r.operation;
    if (!capabilities.includes(op)) throw new Error('This operation is not supported by the browser Git backend: ' + op);
    if (op === 'demo') { await demo(); return { snapshot: await snapshot() }; }
    if (['init', 'open', 'clone'].includes(op)) {
      const name = String(r.root || '').replace(base + '/', '');
      if (!/^[\p{L}\p{N}][\p{L}\p{N}._ -]{0,99}$/u.test(name) || name === '..' || name === '.git') throw new Error('Enter a repository name, not a local filesystem path.');
      const destination = base + '/' + name;
      if (op !== 'open' && await exists(destination) && (await pfs.readdir(destination)).length) throw new Error('Repository destination is not empty.');
      if (op === 'open' && !(await exists(destination + '/.git'))) throw new Error('No browser repository exists with that name.');
      if (op !== 'open') await mkdir(destination);
      const previous = dir; dir = destination; historyLimit = 200; historyKey = '';
      try {
        if (op === 'init') await git.init({ ...options(), defaultBranch: 'main' });
        if (op === 'clone') await git.clone({ ...options(), http, url: https(r.value), corsProxy: r.proxy ? https(r.proxy) : undefined, singleBranch: true, depth: 100, onAuth: () => ({ username: r.token || '', password: 'x-oauth-basic' }) });
        return { snapshot: await snapshot() };
      } catch (e) { dir = previous; throw e; }
    }
    if (!dir) throw new Error('Open a repository first.');
    if (r.root && r.root !== dir) throw new Error('Repository changed. Refresh before continuing.');
    if (!['refresh', 'read', 'diff', 'commitFiles', 'export', 'review', 'history'].includes(op) && r.expectedHead && r.expectedHead !== await head()) throw new Error('HEAD changed. Refresh and review before retrying.');
    const author = { name: r.author || 'GitSpace User', email: r.email || 'user@example.com' };
    const network = { ...options(), http, remote: 'origin', corsProxy: r.proxy ? https(r.proxy) : undefined, onAuth: () => ({ username: r.token || '', password: 'x-oauth-basic' }) };
    if (reviewOperations.includes(op)) {
      const result = await review.execute({ ...r, author: author.name, email: author.email });
      return result ?? { snapshot: await snapshot() };
    }
    switch (op) {
      case 'refresh': break;
      case 'history': historyLimit = Math.max(1, Math.min(2000, Math.trunc(r.limit || 200))); break;
      case 'read': return { text: await read(r.path) };
      case 'write': await write(r.path, r.message); break;
      case 'diff': {
        relativePath(r.path); const id = r.value ? oid(r.value) : await head();
        const parent = r.value ? (await git.readCommit({ ...options(), oid: id })).commit.parent[0] : id;
        const before = await blob(parent, r.path); const after = r.value ? await blob(id, r.path) : null;
        try { return { before: before ? text(before) : '', after: r.value ? (after ? text(after) : '') : await read(r.path), binary: false }; }
        catch (e) { return { binary: true, text: e.message }; }
      }
      case 'commitFiles': return { changes: await commitFiles(r.value) };
      case 'stage': await stage(r.paths); break;
      case 'unstage':
        for (const path of r.paths || []) await git.resetIndex({ ...options(), filepath: relativePath(path) });
        break;
      case 'commit': case 'amend': {
        if (!r.message?.trim()) throw new Error('A commit summary is required.');
        if (op === 'amend') confirmed(r);
        if (/[\r\n<>]/.test(author.name + author.email)) throw new Error('Invalid author identity.');
        const selected = new Set(r.paths || []);
        if (op === 'commit' && !selected.size) throw new Error('Select at least one changed file.');
        if ((await matrix()).some(([path, h, , s]) => h !== s && !selected.has(path))) throw new Error('Unselected files are already staged. Unstage those files before committing the selection in the browser.');
        if (selected.size) await stage([...selected]);
        await git.commit({ ...options(), author, message: r.message, amend: op === 'amend', disallowEmpty: op !== 'amend' });
        break;
      }
      case 'discard':
        confirmed(r);
        for (const path of r.paths || []) {
          const full = await safeFile(path); const bytes = await blob(await head(), path);
          if (!bytes) throw new Error('Untracked files are not deleted by Discard.');
          await mkdir(full.slice(0, full.lastIndexOf('/'))); await pfs.writeFile(full, bytes); await git.resetIndex({ ...options(), filepath: path });
        }
        break;
      case 'branch': await git.branch({ ...options(), ref: reference(r.value), checkout: true }); break;
      case 'checkout': await clean(); await git.checkout({ ...options(), ref: reference(r.value) }); break;
      case 'renameBranch': await git.renameBranch({ ...options(), oldref: await git.currentBranch(options()), ref: reference(r.value) }); break;
      case 'deleteBranch': {
        confirmed(r); reference(r.value);
        if (r.value === await git.currentBranch(options())) throw new Error('Cannot delete the current branch.');
        const ancestor = await git.resolveRef({ ...options(), ref: r.value }); const current = await head();
        if (ancestor !== current && !(await git.isDescendent({ ...options(), oid: current, ancestor }))) throw new Error('Branch contains unmerged commits; deletion refused.');
        await git.deleteBranch({ ...options(), ref: r.value }); break;
      }
      case 'remote': await git.addRemote({ ...options(), remote: reference(r.path), url: https(r.value) }); break;
      case 'fetch': await git.fetch({ ...network, singleBranch: true, prune: true }); break;
      case 'pull': await clean(); await git.pull({ ...network, singleBranch: true, fastForwardOnly: true, author }); break;
      case 'push': await git.push({ ...network }); break;
      case 'merge': {
        confirmed(r); await clean();
        await git.merge({ ...options(), theirs: reference(r.value), fastForwardOnly: true, author });
        await git.checkout({ ...options(), ref: await git.currentBranch(options()), force: true }); break;
      }
      case 'tag': await git.tag({ ...options(), ref: reference(r.value) }); break;
      case 'deleteTag': confirmed(r); await git.deleteTag({ ...options(), ref: reference(r.value) }); break;
      case 'stash':
        await looseStashOnly();
        if ((await matrix()).some(([, h, w]) => h === 0 && w !== 0)) throw new Error('Browser stash cannot include untracked files. Commit those files first, or use the desktop client.');
        await git.setConfig({ ...options(), path: 'user.name', value: author.name }); await git.setConfig({ ...options(), path: 'user.email', value: author.email });
        await git.stash({ ...options(), op: 'push', message: r.message || 'Saved in GitSpace' }); break;
      case 'stashApply': case 'stashDrop': {
        await looseStashOnly();
        const match = /^stash@\{(\d+)\}$/.exec(r.value); if (!match) throw new Error('Invalid stash reference.');
        if (op === 'stashApply') await clean(); else confirmed(r);
        await git.stash({ ...options(), op: op === 'stashApply' ? 'apply' : 'drop', refIdx: Number(match[1]) }); break;
      }
      case 'export': {
        const files = {}; let total = 0;
        async function visit(path, key = '') {
          for (const name of await pfs.readdir(path)) {
            const full = path + '/' + name; const relative = key ? key + '/' + name : name; const stat = await pfs.lstat(full);
            if (stat.isSymbolicLink()) throw new Error('ZIP export currently requires a repository without symbolic links.');
            if (stat.isDirectory()) await visit(full, relative);
            else { total += stat.size; if (total > 64 * 1024 * 1024) throw new Error('ZIP export limit is 64 MiB.'); files[relative] = await pfs.readFile(full); }
          }
        }
        await visit(dir); const zip = zipSync(files, { level: 3 });
        let binary = ''; for (let i = 0; i < zip.length; i += 8192) binary += String.fromCharCode(...zip.subarray(i, i + 8192));
        return { text: btoa(binary) };
      }
    }
    return { snapshot: await snapshot() };
  }
  return {
    execute(request) {
      const result = queue.then(() => execute(request)); queue = result.catch(() => {}); return result;
    },
    capabilities,
    get diagnostics() { return { historyReads }; }
  };
}
