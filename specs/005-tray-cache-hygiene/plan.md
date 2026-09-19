# Implementation Plan: Persistent cache-hygiene settings in the tray

**Branch**: `005-tray-cache-hygiene` | **Date**: 2026-09-19 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/005-tray-cache-hygiene/spec.md`

## Summary

Add a cache-hygiene section to the tray's router settings window that persists three choices with the
existing per-user router settings (`router.json`) and feeds them to the supervised router on start: the
enabled fix set, whether to report cache-waste to OmnisVigil, and the billing model. The settings flow
to the router through the same environment-injection channel the tray already uses for `OmnisVigil__*`,
so no new transport is needed. Saving restarts the router the same way changing the port already does.
This complements `006-tray-cache-hygiene-display` (the live popup and runtime toggle): 006 flips the
fixes for the running session, 005 makes the choices durable across restarts and adds the two controls
006 lacks (emit and billing). Feature 006's `/v1/cache-hygiene/fixes` endpoint is reused to show when an
OmnisVigil policy governs the fixes, so the window does not pretend a local edit is in charge.

## Technical Context

**Language/Version**: C# / .NET 10 (`net10.0`; the tray targets `net10.0-windows`)

**Primary Dependencies**: WinForms (`RouterSettingsWindow`), `OmnisRouter.LocalProxy` (`RouterSettings`
persistence, `RouterSupervisor` env injection, `RouterManagementClient`), `OmnisRouter.CacheHygiene`
(`FixClass`, `BillingModel`), `OmnisRouter.Vigil` (`OmnisVigilOptions.EmitCacheWaste`, the emission gate)

**Storage**: the existing per-user `router.json` (`%APPDATA%\OmnisRouter\router.json`), extended with
three additive fields. No new store, no migration beyond additive JSON fields read with a default.

**Testing**: xUnit in `OmnisRouter.LocalProxy.Tests` (settings round-trip, env mapping). The WinForms
window is build-verified.

**Target Platform**: Windows 10/11 (the tray)

**Project Type**: desktop-app (tray) supervising a local web-service (router)

**Performance Goals**: none specific; saving restarts the router, which already happens for a port change.

**Constraints**: content-free (settings are config values, never prompt bytes); governance precedence
unchanged (an OmnisVigil `cache_fixes` policy still wins and is shown as such); no change to the
cache-hygiene engine, its safety proofs, the receipt fields, or the wire contract.

**Scale/Scope**: single-user desktop; three settings plus their env mapping and a settings-window section.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **I. Modular & Composable** — PASS. The settings live in `RouterSettings`; the env mapping is a small
  pure function; the window and the router-start path consume them. No new coupling.
- **II. Contract Stability & SemVer** — PASS. Additive `router.json` fields (a MINOR change), read with a
  default when absent. The tray-to-router config keys are the router's existing `CacheHygiene__*` /
  `OmnisVigil__*` bindings. No wire-contract change.
- **III. Comprehensive Tests** — PASS. Planned: settings round-trip, the env-mapping helper (enabled
  fixes to `CacheHygiene__EnabledFixes__N`, billing, emit gate), and governance-display behaviour.
- **IV. Deterministic & Observable** — PASS. The mapping is deterministic; the applied config is visible
  in the running router and via 006's popup.
- **V. Simplicity & Justified Complexity** — PASS. Reuses the persistence, the supervisor env channel,
  and 006's fixes endpoint. No new mechanism.
- **VI. Complete the Scope** — PASS. All three settings and the governance display are planned.
  Hot-reloading the options without a restart is explicitly deferred and noted, not silently dropped.
- **X. Production Changes Wait for a Human** — N/A. Local desktop only.
- **XI. Establish the Mechanism** — PASS. Built from verified code: `RouterSettings` persistence,
  `RouterSupervisor.StartAsync(port, token, reportEnvironment)` env injection,
  `TrayContext.BuildReportEnvironment`, and that `VigilReceiptPusher` gates emission on
  `OmnisVigilOptions.EmitCacheWaste` (not `CacheHygieneOptions.EmitToVigil`, which is a redundant leftover).
- **XII. Explain Every Number** — N/A here (controls, not figures); the billing choice this persists is
  what makes 006's figures label themselves correctly.

No violations. Complexity Tracking is empty.

## Project Structure

### Documentation (this feature)

```text
specs/005-tray-cache-hygiene/
├── plan.md, research.md, data-model.md, quickstart.md
├── contracts/
│   ├── router-settings.md       # the persisted fields + their config mapping
│   └── settings-window.md       # the settings-window UI contract
└── tasks.md                     # /speckit-tasks output (not created here)
```

### Source Code (repository root)

```text
src/
├── OmnisRouter.LocalProxy/
│   ├── RouterSettings.cs          # + EnabledFixes, EmitCacheWaste, Billing (persisted)
│   └── CacheHygieneEnv.cs         # NEW: pure map RouterSettings -> CacheHygiene__*/OmnisVigil__* env
└── OmnisRouter.Tray/
    ├── RouterSettingsWindow.cs    # + cache-hygiene section (fix checkboxes, emit, billing), governance note
    └── TrayContext.cs             # OnRouterSettings persists the settings; report env includes the map

tests/
└── OmnisRouter.LocalProxy.Tests/
    ├── RouterSettingsCacheTests.cs   # round-trip + defaults
    └── CacheHygieneEnvTests.cs       # env mapping (fixes/billing/emit)
```

**Structure Decision**: Extend the existing tray + LocalProxy. The one new file is a pure
`CacheHygieneEnv` mapper (testable without WinForms), so the window and the router-start path share one
mapping and the tests do not need the GUI.

## Complexity Tracking

No constitution violations. This section is intentionally empty.
