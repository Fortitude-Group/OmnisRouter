---
description: "Task list for the collect watcher Windows install & execution surface"
---

# Tasks: Collect watcher — professional Windows install & execution surface

**Input**: Design documents from `specs/002-collect-tray-app/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/

**Tests**: Included — the spec's testing section and each story's Independent Test request them. Per constitution Principle III the gate is comprehensive coverage at merge; test-first ordering is encouraged but not mandatory, so tests are listed alongside their implementation rather than as a strict red-first step.

**Organization**: Grouped by user story. The engine extraction (Phase 2) is the one hard blocking root; after it, the tray stories fan out.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: can run in parallel (different files, no dependency on an incomplete task)
- **[Story]**: US1–US5 from spec.md

## Path Conventions

New: `src/OmnisRouter.Collect/` (net10.0 lib), `src/OmnisRouter.Tray/` (net10.0-windows WinExe), `tests/OmnisRouter.Collect.Tests/`, `installer/msi/` (WiX), `installer/winget/`. Existing: `src/OmnisRouter.Api/` (CLI host), `.github/workflows/release.yml`.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Create the new projects and wire them into the solution.

- [X] T001 Create `src/OmnisRouter.Collect/OmnisRouter.Collect.csproj` (net10.0 class library, references `src/OmnisRouter.Vigil`) and add it to `OmnisRouter.slnx`.
- [X] T002 [P] Create `tests/OmnisRouter.Collect.Tests/OmnisRouter.Collect.Tests.csproj` (xunit, Microsoft.NET.Test.Sdk, coverlet — mirror `tests/OmnisRouter.Api.Tests`), reference `OmnisRouter.Collect`, add to `OmnisRouter.slnx`.
- [X] T003 [P] Create `src/OmnisRouter.Tray/OmnisRouter.Tray.csproj` (`net10.0-windows`, `OutputType=WinExe`, `UseWindowsForms=true`, `ApplicationManifest` for per-user), reference `OmnisRouter.Collect`, add to `OmnisRouter.slnx` guarded so non-Windows CI builds skip it.
- [X] T004 [P] Scaffold `installer/msi/` (empty WiX v5 project) and `installer/winget/` (manifest folder) with placeholder READMEs so the packaging tasks have a home.

**Checkpoint**: `dotnet build OmnisRouter.slnx` succeeds with the empty new projects (0 warnings — `TreatWarningsAsErrors` is repo-wide).

---

## Phase 2: Foundational — Engine extraction (BLOCKING)

**Purpose**: The shared `CollectEngine`. Every user story depends on this. Guards FR-021 (one shared implementation) and FR-022 (identical receipts).

**⚠️ CRITICAL**: No user story work begins until this phase and its parity tests pass.

- [X] T005 [P] Move `ModelPrices.cs` verbatim from `src/OmnisRouter.Api/Collect/` to `src/OmnisRouter.Collect/ModelPrices.cs` (namespace `OmnisRouter.Collect`).
- [X] T006 [P] Move `TranscriptReader.cs` verbatim to `src/OmnisRouter.Collect/TranscriptReader.cs`.
- [X] T007 Extract the receipt builder (`ToRecord`) into `src/OmnisRouter.Collect/ReceiptRecord.cs`, preserving the exact JSON shape (contracts/collect-engine.md).
- [X] T008 Move option parsing into `src/OmnisRouter.Collect/CollectOptions.cs` (all flags per contracts/cli.md, unchanged).
- [X] T009 [P] Define `IReceiptSink` + `PostResult` in `src/OmnisRouter.Collect/IReceiptSink.cs` (contracts/collect-engine.md).
- [X] T010 Implement `HttpReceiptSink` in `src/OmnisRouter.Collect/HttpReceiptSink.cs` — the current `PostBatchAsync` logic verbatim (POST `/v1/ingest`, Bearer, `{schema_version:1,records:[…]}`).
- [X] T011 [P] Define `CollectionStatus` record + `CollectState` enum in `src/OmnisRouter.Collect/CollectionStatus.cs` (fields per data-model.md).
- [X] T012 [P] Define `IClock` (+ system impl) in `src/OmnisRouter.Collect/IClock.cs` and `ICollectLog` (+ a minimal file impl) in `src/OmnisRouter.Collect/ICollectLog.cs` — the rolling impl arrives in US4.
- [X] T013 Implement `CollectEngine` in `src/OmnisRouter.Collect/CollectEngine.cs`: backfill (window/`--all`), watch loop, `Pause()`/`Resume()`, `StatusChanged`, id de-dup with failed-tick rollback, today-counter rollover via `IClock`, no `Console`. Depends on T005–T012.
- [X] T014 Add `src/OmnisRouter.Api/Collect/ConsoleCollectRunner.cs` that drives `CollectEngine` and reproduces today's CLI output byte-for-byte; repoint `src/OmnisRouter.Api/Program.cs` (the `args[0]=="collect"` branch) at it; delete the old `src/OmnisRouter.Api/Collect/TranscriptCollector.cs`. Depends on T013.
- [X] T015 [P] Engine idempotency test (backfill + repeated watch ticks + failed-tick rollback never double-count or drop) in `tests/OmnisRouter.Collect.Tests/CollectEngineIdempotencyTests.cs`.
- [X] T016 [P] `ModelPrices` pricing test incl. cache-read/creation tokens in `tests/OmnisRouter.Collect.Tests/ModelPricesTests.cs`.
- [X] T017 [P] Status-transition test (Idle→Backfilling→Watching→Paused, Error then recovery, midnight rollover via a fake `IClock`) in `tests/OmnisRouter.Collect.Tests/StatusTransitionTests.cs`.
- [X] T018 [P] `TranscriptReader` test (fixtures, `--since` window, malformed lines skipped) in `tests/OmnisRouter.Collect.Tests/TranscriptReaderTests.cs`.
- [X] T019 [P] Receipt JSON-shape test pinning `ReceiptRecord` byte-for-byte to the current output (FR-022) in `tests/OmnisRouter.Collect.Tests/ReceiptRecordShapeTests.cs`.
- [X] T020 [P] CLI golden test: run a fixture through `collect --dry-run --all` and assert the header/progress/summary match the current output (FR-020) in `tests/OmnisRouter.Api.Tests/CollectCliGoldenTests.cs`.

**Checkpoint**: CLI behaves identically to before; engine parity and idempotency proven. Surfaces can now be built.

---

## Phase 3: User Story 1 — Install once and forget it, no console (Priority: P1) 🎯 MVP

**Goal**: A background tray watcher that starts at login, runs with no window, and shows liveness. For this slice it reads endpoint/key from the existing `OmnisVigil` config section (secure config is US2).

**Independent Test**: On a clean account, log in and confirm the watcher runs with a tray icon, no console, and posts receipts exactly as the old console command did.

- [ ] T021 [US1] `src/OmnisRouter.Tray/Program.cs`: single-instance `Local\` mutex (per user SID; second launch signals the running instance and exits), no console, resolve endpoint/key from the `OmnisVigil` config section, construct `CollectEngine` + `HttpReceiptSink`, run with a cancellation lifetime.
- [ ] T022 [P] [US1] Add tray icons for running/paused/error states in `src/OmnisRouter.Tray/assets/` and embed them as resources.
- [ ] T023 [US1] `src/OmnisRouter.Tray/TrayIcon.cs`: `NotifyIcon` with State→icon mapping and the tooltip strings from contracts/tray-ux.md, subscribed to `StatusChanged`. Depends on T021, T022.
- [ ] T024 [US1] `src/OmnisRouter.Tray/LoginTask.cs`: register/remove a per-user "at log on" Scheduled Task `OmnisRouter Collect` with restart-on-failure, launching the tray exe.
- [ ] T025 [P] [US1] Tray smoke test (launches, stays alive, creates the icon — capture handle, verify alive, don't block) and single-instance mutex test in `tests/OmnisRouter.Collect.Tests/TraySmokeTests.cs` (Windows-guarded).

**Checkpoint**: MVP — background tray watcher runs and reports liveness. Deployable/demoable.

---

## Phase 4: User Story 2 — Guided first-run setup, no flags (Priority: P2)

**Goal**: First-run onboarding window; key stored DPAPI-encrypted, off the command line.

**Independent Test**: With no saved config, launch → prompted for URL+key → after save it posts and never prompts again; the key is not readable in the process list or config.

- [ ] T026 [P] [US2] `src/OmnisRouter.Collect/ProtectedSecret.cs`: DPAPI `CurrentUser` wrap/unwrap (base64), clear `PlatformNotSupportedException` off-Windows (data-model.md).
- [ ] T027 [P] [US2] `src/OmnisRouter.Collect/CollectConfig.cs`: load/save `%APPDATA%\OmnisRouter\collect.json` with validation per contracts/config-schema.md (absolute URL, decryptable key, defaults).
- [ ] T028 [US2] `src/OmnisRouter.Tray/SetupWindow.cs`: endpoint pre-filled `https://app.omnisvigil.com`, masked key field, "Open Connect page →", validate + DPAPI-protect + save, "Start at login" toggle (calls `LoginTask`). Depends on T026, T027, T024.
- [ ] T029 [US2] Wire tray startup to prefer `collect.json`; missing/invalid config opens `SetupWindow` (FR-013); decrypted key feeds `HttpReceiptSink`. Depends on T028, T021.
- [ ] T030 [P] [US2] Tests: config round-trip, DPAPI round-trip (Windows-guarded), no-config→onboarding, invalid/empty key refused, in `tests/OmnisRouter.Collect.Tests/CollectConfigTests.cs`.

