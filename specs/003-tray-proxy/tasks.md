---
description: "Task list for the tray-managed local router proxy"
---

# Tasks: Tray-managed local router proxy

**Input**: Design documents from `specs/003-tray-proxy/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md

**Tests**: INCLUDED. The constitution (Principle III) requires comprehensive tests for public
contracts at merge, and the client-link transforms plus the supervision/keys paths are exactly such
contracts. Test-first ordering is encouraged but optional; coverage at merge is mandatory.

**Testability note**: the WinForms `OmnisRouter.Tray` is Windows-only and is NOT in `OmnisRouter.slnx`
(CI builds the solution on Linux). All logic that needs CI tests therefore lives in cross-platform
libraries that ARE in the solution: `OmnisRouter.ClientLink` (client wiring/revert) and
`OmnisRouter.LocalProxy` (supervision, management client, settings). The tray is a thin WinForms
consumer of both. Windows DPAPI stays behind an `ISecretProtector` seam so the core stays testable.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: can run in parallel (different files, no dependency on an incomplete task)
- **[Story]**: US1..US5, on user-story tasks only

---

## Phase 1: Setup (Shared Infrastructure)

- [x] T001 Create `src/OmnisRouter.LocalProxy/OmnisRouter.LocalProxy.csproj` (net10.0, nullable, no WinForms) and add it to `OmnisRouter.slnx`.
- [x] T002 [P] Create `src/OmnisRouter.ClientLink/OmnisRouter.ClientLink.csproj` (net10.0, nullable) and add it to `OmnisRouter.slnx`.
- [x] T003 [P] Create xUnit test projects `tests/OmnisRouter.LocalProxy.Tests/` and `tests/OmnisRouter.ClientLink.Tests/` and add both to `OmnisRouter.slnx`.
- [ ] T004 Add project references: `OmnisRouter.Tray` → `OmnisRouter.LocalProxy` and `OmnisRouter.ClientLink`; each test project → its library (edit the four `.csproj` files).

---

## Phase 2: Foundational (Blocking Prerequisites)

**⚠️ No user story work begins until this phase is complete.**

- [x] T005 [P] Define `RouterProcessState` enum (Off, Starting, Running, Error) and a `RouterStatus` record in `src/OmnisRouter.LocalProxy/RouterProcessState.cs`.
- [x] T006 [P] Define `ISecretProtector` (Protect/Unprotect) in `src/OmnisRouter.LocalProxy/ISecretProtector.cs`, and implement the Windows DPAPI version `DpapiSecretProtector` (reusing the existing `ProtectedSecret`) in `src/OmnisRouter.Tray/DpapiSecretProtector.cs`.
- [x] T007 Implement `RouterSettings` (POCO + `System.Text.Json` load/save of `%APPDATA%\OmnisRouter\router.json`: schemaVersion, enabled, port with 1024-65535 validation defaulting to 8787, DPAPI-protected token via `ISecretProtector`, connectedClients) in `src/OmnisRouter.LocalProxy/RouterSettings.cs`.
- [x] T008 [P] Implement `RouterPaths` (resolve the bundled `omnisrouter.exe` beside the tray, and the router working dir `%APPDATA%\OmnisRouter\router\`) in `src/OmnisRouter.LocalProxy/RouterPaths.cs`.
- [x] T009 [P] Implement `RouterToken` (generate a high-entropy token once, persisted through `RouterSettings`) in `src/OmnisRouter.LocalProxy/RouterToken.cs`.
- [x] T010 Implement `RouterManagementClient` (`GET /health`, `GET /readyz`, `POST/GET/DELETE /v1/keys` with `Authorization: Bearer`, typed requests/responses per `contracts/router-management.md`) in `src/OmnisRouter.LocalProxy/RouterManagementClient.cs`.
- [x] T011 Implement `RouterSupervisor` (launch hidden `omnisrouter.exe` with `--urls http://127.0.0.1:<port>`, working dir, `Omnis__BootstrapToken` env; bounded `/readyz` readiness poll; stop; crash-restart with backoff; port-in-use → Error; emit `RouterStatus` changes) in `src/OmnisRouter.LocalProxy/RouterSupervisor.cs`.
- [x] T012 Extend the tray's rolling-file logging to record router lifecycle events, in `src/OmnisRouter.Tray/TrayContext.cs`.

