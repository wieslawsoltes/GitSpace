import { chromium } from 'playwright';
import { createRequire } from 'node:module';
import { mkdir, writeFile, readFile } from 'node:fs/promises';
import assert from 'node:assert/strict';

const { unzipSync } = createRequire(new URL('../../src/GitSpace.BrowserGit/package.json', import.meta.url))('fflate');
const output = 'artifacts/browser-tests'; await mkdir(output, { recursive: true });
const browser = await chromium.launch({ headless: true, args: ['--enable-unsafe-swiftshader', '--disable-dev-shm-usage'] });
const page = await browser.newPage({ viewport: { width: 1360, height: 860 }, acceptDownloads: true });
const messages = [], errors = []; let passed = 0;
page.on('console', message => { messages.push(message.type() + ': ' + message.text()); if (message.text().includes('[GitSpace]')) console.log(message.text()); });
page.on('pageerror', error => errors.push(error.stack || String(error)));
async function check(name, action) { console.log('START ' + name); await action(); passed++; console.log('PASS ' + name); }
async function accessibility() { const enable = page.locator('#uno-enable-accessibility'); if (await enable.count()) await enable.dispatchEvent('click'); await page.waitForTimeout(500); }
async function click(name, role = 'button') {
  const item = page.getByRole(role, { name, exact: true }).first(); await item.waitFor({ state: 'attached', timeout: 10000 });
  const bounds = await item.boundingBox(); assert.ok(bounds && bounds.width > 0 && bounds.height > 0, 'Rendered control bounds: ' + name);
  await page.mouse.click(bounds.x + bounds.width / 2, bounds.y + bounds.height / 2);
}
async function activateDialog(name) {
  // Uno's semantic button click is connected to the actual managed button's
  // IInvokeProvider. Use that accessibility action for popup controls whose
  // flattened semantic bounds are local. This does not invoke app commands,
  // change diagnostic state, or call the Git worker directly.
  const button = page.getByRole('button', { name, exact: true });
  await button.waitFor({ state: 'attached' });
  assert.equal(await button.isDisabled(), false, 'Dialog action is enabled');
  await button.dispatchEvent('click');
}
async function input(name, value) {
  const field = page.getByRole('textbox', { name, exact: true });
  await field.fill(value);
  assert.equal(await field.inputValue(), value);
  // Allow managed TextBox updates and the popup's initial-focus dispatch to settle.
  await page.waitForTimeout(100);
}
async function reviewMode(index) {
  const control = page.getByRole('combobox', { name: 'Diff review mode', exact: true });
  const box = await control.boundingBox(); assert.ok(box);
  await page.mouse.click(box.x + box.width / 2, box.y + box.height / 2);
  await page.keyboard.press('Home');
  for (let n = 0; n < index; n++) await page.keyboard.press('ArrowDown');
  await page.keyboard.press('Enter');
  await page.waitForFunction(mode => gitspaceDiagnostics.reviewMode === mode, ['all', 'unstaged', 'staged'][index]);
}
async function menu(title, item) { await click(title); await click(item, 'menuitem'); }
async function ready() { await page.waitForFunction(() => globalThis.gitspaceDiagnostics?.ready && !gitspaceDiagnostics.busy, null, { timeout: 90000 }); }
try {
  await page.goto(process.env.BASE_URL || 'http://127.0.0.1:4173/GitSpace/', { waitUntil: 'domcontentloaded' });
  await ready(); await accessibility();
  await check('real Uno canvas, Git worker and tutorial repository', async () => {
    assert.ok(await page.locator('canvas').count() > 0);
    const state = await page.evaluate(() => gitspaceDiagnostics);
    assert.equal(state.repository, 'Tutorial'); assert.equal(state.changes, 4); assert.equal(state.commits, 4); assert.match(state.head, /^[a-f0-9]{40}$/);
    await page.waitForFunction(() => gitspaceDiagnostics.frames > 0);
  });
  await page.screenshot({ path: output + '/01-changes-dark.png', fullPage: true });
  await check('History tab shows real commit changes', async () => { await click('History'); await page.waitForFunction(() => gitspaceDiagnostics.history); });
  await page.screenshot({ path: output + '/02-history.png', fullPage: true });
  await check('Changes tab and split diff are interactive', async () => {
    await click('Changes'); await page.waitForFunction(() => !gitspaceDiagnostics.history);
    await click('Toggle split diff'); await page.waitForFunction(() => gitspaceDiagnostics.split);
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
  await check('new file and text editor write a real working file', async () => {
    await menu('File', 'New file…'); await input('Repository-relative path', 'browser-test.txt'); await activateDialog('Create file');
    await input('File editor', 'Created in the browser\nUnicode: zażółć\n');
    await activateDialog('Save file');
    await page.waitForFunction(() => gitspaceDiagnostics.changes === 1 && gitspaceDiagnostics.activePath === 'browser-test.txt' && gitspaceDiagnostics.rowCount === 2 && !gitspaceDiagnostics.busy);
  });
  await check('partial line selection survives refresh and commits only the chosen line', async () => {
    await reviewMode(1);
    await page.waitForFunction(() => gitspaceDiagnostics.rowCount === 2 && !gitspaceDiagnostics.split);
    const diff = await page.getByRole('group', { name: 'Diff viewer', exact: true }).boundingBox();
    assert.ok(diff); await page.mouse.click(diff.x + 180, diff.y + 9);
    await page.waitForFunction(() => gitspaceDiagnostics.selectedRows === 1);
    await menu('View', 'Refresh'); await ready();
    assert.equal(await page.evaluate(() => gitspaceDiagnostics.selectedRows), 1);
    await click('Stage selection');
    await page.waitForFunction(() => gitspaceDiagnostics.stagedFiles === 1 && !gitspaceDiagnostics.busy);
    const previous = await page.evaluate(() => gitspaceDiagnostics.head);
    await input('Commit summary', 'Commit only the first line');
    await click('Commit staged to feature/browser-test');
    await page.waitForFunction(id => gitspaceDiagnostics.head !== id && !gitspaceDiagnostics.busy, previous);
    assert.equal(await page.evaluate(() => gitspaceDiagnostics.changes), 1);
    assert.equal(await page.evaluate(() => gitspaceDiagnostics.commits), 6);
    await reviewMode(0);
  });
  await check('export ZIP preserves exact multiline working bytes and real Git metadata', async () => {
    await menu('File', 'Export repository ZIP…');
    const pending = page.waitForEvent('download'); await activateDialog('Confirm');
    const download = await pending; const zip = output + '/tutorial-export.zip'; await download.saveAs(zip);
    const files = unzipSync(await readFile(zip));
    assert.equal(new TextDecoder().decode(files['browser-test.txt']), 'Created in the browser\nUnicode: zażółć\n');
    assert.equal(new TextDecoder().decode(files['.git/HEAD']).trim(), 'ref: refs/heads/feature/browser-test');
  });
  await check('theme switch preserves the worktree', async () => {
    await menu('View', 'Toggle light / dark appearance'); await ready(); assert.equal(await page.evaluate(() => gitspaceDiagnostics.changes), 1);
  });
  await page.screenshot({ path: output + '/04-light-theme.png', fullPage: true });
  await check('reload preserves branch, history and uncommitted work', async () => {
    const head = await page.evaluate(() => gitspaceDiagnostics.head); await page.waitForTimeout(1000);
    await page.reload({ waitUntil: 'domcontentloaded' }); await ready(); await accessibility();
    const state = await page.evaluate(() => gitspaceDiagnostics); assert.equal(state.head, head); assert.equal(state.branch, 'feature/browser-test'); assert.equal(state.changes, 1);
  });
  await check('no unhandled browser exceptions', async () => { assert.deepEqual(errors, []); });
  console.log(`RESULT: ${passed} real Chromium workflows passed.`);
} finally {
  await page.screenshot({ path: output + '/last-state.png', fullPage: true }).catch(() => {});
  await writeFile(output + '/console.log', messages.join('\n')); await writeFile(output + '/errors.json', JSON.stringify(errors, null, 2));
  await writeFile(output + '/report.json', JSON.stringify({ passed, errors }, null, 2));
  await writeFile(output + '/dom.html', await page.content());
  await writeFile(output + '/state.json', JSON.stringify(await page.evaluate(() => globalThis.gitspaceDiagnostics || null), null, 2));
  await browser.close();
}
