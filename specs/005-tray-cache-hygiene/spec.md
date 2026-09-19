# Feature Specification: Persistent cache-hygiene settings in the tray

**Feature Branch**: `005-tray-cache-hygiene`

**Created**: 2026-09-19

**Status**: Draft

**Input**: Let a standalone tray user persist their cache-hygiene choices, the enabled fixes, whether to
report to OmnisVigil, and the billing model, from the router settings window, so the choices survive a
restart and never need hand-editing `appsettings.json` or environment variables. Extends the shipped
tray proxy (`003-tray-proxy`), the shipped cache-hygiene engine (`004-cache-hygiene`), and the shipped
tray cache-hygiene surface (`006-tray-cache-hygiene-display`).

## Relationship to feature 006 (read this first)

`006-tray-cache-hygiene-display` already shipped and shifts this spec's scope. In the tray popup, 006
already:

- shows the cache waste and recovered saving figures,
- lets the user toggle each fix on or off **at runtime** (`PUT /v1/cache-hygiene/fixes`), with no
  restart, and
- shows when an OmnisVigil policy governs the fixes (effective vs local state).

So the "turn a fix on without hand-editing config" problem is already solved for the live session. What
006 does **not** do is make those choices stick: its runtime toggle is in-memory and resets when the
router restarts, and it has no control for reporting-to-OmnisVigil or the billing model. This feature is
therefore narrowed to **persistence and the two missing controls**: a settings-window surface that
writes the choices to the router's configuration so they survive a restart, plus the emit-to-Vigil and
billing-model settings 006 has no control for. It does not duplicate 006's live popup display or its
runtime toggle.

## Overview

The router measures prompt-cache waste by default and can remove three provably-safe causes before
forwarding a request (`004-cache-hygiene`). Those fixes are opt-in per class. Feature 006 lets a user
flip them for the running session from the tray popup, but the choice is lost on the next restart, and
there is still no tray control for reporting the figures to OmnisVigil (off by default, the usual reason
a connected dashboard shows nothing) or for the billing model.

This feature adds a small cache-hygiene section to the router settings window that persists these
choices with the tray's other router settings and applies them to the supervised router on its next
start, the same way changing the port already restarts the router. The three settings are: the enabled
fix set, the emit-to-Vigil flag, and the billing model. Persisting the fix set makes a 006 runtime
toggle durable; the emit and billing controls are new.

Governance precedence is unchanged: when OmnisVigil serves a cache-fixes policy it is authoritative, so
the settings window shows the fix section as governed by OmnisVigil rather than pretending the local
setting is in charge, matching how 006's popup already presents it.

This feature does not change the cache-hygiene engine, the safety proofs, or the wire contract. It is a
persistence surface over settings that already exist.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Make a fix choice stick across restarts (Priority: P1)

A standalone user who has turned a fix on (in the 006 popup, or here) opens the router settings window,
ticks "Normalise line endings" so it is a saved default, and saves. The router restarts with that fix
enabled, and it stays enabled every time the router starts from then on, not just for the current
session.

**Why this priority**: 006 already gives instant, no-hand-edit toggling, but only for the running
session. Persistence is the missing half, so a chosen fix is not silently lost on the next restart.
Everything else supports or qualifies it.

**Independent Test**: Enable a fix in the settings window, save, restart the router, and confirm it
starts with that fix in its enabled set without any further action, matching what setting
`CacheHygiene__EnabledFixes__0` by hand would do.

**Acceptance Scenarios**:

1. **Given** no fix is a saved default, **When** the user ticks a fix and saves, **Then** the router
   restarts with it enabled and starts with it enabled on every later restart.
2. **Given** a fix is a saved default, **When** the user unticks it and saves, **Then** the router
   restarts without it and does not re-enable it on later restarts.
3. **Given** the user changes a toggle but cancels, **When** the window closes, **Then** nothing is
   persisted and the router is not restarted.

### User Story 2 - Report the waste to a dashboard (Priority: P2)

A user who wants the cache-waste figures on their OmnisVigil dashboard ticks "Report cache-waste to
OmnisVigil" and saves; from then on the router emits the content-free block on its receipts.

**Why this priority**: Emit-to-Vigil is off by default and is the usual reason a connected dashboard
shows no cache-waste data. There is no tray control for it at all today (006 does not have one), so this
removes a common dead end. It ranks below persistence because it only helps users who report to Vigil.

**Independent Test**: Tick the emit toggle, save, and confirm the router starts with `EmitToVigil` on
and the next reported receipt carries the content-free cache-waste block.

**Acceptance Scenarios**:

1. **Given** emit is off, **When** the user ticks it and saves, **Then** the router restarts with emit
   on and reports the cache-waste block, and it stays on across later restarts.
2. **Given** the user is not connected to any OmnisVigil workspace, **When** they open settings, **Then**
   the emit toggle explains it only takes effect once a workspace is connected.

### User Story 3 - Set the billing model (Priority: P3)

A user on a flat-rate subscription sets the billing model to subscription and saves, so the pounds are
shown as shadow estimates rather than as a bill, everywhere the figures appear (006's popup, the
receipt, and OmnisVigil).

**Why this priority**: Billing model is a deployment fact the router cannot detect, and it changes how
every figure is labelled. There is no tray control for it today. It ranks last because pay-as-you-go is
the correct default for most standalone users, so many never need to touch it.

