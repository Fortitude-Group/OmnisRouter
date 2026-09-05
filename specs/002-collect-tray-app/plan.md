# Implementation Plan: Collect watcher — professional Windows install & execution surface

**Branch**: `002-collect-tray-app` | **Date**: 2026-09-05 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `specs/002-collect-tray-app/spec.md`; source design at `docs/superpowers/specs/2026-09-05-omnisrouter-tray-collect-surface-design.md`.

## Summary

Turn `omnisrouter collect --watch` from a console left open in `C:\Tools` into an installed Windows application that starts at login, runs with no window, and reports itself through a system-tray icon. Extract the collect logic out of the ASP.NET web project into a shared `OmnisRouter.Collect` library built around a headless `CollectEngine` that exposes an observable status snapshot and posts through an injected `IReceiptSink`. The existing CLI mode and a new WinForms tray shell both consume that one engine, so their behaviour cannot diverge. Ship it as a per-user WiX MSI plus a winget manifest, auto-started by a per-user Scheduled Task, with the project key stored DPAPI-encrypted instead of on the command line. Liveness lives locally; spend/savings analytics stay in the OmnisVigil dashboard.

## Technical Context

**Language/Version**: C# on .NET 10. Shared engine + CLI target `net10.0` (cross-platform); the tray targets `net10.0-windows` (WinForms). Repo-wide `Nullable`, `ImplicitUsings`, `LangVersion latest`, and `TreatWarningsAsErrors=true` (Directory.Build.props) apply to every new project.

**Primary Dependencies**: Windows Forms (tray UI, Windows-only TFM); `System.Security.Cryptography.ProtectedData` (DPAPI key protection); WiX Toolset v5 (MSI); winget manifest (distribution); existing `OmnisRouter.Vigil` (`OmnisVigilOptions`); `System.Text.Json` (config + receipts, already used); xunit + Microsoft.NET.Test.Sdk (tests).

