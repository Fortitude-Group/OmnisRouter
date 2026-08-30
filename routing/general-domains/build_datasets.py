"""Build v5 routing datasets: coding/math (real OmnisBench) + STEM/social (MMLU-Pro) +
humanities/social-studies (MMLU original), all measured on PUBLIC data held out from
RouterArena. Broad prompt coverage so any general query clusters confidently."""
import json, os, random, collections

HERE = os.path.dirname(__file__)
ROOT = os.path.dirname(os.path.dirname(HERE))
DS = os.path.join(ROOT, "routing", "datasets")
CAND = {"gpt-5":"openai/gpt-5","gpt-5-nano":"openai/gpt-5-nano",
        "claude-haiku-4-5":"anthropic/claude-haiku-4-5","claude-opus-5":"anthropic/claude-opus-5"}
MMLU_PRO_DOMS = ["biology","chemistry","physics","history","law","philosophy",
                 "psychology","economics","business","health","engineering"]
MMLU_ORIG_GROUPS = {
 "humanities": ["philosophy","world_religions","high_school_european_history","high_school_world_history",
                "high_school_us_history","prehistory","jurisprudence","moral_disputes","logical_fallacies"],
 "social-studies": ["sociology","high_school_geography","high_school_government_and_politics","us_foreign_policy",
                    "marketing","management","public_relations","human_sexuality","high_school_psychology"],
}

# --- bench-results v5 (coding/math REAL + every measured general domain) ---
q = json.load(open(os.path.join(HERE, "quality_by_domain.json")))
v3 = json.load(open(os.path.join(DS, "bench-results-v3.json")))
gen_doms = [d for d in MMLU_PRO_DOMS + list(MMLU_ORIG_GROUPS)
            if any(f"{m}|{d}" in q for m in CAND)]
domains = {"coding": v3["domains"]["coding"], "math": v3["domains"]["math"]}
source  = {"coding": v3["_source"]["coding"], "math": v3["_source"]["math"]}
for d in gen_doms:
    cell, src = {}, {}
    for m, ck in CAND.items():
        e = q.get(f"{m}|{d}")
        if e: cell[ck] = round(e["acc"], 4); src[ck] = "mmlu"
    if cell: domains[d] = cell; source[d] = src
json.dump({"_comment": "v5: coding/math REAL (OmnisBench); general domains REAL from MMLU-Pro/MMLU "
                       "(public, held out from RouterArena). 2026-08-26.",
           "_source": source, "domains": domains},
          open(os.path.join(DS, "bench-results-v5.json"), "w"), indent=1)
print("bench-results-v5 domains:", list(domains))

# --- prompts v5 ---
from datasets import load_dataset
random.seed(7)
prompts = []
for line in open(os.path.join(DS, "prompts-v2.jsonl"), encoding="utf-8"):
    line = line.strip()
    if line and json.loads(line).get("domain") in ("coding", "math"):
        prompts.append(json.loads(line))
print("coding/math seeds:", len(prompts))

mp = load_dataset("TIGER-Lab/MMLU-Pro", split="test")
bycat = collections.defaultdict(list)
for r in mp: bycat[r["category"]].append(r["question"])
for d in MMLU_PRO_DOMS:
    for qt in random.sample(bycat.get(d, []), min(18, len(bycat.get(d, [])))):
        prompts.append({"text": qt, "domain": d})

mm = load_dataset("cais/mmlu", "all", split="test")
bysub = collections.defaultdict(list)
for r in mm: bysub[r["subject"]].append(r["question"])
for d, subs in MMLU_ORIG_GROUPS.items():
    pool = [x for s in subs for x in bysub.get(s, [])]
    for qt in random.sample(pool, min(24, len(pool))):
        prompts.append({"text": qt, "domain": d})

random.shuffle(prompts)
with open(os.path.join(DS, "prompts-v5.jsonl"), "w", encoding="utf-8") as f:
    for r in prompts: f.write(json.dumps(r) + "\n")
print(f"prompts-v5.jsonl: {len(prompts)} prompts, {len(set(r['domain'] for r in prompts))} domains:",
      dict(collections.Counter(r['domain'] for r in prompts)))
