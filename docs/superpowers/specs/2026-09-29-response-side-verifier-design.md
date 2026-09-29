# OmnisRouter response-side verifier: a second net on the way out (design)

Status: design, 2026-09-29. Written as source input for `/speckit-specify` (this would be spec
`007-response-verifier`). Owner: the OmnisRouter workstream. Parked until the launch settles, then
specify.

## The idea in one paragraph

Routing decides which model answers before the answer exists. That is the whole game, and it is also
the whole blind spot: the router bets on the cheapest capable model, the model answers, and nothing
looks at whether the answer was any good. Most of the time that is fine, because the confidence floor
already sends the genuinely hard prompts to a strong model up front. But the floor is a guess made
from the prompt alone, and a cheap model will sometimes take a prompt it looked able to handle and
return something empty, truncated, malformed, or confidently wrong. The response-side verifier is a
second net for exactly that case: a cheap check on the way out that, when it is worried, escalates or
re-runs on a stronger model and marks what it did on the receipt. It never silently rewrites or eats
a response. It is advisory by construction, and it is the honest answer to the question the launch
posts invite, "how do you catch a botched cheap answer?"

## Why this belongs in the router, and why now

The router sits in the request path, so it already holds the response bytes, the real `usage`, the
stop reason, and the decision it made. That is everything a cheap structural check needs, and none of
it costs an extra call. It is also the only layer that can act on a bad answer the way routing acts
on a hard prompt: pick a different model and try again, on the same wire contract, transparently, and
put the second attempt's cost on the receipt next to the first. A harness-side verifier (the kind an
agent extension builds) has signals the router does not, process exit codes and test results, and it
owns the confirm-before-run UX. The router has signals the harness does not, the full candidate pool
and the pricing. These are two different nets over two different failure modes, and they compose. This
design is the router's net. It does not try to be the harness's.

**On the existing ESCALATED path.** The confidence floor already produces a `decision: ESCALATED`
when the router will not gamble on a cheap model for a prompt it cannot place. That is a routing-time
bet made before any answer exists. The verifier is the post-hoc complement: the bet was taken, the
cheap answer came back, and it looks wrong. Same vocabulary, different moment. The receipt has to
make the two distinguishable, not blur them into one word.

**On effort.** The cheap tier of the check rides on bytes the router already has, so it is close to
free and can run on every routed response. The expensive tier, an actual strong-model re-run or
semantic judgement, only fires when the cheap tier is already worried. That is the design rule that
keeps this from doubling the bill: you never spend a strong-model check on a response the cheap
signals are happy with.

## What it does

### 1. Cheap structural checks on every response (free, content-light)

For each routed response the router runs a set of deterministic checks over bytes and metadata it
already holds. None of these calls a model:

- **Empty or degenerate output.** Zero-length, whitespace-only, or a stop reason of `max_tokens`
  with no natural close (truncated mid-answer).
- **Contract failure the client asked for.** If the request set a strict JSON schema, a tool-use
  requirement, or a response format, and the response does not satisfy it (unparseable JSON, a schema
  violation, a missing required tool call), that is a hard structural miss.
- **Refusal or capability-shortfall shape.** A short apology or a "I can't help with that" pattern on
  a request that was not itself a refusal candidate, which on a cheap model often means it fell over
  rather than genuinely declined.
- **Length sanity against the ask.** A one-line answer to a request that supplied a large body of
  material to work through, bounded loosely so it flags the obviously-truncated, not the terse.

Each check yields a signal with a class and a cheap/hard weight. The point of this tier is triage:
decide, for free, whether the expensive tier is worth spending.

### 2. The escalation decision (spend a strong check only when worried)

If the cheap signals are clean, the response is returned untouched and the receipt records that
verification ran and passed. If a cheap signal is a hard miss (a schema the client demanded did not
validate), the router can escalate without a semantic check, because the failure is already proven.
If a cheap signal is soft (looks truncated, looks like a fallover), and only then, the router may
spend the expensive tier:

- **Re-run on a stronger model.** The cheapest option that beats the first pick in predicted quality
  answers the same request again. This is the router doing what it already does, one rung up.
- **Semantic judgement (optional, later).** A cheap-but-capable model judges whether the answer meets
  the ask, as a gate before paying for a full re-run. This is the part that overlaps a harness
  "hallucination guard", and it is the part that reads response content deeply, so it is opt-in and
  off by default (see locked decisions).

The floor for spending is deliberately high. The default posture is: flag freely, re-run rarely, and
only where a cheap signal already found something concrete.

### 3. Advisory, never destructive

The verifier does not rewrite a response and does not swallow one. Its outputs are: return the
original, return a re-run's answer instead (with both attempts costed on the receipt), or return the
original flagged. A proxy that silently ate or edited answers would be impossible to trust, and trust
is the product. The client always gets a real upstream answer, and always gets told what the verifier
thought of it.

### 4. Use the signal the harness has, if it offers it (the handshake)

