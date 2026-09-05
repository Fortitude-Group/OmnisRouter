# OmnisRouter — professional install & execution surface for the collect watcher

**Date:** 2026-09-05
**Status:** Design approved — ready for implementation planning
**Scope:** The `omnisrouter collect --watch` subscription-observe mode only. The routing proxy and
its `/ui` are out of scope.

## Problem

The subscription watcher is run today as a console command left open in a window:

```
C:\Tools\omnisrouter-v0.1.3-win-x64\omnisrouter.exe collect \
  --url https://app.omnisvigil.com --key ovk_… --all --watch
```

It tails Claude Code's transcripts, prices the tokens, and posts content-free receipts to the
OmnisVigil cloud dashboard (`https://app.omnisvigil.com`). Two problems:

1. **Amateurish execution surface** — a console window left open in another window, launched from a
   zip unpacked into `C:\Tools\…`, with the project key sitting in the process command line.
2. **Poor activity surface** — scrolling lines like
   `[14:01:35] +1 new receipt(s)   (session total 49,075)` are effectively debug output masquerading
   as an activity display.

The goal is a professional install-and-run experience on Windows: installed like a real app,
auto-starting in the background with no window, and reporting itself through a glanceable tray icon
rather than a scrolling console.

## Key facts that shaped the design

- **One binary, two modes** (`src/OmnisRouter.Api/Program.cs:23`): `omnisrouter collect …` is the
  watcher; `omnisrouter` (no `collect`) is the routing proxy web host that serves `/ui`, `/health`.
  There are **not** two executables.
- **The watcher only exists to feed the cloud.** Its whole job is to push receipts to OmnisVigil.
  That dashboard (`app.omnisvigil.com`) was recently rebuilt into a full sidebar app (spend ledger,
  savings, kill-switch signal, members). So the watcher **already has a rich, professional
  analytics dashboard** — it is the cloud one. A local dashboard must **not** re-implement it.
- **Router mode's `/ui` exists because the proxy can run standalone** (open-core, no OmnisVigil).
  That justification does not transfer to the watcher, which is meaningless without the cloud.
- **The watcher tails per-user data** (`~/.claude/projects`) that only grows while the user is
  logged in and working. A session-0 Windows Service that runs at the login screen would have
  nothing to collect — so a user-session app is not a compromise, it is the correct lifetime.
- **The collect code is a clean, self-contained trio** — `TranscriptCollector`, `TranscriptReader`,
  `ModelPrices` (`src/OmnisRouter.Api/Collect/`) — with a single external dependency,
  `OmnisRouter.Vigil` (for `OmnisVigilOptions`). But it currently lives inside the ASP.NET web
  project and interleaves the tail loop with `Console.WriteLine`.

## Design principle: split "liveness" from "analytics"

"Show its working" is two different needs wearing one word:

| Need | Belongs |
|---|---|
| **Liveness** — is it running, connected, when did it last post, how many today, any errors? | **Local** (tray + popup). You should not have to open a browser and sign in to know a background process is alive. |
| **Analytics** — spend, savings, trends, per-project/model breakdown | **Cloud** (`app.omnisvigil.com`), the single source of truth, already built. |

The tray shows liveness and always hands off to the cloud for analytics.

## Section 1 — Architecture & process model

**Execution model:** a single **user-session app, launched at login, no console window**, with the
collect engine and the tray icon in one process. Not a session-0 Windows Service. It keeps the
"managed, auto-starts, restarts on crash" feel via a per-user Scheduled Task (Section 3), without
the session-0 / IPC baggage a service + separate tray would force.

**Project changes:**

- **New library `src/OmnisRouter.Collect` (`net10.0`, no web deps).** Move the three collect files
  here. References `OmnisRouter.Vigil` for `OmnisVigilOptions` (or a slimmed options type).
