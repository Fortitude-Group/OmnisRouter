# Phase 0 Research: Collect watcher Windows install & execution surface

The brainstorm design resolved the big product decisions (single user-session tray, MSI + winget, DPAPI key, shared engine). This document records the technical decisions the plan depends on, with rationale and rejected alternatives. Everything here was checked against the actual repo (Program.cs, the Collect trio, Directory.Build.props, release.yml) rather than assumed.

## D1 — Process & execution model: single user-session app, not a Windows Service

**Decision**: One `net10.0-windows` process launched at login that hosts both the collect engine and the tray UI. Auto-start via a per-user Task Scheduler "at log on" trigger with restart-on-failure. No session-0 Windows Service.

**Rationale**: A Windows Service runs in session 0 and cannot draw a tray icon or window, which would force a two-process split (service + tray) communicating over IPC. The watcher tails per-user data (`~/.claude/projects`) that only changes while the user is logged in, so the login-session lifetime is correct, not a compromise. Task Scheduler gives the "managed, restarts on crash, starts after login" behaviour without the session-0/IPC cost.

**Alternatives rejected**: (a) Windows Service + separate tray + named-pipe IPC — real complexity for no benefit here. (b) Registry `Run` key auto-start — no restart-on-failure and fires in the boot storm; Task Scheduler is strictly better and still per-user, no admin.

## D2 — Engine extraction & the `Console`/loop split