**Checkpoint**: the testable proxy core exists; user stories can proceed.

---

## Phase 3: User Story 1 - Run the router from the tray, no console (Priority: P1) 🎯 MVP

**Goal**: a background router controlled by one tray toggle, no console, auto-starting from login, with clear state.

**Independent Test**: enable the toggle on a clean account, confirm the router serves on loopback with no console, `/readyz` passes, and it returns after a re-login.

### Tests for User Story 1

- [x] T013 [P] [US1] Supervision tests against a stub loopback server (Starting→Running only after `/readyz`, crash→auto-restart, repeated failure→Error, port-in-use→Error) in `tests/OmnisRouter.LocalProxy.Tests/RouterSupervisorTests.cs`.
- [x] T014 [P] [US1] `RouterSettings` round-trip tests (defaults, port validation, protected-token via a fake `ISecretProtector`, enabled persistence) in `tests/OmnisRouter.LocalProxy.Tests/RouterSettingsTests.cs`.

### Implementation for User Story 1

- [x] T015 [US1] Add the "Local router proxy" context-menu toggle wired to start/stop the supervisor and persist `enabled`, in `src/OmnisRouter.Tray/TrayContext.cs`.
- [x] T016 [US1] Restore the proxy to its persisted `enabled` state on tray launch, and confirm the existing login Scheduled Task carries it, in `src/OmnisRouter.Tray/TrayContext.cs` and `src/OmnisRouter.Tray/LoginTask.cs`.
- [x] T017 [P] [US1] Add a routing-live tray icon treatment distinct from the collector states, in `src/OmnisRouter.Tray/TrayIcons.cs`.
- [x] T018 [P] [US1] Add the plain-language mode readout (routing vs collecting vs both) to the status popup, in `src/OmnisRouter.Tray/StatusPopup.cs` and `src/OmnisRouter.Tray/StatusFormat.cs`.
- [x] T019 [US1] Add the first-enable confirmation explaining routed traffic is BYOK per-token billing, gating the first start, in `src/OmnisRouter.Tray/TrayContext.cs`.
- [x] T020 [US1] Surface error states (port conflict, readiness timeout, repeated crash) into the icon and popup, in `src/OmnisRouter.Tray/TrayContext.cs`.

**Checkpoint**: US1 is independently functional (MVP with US2).

---

## Phase 4: User Story 2 - Add provider keys in a settings window (Priority: P1)

**Goal**: manage BYOK keys for the four providers through the router's encrypted store.

**Independent Test**: with the proxy running, add a key, see it as set, restart, confirm still set, remove it.

### Tests for User Story 2

- [x] T021 [P] [US2] `RouterManagementClient` keys tests against a stub (`POST/GET/DELETE /v1/keys` shapes, bearer header, non-2xx surfaced, key value never returned) in `tests/OmnisRouter.LocalProxy.Tests/RouterManagementClientTests.cs`.

### Implementation for User Story 2

- [x] T022 [US2] Build the `KeysWindow` dialog listing Anthropic, OpenAI, Gemini, OpenRouter with set/unset status, add and remove, never displaying a stored value, in `src/OmnisRouter.Tray/KeysWindow.cs`.
- [x] T023 [US2] Wire add/remove to `RouterManagementClient` using the router token, in `src/OmnisRouter.Tray/KeysWindow.cs`.
- [x] T024 [US2] On first enable with no keys, open `KeysWindow` and warn while none is set (readiness-gated), in `src/OmnisRouter.Tray/TrayContext.cs`.
- [x] T025 [US2] Add the "Provider keys…" menu item under the proxy toggle, in `src/OmnisRouter.Tray/TrayContext.cs`.

**Checkpoint**: US1 + US2 = MVP (a running, key-holding router from the tray).

---

## Phase 5: User Story 3 - Connect a coding tool with one click, and revert (Priority: P2)

**Goal**: wire Claude Code, Codex, Cursor at the local router and revert exactly.

**Independent Test**: connect Claude Code, confirm its config points at the router, route a request, revert, confirm byte-for-byte restore.

### Tests for User Story 3

