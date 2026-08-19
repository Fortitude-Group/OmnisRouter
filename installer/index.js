#!/usr/bin/env node
'use strict';

/**
 * omnisrouter-cli — thin installer that wires common client tools (Claude Code, Codex, Cursor)
 * to a running OmnisRouter instance, mirroring familiar `npx`-style onboarding (FR-019 / T069).
 *
 * OmnisRouter auto-detects the request format from the endpoint path (docs/self-host.md), so
 * each client just needs its normal provider base-URL setting pointed at the router plus the
 * router's own bearer token — no client code or SDK changes:
 *
 *   Claude Code (Anthropic Messages) -> POST {url}     (SDK itself appends /v1/messages)
 *   Codex        (OpenAI Chat Compl.) -> POST {url}/v1  (OpenAI SDK convention already includes /v1)
 *   Cursor       (OpenAI-compatible)  -> POST {url}/v1
 *
 * Node built-ins only — no dependencies, so `npx omnisrouter-cli` needs nothing preinstalled.
 *
 * Safety: by default this tool only PRINTS the config it would apply. It NEVER edits a client's
 * real config file unless --write is passed, and even then it backs up whatever was there first.
 */

const fs = require('fs');
const os = require('os');
const path = require('path');

const DEFAULT_URL = 'http://localhost:8080';
const VALID_CLIENTS = ['claude', 'codex', 'cursor', 'print'];

function parseArgs(argv) {
  const args = { url: DEFAULT_URL, token: undefined, client: 'print', write: false, help: false };
  for (let i = 0; i < argv.length; i++) {
    const a = argv[i];
    switch (a) {
      case '--help':
      case '-h':
        args.help = true;
        break;
      case '--write':
        args.write = true;
        break;
      case '--url':
        args.url = argv[++i];
        break;
      case '--token':
        args.token = argv[++i];
        break;
      case '--client':
        args.client = argv[++i];
        break;
      default:
        if (a.startsWith('--url=')) args.url = a.slice('--url='.length);
        else if (a.startsWith('--token=')) args.token = a.slice('--token='.length);
        else if (a.startsWith('--client=')) args.client = a.slice('--client='.length);
        else {
          process.stderr.write(`Unknown argument: ${a}\n\n`);
          args.help = true;
        }
    }
  }
  return args;
}

function printHelp() {
  process.stdout.write(`omnisrouter-cli — wire Claude Code, Codex, or Cursor to a running OmnisRouter

USAGE
    npx omnisrouter-cli [--url <router-base-url>] [--token <router-token>]
                        [--client claude|codex|cursor|print] [--write]

OPTIONS
    --url <base-url>   Base URL of your running OmnisRouter instance. Default: ${DEFAULT_URL}
    --token <token>    Router bearer token (see docs/self-host.md "Adding a BYOK provider key +
                       a router token"). If omitted, a placeholder is shown instead.
    --client <name>    Which client to configure: claude | codex | cursor | print. Default: print
                       (print = show the settings for ALL clients, write nothing).
    --write            Actually write the config file for the selected client (requires --client
                       to be one of claude|codex|cursor, not print). Without --write, this tool
                       NEVER touches disk — it only prints what it would do. Existing files are
                       always backed up before being modified.
    -h, --help         Show this help and exit.

EXAMPLES
    npx omnisrouter-cli --url http://localhost:8080 --token sk-omr-...
    npx omnisrouter-cli --url http://localhost:8080 --token sk-omr-... --client cursor --write
    npx omnisrouter-cli --client claude --write
`);
}

