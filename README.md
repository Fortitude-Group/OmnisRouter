# OmnisRouter

**Open-core, drop-in LLM routing proxy** — routes each request to the cheapest *capable* model with
**fully open, reproducible routing** and a **per-request routing receipt** that shows exactly which
model was picked, how confident the router was, what alternatives it considered, and what you saved.

Point an existing client (Claude Code, Codex, Cursor, or any app) at OmnisRouter with a base-URL
change; it accepts **Anthropic Messages**, **OpenAI Chat Completions**, and **Gemini** formats,
routes to the cheapest capable model in your pool — possibly on a *different* provider — translates
faithfully (streaming, tools, vision, prompt caching, extended thinking), and returns the response in
your client's original format.

The transparency wedge: the routing model (in-process embedding → intent cluster → published policy
table) is **versioned and reproducible from public data**, and its `policy_version` is stamped into
every decision. No black-box centroids, no unpublished savings claims.

## Why

- **Cheapest capable, not cheapest.** A confidence floor escalates hard prompts to a strong model —
  and the escalation is visible in the receipt.
- **Receipts on every request** (`X-Omnis-*` headers + `POST /v1/route` + an NDJSON decision log) —
  see [contracts](./specs/001-omnisrouter/contracts/).
- **Never picks a model it can't call** — routes only to providers you've given a BYOK key.
- **Refuse, don't silently drop** — if a candidate can't honor a capability (vision, cache pin,
  thinking-signature continuity, strict schema), the router returns an explicit error.
- **BYOK, encrypted at rest** (AES-256-GCM); prompts leave your infra only to the chosen upstream.
- **Single process + SQLite** self-host (Postgres optional); one binary or `docker compose`.

## Endpoints

| Route | Purpose |
|---|---|
| `POST /v1/chat/completions` · `POST /v1/messages` · `POST /v1beta/models/{model}:{action}` | Routed, in each wire format |
| `POST /v1/route` | Full routing decision, **no upstream call, no cost** |
| `GET /v1/analytics/routing-decisions` | NDJSON decision-log export (content-free) |
| `GET /v1/models` · `POST/GET/DELETE /v1/keys` | Candidate pool · BYOK key management |
| `GET /ui` · `GET /health` · `GET /readyz` | Self-host dashboard · probes |

See [`docs/api.md`](./docs/api.md) for the full surface + receipt headers.

## Getting started