**Independent Test**: Set the billing model to subscription, save, and confirm the router starts with
`Billing=Subscription` and the figures are marked as shadow estimates.

**Acceptance Scenarios**:

1. **Given** billing is pay-as-you-go, **When** the user sets it to subscription and saves, **Then** the
   router restarts with subscription billing and the figures are shown as shadow estimates.

### Edge Cases

- Saving a change restarts the router; if the user is mid-request the restart follows the same
  drain-and-restart behaviour a port change already uses, and the window warns that saving restarts the
  router.
- When an OmnisVigil policy governs the fixes, the settings window shows the fix section as managed by
  OmnisVigil (as 006's popup does) and a saved local fix set does not override the policy while it is
  active; it applies again if the policy is later withdrawn.
- Measurement is on by default and content-free; it is shown as status, never as a switch that could
  imply it is off by default.

## Requirements *(mandatory)*

- **FR-001**: The router settings window MUST present a per-class on/off control for each of the three
  shipped fixes (`line_ending`, `trailing_whitespace`, `tool_ordering`), and MUST persist the chosen set
  with the tray's other router settings so it is applied on every subsequent router start.
- **FR-002**: The persisted fix set MUST be applied to the supervised router on start, equivalently to
  setting `CacheHygiene__EnabledFixes` by hand, so a choice made here survives a restart (which the 006
  runtime toggle does not).
- **FR-003**: The window MUST present a control to emit the content-free cache-waste figures to
  OmnisVigil (`EmitToVigil`), off by default, persisted and applied on start, with a note that it only
  takes effect when a workspace is connected.
- **FR-004**: The window MUST present the billing model (pay-as-you-go or subscription), defaulting to
  pay-as-you-go, persisted and applied on start, and make clear subscription shows figures as shadow
  estimates.
- **FR-005**: When OmnisVigil is serving a cache-fixes policy, the fix controls MUST show the policy's
  enabled set and be marked as managed by OmnisVigil, consistent with 006's popup, and a saved local set
  MUST NOT be presented as overriding the policy.
- **FR-006**: Saving a change that alters router configuration MUST restart the router using the same
  behaviour as an existing settings change (e.g. the port), and the window MUST tell the user that
  saving restarts the router.
- **FR-007**: Measurement MUST be shown as status, not as a switch implying it is off by default;
  measurement is on by default and content-free.
- **FR-008**: This feature MUST NOT change the cache-hygiene engine, its safety proofs, the receipt
  fields, or the wire contract; it only reads and writes the existing `CacheHygiene` settings.
- **FR-009**: This feature MUST NOT duplicate feature 006's live popup display or its runtime fix
  toggle; the settings window is the durable-configuration surface, the popup is the live one.

## Success Criteria *(mandatory)*

- **SC-001**: A fix enabled in the settings window is still enabled after the router is stopped and
  started again, with no further action and no file or environment edit.
- **SC-002**: With a fix persisted from the tray, a request that would have missed on that cause is
  reported by the provider as a cache read and the receipt shows the saving, identical to enabling the
  fix by config.
- **SC-003**: A user who has never reported to OmnisVigil can discover, from the settings window, why the
  dashboard shows no cache-waste data and turn on emit in one place, and it stays on across restarts.
- **SC-004**: A subscription user can set the billing model once and have every figure labelled a shadow
  estimate thereafter, across restarts.
- **SC-005**: When a Vigil cache-fixes policy is active, the settings window shows the governed set and a
  saved local edit does not silently diverge from it.

## Key Entities

- **Persisted cache-hygiene settings**: the tray-persisted view of the router's `CacheHygiene` options
  the window edits, comprising the enabled fix set, the emit-to-Vigil flag and the billing model.
  Written to the supervised router as `CacheHygiene__*` on start.
- **Governance state**: whether OmnisVigil is currently authoritative over the fix set for this router,
  used to decide whether the fix controls are editable or shown as managed by OmnisVigil (the same state
  006 surfaces in the popup).

## Dependencies & Assumptions

- Builds on `003-tray-proxy` (the settings window, the supervised-router lifecycle, and the
  environment-injection path that already passes `OmnisVigil__*` to the router process),
  `004-cache-hygiene` (`CacheHygieneOptions`, the `CacheHygiene` config section, the `Billing` and
  `EmitToVigil` options, and the Vigil `IFixPolicy` override), and `006-tray-cache-hygiene-display` (the
  live popup, the runtime fixes endpoint, and the governance display this reuses rather than reinvents).
- Assumes restart-on-save is acceptable for these settings; a later increment could make the options
  hot-reloadable to avoid the restart, but that is out of scope here. Note that 006 already provides the
  no-restart path for the fixes at runtime; this feature is specifically the durable one.
- No secrets are involved, so no DPAPI protection is needed for these settings.
- Windows tray only, matching `003-tray-proxy`.

## Out of Scope

- The live popup display of the figures and the runtime (no-restart) fix toggle: both shipped in
  `006-tray-cache-hygiene-display`.
- Any change to the cache-hygiene detection or fix engine, the safety proofs, or the frozen cache-waste
  wire contract.
- Hot-reloading the persisted options without a router restart.
- A per-request or historical cache-waste view in the tray; the dashboard is OmnisVigil's job.
