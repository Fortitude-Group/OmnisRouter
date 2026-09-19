# Feature Specification: Tray cache-hygiene controls

**Feature Branch**: `005-tray-cache-hygiene`

**Created**: 2026-09-19

**Status**: Draft

**Input**: Let a standalone tray user turn cache-hygiene fixes on or off, and control emit-to-Vigil and
the billing model, from the router settings window, so capturing the cache-waste savings never needs
hand-editing `appsettings.json` or environment variables. Extends the shipped tray proxy
(`003-tray-proxy`) and the shipped cache-hygiene engine (`004-cache-hygiene`).

## Overview

The router already measures prompt-cache waste by default and can remove three provably-safe causes
before forwarding a request (`004-cache-hygiene`). Those fixes are opt-in per class and, for a
standalone user with no OmnisVigil, the only way to turn them on today is to set the `CacheHygiene`
config section by hand and restart. That is friction on exactly the low-effort saving the feature
exists to capture.

This feature surfaces the controls in the tray app the user already runs (`003-tray-proxy`). The
router settings window gains a small cache-hygiene section: a per-class toggle for each of the three
fixes, a toggle to emit the content-free cache-waste figures to OmnisVigil, and the billing model.
The toggles write the same `CacheHygiene__*` settings the supervised proxy already reads, persisted
with the tray's other router settings and applied on the next router start, the same way changing the
port already restarts the router.

Control precedence is unchanged and must be visible. When OmnisVigil is serving a cache-fixes policy,
that policy is authoritative and overrides the local set, so the tray shows the fix toggles as managed
by OmnisVigil rather than pretending the local switch is in charge. With no Vigil policy, the local
toggles are what the router uses.

This feature does not change the router's cache-hygiene engine, the safety proofs, or the wire
contract. It is a desktop control surface over settings that already exist.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Turn a fix on without editing config (Priority: P1)

A standalone user running the local router from the tray opens the router settings window, ticks
"Normalise line endings", and saves. The router restarts with that fix enabled, and from then on a
request whose only difference from the last is line endings comes back as a cache read, with the
saving on the receipt.

**Why this priority**: This is the whole point, removing the hand-edit-and-restart barrier for the
opt-in saving. Everything else supports or qualifies it.

**Independent Test**: With the router running from the tray, enable one fix in the settings window,
save, and confirm the supervised router process starts with that fix in its enabled set and applies
it, matching what setting `CacheHygiene__EnabledFixes__0` by hand would do.

**Acceptance Scenarios**:

1. **Given** the router is running and no fix is enabled, **When** the user ticks a fix and saves,
   **Then** the router restarts and applies that fix, and the settings persist across app restarts.
2. **Given** a fix is enabled, **When** the user unticks it and saves, **Then** the router restarts
   with that fix removed and forwards requests unchanged for that cause.
3. **Given** the user changes a toggle but cancels, **When** the window closes, **Then** nothing
   changes and the router is not restarted.

### User Story 2 - See when OmnisVigil is in charge (Priority: P2)

A user whose router is connected to an OmnisVigil workspace that governs cache fixes opens the
settings window and sees the fix toggles reflect the Vigil-served set and marked as managed by
OmnisVigil, so they understand the local switches are not what is deciding.

**Why this priority**: Without it the local toggles would lie when a Vigil policy is authoritative,
which is the exact confusion this feature must avoid. It depends on US1's controls existing.

**Independent Test**: With a Vigil cache-fixes policy active, open the settings window and confirm the
fix toggles show the Vigil set, are labelled as governed by OmnisVigil, and editing them locally does
not override the policy.

**Acceptance Scenarios**:

1. **Given** OmnisVigil is serving a cache-fixes policy, **When** the user opens settings, **Then**
   each fix toggle shows the policy's state and is marked as managed by OmnisVigil.
2. **Given** no Vigil policy is set, **When** the user opens settings, **Then** the fix toggles are
   editable and reflect the local configuration.

### User Story 3 - Report the waste to a dashboard (Priority: P3)

A user who wants the cache-waste figures on their OmnisVigil dashboard ticks "Report cache-waste to
OmnisVigil" and saves; from then on the router emits the content-free block on its receipts.

**Why this priority**: Emit-to-Vigil is off by default and is the reason a connected dashboard often
shows no cache-waste data. Surfacing it removes a common dead end, but it is only useful to users who
also report to Vigil, so it ranks below the local-fix path.

