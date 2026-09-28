const encode = new TextEncoder();
const decode = new TextDecoder('utf-8', { fatal: true, ignoreBOM: true });
const limit = 2 * 1024 * 1024;
export const reviewOperations = ['review', 'stageText', 'unstageText', 'commitStaged'];
export async function hash(text) {
  const bytes = await crypto.subtle.digest('SHA-256', encode.encode(text));
  return Array.from(new Uint8Array(bytes), b => b.toString(16).padStart(2, '0')).join('');
}
const version = file => hash((file.exists ? '1' : '0') + ':' + file.mode + ':' + file.text);
function verify(expected, actual) {
  if (typeof expected !== 'string' || expected.length !== 64 || expected !== actual) throw new Error('The reviewed content changed. Refresh and review it again; nothing was staged.');
}
function decodeText(bytes) {
  if (bytes.length > limit || bytes.includes(0)) throw new Error('Only UTF-8 text up to 2 MiB supports partial staging.');
  return decode.decode(bytes);
}
export function createReview({ fs, git, getDir, safeFile, read }) {
  const pfs = fs.promises;
  const options = () => ({ fs, dir: getDir() });
  async function entry(tree, path) {
    let found = { exists: false, mode: '100644', text: '' };
    await git.walk({ ...options(), trees: [tree], map: async (name, [item]) => {
      if (name === '.') return;
      if (name !== path) return path.startsWith(name + '/') ? undefined : null;
      if (!item) return null;
      const type = await item.type(); const mode = await item.mode();
      if (type !== 'blob' || ![0o100644, 0o100755].includes(mode)) throw new Error('Partial staging requires a regular, resolved text file.');
      const bytes = (await git.readBlob({ ...options(), oid: await item.oid() })).blob; found = { exists: true, mode: mode.toString(8), text: decodeText(bytes) };
      return null;
    } });
    return found;
  }
  async function indexHash() {
    try {
      const bytes = await pfs.readFile(getDir() + '/.git/index');
      const value = await crypto.subtle.digest('SHA-256', bytes);
      return Array.from(new Uint8Array(value), b => b.toString(16).padStart(2, '0')).join('');
    } catch (e) { if (e.code === 'ENOENT') return hash(''); throw e; }
  }
  async function review(r) {
    const full = await safeFile(r.path);
    const index = await entry(git.STAGE(), r.path); let before, after;
    if (r.value === 'staged') {
      try { before = await entry(git.TREE({ ref: 'HEAD' }), r.path); }
      catch (e) { if (!['NotFoundError', 'ResolveRefError'].includes(e.code)) throw e; before = { exists: false, mode: '100644', text: '' }; }
      after = index;
    } else {
      if (r.value && r.value !== 'unstaged') throw new Error('Unknown review mode.');
      before = index; let exists = true, mode = index.mode;
      try { const stat = await pfs.stat(full); if (stat.size > limit) throw new Error('File exceeds the 2 MiB text limit.'); if (stat.mode & 0o100) mode = '100755'; }
      catch (e) { if (e.code !== 'ENOENT') throw e; exists = false; }
      after = { exists, mode, text: exists ? await read(r.path) : '' };
    }
    return { before: before.text, after: after.text, beforeExists: before.exists, afterExists: after.exists,
      beforeHash: await version(before), afterHash: await version(after), beforeMode: before.mode, afterMode: after.mode };
  }
  async function execute(r) {
    if (r.operation === 'review') {
      try { return await review(r); }
      catch (e) { if (/UTF-8|2 MiB|encoded data/.test(e.message)) return { binary: true, text: e.message }; throw e; }
    }
    if (r.operation === 'commitStaged') {
      verify(r.indexHash, await indexHash());
      if (!r.message?.trim() || !r.author?.trim() || !r.email?.trim() || /[\r\n\0<>]/.test(r.author + r.email)) throw new Error('A message and valid Git author identity are required.');
      await git.commit({ ...options(), author: { name: r.author, email: r.email }, message: r.message.replace(/\r\n?|\n/g, '\n'), disallowEmpty: true });
      return null;
    }
    const staged = r.operation === 'unstageText';
    const current = await review({ ...r, value: staged ? 'staged' : 'unstaged' });
    verify(r.beforeHash, current.beforeHash); verify(r.afterHash, current.afterHash);
    decodeText(encode.encode(r.message));
    const targetExists = staged ? current.beforeExists : current.afterExists;
    if (r.remove && (targetExists || r.message.length !== 0)) throw new Error('The reviewed target is not a deletion.');
    if (r.remove) await git.updateIndex({ ...options(), filepath: r.path, remove: true, force: true });
    else {
      const mode = staged ? current.afterMode : current.beforeExists ? current.beforeMode : current.afterMode;
      const oid = await git.writeBlob({ ...options(), blob: encode.encode(r.message) });
      await git.updateIndex({ ...options(), filepath: r.path, oid, mode: parseInt(mode, 8), add: true });
    }
    return null;
  }
  return { execute, indexHash };
}
