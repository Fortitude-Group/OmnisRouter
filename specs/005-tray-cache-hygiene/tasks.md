# Tasks: Persistent cache-hygiene settings in the tray

**Feature**: `005-tray-cache-hygiene` | **Spec**: [spec.md](./spec.md) | **Plan**: [plan.md](./plan.md)

**Organization**: By user story. US1 (persist + apply the fix set) carries the shared plumbing
(settings fields + env mapping + settings-window section + restart-on-save). US2 (emit toggle) and US3
(billing) are small additions to the same surfaces. Tests per constitution Principle III.

**Parallel fan-out**: `[P]` marks tasks that can run concurrently (different files, no incomplete
dependency). The settings-window edits and the `RouterSettings`/`CacheHygieneEnv` edits are the shared
files, so their tasks serialise; the tests fan out once their targets exist.

---

## Phase 1: Setup

- [X] T001 Confirm the baseline builds green: `dotnet build OmnisRouter.slnx -c Debug` (0/0). This feature extends `OmnisRouter.LocalProxy` and `OmnisRouter.Tray` only.

## Phase 2: Foundational (blocking prerequisites)

- [X] T002 Add the persisted fields to `src/OmnisRouter.LocalProxy/RouterSettings.cs`: `EnabledFixes` (List<string>, default empty), `EmitCacheWaste` (bool, default false), `Billing` (string, default "PayAsYouGo"), read with defaults when absent. Per data-model.md.
- [X] T003 Add `src/OmnisRouter.LocalProxy/CacheHygieneEnv.cs`: a pure `Map(RouterSettings) -> IReadOnlyDictionary<string,string>` producing `CacheHygiene__EnabledFixes__N` (C# enum names), `CacheHygiene__Billing`, and `OmnisVigil__EmitCacheWaste`, per contracts/router-settings.md. Validates fix names against the three known classes, drops unknowns. Depends on T002.

**Checkpoint**: settings persist and map to config env; nothing consumes them yet.

---

## Phase 3: User Story 1 — Make a fix choice stick across restarts (Priority: P1) 🎯 MVP

**Goal**: A fix ticked in the settings window persists and is applied to the router on every start.

**Independent Test**: Enable a fix in the window, save, restart the router, confirm it starts with the fix without further action.

- [X] T004 [US1] Merge the cache env into the router-start path: in `src/OmnisRouter.Tray/TrayContext.cs` (`BuildReportEnvironment`) or `src/OmnisRouter.Tray/RouterController.cs` (before `StartAsync`), add `CacheHygieneEnv.Map(settings)` to the report environment, without overwriting the `OmnisVigil__*` connection keys. Depends on T003.
- [X] T005 [US1] Add the fix-class section to `src/OmnisRouter.Tray/RouterSettingsWindow.cs`: three checkboxes (Line endings, Trailing spaces, Tool order) bound to the persisted `EnabledFixes`, off by default, plus the "saving restarts the router" note. Expose the chosen set on save.
- [X] T006 [US1] Wire `OnRouterSettings` in `src/OmnisRouter.Tray/TrayContext.cs`: pass the current settings to the window, on save persist `EnabledFixes` to `router.json` and restart the router if it (or the port) changed; cancel persists nothing. Depends on T005.
- [X] T007 [P] [US1] Settings round-trip test in `tests/OmnisRouter.LocalProxy.Tests/RouterSettingsCacheTests.cs`: the three fields save and load, defaults when absent, unknown fix names dropped on load. Depends on T002.
- [X] T008 [P] [US1] Env-mapping test in `tests/OmnisRouter.LocalProxy.Tests/CacheHygieneEnvTests.cs`: enabled fixes map to `CacheHygiene__EnabledFixes__N` with enum names; empty set emits no fix keys; content-free (only config keys). Depends on T003.

**Checkpoint**: MVP — a persisted fix set is applied on every router start.

---

## Phase 4: User Story 2 — Report the waste to a dashboard (Priority: P2)

**Goal**: A tray toggle turns on cache-waste reporting to OmnisVigil, persisted across restarts.

**Independent Test**: Tick the emit toggle, save, confirm the router starts with `OmnisVigil__EmitCacheWaste=true` and reports the block.

- [X] T009 [US2] Add the "Report cache-waste to OmnisVigil" checkbox to `RouterSettingsWindow.cs`, bound to `EmitCacheWaste`, off by default, with the note that it only takes effect once a workspace is connected. Persist + restart on save via `OnRouterSettings`. Depends on T005, T006.
- [X] T010 [P] [US2] Extend `CacheHygieneEnvTests.cs`: `EmitCacheWaste` true/false maps to `OmnisVigil__EmitCacheWaste`, and the map never overwrites the `OmnisVigil__Enabled/Endpoint/ProjectKey` connection keys. Depends on T003.

**Checkpoint**: reporting can be switched on from the tray and survives restarts.

---

## Phase 5: User Story 3 — Set the billing model (Priority: P3)

**Goal**: A tray control sets the billing model, persisted, so figures are labelled correctly.

**Independent Test**: Set subscription, save, confirm the router starts with `CacheHygiene__Billing=Subscription`.

- [X] T011 [US3] Add the billing selector (pay-as-you-go / subscription) to `RouterSettingsWindow.cs`, bound to `Billing`, default pay-as-you-go, with the shadow-estimate note. Persist + restart on save. Depends on T005, T006.
- [X] T012 [P] [US3] Extend `CacheHygieneEnvTests.cs`: `Billing` maps to `CacheHygiene__Billing` with the enum name, default pay-as-you-go. Depends on T003.

**Checkpoint**: billing model is set from the tray and persists.

---

## Phase 6: Governance display & Polish

- [X] T013 [US1] Governance display in `RouterSettingsWindow.cs` / `TrayContext.cs`: when the router is running, read `GET /v1/cache-hygiene/fixes` (feature 006) on open; if `policy_overrides` is true, show the fix checkboxes as the policy's `effective` set marked "managed by OmnisVigil" and not locally authoritative. Fail-open: if the router is down or the call fails, edit the persisted set as plain defaults. Depends on T005.
- [X] T014 [P] Document the settings-window controls in `docs/collect-tray.md` and the persisted fields + config mapping in `docs/self-host.md`.
- [X] T015 Run the quickstart.md scenarios and record the results.
- [X] T016 Final gate: `dotnet build OmnisRouter.slnx -c Release` (0 error / 0 warning) and `dotnet test OmnisRouter.slnx -c Release` green.

---

## Dependencies

- **Phase 1 → Phase 2 → user stories.** T002 (settings) and T003 (env map) block everything.
- **US1 chain**: T002 → T003 → T004; T005 → T006. Tests T007/T008 `[P]`.
- **US2**: T009 depends on the window/save (T005/T006); T010 `[P]`.
- **US3**: T011 depends on T005/T006; T012 `[P]`.
- **Shared files**: `RouterSettingsWindow.cs` (T005, T009, T011, T013) and `TrayContext.cs`/`RouterController.cs` (T004, T006, T013) are edited by several tasks, so those serialise with per-task review; the LocalProxy tests fan out.

## Implementation strategy

- **MVP = Phase 1 + Phase 2 + US1**: persist and apply the fix set. Shippable on its own.
- Then US2 (emit) and US3 (billing) as small additions to the same window, and the governance display.
- The env mapping (`CacheHygieneEnv`) is the one piece with real logic; it is unit-tested independently of the WinForms window.