**Checkpoint**: A non-technical user can install and configure without touching a command line.

---

## Phase 5: User Story 3 — Glance at status, jump to the dashboard (Priority: P2)

**Goal**: Left-click liveness popup, pause/resume, and a dashboard handoff.

**Independent Test**: Click the tray → panel shows real state/last-post/today/error; Pause stops posting and Resume restarts it; Open dashboard opens OmnisVigil.

- [ ] T031 [US3] `src/OmnisRouter.Tray/StatusPopup.cs`: borderless panel anchored to the tray, bound to `CollectionStatus`, dismiss on focus loss, "Full dashboard →" link (contracts/tray-ux.md).
- [ ] T032 [US3] `src/OmnisRouter.Tray/TrayMenu.cs`: context menu (Pause/Resume, Open dashboard, Settings, Quit; Open/Clear logs added in US4). Depends on T023.
- [ ] T033 [US3] Wire Pause/Resume to `engine.Pause()/Resume()` and persist `paused` in `collect.json`; left-click opens the popup. Depends on T031, T032, T027.
- [ ] T034 [P] [US3] Status-formatting tests: tooltip and popup strings for each `CollectState` (incl. backfill progress, error+time) in `tests/OmnisRouter.Collect.Tests/StatusFormattingTests.cs` (format helpers live in `OmnisRouter.Collect` so they are testable without UI).

