# Routing model artifacts

This directory holds the versioned, reproducible routing-model artifacts that OmnisRouter's
in-process ONNX cluster-scorer loads at startup:

- `centroids-<ver>.bin` — the embedding-space cluster centroids (k ≈ 64 intent clusters) produced by
  the offline build job in `src/OmnisRouter.RoutingModel.Build/`.
- `policy-<ver>.json` — the per-cluster policy table: for each cluster, the ranked candidate models
  (from `config/models.yaml`) filtered by capability and ranked by cost, plus the confidence
  threshold used to decide when a request is routed with confidence versus falls back to a safe
  default.

Both files share the same `<ver>` (e.g. `centroids-2026-08-15.bin` / `policy-2026-08-15.json`) so a
centroid set and its policy table are always loaded as a matched pair.

## Reproducibility (FR-006)

These artifacts are **not hand-authored**. They are produced by re-running the build job in
`OmnisRouter.RoutingModel.Build` against public data (embedding training corpus + published
benchmark/capability data) and the pinned pricing snapshot in `config/pricing/`. Given the same
inputs and the same `<ver>` tag, the build is deterministic — re-running it reproduces byte-identical
`centroids-<ver>.bin` and `policy-<ver>.json` files. This is what the golden routing-model
determinism test in `tests/OmnisRouter.Routing.Tests/` checks: fixed inputs against the committed
artifacts must yield stable routing decisions.

## `policy_version` is stamped into every decision

The router loads exactly one `(centroids, policy)` pair at a time, identified by its `<ver>`. That
`<ver>` is recorded as `policy_version` on every `ModelDecision` / routing receipt (see
`specs/001-omnisrouter/contracts/routing-receipt.schema.json`), so any routing decision can always be
traced back to the exact model artifacts that produced it — even after newer artifacts have shipped.

## Shipping a new version

1. Run the build job (`OmnisRouter.RoutingModel.Build`) against the current public data + pricing
   snapshot, producing a new `<ver>` pair.
2. Commit both files together — never update one without the other.
3. Update the loader's default `<ver>` once the new pair has passed the golden determinism test.
