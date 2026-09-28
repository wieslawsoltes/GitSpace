// Copyright (c) 2026 GitSpace contributors. MIT license.
// Independent path-segment implementation for the Git browser bundle.
// Git's internal paths are slash-separated; a Windows drive resets the root.
export function join(...values) {
  const parts = []; let prefix = ''; let absolute = false; let trailing = false; let started = false;
  for (const value of values) {
    if (typeof value !== 'string') throw new TypeError('Path segments must be strings.');
    if (!value) continue;
    const segment = value.replaceAll('\\', '/');
    const drive = /^[A-Za-z]:/.exec(segment)?.[0];
    let body = segment;
    if (drive && segment[2] === '/') { parts.length = 0; prefix = drive; absolute = true; body = segment.slice(3); started = true; }
    else if (!started) { prefix = drive || ''; absolute = segment.startsWith('/'); body = drive ? segment.slice(2) : segment; started = true; }
    trailing = segment.endsWith('/');
    for (const part of body.split('/')) {
      if (!part || part === '.') continue;
      if (part === '..') {
        if (parts.length && parts[parts.length - 1] !== '..') parts.pop();
        else if (!absolute) parts.push(part);
      } else parts.push(part);
    }
  }
  let result = prefix + (absolute ? '/' : '') + parts.join('/');
  if (!result) result = '.';
  if (trailing && !result.endsWith('/')) result += '/';
  return result;
}