function buildConfigs(url, token) {
  const cleanUrl = url.replace(/\/+$/, '');
  const tokenValue = token || '<YOUR_ROUTER_TOKEN>';
  return {
    claude: {
      label: 'Claude Code (Anthropic Messages format)',
      note: 'Claude Code / the Anthropic SDK append /v1/messages themselves, so ANTHROPIC_BASE_URL is the router root (no /v1 suffix).',
      env: {
        ANTHROPIC_BASE_URL: cleanUrl,
        ANTHROPIC_AUTH_TOKEN: tokenValue,
      },
      configPath: path.join(os.homedir(), '.claude', 'settings.json'),
      writeStrategy: 'claude-settings-json',
    },
    codex: {
      label: 'Codex (OpenAI Chat Completions format)',
      note: 'OpenAI-style clients expect the base URL to already include /v1.',
      env: {
        OPENAI_BASE_URL: `${cleanUrl}/v1`,
        OPENAI_API_KEY: tokenValue,
      },
      configPath: path.join(os.homedir(), '.codex', 'config.toml'),
      writeStrategy: 'codex-config-toml',
    },
    cursor: {
      label: 'Cursor (OpenAI-compatible format)',
      note: 'Cursor has no documented, safe-to-script config file for this — set it in the UI: Cursor Settings -> Models -> OpenAI API Base URL / API Key.',
      env: {
        OPENAI_BASE_URL: `${cleanUrl}/v1`,
        OPENAI_API_KEY: tokenValue,
      },
      configPath: null,
      writeStrategy: 'print-only',
    },
  };
}

function printEnvBlock(label, note, env) {
  process.stdout.write(`\n--- ${label} ---\n`);
  if (note) process.stdout.write(`# ${note}\n`);
  for (const [k, v] of Object.entries(env)) {
    process.stdout.write(`export ${k}=${JSON.stringify(v)}\n`);
  }
}

function timestamp() {
  return new Date().toISOString().replace(/[:.]/g, '-');
}

function backupFile(filePath) {
  if (!fs.existsSync(filePath)) return null;
  const backupPath = `${filePath}.bak-${timestamp()}`;
  fs.copyFileSync(filePath, backupPath);
  return backupPath;
}

/**
 * Claude Code's settings.json supports a top-level "env" object of environment variables applied
 * to the CLI process. We merge into it rather than overwrite the file, and back up first.
 */
function writeClaudeSettings(configPath, env) {
  fs.mkdirSync(path.dirname(configPath), { recursive: true });
  let existing = {};
  if (fs.existsSync(configPath)) {
    const raw = fs.readFileSync(configPath, 'utf8');
    try {
      existing = raw.trim() ? JSON.parse(raw) : {};
    } catch (err) {
      throw new Error(
        `Refusing to write: ${configPath} exists but is not valid JSON (${err.message}). ` +
          `Fix or remove it manually, then re-run with --write.`
      );
    }
  }
  const backupPath = backupFile(configPath);
  const merged = { ...existing, env: { ...(existing.env || {}), ...env } };
  fs.writeFileSync(configPath, JSON.stringify(merged, null, 2) + '\n', 'utf8');
  return { configPath, backupPath };
}

/**
 * Codex CLI's config.toml supports a [model_providers.<id>] table (name/base_url/env_key/wire_api).
 * We never parse or rewrite the existing TOML (no TOML parser in Node built-ins, and we don't want
 * to risk corrupting a config we can't fully understand) — we only ever APPEND a clearly delimited,
 * idempotently-replaceable block, after backing up whatever was there.
 */
