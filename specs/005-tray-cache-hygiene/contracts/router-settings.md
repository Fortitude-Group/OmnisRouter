# Contract: router.json cache-hygiene fields and their config mapping

The persisted settings and how they reach the supervised router. Additive to schema 1; no migration.

## router.json (additive fields)

```jsonc
{
  "schemaVersion": 1,
  "port": 8787,
  "enabledFixes": ["LineEnding"],       // C# FixClass names; empty by default
  "emitCacheWaste": false,               // report cache-waste to OmnisVigil; off by default
  "billing": "PayAsYouGo"                // or "Subscription"
}
```

- Absent fields read as their defaults (`[]`, `false`, `PayAsYouGo`), so an old file upgrades silently.
- `enabledFixes` values are validated against the three known classes; unknown values are dropped on load
  rather than failing the whole file.

## Environment injected at router start (`CacheHygieneEnv.Map`)

| From | Key | Value |
|---|---|---|
| `enabledFixes` | `CacheHygiene__EnabledFixes__0`, `__1`, … | one per fix, C# enum name; none when empty |
| `billing` | `CacheHygiene__Billing` | `PayAsYouGo` \| `Subscription` |
| `emitCacheWaste` | `OmnisVigil__EmitCacheWaste` | `true` \| `false` |

Rules:
- The map is a pure function of `RouterSettings`, deterministic and unit-tested.
- It is merged with the existing `OmnisVigil__*` report environment; on a key clash the cache map does not
  overwrite the connection keys (`Enabled`/`Endpoint`/`ProjectKey`), it only adds `EmitCacheWaste`.
- Emitting `OmnisVigil__EmitCacheWaste=true` only has an effect once an OmnisVigil workspace is connected
  (the pusher runs only when the integration is configured), which the window states.