**Independent Test**: Tick the emit toggle, save, and confirm the router starts with `EmitToVigil` on
and the next reported receipt carries the content-free cache-waste block.

**Acceptance Scenarios**:

1. **Given** emit is off, **When** the user ticks it and saves, **Then** the router restarts with
   emit on and reports the cache-waste block.
2. **Given** the user is not reporting to any OmnisVigil workspace, **When** they open settings,
   **Then** the emit toggle explains it only takes effect once a workspace is connected.

### Edge Cases

- Changing a toggle restarts the router; if the user is mid-request the restart follows the same
  drain-and-restart behaviour a port change already uses, and the window warns that saving restarts
  the router.
- A fix is enabled but the router skips it on a request it can't prove safe: this is the engine's
  existing measured-only behaviour and is not changed here; the toggle reflects intent, not a
  guarantee every request is transformed.
- Billing model is a deployment fact, not detected; the control defaults to pay-as-you-go and setting
  it to subscription switches the figures to shadow estimates.

## Requirements *(mandatory)*

- **FR-001**: The router settings window MUST present a per-class on/off control for each of the three
  shipped fixes (`line_ending`, `trailing_whitespace`, `tool_ordering`), off by default.
- **FR-002**: Saving the settings MUST persist the chosen fix set with the tray's other router
  settings and apply it to the supervised router on its next start, equivalently to setting
  `CacheHygiene__EnabledFixes` by hand.
- **FR-003**: The window MUST present a control to emit the content-free cache-waste figures to
  OmnisVigil (`EmitToVigil`), off by default, with a note that it only takes effect when a workspace
  is connected.
- **FR-004**: The window MUST present the billing model (pay-as-you-go or subscription), defaulting to
  pay-as-you-go, and make clear subscription shows figures as shadow estimates.
- **FR-005**: When OmnisVigil is serving a cache-fixes policy, the fix controls MUST show the
  policy's enabled set and be marked as managed by OmnisVigil, and local edits MUST NOT be presented
  as overriding the policy.
- **FR-006**: Saving a change that alters router configuration MUST restart the router using the same
  behaviour as an existing settings change (e.g. the port), and the window MUST tell the user that
  saving restarts the router.
- **FR-007**: The controls MUST NOT expose measurement as switchable in a way that implies it is off
  by default; measurement is on by default and content-free, so it is shown as status, not a barrier.
- **FR-008**: This feature MUST NOT change the cache-hygiene engine, its safety proofs, the receipt
  fields, or the wire contract; it only reads and writes the existing `CacheHygiene` settings.

## Success Criteria *(mandatory)*

- **SC-001**: A standalone user can enable a fix and have it apply to routed traffic without editing
  any file or environment variable.
- **SC-002**: With a fix enabled from the tray, a request that would have missed on that cause is
  reported by the provider as a cache read, and the receipt shows the saving, identical to enabling
  the fix by config.
- **SC-003**: When a Vigil cache-fixes policy is active, the tray shows the governed set and does not
  let a local edit silently diverge from it.
- **SC-004**: A user who has never reported to OmnisVigil can discover, from the settings window, why
  the dashboard shows no cache-waste data and turn on emit in one place.

## Key Entities

- **Cache-hygiene settings**: the tray-persisted view of the router's `CacheHygiene` options that the
  window edits, comprising the enabled fix set, the emit-to-Vigil flag and the billing model. Written
  to the supervised router as `CacheHygiene__*` on start.
- **Governance state**: whether OmnisVigil is currently authoritative over the fix set for this
  router, used to decide whether the fix controls are editable or shown as managed by OmnisVigil.

## Dependencies & Assumptions

- Builds on the shipped `003-tray-proxy` (the settings window, the supervised-router lifecycle, and
  the environment-injection path that already passes `OmnisVigil__*` to the router process) and the
  shipped `004-cache-hygiene` (`CacheHygieneOptions`, the `CacheHygiene` config section, and the
  Vigil `IFixPolicy` override).
- Assumes the established restart-on-save behaviour is acceptable for these settings; a later
  increment could make the options hot-reloadable to avoid the restart, but that is out of scope here.
- No secrets are involved, so no DPAPI protection is needed for these settings.
- Windows tray only, matching `003-tray-proxy`.

## Out of Scope

- Any change to the cache-hygiene detection or fix engine, safety proofs, or the frozen cache-waste
  wire contract.
- Hot-reloading the options without a router restart.
- A per-request or historical cache-waste view in the tray; the dashboard is OmnisVigil's job.
