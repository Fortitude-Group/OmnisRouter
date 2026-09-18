# Feature Specification: Cache hygiene — a measured cost lever

**Feature Branch**: `004-cache-hygiene`

**Created**: 2026-09-18

**Status**: Draft

**Input**: Approved design at `docs/superpowers/specs/2026-09-18-cache-hygiene-design.md`. Give OmnisRouter a second, routing-independent way to cut the bill: measure prompt-cache waste in the request path from bytes it already holds, optionally remove a fixable cause before forwarding, show the saving in the receipt, and report content-free cache-waste fields to OmnisVigil. Anthropic-first.

## Overview

OmnisRouter already lowers the price per token by routing to the cheapest capable model. Cache hygiene lowers how many tokens you pay the cache **write** premium for, on the same model, with no change to output quality. Prompt caching charges a write premium and only refunds it as a cheap read when the prefix comes back byte-for-byte; one stray carriage return, a moved timestamp, or a reordered tool list throws the cached prefix away and you pay to write it again. Because the router sits in the request path, it holds the exact bytes and gets back the real usage and the real cache result, so it can turn that waste into a measured pound figure and, where a fix is provably safe and the operator has opted in, remove the cause before it costs anything. Measurement is free and content-free, so it is on by default; anything that changes the bytes the user sent is off until the operator turns it on.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - See what cache misses are costing you (Priority: P1)

An operator running the router sees, per request, why a cached prefix missed, how many tokens were recomputed at write price, and what that was worth — grounded in the real usage the provider reported. They learn where the waste is without changing anything they send.

**Why this priority**: Measurement alone delivers the value — you find out where the money is going — with zero mutation and zero risk. It is the minimum viable slice: the whole feature can ship as measurement-only and still be worth having.

**Independent Test**: Send two near-identical requests that differ only by a carriage return in the cached prefix. The receipt on the second shows a line-ending-drift cause, the recomputed token count, and the avoidable pound figure, computed from the real cache read/write usage — no bytes leave the machine.

**Acceptance Scenarios**:

1. **Given** a routed request whose cached prefix diverges from the previous request in its lineage, **When** the response returns, **Then** the receipt reports the cause of the miss, the recomputed tokens, and the avoidable pounds.
2. **Given** the miss was caused by a genuine content edit or a deliberate model change, **When** the receipt is produced, **Then** the miss is marked unavoidable and no avoidable pounds are attributed.
3. **Given** measurement is running, **When** any record is sent onward from the machine, **Then** it contains no prompt bytes, no diff, and no keys.
4. **Given** the first request in a lineage (nothing to compare against), **When** it is processed, **Then** no waste figure is produced and the request is unaffected.

---

### User Story 2 - Never let this cost a request (Priority: P1)

The operator is guaranteed that measuring or fixing cache waste can never fail, block, or slow a request. A request is served whether or not any of this ran.

**Why this priority**: This is the invariant that makes the feature safe to leave on. Without it, a cost-saving convenience could become an availability risk. It gates everything else.

**Independent Test**: Inject a fault into the analysis and into a fix so each throws; confirm every request is still served with a correct response, no added failures, and no measurable latency added to the request's hot path.

**Acceptance Scenarios**:

1. **Given** the analysis throws or exceeds its time budget, **When** the request is processed, **Then** the request is served normally and simply carries no cache fields.
2. **Given** a fix throws or cannot prove it is safe, **When** the request is processed, **Then** the fix is skipped and the original request is forwarded unchanged.
3. **Given** measurement is on for every request, **When** hot-path latency is measured with and without the feature, **Then** there is no measurable added latency on the request path.

---

### User Story 3 - Recover the waste automatically (Priority: P2)

The operator turns on a fix class. From then on the router removes that fixable cause from the request before forwarding, so the miss becomes a cache read, and the receipt shows the measured tokens and pounds saved. Fixes are off until turned on, one class at a time.

**Why this priority**: This is the money-saver, but it changes the bytes the user sent, so it depends on the measurement (P1) proving the saving first and on the fail-open guarantee (P1). It is opt-in by design.

**Independent Test**: With line-ending normalisation enabled, send a request whose prefix differs from the previous one only by line endings; confirm the provider now reports a cache read instead of a write, the receipt shows the tokens and pounds saved, and the response is equivalent to the un-normalised one.

**Acceptance Scenarios**:

1. **Given** a fix class is enabled and a request has that fixable cause, **When** the request is forwarded, **Then** the cause is removed, the provider reports a read, and the receipt shows the measured saving with a before/after.
2. **Given** a fix class is disabled (the default), **When** a request has that cause, **Then** the request is forwarded unchanged and the receipt shows the avoidable waste as a saving the operator could capture by enabling the fix.
3. **Given** a fix is enabled but cannot prove it preserves meaning on a particular request, **When** that request is processed, **Then** the fix is skipped and the miss is reported as measured-only.
4. **Given** the same request is served with and without a fix, **When** the responses are compared, **Then** they are equivalent.

