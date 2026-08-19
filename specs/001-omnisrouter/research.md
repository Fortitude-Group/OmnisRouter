# Phase 0 Research: OmnisRouter v1

**Feature**: `001-omnisrouter` | **Date**: 2026-08-19 | **Plan**: [plan.md](./plan.md)

This document resolves the open technical questions from the approved design spec (§13 Open Questions) and the "NEEDS CLARIFICATION" items in the plan's Technical Context. Each item is stated as **Decision / Rationale / Alternatives considered**. Findings were gathered by four parallel research passes (embedder, cross-format translation, .NET streaming/BYOK, routing policy); source URLs are listed per section.

---

## R1. In-process embedder (design Open Q1)

**Decision**: Pin **`bge-small-en-v1.5`** (BAAI, MIT), 384-dim, served via `Microsoft.ML.OnnxRuntime` with **int8 quantization** (start from the pre-quantized `Qdrant/bge-small-en-v1.5-onnx-Q`). Tokenize with **`Microsoft.ML.Tokenizers.BertTokenizer`** (in-box, WordPiece). Pinned **fallback: `gte-small`** (same 384-dim / MIT / WordPiece, MTEB within ~1 pt).

**Rationale**: Best quality-per-size of the candidates (MTEB 62.17, ahead of e5-small-v2 ~59.9 and all-MiniLM-L6-v2 ~56.3); 384-dim keeps k-means/centroid math and index memory small; ships an official ONNX export **plus** a ready-made quantized variant (no in-house export pipeline for v1); **no required prompt-prefix scheme** (unlike e5/nomic) so the raw prompt goes straight to the tokenizer — removes a class of "forgot the prefix, silently degraded" bugs on the routing hot path; MIT license is unambiguous for open-core commercial use; BERT/WordPiece tokenizer is natively covered in .NET. fp32 CPU inference for a ~33M-param/384-dim model is already low-single-digit ms (well inside the 50ms budget); int8 buys ~4× smaller artifact + memory headroom under concurrency at <1–2 MTEB-pt cost.

**Alternatives considered**: `gte-small` — near-identical, kept as pinned fallback. `e5-small-v2` — mandatory `query:`/`passage:` prefixes don't map cleanly to "embed one prompt for intent clustering." `all-MiniLM-L6-v2` — smallest/fastest and most battle-tested in .NET ports, but lowest MTEB risks fuzzier cluster boundaries; reserve if the latency budget proves tight on target hardware. `nomic-embed-text-v1.5` — highest raw MTEB + long context, but ~4× compute and 548MB fp32 works against "ship in a container," and long-context is wasted on short routing prompts.

*Sources*: HF model cards (BAAI/bge-small-en-v1.5, thenlper/gte-small, intfloat/e5-small-v2, nomic-ai/nomic-embed-text-v1.5), MTEB leaderboard, Microsoft.ML.Tokenizers BertTokenizer docs, ONNX Runtime C# BERT tutorial.

---

## R2. Cross-format translation matrix (design §4 — "the hard part")

**Decision**: Maintain a neutral internal model and treat translation as **capability-gated**, not best-effort. Build a **per-model capability table** and enforce **pre-route guardrails** that return an explicit error to the client rather than silently dropping any capability the request actually exercises. The most important structural facts driving the adapters:

