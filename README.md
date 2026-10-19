# OmnisRouter

A self-hosted proxy that sends each LLM request to the cheapest model that can actually do it, and
hands you a receipt saying why.

Point Claude Code, Codex, Cursor or your own app at it by changing the base URL. It speaks the
Anthropic Messages, OpenAI Chat Completions and Gemini formats, so a request can come in as one, go
out to a different provider's model, and come back in the format your client sent (streaming, tools,
images, prompt caching and extended thinking included). Apache-2.0, your own API keys, and nothing in
the middle but you.

## Why I built it

My coding agents were sending everything to the most expensive model. Most of those requests don't
need a frontier model. Some really do. Trouble is, a monthly bill can't tell you which, so every
request gets the expensive one, just in case.

OmnisRouter makes that call per request and shows its working:

- **Cheapest capable, not cheapest.** If the router isn't confident about a prompt it escalates to a
  strong model, and the receipt says so.
- **A receipt on every request.** `X-Omnis-*` response headers, a `POST /v1/route` endpoint that shows
  the decision without calling anyone (so it costs nothing), and a content-free NDJSON decision log.
  The formats are in [contracts](./specs/001-omnisrouter/contracts/).
- **It only routes to providers you've given a key.** It won't pick a model it can't call.
- **It refuses rather than quietly dropping things.** If the cheaper model can't honour something the
  request needs (images, a cache pin, thinking-signature continuity, a strict schema), you get an
  explicit error, not a worse answer.
- **Your keys, encrypted at rest** (AES-256-GCM). Prompts only go to the upstream model that was picked.
- **One process and a SQLite file.** Run the binary or `docker compose`. Postgres is optional.

How it decides: an embedding model running in-process (`bge-small-en-v1.5`, ONNX) puts the prompt in
an intent cluster, then a published policy table picks the cheapest model that clears the quality bar
for that cluster. Making the decision needs no external calls. Every receipt carries the
`policy_version`, and the policy rebuilds byte-for-byte from public data, so you can check it rather
than take my word for it.

## Getting started

