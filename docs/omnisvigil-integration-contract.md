# OmnisRouter to OmnisVigil integration contract (v1)

Status: reconciled v1, 2026-08-27. Owner of this file: the OmnisRouter (router + bench)
workstream. This is the canonical definition. The OmnisVigil workstream has reconciled against
it in `OmnisVigil/specs/001-team-spend-control-plane/contracts/` (`receipt-up.v1.md`,
`policy-down.v1.md`, `content-free-allowlist.md`). This revision folds their feedback back in:
authoritative fleet spend in the policy, the richer ingest response, and strict closed-schema
enforcement. Neither side needs the other running to build against it.

OmnisVigil is the paid team control plane. OmnisRouter is the free, open, self-hosted router
that does the work and captures the data. The coupling is exactly two flows over one
authenticated channel:

- Receipts up: the router pushes content-free per-request records to the Vigil collector.
- Policy down: the router pulls the current caps, allowed models, and kill state from Vigil
  and enforces them locally.

## Hard guarantees (do not break these)

- Content-free by construction. A record carries a non-reversible `request_hash`, never prompt
  or response text, and never API keys. This is the privacy contract that lets a self-hosted
  router talk to a hosted Vigil. Attribution tags are caller-supplied labels only. The router
  MUST reject or drop any tag value over a fixed length and MUST NOT copy request content into
  a tag.
- Fail-open on reporting. Routing a request MUST NEVER block on, wait for, or fail because of
  Vigil. The reporting queue is bounded and drops oldest when full. A request is served
  whether or not its record is ever delivered.
- Fail-safe on policy. If Vigil is unreachable, the router keeps enforcing the last known
  policy (caps and kill state). A runaway is still capped from the last good poll. Policy is
  never fetched inline on the request path, only from a background poller.

## Flow 1: receipts up (router to Vigil)

The router already persists a content-free `DecisionLogEntry` and exposes it as NDJSON at
`GET /v1/analytics/routing-decisions`. Vigil does not pull that (self-hosted routers sit
behind firewalls and cannot be reached inbound). Instead the router PUSHES batches out.

Endpoint (Vigil side): `POST {vigil_endpoint}/v1/ingest`
Auth: `Authorization: Bearer <project_key>` (the project key from router config)
Body: `{ "schema_version": 1, "records": [ <ingest record>, ... ] }`, gzip encouraged.
Delivery: at-least-once. Vigil MUST dedupe on `record.id` (idempotent upsert).
Batching (router side): flush every N records or T seconds, whichever first (defaults
`BatchSize=200`, `FlushSeconds=10`, tunable).
Responses: `202 { "accepted": <int>, "duplicates": <int>, "rejected": [ ... ] }` (the router
reads `accepted`); `401` bad key; `429` with `Retry-After` for backoff. On any non-2xx or
timeout the router retains the batch (up to the queue cap) and retries with exponential
backoff. It never blocks routing while doing so.

Closed schema on the wire. Vigil deserialises each record against the exact ingest schema with
unknown members disallowed. Any field outside the schema (a stray `prompt`, `content`, a
`metadata` blob, or any unknown key, top level or inside `tags`) is a hard per-record rejection
and flags the router. So the router MUST emit exactly the schema fields and nothing else, never
leak an internal field, never add a free-form blob. Tenant is resolved from the project key;
the record's `tenant_id` is ignored by Vigil (it stays in the record for the router's own local
view and the NDJSON export). Idempotency is by `(tenant, id)`, so replays never double-count.

### Ingest record schema

The record is the existing `DecisionLogEntry` plus the fields Vigil needs for attribution and
an honest savings ledger. Formal schema: `docs/contracts/omnisvigil-ingest-record.schema.json`.

Already emitted today (content-free decision log):

