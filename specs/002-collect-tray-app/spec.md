# Feature Specification: Collect watcher — professional Windows install & execution surface

**Feature Branch**: `002-collect-tray-app`

**Created**: 2026-09-05

**Status**: Draft

**Input**: Approved brainstorm design at `docs/superpowers/specs/2026-09-05-omnisrouter-tray-collect-surface-design.md`. Give the OmnisRouter collect watcher (`omnisrouter collect --watch`) a professional Windows install and execution surface, replacing the console window left open in `C:\Tools`. The watcher tails per-user Claude Code transcripts and pushes content-free usage receipts to the OmnisVigil cloud dashboard.

## Overview

Today a user who wants OmnisVigil to see their flat-rate Claude usage runs a console command and leaves the window open:

```
omnisrouter.exe collect --url https://app.omnisvigil.com --key ovk_… --all --watch
```

That console window is the install surface and the activity surface both, and neither is acceptable for a product: the tool is an unpacked zip in `C:\Tools`, the project key sits in plain view on the command line, and "activity" is scrolling debug text. This feature replaces that with a Windows application: installed like any other app, running quietly in the background from login, and reporting itself through a system-tray presence. The real analytics stay in the OmnisVigil dashboard, which already presents them well; the local surface is about *liveness*, not a second dashboard.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Install once and forget it, no console (Priority: P1)

A user installs OmnisRouter like a normal Windows app. From the next login onward it starts on its own, runs with no visible window, and shows a small tray icon whose colour tells them at a glance whether collection is healthy. They never open a console again.

**Why this priority**: This is the headline complaint. Without it the tool still looks and behaves like a script left running. It is also the minimum viable slice: a background tray watcher that installs, auto-starts, and reports liveness delivers the whole value on its own.

**Independent Test**: Install the package on a clean Windows account, log out and back in, and confirm the watcher is running with a tray icon and no console window, and that it is posting receipts (verifiable in the dashboard) exactly as the old console command did.

**Acceptance Scenarios**:

1. **Given** the package is installed, **When** the user logs in, **Then** the watcher starts automatically with no console window and a tray icon appears.
2. **Given** the watcher is running and healthy, **When** the user hovers the tray icon, **Then** a tooltip shows a running state, the last-post time, and today's receipt count.
3. **Given** the watcher cannot reach OmnisVigil or a post fails, **When** the user looks at the tray icon, **Then** its colour/state shows an error rather than staying green.
4. **Given** the watcher is already running, **When** the user launches it again, **Then** a second copy does not start (no double-counting); the existing one is surfaced instead.
5. **Given** the watcher process stops unexpectedly, **When** the failure occurs during a logged-in session, **Then** it is restarted automatically without user action.

---

### User Story 2 - Guided first-run setup, no flags (Priority: P2)

On first run with nothing configured, the app asks for what it needs through a small window: the dashboard URL (pre-filled) and the project key, with a button that opens the dashboard's Connect page to fetch a key. Once saved, it starts watching. The key is stored so it is not exposed on any command line, and the user never has to remember flags.

**Why this priority**: Getting the key off the command line and removing the flag ceremony is core to "professional", but the P1 slice can be proven using existing configuration first, so this is a fast follow rather than a blocker.

**Independent Test**: On an account with no saved configuration, launch the app and confirm it prompts for URL + key, that "Open Connect page" reaches the dashboard, and that after saving it begins posting receipts and does not prompt again on the next launch.

**Acceptance Scenarios**:

1. **Given** no saved configuration, **When** the app launches, **Then** it shows a setup window with a pre-filled URL, an empty key field, and an "Open Connect page" action.
2. **Given** the setup window is open, **When** the user pastes a valid key and saves, **Then** collection starts and the setup window does not reappear on subsequent launches.
3. **Given** a saved configuration, **When** anyone inspects the running process or its stored settings, **Then** the project key is not readable in plain text.
4. **Given** an invalid or empty key is entered, **When** the user tries to save, **Then** the app refuses and explains what is wrong rather than starting a broken watcher.

---

### User Story 3 - Glance at status, jump to the real dashboard (Priority: P2)

The user clicks the tray icon and gets a small panel showing current state, when it last posted, how many receipts today, and the last error if there was one. A menu lets them pause and resume collection, and open the OmnisVigil dashboard for the actual spend and savings analytics.

**Why this priority**: This is the activity-surface half of the complaint. It depends on the P1 tray existing, and delivers the "show its working" upgrade over scrolling console text.

**Independent Test**: With the watcher running, click the tray icon and confirm the panel reflects real collection state, that Pause stops posting and Resume restarts it, and that "Open dashboard" opens the OmnisVigil site.