**Storage**: `%APPDATA%\OmnisRouter\collect.json` (config; project key DPAPI-encrypted). `%LOCALAPPDATA%\OmnisRouter\logs\` (rolling, size-capped diagnostic log). No database.

**Testing**: xunit. New `tests/OmnisRouter.Collect.Tests` covers the engine, config, and DPAPI round-trip; existing `OmnisRouter.Api.Tests` continues to cover CLI dispatch. Tray shell gets a launch-and-stays-alive smoke test.

**Target Platform**: Windows 10 and later for the tray/installer. The shared engine and the `omnisrouter collect` CLI remain cross-platform (`net10.0`) for Linux/CI/macOS.

**Project Type**: desktop-app (tray) + cli (existing) + shared library.

**Performance Goals**: watcher up and posting within a few seconds of login (SC-002); watch tick default 15s; idle CPU negligible; log storage bounded by a fixed cap indefinitely (SC-006).

**Constraints**: per-user install with no administrator elevation (FR-017); no console window (FR-001); project key never in the process command line or plain-text settings (FR-011); one instance per user session (FR-005); every new project builds warning-clean under `TreatWarningsAsErrors`.

**Scale/Scope**: single local user. Initial backfill can be tens of thousands of transcript entries (a session total of ~49k receipts has been observed), so backfill must stream and show progress rather than block.

## Constitution Check

*GATE: evaluated before Phase 0 and re-evaluated after Phase 1. Constitution v1.5.0.*

| Principle | Assessment | Verdict |
|---|---|---|
| I. Modular & Composable | Collect logic becomes one shared library (`OmnisRouter.Collect`) behind an explicit engine/sink/status interface; CLI and tray are thin consumers. This is the central design move. | PASS |
| II. Contract Stability & SemVer | New public surface (`CollectEngine`, `IReceiptSink`, `CollectionStatus`, config types). No existing published contract changes; the collect internals were `internal`. Public surface kept intentional and documented (contracts/). CLI flags unchanged (FR-020). | PASS |
| III. Comprehensive Tests | New test project with edge cases (idempotency, pricing, status transitions, malformed transcripts, config/DPAPI round-trip). Coverage at merge, per policy. | PASS |
| IV. Deterministic & Observable | Engine is pure given transcripts + clock + sink; timestamps/counters isolated behind an injectable clock so status transitions are snapshot-testable. Structured local log for observability. | PASS |
| V. Simplicity & Justified Complexity | Single-process tray (no session-0 service, no IPC) is the simplest model that meets the requirement. Two new projects are justified by Principle I, not organizational bundling; simpler "leave it in the web project" rejected because a WinForms Windows-only TFM must not depend on ASP.NET. Recorded in Complexity Tracking. | PASS |
| VI. Complete the Scope | All five stories delivered in-feature; only code-signing is deferred, with its reason and tracking recorded (Assumptions). | PASS |
| VII. Tracker Is the Project of Record | OmnisRouter has no external board (GitHub repo; the `specs/` tree + git history are the record). Commits reference the feature; `/speckit-tasks` produces the task list. No ADO sync applies to this repo. | PASS (n/a board) |
| VIII. Start From a Fresh Base | Implementation starts with `git pull --ff-only` on `main`; working tree currently clean. | PASS (enforced at implement) |
| IX. Ask, Then Wait | All gating design choices were locked with the owner in the brainstorm (process model, packaging, key storage, naming). | PASS |
| X. Production Changes Wait for a Human | No production environment change. Release-pipeline edits publish artifacts; OmnisVigil Azure infra is untouched. | PASS (n/a) |
| XI. Establish the Mechanism | Plan grounded in the actual files (Program.cs dispatch, Collect trio, Vigil options, Directory.Build.props, release.yml). | PASS |
| XII. Explain Every Number | Tray shows only liveness figures (state, last-post time, today's count) — each answers "is collection healthy right now". Spend/savings deliberately stay in the dashboard rather than being half-duplicated locally. | PASS |

**Gate result: PASS.** One justified complexity entry (two new projects), recorded below. No unjustified violations.

## Project Structure

### Documentation (this feature)

```text
specs/002-collect-tray-app/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output
│   ├── collect-engine.md      # CollectEngine + IReceiptSink + CollectionStatus surface
│   ├── config-schema.md       # collect.json shape + DPAPI key handling
│   ├── cli.md                 # existing collect CLI flags (unchanged contract)
│   └── tray-ux.md             # tray icon states, menu, popup fields
└── tasks.md             # /speckit-tasks output (NOT created here)
```

### Source Code (repository root)

```text
src/
├── OmnisRouter.Collect/            # NEW net10.0 library — the shared engine
│   ├── CollectEngine.cs            #   headless backfill + watch loop, raises status; no Console
│   ├── IReceiptSink.cs             #   post abstraction (HttpReceiptSink is the default impl)
│   ├── HttpReceiptSink.cs          #   moved from TranscriptCollector.PostBatchAsync
│   ├── CollectionStatus.cs         #   observable status snapshot (record)
│   ├── CollectOptions.cs           #   parsing (moved from TranscriptCollector)
│   ├── TranscriptReader.cs         #   moved as-is
│   ├── ModelPrices.cs              #   moved as-is
│   ├── ReceiptRecord.cs            #   ToRecord() extracted
│   ├── CollectConfig.cs            #   collect.json load/save
│   └── ProtectedSecret.cs          #   DPAPI wrap/unwrap (Windows); no-op guard elsewhere
├── OmnisRouter.Api/
│   └── Collect/ConsoleCollectRunner.cs  # thin console renderer over CollectEngine (Program.cs:23 calls this)
└── OmnisRouter.Tray/               # NEW net10.0-windows WinExe — the Windows face
    ├── Program.cs                  #   single-instance mutex, hosts CollectEngine, no console
    ├── TrayIcon.cs                 #   NotifyIcon: state colour, tooltip, context menu
    ├── StatusPopup.cs              #   left-click liveness panel (view over CollectionStatus)
    ├── SetupWindow.cs              #   first-run onboarding (URL + key + Open Connect page)
    ├── LoginTask.cs                #   register/remove the per-user "at logon" Scheduled Task
    └── assets/                     #   tray icons (running/paused/error)

tests/
└── OmnisRouter.Collect.Tests/      # NEW — engine, config, pricing, DPAPI, transcript reader

installer/
├── msi/                            # NEW — WiX v5 project (per-user MSI, Start Menu, uninstall)
└── winget/                         # NEW — winget manifest (version, installer, locale)
```

**Structure Decision**: Extract the collect trio into `src/OmnisRouter.Collect` (plain `net10.0`, depends only on `OmnisRouter.Vigil` for options), consumed by both `OmnisRouter.Api` (CLI) and the new `src/OmnisRouter.Tray` (`net10.0-windows`). Packaging lives under `installer/` alongside the existing `installer/` npm CLI. The release pipeline gains a `windows-latest` job for the tray publish + MSI + winget (see research.md); the existing Ubuntu `binaries` job and the `omnisrouter collect` CLI are unchanged.

## Complexity Tracking

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| Two new projects (`OmnisRouter.Collect` library + `OmnisRouter.Tray` app) | The tray is a Windows-only WinForms `net10.0-windows` executable; the engine must be shared with the cross-platform CLI without pulling WinForms or ASP.NET into either | Leaving the code in `OmnisRouter.Api` (Sdk.Web) forces the tray to reference the whole web app, and a Windows-only TFM cannot cleanly host it; a single merged project cannot target both `net10.0` and `net10.0-windows` for these two roles |
