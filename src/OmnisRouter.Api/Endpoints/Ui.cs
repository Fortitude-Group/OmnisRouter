namespace OmnisRouter.Api.Endpoints;

/// <summary>
/// <c>GET /ui</c> — a self-contained, dependency-free self-host dashboard (FR-017). It renders spend,
/// savings, and the recent routing-decision log by calling the router's own authenticated analytics
/// endpoint with an operator-supplied token (the page itself is unauthenticated static HTML).
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
  :root { color-scheme: dark; }
  body { margin:0; font:14px/1.5 system-ui,-apple-system,Segoe UI,Roboto,sans-serif; background:#0d1117; color:#e6edf3; }
  header { padding:16px 24px; border-bottom:1px solid #21262d; display:flex; gap:16px; align-items:center; flex-wrap:wrap; }
  h1 { font-size:16px; margin:0; letter-spacing:.02em; }
  .muted { color:#8b949e; }
  input,button { font:inherit; padding:6px 10px; border-radius:6px; border:1px solid #30363d; background:#161b22; color:#e6edf3; }
  button { cursor:pointer; background:#238636; border-color:#238636; }
  button:hover { background:#2ea043; }
  main { padding:24px; max-width:1100px; margin:0 auto; }
  .cards { display:grid; grid-template-columns:repeat(auto-fit,minmax(180px,1fr)); gap:16px; margin-bottom:24px; }
  .card { background:#161b22; border:1px solid #21262d; border-radius:10px; padding:16px; }
  .card .n { font-size:26px; font-weight:600; }
  .card .l { color:#8b949e; font-size:12px; text-transform:uppercase; letter-spacing:.04em; }
  .save { color:#3fb950; }
  table { width:100%; border-collapse:collapse; margin-top:8px; }
  th,td { text-align:left; padding:8px 10px; border-bottom:1px solid #21262d; font-variant-numeric:tabular-nums; }
  th { color:#8b949e; font-weight:500; font-size:12px; text-transform:uppercase; }
  .badge { padding:1px 7px; border-radius:999px; font-size:12px; }
  .routed { background:#1f6feb33; color:#79c0ff; }
  .escalated { background:#9e6a0333; color:#e3b341; }
  .err { color:#f85149; }
</style>
</head>
<body>
<header>
  <h1>OmnisRouter <span class="muted">· self-host dashboard</span></h1>
  <input id="tok" type="password" placeholder="router token" size="28" autocomplete="off">
  <button onclick="load()">Load</button>
  <span id="status" class="muted"></span>
</header>
<main>
  <div class="cards">
    <div class="card"><div class="n" id="c-req">—</div><div class="l">Requests</div></div>
    <div class="card"><div class="n" id="c-spend">—</div><div class="l">Est. spend (USD)</div></div>
    <div class="card"><div class="n save" id="c-save">—</div><div class="l">Est. savings vs strongest</div></div>
    <div class="card"><div class="n" id="c-esc">—</div><div class="l">Escalations</div></div>
  </div>
  <table>
    <thead><tr><th>Time</th><th>Model</th><th>Cluster</th><th>Decision</th><th>Reason</th><th>Cost</th><th>Saved</th><th>ms</th></tr></thead>
    <tbody id="rows"><tr><td colspan="8" class="muted">Enter a router token and click Load.</td></tr></tbody>
  </table>
</main>
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
    tr.innerHTML = `<td class="muted">${(d.timestamp||'').replace('T',' ').slice(0,19)}</td>`+
      `<td>${d.chosen_provider}/${d.chosen_model_id}</td>`+
      `<td>${d.cluster_id}</td>`+
      `<td><span class="badge ${cls}">${d.decision}</span></td>`+
      `<td class="muted">${d.reason}</td>`+
      `<td>${usd(d.est_cost_usd)}</td>`+
      `<td class="save">${usd(Math.max(0,-(d.est_cost_delta_vs_big_usd||0)))}</td>`+
      `<td class="muted">${d.latency_ms}</td>`;
    body.appendChild(tr);
  });
}
</script>
</body>
</html>
""";
}