| field | type | notes |
|---|---|---|
| `id` | string | idempotency key, one per request |
| `timestamp` | string (ISO 8601) | |
| `tenant_id` | string | |
| `session_id` | string or null | router session grouping, not a user id |
| `request_hash` | string | non-reversible hash, NOT content |
| `client_format` | enum `anthropic\|openai\|gemini` | the wire format used |
| `cluster_id` | integer | intent cluster |
| `chosen_provider` | enum `anthropic\|openai\|gemini\|openrouter` | |
| `chosen_model_id` | string | |
| `confidence`, `top1_sim`, `top2_sim`, `margin` | number | routing confidence signals |
| `decision` | enum `ROUTED\|ESCALATED` | |
| `reason` | enum | `cheapest_capable`, `confidence_below_floor`, `low_confidence_cluster`, `capability_guardrail`, `session_pinned` |
| `policy_version` | string | routing-model build id |
| `est_cost_usd` | number | pre-call estimate |
| `est_cost_delta_vs_big_usd` | number | estimated savings vs strongest candidate |
| `session_pin_applied` | boolean | |
| `outcome` | enum `success\|upstream_error\|cancelled` | |
| `latency_ms` | integer | |

Router-side ADDITIONS required for v1 (work items on the router side, see below):

| field | type | why Vigil needs it |
|---|---|---|
| `router_id` | string | which deployment emitted this, to attribute across a fleet |
| `usage` | object | actual token accounting: `{ input_tokens, output_tokens, cache_creation_tokens, cache_read_tokens }` from `ChatResponse.Usage` |
| `actual_cost_usd` | number | real spend from actual usage and the pinned pricing snapshot, not the estimate |
| `actual_cost_delta_vs_big_usd` | number | the real per-request saving vs the strongest candidate. This is the number the savings ledger and the guarantee pay out on |
| `pricing_snapshot_date` | string (date) | which price list the cost math used (already on the receipt) |
| `tags` | object or null | caller-supplied attribution labels, all optional: `{ project, team, client_name, commit, branch }`. Length-capped, content-free |

`est_*` fields stay (they are the pre-call decision). `actual_*` fields are the truth Vigil
reports. Where a request has no usage yet (cancelled, upstream error) the `actual_*` fields
are null and `outcome` explains why.

### Attribution tags

Tags are set by the client or harness via request headers on the routed call, which the router
copies into the record. Proposed header to record mapping:

- `X-Omnis-Project` -> `tags.project`
- `X-Omnis-Team` -> `tags.team`
- `X-Omnis-Client` -> `tags.client_name` (e.g. `claude-code`, `cursor`, `codex`)
- `X-Omnis-Commit` -> `tags.commit`
- `X-Omnis-Branch` -> `tags.branch`

All optional. Missing tags mean that dimension is "unattributed" in Vigil, never an error. The
router caps each tag at 200 characters and strips anything that is not a tag value.

## Flow 2: policy down (Vigil to router)

Endpoint (Vigil side): `GET {vigil_endpoint}/v1/policy`
Auth: `Authorization: Bearer <project_key>`
Caching: `policy_version` is a content hash, so an unchanged policy yields the same version.
The router sends `If-None-Match: "<policy_version>"`; Vigil returns `304` (the common case) or
`200` with `ETag: "<policy_version>"` and `Cache-Control: max-age=45`. `401` (missing, invalid,
or revoked key) means the router falls back to its local free-tier cap. `5xx` or unreachable:
the router keeps enforcing the last known policy.
Poll interval: `PolicyPollSeconds` default 45, background only, never on the request path.

Response body (`200`):

```json
{
  "policy_version": "vigil-<content-hash>",
  "caps": {
    "monthly_usd": 5000,
    "per_project_usd": { "web": 2000, "infra": 1500 },
    "spent_usd": 214.30,
    "spent_per_project_usd": { "web": 120.00 }
  },
  "allowed_models": ["anthropic/claude-haiku-4-5", "openai/gpt-5-nano", "openai/gpt-5"],
  "confidence_floor": 0.05,
  "kill": { "org": false, "teams": [] },
  "updated_at": "2026-08-27T10:00:00Z"
}
```

