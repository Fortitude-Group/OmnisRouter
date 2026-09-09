# The collect tray (Windows)

`omnisrouter collect --watch` reads Claude Code's local transcripts and reports your subscription
usage to your OmnisVigil dashboard. On Windows you don't have to run it as a console command and
leave the window open. Install the tray instead: it runs quietly in the background from login and
shows a small icon that tells you, at a glance, whether collection is healthy.

The command line still works everywhere (Linux, CI, or if you just prefer it). The tray is the same
engine with a Windows face.

## Install

```powershell
winget install OmnisRouter
```

Or download `OmnisRouter-<version>-win-x64.msi` from the
[latest release](https://github.com/Fortitude-Group/OmnisRouter/releases/latest) and run it. The
installer is per-user, so it never asks for administrator rights, and it lands in "Installed apps"
like any other program.

The first time it runs it asks for two things: your OmnisVigil dashboard URL (already filled in with
`https://app.omnisvigil.com`) and your project key. The "Get a key from OmnisVigil" button opens the
dashboard's Connect page, where you can generate one. Paste it, save, and that's it. The key is
encrypted with your Windows account and stored in `%APPDATA%\OmnisRouter\collect.json`. It never sits
on a command line and never leaves your machine except as the Bearer token on the receipts it posts.

From then on it starts on its own every time you log in.

## The tray icon

- **Green** means it's watching and the last post went through.
- **Grey** means you've paused it.
- **Amber** means the last post failed and it's retrying. Collection isn't lost, it catches up.

Hover for the last-post time and today's count. Left-click for a small panel with the same plus any
error, and a link straight to the dashboard where the real spend and savings numbers live. Right-click
for the menu: pause and resume, open the dashboard, open or clear the logs, settings, and quit.

The tray shows liveness only, on purpose. The analytics belong in the dashboard, which already does
them well, so there's no second copy to drift out of step.

## Local router proxy

The tray does a second job, off by default and separate from the collector. Turn on "Local router
proxy" from the menu and it runs OmnisRouter itself, a small server on `127.0.0.1:8787`, and lets you
point your coding tools at it. Your requests then go through your own provider API keys (Anthropic,
OpenAI, Gemini or OpenRouter), billed per token by the provider, with the router's cap and
kill-switch applied on your machine. No Docker, no console.

The two toggles are independent. Report a flat-rate subscription with the collector, route through
the proxy, do both, or neither. Each is remembered across restarts. When routing is on the tray icon
changes, so you can tell at a glance that traffic is going through the router, and the first time you
turn it on it explains what per-token BYOK billing means and asks you to confirm. It's a different
model from a flat-rate plan and worth a deliberate yes.

### Provider keys

"Provider keys…" opens a window to paste a key for each provider you want to use. Keys go straight
into the router's encrypted store and are never shown back afterwards. The actions stay disabled
until the router is up.

### Connect an app

"Connect an app…" wires a coding tool to the local router in one click, and reverts it cleanly. The
tray records the tool's current settings before it changes anything, so Revert restores them exactly.

- **Claude Code** gets `ANTHROPIC_BASE_URL` and `ANTHROPIC_AUTH_TOKEN` merged into
  `~/.claude/settings.json`. Everything else in the file is left alone, and a timestamped backup is
  written first.
- **Codex** gets a small managed block in `~/.codex/config.toml` plus the `OMNISROUTER_API_KEY`
  environment variable. Connect again and it replaces that block in place rather than stacking a
  second one.
- **Cursor** has no config file that's safe to script, so the tray shows the base URL and key to
  paste into Cursor Settings, Models, with copy buttons.

Turn the proxy off while apps are still connected and the tray warns you, then offers to revert them,
so nothing is left pointing at a router that has stopped listening.

### Routed spend on the dashboard

With the proxy on and an OmnisVigil project key set, the router reports its own usage to the
dashboard, carrying the saving against the frontier model. A tool connected to the proxy is dropped
from the collector at the same moment, so the same spend never lands twice. Route without a key and
the tray offers to capture one first. Skip that and the router still runs, and reports nothing.

### The port

The router listens on `127.0.0.1:8787` by default. "Router settings…" changes the port if 8787
clashes with something else. A change restarts the router and re-points every connected app to the
new address.

## Where things live

| What | Where |
|---|---|
| Collector config (URL + encrypted key) | `%APPDATA%\OmnisRouter\collect.json` |
| Router settings (port, encrypted token, connected apps) | `%APPDATA%\OmnisRouter\router.json` |
| Router data (database, keys, embedder) | `%APPDATA%\OmnisRouter\router\` |
| Logs (rolling, size-capped) | `%LOCALAPPDATA%\OmnisRouter\logs\` |
| Auto-start | a per-user "at log on" Scheduled Task, `OmnisRouter Collect`, with restart-on-failure |

## Uninstall

Remove it from "Installed apps" (or `winget uninstall OmnisRouter`). That takes the program, its
Start-Menu entry, and the login task with it. Your config is left in place in case you reinstall.

## A note on the SmartScreen warning

Until the installer is signed with a certificate, Windows SmartScreen will warn that the publisher is
unknown. Choose "More info" then "Run anyway". Signing is on the list.