Run the published image, or grab a self-contained binary for your platform from the
[latest release](https://github.com/Fortitude-Group/OmnisRouter/releases/latest) (no .NET needed):

```bash
# Published container (ships the ONNX embedder)
docker run -d -p 8080:8080 -v omnisrouter-data:/data ghcr.io/fortitude-group/omnisrouter:latest

# or build from source (.NET 10)
docker compose -f deploy/docker-compose.yml up -d --build
dotnet run --project src/OmnisRouter.Api      # http://localhost:8080
```

Then set a bootstrap token (`Omnis:BootstrapToken`), add a BYOK key (`POST /v1/keys`), and point a
client at the router with the published helper:

```bash
npx omnisrouter-cli@latest --url http://localhost:8080 --token <router-token> --client cursor --write
```

Full operator guide: [`docs/self-host.md`](./docs/self-host.md).

## Observe a flat-rate Claude subscription

A subscription can't be proxied, so the same binary has a `collect` mode that reads Claude Code's
local transcripts and reports the usage to your OmnisVigil dashboard, content-free:

```bash
omnisrouter collect --url https://app.omnisvigil.com --key <project-key> --all --watch
```

On Windows you can install this as a background tray app instead of leaving a console open. It starts
at login, runs with no window, and shows a small icon for whether collection is healthy:

```powershell
winget install OmnisRouter
```

The same tray can also run the router itself as a local proxy on `127.0.0.1`, so you route through
your own provider keys with no Docker and no console. It wires Claude Code, Codex or Cursor to the
router in one click (and reverts them cleanly), and routed spend lands on your dashboard next to the
subscription usage, counted once.

See [`docs/collect-tray.md`](./docs/collect-tray.md).

## Reproducible routing model

The routing model is built by a documented, deterministic offline job — see
[`routing/BUILD.md`](./routing/BUILD.md). Same inputs → byte-identical model. Each tagged release must
pass the gate in [`scripts/release-gate.ps1`](./scripts/release-gate.ps1) (clean build + green tests +
a passing OmnisBench run) and publishes its benchmark frontier.

The shipped model uses the pinned ONNX `bge-small-en-v1.5` embedder for semantic intent clustering,
and its **coding + math** policy is driven by **real [OmnisBench](https://github.com/Fortitude-Group/OmnisBench)
measurements** (via `scripts/omnisbench_to_benchresults.py` + `merge_benchresults.py`) — the other
domains use estimates pending broader OmnisBench coverage. See [`docs/calibration.md`](./docs/calibration.md).

## Cache hygiene

Routing lowers the price per token. Cache hygiene lowers how many tokens you pay the cache *write*
premium for, on the same model, with no change to output. Prompt caching only refunds that premium as
a cheap read when the prefix comes back byte-for-byte, and the match runs front to back, so one stray
carriage return, a moved timestamp or a reordered tool list near the top throws the whole cached
prefix away and you pay to write it again. Because the router sits in the request path it holds the
exact bytes and gets the real cache result back, so it can measure that waste as a pound figure and,
where a fix is provably safe, remove the cause before it costs anything. Anthropic-first, where the
explicit cache breakpoint makes the prefix exact.

Measurement is on by default and content-free, so you see the waste with no configuration. Every fix
that changes the bytes you sent is off until you turn it on, one class at a time, and each is applied
only where it can prove it preserves meaning on that request (otherwise it skips and reports the miss
measured-only). Three fixes ship, each provably safe:

- `line_ending` — normalise CRLF/CR to LF in text content
- `trailing_whitespace` — strip trailing spaces and tabs at the end of each line
- `tool_ordering` — sort tool definitions and canonicalise their JSON key order, only where the wire
  treats tools as an unordered set

Three other avoidable causes are measured but have no automatic fix, because a safe transform can't be
proven: a volatile header or an injected timestamp before the cache breakpoint, and non-deterministic
file concatenation. Those are a one-off change at the source, keep volatile lines (timestamps, branch
names, coverage numbers) out of the first few lines of a `CLAUDE.md`, and make any file concatenation
deterministic.

### Enabling it

Configuration lives under the `CacheHygiene` section. In `appsettings.json`:

```json
{
  "CacheHygiene": {
    "MeasurementEnabled": true,
    "EmitToVigil": true,
    "EnabledFixes": [ "LineEnding", "TrailingWhitespace", "ToolOrdering" ],
    "Billing": "PayAsYouGo"
  }
}
```

Or as environment variables (arrays are indexed):

```bash
CacheHygiene__EmitToVigil=true
CacheHygiene__EnabledFixes__0=LineEnding
CacheHygiene__EnabledFixes__1=TrailingWhitespace
CacheHygiene__EnabledFixes__2=ToolOrdering
```

- `EnabledFixes` is empty by default, so nothing mutates a request until you list it. The names are
  `LineEnding`, `TrailingWhitespace` and `ToolOrdering` (they map to the `line_ending`,
  `trailing_whitespace` and `tool_ordering` labels on the receipt).
- `EmitToVigil` is **off by default**. Turn it on to send the content-free cache-waste figures up to
  your OmnisVigil dashboard. It's gated so an older Vigil can't reject a receipt over a newer field, so
  if the dashboard shows "no cache-waste data yet", this is usually why.
- `Billing` is `PayAsYouGo` by default. Set it to `Subscription` if you route through a flat-rate
  plan, so the figures show as shadow estimates rather than money owed.
- If OmnisVigil is serving a cache-fixes policy, that policy is authoritative and overrides
  `EnabledFixes` here. With no Vigil policy, the router uses this local config.
- These are read at startup, so change them and restart the router.

## Build & test

```bash
dotnet build OmnisRouter.slnx -c Release   # 0 warnings / 0 errors
dotnet test  OmnisRouter.slnx              # all green
```

## License

Apache-2.0 — see [LICENSE](./LICENSE). Copyright 2026 Fortitude Omnis Group.
