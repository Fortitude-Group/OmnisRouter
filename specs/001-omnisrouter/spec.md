# Feature Specification: OmnisRouter v1 — Transparent LLM Routing Proxy

**Feature Branch**: `001-omnisrouter`

**Created**: 2026-08-19

**Status**: Draft

**Input**: Design spec `docs/superpowers/specs/2026-08-18-omnisrouter-design.md` (Approved 2026-08-18) — "open-core, drop-in LLM proxy that routes each request to the cheapest *capable* model with fully open, reproducible routing and per-request routing receipts."

## User Scenarios & Testing *(mandatory)*

The actors are **developers** and **operators** who self-host OmnisRouter to sit between their existing LLM clients (Claude Code, Codex, Cursor, opencode, custom apps) and the model providers. They adopt it to spend less per request **without giving up trust** — they can always see, and independently reproduce, why each request went where it did.

### User Story 1 - Drop-in cost routing (Priority: P1)

A developer already using an LLM client changes only the base URL to point at their self-hosted OmnisRouter, supplies their own provider API key, and keeps working. Each request is automatically sent to the cheapest model capable of handling it, and the response comes back in the exact format the client sent — the client is unchanged and unaware anything routed.

**Why this priority**: This is the core value and the minimum viable product. If a single request format can be routed to a cheaper capable model and returned faithfully, the product delivers savings and everything else builds on it.

**Independent Test**: Point a client that speaks one supported request format at the router with a valid provider key; send a mix of simple and hard prompts; confirm each response is well-formed in the original format and that simple prompts were served by a cheaper model than hard ones, with a measurable aggregate cost reduction versus always using the strongest model.

**Acceptance Scenarios**:

1. **Given** a client configured only with a new base URL and a valid provider key, **When** it sends a request in a supported format, **Then** the router selects a capable model, calls the upstream provider, and returns a response in the client's original format with no client-side changes.
2. **Given** a batch of low-complexity requests, **When** they are routed, **Then** they are predominantly served by lower-cost models and the aggregate spend is lower than serving them all with the strongest model, at equivalent output quality.
3. **Given** a request the router is not confident a cheap model can handle, **When** it is routed, **Then** the router escalates to a stronger model rather than risk a poor answer, and the escalation is recorded.

---

### User Story 2 - Routing receipts and transparency (Priority: P2)

For every response, the operator can see exactly why a model was chosen — the chosen model, the router's confidence, the alternatives that were considered, and the estimated cost difference versus the strongest option. They can also ask the router "what would you do with this request?" without spending money on an upstream call, and export a full log of past decisions.

**Why this priority**: Verifiable trust is the product's entire wedge against black-box competitors. Receipts turn "trust us, we save you money" into an inspectable, per-request claim. It is second only to routing itself because without it the product is just another opaque router.

**Independent Test**: Send any routed request and confirm the response carries receipt information (chosen model, confidence, alternatives, cost delta); call the decision-only endpoint with a request body and confirm it returns the same decision without contacting an upstream provider; export the decision log and confirm every prior routed request appears with its decision fields.

**Acceptance Scenarios**:

1. **Given** any routed response, **When** the operator inspects it, **Then** it exposes the chosen model, confidence, cluster/intent, policy version, estimated cost, and cost delta versus the strongest candidate.
2. **Given** a request body, **When** the operator asks for a routing decision only, **Then** the router returns the full decision (chosen model, confidence, ranked alternatives with estimated cost deltas) without calling any upstream provider or incurring cost.
3. **Given** a period of routed traffic, **When** the operator exports the decision log, **Then** every routed request is present with its decision, timestamp, and policy version.

---

### User Story 3 - Reproducible, verifiable routing (Priority: P3)

A skeptical developer wants to confirm the routing is honest. They rebuild the routing model themselves from public data using the published build job and get the same routing model the product ships. For any tagged release, they can retrieve the benchmark run that backs its savings claim and re-verify it.

**Why this priority**: Reproducibility is what makes the transparency claim credible rather than cosmetic — it is the durable differentiator. It is P3 because the product is usable and trustworthy-in-the-moment with P1+P2; reproducibility hardens the trust for the audience that checks.

