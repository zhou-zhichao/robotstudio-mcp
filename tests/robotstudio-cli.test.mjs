import test from 'node:test';
import assert from 'node:assert/strict';
import { createServer } from 'node:http';
import { once } from 'node:events';
import { mkdtemp, readFile, writeFile, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join, resolve } from 'node:path';
import { spawn } from 'node:child_process';
import { call } from '../scripts/robotstudio.mjs';

async function mock(t, handler) {
  const server = createServer(handler).listen(0, '127.0.0.1');
  await once(server, 'listening');
  t.after(() => { server.closeAllConnections(); server.close(); });
  return `http://127.0.0.1:${server.address().port}`;
}
async function cli(args) {
  const child = spawn(process.execPath, [resolve('scripts/robotstudio.mjs'), ...args]);
  let stdout = '', stderr = '';
  child.stdout.on('data', data => { stdout += data; });
  child.stderr.on('data', data => { stderr += data; });
  const [code] = await once(child, 'close');
  return { code, stdout, stderr };
}
test('read endpoint using POST preserves filters and Unicode', async t => {
  let observed;
  const url = await mock(t, async (req, res) => {
    const chunks = []; for await (const chunk of req) chunks.push(chunk);
    observed = { path: req.url, method: req.method, body: JSON.parse(Buffer.concat(chunks)) };
    res.end('{"success":true,"objects":[]}');
  });
  await call('get_scene_objects', { nameFilter: '箱子', includeChildren: false }, { url });
  assert.deepEqual(observed, { path: '/scene/objects', method: 'POST', body: { nameFilter: '箱子', includeChildren: false } });
});
test('reject invalid inputs without making requests', async t => {
  let hits = 0;
  const url = await mock(t, (_, res) => { hits++; res.end('{}'); });
  for (const [command, params] of [
    ['set_io_signal', { signalName: 'DO_Test', value: '1' }],
    ['control_simulation', { action: 'typo' }],
    ['get_station_status', { ignored: true }],
    ['get_screenshot', { width: 5000 }],
    ['upload_rapid_module', {}],
    ['constructor', {}],
  ]) await assert.rejects(call(command, params, { url }));
  assert.equal(hits, 0);
});
test('HTTP and application errors fail, including HTTP 200 success:false', async t => {
  const url = await mock(t, (req, res) => {
    res.statusCode = req.url === '/health' ? 409 : 200;
    res.end('{"success":false,"message":"Controller busy"}');
  });
  await assert.rejects(call('health', {}, { url }), /409.*Controller busy/);
  await assert.rejects(call('get_station_status', {}, { url }), /200.*Controller busy/);
  const result = await cli(['health', '--url', url]);
  assert.equal(result.code, 1);
  assert.equal(result.stdout, '');
  assert.equal(JSON.parse(result.stderr).success, false);
});
test('malformed JSON and redirects are not accepted', async t => {
  const url = await mock(t, (req, res) => {
    if (req.url === '/health') { res.writeHead(302, { Location: '/status' }); res.end(); }
    else res.end('<html>not JSON</html>');
  });
  await assert.rejects(call('health', {}, { url }));
  await assert.rejects(call('get_station_status', {}, { url }), /invalid JSON/);
});
test('timed-out mutation is not retried', async t => {
  let hits = 0;
  const url = await mock(t, () => { hits++; });
  await assert.rejects(call('control_simulation', { action: 'stop' }, { url, timeout: 100 }), /Outcome unknown/);
  assert.equal(hits, 1);
});
test('file upload preserves RAPID quotes, Unicode and CRLF without BOM', async t => {
  const dir = await mkdtemp(join(tmpdir(), 'rs-cli-'));
  t.after(() => rm(dir, { recursive: true, force: true }));
  const code = 'MODULE Demo\r\nPROC main()\r\nTPWrite "箱子";\r\nENDPROC\r\nENDMODULE';
  const file = join(dir, 'source.mod'); await writeFile(file, '\uFEFF' + code);
  let actual;
  const url = await mock(t, async (req, res) => {
    const chunks = []; for await (const chunk of req) chunks.push(chunk);
    actual = JSON.parse(Buffer.concat(chunks)); res.end('{"success":true}');
  });
  const result = await cli(['upload_rapid_module', '--code-file', file, '--url', url]);
  assert.equal(result.code, 0, result.stderr);
  assert.equal(actual.code, code);
});
test('screenshots become PNG artifacts, not base64 text, and never overwrite', async t => {
  const dir = await mkdtemp(join(tmpdir(), 'rs-cli-'));
  t.after(() => rm(dir, { recursive: true, force: true }));
  const output = join(dir, 'view.png');
  const png = 'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+a9WQAAAAASUVORK5CYII=';
  const url = await mock(t, (_, res) => res.end(JSON.stringify({ success: true, imageBase64: png, mimeType: 'image/png' })));
  const result = await cli(['get_screenshot', '--output', output, '--url', url]);
  assert.equal(result.code, 0, result.stderr);
  assert.equal(JSON.parse(result.stdout).output, output);
  assert.equal(result.stdout.includes(png), false);
  assert.deepEqual(await readFile(output), Buffer.from(png, 'base64'));
  const repeated = await cli(['get_screenshot', '--output', output, '--url', url]);
  assert.equal(repeated.code, 1);
  assert.deepEqual(await readFile(output), Buffer.from(png, 'base64'));
});
