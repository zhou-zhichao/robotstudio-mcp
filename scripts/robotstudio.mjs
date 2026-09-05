#!/usr/bin/env node
import { readFile, writeFile, mkdir, lstat } from 'node:fs/promises';
import { readFileSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { pathToFileURL } from 'node:url';

// Schemas describe HTTP inputs; controller behavior remains in the add-in.
const text = { type: 'string' };
const bool = { type: 'boolean' };
const names = { taskName: text, moduleName: text };
const choice = (...values) => ({ type: 'string', values });
const route = (path, method = 'GET', fields = {}, required = [], timeout = 10000) =>
  ({ path, method, fields, required, timeout });
export const commands = {
  health: route('/health'),
  get_station_status: route('/status'),
  get_robot_joints: route('/joints'),
  control_simulation: route('/simulation', 'POST', { action: choice('start', 'stop', 'reset') }, ['action']),
  upload_rapid_module: route('/rapid/upload', 'POST', { ...names, code: text, replaceExisting: bool }, ['code'], 30000),
  control_rapid_execution: route('/rapid/execute', 'POST', {
    action: choice('start', 'stop', 'resetpp'), taskName: text,
    executionMode: choice('continuous', 'step_over', 'step_in'),
    cycle: choice('once', 'forever'), stopMode: choice('instruction', 'cycle', 'immediate'),
  }, ['action']),
  get_rapid_execution_status: route('/rapid/status'),
  get_rapid_module_source: route('/rapid/source', 'POST', names, [], 20000),
  list_rapid_modules: route('/rapid/modules'),
  get_execution_errors: route('/rapid/errors', 'GET', {}, [], 20000),
  get_screenshot: route('/screenshot', 'POST', {
    width: { type: 'number', integer: true, min: 1, max: 3840 },
    height: { type: 'number', integer: true, min: 1, max: 2160 },
  }, [], 20000),
  read_rapid_variable: route('/rapid/variable', 'POST', { ...names, variableName: text }, ['variableName']),
  set_rapid_variable: route('/rapid/variable/set', 'POST', { ...names, variableName: text, value: text }, ['variableName', 'value']),
  list_rapid_variables: route('/rapid/variables', 'POST', { ...names, typeFilter: text }),
  get_io_signals: route('/io/signals', 'POST', { signalName: text }),
  set_io_signal: route('/io/signals/set', 'POST', { signalName: text, value: { type: 'number' } }, ['signalName', 'value']),
  get_scene_objects: route('/scene/objects', 'POST', { nameFilter: text, includeChildren: bool }),
};

const extendedTools = JSON.parse(readFileSync(new URL('../src/extended-tools.json', import.meta.url), 'utf8'));
for (const tool of extendedTools) {
  commands[tool.name] = route(tool.path, tool.method,
    Object.fromEntries(Object.entries(tool.inputSchema.properties).map(([key, rule]) => [key, {
      type: rule.type === 'integer' ? 'number' : rule.type,
      integer: rule.type === 'integer', min: rule.minimum, max: rule.maximum, values: rule.enum,
    }])), tool.inputSchema.required, tool.timeout);
}

export async function call(command, params = {}, options = {}) {
  const spec = Object.hasOwn(commands, command) ? commands[command] : null;
  if (!spec) throw new Error(`Unknown command: ${command}. Run --help.`);
  if (!params || typeof params !== 'object' || Array.isArray(params)) throw new Error('Parameters must be a JSON object.');
  for (const key of spec.required) {
    if (!Object.hasOwn(params, key)) throw new Error(`Missing parameter: ${key}`);
  }
  for (const [key, value] of Object.entries(params)) {
    const rule = Object.hasOwn(spec.fields, key) ? spec.fields[key] : null;
    if (!rule) throw new Error(`Unknown parameter: ${key}`);
    if (typeof value !== rule.type || (rule.type === 'string' && !value.trim()) ||
        (rule.type === 'number' && !Number.isFinite(value)) ||
        (rule.values && !rule.values.includes(value)) ||
        (rule.integer && !Number.isInteger(value)) || value < rule.min || value > rule.max) {
      throw new Error(`Invalid parameter: ${key}`);
    }
  }
  const base = new URL(options.url ?? process.env.ROBOTSTUDIO_API_BASE ?? 'http://127.0.0.1:8080');
  if (!['http:', 'https:'].includes(base.protocol) || base.username || base.password || base.search || base.hash || base.pathname !== '/') {
    throw new Error('URL must be an HTTP(S) origin without credentials, path, query or fragment.');
  }
  const timeout = options.timeout ?? spec.timeout;
  if (!Number.isInteger(timeout) || timeout < 1 || timeout > 300000) throw new Error('Timeout must be 1-300000 milliseconds.');
  const abort = new AbortController();
  const timer = setTimeout(() => abort.abort(), timeout);
  try {
    const response = await fetch(new URL(spec.path, base), {
      method: spec.method, redirect: 'error', signal: abort.signal,
      headers: { 'Content-Type': 'application/json' },
      ...(spec.method === 'POST' ? { body: JSON.stringify(params) } : {}),
    });
    let result;
    try { result = JSON.parse(await response.text()); }
    catch (error) {
      if (abort.signal.aborted) throw error;
      throw new Error(`HTTP ${response.status}: invalid JSON response.`);
    }
    if (!result || typeof result !== 'object' || Array.isArray(result)) throw new Error('Expected a JSON object response.');
    if (!response.ok || result.success === false || result.error) {
      throw new Error(`HTTP ${response.status}: ${String(result.message ?? result.error ?? 'Request failed').slice(0, 1000)}`);
    }
    return result;
  } catch (error) {
    if (abort.signal.aborted) throw new Error(`Request timed out after ${timeout}ms. Outcome unknown; inspect state before retrying.`, { cause: error });
    throw error;
  } finally { clearTimeout(timer); }
}

export async function main(args) {
  const [command, ...rest] = args;
  if (!command || command === '--help') {
    console.log('Usage: node scripts/robotstudio.mjs <command> [--params-file file.json] [--code-file program.mod] [--output file] [--url origin] [--timeout milliseconds]\n' +
      'Use --describe <command> for parameters. Screenshot requires --output (PNG); other outputs save JSON. No automatic retries.\n' + Object.keys(commands).join('\n'));
    return;
  }
  if (command === '--describe') {
    if (rest.length !== 1 || !Object.hasOwn(commands, rest[0])) throw new Error('Provide one valid command to describe.');
    console.log(JSON.stringify(commands[rest[0]], null, 2));
    return;
  }
  const options = {};
  for (let i = 0; i < rest.length; i += 2) {
    const key = rest[i];
    if (!['--params-file', '--code-file', '--output', '--url', '--timeout'].includes(key) ||
        !rest[i + 1] || rest[i + 1].startsWith('--') || Object.hasOwn(options, key)) throw new Error(`Invalid or repeated option: ${key}`);
    options[key] = rest[i + 1];
  }
  if (command === 'get_screenshot' && !options['--output']) throw new Error('Screenshot requires --output file.png.');
  if (options['--code-file'] && !['upload_rapid_module', 'validate_rapid'].includes(command)) throw new Error('--code-file is only valid for upload_rapid_module or validate_rapid.');
  if (options['--output']) {
    try {
      await lstat(resolve(options['--output']));
      throw new Error('Output already exists. Choose a new filename.');
    } catch (error) { if (error.code !== 'ENOENT') throw error; }
  }
  const params = options['--params-file'] ? JSON.parse((await readFile(options['--params-file'], 'utf8')).replace(/^\uFEFF/, '')) : {};
  if (!params || typeof params !== 'object' || Array.isArray(params)) throw new Error('Parameters must be a JSON object.');
  if (options['--code-file']) {
    if (Object.hasOwn(params, 'code')) throw new Error('Use either code or --code-file, not both.');
    params.code = (await readFile(options['--code-file'], 'utf8')).replace(/^\uFEFF/, '');
  }
  const result = await call(command, params, { url: options['--url'], timeout: options['--timeout'] === undefined ? undefined : Number(options['--timeout']) });
  if (options['--output']) {
    const output = resolve(options['--output']);
    let content = JSON.stringify(result) + '\n';
    if (command === 'get_screenshot') {
      const encoded = result.imageBase64;
      if (typeof encoded !== 'string' || !/^[A-Za-z0-9+/]+={0,2}$/.test(encoded)) throw new Error('Invalid screenshot encoding.');
      content = Buffer.from(encoded, 'base64');
      if (content.toString('base64') !== encoded || !content.subarray(0, 8).equals(Buffer.from('89504e470d0a1a0a', 'hex'))) throw new Error('Response is not a PNG screenshot.');
    }
    await mkdir(dirname(output), { recursive: true });
    await writeFile(output, content, { flag: 'wx' });
    console.log(JSON.stringify({ success: true, output }));
  } else console.log(JSON.stringify(result));
}

if (process.argv[1] && import.meta.url === pathToFileURL(resolve(process.argv[1])).href) {
  main(process.argv.slice(2)).catch(error => {
    console.error(JSON.stringify({ success: false, error: error.message }));
    process.exitCode = 1;
  });
}
