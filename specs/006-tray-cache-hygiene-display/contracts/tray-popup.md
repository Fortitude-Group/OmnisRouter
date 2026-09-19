# Contract: tray popup cache-hygiene surface

The UI contract for the addition to `StatusPopup`. This is what the user sees and can do; the wire
shapes behind it are the two endpoint contracts in this folder.

## Display (all three user stories)

Below the popup's existing mode / state / last-posted / today lines, and above the "Full dashboard →"
link, a cache-hygiene section shows:

- **A waste line**: avoidable cache waste for the period, in GBP, e.g. "Cache waste today: £0.018
  (avoidable)". Marked as a shadow estimate when `shadow_price` is true.
- **A recovered line**: saving recovered by fixes, in GBP, e.g. "Recovered by fixes: £0.011".
- **A basis note** (discoverable, not necessarily inline): pricing snapshot date, FX date, and
  bill-versus-shadow, so every number is explained (Principle XII).
- **States**: "nothing measured yet" before any analysis; "measurement off" when disabled; "not
  measuring" when the router is stopped or unreachable. Never blank, stale, or an error dialog.

## Control (User Story 2)

- A per-class toggle for `line_ending`, `trailing_whitespace`, `tool_ordering`, reflecting `effective`
  state.
- When `policy_overrides` is true, the toggles show the policy-driven state and indicate the OmnisVigil
  policy is in control, so a local toggle is not silently presented as authoritative.
- Toggling calls `PUT /v1/cache-hygiene/fixes` and re-reads the resulting state.

## Collect mode (User Story 3)

- When the tray is watching a subscription (collect mode), the section shows the gross observed
  cache-write shadow cost, labelled "estimate, not a bill", with no cause breakdown and no avoidable/
  unavoidable split (the collect path is content-free and cannot classify).

## Rules

- **Content-free.** The surface renders only figures and labels. No prompt text, diff, or key can appear.
- **Fail-open.** Any failure to fetch figures or set fixes degrades to a clear state line and never
  blocks, hangs, or crashes the popup or the tray.
- **Reuses the poll.** The section refreshes on the same cadence the popup already updates status; opening
  the popup shows current figures within 5 seconds (SC-001).
