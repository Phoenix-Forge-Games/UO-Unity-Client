#!/usr/bin/env node
// game-mcp: an MCP server (stdio) for the game server's admin API (Docs/AdminTools.md), so AI agents can look after
// the live game: who's on, accounts and admin access, announcements, teleports, items, creatures, skills, saves,
// planned restarts, and content changes that go live with no restart.
//
// No dependencies: Node 18+ only. The admin API listens on 127.0.0.1 only, so this reaches it one of two ways
// (config.json, or GAME_MCP_TARGET=local|ssh):
//   ssh   - through `ssh <host>` (key auth), running a short PowerShell relay on the server; the API token is read
//           there and never leaves the server.
//   local - this PC's own server (Solo or a local build): 127.0.0.1, token from ServerData/admin-token.txt.

import { spawn } from 'node:child_process';
import { readFileSync, writeFileSync, existsSync, mkdirSync } from 'node:fs';
import net from 'node:net';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const HERE = path.dirname(fileURLToPath(import.meta.url));
const PROJECT = path.resolve(HERE, '..', '..');

const DEFAULTS = {
  target: 'ssh',
  port: 7771,
  ssh: { host: 'phoenix-server', bin: 'ssh', serverData: 'C:\\Games\\UOGameServer\\ServerData', log: 'C:\\Games\\UOGameServer\\server.log', taskName: 'UOGameServer', process: 'UOGameServer', timeoutMs: 120000 },
  local: { serverData: path.join(PROJECT, 'ServerData') },
  contentFolder: path.join(PROJECT, 'Assets', 'UOClient', 'Content'),
};

function loadConfig() {
  let file = {};
  const p = process.env.GAME_MCP_CONFIG || path.join(HERE, 'config.json');
  if (process.env.GAME_MCP_CONFIG && !existsSync(p)) {
    // Never fall back to the defaults (the live server) when a config was asked for and can't be read.
    process.stderr.write(`game-mcp: GAME_MCP_CONFIG names ${p}, which doesn't exist (a Windows path is needed). Refusing to start.\n`);
    process.exit(2);
  }
  if (existsSync(p)) file = JSON.parse(readFileSync(p, 'utf8'));
  const cfg = { ...DEFAULTS, ...file, ssh: { ...DEFAULTS.ssh, ...(file.ssh || {}) }, local: { ...DEFAULTS.local, ...(file.local || {}) } };
  if (process.env.GAME_MCP_TARGET) cfg.target = process.env.GAME_MCP_TARGET;
  return cfg;
}

const cfg = loadConfig();

// ------------------------------------------------------------------ talking to the admin API

function runSsh(script, stdin, timeoutMs) {
  const encoded = Buffer.from(script, 'utf16le').toString('base64');
  return new Promise((resolve, reject) => {
    const child = spawn(cfg.ssh.bin, ['-o', 'BatchMode=yes', '-o', 'ConnectTimeout=10', cfg.ssh.host,
      `powershell -NoProfile -NonInteractive -EncodedCommand ${encoded}`], { windowsHide: true });
    let out = '', err = '';
    const timer = setTimeout(() => { child.kill(); reject(new Error(`No answer from ${cfg.ssh.host} in ${timeoutMs / 1000} s.`)); }, timeoutMs);
    child.stdout.on('data', (d) => (out += d));
    child.stderr.on('data', (d) => (err += d));
    child.on('error', (e) => { clearTimeout(timer); reject(e); });
    child.on('close', (code) => {
      clearTimeout(timer);
      if (code !== 0 && !out.trim()) reject(new Error(`ssh ${cfg.ssh.host} failed (${code}): ${err.trim().slice(0, 400)}`));
      else resolve(out);
    });
    child.stdin.end(stdin ?? '');
  });
}

