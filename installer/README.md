# omnisrouter-cli

Thin installer that wires common client tools — **Claude Code**, **Codex**, and **Cursor** — to a
running [OmnisRouter](../README.md) instance. Mirrors the familiar `npx`-style onboarding of other
router tools (FR-019): point the client's normal provider base URL at the router, keep the client's
own SDK/config unchanged.

Node built-ins only — no dependencies to install.

## Usage

```bash
# Show the config for ALL clients (default). Never touches disk.
npx omnisrouter-cli --url http://localhost:8080 --token <router-token>

# Show the config for just one client.
npx omnisrouter-cli --url http://localhost:8080 --token <router-token> --client cursor

# Actually write the config file for one client (backs up any existing file first).
npx omnisrouter-cli --url http://localhost:8080 --token <router-token> --client cursor --write
```

Or, from a checkout of this repo:

```bash
node installer/index.js --help
node installer/index.js --url http://localhost:8080 --token <router-token>
```

Get a router token from a running OmnisRouter instance first — see
[`docs/self-host.md`](../docs/self-host.md), "Adding a BYOK provider key + a router token".

## Options

| Flag | Default | Meaning |
|---|---|---|
| `--url <base-url>` | `http://localhost:8080` | Base URL of your running OmnisRouter instance |
| `--token <token>` | _(placeholder shown)_ | Router bearer token |
| `--client <name>` | `print` | `claude` \| `codex` \| `cursor` \| `print` |
| `--write` | off | Actually write the client's config file. Without it, this tool only prints — it never touches disk |

## What gets configured

OmnisRouter auto-detects the request format from the endpoint path, so each client just needs its
normal provider base URL pointed at the router, plus the router's bearer token — no SDK changes:

| Client | Format | Base URL setting | Where |
|---|---|---|---|
| Claude Code | Anthropic Messages | `ANTHROPIC_BASE_URL=<url>` (root — the SDK appends `/v1/messages` itself), `ANTHROPIC_AUTH_TOKEN=<token>` | `env` block in `~/.claude/settings.json` |
| Codex | OpenAI Chat Completions | `OPENAI_BASE_URL=<url>/v1`, `OPENAI_API_KEY=<token>` | `[model_providers.omnisrouter]` block in `~/.codex/config.toml` |
| Cursor | OpenAI-compatible | `OPENAI_BASE_URL=<url>/v1`, `OPENAI_API_KEY=<token>` | No safe scriptable config file — set via Cursor Settings -> Models -> OpenAI API Base URL / API Key |

## Safety

- **Default is print-only.** Nothing is written to disk unless you pass `--write`.
- **Existing files are always backed up first**, next to the original as
  `<file>.bak-<ISO-timestamp>`.
- Claude Code's `settings.json` is merged (only the `env` keys above are touched; every other key
  in the file is preserved). If the existing file isn't valid JSON, the tool refuses to write
  rather than guess.
- Codex's `config.toml` is never parsed or rewritten wholesale — omnisrouter-cli only appends (or,
  on a second run, replaces in place) a clearly delimited
  `# >>> omnisrouter-cli managed block >>>` / `# <<< omnisrouter-cli managed block <<<` section, so
  you can always find, hand-edit, or delete exactly what it added.
- Cursor has no documented, safe-to-script config file for its model base URL, so `--client cursor
  --write` is intentionally a no-op that prints the values to paste into the UI instead of
  guessing at an undocumented file format.