**Independent Test**: Run the documented build job against the public datasets and confirm it produces a routing model byte-identical (or decision-equivalent within a stated tolerance) to the shipped one; for a tagged release, open its published benchmark artifact and confirm the stated savings-at-parity figure is reproducible from it.

**Acceptance Scenarios**:

1. **Given** the published build job and public datasets, **When** a third party runs it, **Then** it produces the same versioned routing model (centroids + policy table) the release ships, and that version identifier appears in the release's receipts.
2. **Given** a tagged release, **When** a developer opens its published benchmark frontier, **Then** the "saves X% at parity" claim is backed by a re-runnable artifact, not marketing copy.
3. **Given** committed routing-model data and fixed inputs, **When** the routing decision is evaluated, **Then** the decisions are stable across runs (guarding against silent routing drift).

---

### User Story 4 - Faithful feature preservation across formats and providers (Priority: P2)

A developer relies on streaming, tool/function calling, vision inputs, prompt caching, and extended thinking. When OmnisRouter routes their request — possibly to a model on a *different* provider than the request format implies — none of these capabilities are silently dropped, and multi-turn conversations keep their prompt caches warm.

**Why this priority**: Real clients depend on these features; dropping them makes the router unusable for the exact power users it targets. It shares P2 because faithful translation is table-stakes for adoption alongside receipts.

**Independent Test**: For each supported request format, send streaming requests, tool-call requests, and vision requests that route to a model on another provider; confirm the streamed response, tool calls, and vision handling arrive correctly in the client's original format; confirm cache-control and extended-thinking semantics survive the round trip; run a multi-turn session and confirm turns stay pinned to keep the cache warm unless the request's intent changes materially.

**Acceptance Scenarios**:

1. **Given** a streaming request in any supported format routed to any candidate model, **When** the upstream streams tokens, **Then** the client receives a correct incremental stream in its original format.
2. **Given** a request using tools, vision, prompt caching, or extended thinking, **When** it is routed across formats/providers, **Then** those capabilities are preserved end-to-end rather than dropped.
3. **Given** a multi-turn session, **When** subsequent turns keep the same intent, **Then** they are pinned to the same upstream to preserve cache economics, and any pinning or un-pinning decision is visible in the receipt.

---

### User Story 5 - Secure, single-process self-host with BYOK (Priority: P2)

An operator runs the entire router as a single self-contained process with a local database, supplying their own provider keys. Those keys are encrypted at rest, and prompt content never leaves the operator's infrastructure except to the chosen upstream provider. No keys or prompt content appear in logs or receipts.

**Why this priority**: The v1 posture is developer, bottom-up, self-hosted open core. Easy, safe self-hosting with the operator's own keys is a precondition for anyone running it at all.

**Independent Test**: Bring the router up as a single process with a local database and a provided key; confirm the key is stored encrypted (not readable in plaintext at rest) and never appears in logs or receipts; confirm prompt content is sent only to the chosen upstream provider and to no other network destination.

**Acceptance Scenarios**:

1. **Given** an operator-supplied provider key, **When** it is stored, **Then** it is encrypted at rest and is never written to logs, receipts, or decision exports in plaintext.
2. **Given** a running router, **When** it processes a request, **Then** prompt content is transmitted only to the chosen upstream provider and nowhere else.
3. **Given** a fresh machine, **When** the operator starts the router, **Then** it runs as a single process with a local database and no external service dependency required for core routing.

---

### Edge Cases

