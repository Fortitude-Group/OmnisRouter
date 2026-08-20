#!/usr/bin/env python3
"""Merge real OmnisBench per-model quality into the routing bench-results, over the aligned pool.

Real coding+math quality (from an OmnisBench run, via omnisbench_to_benchresults.py) replaces the
estimates for the benchmarked models; estimates are kept (remapped to the aligned pool ids) for the
domains/models OmnisBench doesn't cover yet. A top-level `_source` map records real-vs-estimate per cell.
"""
import json
import sys
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
    omnisbench_results, estimates_path, out_path = sys.argv[1], sys.argv[2], sys.argv[3]
    real = convert(omnisbench_results)["domains"]
    with open(estimates_path, encoding="utf-8") as fh:
        est_file = json.load(fh)

    domains: dict[str, dict[str, float]] = {}
    source: dict[str, dict[str, str]] = {}
    for domain, est_models in est_file["domains"].items():
        est = {REMAP.get(k, k): v for k, v in est_models.items()}
        row: dict[str, float] = {}
        src: dict[str, str] = {}
        for model in POOL:
            if domain in real and model in real[domain]:
                row[model] = real[domain][model]
                src[model] = "omnisbench"
            else:
                row[model] = est[model]
                src[model] = "estimate"
        domains[domain] = row
        source[domain] = src

    out = {
        "_comment": (
            "Per-model, per-domain quality for OmnisRouter routing. coding+math for the benchmarked "
            "models are REAL (OmnisBench run 2026-08-19); the rest are estimates pending broader "
            "OmnisBench coverage. See _source for real-vs-estimate per cell."
        ),
        "_source": source,
        "domains": domains,
    }
    with open(out_path, "w", encoding="utf-8") as fh:
        fh.write(json.dumps(out, indent=2) + "\n")
    n_real = sum(1 for d in source.values() for s in d.values() if s == "omnisbench")
    print(f"wrote {out_path}: {n_real} real cells (OmnisBench), rest estimated")


if __name__ == "__main__":
    main()
