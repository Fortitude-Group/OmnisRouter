# OmnisRouter cache-hygiene: a measured cost lever (design)

Status: design, 2026-09-18. Written as source input for `/speckit-specify` (this would be spec
`004-cache-hygiene`). Owner: the OmnisRouter workstream, with a reporting handoff to OmnisVigil.

## The idea in one paragraph

OmnisRouter already cuts the bill one way: it routes each request to the cheapest capable model, so
you pay a lower per-token price. Cache hygiene is a second way to cut the same bill, and it is
independent of routing. Prompt caching charges you a write premium (about 1.25x on a five-minute
write, 2x on an hour) and only refunds it as a cheap read (about 0.1x) when the prefix comes back
byte for byte. One stray carriage return, a timestamp that moves, or a tool list that serialises in
a different order throws the cached prefix away, and you pay to write it again. That is money spent
rewriting a cache entry the model already had. Routing lowers the price per token. Hygiene lowers
how many tokens you pay write price for, on the same model, with no change to output quality. The
two stack.

## Why this belongs in the router, and why now

The router sits in the request path, so it already holds the exact bytes and gets back the real
`usage` and the real cache result. That means it can do the thing a subscription-only tool cannot:
turn a prediction into a measured pound figure, and fix the cause in flight before it costs anything.
A sibling tool (CacheScope, in the ProseWeightVisualizer repo) proved the analysis on static files
and on transcripts, and found real CRLF cache-hostility in live instruction files. But on a
subscription it can only predict, and its transcript path over-counts. In the router the same
analysis is exact.

**On effort.** The analysis rides on tokens the router is already processing to forward the request,
so the marginal cost of running it is close to zero. That inverts the usual build calculus: there is
no cheap-wins subset to pick, because any lever that saves anything is worth having. This design
therefore covers every hygiene lever we can name, not a starter set. Reducing the bill is the point,
and free measurement plus free-to-apply fixes is exactly the kind of thing that should always be on.

## What it does

### 1. Measure cache waste in the path (content-free out)

For each routed request the router computes a cache-hygiene analysis over bytes it already has, and
across consecutive requests that share a cached prefix it compares them to find why the cache missed:

- the exact first divergent byte between this request's prefix and the last one in its lineage,
- the cause (CRLF drift, trailing whitespace, a volatile header or timestamp before the cache
  breakpoint, an unsorted or reordered tool list, a genuine content edit),
- the tokens that recomputed and their cost, grounded in the real `usage` the provider reported,
- whether the miss was avoidable (a genuine edit or an intended model change is not).

Only content-free results leave the machine. The record pushed to OmnisVigil carries the existing
non-reversible `request_hash`, plus a cause class, a token count, and a pound figure. It never
carries prompt bytes, the diff, or keys. This holds the existing content-free-by-construction
guarantee: a cause label and a number leak nothing. The byte-level detail (the actual diff, the
offending line) stays local to the operator, or rides back to the caller in their own receipt, since
the caller already owns their content.

### 2. Attribute and report through OmnisVigil

The content-free cache-waste fields extend the receipts-up record defined in
`docs/omnisvigil-integration-contract.md`. OmnisVigil aggregates them into the headline it is built
to show: cache misses cost this project X last week, broken down by cause and model, with the fix.
The analysis logic is the CacheScope cost core, reconciled against its versioned result contract and
implemented in the Vigil workstream (the contract crosses the language boundary; the Python code does
not). The meter forks the way CacheScope's does: measured pounds on pay-as-you-go, quota plus a
labelled shadow-price where there is no per-token bill.

### 3. Reduce the waste in flight (the money-saver)

Where a fix is provably semantics-preserving, the router applies it to the request before forwarding,
so the miss never happens. Every fix is opt-in per class, off by default for anything that mutates
the request, and skipped rather than forced whenever safety cannot be shown:

- **Line-ending normalisation.** Normalise request bytes to LF. Safe, and it kills the exact issue
  CacheScope found in the wild without asking every user to fix their own files.
- **Trailing-whitespace normalisation.** Strip trailing spaces and tabs on lines. Safe.
- **Canonical ordering of machine-generated structure.** Sort tool definitions and canonicalise the
  key order of tool JSON, which the model treats as a set but which serialises unstably. Applied only
  where the wire format lets us prove the meaning is unchanged.