- **`OmnisRouter.Api`** references `OmnisRouter.Collect` and keeps the `omnisrouter collect …` CLI
  mode working exactly as today (Linux / CI / headless). The `Program.cs:23` dispatch is unchanged;
  it now calls into the library.
- **New `src/OmnisRouter.Tray` (`net10.0-windows`, WinForms, `WinExe`).** References
  `OmnisRouter.Collect`. This is the Windows face — the MSI/winget install artifact.

**Engine refactor — the pivotal move:** split the tail loop from console rendering.

- **`CollectEngine`** (in `OmnisRouter.Collect`) runs the backfill + watch loop headlessly. It
  **never touches `Console`.** It exposes an observable **status snapshot** and/or raises status
  events. It posts receipts through an injected **`IReceiptSink`** (the current `HttpClient` POST
  becomes the default implementation), which makes the engine network-free under test.
- Two thin renderers consume the engine:
  - the existing **console renderer** in the CLI path (unchanged CLI output), and
  - the **tray shell** (icon + popup).

**Status snapshot shape** (bound by both the popup and, later, richer views):

```
State         : Idle | Backfilling | Watching | Paused | Error
LastPostUtc   : timestamp?
TodayReceipts : int
TodayTokens   : long
LastError     : string?   (message + time)
Endpoint      : string    (for display)
```

This preserves the "one product, one install" rule: `OmnisRouter.Collect` is a library, not a
second CLI; the tray is just the Windows packaging of the same engine.

## Section 2 — The tray + popup surface

**Tray icon** — the always-visible liveness signal:

- **Green** = running & connected; **grey** = paused; **amber/red** = last post failed / cannot
  reach OmnisVigil.
- **Tooltip:** `OmnisRouter · last posted 14:01 · 49,075 today`.

**Right-click menu:** Pause / Resume · Open dashboard → (`app.omnisvigil.com`) · Open logs ·
Clear logs · Settings · Quit.

**Left-click popup** — a small panel anchored to the tray, a pure view over the engine's status
snapshot:

- **Ship now (liveness only):** status line, last-post time, today's receipt count, last error if
  any, and the current `Backfilling… / Watching` state.
- **Grow later (designed-for, not built now):** a mini snapshot — top models used today, token
  totals, a tiny sparkline — plus a **"Full dashboard →"** link that always hands off to the cloud
  for real analytics. Because the popup binds to the status snapshot, growing it is adding fields,
  not re-plumbing.

**Naming/branding:** **"OmnisRouter"** throughout (tray tooltip, Start-Menu entry, Installed-Apps
publisher), consistent with the one-binary story.

## Section 3 — Install & execution surface

**Packaging — an MSI (WiX), per-user, no admin.** The tool lands in **Installed Apps** with a real
publisher and version, gets a Start-Menu entry, and uninstalls cleanly — replacing the
`C:\Tools\…` zip. Per-user install means no UAC prompt. WiX is chosen because it is the standard,
scriptable way to produce this from the .NET build and slots into the existing release pipeline.

**Winget manifest — from the start.** Ship a winget manifest alongside the MSI so
`winget install OmnisRouter` works from day one, and `winget validate` runs in the release gate.
The manifest wraps the same MSI.

**Auto-start — a per-user Scheduled Task, "at log on," created by the installer.** Preferred over a
Run-key because Task Scheduler can **restart on failure** and start a few seconds after login (out
of the boot storm). This is the "managed, restarts if it crashes" feel, in the user session.

**Single instance** — a named mutex; a second launch surfaces the existing tray popup instead of
double-collecting.

**Uninstall** removes files, the Start-Menu entry, and the scheduled task; leaves user config in
place (standard behaviour).

**Deferred (noted, not gating):** an **Authenticode code-signing certificate.** An unsigned MSI
still trips SmartScreen ("unknown publisher"). Truly shrink-wrapped needs a cert; tracked as a
follow-up, not a blocker for this design.

