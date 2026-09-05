import test from 'node:test';
import assert from 'node:assert/strict';
import { createServer } from 'node:http';
import { once } from 'node:events';
import { readFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import { Client } from '../src/node_modules/@modelcontextprotocol/sdk/dist/esm/client/index.js';
import { StdioClientTransport } from '../src/node_modules/@modelcontextprotocol/sdk/dist/esm/client/stdio.js';
import { call } from '../scripts/robotstudio.mjs';

test('new CLI tools reject out-of-range motion inputs before HTTP', async () => {
  for (const [name, params] of [
    ['set_speed_override', { percent: 101 }], ['set_speed_override', { percent: 1.5 }],
    ['set_simulation_speed', { multiplier: 0 }], ['get_robot_pose', { frame: 'typo' }],
    ['create_target', { targetName: 'p1', xMm: 1, yMm: 2 }],
    ['load_rapid_program', {}], ['read_controller_file', { relativePath: 123 }],
  ]) await assert.rejects(call(name, params, { url: 'http://127.0.0.1:1' }), /parameter/i);
});

test('MCP lists all tools, validates inputs, and preserves restore failure recovery details', async t => {
  const seen = [];
  const http = createServer(async (req, res) => {
    const chunks = []; for await (const chunk of req) chunks.push(chunk);
    seen.push({ url: req.url, body: JSON.parse(Buffer.concat(chunks)) });
    res.setHeader('Content-Type', 'application/json');
    res.end(JSON.stringify(req.url === '/program/load'
      ? { success: false, message: 'Restore failed', recoveryBackup: { backupId: 'recovery123' } }
      : { success: true, position: { x: 1, y: 2, z: 3 }, units: 'mm' }));
  }).listen(0, '127.0.0.1');
  await once(http, 'listening');
  t.after(() => { http.closeAllConnections(); http.close(); });
  const client = new Client({ name: 'extended-tools-test', version: '1.0.0' });
  const transport = new StdioClientTransport({ command: process.execPath,
    args: [resolve('src/dist/server.js')],
    env: { ...process.env, ROBOTSTUDIO_API_BASE: `http://127.0.0.1:${http.address().port}` }, stderr: 'pipe' });
  t.after(() => client.close());
  await client.connect(transport);
  const { tools } = await client.listTools();
  assert.equal(tools.length, 34);
  const catalog = JSON.parse(await readFile('src/extended-tools.json', 'utf8'));
  for (const spec of catalog) assert.ok(tools.some(tool => tool.name === spec.name));
  const bad = await client.callTool({ name: 'set_speed_override', arguments: { percent: 101 } }).catch(error => error);
  assert.ok(bad instanceof Error || bad.isError);
  assert.equal(seen.length, 0);
  const pose = await client.callTool({ name: 'get_robot_pose', arguments: { frame: 'workobject' } });
  assert.equal(JSON.parse(pose.content[0].text).units, 'mm');
  assert.deepEqual(seen[0], { url: '/robot/pose', body: { frame: 'workobject' } });
  const restore = await client.callTool({ name: 'load_rapid_program', arguments: { backupId: 'saved123' } });
  assert.equal(restore.isError, true);
  assert.equal(JSON.parse(restore.content[0].text).recoveryBackup.backupId, 'recovery123');
});