Run the published image (or grab a self-contained binary from the
[latest release](https://github.com/Fortitude-Group/OmnisRouter/releases/latest), no .NET needed). Set a
bootstrap token on first run so you can authenticate straight away:

```bash
docker run -d -p 8080:8080 \
  -e Omnis__BootstrapToken=my-secret-token \
  -v omnisrouter-data:/data \
  ghcr.io/fortitude-group/omnisrouter:latest

curl http://localhost:8080/health          # {"status":"ok"}
```

Add a key for each provider you want in the pool. The router only routes to providers it has a key for:

```bash
curl -X POST http://localhost:8080/v1/keys \
  -H "Authorization: Bearer my-secret-token" -H "Content-Type: application/json" \
  -d '{"provider":"anthropic","label":"primary","api_key":"sk-ant-..."}'

curl -X POST http://localhost:8080/v1/keys \
  -H "Authorization: Bearer my-secret-token" -H "Content-Type: application/json" \
  -d '{"provider":"openai","label":"primary","api_key":"sk-..."}'
```

Ask it what it would do with a prompt. No upstream call, no cost:

```bash
curl -X POST http://localhost:8080/v1/route \
  -H "Authorization: Bearer my-secret-token" -H "Content-Type: application/json" \
  -d '{"messages":[{"role":"user",
        "content":"Generate a git commit message for a fix to a null-reference crash in the payment retry loop."}]}'
```

The reply is the receipt: the model it picked, how confident it was, what else it weighed, and the
saving against the strongest candidate:

```json
{
  "decision": "ROUTED",
  "chosen": { "provider": "anthropic", "model_id": "claude-haiku-4-5" },
  "confidence": 0.21,
  "confidence_floor": 0.2,
  "alternatives": [
    { "model_id": "claude-haiku-4-5", "predicted_quality": 0.87, "est_cost_usd": 0.002583 },
    { "model_id": "gpt-5",            "predicted_quality": 0.90, "est_cost_usd": 0.005149 },
    { "model_id": "claude-opus-5",    "predicted_quality": 0.93, "est_cost_usd": 0.012915 }
  ],
  "est_cost_usd": 0.002583,
  "est_cost_delta_vs_big_usd": -0.010332,
  "policy_version": "v3-omnisbench-2026-08-20"
}
```

So a commit message goes to Haiku at about a fifth of the flagship's price. What it picks depends on
the prompt and which providers you've keyed. When confidence drops below the floor it doesn't gamble
on a cheap model: the receipt says `"decision": "ESCALATED"` and the request goes to a strong one.

Then point a client at it:

```bash
npx omnisrouter-cli@latest --url http://localhost:8080 --token my-secret-token --client cursor --write
```

To build from source instead:

```bash
docker compose -f deploy/docker-compose.yml up -d --build
# or, with the .NET 10 SDK:
dotnet run --project src/OmnisRouter.Api        # http://localhost:8080
```

The operator guide is [`docs/self-host.md`](./docs/self-host.md), and the full API with the receipt
headers is [`docs/api.md`](./docs/api.md).

## Endpoints

| Route | Purpose |
|---|---|
| `POST /v1/chat/completions` · `POST /v1/messages` · `POST /v1beta/models/{model}:{action}` | Routed, in each wire format |
| `POST /v1/route` | The routing decision on its own. No upstream call, no cost |
| `GET /v1/analytics/routing-decisions` | Decision log export (NDJSON, content-free) |
| `GET /v1/models` · `POST/GET/DELETE /v1/keys` | Candidate pool · your provider keys |
| `GET /ui` · `GET /health` · `GET /readyz` | Self-host dashboard · probes |

## What's measured and what isn't

The routing is only as good as the quality numbers behind it, so here's where they come from.

- **Coding and maths** are scored from [OmnisBench](https://github.com/Fortitude-Group/OmnisBench) runs,
  my open benchmark. Every stored model answer is in the results file, and `omnisbench verify`
  re-grades the lot offline with no API calls.
- **Everything else** (SQL, reasoning, writing, Q&A, summarising, translation) runs on estimates for
  now. They're labelled as estimates in the build inputs, and clusters with thin data are marked
  low-confidence, which makes the router escalate rather than guess.
- Widening the measured domains is the next job. If your workload sits in an estimated cluster, treat
  the router's choices there as a starting point and check the receipts.

The routing model comes out of a deterministic offline build, described in
[`routing/BUILD.md`](./routing/BUILD.md): same inputs, byte-identical model. A tagged release has to
pass [`scripts/release-gate.ps1`](./scripts/release-gate.ps1) first (clean build, green tests, a
passing OmnisBench run). Tuning notes are in [`docs/calibration.md`](./docs/calibration.md).

## Watching a flat-rate Claude subscription

You can't proxy a subscription, so the same binary has a `collect` mode. It reads Claude Code's local
transcripts and reports the usage (token counts, no content) to an OmnisVigil dashboard:

```bash
omnisrouter collect --url https://app.omnisvigil.com --key <project-key> --all --watch
```

On Windows there's a tray app instead of a console window. Get `OmnisRouter-<version>-win-x64.msi` from
the [latest release](https://github.com/Fortitude-Group/OmnisRouter/releases/latest). It starts at
login, runs with no window, and its icon shows whether collection is healthy. It can also run the
router as a local proxy on `127.0.0.1`, wire Claude Code, Codex or Cursor to it in one click (and put
them back), and count routed spend alongside the subscription usage without double-counting. More in
[`docs/collect-tray.md`](./docs/collect-tray.md).

OmnisVigil is the paid hosted dashboard I build on top of the receipts (spend by project and person,
budget caps, a kill switch). You don't need it. The router is Apache-2.0 and stays that way.

## Cache hygiene

Routing lowers the price per token. Cache hygiene cuts how often you pay the cache *write* price on
the same model, with no change to the output.

Prompt caching only gives you the cheap read when the start of the prompt comes back byte-for-byte
identical, and the match runs front to back. One stray carriage return, a moved timestamp or a
reordered tool list near the top, and the whole cached prefix gets thrown away and written again at
the higher price. The router sits in the request path, holds the exact bytes and sees the real cache
result come back, so it can measure that waste in pounds and, where a fix is provably safe, remove the
cause before it costs you. It's Anthropic-first, because the explicit cache breakpoint makes the
prefix exact.

Measuring is on by default and content-free. Every fix that changes the bytes you sent is off until
you turn it on, one at a time, and each one only applies where it can prove the meaning is unchanged
for that request. Otherwise it skips and just reports the miss. Three fixes ship:

- `line_ending`: normalise CRLF and CR to LF in text content
- `trailing_whitespace`: strip trailing spaces and tabs from each line
- `tool_ordering`: sort tool definitions and canonicalise their JSON key order, only where the wire
  format treats tools as an unordered set

Three other causes are measured but not fixed automatically, because there's no way to prove a fix is
safe: a volatile header or an injected timestamp before the cache breakpoint, and file concatenation
in a different order each time. Those are a one-off change at the source. Keep timestamps, branch
names and coverage numbers out of the first few lines of a `CLAUDE.md`, and concatenate files in a
fixed order.

### Turning it on

Cache hygiene lives in the `CacheHygiene` section. Reporting the figures to OmnisVigil is a separate
switch in the `OmnisVigil` section. In `appsettings.json`:

```json
{
  "CacheHygiene": {
    "MeasurementEnabled": true,
    "EnabledFixes": [ "LineEnding", "TrailingWhitespace", "ToolOrdering" ],
    "Billing": "PayAsYouGo"
  },
  "OmnisVigil": {
    "EmitCacheWaste": true
  }
}
```

Or as environment variables (arrays are indexed):

```bash
OmnisVigil__EmitCacheWaste=true
CacheHygiene__EnabledFixes__0=LineEnding
CacheHygiene__EnabledFixes__1=TrailingWhitespace
CacheHygiene__EnabledFixes__2=ToolOrdering
```

- `EnabledFixes` is empty by default, so nothing touches a request until you list it. The names map
  to the `line_ending`, `trailing_whitespace` and `tool_ordering` labels on the receipt.
- `OmnisVigil:EmitCacheWaste` is **off** by default, and it's in the `OmnisVigil` section, not
  `CacheHygiene`. It's gated so an older OmnisVigil can't reject a receipt over a field it doesn't
  know. If the dashboard says "no cache-waste data yet", this is usually why. Waste is only measured on
  routed traffic, so a collect-only setup has nothing to send.
- `Billing` is `PayAsYouGo` by default. Set it to `Subscription` if you're on a flat-rate plan, and
  the figures show as shadow estimates rather than money owed.
- If OmnisVigil serves a cache-fixes policy, that wins over `EnabledFixes` here. With no policy from
  OmnisVigil, the local config applies.
- These are read at startup, so restart the router after changing them.

## Build and test

```bash
dotnet build OmnisRouter.slnx -c Release   # 0 warnings, 0 errors
dotnet test  OmnisRouter.slnx              # all green
```

## Licence

Apache-2.0, see [LICENSE](./LICENSE). Copyright 2026 Fortitude Omnis Group.
