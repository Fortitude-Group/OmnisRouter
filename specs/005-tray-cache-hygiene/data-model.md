# Data Model: Persistent cache-hygiene settings in the tray

Additive fields on the existing persisted settings, plus their config mapping. No new store.

## RouterSettings (extended; persisted to router.json)

| Field | Type | Default | Notes |
|---|---|---|---|
| EnabledFixes | list of string | empty | Fix classes the user has turned on as saved defaults, by C# enum name: `LineEnding`, `TrailingWhitespace`, `ToolOrdering`. |
| EmitCacheWaste | bool | false | Whether the router reports the content-free cache-waste block to OmnisVigil. Off by default (research D3). |
| Billing | string | `PayAsYouGo` | Billing model: `PayAsYouGo` or `Subscription`. Drives whether figures are shown as shadow estimates. |

All three are read with their default when absent, so an existing `router.json` (schema 1) upgrades
silently. Persisted with the existing camelCase, indented JSON serializer.

## Config mapping (RouterSettings -> child-process environment)

Produced by `CacheHygieneEnv.Map(settings)` and merged into the report environment at router start:

| Setting | Environment key(s) | Value |
|---|---|---|
| EnabledFixes | `CacheHygiene__EnabledFixes__0`, `__1`, ... | one entry per enabled fix, the C# enum name |
| Billing | `CacheHygiene__Billing` | `PayAsYouGo` or `Subscription` |
| EmitCacheWaste | `OmnisVigil__EmitCacheWaste` | `true` or `false` (the flag the pusher actually reads) |

Notes:
- When `EnabledFixes` is empty, no `CacheHygiene__EnabledFixes__*` keys are emitted, so the router keeps
  its default (all fixes off).
- The keys use the router's existing configuration binding; no router-side code change is needed.

## Governance state (read-only, from the running router)

Fetched from `GET /v1/cache-hygiene/fixes` (feature 006) when the window opens:

| Field | Type | Use |
|---|---|---|
| effective | string[] | the fixes actually running (wire names) after any policy override |
| policy_overrides | bool | when true, the window shows the fix controls as managed by OmnisVigil |

Not persisted; purely to render the window honestly.
