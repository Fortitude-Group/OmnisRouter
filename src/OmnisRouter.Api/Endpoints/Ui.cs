namespace OmnisRouter.Api.Endpoints;

/// <summary>
/// <c>GET /ui</c> — a self-contained, dependency-free self-host dashboard (FR-017). It renders spend,
/// savings, and the recent routing-decision log by calling the router's own authenticated analytics
/// endpoint with an operator-supplied token (the page itself is unauthenticated static HTML).
/// Styled in the Fortitude Omnis web livery (teal/petrol --ds-* tokens, Inter + JetBrains Mono).
/// Fonts are named first with system fallbacks so the page stays offline-safe (no external fetch).
/// </summary>
public static class UiEndpoint
{
    public static IEndpointRouteBuilder MapUi(this IEndpointRouteBuilder app)
    {
        app.MapGet("/ui", () => Results.Content(Page, "text/html; charset=utf-8"));
        return app;
    }

    private const string Page = """
<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
<title>OmnisRouter — Self-Host Dashboard</title>
<style>
  :root {
    color-scheme: dark;
    --ds-bg:8 12 20; --ds-surface:14 20 32; --ds-surface2:20 30 47;
    --ds-border:30 45 69; --ds-border2:37 54 80;
    --ds-accent:45 212 191; --ds-bright:94 234 212;
    --ds-text:241 245 249; --ds-muted:166 180 200; --ds-dim:124 141 166;
    --ds-danger:248 113 113; --ds-warning:251 191 36; --ds-success:52 211 153;
    --sans:'Inter',system-ui,-apple-system,'Segoe UI',Roboto,sans-serif;
    --mono:'JetBrains Mono',ui-monospace,SFMono-Regular,Menlo,Consolas,monospace;
  }
  * { box-sizing:border-box; }
  body {
    margin:0; min-height:100vh; font:15px/1.55 var(--sans); color:rgb(var(--ds-text));
    background:
      radial-gradient(60rem 30rem at 78% -12%, rgb(var(--ds-accent) / .10), transparent 60%),
      radial-gradient(48rem 24rem at 0% -4%, rgb(var(--ds-bright) / .06), transparent 55%),
      rgb(var(--ds-bg));
    background-attachment:fixed;
  }
  body::before {
    content:''; position:fixed; inset:0; z-index:-1; pointer-events:none;
    background-image:radial-gradient(rgb(var(--ds-border2) / .35) 1px, transparent 1px);
    background-size:22px 22px;
    -webkit-mask-image:linear-gradient(to bottom, black, transparent 72%);
    mask-image:linear-gradient(to bottom, black, transparent 72%);
  }
  a { color:rgb(var(--ds-accent)); text-decoration:none; }
  a:hover { color:rgb(var(--ds-bright)); }
  :focus-visible { outline:2px solid rgb(var(--ds-accent)); outline-offset:2px; border-radius:6px; }
  .skip-link { position:absolute; left:-9999px; top:0; z-index:10; padding:8px 14px; border-radius:8px;
    background:rgb(var(--ds-surface)); color:rgb(var(--ds-text)); border:1px solid rgb(var(--ds-border2)); }
  .skip-link:focus { left:12px; top:12px; }

  header {
    position:sticky; top:0; z-index:5; display:flex; gap:18px; align-items:center; flex-wrap:wrap;
    padding:14px 24px; background:rgb(var(--ds-surface) / .72);
    -webkit-backdrop-filter:blur(12px) saturate(1.2); backdrop-filter:blur(12px) saturate(1.2);
    border-bottom:1px solid rgb(var(--ds-border)); box-shadow:0 8px 30px rgb(0 0 0 / .25);
  }
  .brand { display:flex; align-items:baseline; gap:12px; margin-right:auto; flex-wrap:wrap; }
  .wordmark { font-size:18px; font-weight:800; letter-spacing:-.02em; }
  .wordmark .g { background:linear-gradient(90deg, rgb(var(--ds-bright)), rgb(var(--ds-accent)));
    -webkit-background-clip:text; background-clip:text; color:transparent; }
  .eyebrow { font-family:var(--mono); font-size:10.5px; font-weight:600; text-transform:uppercase;
    letter-spacing:.16em; color:rgb(var(--ds-accent)); background:rgb(var(--ds-accent) / .10);
    border:1px solid rgb(var(--ds-accent) / .28); padding:3px 9px; border-radius:999px; }
  .byline { font-size:12.5px; color:rgb(var(--ds-dim)); }
  .byline a { font-weight:600; }
  .controls { display:flex; gap:10px; align-items:center; flex-wrap:wrap; }
  input, button { font:inherit; padding:8px 12px; border-radius:9px;
    border:1px solid rgb(var(--ds-border2)); background:rgb(var(--ds-surface2)); color:rgb(var(--ds-text)); }
  input::placeholder { color:rgb(var(--ds-dim)); }
  button { cursor:pointer; font-weight:600; color:rgb(var(--ds-bg)); background:rgb(var(--ds-accent));
    border-color:transparent; transition:transform .12s ease, background .12s ease; }
  button:hover { background:rgb(var(--ds-bright)); transform:translateY(-1px); }
  #status { font-size:12.5px; color:rgb(var(--ds-dim)); }

  main { padding:28px 24px 8px; max-width:1120px; margin:0 auto; }
  .cards { display:grid; grid-template-columns:repeat(auto-fit,minmax(200px,1fr)); gap:16px; margin-bottom:24px; }
  .card { background:rgb(var(--ds-surface)); border:1px solid rgb(var(--ds-border)); border-radius:16px;
    padding:18px 18px 16px; transition:border-color .15s ease, transform .15s ease; }
  .card:hover { border-color:rgb(var(--ds-accent) / .5); transform:translateY(-2px); }
  .card .n { font-size:28px; font-weight:800; letter-spacing:-.02em; }
  .card .l { margin-top:6px; font-family:var(--mono); color:rgb(var(--ds-dim)); font-size:10.5px;
    text-transform:uppercase; letter-spacing:.12em; }
  .card.save .n { color:rgb(var(--ds-success)); }

  .panel { background:rgb(var(--ds-surface)); border:1px solid rgb(var(--ds-border)); border-radius:16px; overflow:hidden; }
  .tablewrap { overflow-x:auto; }
  table { width:100%; border-collapse:collapse; }
  th, td { text-align:left; padding:11px 14px; border-bottom:1px solid rgb(var(--ds-border));
    font-variant-numeric:tabular-nums; font-size:13.5px; white-space:nowrap; }
  th { font-family:var(--mono); color:rgb(var(--ds-dim)); font-weight:600; font-size:10.5px;
    text-transform:uppercase; letter-spacing:.1em; background:rgb(var(--ds-surface2) / .5); }
  tbody tr:last-child td { border-bottom:none; }
  tbody tr:hover td { background:rgb(var(--ds-surface2) / .4); }
  .muted { color:rgb(var(--ds-dim)); }
  .save { color:rgb(var(--ds-success)); }
  .mono { font-family:var(--mono); }
  .badge { padding:2px 9px; border-radius:999px; font-size:11px; font-weight:600; font-family:var(--mono); letter-spacing:.03em; }
  .routed { background:rgb(var(--ds-accent) / .14); color:rgb(var(--ds-accent)); border:1px solid rgb(var(--ds-accent) / .3); }
  .escalated { background:rgb(var(--ds-warning) / .14); color:rgb(var(--ds-warning)); border:1px solid rgb(var(--ds-warning) / .3); }
  .err { color:rgb(var(--ds-danger)); }

  footer { max-width:1120px; margin:28px auto 0; padding:20px 24px 30px; border-top:1px solid rgb(var(--ds-border));
    color:rgb(var(--ds-dim)); font-size:12.5px; display:flex; justify-content:space-between; gap:12px; flex-wrap:wrap; }
  footer a { font-weight:600; }
  @media (max-width:640px) { .brand { margin-right:0; width:100%; } }
</style>
</head>
<body>
<a class="skip-link" href="#main">Skip to dashboard</a>
<header>
  <div class="brand">
    <span class="wordmark">Omnis<span class="g">Router</span></span>
    <span class="eyebrow">Self-Host Dashboard</span>
    <span class="byline">by <a href="https://fortitude-omnis.group" target="_blank" rel="noopener">Fortitude Omnis</a></span>
  </div>
  <div class="controls">
    <input id="tok" type="password" placeholder="router token" size="26" autocomplete="off" aria-label="Router token">
    <button onclick="load()">Load</button>
    <span id="status" role="status" aria-live="polite"></span>
  </div>
</header>
<main id="main">
  <div class="cards">
    <div class="card"><div class="n" id="c-req">—</div><div class="l">Requests</div></div>
    <div class="card"><div class="n" id="c-spend">—</div><div class="l">Est. spend (USD)</div></div>
    <div class="card save"><div class="n" id="c-save">—</div><div class="l">Est. savings vs strongest</div></div>
    <div class="card"><div class="n" id="c-esc">—</div><div class="l">Escalations</div></div>
  </div>
  <div class="panel"><div class="tablewrap">
  <table>
    <thead><tr><th>Time</th><th>Model</th><th>Cluster</th><th>Decision</th><th>Reason</th><th>Cost</th><th>Saved</th><th>ms</th></tr></thead>
    <tbody id="rows"><tr><td colspan="8" class="muted">Enter a router token and click Load.</td></tr></tbody>
  </table>
  </div></div>
</main>
<footer>
  <span>A <a href="https://fortitude-omnis.group" target="_blank" rel="noopener">Fortitude Omnis</a> product.</span>
  <span class="mono">OmnisRouter · self-host</span>
</footer>
<script>
async function load(){
  const tok = document.getElementById('tok').value.trim();
  const status = document.getElementById('status');
  if(!tok){ status.textContent='token required'; return; }
  status.textContent='loading…';
  try {
    const r = await fetch('/v1/analytics/routing-decisions?limit=500',{headers:{Authorization:'Bearer '+tok}});
    if(!r.ok){ status.innerHTML='<span class="err">HTTP '+r.status+'</span>'; return; }
    const text = await r.text();
    const rows = text.split('\n').filter(Boolean).map(JSON.parse);
    render(rows); status.textContent = rows.length+' decisions';
  } catch(e){ status.innerHTML='<span class="err">'+e+'</span>'; }
}
function usd(n){ return '$'+Number(n).toFixed(6); }
function render(rows){
  let spend=0, save=0, esc=0;
  const body = document.getElementById('rows'); body.innerHTML='';
  rows.slice().reverse().forEach(d=>{
    spend += d.est_cost_usd||0;
    save += Math.max(0, -(d.est_cost_delta_vs_big_usd||0));
    if(d.decision==='ESCALATED') esc++;
  });
  document.getElementById('c-req').textContent = rows.length;
  document.getElementById('c-spend').textContent = usd(spend);
  document.getElementById('c-save').textContent = usd(save);
  document.getElementById('c-esc').textContent = esc;
  rows.slice().reverse().slice(0,200).forEach(d=>{
    const tr=document.createElement('tr');
    const cls = d.decision==='ESCALATED'?'escalated':'routed';
    tr.innerHTML = `<td class="muted mono">${(d.timestamp||'').replace('T',' ').slice(0,19)}</td>`+
      `<td class="mono">${d.chosen_provider}/${d.chosen_model_id}</td>`+
      `<td class="mono">${d.cluster_id}</td>`+
      `<td><span class="badge ${cls}">${d.decision}</span></td>`+
      `<td class="muted">${d.reason}</td>`+
      `<td class="mono">${usd(d.est_cost_usd)}</td>`+
      `<td class="save mono">${usd(Math.max(0,-(d.est_cost_delta_vs_big_usd||0)))}</td>`+
      `<td class="muted mono">${d.latency_ms}</td>`;
    body.appendChild(tr);
  });
}
</script>
</body>
</html>
""";
}