// The relay: reads the request from stdin, adds the token kept on the server, sends it to 127.0.0.1, prints the answer.
function relayScript() {
  const tokenPath = path.win32.join(cfg.ssh.serverData, 'admin-token.txt');
  return `
$ErrorActionPreference = 'Stop'
[Console]::InputEncoding = New-Object Text.UTF8Encoding($false)
$req = [Console]::In.ReadToEnd().Trim()
if (-not (Test-Path '${tokenPath}')) { '{"ok":false,"error":"No admin token on the server yet: the game server has not started with the admin API."}'; exit 0 }
$tok = (Get-Content -Raw '${tokenPath}').Trim()
$line = '{"token":"' + $tok + '",' + $req.Substring(1)
try { $c = New-Object Net.Sockets.TcpClient('127.0.0.1', ${Number(cfg.port)}) }
catch { '{"ok":false,"error":"The game server is not answering on 127.0.0.1:${Number(cfg.port)} (stopped, starting, or a build without the admin API)."}'; exit 0 }
try {
  $c.ReceiveTimeout = 125000
  $s = $c.GetStream()
  $enc = New-Object Text.UTF8Encoding($false)
  $w = New-Object IO.StreamWriter($s, $enc); $w.NewLine = "\`n"; $w.AutoFlush = $true
  $w.WriteLine($line)
  $r = New-Object IO.StreamReader($s, $enc)
  [Console]::OutputEncoding = $enc
  $r.ReadLine()
} finally { $c.Close() }`;
}

function callLocal(request) {
  const tokenPath = path.join(cfg.local.serverData, 'admin-token.txt');
  if (!existsSync(tokenPath)) return Promise.resolve({ ok: false, error: `No ${tokenPath}: the local server hasn't started with the admin API.` });
  const token = readFileSync(tokenPath, 'utf8').trim();
  return new Promise((resolve) => {
    const socket = net.connect(cfg.port, '127.0.0.1');
    let data = '';
    socket.setTimeout(125000);
    socket.on('connect', () => socket.write(JSON.stringify({ token, ...request }) + '\n'));
    socket.on('data', (d) => {
      data += d;
      const nl = data.indexOf('\n');
      if (nl >= 0) { socket.end(); resolve(JSON.parse(data.slice(0, nl))); }
    });
    socket.on('timeout', () => { socket.destroy(); resolve({ ok: false, error: 'The local server did not answer in time.' }); });
    socket.on('error', (e) => resolve({ ok: false, error: `The local server is not answering on 127.0.0.1:${cfg.port}: ${e.message}` }));
    socket.on('end', () => { if (data && data.indexOf('\n') < 0) resolve(JSON.parse(data)); });
  });
}

async function api(op, args = {}) {
  const request = { op, ...args };
  if (cfg.target === 'local') return callLocal(request);
  const out = await runSsh(relayScript(), JSON.stringify(request), cfg.ssh.timeoutMs);
  const line = out.split(/\r?\n/).map((l) => l.trim()).filter((l) => l.startsWith('{')).pop();
  if (!line) throw new Error(`Unreadable answer from the server: ${out.slice(0, 300) || '(empty)'}`);
  return JSON.parse(line);
}

// Put a file into the server's ServerData/Import folder (scp for the ssh target, a copy for local).
async function upload(file, name) {
  if (!existsSync(file)) throw new Error(`No file ${file}.`);
  if (cfg.target === 'local') {
    const dir = path.join(cfg.local.serverData, 'Import');
    mkdirSync(dir, { recursive: true });
    writeFileSync(path.join(dir, name), readFileSync(file));
    return;
  }

  const dir = path.win32.join(cfg.ssh.serverData, 'Import');
  await runSsh(`New-Item -ItemType Directory -Force '${dir}' | Out-Null; 'ok'`, '', 60000);
  await new Promise((resolve, reject) => {
    const child = spawn('scp', ['-q', '-o', 'BatchMode=yes', file, `${cfg.ssh.host}:${dir.replace(/\\/g, '/')}/${name}`], { windowsHide: true });
    let err = '';
    child.stderr.on('data', (d) => (err += d));
    child.on('close', (code) => (code === 0 ? resolve() : reject(new Error(`scp failed: ${err.trim()}`))));
    child.on('error', reject);
  });
}

