# Quickstart & Validation: Collect watcher Windows surface

Runnable checks that prove the feature works end to end. References [contracts/](./contracts/) and [data-model.md](./data-model.md) for detail rather than repeating it.

## Prerequisites

- Windows 10+ with .NET 10 SDK (dev) or nothing (installed MSI is self-contained).
- A Claude Code transcript folder at `~/.claude/projects` with some history.
- An OmnisVigil project key (`ovk_…`) from the dashboard Connect page.

## Build & test (dev loop)

```powershell
dotnet build OmnisRouter.slnx -c Debug          # must be 0 Error / 0 Warning (TreatWarningsAsErrors)
dotnet test  OmnisRouter.slnx -c Debug          # engine, config, DPAPI, CLI golden tests green
dotnet run --project src/OmnisRouter.Tray       # launches the tray in your session (no console)
```

## Scenario 1 — CLI parity (FR-020, FR-022)

Prove the extraction changed nothing for the command line.

```powershell
dotnet run --project src/OmnisRouter.Api -- collect --dry-run --all
```

**Expected**: the same header, `scanned/unique/posted/dup` progress, and `unique calls / would post …` summary the current binary prints. The CLI golden test asserts this automatically.

## Scenario 2 — First-run onboarding (US2, FR-010/011/013)

Delete any `%APPDATA%\OmnisRouter\collect.json`, then launch the tray.

**Expected**:
1. Setup window opens with `https://app.omnisvigil.com` pre-filled and an "Open Connect page" button.
2. Paste the key, Save → collection starts, window closes.
3. `collect.json` exists and contains `protectedKey` (a DPAPI blob), **not** the raw key. Confirm:
   ```powershell
   Get-Content "$env:APPDATA\OmnisRouter\collect.json"   # protectedKey is base64, no ovk_ visible
   Get-CimInstance Win32_Process -Filter "Name='OmnisRouter.Tray.exe'" | Select CommandLine  # no key on the command line
   ```
4. Relaunch → no setup prompt (FR-012).

## Scenario 3 — Background execution & liveness (US1, FR-001/003/004/005)

**Expected**:
- Tray icon present, no console window (`Get-Process` shows `OmnisRouter.Tray`, no conhost for it).
- Hover → tooltip `OmnisRouter · last posted HH:MM · N today`.
- Launch a second instance → it does not start a second watcher; the existing popup surfaces (single-instance mutex).
- Kill the process during a session → the login Scheduled Task's restart-on-failure brings it back (or re-login restarts it).

## Scenario 4 — Status, pause/resume, dashboard (US3, FR-006/007/008/009)

- Left-click → popup shows state, last-post time, today's count, last error (if any).
- Pause → tray goes grey, posting stops; Resume → back to green, posting continues.
- "Open dashboard" → browser opens `app.omnisvigil.com`.
- The popup's today-count equals what the engine actually posted this session (cross-check the dashboard).

## Scenario 5 — Error handling & recovery (edge cases)

Point `endpoint` at an unreachable URL, run, then restore it.

**Expected**: icon goes amber/red, tooltip says retrying, log records the failure, no receipts lost; on restore the next tick posts the backlog exactly once (no double count).

## Scenario 6 — Logs stay bounded (US4, FR-014/015)

Run through many ticks (or lower `logMaxBytes`).

**Expected**: `%LOCALAPPDATA%\OmnisRouter\logs\` never exceeds `logMaxBytes × logMaxFiles`; "Open logs" shows activity; "Clear logs" empties the set.

## Scenario 7 — Install / winget / uninstall (US5, FR-016..019)

```powershell
msiexec /i OmnisRouter-<ver>.msi /qn        # per-user, no elevation prompt
# or:  winget install OmnisRouter
```

**Expected**:
- "Installed apps" lists OmnisRouter with publisher Fortitude Omnis and the version; Start-Menu entry present.
- A per-user "at log on" Scheduled Task `OmnisRouter Collect` exists (`schtasks /query /tn "OmnisRouter Collect"`).
- Uninstall removes the app, the Start-Menu entry, and the Scheduled Task:
  ```powershell
  msiexec /x OmnisRouter-<ver>.msi /qn
  schtasks /query /tn "OmnisRouter Collect"   # → not found
  ```

## Release gate

`scripts/release-gate.ps1` (build 0/0 + tests + benchmark) still gates the tag. The new Windows release job additionally runs `winget validate` on the manifest and attaches the MSI to the GitHub Release (research.md D9).
