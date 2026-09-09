# Implementation Plan: Tray-managed local router proxy

**Branch**: `003-tray-proxy` (on `main`) | **Date**: 2026-09-09 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `specs/003-tray-proxy/spec.md`. Design source:
`docs/superpowers/specs/2026-09-09-omnisrouter-tray-proxy-design.md`.

## Summary

Bring the OmnisRouter routing proxy into the existing Windows tray app as a second, independent
capability alongside the flat-rate collector. The tray supervises the shipped `omnisrouter.exe` as a
hidden background child process on a loopback port, offers a settings window that manages BYOK
provider keys through the router's own management API, and wires installed coding tools at the local
router with one click and a clean revert. When routing is on, the router reports content-free
receipts to OmnisVigil using the collector's project key, and any connected client is removed from
the collector's scope so nothing is double-counted. Windows only in this scope.

## Technical Context

**Language/Version**: C# on .NET 10. Tray targets `net10.0-windows` (WinForms); the reused server
is the existing `net10.0` `OmnisRouter.Api` published self-contained for `win-x64`.

**Primary Dependencies**: WinForms (tray UI, already in use), the shipped `omnisrouter.exe` server
binary (reused, launched as a child process), `OmnisRouter.Collect` (shared collect engine, already
referenced), `System.Text.Json`, `System.Net.Http` (loopback management calls), Windows DPAPI
(`ProtectedSecret`, already in use), `schtasks` (login task, already in use). No Node dependency is
introduced, the `--write` client rewrite is reimplemented in C#.