// ------------------------------------------------------------------ tools

const str = (description) => ({ type: 'string', description });
const num = (description) => ({ type: 'number', description });
const bool = (description) => ({ type: 'boolean', description });

function show(res) {
  const { ok, ...rest } = res;
  if (!ok) return { text: res.error || 'The server refused.', isError: true };
  if (typeof rest.result === 'string' && Object.keys(rest).length === 1) return { text: rest.result };
  return { text: JSON.stringify(rest, null, 2) };
}

const tools = [
  {
    name: 'game_status',
    description: 'The game server at a glance: protocol version, uptime, players, accounts, creatures alive, which content files can change live and which are changed, any planned shutdown. Start here.',
    schema: {},
    run: async () => show(await api('status')),
  },
  {
    name: 'game_players',
    description: 'Everyone in the world: character id and name, account, admin and admin mode, tile position (X, Y) and height in metres, hits, dead or alive, address.',
    schema: {},
    run: async () => show(await api('players')),
  },
  {
    name: 'game_accounts',
    description: 'Every account: name, admin access, created, characters (id, name).',
    schema: {},
    run: async () => show(await api('accounts')),
  },
  {
    name: 'game_set_admin',
    description: "Grant or take away an account's admin access. An admin account plays normally and gets the Admin section in its skill book (admin mode and the Admin window). Only do this when the user asks.",
    schema: { account: str('Account name'), admin: bool('true to grant, false to take away') },
    required: ['account', 'admin'],
    run: async (a) => show(await api('account.admin', { account: a.account, admin: a.admin })),
  },
  {
    name: 'game_broadcast',
    description: 'Announce a line to everyone online.',
    schema: { text: str('The announcement') },
    required: ['text'],
    run: async (a) => show(await api('broadcast', { text: a.text })),
  },
  {
    name: 'game_kick',
    description: 'Disconnect a player (they can log in again).',
    schema: { player: str('Character name or id'), reason: str('Shown to them') },
    required: ['player'],
    run: async (a) => show(await api('kick', a)),
  },
  {
    name: 'game_teleport',
    description: 'Move a player (and the pets following them): to tile coordinates ("x y", or "x y z" for an exact height), a starting city\'s name, or another player\'s name.',
    schema: { player: str('Character name or id'), to: str('"x y", "x y z", a starting city, or a player') },
    required: ['player', 'to'],
    run: async (a) => show(await api('teleport', a)),
  },
  {
    name: 'game_give',
    description: "Put items into a player's backpack. Item names are content types (Items.json), e.g. Gold, Bandage, Longsword; a partial name takes the first match.",
    schema: { player: str('Character name or id'), item: str('Item type'), amount: num('How many (default 1)') },
    required: ['player', 'item'],
    run: async (a) => show(await api('give', a)),
  },
  {
    name: 'game_spawn',
    description: 'Spawn creatures (Creatures.json types, e.g. Orc, Mongbat; a partial name takes the first match) in front of a player, or at a place. They belong to no spawn point and stay until killed.',
    schema: { creature: str('Creature type'), count: num('How many, 1-50 (default 1)'), player: str('In front of this player'), at: str('Or at a place: "x y" or a starting city (ground must be loaded there: someone nearby)') },
    required: ['creature'],
    run: async (a) => show(await api('spawn', a)),
  },
  {
    name: 'game_heal',
    description: 'Restore a player fully (hits, mana, stamina, poison, paralysis), or resurrect them if dead.',
    schema: { player: str('Character name or id') },
    required: ['player'],
    run: async (a) => show(await api('heal', a)),
  },
  {
    name: 'game_kill',
    description: 'Kill a player (not one in admin mode). Only when the user asks.',
    schema: { player: str('Character name or id') },
    required: ['player'],
    run: async (a) => show(await api('kill', a)),
  },
  {
    name: 'game_set_skill',
    description: "Set a player's skill (0-120), by key or name (Skills.json), e.g. Magery, Swordsmanship, \"Animal Taming\".",
    schema: { player: str('Character name or id'), skill: str('Skill key or name'), value: num('0 to 120') },
    required: ['player', 'skill', 'value'],
    run: async (a) => show(await api('skill', a)),
  },
  {
    name: 'game_save',
    description: 'Save the world now (characters, houses, boats).',
    schema: {},
    run: async () => show(await api('save')),
  },
  {
    name: 'game_shutdown',
    description: 'Plan a server stop: everyone is warned (10 and 5 minutes, 2, 1, 30 s, 10 s, 5 s as they come), the world is saved, and the process exits so the deploy or the scheduled task brings the new build up. Negative seconds calls it off. Use instead of killing the process: a kill loses everything since the last save.',
    schema: { seconds: num('Seconds from now (0: at once; negative: call off)'), message: str('What the warnings say, e.g. "The server restarts for an update"') },
    required: ['seconds'],
    run: async (a) => show(await api('shutdown', a)),
  },
  {
    name: 'game_restart',
    description: 'Restart the game server gently (ssh target): warn everyone, save, stop, then start it again from its scheduled task and wait until it answers. Ask the user first when players are online.',
    schema: { seconds: num('Warning time in seconds (default 60)'), message: str('What the warnings say (default "The server is restarting")') },
    run: async (a) => {
      if (cfg.target === 'local') return { text: 'game_restart needs the ssh target.', isError: true };
      const seconds = Math.max(0, Number(a.seconds ?? 60));
      const res = await api('shutdown', { seconds, message: a.message });
      if (!res.ok) return show(res);
      const wait = `$ErrorActionPreference='Stop'; $deadline = (Get-Date).AddSeconds(${seconds + 90}); ` +
        `while ((Get-Process '${cfg.ssh.process}' -ErrorAction SilentlyContinue) -and (Get-Date) -lt $deadline) { Start-Sleep 2 }; ` +
        `if (Get-Process '${cfg.ssh.process}' -ErrorAction SilentlyContinue) { 'STILL_RUNNING' } else { Start-ScheduledTask -TaskName '${cfg.ssh.taskName}'; 'STARTED' }`;
      const out = (await runSsh(wait, '', (seconds + 120) * 1000)).trim();
      if (out.includes('STILL_RUNNING')) return { text: `The server didn't stop within ${seconds + 90} s; nothing was started. Check game_log.`, isError: true };
      for (let i = 0; i < 30; i++) {
        await new Promise((r) => setTimeout(r, 3000));
        const st = await api('status').catch(() => null);
        if (st?.ok) return { text: `Restarted: saved, stopped and started again; answering after ${(i + 1) * 3} s.` };
      }
      return { text: 'Started again, but the admin API is not answering yet. Check game_log.', isError: true };
    },
  },
  {
    name: 'game_start',
    description: 'Start the game server from its scheduled task (ssh target), when it is stopped.',
    schema: {},
    run: async () => {
      if (cfg.target === 'local') return { text: 'game_start needs the ssh target.', isError: true };
      const out = (await runSsh(`if (Get-Process '${cfg.ssh.process}' -ErrorAction SilentlyContinue) { 'RUNNING' } else { Start-ScheduledTask -TaskName '${cfg.ssh.taskName}'; 'STARTED' }`, '', 60000)).trim();
      return { text: out.includes('RUNNING') ? 'It is already running.' : 'Started; it answers in about 30 s.' };
    },
  },
  {
    name: 'game_content_list',
    description: 'The content files that can change live (no restart, no client update), whether each is changed on the server, and its size.',
    schema: {},
    run: async () => show(await api('content.list')),
  },
  {
    name: 'game_content_get',
    description: 'Read live content as the server runs it. With path (a JSONPath, e.g. "$.Creatures[?(@.Type==\'Orc\')]"), only the matches; without, the whole file (large: Creatures, Crafting and Quests run to megabytes, so prefer a path).',
    schema: { file: str('File name without .json, e.g. Creatures, Quests, Spells, Shops'), path: str('JSONPath to select') },
    required: ['file'],
    run: async (a) => show(await api('content.get', a)),
  },
  {
    name: 'game_content_set',
    description: 'Change live content: replace what a JSONPath selects with a value (or append the value to the list it selects). The server loads it at once, keeps it across restarts (ServerData/Content), and sends it to every client. A change that breaks the file is refused and nothing changes. Existing creatures update in place; new creature types are added at the end. Use game_content_pull afterwards to bring the change into the project.',
    schema: { file: str('File name without .json'), path: str('JSONPath, e.g. "$.Creatures[?(@.Type==\'Orc\')].Hits"'), value: { description: 'The new JSON value' }, append: bool('Append value to the list at path instead of replacing') },
    required: ['file', 'path', 'value'],
    run: async (a) => show(await api('content.set', a)),
  },
  {
    name: 'game_content_put',
    description: 'Replace a whole live content file with new text (validated, then live as with game_content_set). For large files prefer game_content_set.',
    schema: { file: str('File name without .json'), text: str('The whole file') },
    required: ['file', 'text'],
    run: async (a) => show(await api('content.put', a)),
  },
  {
    name: 'game_content_revert',
    description: "Put a live content file back to the copy the game shipped with, and forget the server's change.",
    schema: { file: str('File name without .json') },
    required: ['file'],
    run: async (a) => show(await api('content.revert', a)),
  },
  {
    name: 'game_content_pull',
    description: "Copy a live content file as the server runs it into this project's Assets/UOClient/Content/<file>.json, so the next build ships it (and the server then drops its kept change as matching). Review the diff before committing.",
    schema: { file: str('File name without .json') },
    required: ['file'],
    run: async (a) => {
      const res = await api('content.get', { file: a.file });
      if (!res.ok) return show(res);
      const dest = path.join(cfg.contentFolder, `${res.file}.json`);
      writeFileSync(dest, res.text, 'utf8');
      return { text: `Wrote ${dest} (${res.text.length.toLocaleString()} characters; changed on the server: ${res.changed}).` };
    },
  },
  {
    name: 'game_log',
    description: "The end of the game server's log (ssh target only): startup lines, logins, admin actions ([Game] Admin ...), errors.",
    schema: { lines: num('How many lines (default 80)'), filter: str('Only lines containing this text') },
    run: async (a) => {
      if (cfg.target === 'local') return { text: 'game_log reads the remote server\'s log; for a local server read its log file directly.', isError: true };
      const n = Math.min(Math.max(Number(a.lines) || 80, 1), 2000);
      const filter = (a.filter || '').replace(/'/g, "''");
      const script = `$ErrorActionPreference='Stop'; [Console]::OutputEncoding = New-Object Text.UTF8Encoding($false); ` +
        (filter ? `Get-Content '${cfg.ssh.log}' | Select-String -SimpleMatch '${filter}' | Select-Object -Last ${n} | ForEach-Object Line`
          : `Get-Content '${cfg.ssh.log}' -Tail ${n}`);
      return { text: (await runSsh(script, '', 60000)).trim() || '(empty)' };
    },
  },
  {
    name: 'game_import_servuo',
    description: "Move a ServUO shard's players in (Docs/ServUOImport.md): accounts with their old passwords, characters, belongings, pets, houses as plot deeds. Takes the file the shard's [ExportForUnity command writes (Tools/ServUOExport), and optionally an import config mapping the shard's custom classes, properties and skills. localFile and localConfig upload them to the server first. Dry run by default: a full report (including what doesn't map) and nothing changed. Characters already imported from that shard are skipped unless replace is true (replacing throws away what they did here since). Only import for real when the user asks.",
    schema: {
      localFile: str('The export on this PC to upload first (optional: the server keeps the last one, ServerData/Import/servuo-export.json)'),
      localConfig: str('An import config on this PC to upload first (optional: ServerData/Import/import-config.json, else stock-ServUO defaults)'),
      dryRun: bool('Report only (default true)'),
      replace: bool('Replace characters imported before (default false)'),
    },
    run: async (a) => {
      for (const [file, name] of [[a.localFile, 'servuo-export.json'], [a.localConfig, 'import-config.json']]) {
        if (file) await upload(file, name);
      }

      return show(await api('import.servuo', { dryRun: a.dryRun ?? true, replace: a.replace ?? false }));
    },
  },
  {
    name: 'game_api',
    description: 'Any admin API operation by name, with its arguments (Docs/AdminTools.md lists them), for anything the other tools do not cover.',
    schema: { op: str('Operation, e.g. "status"'), args: { type: 'object', description: 'Its arguments' } },
    required: ['op'],
    run: async (a) => show(await api(a.op, a.args || {})),
  },
];

// ------------------------------------------------------------------ MCP over stdio (JSON-RPC, one message per line)

const INSTRUCTIONS =
  'Admin tools for the SeriousFun game server (the Unity rewrite; not ServUO). Start with game_status. ' +
  'Content changes (game_content_set/put) go live at once for everyone with no restart and no client update; ' +
  'pull them into the project with game_content_pull so the next build ships them. Code changes still need a new build: ' +
  'restart with game_shutdown (warns players, saves) rather than killing the process. Every admin action is logged on the server. ' +
  'Ask the user before kicking, killing, granting admin access, or shutting down while players are online.';

function send(msg) {
  process.stdout.write(JSON.stringify(msg) + '\n');
}

async function handle(msg) {
  const { id, method, params } = msg;
  if (id === undefined || id === null) return; // a notification
  try {
    switch (method) {
      case 'initialize':
        return send({ jsonrpc: '2.0', id, result: {
          protocolVersion: params?.protocolVersion || '2025-06-18',
          capabilities: { tools: {} },
          serverInfo: { name: 'game-mcp', version: '1.0.0' },
          instructions: INSTRUCTIONS,
        } });
      case 'ping':
        return send({ jsonrpc: '2.0', id, result: {} });
      case 'tools/list':
        return send({ jsonrpc: '2.0', id, result: { tools: tools.map((t) => ({
          name: t.name,
          description: t.description,
          inputSchema: { type: 'object', properties: t.schema, required: t.required || [] },
        })) } });
      case 'tools/call': {
        const tool = tools.find((t) => t.name === params?.name);
        if (!tool) return send({ jsonrpc: '2.0', id, error: { code: -32602, message: `Unknown tool ${params?.name}` } });
        let out;
        try {
          out = await tool.run(params.arguments || {});
        } catch (e) {
          out = { text: e.message, isError: true };
        }
        return send({ jsonrpc: '2.0', id, result: { content: [{ type: 'text', text: out.text }], isError: !!out.isError } });
      }
      default:
        return send({ jsonrpc: '2.0', id, error: { code: -32601, message: `Method not found: ${method}` } });
    }
  } catch (e) {
    send({ jsonrpc: '2.0', id, error: { code: -32603, message: e.message } });
  }
}

let buffer = '';
process.stdin.setEncoding('utf8');
process.stdin.on('data', (chunk) => {
  buffer += chunk;
  let nl;
  while ((nl = buffer.indexOf('\n')) >= 0) {
    const line = buffer.slice(0, nl).trim();
    buffer = buffer.slice(nl + 1);
    if (!line) continue;
    let msg;
    try {
      msg = JSON.parse(line);
    } catch {
      send({ jsonrpc: '2.0', id: null, error: { code: -32700, message: 'Parse error' } });
      continue;
    }
    handle(msg);
  }
});
process.stderr.write(`game-mcp ready (${tools.length} tools, target ${cfg.target})\n`);
