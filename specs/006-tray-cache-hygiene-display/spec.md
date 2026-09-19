# Feature Specification: Cache hygiene in the tray

**Feature Branch**: `006-tray-cache-hygiene-display`

**Created**: 2026-09-19

**Status**: Draft

**Input**: User description: "006-tray-cache-hygiene-display"

## User Scenarios & Testing *(mandatory)*

### User Story 1 - See what caching is costing, at a glance (Priority: P1)

The person running OmnisRouter from the tray opens the tray popup and, alongside the mode, state and
today's totals it already shows, sees a short cache-hygiene summary: how much avoidable prompt-cache
waste has built up and how much has been recovered. Each figure is labelled with what it is and the
period it covers. They get this on their own machine, without opening the web dashboard or signing up
to the paid control plane.

**Why this priority**: The tray is the only surface a self-hoster sees by default. Feature 004 already
measures cache waste on every routed request and puts it on the receipt headers and the OmnisVigil
receipt, but a self-hoster with no OmnisVigil never sees it. Surfacing the two headline numbers in the
popup is the smallest slice that turns the measurement into something a person actually notices and
acts on. It stands alone as a shippable improvement.

**Independent Test**: Run the router under the tray, drive traffic that includes at least one avoidable
cache miss and at least one miss recovered by an enabled fix, open the tray popup, and confirm the
avoidable-waste and recovered-saving figures appear and reconcile with the router's own receipts.

**Acceptance Scenarios**:

1. **Given** the router is running under the tray and has served routed traffic with at least one
   avoidable cache miss, **When** the user opens the tray popup, **Then** it shows the avoidable cache
   waste for the period and the amount recovered by fixes, each with a plain-language label.
2. **Given** the router has just started and no cache analysis has run yet, **When** the user opens the
   popup, **Then** the cache-hygiene lines read as zero or "nothing measured yet" rather than blank,
   an error, or a stale figure.
3. **Given** the figures are shown, **When** the user reads them, **Then** each figure carries its unit
   and the period it covers, the pounds state their pricing and FX basis and whether they are a real
   bill or a subscription shadow figure (constitution Principle XII), and no prompt text, request
   content, or key ever appears (content-free).

---

### User Story 2 - Turn recovery on without editing a config file (Priority: P2)

From the tray, the user turns the byte-mutating fixes (line-ending, trailing-whitespace, tool-ordering)
on or off per class and sees the effect reflected in the next popup summary, without hand-editing a
configuration file or restarting anything by hand.

**Why this priority**: Feature 004 ships the fixes off by default and recovers real money only once
they are enabled. A self-hoster's only route today is editing the `CacheHygiene` config section. A tray
control removes that friction for exactly the person who has no OmnisVigil policy to do it for them. It
depends on US1 being present to show the resulting saving, so it comes second.

**Scope decision**: The tray both displays cache hygiene and lets the user toggle the fixes. The toggle
is a write action that changes how requests are forwarded, so it is a deliberate, per-class control that
respects an OmnisVigil policy override where one is present.

**Independent Test**: With US1 in place, enable a fix from the tray, drive the same repeated-prefix
traffic that previously missed, and confirm the popup's recovered-saving figure rises and the miss it
addresses stops being counted as waste.

**Acceptance Scenarios**:

1. **Given** all fixes are off, **When** the user enables the line-ending fix from the tray, **Then**
   subsequent recoverable misses are recovered and the popup's recovered-saving figure reflects it.
2. **Given** the OmnisVigil control plane is connected and its policy sets the enabled fixes, **When**
   the user views the tray, **Then** the tray shows the effective state (policy wins where present) and
   does not silently present a local toggle as authoritative when the policy overrides it.

---

### User Story 3 - Cover the watched subscription, not just routed traffic (Priority: P3)

A user running the tray in collect mode (watching a Claude subscription rather than routing) sees the
cache-hygiene cost of that observed subscription workload, expressed as a shadow figure, so they learn
what prompt-cache inefficiency is notionally costing even though a subscription is not billed per token.

**Why this priority**: The tray was built first as a subscription watcher, so this is the reading that
serves that original user. It is last because it needs cache-waste measurement over observed
subscription usage, which feature 004 does not perform today (004 measures on the routed request path
only), so it is the largest and least certain slice.

**Scope decision**: Scope covers both, phased. Routed-traffic hygiene ships first (User Story 1, reusing
feature 004). Cache hygiene for the watched subscription in collect mode is in scope as this P3 slice,
and needs new cache-waste measurement over observed subscription usage, shown shadow-priced.

**Independent Test**: With the tray in collect mode against a subscription workload that reuses a cache
prefix, confirm the popup shows a shadow-priced cache-waste figure clearly labelled as an estimate, not
a bill.

**Acceptance Scenarios**:

