# Contract: router settings window cache-hygiene section

The UI contract for the addition to `RouterSettingsWindow`. Behaviour, not layout.

## Controls

Below the existing port field, a "Cache hygiene" section:

- **Three fix checkboxes** — Line endings, Trailing spaces, Tool order. Reflect the persisted
  `enabledFixes`. Off by default.
- **A "Report cache-waste to OmnisVigil" checkbox** — reflects `emitCacheWaste`, off by default, with a
  note that it only takes effect once an OmnisVigil workspace is connected.
- **A billing selector** — pay-as-you-go (default) or subscription, with a note that subscription shows
  the figures as shadow estimates, not a bill.
- **A note that saving restarts the router.**

## Behaviour

- **Load**: on open, the controls show the persisted settings. If the router is running, the window also
  reads `GET /v1/cache-hygiene/fixes`; when `policy_overrides` is true it shows the fix checkboxes as the
  policy's `effective` set, marked "managed by OmnisVigil", and does not present them as locally
  authoritative.
- **Save**: persists the chosen fix set, emit flag and billing model to `router.json`, then, if any of
  them (or the port) changed, restarts the router so the new values apply on start. Cancel persists
  nothing and does not restart.
- **Governed fixes**: while a policy governs the fixes, saving does not claim to override it; a saved
  local set applies again only if the policy is later withdrawn.
- **Content-free**: the window shows and stores configuration values only, never prompt content.

## Relationship to feature 006

This is the durable-configuration surface. The live figures and the no-restart runtime fix toggle are in
006's popup. The two do not duplicate: a fix turned on here survives a restart; a fix flipped in 006's
popup is for the running session. Both honour the same OmnisVigil policy precedence.
