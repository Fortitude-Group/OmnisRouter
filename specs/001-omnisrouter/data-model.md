# Data Model: OmnisRouter v1

**Feature**: `001-omnisrouter` | **Date**: 2026-08-19 | Derived from [spec.md](./spec.md) Key Entities + [research.md](./research.md).

Two categories: **in-memory domain types** (the neutral request/response model and routing types — not persisted) and **persisted entities** (EF Core, SQLite/Postgres). Persisted entities are marked **[DB]**.

---

## In-memory domain types

### ChatRequest (neutral internal representation)
The provider-agnostic form every ingress adapter produces and every egress adapter consumes.
- `Model` (string, optional — client-requested; may be overridden by routing)
- `System` (text + optional per-segment cache markers)
- `Messages[]`: ordered turns, each `Role` (`system|user|assistant|tool`) + `Parts[]`
  - Part kinds: `TextPart`, `ImagePart` (`MediaType`, `Base64` **or** `Url` — URL normalized/flagged for fetch per R2), `ToolUsePart` (`Id`, `Name`, `InputJson`), `ToolResultPart` (`ToolUseId`, `Content`, `IsError`), `ThinkingPart` (`Text?`, `Signature?`, `Provider`, `Model` — provenance-bound per R2)
- `Tools[]`: `Name`, `Description`, `JsonSchema`, `Strict?`
- `ToolChoice` (`auto|any|none|specific(name)`)
- `CacheDirectives[]`: breakpoint markers with `Ttl` (`5m|1h`)
- `Thinking`: `Enabled`, `Effort` (`low..max`) or `BudgetTokens?`
- `Stream` (bool)
- `SessionId?` (from `X-Omnis-Session-Id` or derived — see SessionPin)
- `CapabilitiesUsed` (derived set: `vision`, `tools`, `parallel_same_tool`, `remote_image_url`, `cache_pin_guaranteed`, `thinking_with_signature`, `strict_schema`, `numeric_reasoning_budget`) — drives guardrails
**Validation**: at least one message; tool result parts must reference a prior tool-use id; image media type ∈ supported set.

### ChatResponse / stream events
Neutral response: `Content[]` (same part kinds), `StopReason`, `Usage` (`InputTokens`, `OutputTokens`, `CacheCreationTokens`, `CacheReadTokens`), plus the attached **Receipt**. Streaming is modelled as `IAsyncEnumerable<NeutralStreamEvent>` (block-open/delta/close/usage/error) that egress adapters re-frame into each wire format (R2/R3).

### ModelRef
- `Provider` (`anthropic|openai|gemini|openrouter`), `ModelId`, `Capabilities` (vision/tools/thinking/strict/parallel-tools/cache flags), `Pricing` (ref into the pinned snapshot). Backs the **per-model capability table** guardrails check against.

### ModelDecision / Receipt
The routing choice, surfaced per response and persisted (see DecisionLogEntry).
- `Chosen` (ModelRef), `Confidence` (softmax `p₁`), `Top1CosineSim`, `Top2CosineSim`, `Margin`
- `Alternatives[]`: `Model`, `PredictedQuality`, `EstCostUsd`, `EstCostDeltaUsd`
- `ClusterId`, `PolicyVersion`, `Decision` (`ROUTED|ESCALATED`), `Reason` (e.g. `cheapest_capable`, `confidence_below_floor`, `low_confidence_cluster`, `capability_guardrail`)
- `EstCostUsd`, `EstCostDeltaVsBigUsd`
- `SessionPinApplied` (bool), `SessionPinReason?`
**Validation**: `Confidence ∈ [0,1]`; `PolicyVersion` non-empty and resolvable to a shipped routing model; if `Decision=ESCALATED` then `Chosen` = configured strong default.

### RoutingModel (loaded from versioned files)
- `Version` (string), `Centroids[k][dim]` (from `centroids-<ver>.bin`), `PolicyTable`: per `ClusterId` → ranked `PolicyRow` (`Model`, `PredictedQuality`, `RankByCost`, `LowConfidence`), `Temperature T`, `ConfidenceFloor τ`.
**Validation**: `dim` matches the pinned embedder (384); each policy row's models exist in the candidate pool; `k = Centroids.Length`.

---

## Persisted entities [DB]

### Install [DB]
One row per router instance/tenant. `Id`, `TenantId`, `CreatedAt`, `Settings` (JSON: confidence floor override, strong-default model, cost_tier default).

### ProviderKey [DB]  *(BYOK)*
- `Id`, `TenantId`, `Provider`, `Label`
- `ApiKeyEncrypted` (`byte[]` = `keyVersion ‖ nonce(12) ‖ tag(16) ‖ ciphertext`, AES-256-GCM via `ValueConverter`)
- `KeyVersion` (int), `CreatedAt`, `LastUsedAt?`
**Validation/invariants**: plaintext never persisted, logged, or exported; fresh random nonce per write; lookup by (`TenantId`,`Provider`)/`Id` only (encrypted column is not queryable).

### RouterToken [DB]
Client-facing auth token for calling the router. `Id`, `TenantId`, `HashedToken`, `Name`, `CreatedAt`, `RevokedAt?`. (Store a hash, never the raw token.)

### Usage [DB]
Aggregated spend/savings for the dashboard. `Id`, `TenantId`, `Date`, `Provider`, `ModelId`, `Requests`, `InputTokens`, `OutputTokens`, `CostUsd`, `CostVsBigUsd` (savings basis).

### DecisionLogEntry [DB]
Append-only routing decision log (FR-009; export via NDJSON).
- `Id`, `TenantId`, `Timestamp`, `SessionId?`
- `RequestHash` (non-reversible — **no prompt content stored**), `ClientFormat`, `ClusterId`
- `ChosenProvider`, `ChosenModelId`, `Confidence`, `Top1Sim`, `Top2Sim`, `Margin`
- `Decision`, `Reason`, `PolicyVersion`
- `EstCostUsd`, `EstCostDeltaVsBigUsd`, `SessionPinApplied`
- `Outcome` (`success|upstream_error|cancelled`), `LatencyMs`
**Invariants**: no prompt/response content or keys (FR-014); one row per routed request (SC-004).

### SessionPin [DB or cache]
- `SessionKey` (client `X-Omnis-Session-Id` or `HMAC-SHA256(server_secret, tenant ‖ system ‖ first_user)`/128-bit), `TenantId`, `PinnedProvider`, `PinnedModelId`, `ClusterId`, `LastSeenAt`, `ExpiresAt`
**State transitions**: *unpinned → pinned* on first turn of a session; *pinned → re-routed* when the turn's `ClusterId` changes materially (un-pin, record `SessionPinReason`); *pinned → expired* on TTL. May live in an in-memory/embedded cache rather than a durable table.

### PricingSnapshot (versioned config, `config/pricing/<date>.yaml`)
Per `Provider`/`ModelId`: `InputPer1k`, `OutputPer1k`, `CacheWritePer1k?`, `CacheReadPer1k?`, `SnapshotDate`. Shared in spirit with OmnisBench so router cost math == benchmark cost math (FR-016, Principle XII).

---

## Relationships (summary)

```
Install 1──* ProviderKey
Install 1──* RouterToken
Install 1──* DecisionLogEntry 1──0..1 SessionPin (by SessionKey)
Install 1──* Usage
RoutingModel *──* ModelRef (via PolicyTable rows)  ── priced by ──> PricingSnapshot
ChatRequest ──produces──> ModelDecision ──logged as──> DecisionLogEntry
```