---

### User Story 4 - Report the saving to the team, content-free (Priority: P2)

The content-free cache-waste figures ride the existing receipts-up record to OmnisVigil, which aggregates them into a team headline. No prompt content is involved.

**Why this priority**: Team-level visibility is where the operator acts on the numbers, but it depends on measurement (P1) producing the fields and is additive to the existing reporting path.

**Independent Test**: With reporting on, confirm the outbound record carries the content-free cache-waste block and that a content-free test over outbound records finds no prompt bytes, diff, or keys.

**Acceptance Scenarios**:

1. **Given** a request was analysed, **When** its record is reported, **Then** the record carries the content-free cache-waste fields (cause class, avoidable, recomputed tokens, avoidable pounds, fix applied, tokens and pounds saved, pricing version, FX date, rate, shadow-price flag) and nothing content-bearing.
2. **Given** a request was not analysed (analysis off, or non-cacheable), **When** its record is reported, **Then** the record is identical to what is reported today.

---

### User Story 5 - Trust the pounds (Priority: P3)

Every pound figure the operator sees is stamped with the pricing version and the FX date it was computed with, and is marked measured or a shadow estimate. On a flat-rate subscription it is never presented as money owed.

**Why this priority**: It makes the numbers defensible, but the numbers exist and are useful before this polish; it is the correctness of the money framing.

**Independent Test**: Compare a pay-as-you-go request and a subscription request; confirm the PAYG figures are marked measured, the subscription figures are marked shadow estimates, and both carry a pricing version and FX date; confirm no subscription figure is ever labelled as a bill.

**Acceptance Scenarios**:

1. **Given** any pound figure is produced, **When** it is shown or reported, **Then** it carries the pricing version and the FX date used.
2. **Given** the request is on a flat-rate subscription, **When** a pound figure is produced, **Then** it is labelled a shadow estimate and is never presented as money owed.

---

### Edge Cases

- **First request in a lineage**: nothing to diff against, so no waste figure is produced; the request is unaffected.
- **Non-cacheable request** (no cache breakpoint present): no analysis runs; the record is unchanged.
- **Analysis or fix fault / timeout**: the request is served regardless; cache fields are simply absent (fail-open).
- **Fix cannot prove safety** on a given request: it is skipped; measurement still reports the avoidable waste.
- **Streaming response**: the real usage arrives on the terminal event; the analysis runs after it, off the hot path.
- **Subscription with no per-token bill**: every pound figure is a labelled shadow estimate.
- **A genuine edit, a deliberate model change, or a system-prompt change**: the miss is correct cache behaviour, marked unavoidable, and attributed no avoidable waste.

## Requirements *(mandatory)*

### Functional Requirements

**Measurement (default-on, content-free)**

- **FR-001**: For each routed cacheable request, the system MUST compute a cache-hygiene analysis over the bytes it forwards, without blocking or failing the request.
- **FR-002**: When a cached prefix misses, the analysis MUST identify the cause from a defined set: line-ending drift, trailing whitespace, a volatile header or timestamp before the cache breakpoint, an unsorted or reordered tool list, a genuine content edit, a model change, or a system-prompt change.
- **FR-003**: The analysis MUST use the real usage reported by the provider (measured, not predicted), including the actual cache read and write token counts.
- **FR-004**: The analysis MUST classify whether a miss was avoidable; a genuine edit, a deliberate model change, and a system-prompt change MUST be classed unavoidable and attributed no avoidable waste.
- **FR-005**: Measurement MUST run on by default and MUST be content-free — no prompt bytes, diff, or keys leave the machine as a result of it.

**Receipt**

- **FR-006**: The routing receipt MUST include a cache section reporting, for the request: the cause, the recomputed tokens, the avoidable pounds, and — where a fix ran — the tokens and pounds saved with a before/after.
- **FR-007**: Byte-level before/after detail MUST appear only in the synchronous receipt returned to the calling client (who owns their content), never in any record sent onward.

**Fixes (opt-in, provably safe)**

- **FR-008**: The system MUST be able to normalise a request before forwarding to remove a fixable cause: line-ending normalisation, trailing-whitespace normalisation, and canonical ordering of machine-generated tool definitions.
- **FR-009**: Every fix MUST be opt-in per class and off by default; a fix MUST be applied only where its meaning-preservation can be shown for that request, and skipped otherwise.
- **FR-010**: A fix MUST NOT change the response — the same request served with and without the fix MUST return an equivalent response.
- **FR-011**: When an enabled fix removes a cause and turns a cache write into a read, the receipt MUST show the measured tokens and pounds saved.
- **FR-012**: Fix classes MUST be controllable by an OmnisVigil policy, the way caps and kill-state already are.