**Acceptance Scenarios**:

1. **Given** the watcher is running, **When** the user left-clicks the tray icon, **Then** a panel shows state, last-post time, today's receipt count, and last error if any.
2. **Given** the panel or menu is open, **When** the user chooses Pause, **Then** collection stops and the tray state reflects paused; choosing Resume restarts it.
3. **Given** the menu is open, **When** the user chooses "Open dashboard", **Then** the OmnisVigil dashboard opens in the browser.
4. **Given** collection has posted receipts today, **When** the panel is shown, **Then** the counts it displays match what the watcher has actually posted this session.

---

### User Story 4 - Managed local logs (Priority: P3)

Diagnostic detail that used to scroll past in the console is written to a local log the user can open from the tray menu. The log is capped so it cannot grow without bound, and the user can clear it.

**Why this priority**: Useful for troubleshooting and it replaces the console's diagnostic role, but day-to-day the tray liveness is enough, so it is the lowest priority.

**Independent Test**: Run the watcher through several collection cycles, open the log from the menu and confirm it records activity and errors, confirm the log does not exceed its size cap over a long run, and confirm Clear empties it.

**Acceptance Scenarios**:

1. **Given** the watcher has been running, **When** the user chooses "Open logs", **Then** the current log opens showing collection activity and any errors.
2. **Given** the log has reached its size cap, **When** more is written, **Then** old content is rolled off and total log storage stays within the cap.
3. **Given** the menu is open, **When** the user chooses "Clear logs", **Then** the logs are emptied.

---

### User Story 5 - Standard install, distribution and clean removal (Priority: P2)

The tool appears in Windows "Installed apps" with a name, publisher and version, and has a Start-Menu entry. It can be installed from the winget package manager. Uninstalling removes the app, its Start-Menu entry, and its auto-start registration, leaving the machine clean.

**Why this priority**: The "Installed apps" entry and clean uninstall are a large part of what makes an install feel professional versus a zip in `C:\Tools`, and winget is the expected distribution channel. It builds on the P1 package.

**Independent Test**: Install via the package and via `winget install`, confirm an "Installed apps" entry with correct metadata and a Start-Menu shortcut, then uninstall and confirm the app, shortcut, and auto-start entry are all gone.

**Acceptance Scenarios**:

1. **Given** the package is installed, **When** the user opens Windows "Installed apps", **Then** OmnisRouter is listed with a publisher and version and can be uninstalled from there.
2. **Given** winget is available, **When** the user runs the documented winget install command, **Then** the same app is installed.
3. **Given** the app is installed and auto-starting, **When** the user uninstalls it, **Then** the app, its Start-Menu entry, and its login auto-start registration are removed.
4. **Given** the app is installed without administrator rights, **When** installation runs, **Then** it completes without an elevation prompt.

---

### Edge Cases

- **No transcripts yet**: a freshly configured account with an empty transcript folder shows a healthy idle/watching state, not an error.
- **Dashboard unreachable at start**: the watcher starts, shows an error/degraded state, keeps retrying, and recovers to healthy on the next successful post without losing or double-counting receipts.
- **Machine sleeps / user locks the screen**: on resume the watcher catches up on transcripts written while asleep without double-counting.
- **Clock crosses local midnight**: "today" counts reset appropriately at local midnight.
- **Config file present but key missing or corrupt**: the app falls back to the setup window rather than crashing or running blind.
- **Second login session / fast user switching**: the single-instance guard is per-user-session so one user's watcher does not block another's.
- **Large first backfill**: the initial history backfill shows progress and does not present as a hang or an error.

## Requirements *(mandatory)*

### Functional Requirements

**Execution surface**

- **FR-001**: The watcher MUST be runnable as a background application in the user's own login session, with no console window.
- **FR-002**: The application MUST start automatically when the user logs in, and MUST be restarted automatically if it stops unexpectedly during a logged-in session.
- **FR-003**: The application MUST present a system-tray icon whose visual state distinguishes at least: running/healthy, paused, and error/unreachable.
- **FR-004**: The tray tooltip MUST show a summary of current state including the last-post time and today's receipt count.
- **FR-005**: The application MUST prevent a second concurrent instance for the same user session so usage is never double-counted; a second launch MUST surface the existing instance.

**Status & control**

- **FR-006**: The user MUST be able to open a status panel from the tray showing state, last-post time, today's receipt count, and the last error (if any).
- **FR-007**: The user MUST be able to pause and resume collection from the tray without exiting the application.
- **FR-008**: The user MUST be able to open the OmnisVigil dashboard from the tray. The local surface MUST NOT attempt to reproduce the dashboard's spend/savings analytics.
- **FR-009**: The status shown locally MUST reflect what the watcher has actually posted (counts and last-post time MUST match the collection activity, not be estimated separately).

