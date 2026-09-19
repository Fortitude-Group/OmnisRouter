# Quickstart & Validation: Persistent cache-hygiene settings in the tray

Runnable checks against the success criteria. References [contracts/](./contracts/) and
[data-model.md](./data-model.md) rather than repeating them.

## Prerequisites

- Windows, .NET 10 SDK, the tray built from `src/OmnisRouter.Tray` (or an installed MSI), and a router
  the tray supervises. An OmnisVigil workspace for the emit scenario, or the test doubles offline.

## Build & test

```powershell
dotnet build OmnisRouter.slnx -c Debug            # 0 error / 0 warning
dotnet test  OmnisRouter.slnx -c Debug            # settings round-trip + env mapping
```

## Scenario 1 - A fix choice survives a restart (US1, SC-001, SC-002)

Open the router settings window, tick "Line endings", save (the router restarts). Confirm the router
starts with the fix enabled, drive a CRLF-only repeat, and see the cache read on the receipt. Stop and
start the router again and confirm the fix is still enabled with no further action.

**Expected**: the fix is persisted to `router.json` and applied on every start via
`CacheHygiene__EnabledFixes__0=LineEnding`, unlike 006's runtime toggle which would reset.

## Scenario 2 - Turn on reporting to OmnisVigil (US2, SC-003)

With an OmnisVigil workspace connected, tick "Report cache-waste to OmnisVigil", save. Drive routed
traffic that produces an avoidable miss.

**Expected**: the router starts with `OmnisVigil__EmitCacheWaste=true`, the receipts now carry the
content-free cache-waste block, and it appears on the Vigil page. The setting persists across restarts.
With no workspace connected, the toggle explains it only takes effect once one is.

## Scenario 3 - Set the billing model (US3, SC-004)

Set billing to subscription, save.

**Expected**: the router starts with `CacheHygiene__Billing=Subscription` and every figure (006's popup,
the receipt, OmnisVigil) is marked a shadow estimate, not a bill. Persists across restarts.

## Scenario 4 - OmnisVigil policy governs the fixes (SC-005, FR-005)

With a Vigil `cache_fixes` policy active, open the settings window.

**Expected**: the fix checkboxes show the policy's effective set, marked "managed by OmnisVigil", and a
saved local edit does not override the policy while it is active.

## Scenario 5 - Cancel changes nothing

Change a control, then cancel.

**Expected**: nothing is persisted and the router is not restarted.

## Content-free & mapping (unit)

`CacheHygieneEnv.Map` and the `RouterSettings` round-trip are unit tested: the fix names map to
`CacheHygiene__EnabledFixes__N`, billing and emit map to their keys, defaults are correct, and only
configuration values (never prompt content) are produced.

## Validation results (2026-09-20)

Release gate green: `dotnet build OmnisRouter.slnx -c Release` → 0 error / 0 warning; the tray publishes
clean (`-p:PublishProfile=tray-win-x64`); `dotnet test OmnisRouter.slnx -c Release` → 376 passed / 0
failed. The logic is pinned by unit tests; the WinForms settings window is build- and publish-verified:

| Scenario | Covered by |
|---|---|
| 1 — fix choice survives a restart | `RouterSettingsCacheTests` (round-trip), `CacheHygieneEnvTests` (fixes → `CacheHygiene__EnabledFixes__N`) |
| 2 — turn on reporting to OmnisVigil | `CacheHygieneEnvTests.Emit_maps_to_the_vigil_gate_flag` |
| 3 — set the billing model | `CacheHygieneEnvTests.Billing_maps_to_its_key_and_defaults_to_pay_as_you_go` |
| 4 — OmnisVigil policy governs the fixes | governance path in `RouterSettingsWindow` (reads 006's `/v1/cache-hygiene/fixes`); build-verified |
| 5 — cancel changes nothing | `RouterController.ApplyCacheSettingsAsync`/`ChangePortAsync` no-op-when-unchanged; the window persists only on OK |
| mapping is content-free | `CacheHygieneEnvTests.Map_produces_only_configuration_keys` |

The live scenarios that need a running router with a workspace (the Vigil page showing data) are proven
at the mapping and persistence level here; the end-to-end flow is exercised by installing the tray build
and opening Router settings.

## Release gate

`scripts/release-gate.ps1` (build 0/0 + tests) gates the tag; the mapping and round-trip tests run in
the suite.