**Content-free reporting**

- **FR-013**: The content-free cache-waste fields MUST extend the existing receipts-up record: cause class, avoidable flag, recomputed tokens, avoidable pounds, fix applied (or none), tokens and pounds saved, pricing version, FX date, USD→GBP rate, and a shadow-price flag. This field set MUST be frozen in the ingest contract (schema and contract document) as a deliverable of this feature.
- **FR-014**: The outbound record MUST remain content-free, proven by an automated test over outbound records that finds no prompt bytes, diff, or keys.
- **FR-015**: The cache-waste block MUST be optional on the record: a request with no cache analysis (analysis off, or non-cacheable) MUST produce a record identical to today's.

**Trustworthy pounds**

- **FR-016**: Every pound figure MUST be stamped with the pricing version and the FX date used, and marked measured or shadow-price.
- **FR-017**: On a flat-rate subscription a pound figure MUST be a labelled shadow estimate and MUST NEVER be presented as money owed.
- **FR-018**: The USD→GBP rate MUST come from the dated, immutable pricing snapshot — reproducible, with no live fetch.

**Never cost a request**

- **FR-019**: Cache analysis and any fix MUST NEVER block, delay a request past a stated small budget, or fail it; a request MUST be served whether or not analysis or a fix ran.
- **FR-020**: Any analysis that would add latency MUST run off the request's hot path.

**Scope**

- **FR-021**: This release applies to Anthropic-routed requests, which carry an explicit cache breakpoint; OpenAI and Gemini caching are out of scope here.
- **FR-022**: The feature MUST NOT change routing decisions or the wire translation; it is additive to the router.

### Key Entities

- **Cache-hygiene analysis result**: for one request — the cause class, whether the miss was avoidable, the recomputed tokens, the avoidable pounds, and, where a fix ran, the saved tokens and pounds. Reconciled to a versioned result contract shared with the sibling analysis tool so the numbers agree.
- **Cache-waste record fields**: the content-free block that extends the receipts-up record (see FR-013). Scalars and labels only.
- **Fix class**: one of line-ending normalisation, trailing-whitespace normalisation, tool-definition ordering — each with an enabled/disabled state, off by default, policy-controllable.
- **Pricing / FX stamp**: the pricing version, FX date, USD→GBP rate, and the measured-or-shadow marker attached to every pound figure.
- **Lineage**: the identity that groups consecutive requests sharing a cached prefix, against which the current prefix is diffed.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: For a request whose prefix would have missed on a fixable cause, with the corresponding fix enabled, the provider reports a cache read instead of a write, and the receipt shows the measured tokens and pounds saved.
- **SC-002**: No prompt bytes, diff, or key ever appears in a record sent onward, verified by a content-free test over outbound records.
- **SC-003**: Zero added request failures and no measurable latency added to the request's hot path, verified by fault injection and a latency comparison with the feature on and off.
- **SC-004**: A safe fix is response-transparent: the same request served with and without normalisation returns an equivalent response.
- **SC-005**: Every pound figure carries its pricing version and FX date and is marked measured or shadow-price; no subscription figure is ever presented as a bill.
- **SC-006**: Measurement is on with no configuration; every byte-mutating fix is off until explicitly enabled, one class at a time.
- **SC-007**: A request with cache analysis disabled or a non-cacheable request produces an outbound record identical to today's.

## Assumptions

- **Anthropic-first.** This release covers Anthropic's explicit cache breakpoints. OpenAI automatic prefix caching and Gemini context caching are a separate later feature (Gemini also needs its cache usage surfaced first).
- **All mutations opt-in; measurement default-on.** No fix, including line-ending normalisation, changes the user's bytes without the operator enabling it per class.
- **The byte analysis runs in the router.** Content-free-out forces the per-request analysis onto the machine that holds the bytes; it is reconciled against the sibling tool's versioned result contract so the two agree. The team dashboard that aggregates the reported figures is a separate specification in the OmnisVigil repository; this feature freezes the content-free contract that dashboard consumes.
- **Nothing is kept on disk.** The previous prefix is held only in memory, long enough to diff the next request, then evicted; the byte-level detail rides back only in the synchronous caller receipt. An operator may opt into a local diagnostic log.
- **FX lives in the dated pricing snapshot.** The USD→GBP rate and its date are part of the immutable, dated pricing snapshot, alongside the prices — reproducible, no network dependency.
- **Additive.** The feature reuses the existing request path, receipt, pricing snapshot, and receipts-up reporting; it does not change routing or wire translation.
- **The saving is shown, not asserted.** Every figure is grounded in the provider's real usage and the dated pricing; the static prediction is used only for the offline case with no traffic yet, and is labelled a prediction.