- **Streaming granularity differs fundamentally.** Anthropic emits named events with explicit block open/close bracketing (`message_start` → `content_block_start`/`content_block_delta`/`content_block_stop` → `message_delta` → `message_stop`); OpenAI emits unnamed `chat.completion.chunk` deltas terminated by `data: [DONE]`; **Gemini emits whole `GenerateContentResponse` objects per chunk** (client must diff). Translating to/from Gemini requires the router to **synthesize** the finer-grained Anthropic block bracketing / OpenAI `tool_calls[].index` from Gemini's coarse chunks — there is no 1:1 frame mapping.
- **Tool-call identity doesn't round-trip.** Anthropic (`tool_use_id`) and OpenAI (`tool_call_id`) pair a call to its result by explicit id; **Gemini `functionResponse` matches by name only** → parallel same-named calls cannot be safely disambiguated through Gemini. OpenAI nests tools under `function` and serializes `arguments` as a **string**; Anthropic/Gemini pass an object. Gemini's tool-param schema is a **restricted OpenAPI subset** (`$ref`/`oneOf`/`anyOf`/many `format` values don't survive) and has **no `strict` guarantee**.
- **Vision**: Anthropic is **base64-only** (`source.base64`); OpenAI supports **remote `image_url`** + a `detail` fidelity knob; Gemini uses `inline_data` (base64) or `file_data.file_uri` (**pre-registered** via Files API). An OpenAI remote-URL image routed to Anthropic/Gemini must be **fetched + re-encoded by the router**.
- **Prompt caching**: Anthropic is **explicit** (`cache_control` breakpoints, 5m/1h TTL); OpenAI is **automatic/unaddressable** (+ optional `prompt_cache_key`); Gemini is **implicit** + a separate **explicit `CachedContent` resource** (out-of-band lifecycle). `cache_control` pin semantics **cannot** be honored on OpenAI.
- **Reasoning / "thinking"** is the **strongest no-round-trip zone**: Anthropic `signature`, OpenAI Responses `encrypted_content`, Gemini `thoughtSignature` are three opaque blobs, each **provider- and model-bound** — none replay across providers, and a signature is silently ignored even by a *different model on the same provider*. OpenAI **Chat Completions has no thinking/reasoning item at all**. Effort vocabularies differ (enum vs numeric `budget_tokens`/`thinkingBudget`).

**Guardrail rules (enforced pre-dispatch, return 4xx not a silent downgrade)**: refuse to route when the request exercises a capability the candidate provider would silently drop — vision→non-vision model; remote image-URL→provider that can't dereference it (unless the router fetches+re-encodes); explicit guaranteed cache pin→provider that can't honor it; **multi-turn reasoning with a signature→a different model/provider than produced it (pin the whole conversation to one model)**; thinking blocks→OpenAI Chat Completions; parallel same-named tool calls→Gemini; `strict:true`→Gemini; unsupported schema dialect→Gemini; numeric reasoning budget→enum-only provider (map + surface the approximation).

**Rationale**: Silent degradation is worse than an explicit error — the caller cannot detect it after the fact, and "faithful preservation" (FR-011) is a spec requirement and a trust-wedge claim. Gating on a declared capability table (evaluated before dispatch) is cheaper and safer than discovering incompatibility via a failed/garbage upstream call.

**Alternatives considered**: Best-effort silent stripping (rejected — violates FR-011 and the transparency posture). Raw byte SSE passthrough (rejected — impossible when re-serializing into the client's original format). Supporting only same-provider routing in v1 (rejected — cross-provider "cheapest capable" is the core value; instead we gate the specific capabilities that can't cross).

*Sources*: Anthropic docs (streaming, tool use, prompt caching, extended thinking); OpenAI docs (function calling, images/vision, prompt caching, reasoning, streaming events); Gemini docs (text generation/streaming, function calling, image understanding, context caching, thinking, generateContent REST).

---

## R3. ASP.NET Core streaming, upstream HttpClient, BYOK, persistence

**Decision (streaming)**: Use **.NET 10 native SSE** — parse the upstream with `System.Net.ServerSentEvents` (`SseParser<T>`), transform via `await foreach` into `IAsyncEnumerable<SseItem<T>>`, and emit with `TypedResults.ServerSentEvents(...)`. **Not** a raw byte tunnel (the router re-serializes into the client's format). Propagate `HttpContext.RequestAborted` into both the upstream call and the enumeration. Disable response buffering/compression for `text/event-stream` (and set `X-Accel-Buffering: no` for reverse proxies).

**Decision (upstream client)**: **Typed `HttpClient`** via `AddHttpClient<T>()` with a `SocketsHttpHandler` (`PooledConnectionLifetime ~15m`, `PooledConnectionIdleTimeout ~5m`, `MaxConnectionsPerServer` sized to concurrency). Set `Timeout = Timeout.InfiniteTimeSpan` and cancel via a per-call token linked to `RequestAborted`. Call `SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct)`. Apply **`Microsoft.Extensions.Http.Resilience` retries ONLY to non-streaming calls** (routing lookups, model list) — never to the streaming completion client, because a retry after `ResponseHeadersRead` would resend the prompt and **double-charge / duplicate** already-streamed tokens.

**Decision (BYOK)**: **Direct AES-256-GCM** via `System.Security.Cryptography.AesGcm` behind an `ISecretCipher`, master key from an `IMasterKeyProvider` (v1: `LocalFileMasterKeyProvider` reading a key file **outside** the SQLite DB, OS-permission-restricted; hosted: `KmsMasterKeyProvider`). Store `keyVersion || nonce(12B) || tag(16B) || ciphertext` per row; **fresh random 12-byte nonce every encryption** (GCM nonce reuse is catastrophic). Rotation = decrypt-old/re-encrypt-new tracked by `keyVersion`.

**Decision (persistence)**: One `DbContext`; **SQLite default** (`UseSqlite`, single-file self-host) with **optional Npgsql**, selected by config at startup. **Per-provider migrations assemblies** (`Migrations/Sqlite`, `Migrations/Npgsql`). BYOK column via an EF Core **`ValueConverter`** closing over the injected `ISecretCipher` at model-build time (value-converted columns aren't LINQ-queryable — fine, keys are looked up by id/provider, never by plaintext).

**Rationale**: These are Microsoft's blessed patterns for streaming proxies + socket/DNS hygiene; the resilience carve-out prevents the concrete double-charge failure mode; direct AesGcm (vs Data Protection API) gives a **portable** single-file self-host whose encrypted rows survive copying the DB between machines, and a clean `IMasterKeyProvider` swap to KMS for hosted mode with **zero schema change**.

**Alternatives considered**: ASP.NET Data Protection API for BYOK (rejected — key-ring lifecycle is opaque and tied to the machine, awkward for a portable DB). Raw-byte streaming proxy (rejected — see R2). Single migration set for both DB providers (rejected — column types differ; maintain one set per provider).

*Sources*: Microsoft Learn (TypedResults.ServerSentEvents, .NET 10 release notes, HttpClient guidelines, IHttpClientFactory, HTTP resilience, AesGcm, EF Core value conversions & multi-provider migrations); community SSE-behind-proxy write-ups.

---

## R4. Routing policy — cluster-scorer, confidence, policy table, k, session key (design Open Q2)

**Decision (confidence)**: Compute cosine distances to all k centroids, then a **temperature-scaled softmax over negative distances**; confidence = top-1 probability `p₁`, with temperature `T` calibrated offline (minimize ECE on held-out data). **Confidence floor τ default 0.55–0.60**; **binary escalation**: `p₁ < τ → route to the fixed strong default model`, no blending. The receipt records raw top-1/top-2 cosine sim **and** margin **and** softmax confidence (raw values are what a human debugs from).

**Decision (policy table)**: Offline job, per cluster `c`: compute `Q(c,m)` (OmnisBench quality on cluster-`c` prompts) and `Cost(c,m)` (using that **cluster's** token profile, not a global average); keep candidates within a **relative quality band** `Q(c,m) ≥ Q_max(c)·(1−ε)`, **default ε=0.05**; **rank survivors ascending by cost** → that ranked list *is* the row ("cheapest capable" = index 0); **store the full ranked list** (enables provider-outage failover to next candidate and a caller `cost_tier` dial); require **n(c,m) ≥ 30** benchmark prompts per cell or mark the row `low_confidence` (auto-escalate). Version the whole table (`policy_table_version`, stamped into every receipt — ties FR-006 reproducibility to a specific offline run).

**Decision (k)**: **Start at k=64** (anchored to Avengers-Pro's validated k=60 on a comparable mixed coding/knowledge/math workload; convenient power of two). Re-pick via elbow + silhouette sweep over {8,16,32,64,96,128,192}, **capped by the n≥30 statistical-power constraint** for the OmnisBench prompt budget. Treat k as a job parameter, not a code constant.

**Decision (session key — design Open Q2)**: **Support both, client header preferred.** Use client-supplied **`X-Omnis-Session-Id`** if present; else derive `HMAC-SHA256(server_secret, tenant_id ‖ system_prompt ‖ first_user_message)` truncated to **128 bits**. Hash **only the first message + system prompt** (never the growing transcript — that would mint a new key every turn and defeat pinning). Use **HMAC with a server secret** (plain SHA-256 makes the key a precomputable fingerprint / cache-timing oracle). Namespace by tenant. Never return the raw key to callers.

**Rationale**: Raw cosine alone is unstable across clusters of differing tightness; margin alone discards magnitude; softmax folds both into one calibratable scalar the escalation gate can use. Relative quality band is **self-calibrating** as the model frontier shifts (no per-cluster hand-tuning) and keeps "what was even eligible" auditable (vs an opaque blended score). Both-source session key covers heterogeneous clients (raw OpenAI-compat/curl won't send a header) without breaking pinning when a client edits its first message.

**Alternatives considered**: raw-margin-only or raw-cosine-only confidence (kept as secondary receipt fields, not the decision variable); absolute per-cluster quality thresholds (brittle, re-tuned per benchmark revision — reserve as an optional override for high-risk clusters); learned confidence model (defer to v2 once production replay data exists); client-header-only or derived-hash-only session key (each silently loses pinning for part of the traffic). Prior art borrowed: Avengers-Pro (embed→cluster→per-cluster rank — the direct precedent; add the confidence floor + receipt it lacks), Martian (willingness-to-pay knob, ranked-list failover), OpenRouter Auto (`cost_tier` dial, always surface which model answered), RouteLLM (calibrate against real ground truth; simplest scorer won). Every surveyed competitor hides either the methodology or the decision mechanics — **treat the receipt schema as a stable, versioned public contract** (the wedge).

*Sources*: RouteLLM (arXiv:2406.18665), Avengers-Pro (arXiv:2508.12631), NotDiamond / uncertainty-routing (arXiv:2410.13284, 2502.11021), Martian, OpenRouter Auto Router docs.

**Assumption flag**: the session-key formula is original design reasoning derived from caching-proxy/CDN cache-key practice — **not** copied from a published router (none surveyed publish a session-pinning formula). Validate empirically during implementation.

---

## Consolidated decisions → Technical Context

| Question | Resolved value |
|---|---|
| Embedder | `bge-small-en-v1.5` int8 ONNX (fallback `gte-small`); `Microsoft.ML.Tokenizers.BertTokenizer` |
| Streaming | .NET 10 `System.Net.ServerSentEvents` + `TypedResults.ServerSentEvents`, parse-and-retranslate |
| Upstream client | Typed `HttpClient` + `SocketsHttpHandler`, `InfiniteTimeSpan`, `ResponseHeadersRead`; resilience on non-stream only |
| BYOK | AES-256-GCM (`AesGcm`) behind `ISecretCipher`/`IMasterKeyProvider`; local-file key v1 → KMS hosted |
| Storage | EF Core, SQLite default + optional Npgsql; per-provider migrations; encrypted-column `ValueConverter` |
| Confidence | softmax over −cosine distance, floor τ≈0.55–0.60, binary escalate |
| Policy table | relative quality band ε=0.05, rank by cost, full list stored, n≥30 per cell, versioned |
| Clusters k | 64 (job parameter; validated by elbow+silhouette under n≥30 cap) |
| Session key | client `X-Omnis-Session-Id` else `HMAC-SHA256(secret, tenant‖system‖first-user)`/128-bit |
| Installer / dashboard scope | thin `npx` installer in-repo (`installer/`); v1 dashboard = spend + savings + decisions table (minimal) |