**Storage**: reused as-is by the server, the router's SQLite `omnisrouter.db` (working dir
`%APPDATA%\OmnisRouter\router\`) and the AES-256-GCM `master.key`. New tray-owned config in
`%APPDATA%\OmnisRouter\router.json` (proxy on/off, port, DPAPI-protected router token, connected
clients). The collector's `collect.json` is untouched.

**Testing**: xUnit (matches the repo). Golden-file tests for each client link transform and its
revert, a stubbed loopback server for the keys window and the readiness/supervision path, and a
dedupe test for the collector-scope exclusion. Manual end-to-end on Windows for the real router +
real provider key + real client request, consistent with how the tray and live sign-in are verified
today.

**Target Platform**: Windows 10/11 x64 desktop.

**Project Type**: desktop app (WinForms tray) wrapping and supervising a local web-service binary.

**Performance Goals**: proxy reports "running" within a few seconds of enable (readiness poll with a
bounded timeout, default 30s), negligible idle CPU when running, last-seen/status updates marshalled
to the UI thread without blocking it.

**Constraints**: loopback-only bind (`127.0.0.1`), never a console window, per-user no-elevation
install, offline-capable (server + embedder bundled), no second copy of any provider key held by the
tray, revert must restore a connected client byte-for-byte.

**Scale/Scope**: single user, single machine, one supervised router instance, four providers, three
client integrations.

## Constitution Check

*GATE: evaluated against OmnisRouter constitution v1.5.0. Must pass before Phase 0 and again after
Phase 1.*

- **I Modular & Composable**: PASS. The client-link rewrite/revert is a self-contained library
  (`OmnisRouter.ClientLink`) with one interface per client strategy; the router supervision and the
  management client are separate units. The tray is a thin consumer.
- **II Contract Stability & SemVer**: PASS. No existing public contract changes. The feature consumes
  the already-published `/v1/keys`, `/health`, `/readyz` router API and the existing client env-var
  conventions. The new `router.json` is an internal, versioned config file (`SchemaVersion`).
- **III Comprehensive Tests for Public Contracts**: PASS (planned). The client-link transforms are a
  guaranteed output surface and get golden tests including revert and malformed-input refusal; the
  supervision and keys paths get tests against a stub. Coverage is required at merge.
- **IV Deterministic & Observable**: PASS. Client-link output is deterministic and golden-tested; the
  tray already has a rolling file log, extended to cover router lifecycle events.
- **V Simplicity & Justified Complexity**: PASS. Supervising the existing binary as a child process
  is the simplest route that reuses the whole router; in-process Kestrel hosting was rejected (see
  research.md) as more coupling for no user benefit.
- **VI Complete the Scope**: PASS. All five user stories, revert, and dedupe ship together. macOS and
  Linux desktop apps are explicitly out of scope and recorded as such, not deferred known work.
- **VII Tracker in Sync**: N/A-with-note. OmnisRouter's project of record is `specs/` plus the git
  history (no separate issue board is configured for this repo, unlike the ADO-tracked sibling). If a
  board is later added, task sync applies. Commits reference the feature by slug.
- **VIII Fresh Base**: PASS. Work starts from a freshly pushed `main` (design + spec already on it).
- **IX Ask, Then Wait**: PASS. Every gated decision (app shape, wiring, platform, port, OmnisVigil
  dedupe) was put to the owner and answered before this plan.
- **X Production Changes Wait for a Human**: PASS / not triggered. This feature ships as a desktop
  release through the existing tagged `release.yml` path; it changes no production server
  environment. The release itself remains a human-approved, script-driven step.
- **XI Establish the Mechanism**: PASS. The plan is built on a code-level recon of the actual tray,
  server, key store, client-rewrite and release mechanics (cited in research.md), not on assumption.
- **XII Explain Every Number**: PASS. The only figures shown are the collector's existing counts and
  key-set/connection states, each with a plain meaning; no unexplained metric is added.

No violations. Complexity Tracking table not required.

## Project Structure

### Documentation (this feature)

```text
specs/003-tray-proxy/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output
│   ├── client-link.md         # per-client wiring + revert contract
│   └── router-management.md    # tray ↔ router loopback contract (consumed)
└── tasks.md             # /speckit-tasks output (not created here)
```

### Source Code (repository root)

```text
src/
├── OmnisRouter.LocalProxy/          # NEW cross-platform lib (net10.0, in solution → CI-tested)
│   ├── RouterProcessState.cs        #   state enum + RouterStatus record
│   ├── ISecretProtector.cs          #   DPAPI seam (Windows impl lives in the tray)
│   ├── RouterSettings.cs            #   router.json load/save (port, protected token, connected clients)
│   ├── RouterPaths.cs               #   bundled omnisrouter.exe + working dir resolution
│   ├── RouterToken.cs               #   generate/persist the router token once
│   ├── RouterManagementClient.cs    #   loopback /health, /readyz, /v1/keys (bearer)
│   └── RouterSupervisor.cs          #   launch/supervise omnisrouter.exe child, readiness, restart
├── OmnisRouter.ClientLink/          # NEW cross-platform lib: client wiring + revert
│   ├── IClientLink.cs               #   strategy per client + ClientPriorState + ClientLinkResult
│   ├── ClaudeCodeLink.cs            #   ~/.claude/settings.json env merge + revert
│   ├── CodexLink.cs                 #   ~/.codex/config.toml managed block + env var + revert
│   ├── CursorLink.cs                #   show-only values
│   └── ClientDetection.cs           #   which clients are installed
├── OmnisRouter.Tray/                # EXTEND (WinForms, net10.0-windows) — thin consumer of the libs
│   ├── DpapiSecretProtector.cs      #   Windows ISecretProtector impl (wraps ProtectedSecret)
│   ├── KeysWindow.cs                #   provider keys settings dialog
│   ├── ConnectWindow.cs             #   connect-an-app dialog
│   ├── RouterSettingsWindow.cs      #   port setting
│   ├── TrayContext.cs               #   EXTEND: second toggle, proxy state, mode legibility, dedupe wiring
│   ├── TrayIcons.cs                 #   EXTEND: routing-live icon treatment
│   ├── StatusPopup.cs / StatusFormat.cs  # EXTEND: plain-language mode readout
│   └── LoginTask.cs                 #   EXTEND: proxy state restored at login
├── OmnisRouter.Api/                 # REUSED unchanged (the server binary the tray launches)
└── OmnisRouter.Collect/            # EXTEND: honour a connected-client exclusion set (dedupe)

tests/
├── OmnisRouter.LocalProxy.Tests/    # NEW: supervision (stub server), settings, keys client, dedupe
└── OmnisRouter.ClientLink.Tests/    # NEW: golden transforms + revert + malformed refusal

.github/workflows/release.yml         # EXTEND: windows job also publishes OmnisRouter.Api win-x64
                                      #         into the tray publish dir before wix build
installer/msi/OmnisRouter.wxs         # unchanged (globs the tray publish dir)
```

**Structure Decision**: the tray is Windows-only and not in `OmnisRouter.slnx` (CI builds on Linux),
so all testable logic lives in two new cross-platform libraries in the solution: `OmnisRouter.LocalProxy`
(supervision, management client, settings) and `OmnisRouter.ClientLink` (client wiring/revert). Windows
DPAPI sits behind `ISecretProtector` so the core stays CI-testable (Principle III). The tray is a thin
WinForms consumer. The server is reused as a bundled binary, not referenced as a project, keeping the
ONNX/web stack out of the WinForms process (Principle V). Collector dedupe is a small extension to
`OmnisRouter.Collect`.

## Complexity Tracking

No constitution violations. Table intentionally empty.