- **No confident cluster**: When the router cannot confidently match a request to an intent, it escalates to a strong default model and records low confidence in the receipt rather than guessing cheaply.
- **Cross-format capability gaps**: When a client requests a capability the chosen model's provider expresses differently (or not at all), the router preserves it where a faithful mapping exists and, where none exists, avoids routing to a model that would silently drop it.
- **Upstream error or timeout**: When the chosen upstream fails or times out mid-stream, the client receives a well-formed error in its original format and the decision log records the failure; partial streams are handled without corrupting the client stream.
- **Cancelled request**: When the client cancels, the in-flight upstream call is cancelled and no further tokens are billed or streamed.
- **Session intent shift**: When a pinned multi-turn session changes intent materially, the router re-routes rather than staying pinned, and the change is shown in the receipt.
- **Missing or invalid provider key**: When no valid key exists for the chosen model's provider, the router returns a clear, non-leaking error and does not fall back to an unauthorized key.
- **Unknown/unsupported request field**: When a request contains fields the internal representation does not model, the router preserves them where safe and never fabricates upstream behavior.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST accept requests in the Anthropic Messages, OpenAI Chat Completions, and Gemini formats and be adoptable by an existing client via a base-URL change only, with no other client modification.
- **FR-002**: The system MUST normalize each incoming request to a single internal representation, route it, and return the response in the *client's original* request format.
- **FR-003**: The system MUST select, for each request, the lowest-cost model it assesses as capable of handling that request, choosing among a configured pool of candidate models that may span multiple providers.
- **FR-004**: The system MUST escalate to a stronger model when its confidence that a cheaper model is capable falls below a configurable floor, and MUST record that escalation.
- **FR-005**: The routing decision MUST be produced by matching the request to a published intent cluster and applying that cluster's published quality/cost policy; the routing model (cluster definitions + policy table) MUST be versioned data that ships with the product.
- **FR-006**: The routing model MUST be reproducible from public datasets by a documented, re-runnable build job, and the resulting version identifier MUST be stamped into every routing decision so any decision is traceable to an exact, obtainable routing model.
- **FR-007**: The system MUST attach a routing receipt to every routed response exposing at minimum: chosen model, confidence, intent/cluster, policy version, estimated cost, and estimated cost delta versus the strongest candidate.
- **FR-008**: The system MUST provide a decision-only capability that returns the full routing decision (chosen model, confidence, ranked alternatives with estimated cost deltas) for a given request **without** calling any upstream provider.
- **FR-009**: The system MUST persist a decision log of routed requests and allow the operator to export it.
- **FR-010**: The system MUST preserve streaming responses end-to-end for every supported format and every candidate model.
- **FR-011**: The system MUST preserve tool/function calling, vision inputs, prompt-caching directives, and extended-thinking semantics across routing, including when the chosen model lives on a different provider than the request format implies; where a faithful cross-provider mapping does not exist, it MUST NOT route in a way that silently drops the capability.
- **FR-012**: The system MUST support session pinning so that consecutive turns of a conversation stay on the same upstream while that preserves a warm prompt cache, un-pinning when the request's intent changes materially, and MUST surface pinning decisions in the receipt.
- **FR-013**: The system MUST use operator-supplied provider keys (BYOK), stored encrypted at rest, and MUST NOT transmit prompt content to any destination other than the chosen upstream provider.
- **FR-014**: The system MUST NOT write provider keys or prompt content in plaintext to logs, receipts, or decision exports.
- **FR-015**: The system MUST run as a single self-contained process with a local embedded datastore by default, requiring no external service for core routing; an external database MAY be used optionally for scale.
- **FR-016**: The system MUST advertise the pool of candidate models it can route to, and MUST compute cost using a pinned, dated pricing snapshot so that cost figures in receipts are explainable and consistent.
- **FR-017**: The system MUST expose health/readiness signals suitable for self-host operation and a self-host view that surfaces spend, savings, and recent routing decisions.
- **FR-018**: Each tagged release MUST publish the benchmark run (frontier + leaderboard) that backs its savings claim, and a release MUST NOT be tagged unless it passes that benchmark gate together with a clean build and green tests.
- **FR-019**: The system MUST provide a one-line installer that wires the common client tools (e.g. Claude Code, Codex, Cursor) to a running router, mirroring familiar onboarding ergonomics.
- **FR-020**: Routing overhead MUST be computed in-process without an additional network hop on the request's critical path.

### Key Entities *(include if feature involves data)*