## Section 4 — Config, first-run & status plumbing

**Config off the command line.** URL + project key move into `%APPDATA%\OmnisRouter\collect.json`.
The **project key is stored DPAPI-encrypted** (per-user, `System.Security.Cryptography.ProtectedData`),
never plaintext — which also removes the secret from the process command line where it sits today.
The headless CLI keeps accepting `--url/--key` for CI.

**First-run onboarding.** Launched with no config, the popup opens in a connect state:

- URL field, defaulted to `https://app.omnisvigil.com`.
- Project-key field.
- An **"Open Connect page →"** button deep-linking to the OmnisVigil Connect page (which already
  generates a key + copyable command).

Paste key → Save → it starts watching. No console, no flags to remember.

**Logs — rolling and bounded.** The engine writes a **rolling, size-capped log** at
`%LOCALAPPDATA%\OmnisRouter\logs\` (e.g. cap per file + limited retained files) so logs cannot grow
unbounded. This replaces the scrolling console as the *diagnostic* record; the tray/popup is the
*at-a-glance* record. Menu items **"Open logs"** and **"Clear logs"** manage it.

## Section 5 — Testing

**Engine (`OmnisRouter.Collect`) — where the real coverage lives.** With the POST behind
`IReceiptSink`, tests use a fake sink and no network:

- **Idempotency** — an entry id is never double-counted across backfill + repeated watch ticks,
  including the failure-rollback path that re-adds ids to the seen-set on a failed tick.
- **Pricing** — `ModelPrices.CostUsd` for known models including cache-read / cache-creation tokens.
- **Status transitions** — `Idle → Backfilling → Watching → Paused`, `→ Error` on post failure and
  recovery on the next good tick; today-counters reset at local midnight.
- **Transcript reader** — fixture parsing, `--since` window, malformed lines skipped.
- **Dry-run** counts preserved (ported behaviour).

**Config** — `collect.json` round-trip; DPAPI encrypt/decrypt of the key (Windows-guarded test);
no-config → onboarding state.

**Tray shell** — kept logic-free (pure data-binding), so testing is light: a launch-and-stays-alive
smoke check (capture the process handle, verify alive, do not block) and the single-instance mutex.

**Packaging** — release-gate checks that the MSI builds and `winget validate` passes on the
manifest.

New test project `tests/OmnisRouter.Collect.Tests`; existing `OmnisRouter.Api.Tests` still covers
CLI dispatch.

## Out of scope

- The routing proxy and its `/ui`.
- Any change to the OmnisVigil cloud dashboard or its contract endpoints.
- macOS / Linux tray surfaces (the headless `omnisrouter collect` CLI remains their path; a
  cross-platform tray is a later, separate effort).
- Code-signing certificate procurement (deferred follow-up).

## Task decomposition (for the implementation plan)

Foundational, then fan out. The engine extraction is the dependency root; once
`CollectEngine` + `IReceiptSink` + the status snapshot exist, the tray, config, installer, and
tests parallelise.

1. **[root] Extract `OmnisRouter.Collect`** — move the three files, define `CollectEngine`,
   `IReceiptSink`, status snapshot; rewire `OmnisRouter.Api` CLI to the library; keep CLI output
   identical. *(blocks the rest)*
2. **[parallel] Config + DPAPI** — `collect.json` load/save, DPAPI key protection, onboarding-state
   detection.
3. **[parallel] Tray shell `OmnisRouter.Tray`** — icon states, tooltip, menu, liveness popup,
   single-instance mutex, login launch; binds to the status snapshot.
4. **[parallel] Rolling log appender** — size cap + retention; Open/Clear logs wiring.
5. **[parallel] Packaging** — WiX MSI (per-user, Start Menu, Scheduled Task, uninstall) + winget
   manifest + release-gate validation.
6. **[after 1] Tests** — `OmnisRouter.Collect.Tests` for engine/config; smoke test for the tray.
