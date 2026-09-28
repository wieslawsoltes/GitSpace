import { chromium } from 'playwright';
import { mkdir, writeFile, readFile } from 'node:fs/promises';
import { createRequire } from 'node:module';
import { inflateSync } from 'node:zlib';
import assert from 'node:assert/strict';

const { unzipSync } = createRequire(new URL('../../src/GitSpace.BrowserGit/package.json', import.meta.url))('fflate');
const output = 'artifacts/browser-tests'; await mkdir(output, { recursive: true });
const browser = await chromium.launch({ headless: true, args: ['--enable-unsafe-swiftshader', '--disable-dev-shm-usage'] });
const page = await browser.newPage({ viewport: { width: 1360, height: 860 }, acceptDownloads: true });
const messages = [], errors = []; let passed = 0, activeCheck = '', failure = '';
const fileText = 'Created in the browser\nUnicode: zażółć\n';
page.on('console', message => { messages.push(message.type() + ': ' + message.text()); if (message.text().includes('[GitSpace]')) console.log(message.text()); });
page.on('pageerror', error => errors.push(error.stack || String(error)));
async function check(name, action) {
  activeCheck = name; console.log('START ' + name);
  try { await action(); passed++; console.log('PASS ' + name); }
  catch (error) { failure = String(error.stack || error); throw error; }
}
async function accessibility() { const enable = page.locator('#uno-enable-accessibility'); if (await enable.count()) await enable.dispatchEvent('click'); await page.waitForTimeout(500); }
async function click(name, role = 'button') {
  const item = page.getByRole(role, { name, exact: true }).first(); await item.waitFor({ state: 'attached', timeout: 10000 });
  const bounds = await item.boundingBox(); assert.ok(bounds && bounds.width > 0 && bounds.height > 0, 'Rendered control bounds: ' + name);
  await page.mouse.click(bounds.x + bounds.width / 2, bounds.y + bounds.height / 2);
}
async function activateDialog(name) {
  // Invoke the shipped managed IInvokeProvider, never an application command or worker API.
  const button = page.getByRole('button', { name, exact: true });
  await button.waitFor({ state: 'attached' }); assert.equal(await button.isDisabled(), false);
  await button.dispatchEvent('click');
}
async function input(name, value) {
  const field = page.getByRole('textbox', { name, exact: true });
  await field.waitFor({ state: 'visible' });
  await field.focus();
  // Opening/focusing a Uno popup asynchronously transfers its managed selection
  // to the native text bridge. Let that handoff finish before replacing text.
  await page.waitForTimeout(250);
  await field.fill('');
  await page.waitForTimeout(100);
  assert.equal(await field.inputValue(), '', 'The focused editor must be empty before replacement');
  await field.fill(value);
  await page.waitForTimeout(100);
  assert.equal(await field.inputValue(), value);
}
async function menu(title, item) { await click(title); await click(item, 'menuitem'); }
async function reviewMode(index) {
  await activateDialog(['All changes', 'Unstaged changes', 'Staged changes'][index]);
  await page.waitForFunction(value => gitspaceDiagnostics.reviewMode === value, ['all', 'unstaged', 'staged'][index]);
}
async function ready() { await page.waitForFunction(() => globalThis.gitspaceDiagnostics?.ready && !gitspaceDiagnostics.busy, null, { timeout: 90000 }); }
function readObject(files, id, type) {
  assert.match(id, /^[a-f0-9]{40}$/);
  const bytes = inflateSync(files['.git/objects/' + id.slice(0, 2) + '/' + id.slice(2)], { maxOutputLength: 2 * 1024 * 1024 });
  const end = bytes.indexOf(0); assert.ok(end > 0);
  assert.equal(bytes.subarray(0, end).toString(), type + ' ' + (bytes.length - end - 1));
  return bytes.subarray(end + 1);
}
function committedRootFile(files, commit, filename) {
  const treeId = /^tree ([a-f0-9]{40})$/m.exec(readObject(files, commit, 'commit').toString())?.[1];
  const tree = readObject(files, treeId, 'tree');
  for (let offset = 0; offset < tree.length;) {
    const end = tree.indexOf(0, offset); assert.ok(end > offset && end + 21 <= tree.length);
    const header = tree.subarray(offset, end).toString();
    const id = tree.subarray(end + 1, end + 21).toString('hex');
    if (header.slice(header.indexOf(' ') + 1) === filename) return readObject(files, id, 'blob').toString('utf8');
    offset = end + 21;
  }
  throw new Error('Committed file is missing: ' + filename);
}
try {
  await page.goto(process.env.BASE_URL || 'http://127.0.0.1:4173/GitSpace/', { waitUntil: 'domcontentloaded' });
  await ready(); await accessibility();
  await check('real Uno canvas, Git worker and tutorial repository', async () => {
    assert.ok(await page.locator('canvas').count() > 0);
    const state = await page.evaluate(() => gitspaceDiagnostics);
    assert.equal(state.repository, 'Tutorial'); assert.equal(state.changes, 4); assert.equal(state.commits, 4); assert.match(state.head, /^[a-f0-9]{40}$/);
    await page.waitForFunction(() => gitspaceDiagnostics.frames > 0);
  });
  await check('file inclusion supports mixed state with exactly four recycled rows', async () => {
    const list = page.getByRole('listbox', { name: 'Changed files', exact: true });
    const row = list.getByRole('option', { name: 'README.md', exact: true });
    await row.waitFor({ state: 'attached' });
    assert.equal(await list.getByRole('option').count(), 4, 'One realized row per tutorial file, no duplicates');
    // The row is nested in the listbox: boundingBox applies the parent offset
    // exactly once. Verify the checkbox target remains inside that list viewport.
    const box = await row.boundingBox(); const viewport = await list.boundingBox();
    assert.ok(box && viewport && box.width > 0 && box.height > 0);
    const x = box.x + 18, y = box.y + box.height / 2;
    assert.ok(x >= viewport.x && x < viewport.x + viewport.width && y >= viewport.y && y < viewport.y + viewport.height);
    await page.mouse.click(x, y);
    await page.waitForFunction(() => document.querySelector('[aria-label="Select all changed files"]').getAttribute('aria-checked') === 'mixed');
    await click('Select all changed files', 'checkbox');
    await page.waitForFunction(() => document.querySelector('[aria-label="Select all changed files"]').getAttribute('aria-checked') === 'true');
    assert.equal(await page.getByRole('checkbox', { name: 'Select all changed files', exact: true }).isChecked(), true);
  });
  await check('review mode supports one-click selection and keyboard navigation', async () => {
    await click('Unstaged changes');
    await page.waitForFunction(() => gitspaceDiagnostics.reviewMode === 'unstaged');
    await page.keyboard.press('ArrowRight');
    await page.waitForFunction(() => gitspaceDiagnostics.reviewMode === 'staged');
    await page.keyboard.press('Home');
    await page.waitForFunction(() => gitspaceDiagnostics.reviewMode === 'all');
  });
  await page.screenshot({ path: output + '/01-changes-dark.png', fullPage: true });
  await check('History tab shows real commit changes', async () => { await click('History'); await page.waitForFunction(() => gitspaceDiagnostics.history); });
  await page.screenshot({ path: output + '/02-history.png', fullPage: true });
  await check('Changes tab and split diff are interactive', async () => {
    await click('Changes'); await page.waitForFunction(() => !gitspaceDiagnostics.history);
    await activateDialog('Toggle split diff'); await page.waitForFunction(() => gitspaceDiagnostics.split);
  });
  await page.screenshot({ path: output + '/03-split-diff.png', fullPage: true });
  await check('commit composer makes a real selected-file commit', async () => {
    const previous = await page.evaluate(() => gitspaceDiagnostics.head);
    await input('Commit summary', 'Improve repository refresh workflow');
    await input('Commit description', 'Created through the actual Uno commit controls in Chromium.');
    await click('Commit to main'); await page.waitForFunction(id => gitspaceDiagnostics.head !== id && !gitspaceDiagnostics.busy, previous);
    assert.equal(await page.evaluate(() => gitspaceDiagnostics.changes), 0); assert.equal(await page.evaluate(() => gitspaceDiagnostics.commits), 5);
  });
  await check('create a branch through the branch dialog', async () => {
    await menu('Branch', 'New branch…'); await input('Branch name', 'feature/browser-test');
    await page.screenshot({ path: output + '/branch-before-create.png', fullPage: true });
    await activateDialog('Create branch');
    await page.waitForFunction(() => gitspaceDiagnostics.branch === 'feature/browser-test' && !gitspaceDiagnostics.busy);
  });
  await check('text editor preserves Unicode and displays both saved lines', async () => {
    await menu('File', 'New file…'); await input('Repository-relative path', 'browser-test.txt'); await activateDialog('Create file');
    await input('File editor', fileText); await activateDialog('Save file');
    await page.waitForFunction(() => gitspaceDiagnostics.changes === 1 && gitspaceDiagnostics.activePath === 'browser-test.txt' && gitspaceDiagnostics.rowCount === 2 && !gitspaceDiagnostics.busy);
  });
  await check('partial review stages one selected line and preserves selection through refresh', async () => {
    await reviewMode(1);
    await page.waitForFunction(() => gitspaceDiagnostics.rowCount === 2 && !gitspaceDiagnostics.split);
    const bounds = await page.getByRole('group', { name: 'Diff viewer', exact: true }).boundingBox(); assert.ok(bounds);
    await page.mouse.click(bounds.x + 130, bounds.y + 9);
    await page.waitForFunction(() => gitspaceDiagnostics.selectedRows === 1);
    await menu('View', 'Refresh'); await ready();
    assert.equal(await page.evaluate(() => gitspaceDiagnostics.selectedRows), 1);
    await activateDialog('Stage selection');
    await page.waitForFunction(() => gitspaceDiagnostics.stagedFiles === 1 && !gitspaceDiagnostics.busy);
    await input('Commit summary', 'Commit only the selected first line');
    const previous = await page.evaluate(() => gitspaceDiagnostics.head);
    await click('Commit staged to feature/browser-test');
    await page.waitForFunction(id => gitspaceDiagnostics.head !== id && gitspaceDiagnostics.commits === 6 && !gitspaceDiagnostics.busy, previous);
    assert.equal(await page.evaluate(() => gitspaceDiagnostics.changes), 1);
  });
  await check('real ZIP export preserves exact worktree and selected committed bytes', async () => {
    await menu('File', 'Export repository ZIP…');
    const pending = page.waitForEvent('download'); await activateDialog('Confirm');
    const download = await pending; const zipPath = output + '/tutorial-export.zip'; await download.saveAs(zipPath);
    const files = unzipSync(await readFile(zipPath)); const decoder = new TextDecoder('utf-8', { fatal: true, ignoreBOM: true });
    assert.equal(decoder.decode(files['browser-test.txt']), fileText);
    assert.equal(decoder.decode(files['.git/HEAD']).trim(), 'ref: refs/heads/feature/browser-test');
    const head = decoder.decode(files['.git/refs/heads/feature/browser-test']).trim();
    assert.equal(head, await page.evaluate(() => gitspaceDiagnostics.head));
    assert.equal(committedRootFile(files, head, 'browser-test.txt'), 'Created in the browser\n');
  });
  await check('theme switch preserves the worktree', async () => {
    await menu('View', 'Toggle light / dark appearance'); await ready();
    assert.equal(await page.evaluate(() => gitspaceDiagnostics.changes), 1); assert.equal(await page.evaluate(() => gitspaceDiagnostics.rowCount), 2);
    // Uno updates a TextBlock's accessible name through aria-label while the
    // semantic paragraph's initial text node can remain unchanged.
    await page.waitForFunction(() => {
      const label = document.querySelector('[xamlautomationid="ReviewedFilePath"]');
      return (label?.getAttribute('aria-label') || label?.textContent) === 'browser-test.txt';
    });
  });
  await page.screenshot({ path: output + '/04-light-theme.png', fullPage: true });
  await check('reload preserves branch, history and multiline uncommitted work', async () => {
    const head = await page.evaluate(() => gitspaceDiagnostics.head); await page.waitForTimeout(1000);
    await page.reload({ waitUntil: 'domcontentloaded' }); await ready(); await accessibility();
    const state = await page.evaluate(() => gitspaceDiagnostics); assert.equal(state.head, head); assert.equal(state.branch, 'feature/browser-test'); assert.equal(state.changes, 1); assert.equal(state.rowCount, 2);
  });
  await check('no unhandled browser exceptions', async () => { assert.deepEqual(errors, []); });
  console.log(`RESULT: ${passed} real Chromium workflows passed.`);
} finally {
  await page.screenshot({ path: output + '/last-state.png', fullPage: true }).catch(() => {});
  await writeFile(output + '/console.log', messages.join('\n')); await writeFile(output + '/errors.json', JSON.stringify(errors, null, 2));
  await writeFile(output + '/report.json', JSON.stringify({ passed, activeCheck, failure, errors }, null, 2));
  await writeFile(output + '/dom.html', await page.content());
  await writeFile(output + '/state.json', JSON.stringify(await page.evaluate(() => globalThis.gitspaceDiagnostics || null), null, 2));
  await browser.close();
}
