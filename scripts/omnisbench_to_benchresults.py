#!/usr/bin/env python3
"""Bridge: convert an OmnisBench results.json into OmnisRouter routing bench-results.

Closes the loop the program promised — OmnisBench's measured per-model quality drives OmnisRouter's
routing policy table. Aggregates each item's (chosen_model, dataset, passed) into per-model,
per-domain quality (mean pass rate), mapping OmnisBench datasets to routing domains.

Usage:
  python scripts/omnisbench_to_benchresults.py <omnisbench_results.json> [--out routing/datasets/bench-results-from-omnisbench.json]

Coverage is only as broad as the OmnisBench run: a run over humaneval+gsm8k with N models yields
real quality for {coding, math} x those models. Merge into the estimate file for full-pool coverage;
run OmnisBench over more datasets/models to widen it.
"""
import argparse
import json
from collections import defaultdict

# OmnisBench dataset -> OmnisRouter routing domain.
DATASET_TO_DOMAIN = {
    "humaneval": "coding",
    "mbpp": "coding",
    "livecodebench": "coding",
    "gsm8k": "math",
    "mmlu_pro": "reasoning",
}


def convert(results_path: str, policy_prefix: str | None = None) -> dict:
    """policy_prefix keeps only items from policies whose name starts with it. Use "always_" for a
    run with one always_model policy per model: each model then counts once per item. Without it,
    random and oracle rows are counted too, and those only cover the items they happened to pick."""
    with open(results_path, encoding="utf-8") as fh:
        data = json.load(fh)

    # (model, domain) -> [passed bools]
    cells: dict[tuple[str, str], list[bool]] = defaultdict(list)
    for item in data.get("items", []):
        model = item.get("chosen_model")
        dataset = item.get("dataset")
        passed = item.get("passed")
        if policy_prefix and not str(item.get("policy", "")).startswith(policy_prefix):
            continue
        if model is None or dataset is None or passed is None:
            continue
        domain = DATASET_TO_DOMAIN.get(dataset)
        if domain is None:
            continue
        cells[(model, domain)].append(bool(passed))

    domains: dict[str, dict[str, float]] = defaultdict(dict)
    counts: dict[str, dict[str, int]] = defaultdict(dict)
    for (model, domain), results in cells.items():
        domains[domain][model] = round(sum(results) / len(results), 4)
        counts[domain][model] = len(results)

    return {
        "_comment": (
            "Per-model, per-domain quality DERIVED FROM A REAL OmnisBench RUN "
            f"(run_date={data.get('provenance', {}).get('run_date')}, "
            f"snapshot={data.get('provenance', {}).get('snapshot_date')}). "
            "quality = mean pass rate; _sample_counts records n per cell."
        ),
        "_source": "omnisbench",
        "_sample_counts": counts,
        "domains": {d: domains[d] for d in sorted(domains)},
    }


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("results")
    ap.add_argument("--out", default=None)
    ap.add_argument("--policy-prefix", default=None, help='e.g. "always_" to count each model once per item')
    args = ap.parse_args()

    out = convert(args.results, args.policy_prefix)
    text = json.dumps(out, indent=2)
    if args.out:
        with open(args.out, "w", encoding="utf-8") as fh:
            fh.write(text + "\n")
        print(f"wrote {args.out}")
    print(text)


if __name__ == "__main__":
    main()