- [x] T026 [P] [US3] Golden connect+revert for Claude Code (env merge, prior-state capture, remove-if-absent restore, malformed-JSON refusal) in `tests/OmnisRouter.ClientLink.Tests/ClaudeCodeLinkTests.cs`.
- [x] T027 [P] [US3] Golden connect+revert for Codex (managed block insert/replace/remove, env var set/unset, prior block preserved) in `tests/OmnisRouter.ClientLink.Tests/CodexLinkTests.cs`.
- [x] T028 [P] [US3] Cursor show-only output test and idempotent reconnect test in `tests/OmnisRouter.ClientLink.Tests/CursorLinkTests.cs`.

### Implementation for User Story 3

- [x] T029 [P] [US3] Define `IClientLink`, `ClientLinkResult`, and `ClientPriorState` in `src/OmnisRouter.ClientLink/IClientLink.cs`.
- [x] T030 [P] [US3] Implement `ClaudeCodeLink` (`~/.claude/settings.json` env merge, prior-state capture, revert) in `src/OmnisRouter.ClientLink/ClaudeCodeLink.cs`. NOTE: connect/revert are pure string transforms; the timestamped backup + disk write are the executor's job (T033).
- [x] T031 [P] [US3] Implement `CodexLink` (`~/.codex/config.toml` managed block plus `OMNISROUTER_API_KEY` env var, revert) in `src/OmnisRouter.ClientLink/CodexLink.cs`. NOTE: connect/revert are pure; the backup, real env-var set/unset + prior env capture are the executor's job (T033).
- [x] T032 [P] [US3] Implement `CursorLink` (show-only `OPENAI_BASE_URL`/`OPENAI_API_KEY` values) in `src/OmnisRouter.ClientLink/CursorLink.cs`.
- [ ] T033 [US3] Implement client detection and persist `connectedClients` through `RouterSettings`, in `src/OmnisRouter.ClientLink/ClientDetection.cs` and wired in the tray.
- [ ] T034 [US3] Build the `ConnectWindow` dialog (per-client Connect/Revert, Cursor values with copy), in `src/OmnisRouter.Tray/ConnectWindow.cs`.
- [ ] T035 [US3] Add the "Connect an app…" menu item under the proxy toggle, in `src/OmnisRouter.Tray/TrayContext.cs`.

**Checkpoint**: US1-US3 all independently functional.

---

## Phase 6: User Story 4 - Routed spend in OmnisVigil, no double counting (Priority: P2)

**Goal**: routed traffic and savings show on the dashboard once, from the router's receipts.

**Independent Test**: with both toggles on and a project key, connect Claude Code, route traffic, confirm the dashboard shows it once (router source) with savings, and the collector does not also report it.

### Tests for User Story 4

- [ ] T036 [P] [US4] Collector dedupe test: a connected client is excluded from collect scope and returns on disconnect, in `tests/OmnisRouter.LocalProxy.Tests/CollectDedupeTests.cs` (drives `OmnisRouter.Collect`).

### Implementation for User Story 4

