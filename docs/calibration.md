# Routing calibration (T/τ/k)

The cluster-scorer has three tunable knobs. This documents the current defaults, the procedure to
calibrate them against real data, and what's shipped vs. deferred.

## Knobs & current defaults

| Knob | Where | Default | Meaning |
|---|---|---|---|
| **T** (temperature) | `ClusterScorerOptions.Temperature` | `0.15` | Sharpness of the softmax over negative cosine distances that produces the confidence score. |
| **τ** (confidence floor) | `ClusterScorerOptions.ConfidenceFloor` | `0.55` | Below this top-1 confidence the router escalates to the strong-default model (visible in the receipt). Research target range 0.55–0.60. |
| **k** (clusters) | build-model `--k` | `8` (sample) | Number of intent clusters. Research target **64** (anchored to Avengers-Pro's validated k=60); the bundled sample dataset is small, so the shipped model uses k=8. |

Override T/τ per deployment via `Routing:ClusterScorer:Temperature` / `:ConfidenceFloor` config.

## Calibration procedure (with real data)

1. **k** — embed the OmnisBench prompt corpus, sweep k ∈ {8,16,32,64,96,128,192}, plot inertia (elbow)
   and cross-check silhouette; **cap k so every (cluster, model) cell has ≥30 benchmark prompts**
   (the build job marks thin cells `low_confidence` → auto-escalate). Don't let clustering metrics
   push k past what the benchmark budget supports.
2. **T** — fit temperature to minimize expected calibration error (ECE) on a held-out set where
   "correct" = the cluster's cheapest-capable model actually met the quality bar.
3. **τ** — pick the floor that caps escalation at a target rate (start ≤15% of traffic) by replaying
   validation traffic; don't choose it in the abstract.

## Shipped vs. deferred (v1)

- **Shipped & reproducible now:** the whole pipeline (embed → k-means → relative-quality-band policy
  table), the release gate, golden + reproducibility tests. Same inputs → byte-identical model.
- **Deferred follow-ups** (make routing *semantically* strong, not just reproducible):
  - Pin the ONNX **bge-small-en-v1.5** embedder (currently the deterministic `HashingEmbedder`
    fallback is used so no model asset is required to build/run).
  - Feed **real OmnisBench** per-cluster quality/cost results as the bench-results input (currently a
    small sample), then run the calibration above and record the chosen T/τ/k here.