Enforcement (router side, all local, all on the last fetched policy):
- Raw caps, router resolves. Vigil sends the org cap and each project ceiling raw, it does not
  pre-resolve an effective cap. The router enforces the most-restrictive of the org and
  per-project ceilings.
- Fleet spend. `caps.spent_usd` and `caps.spent_per_project_usd` are Vigil's authoritative
  cross-router rollup. The router enforces the fleet cap against the true total, treating
  remaining headroom as `cap - spent` from the last policy, plus its own local spend since that
  poll. Without this a fleet of N routers under one shared cap would each spend up to the cap
  and collectively blow past it. On a Vigil outage the router keeps enforcing on the last known
  `spent` plus its local counters, so a runaway is still bounded.
- `kill.org == true` -> the router hard-stops every routed request with a defined error
  (fleet-wide cutoff). `kill.teams` is reserved: per-team kill is a v1.x follow-on, so the
  router should accept the field but need not act on it yet.
- `allowed_models` and `confidence_floor` override the local routing policy where present.
  `allowed_models` is tenant-wide in v1 (see Known gaps).

The free self-hosted router keeps its own local cap and kill-switch independent of all this.
Flow 2 is the fleet-wide layer on top, and it only exists when a project key is configured.

## Auth and config (router side)

The router config gains an `OmnisVigil` section:

```json
"OmnisVigil": {
  "Enabled": true,
  "Endpoint": "https://ingest.omnisvigil.fortitude-omnis.group",
  "ProjectKey": "${OMNIS_VIGIL_PROJECT_KEY}",
  "BatchSize": 200,
  "FlushSeconds": 10,
  "PolicyPollSeconds": 45
}
```

`ProjectKey` is a secret and comes from the environment, never committed. Absent or
`Enabled=false` means the router runs fully standalone (the free tier): no push, no policy
poll, local cap and kill-switch only.

## Known gaps (agreed, follow-on)

- Per-project model policy is not expressible in the flat `allowed_models`, which is tenant-wide
  in v1. A per-project model structure is a v1.x follow-on that both sides agree together before
  either builds it.
- Per-team kill (`kill.teams`) is reserved. The field exists in v1, per-team enforcement is a
  follow-on. `kill.org` is the working fleet cutoff for v1.

## Versioning

`schema_version: 1` on every ingest batch and a `policy_version` on every policy response.
v1 changes are additive only. A breaking change is a new `schema_version`, and Vigil accepts
both during a migration window. Any breaking change is coordinated across both workstreams.

## Work items this contract creates

Router side (this workstream):
1. Extend `DecisionLogEntry` and the log write path to capture actual `Usage`, `actual_cost_usd`,
   and `actual_cost_delta_vs_big_usd` (today only `est_cost_usd` is stored).
2. Capture the `X-Omnis-*` attribution headers into `tags`, length-capped and content-free.
3. Add a `router_id`.
4. Build the reporting sink: bounded queue, batch, push to `/v1/ingest`, fail-open. It MUST
   serialise exactly the ingest schema and no extra fields (Vigil rejects and flags any record
   with an unknown field).
5. Build the policy poller and local enforcement: raw caps resolved most-restrictive, the fleet
   cap enforced against `caps.spent_usd` (not just the local view), `kill.org`, `allowed_models`,
   and `confidence_floor`. Fail-safe on the last known policy.
6. Add the `OmnisVigil` config section.

Vigil side (other workstream, reconciled in `OmnisVigil/specs/001-team-spend-control-plane/`):
1. `POST /v1/ingest`: authenticate the project key, resolve tenant from the key, dedupe on
   `(tenant, id)`, reject records with unknown fields, store.
2. `GET /v1/policy`: serve the current policy (content-hash `policy_version`, ETag, fleet spend).
3. Everything downstream: attribution rollups, savings ledger, dashboard, caps, kill-switch UI,
   alerting, billing.
