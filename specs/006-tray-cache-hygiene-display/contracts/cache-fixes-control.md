# Contract: cache-fixes control endpoint

`GET /v1/cache-hygiene/fixes` and `PUT /v1/cache-hygiene/fixes` — read and set the byte-mutating fix
classes at runtime, from the tray, with the OmnisVigil policy override still authoritative. Loopback
management surface.

## GET response 200

```json
{
  "local": ["line_ending"],
  "effective": ["line_ending", "trailing_whitespace"],
  "policy_overrides": true
}
```

- `local` — the fixes enabled in local config/runtime.
- `effective` — what actually runs after any OmnisVigil policy override (`IFixPolicy`).
- `policy_overrides` — true when a policy is present and authoritative, so the tray must show `effective`
  as the truth and mark the local toggles as not currently in control (FR-010).

## PUT request

```json
{ "enabled": ["line_ending", "trailing_whitespace"] }
```

- Sets the local enabled set at runtime; takes effect for subsequent requests with no restart or config
  edit (FR-009).
- Allowed values: `line_ending`, `trailing_whitespace`, `tool_ordering`. Unknown values are rejected.
- Response is the resulting `FixState` (same shape as GET), so the tray immediately sees whether a policy
  overrode the change.

## Rules

- **Idempotent.** `PUT` replaces the local set; re-sending the same set is a no-op.
- **Policy precedence unchanged.** The endpoint sets only the local layer; `CacheHygieneService`
  continues to consult `IFixPolicy` first, so a `cache_fixes` policy from OmnisVigil still wins.
- **Safe values only.** The three fix classes are the ones 004 defined; nothing here can enable an
  unproven transform.
- **No effect on measurement.** Toggling fixes changes recovery, not whether waste is measured.
