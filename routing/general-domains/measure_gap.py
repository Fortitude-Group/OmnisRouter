"""Gap-fill: measure the pool on PUBLIC humanities/social questions (MMLU original) to
cover the RouterArena domains MMLU-Pro lacks (Literature, Language, Arts, Social Science).
Held out from RouterArena. Merges into quality_by_domain.json."""
import sys, os, json, random, collections
sys.path.insert(0, os.path.dirname(__file__))
from measure import call, mk_prompt, grade, cost, PRICE
from datasets import load_dataset

GROUPS = {
 "humanities": ["philosophy","world_religions","high_school_european_history","high_school_world_history",
                "high_school_us_history","prehistory","jurisprudence","moral_disputes","logical_fallacies"],
 "social-studies": ["sociology","high_school_geography","high_school_government_and_politics","us_foreign_policy",
                    "marketing","management","public_relations","human_sexuality","high_school_psychology"],
}
PER = 30
ds = load_dataset("cais/mmlu","all",split="test")
bysub = collections.defaultdict(list)
for r in ds: bysub[r["subject"]].append(r)
random.seed(11)
acc = collections.defaultdict(collections.Counter); spend = collections.Counter()
for dom, subs in GROUPS.items():
    pool=[]
    for s in subs: pool += bysub.get(s, [])
    sample = random.sample(pool, min(PER, len(pool)))
    for r in sample:
        correct = chr(65 + int(r["answer"]))
        p = mk_prompt(r["question"], r["choices"])
        for m in PRICE:
            try:
                txt,it,ot = call(m,p)
                acc[(m,dom)]["c"]+=int(grade(txt,correct)); acc[(m,dom)]["n"]+=1; spend[m]+=cost(m,it,ot)
            except Exception: pass
    print(f"{dom}: measured {len(sample)}  spend ${sum(spend.values()):.2f}", flush=True)

path=os.path.join(os.path.dirname(__file__),"quality_by_domain.json")
out=json.load(open(path))
for (m,dom),c in acc.items():
    if c["n"]: out[f"{m}|{dom}"]={"acc":c["c"]/c["n"],"n":c["n"]}
json.dump(out,open(path,"w"),indent=1)
print("\nSPEND",{k:round(v,3) for k,v in spend.items()},"TOTAL",round(sum(spend.values()),3))
for dom in GROUPS:
    print(dom, {m:f"{acc[(m,dom)]['c']/max(1,acc[(m,dom)]['n'])*100:.0f}%" for m in PRICE})
