"""Measure the OmnisRouter pool's per-domain quality on MMLU-Pro (PUBLIC, held out from
RouterArena). Feeds the routing-model rebuild so general queries stop defaulting to Opus.
Cheap models measured broadly; strong models anchored on a smaller sample. Cost-tracked."""
import os, json, re, sys, time, urllib.request, collections, random

PRICE = {  # per Mtok (input, output)
 "claude-opus-5":(5.0,25.0), "gpt-5":(1.25,10.0),
 "claude-haiku-4-5":(1.0,5.0), "gpt-5-nano":(0.05,0.40),
}
ANTH = {"claude-opus-5","claude-haiku-4-5"}
OPENAI = {"gpt-5","gpt-5-nano"}
AK = os.environ.get("ANTHROPIC_API_KEY"); OK = os.environ.get("OPENAI_API_KEY")

def call(model, prompt, max_out=600):
    if model in OPENAI:
        body=json.dumps({"model":model,"messages":[{"role":"user","content":prompt}],
                         "max_completion_tokens":max_out,"reasoning_effort":"minimal"}).encode()
        req=urllib.request.Request("https://api.openai.com/v1/chat/completions",data=body,
            headers={"Authorization":f"Bearer {OK}","Content-Type":"application/json"})
        r=json.loads(urllib.request.urlopen(req,timeout=180).read())
        txt=r["choices"][0]["message"]["content"] or ""
        u=r["usage"]; return txt,u["prompt_tokens"],u["completion_tokens"]
    else:
        body=json.dumps({"model":model,"max_tokens":max_out,
                         "messages":[{"role":"user","content":prompt}]}).encode()
        req=urllib.request.Request("https://api.anthropic.com/v1/messages",data=body,
            headers={"x-api-key":AK,"anthropic-version":"2023-06-01","Content-Type":"application/json"})
        r=json.loads(urllib.request.urlopen(req,timeout=180).read())
        txt="".join(b.get("text","") for b in r.get("content",[]))
        u=r["usage"]; return txt,u["input_tokens"],u["output_tokens"]

def mk_prompt(q,options):
    letters=[chr(65+i) for i in range(len(options))]
    opts="\n".join(f"{L}) {o}" for L,o in zip(letters,options))
    return (f"Answer the following multiple-choice question.\n\nQuestion: {q}\n\nOptions:\n{opts}\n\n"
            f"Reason briefly, then end your reply with a line exactly: Answer: <letter>")

def grade(txt, correct):
    m=re.findall(r"Answer:\s*([A-J])", txt, re.I)
    if m: return m[-1].upper()==correct.upper()
    m=re.findall(r"\b([A-J])\b", txt.strip())
    return bool(m) and m[-1].upper()==correct.upper()

def cost(model,itok,otok):
    pin,pout=PRICE[model]; return (itok*pin+otok*pout)/1e6

def run(rows, models, tag):
    acc=collections.defaultdict(lambda: collections.Counter())  # (model,cat)->[correct,total]
    spend=collections.Counter(); errs=0
    for i,r in enumerate(rows):
        p=mk_prompt(r["question"], r["options"]); cat=r["category"]
        for m in models:
            try:
                txt,it,ot=call(m,p); ok=grade(txt,r["answer"])
                acc[(m,cat)]["c"]+=int(ok); acc[(m,cat)]["n"]+=1
                spend[m]+=cost(m,it,ot)
            except Exception as e:
                errs+=1
        if (i+1)%50==0: print(f"  [{tag}] {i+1}/{len(rows)}  spend=${sum(spend.values()):.2f}",flush=True)
    return acc,spend,errs

if __name__=="__main__":
    mode=sys.argv[1] if len(sys.argv)>1 else "dry"
    from datasets import load_dataset
    ds=load_dataset("TIGER-Lab/MMLU-Pro",split="test")
    bycat=collections.defaultdict(list)
    for r in ds: bycat[r["category"]].append(r)
    random.seed(0)
    cats=sorted(bycat)
    if mode=="dry":
        rows=[]; [rows.extend(random.sample(bycat[c],min(1,len(bycat[c])))) for c in cats[:5]]
        rows=rows[:5]; models=list(PRICE)
    elif mode=="openai":
        per=int(sys.argv[2]) if len(sys.argv)>2 else 25
        rows=[]
        for c in cats: rows.extend(random.sample(bycat[c],min(per,len(bycat[c]))))
        models=["gpt-5","gpt-5-nano"]
    else:
        per=int(sys.argv[2]) if len(sys.argv)>2 else 30
        rows=[];
        for c in cats: rows.extend(random.sample(bycat[c],min(per,len(bycat[c]))))
        models=list(PRICE)
    print(f"mode={mode} rows={len(rows)} models={models} cats={len(cats)}",flush=True)
    acc,spend,errs=run(rows,models,mode)
    print("\n=== SPEND ===");
    for m in PRICE: print(f"  {m:20} ${spend[m]:.3f}")
    print(f"  TOTAL ${sum(spend.values()):.3f}   errors={errs}")
    if mode!="dry":
        path=os.path.join(os.path.dirname(__file__),"quality_by_domain.json")
        out=json.load(open(path)) if os.path.exists(path) else {}
        for m in models:
            for c in cats:
                a=acc[(m,c)]
                if a["n"]: out[f"{m}|{c}"]={"acc":a["c"]/a["n"],"n":a["n"]}
        json.dump(out,open(path,"w"),indent=1)
        print("\n=== PER-DOMAIN ACCURACY (nano / haiku / gpt-5 / opus) ===")
        for c in cats:
            vals=[]
            for m in ["gpt-5-nano","claude-haiku-4-5","gpt-5","claude-opus-5"]:
                e=out.get(f"{m}|{c}"); vals.append(f"{e['acc']*100:3.0f}%" if e else "  - ")
            print(f"  {c:18} " + "  ".join(vals))
        print("\nsaved quality_by_domain.json")