**Checkpoint**: The activity surface replaces the scrolling console.

---

## Phase 6: User Story 4 — Managed local logs (Priority: P3)

**Goal**: Rolling, size-capped local log with open/clear from the tray.

**Independent Test**: Run many ticks → logs never exceed the cap; Open logs shows activity; Clear empties them.

- [ ] T035 [US4] `src/OmnisRouter.Collect/RollingFileLog.cs` implementing `ICollectLog`: active file + N rotated files, per-file byte cap, oldest dropped on roll (data-model.md LogSet). Replaces the minimal impl from T012.
- [ ] T036 [US4] Point the tray's engine at `RollingFileLog` (path `%LOCALAPPDATA%\OmnisRouter\logs\`) and add "Open logs" / "Clear logs" to the menu. Depends on T035, T032.
- [ ] T037 [P] [US4] Tests: cap enforced across rolls (total ≤ `LogMaxBytes×LogMaxFiles`), Clear empties the set, in `tests/OmnisRouter.Collect.Tests/RollingFileLogTests.cs`.

**Checkpoint**: Diagnostics are captured and bounded.

---

## Phase 7: User Story 5 — Standard install, distribution & clean removal (Priority: P2)

**Goal**: Per-user MSI in "Installed apps", winget install, clean uninstall.

**Independent Test**: Install via MSI and via `winget install` → "Installed apps" entry with publisher/version + Start-Menu shortcut; uninstall removes app, shortcut, and the Scheduled Task.

