# Feature Specification: Tray-managed local router proxy

**Feature Branch**: `003-tray-proxy`

**Created**: 2026-09-09

**Status**: Draft

**Input**: Run and manage the OmnisRouter routing proxy from the Windows tray app, with a settings
window for BYOK provider keys and one-click wiring of coding tools at the local router, so a user
never needs docker or a console. Approved design at
`docs/superpowers/specs/2026-09-09-omnisrouter-tray-proxy-design.md`.

## Overview

Today the OmnisRouter routing proxy runs as a docker container or a console binary, and the tray
app that ships is a flat-rate usage collector only. A user who wants the actual routing and savings
has to run a server themselves and edit client config by hand. This feature brings the proxy into
the same tray app: one installed application, one tray icon, with an independent switch to run the
local router, a settings window to hold provider keys, and one click to point Claude Code, Codex or
Cursor at it. The router itself is unchanged, it is wrapped in a desktop surface so it installs,
runs from login, and reports its state like any other app.

Two modes coexist and must never be confused. "Report usage to OmnisVigil" watches flat-rate Claude
usage and reports it. "Local router proxy" routes your traffic through your own provider keys, which
is per-token billing. The user must always be able to tell, at a glance, which is on.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Run the router from the tray, no console (Priority: P1)

A user installs the app, opens the tray menu, and turns on "Local router proxy". The router starts
quietly in the background with no console window, and the tray shows it as running and healthy.
From the next login it comes back on its own. They never open a terminal or run docker.

**Why this priority**: This is the headline. Without it the proxy is still a server you babysit.
A background router that installs, auto-starts, and reports liveness is the minimum viable slice and
delivers the whole "no docker, no console" value on its own.

**Independent Test**: On a clean Windows account, install the package, enable the proxy toggle, and
confirm the router is serving on the local loopback port with no console window, that its health
endpoint responds, and that after logout and login it is running again.

**Acceptance Scenarios**:

1. **Given** the app is installed and the proxy is off, **When** the user turns on "Local router
   proxy", **Then** the router starts with no visible window and the tray shows a running state
   within a few seconds.
2. **Given** the proxy is starting, **When** the router is not yet ready, **Then** the tray shows a
   starting state and does not report "running" until the readiness check passes.
3. **Given** the proxy is running, **When** the router process exits unexpectedly, **Then** the tray
   restarts it automatically and only shows an error state if restarts keep failing.
4. **Given** the proxy was on at last quit, **When** the user logs in again, **Then** it starts
   automatically in the same state.
5. **Given** the proxy is running, **When** the user turns the toggle off, **Then** the router
   process stops cleanly and the tray shows it as off.

---

### User Story 2 - Add provider keys in a settings window (Priority: P1)

A user opens "Provider keys" from the tray and pastes an API key for one or more providers
(Anthropic, OpenAI, Gemini, OpenRouter). The keys are saved securely and used by the router to make
calls. The user can see which providers have a key set and remove one, but never sees a stored key
value again.

**Why this priority**: The router cannot route without at least one provider key, so key management
is part of the same minimum product as running the proxy. A running router with no keys is inert.

**Independent Test**: With the proxy running, add a key for one provider through the window, confirm
it is accepted and shown as set, restart the app, confirm it is still set, and remove it.

**Acceptance Scenarios**:

1. **Given** the proxy is running and no keys are set, **When** the user first enables routing,
   **Then** the keys window opens prompting for at least one provider key.
2. **Given** the keys window is open, **When** the user pastes a key for a provider and saves,
   **Then** the provider shows as "key set" and the raw value is never displayed again.
3. **Given** a provider has a key set, **When** the user removes it, **Then** the provider shows as
   "no key" and the router can no longer route to that provider.
