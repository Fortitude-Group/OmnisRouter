# Quickstart & Validation Guide: OmnisRouter v1

A runnable path proving the feature works end-to-end. Detailed shapes live in [contracts/](./contracts/) and [data-model.md](./data-model.md) — this guide references them rather than repeating them. (Commands are indicative of the planned layout; concrete task breakdown is produced by `/speckit-tasks`.)

## Prerequisites

- .NET 10 SDK
- An operator BYOK key for at least one provider (Anthropic / OpenAI / Gemini), supplied via env (never committed)
- The shipped routing model in `routing/` (`centroids-<ver>.bin` + `policy-<ver>.json`)

## Build, test, run

```powershell
dotnet restore OmnisRouter.slnx
dotnet build   OmnisRouter.slnx -c Debug        # gate: 0 errors
dotnet test    OmnisRouter.slnx                 # gate: all green (conformance + routing + BYOK + streaming)
dotnet run --project src/OmnisRouter.Api        # starts Kestrel + embedded SQLite; single process
```

## Validate each user story

### US1 — Drop-in cost routing (P1)
1. Add a BYOK key + get a router token (via `/ui` or seed config).
2. Point an OpenAI-style client at the router base URL; send a **simple** prompt and a **hard** prompt.
3. **Expect**: both return valid OpenAI responses; `X-Omnis-Model` shows a *cheaper* model on the simple prompt and a stronger one (or `ESCALATED`) on the hard one.
```powershell
curl -H "Authorization: Bearer $env:ROUTER_TOKEN" -H "Content-Type: application/json" `
  http://localhost:8080/v1/chat/completions `
  -d '{"model":"auto","messages":[{"role":"user","content":"2+2?"}]}' -i   # inspect X-Omnis-* headers
```

### US2 — Receipts & transparency (P2)
1. `POST /v1/route` with any request body → full decision, **no upstream call, no cost** ([route-endpoint.md](./contracts/route-endpoint.md)).
2. Confirm the routed response headers carry the full receipt set ([wire-formats.md](./contracts/wire-formats.md)).
3. `GET /v1/analytics/routing-decisions` → NDJSON; confirm the two US1 requests appear, content-free.

### US3 — Reproducible routing (P3)
1. Re-run the offline build: `dotnet run --project src/OmnisRouter.RoutingModel.Build -- build-model --k 64 --epsilon 0.05 --min-samples 30 --out routing/`.
2. **Expect**: emitted `centroids`/`policy` match the shipped version (decision-equivalent within tolerance); `policy_version` in receipts matches.
3. Run the **golden routing-model test** (fixed inputs → stable decisions).

### US4 — Feature preservation across formats (P2)
1. Send a **streaming** request in each format that routes to a model on a *different* provider → correct incremental stream in the original format.
2. Send **tool-call** and **vision** requests cross-provider → preserved.
3. Trigger a guardrail (e.g. vision request → a non-vision candidate) → **explicit 400** naming the capability, never a silent text-only answer.
4. Multi-turn session → turns stay pinned (`X-Omnis-Session-Pin: applied`) until the intent changes.

### US5 — Secure BYOK self-host (P2)
1. Inspect the SQLite file: `ProviderKey.ApiKeyEncrypted` is ciphertext, **not** readable plaintext.
2. Grep logs, receipts, and the decision export for the key and for prompt text → **no matches** (SC-007).
3. Confirm the process runs with no external service dependency for core routing (SC-008).

## Success-criteria checkpoints

| Check | Criterion |
|---|---|
| First routed response reachable with only a base-URL + key change | SC-001 |
| Added routing overhead ≤50ms p95 vs calling the provider directly | SC-003 |
| 100% of responses carry a full receipt; 100% of requests in the log | SC-004 |
| Rebuild reproduces the routing model within tolerance | SC-005 |
| Cross-format streaming/tools/vision correct | SC-006 |
| No key/prompt leakage; keys encrypted at rest | SC-007 |
| Release gated on clean build + green tests + passing OmnisBench run | SC-009 / FR-018 |