- **Chat Request (internal representation)**: The provider-neutral form of an incoming request — messages, tools, vision parts, caching directives, thinking directives — into which every supported format is normalized and from which every upstream format is produced. All provider quirks are owned at the format boundary, not in this neutral form.
- **Model Decision / Receipt**: The record of a routing choice — chosen model, confidence, ranked alternatives (each with predicted quality and estimated cost delta), intent/cluster, and policy version. Surfaced per response and persisted to the decision log.
- **Routing Model**: The versioned data that drives decisions — the set of intent clusters and the per-cluster policy table (ranked candidate models by quality-per-cost). Built by the reproducible offline job; identified by a policy version stamped into every decision.
- **Candidate Model Pool**: The configured set of models the router may choose among, each associated with its provider and a pinned, dated pricing entry used for cost math.
- **Provider Key (BYOK)**: An operator-supplied credential for an upstream provider, stored encrypted at rest and used only to call that provider; never exposed in logs, receipts, or exports.
- **Session Pin**: The association between a conversation/session identifier and a pinned upstream, used to keep prompt caches warm across turns and released when intent shifts.
- **Decision Log Entry**: A persisted record of one routed request's decision, timestamp, and policy version, used for export and the self-host view.
- **Benchmark / Release Frontier**: The published measurement artifact accompanying each tagged release that makes its savings-at-parity claim verifiable and gates the release.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A developer can adopt the router by changing only the base URL and providing a key, and reach a first successful routed response in under 10 minutes, with no change to their client code.
- **SC-002**: Across a representative mixed workload, total spend is meaningfully lower than serving every request with the strongest model, at equivalent output quality — with the exact percentage published per release as a verifiable benchmark figure rather than asserted.
- **SC-003**: Routing adds no more than 50ms of overhead per request at p95, measured as the added latency versus calling the chosen provider directly.
- **SC-004**: 100% of routed responses carry a complete receipt (chosen model, confidence, intent, policy version, cost, cost delta), and 100% of routed requests appear in the exportable decision log.
- **SC-005**: An independent party can reproduce the shipped routing model from public data using the documented build job and obtain decision-equivalent results within the stated tolerance.
- **SC-006**: For every supported request format, streaming, tool calls, and vision requests routed to a model on a different provider return correct results in the client's original format, verified by a conformance suite including cross-format round trips.
- **SC-007**: No provider key or prompt content appears in any log, receipt, or export, verified by automated checks; provider keys are unreadable in plaintext at rest.
- **SC-008**: The router runs as a single process with an embedded datastore on a clean machine with no external service dependency for core routing.
- **SC-009**: No release is tagged unless its published benchmark run passes the gate alongside a clean build and green tests.

## Assumptions

- **Design decisions are locked at the product level and treated as constraints, not open questions** (per the approved design spec): open-core under a permissive license; the router is the transparency wedge and routing itself is never paywalled; hosted/managed multi-tenant service, unified billing, SSO, team dashboards, enterprise/air-gapped packaging, and online/live-learning routing are explicitly out of scope for v1.
- The candidate pool and cost math are shared in spirit with the companion benchmark program so that the router's cost figures and the benchmark's cost figures agree.
- The reproducible routing model is built by an **offline, documented job**; v1 is not a continuously self-updating system.
- Intent matching runs **in-process** (no external routing service on the critical path) to meet the latency budget.
- The following are reasonable implementation-level defaults chosen now and finalized during planning, not scope-critical clarifications (recorded here rather than blocking the spec): the exact in-process embedding model to pin; whether the session identifier is client-supplied, derived from the conversation, or both; whether the one-line installer ships in this repo or a small companion; and how rich the v1 self-host view is (minimal spend + decisions vs. richer charts).
- CI runs against recorded upstream fixtures; no live provider keys are present in CI.
- The companion benchmark program exists (or is delivered in parallel) to serve as the release gate and to produce the routing policy inputs.

## Dependencies

- **Companion benchmark program** (measurement rig + release gate) — provides the per-cluster quality/cost inputs to the routing policy and the published frontier that gates each release.
- **Upstream LLM providers** (Anthropic, OpenAI, Gemini, and an aggregator) reachable with operator-supplied keys.
- **Public datasets** used to build the reproducible routing model.
- **A pinned open in-process embedding model** and a **pinned, dated pricing snapshot** as versioned inputs.