- **Stable-prefix guard.** Detect volatile content (a timestamp, a per-request id, an unsorted tool
  list) sitting before the cache breakpoint, where it busts the prefix every call. Warn always;
  reorder only where provably safe; never move or edit prose or message content.

### 4. Show the saving in the receipt

The routing receipt gains a cache block (`X-Omnis-Cache-*` and a section in `POST /v1/route`): what
was found, what was fixed, the tokens and pounds saved, and a before/after. The saving is shown, not
asserted. An operator sees per request that a normalisation turned a write back into a read and what
that was worth.

### 5. Cross-provider

The router already translates Anthropic Messages, OpenAI, and Gemini. Caching differs by provider
(Anthropic explicit breakpoints, OpenAI automatic prefix caching, Gemini context caching), so the
analysis and the breakpoint model follow whichever provider the request is routed to, reusing the
adapter layer.

## Locked decisions

- **Content-free out, always.** Bytes, diffs, and keys never leave the machine. Only a hash, a cause
  class, a token count, and a pound figure go to Vigil. This is not negotiable; it is what lets a
  self-hosted router talk to a hosted control plane.
- **Fail-open, like reporting.** Cache analysis and any fix MUST NEVER block, delay past a small
  budget, or fail a request. Analysis that would add latency runs off the hot path. A fix that is not
  proven safe is skipped. A request is served whether or not any of this ran.
- **Measured, not predicted.** In the path we use real `usage` and real breakpoints. The static
  prediction is kept only for the offline case (no traffic yet), clearly labelled as a prediction.
- **Fixes only where semantics are provably preserved.** Line endings, trailing whitespace, and the
  canonical ordering of machine-generated tool JSON qualify. Prose, message content, and anything
  whose meaning could shift do not, and stay warn-only.
- **Mutation is opt-in; measurement is default-on.** Measuring waste is free and content-free, so it
  runs by default. Anything that changes the request the user sent is off until the operator turns it
  on, per fix class, and is controllable by a Vigil policy the way caps and kill state already are.
- **Additive.** A new capability in the router and a new content-free field in the existing receipt
  contract. It does not change routing decisions or the wire translation.

## Non-goals

- Rewriting user prose, reordering messages, or any change that could alter meaning.
- The OmnisVigil dashboard itself. This defines the content-free fields Vigil consumes; the reporting
  UI is Vigil's own spec.
- Model routing. That is spec 001 and a separate lever. This is orthogonal and stacks on it.

## Success criteria

- For a request whose prefix would have missed on a fixable cause, the router applies the fix and the
  provider reports a cache read instead of a write, and the receipt shows the measured tokens and
  pounds saved.
- No prompt bytes, diff, or key ever appears in a record sent to Vigil (a content-free test over the
  outbound records, mirroring the existing allowlist enforcement).
- Zero added request failures and no measurable latency added to the hot path (analysis off-path or
  within a stated budget; fail-open verified by fault injection).
- A safe fix is byte-transparent to the response: the same request served with and without
  normalisation returns an equivalent response, proven in tests.
- Every pound figure is stamped with its pricing version and FX date and marked measured or
  shadow-price, never presented as a bill on a subscription.

## Brainstorming resolutions (2026-09-18)

The three open questions below were resolved with the owner, and a code survey of the request path
grounded the architecture. Recorded here so `/speckit-specify` has a settled input.

**Decisions:**

1. **Anthropic-first.** Ship measurement, fixes, and the receipt for Anthropic's explicit
   `cache_control` breakpoints in `004`, where "bytes before the breakpoint" is exact. OpenAI
   automatic prefix caching and Gemini context caching follow in `005` (Gemini also needs its wire
   model taught `cachedContentTokenCount`, which it does not parse today).
2. **All mutations opt-in; measurement default-on.** No fix, including line-ending normalisation,
   changes the user's bytes without the operator turning it on per class. Measurement runs by default
   and the receipt surfaces "enable line-ending normalisation to save £X" so the win is one deliberate
   click away.
3. **The cost core is reimplemented in the router, in C#.** Content-free-out forces the per-request
   byte analysis into the router; nothing else can see the bytes. It is reconciled against CacheScope's
   versioned RESULT/CAPTURE contract (a C# port of the contract types plus CacheScope's test vectors as
   a conformance suite), so the two implementations agree. Vigil only aggregates the content-free
   fields (see the hand-off below).
