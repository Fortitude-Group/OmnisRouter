#!/usr/bin/env python3
"""Merge real OmnisBench per-model quality into the routing bench-results, over the aligned pool.

Real coding+math quality (from an OmnisBench run, via omnisbench_to_benchresults.py) replaces the
estimates for the benchmarked models; estimates are kept (remapped to the aligned pool ids) for the
domains/models OmnisBench doesn't cover yet. A top-level `_source` map records real-vs-estimate per cell.
"""
import argparse
import json
from omnisbench_to_benchresults import convert

POOL = [
    "openai/gpt-5",
    "openai/gpt-5-nano",
    "anthropic/claude-opus-5",
    "anthropic/claude-haiku-4-5",
    "gemini/gemini-2.5-flash",
    "openrouter/meta-llama/llama-3.3-70b-instruct",
]
# v2 estimate ids -> aligned pool ids (placeholders replaced by the real benchmarked models).
REMAP = {
    "anthropic/claude-opus-4-8": "anthropic/claude-opus-5",
    "openai/gpt-5-mini": "openai/gpt-5-nano",
}


def main() -> None:
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("omnisbench_results")
    ap.add_argument("estimates")
    ap.add_argument("out")
    ap.add_argument("--pool", nargs="+", default=POOL, help="provider/model ids in the output (default: the v3 pool)")
    ap.add_argument("--inherit", nargs="*", default=[], metavar="NEW=OLD",
                    help="where a pool model has no real or estimated cell, use OLD's estimate, labelled as inherited")
    ap.add_argument("--policy-prefix", default=None, help='passed to convert(), e.g. "always_"')
    ap.add_argument("--run-label", default="2026-08-19", help="run date quoted in the _comment")
    args = ap.parse_args()

    inherit = dict(pair.split("=", 1) for pair in args.inherit)
    real = convert(args.omnisbench_results, args.policy_prefix)["domains"]
    with open(args.estimates, encoding="utf-8") as fh:
        est_file = json.load(fh)

    domains: dict[str, dict[str, float]] = {}
    source: dict[str, dict[str, str]] = {}
    # Estimate-file order first, then any domain only the run covers, so default flags reproduce
    # bench-results-v3.json byte for byte.
    order = list(est_file["domains"]) + sorted(set(real) - set(est_file["domains"]))
    for domain in order:
        est = {REMAP.get(k, k): v for k, v in est_file["domains"].get(domain, {}).items()}
        row: dict[str, float] = {}
        src: dict[str, str] = {}
        for model in args.pool:
            if domain in real and model in real[domain]:
                row[model], src[model] = real[domain][model], "omnisbench"
            elif model in est:
                row[model], src[model] = est[model], "estimate"
            elif model in inherit and inherit[model] in est:
                row[model], src[model] = est[inherit[model]], f"estimate, inherited from {inherit[model]}"
            # else: no number we can stand behind, so the model isn't a candidate for this domain.
        domains[domain] = row
        source[domain] = src

    out = {
        "_comment": (
            "Per-model, per-domain quality for OmnisRouter routing. coding+math for the benchmarked "
            f"models are REAL (OmnisBench run {args.run_label}); the rest are estimates pending broader "
            "OmnisBench coverage. See _source for real-vs-estimate per cell."
        ),
        "_source": source,
        "domains": domains,
    }
    with open(args.out, "w", encoding="utf-8") as fh:
        fh.write(json.dumps(out, indent=2) + "\n")
    n_real = sum(1 for d in source.values() for s in d.values() if s == "omnisbench")
    print(f"wrote {args.out}: {n_real} real cells (OmnisBench), rest estimated")


if __name__ == "__main__":
    main()