- [ ] T038 [US5] Tray self-contained `win-x64` publish profile in `src/OmnisRouter.Tray/Properties/PublishProfiles/tray-win-x64.pubxml` (single-folder, self-contained).
- [ ] T039 [US5] WiX v5 MSI in `installer/msi/`: per-user scope (no elevation), harvest the tray publish output, Start-Menu shortcut, ARP metadata (name OmnisRouter, publisher Fortitude Omnis, version from the tag).
- [ ] T040 [US5] WiX custom action in `installer/msi/` to register the per-user "at log on" Scheduled Task on install and remove it on uninstall (mirrors `LoginTask`), so FR-019 leaves nothing behind.
- [ ] T041 [P] [US5] winget manifest set (version + installer + defaultLocale YAML) in `installer/winget/` referencing the released MSI with its silent-install switch.
- [ ] T042 [US5] Add a `windows` job (`runs-on: windows-latest`, `needs: gate`) to `.github/workflows/release.yml`: publish the tray, build the MSI, `winget validate` the manifest, attach the MSI to the GitHub Release. Leave the Ubuntu `binaries`, container, and npm jobs unchanged.
- [ ] T043 [US5] Add a `winget validate` step to `scripts/release-gate.ps1` (or document that the release job runs it) so a broken manifest blocks the tag.

**Checkpoint**: All five stories functional; the tool installs, distributes, and uninstalls cleanly.

---

## Phase 8: Polish & Cross-Cutting

- [ ] T044 [P] Document the Windows tray install (MSI + `winget install OmnisRouter`), auto-start, and uninstall in `README.md` "Getting started" and a new `docs/collect-tray.md`; note the deferred code-signing/SmartScreen caveat.
- [ ] T045 Run every quickstart.md scenario (1–7) on Windows and record the results in the PR/commit notes.
- [ ] T046 Final gate: `dotnet build OmnisRouter.slnx -c Release` (0 error/0 warning) and `dotnet test OmnisRouter.slnx -c Release` green.

---

## Dependencies & Execution Order

- **Phase 1 (Setup)** → **Phase 2 (Engine, BLOCKING)** → user stories.
- **US1 (P1)** is the tray platform. **US2, US3, US4** each build on the tray app from US1 and are otherwise independent of one another (different files), so they fan out in parallel once US1 lands. **US5** packages the tray: it can start after US1 and finalises after US2–US4 so the MSI harvests the complete app.
- **Polish (Phase 8)** last.

### Within the engine (Phase 2)

T005, T006, T009, T011, T012 are parallel [P]. T007/T008/T010 touch extracted pieces. T013 depends on all of them; T014 depends on T013. Tests T015–T020 run in parallel once T013/T014 land.

## Parallel Opportunities

- Setup: T002, T003, T004 in parallel after T001.
- Engine: T005, T006, T009, T011, T012 together; then T015–T020 together.
- Cross-story: after US1 (Phase 3), run US2 (T026–T030), US3 (T031–T034), US4 (T035–T037) as three parallel workstreams; US5 packaging (T038–T043) alongside them, finalising last.

### Parallel example — after MVP (US1) lands

```text
Workstream A (US2): T026, T027 → T028 → T029 → T030
Workstream B (US3): T031, T032 → T033 → T034
Workstream C (US4): T035 → T036 → T037
Workstream D (US5): T038 → T039 → T040 → T041 → T042 → T043
```

## Implementation Strategy

- **MVP** = Phase 1 + Phase 2 + Phase 3 (US1): a background tray watcher that installs-from-build, auto-starts, and shows liveness, reusing existing config. Stop and validate here.
- **Increment 2** = US2 (secure onboarding) + US3 (status/pause/dashboard), the two P2 experience stories.
- **Increment 3** = US5 (MSI/winget/uninstall) to make it a real install, and US4 (logs) for diagnostics.
- Each increment is independently testable and leaves the previous ones working.

## Notes

- `[P]` = different files, no incomplete dependency.
- Constitution III: coverage is the merge gate, not red-first ceremony — tests sit beside their implementation.
- The engine (Phase 2) is the only place the CLI and tray share behaviour; keep all collection logic there so the two surfaces cannot diverge (FR-021/FR-022).
- Code-signing is deferred (spec Assumptions); until a certificate exists the MSI trips SmartScreen.