4. **Keep nothing persistent.** A bounded in-memory lineage cache holds the previous prefix only long
   enough to diff the next request, then evicts. The byte-level detail (offending line, before/after)
   rides back only in the synchronous receipt to the caller, who already owns their content. No local
   content store on disk. An operator may opt into a local rolling diff log for forensics.
5. **FX lives in the dated pricing snapshot.** Add `usd_gbp` + `fx_date` to the immutable pricing
   snapshot yaml, ported from CacheScope's `PricingStamp`. Deterministic and reproducible, no network
   dependency. Every £ figure = USD × rate, stamped with `pricing_version` + `fx_date`.

**Architecture (grounded in the request path, `RoutedRequestHandler.ExecuteAsync`):**

- A new cross-platform library **`OmnisRouter.CacheHygiene`** holds the cost core
  (`CacheHygieneAnalyzer`), the `LineageCache`, the `Normalizers`, the C# port of CacheScope's
  result/capture contract, and the `CauseClass` enum.
- **Analyse the wire bytes, not the client body.** The forwarded bytes are a fresh per-provider
  re-serialisation of the neutral request (`AnthropicRequestMapper.ToWireRequest`), not the incoming
  `body`. Both the current prefix and any normalisation target the egress wire serialisation. The
  prefix is the wire bytes up to and including the block carrying `cache_control`.
- **Hot path vs off path.** Enabled normalisers run after `Decide`, before dispatch (where the image
  materialiser already rewrites the request), capturing before/after. The analysis runs off the hot
  path in `CaptureAndLog`/`BuildLogEntry`, the one place that has both the request and the real
  `Usage` (which already carries the cache read/creation breakdown end to end). Fail-open is
  structural: any exception in normalisation or analysis is swallowed and the request is served.
- **Measured saving.** When a fix runs, the analyzer is given the previous prefix, the current
  *un-normalised* prefix (which would have diverged, e.g. on CRLF), and the *normalised* prefix (which
  now matches). The saving is the tokens that would have recomputed × (write − read) rate, confirmed by
  the provider returning a read.

**Content-free contract fields (the hand-off deliverable).** `004` adds a content-free `cache_waste`
block to the receipts-up ingest record and freezes it in the schema JSON + `omnisvigil-integration-contract.md`:
`cause_class` (the `CauseClass` enum), `avoidable`, `recomputed_tokens`, `waste_gbp`, `fix_applied`
(or null), `saved_tokens`, `saved_gbp`, `pricing_version`, `fx_date`, `usd_gbp`, `shadow_price`. All
scalars and labels, no bytes. It must be added to `DecisionLogEntry`, `IngestRecordMapper.ToRecord`,
the ingest schema JSON, the contract doc, **and** mirrored in `AnalyticsDecisions.ToJson` (two
divergent serialisers), enforced by a content-free test over outbound records. Freezing this contract
is a first-class deliverable of `004`, not an afterthought.

**Receipt.** `ModelDecision` gains a `Cache` block (it already carries `PricingSnapshotDate`), surfaced
as `X-Omnis-Cache-*` headers (`WriteReceiptHeaders`) and a `cache` section in `/v1/route`
(`ReceiptJson`). The byte-level before/after appears only in this synchronous, caller-owned receipt.

**Cross-repo hand-off.** The OmnisVigil dashboard that surfaces these savings is a separate spec in the
OmnisVigil repo, sourced from
`OmnisVigil/docs/superpowers/specs/2026-09-18-cache-waste-reporting-design.md`. It consumes the frozen
`cache_waste` contract and does aggregation + UI only — no byte analysis. The two workstreams meet only
at the content-free contract.

## Remaining items for `/speckit-clarify`

- The lineage key: which identity groups consecutive requests into one cache lineage (session-pin id,
  a client-supplied lineage header, or `request_hash` ancestry), and the in-memory cache's size/age
  bound.
- The latency budget for any in-path normalisation, and the exact off-path mechanism for the analysis
  (fire-and-forget task vs a bounded queue), so fail-open is verifiable by fault injection.
- The default set of enabled fixes shipped in config (expected: none — all off — with measurement on),
  and the Vigil-policy shape that toggles fix classes the way caps/kill state already work.
