# Research: Persistent cache-hygiene settings in the tray

Phase 0 decisions, grounded in the current code.

## D1: Where the settings live — the existing router.json

**Decision**: Add `EnabledFixes` (list), `EmitCacheWaste` (bool), `Billing` (string) to `RouterSettings`,
persisted to `%APPDATA%\OmnisRouter\router.json` alongside `Port`, using its existing camelCase JSON.

**Rationale**: `RouterSettings` already owns per-user router config with `Load`/`Save`, and the settings
window already edits it (the port). Additive fields read with a default when absent, so an old
`router.json` upgrades silently. No new store.

**Alternatives**: a separate cache-settings file (rejected: needless second file; the spec says keep it
with the other router settings).

## D2: How the settings reach the router — the existing env-injection channel

**Decision**: Map the settings to the router's configuration environment and inject them when the tray
starts the router, through the same path that already carries `OmnisVigil__*`
(`RouterSupervisor.StartAsync(port, token, reportEnvironment)`, applied at process start). A pure
`CacheHygieneEnv.Map(settings)` builds the key/value pairs; `RouterController` merges them with the
tray's `OmnisVigil__*` report environment before starting.

**Rationale**: verified in code: `TrayContext.BuildReportEnvironment` already returns `OmnisVigil__*`
and `RouterSupervisor` binds every entry into the child process env. Reusing it means no new transport,
and the router's normal configuration binding reads the values with no router-side change.

**Alternatives**: a management endpoint to push settings at runtime (rejected here: that is 006's runtime
toggle; this feature is the durable, applied-on-start path the spec asked for).

## D3: The exact config keys (the mapping)

**Decision**:
- Enabled fixes -> `CacheHygiene__EnabledFixes__0`, `__1`, ... with the **C# enum names**
  (`LineEnding`, `TrailingWhitespace`, `ToolOrdering`), because `CacheHygieneOptions.EnabledFixes` is a
  `HashSet<FixClass>` and the configuration binder parses enum members by name. (The wire names
  `line_ending` etc. are for the JSON contracts, not config binding.)
- Billing -> `CacheHygiene__Billing` = `PayAsYouGo` | `Subscription` (the `BillingModel` enum names).
- Report to OmnisVigil -> `OmnisVigil__EmitCacheWaste` = `true` | `false`.

**Rationale**: `VigilReceiptPusher` gates the cache-waste block on `OmnisVigilOptions.EmitCacheWaste`
(confirmed at `VigilReceiptPusher.cs`), so that is the operative flag, bound from the `OmnisVigil`
section. `CacheHygieneOptions.EmitToVigil` exists but nothing reads it for emission; it is a redundant
leftover, so the tray toggle drives `OmnisVigil__EmitCacheWaste`, and this feature does not rely on the
dead flag. (Removing `EmitToVigil` is noted as optional cleanup, out of scope.)

**Alternatives**: writing an `appsettings.json` file instead of env (rejected: the env channel already
exists and is per-start, matching the restart-on-save model).

## D4: Showing when OmnisVigil governs the fixes

**Decision**: When the settings window opens and the router is running, query
`GET /v1/cache-hygiene/fixes` (shipped in 006). If `policy_overrides` is true, show the fix checkboxes as
managed by OmnisVigil (reflecting `effective`) and not editable-as-authoritative, matching how 006's
popup already presents it. With no policy, the checkboxes edit the local persisted set.

**Rationale**: reuses 006's endpoint and its precedence, so the two surfaces agree and the window never
claims a local edit overrides an active policy (FR-005). If the router is not running, the window edits
the persisted set as plain defaults.

**Alternatives**: re-deriving policy state in the tray (rejected: 006 already computes and exposes it).

## D5: Applying a change — restart on save

**Decision**: Saving a changed cache setting restarts the router the same way a port change already does,
and the window warns that saving restarts the router.

**Rationale**: the settings are applied to the child process env at start, so they take effect on the
next start; the port already uses this restart-on-save behaviour, so this is consistent and needs no new
lifecycle. 006's runtime toggle remains the no-restart path for the fixes; this is the durable one.

**Alternatives**: hot-reload without restart (deferred, noted in the spec's out-of-scope).