4. **Given** keys are set, **When** the app restarts, **Then** the keys remain set (held in the
   router's encrypted store, not re-entered).

---

### User Story 3 - Connect a coding tool with one click, and revert (Priority: P2)

A user opens "Connect an app", sees their installed coding tools, and connects one with a single
click. That tool now routes through the local router. A revert click puts the tool back exactly as
it was.

**Why this priority**: A proxy nothing points at saves nothing. One-click wiring is what makes it
first-class rather than a manual chore, but the proxy and keys (P1) deliver value before this lands.

**Independent Test**: Connect Claude Code through the tray, confirm its configuration now points at
the local router with the router token, make a request and see it routed, then revert and confirm
the configuration is byte-for-byte what it was before connecting.

**Acceptance Scenarios**:

1. **Given** Claude Code is installed and not connected, **When** the user clicks Connect for it,
   **Then** its configuration is updated to use the local router URL and token, a backup of the
   prior configuration is kept, and the tray shows it as connected.
2. **Given** Claude Code is connected, **When** the user clicks Revert, **Then** its configuration
   is restored to exactly its pre-connect state.
3. **Given** Codex is installed, **When** the user connects then reverts it, **Then** a clearly
   delimited managed block is added and later removed, leaving the rest of its config untouched.
4. **Given** a client whose configuration cannot be edited safely (Cursor), **When** the user opens
   Connect for it, **Then** the tray shows the exact URL and token to paste, with a copy action,
   rather than editing anything.
5. **Given** a client's existing configuration file is malformed, **When** the user clicks Connect,
   **Then** the tray refuses and explains, rather than corrupting the file.

---

### User Story 4 - See routed spend and savings in OmnisVigil, without double counting (Priority: P2)

A user who also reports to OmnisVigil sees their routed traffic and its savings on the dashboard,
attributed correctly, with no client counted twice.

**Why this priority**: It closes the loop with the wider product (spend attribution and the savings
ledger), but the local proxy already delivers routing and savings before this reporting lands.

**Independent Test**: With both toggles on and a project key set, connect Claude Code to the proxy,
make requests, and confirm the dashboard shows that traffic once, from the router's receipts, with
savings populated, and that the same traffic is not also reported by the collector.

**Acceptance Scenarios**:

1. **Given** the proxy is on and a project key is configured, **When** traffic is routed, **Then**
   the router reports content-free receipts for it to OmnisVigil, including the savings delta.
2. **Given** the user has never set up the collector, **When** they enable proxy reporting, **Then**
   the tray prompts for a project key using the same capture flow the collector uses.
3. **Given** a client is connected to the proxy, **When** the collector runs, **Then** that client
   is excluded from the collector's scope so its traffic is reported only once.
4. **Given** a connected client is disconnected, **When** the collector next runs, **Then** that
   client returns to the collector's scope.

---

### User Story 5 - Choose the local port (Priority: P3)

A user opens settings and changes the local port the router listens on, for example because the
default clashes with something else on their machine.

**Why this priority**: A sensible default works for almost everyone, so this is a convenience for
the minority with a conflict, not core to the value.

**Independent Test**: Change the port in settings, confirm the router restarts on the new port, and
confirm any connected clients now point at the new port.

**Acceptance Scenarios**:

1. **Given** the proxy is running on the default port, **When** the user sets a different valid
   port, **Then** the router restarts on the new port and connected clients are re-pointed to it.
2. **Given** the chosen port is already in use, **When** the router tries to start, **Then** the
   tray shows an error state naming the conflict and does not silently fail.
3. **Given** an invalid port value is entered, **When** the user tries to save, **Then** the setting
   is rejected with a clear message.

---

### Edge Cases

- Enabling routing with no provider keys set: the router runs but every route fails, so the tray
  opens the keys window on first enable and warns while no key is set.
- Turning the proxy off while clients are still connected: the tray warns and offers to revert those
  clients, so none is left pointing at a dead port.
- Changing the port while clients are connected: all connected clients are re-pointed to the new URL.
- The router process crashes repeatedly: the tray backs off and surfaces an error rather than
  spinning, matching the collector's supervision behaviour.
- The keys window is opened before the router is ready: actions are disabled until the readiness
  check passes, so no call races an unmigrated server.
- Uninstalling while the proxy is running: the installer stops the app, which stops the router.
- A connected client's config file is edited by the user or the tool after connecting: revert
  restores the state captured at connect time and explains if it can no longer do so cleanly.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The app MUST run the OmnisRouter routing proxy as a background process controlled by a
  single tray toggle, with no console window at any point.
- **FR-002**: The collector and the proxy MUST be independent toggles, each remembered across
  restarts, so a user can run either, both, or neither.
- **FR-003**: The router MUST bind to the local loopback interface only and MUST never be reachable
  from other machines.
- **FR-004**: The app MUST start the proxy automatically at login when it was on at last quit, using
  the same auto-start mechanism the collector uses.
- **FR-005**: The app MUST detect readiness of the router before treating it as running, and MUST
  not enable key management or client wiring until the router is ready.
- **FR-006**: The app MUST supervise the router, restarting it on unexpected exit with backoff, and
  MUST surface a clear error state when restarts keep failing.
- **FR-007**: The app MUST make the active mode unmistakable: a distinct tray icon treatment when
  routing is live, a status readout that names the mode in plain language, and a first-time
  confirmation when routing is enabled that explains routed traffic is billed per token by the
  provider.
- **FR-008**: The app MUST provide a settings window to add, view-as-set, and remove provider keys
  for Anthropic, OpenAI, Gemini and OpenRouter.
- **FR-009**: Provider keys MUST be stored only in the router's encrypted key store, and a stored
  key value MUST never be displayed back to the user.
- **FR-010**: The app MUST let a user connect an installed coding tool (Claude Code, Codex, Cursor)
  to the local router with one action, and MUST record which clients are connected.
- **FR-011**: For clients whose configuration can be edited safely, the app MUST back up the prior
  configuration before changing it and MUST provide a revert that restores that prior state exactly.
- **FR-012**: For a client whose configuration cannot be edited safely, the app MUST present the URL
  and token to apply manually rather than editing the client.
- **FR-013**: The app MUST refuse to edit a client whose existing configuration is malformed, and
  MUST explain why, rather than risk corrupting it.
- **FR-014**: When the proxy is on and a project key is configured, the router MUST report
  content-free receipts for routed traffic to OmnisVigil, including the savings delta.
- **FR-015**: When both toggles are on, the app MUST exclude any client connected to the proxy from
  the collector's scope, so routed traffic is reported once, and MUST return a client to that scope
  when it is disconnected.
- **FR-016**: If proxy reporting is enabled and no project key exists, the app MUST prompt for one
  using the collector's existing capture flow.
- **FR-017**: The app MUST expose a configurable local port with a sensible non-conflicting default,
  MUST validate it, MUST restart the router when it changes, and MUST re-point connected clients to
  the new port.
- **FR-018**: The app MUST warn and offer to revert connected clients when the proxy is turned off,
  so no client is left pointing at a stopped router.
- **FR-019**: The app MUST keep the proxy's configuration and secrets separate from the collector's,
  so the two features' state never entangles.
- **FR-020**: Everything above MUST target Windows in this version. Other platforms keep the existing
  cross-platform command-line router and are out of scope here.

### Key Entities *(include if feature involves data)*

- **Router process**: the supervised background instance of the router, with a state the tray reads
  (off, starting, running, error) and a local port it serves on.
- **Router management token**: a secret the app generates once, uses to authenticate to the router's
  management interface, and writes into connected clients as their auth token. Held encrypted at
  rest.
- **Provider key**: a BYOK credential for one provider, held only in the router's encrypted store,
  referenced by provider and label, never re-displayed.
- **Connected client**: a coding tool the app has wired to the local router, tracked so it can be
  reverted and excluded from the collector's scope.
- **Router settings**: the user-visible configuration for the proxy, at minimum the local port,
  kept separate from the collector's configuration.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A user with no docker or command-line experience can go from a fresh install to a
  routed request through the local proxy without opening a console or a container, using only the
  tray menu and settings window.
- **SC-002**: At no point in normal use does a console window appear.
- **SC-003**: A user can always correctly identify, from the tray alone, whether they are routing
  (per-token) or reporting flat-rate usage, or both.
- **SC-004**: Reverting a connected client restores its configuration to exactly its pre-connect
  state, verified by comparison.
- **SC-005**: With both features on, no client's traffic is counted more than once on the OmnisVigil
  dashboard.
- **SC-006**: The proxy returns to its previous on or off state automatically after a logout and
  login, with no user action.
- **SC-007**: A stored provider key is never shown back to the user after it is saved.

## Assumptions

- The user is on Windows. macOS and Linux users keep the existing `omnisrouter serve` command line
  and are out of scope for this feature.
- The shipped router binary is reused as-is and bundled with the app, including its embedding model,
  accepting the resulting increase in installer size.
- Routing uses the user's own provider keys and is billed per token by the provider, which is a
  different economic model than a flat-rate subscription. The UI is responsible for making that
  clear, not for changing the billing.
- The existing collector's project-key capture and encrypted config surface are reused for the
  proxy's OmnisVigil reporting.
- The router's own management interface and encrypted key store are the single source of truth for
  provider keys, so the app never keeps a second copy.
- Code-signing the bundled binaries is desirable to avoid first-launch warnings and is handled as a
  release concern rather than a functional requirement here.