- [ ] T037 [US4] Launch the supervised router with `OmnisVigil__Enabled=true`, `OmnisVigil__Endpoint`, `OmnisVigil__ProjectKey` taken from the collector's stored config, so `VigilReceiptPusher` reports routed receipts, in `src/OmnisRouter.LocalProxy/RouterSupervisor.cs`.
- [ ] T038 [US4] Add a connected-client exclusion set to `CollectOptions` and honour it while tailing, in `src/OmnisRouter.Collect/CollectOptions.cs` and `src/OmnisRouter.Collect/CollectEngine.cs`.
- [ ] T039 [US4] Feed the tray's `connectedClients` into the collect engine's exclusion set, in `src/OmnisRouter.Tray/TrayContext.cs`.
- [ ] T040 [US4] Prompt for a project key (reusing the collector's `SetupWindow` capture) when proxy reporting is on and none exists, in `src/OmnisRouter.Tray/TrayContext.cs`.

**Checkpoint**: routed spend appears once on the dashboard.

---

## Phase 7: User Story 5 - Choose the local port (Priority: P3)

**Goal**: a validated port setting that restarts the router and re-points connected clients.

**Independent Test**: change the port, confirm the router restarts on it and connected clients follow.

### Tests for User Story 5

- [ ] T041 [P] [US5] Port validation and change-triggers-restart tests (settings + supervisor) in `tests/OmnisRouter.LocalProxy.Tests/PortSettingTests.cs`.

### Implementation for User Story 5

- [ ] T042 [US5] Build the settings window with a validated port field (default 8787), in `src/OmnisRouter.Tray/RouterSettingsWindow.cs`.
- [ ] T043 [US5] On port change, restart the supervisor on the new port and re-point connected clients (re-run the client links with the new root), in `src/OmnisRouter.Tray/TrayContext.cs`.
- [ ] T044 [US5] On disabling the proxy with clients connected, warn and offer to revert them, in `src/OmnisRouter.Tray/TrayContext.cs`.

**Checkpoint**: all five stories independently functional.

---

## Phase 8: Polish & Cross-Cutting Concerns

- [ ] T045 [P] Extend the `windows` release job to also `dotnet publish src/OmnisRouter.Api -r win-x64` self-contained and stage `omnisrouter.exe` plus its `config/`, `routing/`, `models/` into the tray publish dir before `wix build`, in `.github/workflows/release.yml`.
- [ ] T046 Confirm the per-user MSI packages the staged server payload with no `.wxs` change (the existing `**` glob), verifying against `installer/msi/OmnisRouter.wxs`.
- [ ] T047 [P] SemVer bump and CHANGELOG entry for the tray-proxy feature, in `CHANGELOG.md` and the tray `Version` in `src/OmnisRouter.Tray/OmnisRouter.Tray.csproj`.
- [ ] T048 [P] Document the tray proxy, provider keys, connect-an-app and port setting, in `README.md` and `docs/`.
- [ ] T049 Run the `quickstart.md` manual end-to-end on Windows and record the result.
- [ ] T050 Confirm full `dotnet test` is green and the tray builds clean (no warnings-as-errors) before any release.

---

## Dependencies & Execution Order

### Phase dependencies

- Setup (Phase 1) → no dependencies.
- Foundational (Phase 2) → depends on Setup; BLOCKS all user stories.
- US1 (Phase 3) and US2 (Phase 4) → depend on Foundational; together they are the MVP. US2's keys
  window needs the running router from US1's supervisor, so in a single-developer flow do US1 then US2.
- US3 (Phase 5) → depends on Foundational; the client links (T029-T032) are independent of US1/US2 and
  can be built in parallel, but the end-to-end connect needs a running router.
- US4 (Phase 6) → depends on US3 (needs connected clients to dedupe) and on a project key.
- US5 (Phase 7) → depends on US1 (supervisor) and US3 (re-pointing clients).
- Polish (Phase 8) → after the desired stories; T045/T046 gate the first installable release.

### Parallel opportunities

- Setup: T002, T003 in parallel with T001's follow-on.
- Foundational: T005, T006, T008, T009 in parallel; T007/T010/T011 follow the types they use.
- The `OmnisRouter.ClientLink` layer (T029-T032) is fully parallel and independent of the tray, so US3's
  library and tests can proceed while US1/US2 UI work happens.
- All `[P]` test tasks within a story run in parallel.

## Parallel Example: User Story 3 (client links)

```text
Task: "Implement ClaudeCodeLink in src/OmnisRouter.ClientLink/ClaudeCodeLink.cs"
Task: "Implement CodexLink in src/OmnisRouter.ClientLink/CodexLink.cs"
Task: "Implement CursorLink in src/OmnisRouter.ClientLink/CursorLink.cs"
Task: "Golden test ClaudeCodeLink in tests/OmnisRouter.ClientLink.Tests/ClaudeCodeLinkTests.cs"
Task: "Golden test CodexLink in tests/OmnisRouter.ClientLink.Tests/CodexLinkTests.cs"
```

## Implementation Strategy

- **MVP** = Phase 1 + Phase 2 + US1 + US2: a running, key-holding local router driven entirely from
  the tray, no docker, no console. Stop and validate here.
- **Increment 2** = US3: one-click connect and revert.
- **Increment 3** = US4: routed spend on the dashboard with dedupe.
- **Increment 4** = US5: the port setting.
- **Release** = Phase 8: bundle the server in the MSI and ship. T045/T046 are required before the
  first installable build.

## Notes

- `[P]` = different files, no incomplete dependency. `[Story]` maps a task to its user story.
- Keep the supervision, management and settings logic in `OmnisRouter.LocalProxy`, and the wiring in
  `OmnisRouter.ClientLink`, so both are covered by CI tests; the WinForms tray stays a thin consumer.
- Commit after each task or logical group. Reference the feature slug in commit messages.