**Decision**: Move `TranscriptCollector`, `TranscriptReader`, `ModelPrices` from `src/OmnisRouter.Api/Collect/` into a new `net10.0` library `OmnisRouter.Collect`. Refactor `TranscriptCollector.RunAsync` into a headless `CollectEngine` that never references `Console`, raises status changes, and posts through `IReceiptSink`. Two thin renderers consume it: `ConsoleCollectRunner` (in Api, preserving today's exact CLI output) and the tray.

**Rationale**: Principle I — one shared implementation behind an explicit interface so CLI and tray cannot diverge (FR-021, FR-022). The current code interleaves the tail loop with `Console.Write`, which blocks reuse and testing. Putting the POST behind `IReceiptSink` makes the engine network-free under test. The trio has a single external dependency (`OmnisRouter.Vigil` for `OmnisVigilOptions`), so the library stays thin.

**Alternatives rejected**: Reference `OmnisRouter.Api` (Sdk.Web) from the tray — drags ASP.NET into a desktop app and can't cross a Windows-only TFM cleanly. Duplicate the collect logic in the tray — violates Principle I and guarantees drift.

**Behaviour-preservation guardrail**: the receipt record built by `ToRecord` and the id-based de-duplication move verbatim; a test asserts the JSON shape is byte-for-byte what the current code emits (FR-022).

## D3 — Tray UI toolkit: WinForms `NotifyIcon`

**Decision**: WinForms on `net10.0-windows` (`<UseWindowsForms>true</UseWindowsForms>`, `OutputType=WinExe`), using `NotifyIcon` for the tray, a `ContextMenuStrip` for the menu, and a small borderless `Form` for the liveness popup and the setup window.

**Rationale**: `NotifyIcon` is the native, lowest-ceremony tray API on Windows and ships in the Windows Desktop pack; WinExe means no console window (FR-001). The UI is trivial (icon, menu, two small forms), so WPF's weight buys nothing. Keeping the tray logic-free (pure binding over `CollectionStatus`) keeps testing in the engine.

**Alternatives rejected**: WPF — heavier for no gain at this UI complexity. A cross-platform tray library (e.g. Avalonia) — the spec scopes this to Windows; adding a cross-platform UI stack now is unjustified complexity (Principle V) and a separate future effort.

**Gotcha captured**: `TreatWarningsAsErrors=true` is repo-wide, so the WinForms project must be warning-clean (designer-generated code included); prefer code-defined UI over the designer to keep it clean and reviewable.

## D4 — Secret at rest: DPAPI (`ProtectedData`, CurrentUser scope)

**Decision**: Store the project key in `collect.json` as a base64 DPAPI blob encrypted with `DataProtectionScope.CurrentUser`. A `ProtectedSecret` helper wraps/unwraps. The headless CLI keeps taking `--key` for CI and does not require the encrypted store.

**Rationale**: DPAPI is the built-in, keyless per-user secret store on Windows; CurrentUser scope ties the ciphertext to the logged-in user so another account can't read it. This satisfies FR-011 and removes the key from the process command line (its current exposure). `System.Security.Cryptography.ProtectedData` is a first-party package.

**Alternatives rejected**: Windows Credential Manager — heavier API for one secret and awkward to co-locate with the rest of the config. Plain-text config — fails FR-011 outright. Because `ProtectedData` is Windows-only, `ProtectedSecret` guards non-Windows callers (the CLI path never needs it), keeping the library loadable cross-platform.

## D5 — Single instance per user session

**Decision**: A named `Mutex` scoped to the current session (a `Local\` name including the user SID). If already held, signal the running instance to show its popup and exit.

**Rationale**: Prevents double-counting from two concurrent watchers (FR-005) while allowing a second logged-in user their own instance (edge case: fast user switching). A `Local\` mutex is per-session by construction.

**Alternatives rejected**: A global `Global\` mutex — would wrongly block a second user's watcher. A lock file — races and stale-lock cleanup are worse than a kernel mutex.

## D6 — Rolling, size-capped logging

**Decision**: A minimal rolling file appender in the engine: one active log plus a small number of rotated files, each capped (e.g. 1 MB × 3 files), oldest deleted on roll. "Open logs" opens the active file; "Clear logs" truncates/removes the set.

**Rationale**: FR-014/FR-015 require bounded logs with open/clear. A tiny purpose-built appender avoids adding a logging framework dependency for one sink (Principle V). Sizes are configurable in `collect.json`.

**Alternatives rejected**: Serilog/NLog rolling sinks — a full dependency for one bounded file is unjustified here. Unbounded log — fails FR-015.

## D7 — Packaging: WiX v5 per-user MSI

**Decision**: A WiX v5 project under `installer/msi/` producing a per-user MSI (`InstallScope` per-user / `MSIINSTALLPERUSER`), harvesting the tray's self-contained publish output. The MSI creates the Start-Menu shortcut and registers the per-user login Scheduled Task on install; uninstall removes files, shortcut, and the task (FR-016, FR-017, FR-019). ARP (Add/Remove Programs) metadata carries name, publisher "Fortitude Omnis", and version.

**Rationale**: WiX is the standard declarative MSI toolchain and integrates with the .NET build. Per-user scope avoids UAC (FR-017). An MSI is what produces the "Installed apps" entry and clean uninstall that separate a product from a zip.

**Alternatives rejected**: MSIX — sandboxing complicates reading `~/.claude` transcripts and registering an arbitrary Scheduled Task; heavier for this need. Inno Setup/NSIS — not declarative, and winget prefers a clean MSI/ silent install. A self-installing exe (copy-to-LocalAppData on first run) — no ARP entry, weaker uninstall story.

**Scheduled Task creation from MSI**: register the task via a bundled step (WiX custom action invoking `schtasks`/Task Scheduler for the current user) rather than baking the current username into WiX at author time; the task action launches the installed tray exe at logon with restart-on-failure.

## D8 — Distribution: winget manifest from the start

**Decision**: Add a winget manifest set under `installer/winget/` (version + installer + default-locale YAML) referencing the released MSI, validated with `winget validate` in the release gate. Publishing to the public winget-pkgs repo is a release-process step (like the existing npm publish), out of the build itself.

**Rationale**: FR-018 requires winget installability. A clean per-user MSI with a silent-install switch is exactly what a winget manifest wraps. Validating in the gate keeps the manifest honest per release.

**Alternatives rejected**: Deferring winget — the owner asked for it from the start. A winget "portable"/zip manifest — loses the ARP/uninstall benefits the MSI gives.

## D9 — CI/release integration: add a `windows-latest` job

**Decision**: Add a `windows` job to `.github/workflows/release.yml` (needs: gate) that runs on `windows-latest`, publishes `OmnisRouter.Tray` self-contained for `win-x64`, builds the MSI with the WiX .NET tool, validates the winget manifest, and attaches the MSI to the GitHub Release. The existing Ubuntu `binaries` job (self-contained CLI zips) and the container/npm jobs are unchanged.

**Rationale**: WinForms (`net10.0-windows`) and WiX MSI authoring build reliably on Windows; the current pipeline publishes everything from `ubuntu-latest`, which cannot produce a WinForms app or drive WiX naturally. A dedicated Windows job is the clean seam and keeps the CLI/container paths untouched.

**Alternatives rejected**: Cross-building WinForms/MSI on Linux — unsupported/fragile. Building the MSI locally and committing it — not reproducible, not gated.

**Note**: this touches CI config, not any production environment (Principle X n/a). Code-signing the MSI is deferred (Assumptions); until then SmartScreen shows "unknown publisher".

## Resolved unknowns

No `NEEDS CLARIFICATION` markers remained from the spec. The one open technical question surfaced during research — how packaging fits a pipeline that publishes from Ubuntu — is resolved by D9 (a Windows release job).
