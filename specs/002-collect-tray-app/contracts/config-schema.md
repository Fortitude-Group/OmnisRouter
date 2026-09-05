# Contract: collect.json + key protection

## File location

`%APPDATA%\OmnisRouter\collect.json` (roaming per-user). Created by the setup window; read on every launch.

## Schema (v1)

```json
{
  "schemaVersion": 1,
  "endpoint": "https://app.omnisvigil.com",
  "protectedKey": "<base64 DPAPI blob, CurrentUser scope>",
  "root": null,
  "intervalSeconds": 15,
  "logMaxBytes": 1048576,
  "logMaxFiles": 3,
  "paused": false
}
```

| Field | Required | Default | Validation |
|---|---|---|---|
| `schemaVersion` | yes | `1` | integer; unknown future versions refuse to load rather than guess |
| `endpoint` | yes | `https://app.omnisvigil.com` | absolute http/https URL |
| `protectedKey` | yes | — | base64; must DPAPI-unprotect to a non-empty string under the current user |
| `root` | no | `~/.claude/projects` | existing directory when set |
| `intervalSeconds` | no | `15` | ≥ 2 (matches CLI floor) |
| `logMaxBytes` | no | `1048576` | > 0 |
| `logMaxFiles` | no | `3` | ≥ 1 |
| `paused` | no | `false` | bool |

## Key handling rules

- The raw project key is **never** written to disk or passed on a command line. Only `protectedKey` (DPAPI ciphertext) is persisted (FR-011).
- Decrypt lazily at startup; on failure (wrong user, corrupt blob, missing key) the app opens the setup window with an explanation (FR-013), it does not run a broken watcher.
- `HttpReceiptSink` receives the decrypted key in memory only.

## Precedence (headless CLI unchanged)

The `omnisrouter collect` CLI resolves endpoint/key exactly as today: `--url`/`--key` first, else the `OmnisVigil` config section (`appsettings.json` + env). The CLI does **not** read `collect.json` and does **not** require DPAPI — this keeps CI and non-Windows use working (FR-020). `collect.json` is the tray's store only.

## Migration

First tray launch with no `collect.json` and no other config → onboarding. If a user previously ran the CLI with `--url/--key`, those are not auto-imported (different trust model); the setup window is shown once.