1. **Given** the tray is in collect mode watching a subscription, **When** cache misses occur in the
   observed workload, **Then** the popup shows the waste as a shadow figure marked "estimate, not a
   bill".

---

### Edge Cases

- The router is not running (tray shows collect-only, or the supervised router is stopped or errored):
  the cache-hygiene section reads "not measuring" rather than showing a stale or misleading figure.
- Measurement is turned off in config: the section says so plainly rather than showing zero as if it
  had measured and found nothing.
- The period boundary rolls over (e.g. "today" ticks past midnight) while the popup is open: reopening
  shows the new period; the labels always say which period a figure covers.
- Very large or very small money values: figures are formatted so they stay readable and never render
  as raw high-precision decimals.
- OmnisVigil policy toggles the fixes after the tray has shown a local state: the next popup reflects
  the effective (policy-resolved) state.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The tray popup MUST show, in addition to what it shows today, a cache-hygiene summary
  with at least two figures: avoidable cache waste incurred and the amount recovered by fixes, for a
  stated period.
- **FR-002**: Each figure MUST be labelled with what it is and the period it covers, and MUST never be
  shown without its unit (constitution Principle XII).
- **FR-003**: Every monetary figure MUST state the pricing snapshot and FX date behind it and MUST be
  marked as a real bill or a subscription shadow figure, consistent with feature 004's receipts.
- **FR-004**: The summary MUST be content-free: it MUST NOT display or transmit any prompt text,
  request content, diff, or key. It presents scalars and labels only.
- **FR-005**: When no cache analysis has run (fresh start, no qualifying traffic, or measurement
  disabled), the tray MUST show an explicit "nothing measured yet" or "measurement off" state rather
  than a blank, an error, or a stale figure.
- **FR-006**: The figures the tray shows MUST reconcile with the router's own cache-hygiene output for
  the same traffic and period (the tray reports the router's numbers, it does not invent its own).
- **FR-007**: The cache-hygiene summary MUST degrade safely: if the figures cannot be obtained (router
  stopped, unreachable, or errored) the tray MUST show a clear "not measuring" state and MUST NOT
  block, hang, or crash the popup or the rest of the tray.
- **FR-008**: The tray MUST make the fuller breakdown reachable (it already links to the web dashboard),
  so the popup can stay a headline while detail lives in the dashboard.
- **FR-009**: The user MUST be able to enable or disable each byte-mutating fix class from the tray, and
  the change MUST take effect for subsequent requests without a manual config edit.
- **FR-010**: Where the OmnisVigil control plane sets the enabled fixes, the tray MUST show the
  effective state and MUST NOT present a local control as authoritative when the policy overrides it.
- **FR-011**: The cache-hygiene surface MUST cover the watched subscription workload in collect mode,
  shown as a shadow figure clearly marked "estimate, not a bill". This is the P3 slice and requires
  cache-waste measurement over observed subscription usage.

### Key Entities *(include if feature involves data)*

- **Cache-hygiene summary**: the small set of figures the tray shows for a period, derived from the
  router's per-request cache-hygiene results: avoidable waste incurred, amount recovered, the period,
  and the pricing/FX/shadow basis. Content-free.
- **Fix enablement state**: which byte-mutating fix classes are currently active, and whether that state
  comes from local configuration or an OmnisVigil policy.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A self-hoster can see, within 5 seconds of opening the tray popup and with no other tool
  open, how much avoidable cache waste has built up and how much has been recovered.
- **SC-002**: 100% of monetary figures shown carry their unit, period, pricing/FX basis, and
  bill-versus-shadow marking; a review of the surface finds no bare or unexplained number.
- **SC-003**: No prompt content, request text, diff, or key ever appears in the tray surface, verified
  by inspection of everything the surface can render.
- **SC-004**: The figures the tray shows reconcile with the router's receipts for the same period to
  the penny in at least 20 sampled requests.
- **SC-005**: When the router is stopped or measurement is off, 100% of popup opens show a clear
  not-measuring or measurement-off state and none show a stale or blank figure.

## Assumptions

- The tray extends its existing popup (which already shows mode, state, last-posted and today's totals
  and links to the full dashboard); this feature adds a cache-hygiene section to that popup rather than
  introducing a separate window.
- The default period mirrors the popup's existing "today" line plus a since-start view; the exact
  window is a design detail, not a scope decision, and will be settled in planning.
- Routed-traffic cache hygiene (User Story 1) reuses feature 004's existing measurement on the router
  the tray supervises; no new measurement engine is needed for the P1 slice.
- The web dashboard remains the home for the full per-cause breakdown and history; the tray popup stays
  a headline.
- Content-free and "explain every number" are hard constraints inherited from the constitution and
  feature 004, not options.
- This feature is Windows tray only. Beyond feature 004's existing request-path behaviour it adds a
  tray-driven fix toggle (User Story 2) and, for the P3 slice, cache-waste measurement over observed
  subscription usage in collect mode (User Story 3).
