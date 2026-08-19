# Building the routing model yourself

This documents the reproducible offline build (T065/T066) that produces
`centroids-<ver>.bin` + `policy-<ver>.json` (format: [FORMAT.md](./FORMAT.md)). It supersedes the
hand/quick-built seed pair (`centroids-seed.bin` / `policy-seed.json`, `src/OmnisRouter.RoutingModel.Build/seed/`)
that only exists so US1 could route end-to-end before this pipeline landed.

**Reproducible** means: given the same dataset file, the same bench-results file, the same
`config/models.yaml` + pinned pricing snapshot, and the same `--k`/`--epsilon`/`--min-samples`/`--version`
flags, re-running the build produces **byte-identical** `centroids-<ver>.bin` and **textually identical**
`policy-<ver>.json` output, every time, on any machine. `tests/OmnisRouter.Routing.Tests/BuildReproducibilityTests.cs`
(T064) verifies this by invoking the builder twice into separate temp directories and diffing the
output. `tests/OmnisRouter.Routing.Tests/GoldenRoutingModelTests.cs` (T063) then verifies that the
*committed* `v1-2026-08-19` model, loaded the same way the app loads it, produces stable routing
decisions for a handful of fixed prompts — so a routing-behavior regression breaks CI even if nobody
touched the routing code.

## Rebuild it

```bash
dotnet run --project src/OmnisRouter.RoutingModel.Build -- build-model
```

With no flags this reads the bundled sample dataset/bench-results and writes
`routing/centroids-v1-2026-08-19.bin` + `routing/policy-v1-2026-08-19.json` (overwriting the committed
pair with the same bytes, since the inputs haven't changed). Full flag list:

| Flag | Default | Meaning |
|---|---|---|
| `--dataset` | `routing/datasets/sample-prompts.jsonl` | Labeled-prompt dataset (JSONL) |
| `--bench-results` | `routing/datasets/sample-bench-results.json` | Per-domain benchmark quality scores |
| `--k` | `8` | Cluster count (k-means) |
| `--epsilon` | `0.05` | Relative quality band width |
| `--min-samples` | `5` | Minimum dataset prompts/cluster before the row is trusted |
| `--out` | `routing/` | Output directory for the `.bin`/`.json` pair |
| `--version` | `v1-2026-08-19` | `<ver>` tag stamped into `policy_version` and the output filenames |

`config/models.yaml` (candidate pool + capabilities) and the pinned `config/pricing/<date>.yaml`
snapshot (latest by filename if unspecified) are read directly — they aren't flags, since they're
already the single source of truth the rest of the app reads.

## Dataset format (`--dataset`, JSONL)

One JSON object per line:

```json
{"text": "Write a Python function to reverse a linked list.", "domain": "coding"}
{"text": "What's the best way to train for a half marathon?", "domain": "general"}
```

`domain` is a free-text label (`coding`, `general`, `math`, ... — whatever labels your
`--bench-results` file has quality rows for). It is **not** used for clustering itself (clustering is
unsupervised, over the embedding vectors only) — it's used *after* clustering, to pick which
bench-results row scores each cluster (see below). The bundled `routing/datasets/sample-prompts.jsonl`
has 80 prompts (40 `coding`, 40 `general`); production would use a much larger, broader-domain corpus
sized to support `k=64` under the `n>=30`-per-cluster statistical-power constraint (research.md R4).

## Bench-results format (`--bench-results`, JSON)

```json
{
  "domains": {
    "coding": { "anthropic/claude-opus-4-8": 0.96, "openai/gpt-5": 0.93, "...": 0.0 },
    "general": { "anthropic/claude-opus-4-8": 0.93, "openai/gpt-5": 0.90, "...": 0.0 }
  }
}
```

Keys under each domain are `"<provider>/<model_id>"` (provider lowercased, matching
`config/pricing/<date>.yaml`'s casing), one entry per candidate in `config/models.yaml` you want
scored for that domain. Values are a 0-1 benchmark quality score. **In the real pipeline this file is
produced by OmnisBench**, run against the labeled dataset for every candidate model — the bundled
`routing/datasets/sample-bench-results.json` is a small hand-authored sample with the identical shape
(placeholder numbers, not real benchmark output), so the pipeline downstream is exercised exactly as
it would be against real OmnisBench output.

## What the build does

1. **Load** the dataset and bench-results files, and the candidate pool from `config/models.yaml`.
2. **Embed** every prompt with `HashingEmbedder` (dim 384, deterministic, dependency-free — the same
   fallback the app itself uses in dev/CI). Production would pin the ONNX `bge-small-en-v1.5` embedder
   (`OnnxEmbedder`, research.md R1) instead; the pipeline from here down is identical either way, since
   both implement `IEmbedder` — only the embedding vectors would differ.
3. **Cluster** the embeddings with a deterministic spherical k-means (`SphericalKMeans`): fixed-seed
   initial centroid selection, cosine-similarity assignment (a plain dot product since every vector is
   unit-length), centroid update = mean of assigned vectors re-normalized to unit length, empty
   clusters deterministically reseeded from the point farthest from its own centroid. Same seed + same
   input vectors -> same centroids, always.
4. **Build the policy table.** For each cluster: find its *dominant domain* (majority vote over the
   dataset prompts k-means assigned to it, ties broken alphabetically); look up that domain's
   bench-results row; keep every candidate with quality `>= qmax*(1-epsilon)`; rank survivors ascending
   by estimated cost (via `IPricingBook`, using a fixed representative token profile purely for
   *ranking* — production would use each cluster's actual OmnisBench token profile, per research.md
   R4, since ranking order only needs a consistent profile applied to every candidate, not the "right"
   absolute dollar figure). A cluster is marked `"low_confidence": true` when it has fewer than
   `--min-samples` prompts, or when its dominant domain has no bench-results row at all.
5. **Emit** `centroids-<ver>.bin` (routing/FORMAT.md's binary layout) and `policy-<ver>.json` (same
   JSON shape the loader expects, plus three extra per-cluster debug fields —
   `low_confidence`/`sample_count`/`dominant_domain` — that `RoutingModelLoader` simply ignores as
   unmapped JSON properties).

## Determinism, end to end

Every step above is deterministic given its inputs: the embedder has no randomness; k-means'
"randomness" is one fixed `Random(seed)` used only to pick initial centroid indices and to break
empty-cluster ties (both driven by the same seed, and every runtime tie -- nearest-centroid, dominant
domain -- is broken by a fixed rule, never by hash-code or iteration-order accidents); the quality band
and cost ranking are pure functions of the (already-deterministic) cluster assignments plus the input
files. That's what makes "same inputs -> same bytes" true, and what `BuildReproducibilityTests`
checks directly.
