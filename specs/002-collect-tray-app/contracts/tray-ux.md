# Contract: Tray UX

The observable UI contract for `OmnisRouter.Tray`. The tray holds no collection logic; every value below is read from `CollectionStatus`.

## Icon states

| `State` | Icon | Meaning |
|---|---|---|
| `Watching` | green dot | running and last post healthy |
| `Backfilling` | green dot (busy hint) | catching up on history |
| `Idle` | green dot | configured, nothing to post yet |
| `Paused` | grey dot | collection paused by the user |
| `Error` | amber/red dot | last post failed / endpoint unreachable |

## Tooltip

Single line, from `Status`:

```
OmnisRouter · last posted 14:01 · 49,075 today
```

- Paused: `OmnisRouter · paused`.
- Error: `OmnisRouter · can't reach OmnisVigil · retrying`.
- Before first post: `OmnisRouter · watching · 0 today`.

## Right-click menu

| Item | Action | Enabled when |
|---|---|---|
| Pause / Resume | `engine.Pause()` / `engine.Resume()`; persists `paused` | always (label reflects state) |
| Open dashboard | open `Endpoint` in the browser | always |
| Open logs | open the active log file | log exists |
| Clear logs | delete the log set | log exists |
| Settings | open the setup/settings window | always |
| Quit | stop the engine and exit the process | always |

## Left-click popup (liveness)

A small borderless panel anchored to the tray icon, dismissed on focus loss. **Ships showing liveness only**, bound to `CollectionStatus`:

- State line (e.g. "Watching", "Paused", "Backfilling 12,340 / 49,075", "Error: <message>").
- `Last posted: 14:01:35` (or "not yet").
- `Today: 49,075 receipts`.
- Last error with its time, if any.
- A "Full dashboard →" link (same target as Open dashboard).

**Growth path (designed, not built now)**: because the popup binds to the status snapshot, later additions (top models today, token totals, a sparkline) are new bound fields, not new plumbing. Any figure added must still pass Principle XII — it earns its place or it stays in the dashboard.

## Setup / Settings window

First run (no valid config) opens this automatically (FR-010):

- Endpoint field, pre-filled `https://app.omnisvigil.com`.
- Project key field (masked).
- "Open Connect page →" button → opens `<endpoint>`'s Connect page in the browser.
- Save: validates, DPAPI-protects the key, writes `collect.json`, starts collection, closes.
- A "Start at login" toggle (registers/removes the Scheduled Task).

Invalid/empty key on Save → inline error, no save (FR-013).
