# Phase 1 Data Model: Collect watcher Windows surface

These are the in-process and on-disk entities the feature introduces. There is no database. Types live in `OmnisRouter.Collect` unless noted.

## CollectionStatus (in-memory, observable)

The single snapshot the tray binds to and the console renderer can log. Immutable record; the engine publishes a new instance on every change.

| Field | Type | Meaning |
|---|---|---|
| `State` | `CollectState` enum | `Idle`, `Backfilling`, `Watching`, `Paused`, `Error` |
| `LastPostUtc` | `DateTimeOffset?` | When the last receipt batch was accepted; null before the first |
| `TodayReceipts` | `int` | Receipts posted since local midnight |
| `TodayTokens` | `long` | Tokens priced since local midnight |
| `SessionReceipts` | `int` | Receipts posted this process lifetime (parity with the old "session total") |
| `LastError` | `string?` | Message of the most recent failed tick; cleared on the next success |
| `LastErrorUtc` | `DateTimeOffset?` | When `LastError` was recorded |
| `Endpoint` | `string` | Target base URL, for display |
| `BackfillProgress` | `(long scanned, long posted)?` | Non-null only while `Backfilling`, for the progress line |

**State transitions**:

```
Idle ──start──▶ Backfilling ──backfill done──▶ Watching
Watching ──user Pause──▶ Paused ──user Resume──▶ Watching
{Backfilling, Watching} ──post fails──▶ Error ──next good tick──▶ Watching
Paused ──user Pause is a no-op; Resume only──▶ Watching
```

**Rules**:
- `TodayReceipts` / `TodayTokens` reset when the local date rolls over (injected clock so this is testable).
- Entering `Error` never loses queued receipts; ids are returned to the unseen set so the next tick re-posts them (this is the current failure-rollback behaviour, preserved).
- `State == Error` is a display state, not a stop; the watch loop keeps ticking.

## CollectConfig (on disk: `%APPDATA%\OmnisRouter\collect.json`)

Per-user configuration written by the setup window, read on every launch.

| Field | Type | Notes |
|---|---|---|
| `Endpoint` | `string` | Dashboard base URL; defaults to `https://app.omnisvigil.com` |
| `ProtectedKey` | `string` | Base64 DPAPI blob of the project key (CurrentUser scope). Never the raw key |
| `Root` | `string?` | Transcript root override; default `~/.claude/projects` |
| `IntervalSeconds` | `int` | Watch poll interval; default 15 (min 2, matching CLI) |
| `LogMaxBytes` | `long` | Per-file log cap; default 1 MB |
| `LogMaxFiles` | `int` | Retained rotated files; default 3 |
| `Paused` | `bool` | Persisted pause state so a pause survives restart |

**Validation**:
- `Endpoint` must be an absolute `http(s)` URL.
- `ProtectedKey` must decrypt to a non-empty string under the current user; failure routes to the setup window (FR-013), never a silent broken watcher.
- Missing file → first-run onboarding state (FR-010).

## ProtectedSecret (helper)

Wraps `System.Security.Cryptography.ProtectedData` (Windows). `Protect(string) → base64` and `Unprotect(base64) → string`, `DataProtectionScope.CurrentUser`. On non-Windows it throws a clear `PlatformNotSupportedException` — only the tray/config path calls it; the CLI uses `--key` directly and never touches it, so the library stays loadable cross-platform.

## UsageReceipt (unchanged)

The content-free record derived from a transcript entry (`ToRecord` today). Moves verbatim into `ReceiptRecord`. Key fields unchanged: `id` (message id, the idempotency key), `timestamp`, `chosen_model_id`, `usage.{input,output,cache_*}_tokens`, `est_cost_usd`, tags. A test pins the emitted JSON to the current shape (FR-022).

## LogSet (on disk: `%LOCALAPPDATA%\OmnisRouter\logs\`)

| Item | Notes |
|---|---|
| `collect.log` | Active log; appended by the engine |
| `collect.log.1..N` | Rotated files, N = `LogMaxFiles` |

Roll when `collect.log` exceeds `LogMaxBytes`: shift `.k → .k+1`, drop the oldest, start a fresh active file. "Clear logs" deletes the whole set.

## LoginTask (system: Task Scheduler)

A per-user "at log on" task named `OmnisRouter Collect` that launches the installed tray exe, with restart-on-failure. Created/removed by the installer; the tray also exposes register/unregister so Settings can toggle "start at login".