function writeCodexConfig(configPath, env) {
  fs.mkdirSync(path.dirname(configPath), { recursive: true });
  const marker = '# >>> omnisrouter-cli managed block >>>';
  const endMarker = '# <<< omnisrouter-cli managed block <<<';
  const block = [
    marker,
    '# Added by omnisrouter-cli. Safe to edit or delete this block by hand.',
    '# Set model_provider = "omnisrouter" (top level, or per-profile) to actually route through it.',
    '[model_providers.omnisrouter]',
    'name = "OmnisRouter"',
    `base_url = "${env.OPENAI_BASE_URL}"`,
    'env_key = "OMNISROUTER_API_KEY"',
    'wire_api = "chat"',
    endMarker,
    '',
  ].join('\n');

  let existing = '';
  let backupPath = null;
  if (fs.existsSync(configPath)) {
    existing = fs.readFileSync(configPath, 'utf8');
    backupPath = backupFile(configPath);
    const startIdx = existing.indexOf(marker);
    const endIdx = existing.indexOf(endMarker);
    if (startIdx !== -1 && endIdx !== -1) {
      // Replace a previously-written block in place rather than appending a duplicate.
      existing = existing.slice(0, startIdx) + block + existing.slice(endIdx + endMarker.length + 1);
    } else {
      existing = existing.replace(/\s*$/, '\n') + '\n' + block;
    }
  } else {
    existing = block;
  }
  fs.writeFileSync(configPath, existing, 'utf8');
  process.stdout.write(
    `\nSet the token before running codex: export OMNISROUTER_API_KEY=${JSON.stringify(
      env.OPENAI_API_KEY
    )}\n`
  );
  return { configPath, backupPath };
}

function main() {
  const args = parseArgs(process.argv.slice(2));

  if (args.help) {
    printHelp();
    process.exit(0);
  }

  if (!VALID_CLIENTS.includes(args.client)) {
    process.stderr.write(`Invalid --client "${args.client}". Must be one of: ${VALID_CLIENTS.join(', ')}\n`);
    process.exit(1);
  }

  if (args.write && args.client === 'print') {
    process.stderr.write('--write requires --client claude|codex|cursor (not "print").\n');
    process.exit(1);
  }

  if (!args.token) {
    process.stdout.write(
      'Note: no --token supplied. Showing a placeholder — pass --token <router-token> for real output.\n' +
        '(See docs/self-host.md: "Adding a BYOK provider key + a router token" for how to mint one.)\n'
    );
  }

  const configs = buildConfigs(args.url, args.token);

  if (args.client === 'print') {
    process.stdout.write(`OmnisRouter client configuration for base URL: ${args.url}\n`);
    for (const key of ['claude', 'codex', 'cursor']) {
      printEnvBlock(configs[key].label, configs[key].note, configs[key].env);
    }
    process.stdout.write(
      '\nRun again with --client claude|codex|cursor --write to apply one of these (a backup is\n' +
        'made of any existing config first). Cursor has no safe config file to write to — see its\n' +
        'block above for the values to paste into the UI.\n'
    );
    return;
  }

  const cfg = configs[args.client];

  if (!args.write) {
    process.stdout.write(`OmnisRouter client configuration for ${cfg.label} (base URL: ${args.url})\n`);
    printEnvBlock(cfg.label, cfg.note, cfg.env);
    if (cfg.configPath) {
      process.stdout.write(`\nWould write to: ${cfg.configPath}\nRe-run with --write to apply (a backup is made first).\n`);
    } else {
      process.stdout.write('\nThis client has no config file omnisrouter-cli can safely write — apply the values above by hand.\n');
    }
    return;
  }

  // --write path.
  if (cfg.writeStrategy === 'print-only') {
    process.stdout.write(
      `${cfg.label} has no documented, safe-to-script config file, so --write is a no-op here.\n` +
        `Configure it by hand instead:\n`
    );
    printEnvBlock(cfg.label, cfg.note, cfg.env);
    return;
  }

  try {
    let result;
    if (cfg.writeStrategy === 'claude-settings-json') {
      result = writeClaudeSettings(cfg.configPath, cfg.env);
    } else if (cfg.writeStrategy === 'codex-config-toml') {
      result = writeCodexConfig(cfg.configPath, cfg.env);
    } else {
      throw new Error(`Unknown write strategy: ${cfg.writeStrategy}`);
    }
    process.stdout.write(`\nWrote ${cfg.label} config to: ${result.configPath}\n`);
    process.stdout.write(
      result.backupPath ? `Backed up previous file to: ${result.backupPath}\n` : '(no previous file existed)\n'
    );
  } catch (err) {
    process.stderr.write(`\nFailed to write config: ${err.message}\n`);
    process.exit(1);
  }
}

main();