**Configuration & onboarding**

- **FR-010**: On first run with no saved configuration, the application MUST present a setup step that captures the dashboard URL (pre-filled with the default) and the project key, including an action that opens the dashboard's Connect page.
- **FR-011**: The project key MUST be stored encrypted at rest, scoped to the current user, and MUST NOT appear in the process command line or in plain-text settings.
- **FR-012**: After configuration is saved, subsequent launches MUST start collecting without prompting again.
- **FR-013**: Invalid or missing configuration MUST route the user back to setup with a clear explanation, never a silent failure or a broken watcher.

**Logging**

- **FR-014**: The application MUST write diagnostic activity and errors to a local log that the user can open from the tray.
- **FR-015**: Local logs MUST be size-capped with bounded retention so they cannot grow without limit, and the user MUST be able to clear them from the tray.

**Install, distribution & removal**

- **FR-016**: The tool MUST install as a standard Windows application that appears in "Installed apps" with a name, publisher, and version, and MUST provide a Start-Menu entry.
- **FR-017**: Installation MUST complete per-user without requiring administrator elevation.
- **FR-018**: The tool MUST be installable through the winget package manager.
- **FR-019**: Uninstalling MUST remove the application, its Start-Menu entry, and its login auto-start registration; user configuration MAY be retained per standard Windows behaviour.

**Continuity of existing behaviour**

- **FR-020**: The existing headless `omnisrouter collect` command line (including `--url`/`--key`/`--watch`/`--all`/`--since`/`--batch`/`--interval`/`--dry-run`) MUST continue to work unchanged for scripted and non-Windows use.
- **FR-021**: The collection behaviour that both the command line and the tray application drive MUST be a single shared implementation, not duplicated per surface, so their behaviour cannot diverge.
- **FR-022**: The receipts posted by the tray application MUST be byte-for-byte equivalent in shape and idempotency to those posted by the existing command line (same content-free record, same de-duplication by message id, no double-counting across backfill and watch).

### Key Entities

- **Collection status snapshot**: the current state the local surface displays — state (idle / backfilling / watching / paused / error), last-post time, today's receipt count, today's token total, last error, and the target endpoint.
- **Local configuration**: the saved dashboard URL and the encrypted project key for the current user.
- **Usage receipt**: the existing content-free record derived from a Claude Code transcript entry, de-duplicated by message id (unchanged by this feature).
- **Local log**: the capped, rolling diagnostic record of collection activity and errors.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A new user can go from installer to a running, receipt-posting watcher without ever opening or reading a console window.
- **SC-002**: After installation, the watcher is running within a few seconds of login on every subsequent login, with no manual start.
- **SC-003**: At no point during normal operation is the project key visible in the process list or in a plain-text file.
- **SC-004**: A user can tell whether collection is healthy in one glance at the tray icon, without opening anything.
- **SC-005**: Total receipts posted over any period match one-for-one what the existing command line would have posted for the same transcripts (zero double-counts, zero drops).
- **SC-006**: Local log storage stays within its defined cap indefinitely, regardless of how long the watcher runs.
- **SC-007**: Uninstalling leaves no OmnisRouter application, Start-Menu entry, or auto-start registration behind.
- **SC-008**: The existing command-line collect workflow produces identical output and behaviour to before this feature.

## Assumptions

- **Target platform for the new surface is Windows only.** macOS and Linux users continue on the headless command line; a cross-platform tray is a separate future effort (out of scope here).
- **The watcher's correct lifetime is the user's login session.** It tails per-user data that only changes while the user is logged in, so a machine-wide service that runs while logged out is explicitly not wanted.
- **The local surface is a liveness surface, not an analytics surface.** Spend, savings, and trend analytics remain the responsibility of the OmnisVigil dashboard, which already presents them; duplicating them locally is out of scope (Principle XII: a number is shown only where it changes a decision, and that decision already lives in the dashboard).
- **The default dashboard endpoint is `https://app.omnisvigil.com`**, pre-filled at setup and overridable.
- **The specific technology choices are locked in the design doc** (single user-session tray application, WiX MSI plus winget, per-user Scheduled Task at logon, DPAPI-encrypted key, a shared collection engine extracted into its own library). They are recorded there as decisions; this spec states the observable requirements they satisfy.
- **Code-signing is a deferred follow-up.** Without a signing certificate the installer will trip SmartScreen's "unknown publisher" warning; procuring a certificate is tracked separately and is out of scope for this feature.
- **The OmnisVigil dashboard and its ingest/Connect endpoints are unchanged** by this feature; they are consumed as they already exist.