The router cannot see a tool's exit code or a test result. The harness can. So the design leaves an
inbound door: a client can attach a prior turn's outcome to the next request (a small, content-free
signal, "the last thing you routed produced a non-zero exit" or "the tests it wrote failed"), and the
router folds that into the next decision, raising the effective floor for that lineage. This is the
one place the router-side and harness-side nets actually touch, and it is optional on both ends. With
no such signal the verifier works on structural signals alone.

### 5. Show it in the receipt

The routing receipt gains a verification block (`X-Omnis-Verify-*` and a section in `POST /v1/route`
and the decision log): whether verification ran, which checks fired, whether it escalated or re-ran,
and the cost of any second attempt. As with routing and cache hygiene, the action is shown, not
asserted. An operator can see that a cheap answer came back truncated, that the router re-ran on a
stronger model, and what the second attempt cost.

## Locked decisions

- **Advisory, always. Never block, never silently rewrite, never eat a response.** The client gets a
  real answer and the verifier's opinion of it. This is the same non-negotiable posture as the rest of
  the router.
- **Fail-open, like everything else in the path.** A verifier that errors, or runs over a small time
  budget, returns the original response unchanged. Verification failing must never fail a request.
- **Cheap-signal-first.** The free structural tier gates the expensive tier. A strong-model check or
  re-run only fires when a cheap signal already found something. Never pay a strong check on a response
  the cheap signals passed.
- **Structural checks default-on; any model-spending check opt-in.** The free tier runs by default
  because it is free and content-light. Anything that reads response content with a model, or spends a
  re-run, is off until the operator turns it on, per class, and is controllable by a Vigil policy the
  way caps and kill state already are.
- **Content-free out.** What leaves the machine for Vigil is a check class, a boolean, the escalation
  action, and a cost. Never the response bytes, the prompt, or the judged content. Same guarantee as
  the cache-waste contract.
- **Distinct from routing-time escalation.** The receipt must separate a pre-answer `ESCALATED`
  (confidence floor) from a post-answer re-run (verifier). They share intent, not a moment.
- **Additive.** A new capability in the path and a new content-free block in the existing receipt and
  ingest contracts. It does not change how the first routing decision is made.

## Non-goals

- Judging factual correctness in general. The router is not a fact-checker. The semantic tier, if
  enabled, judges "does this answer meet the ask", not "is this true about the world".
- The harness's job: bash-command firewalling, untrusted-file injection scanning, git rollbacks,
  frustration detection. Those need the workspace and the tool loop, which the router does not have.
  This design deliberately stops at the request path.
- Rewriting or repairing a response in place. The verifier chooses between whole answers; it does not
  patch one.
- The OmnisVigil dashboard. This defines the content-free verification fields Vigil consumes; the
  reporting UI is Vigil's own spec.

## Success criteria

- For a routed response that comes back empty or truncated on a cheap model, with re-run enabled, the
  router re-runs on a stronger model and returns the better answer, with both attempts costed on the
  receipt and the reason recorded.
- For a request that demanded a strict schema and got unparseable output, the verifier flags it as a
  hard miss and escalates without spending a semantic check first.
- The free structural tier adds no measurable latency to the hot path and calls no model (verified by
  a check that the passing path issues zero extra upstream calls).
- No response bytes, prompt bytes, or judged content ever appear in a record sent to Vigil (a
  content-free test over the outbound records, mirroring the cache-waste allowlist enforcement).
- A verifier fault or timeout returns the original upstream response unchanged (fail-open, proven by
  fault injection).
- The receipt distinguishes a routing-time `ESCALATED` from a verifier re-run, and never presents a
  re-run's extra cost as if it were the original single-call cost.

## Open questions for `/speckit-specify` and `/speckit-clarify`

These are genuinely open. The design above commits to posture (advisory, cheap-first, fail-open,
content-free) and leaves the mechanics to specify time.

- **The soft-signal escalation policy.** Exactly which soft signals justify spending the expensive
  tier, and the floor for doing so. Start conservative: hard schema/tool misses re-run, soft signals
  flag-only until measured, so the first ship is almost all free structural checks with re-run behind
  an explicit opt-in.
- **Re-run vs semantic-judge ordering.** Whether the first expensive step is always a re-run on a
  stronger model, or an optional cheap semantic gate before paying for the re-run. Leaning: ship
  re-run first, add the semantic gate later as a cost optimisation, because the gate is the part that
  reads content and carries the most posture risk.
- **The harness handshake shape.** The wire form of the inbound outcome signal (a header, a field),
  how a lineage is identified (likely the session-pin id the router already has), and keeping it
  content-free and optional on both ends. This is the piece worth designing with a real harness author
  in the loop rather than in the abstract.
- **Idempotency and streaming.** A re-run is fine for a buffered response; for a streamed response the
  first bytes may already be on the wire before a truncation is visible. Decide whether verification on
  streamed responses is post-hoc flag-only (no re-run once streaming has begun) or whether re-run is
  restricted to non-streamed requests in the first cut. Leaning: flag-only on streams to start.
- **Latency budget for the free tier**, and where it runs relative to the response returning to the
  client, so fail-open and the zero-added-latency criterion are verifiable by fault injection.
