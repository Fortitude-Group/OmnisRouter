using System.Globalization;
using System.Text.Json;
using OmnisRouter.Core.Abstractions;
using OmnisRouter.Core.Model;
using OmnisRouter.Core.Routing;
using OmnisRouter.Routing;

namespace OmnisRouter.Api.Endpoints;

/// <summary>
/// <c>POST /v1/chat/completions</c> — the OpenAI drop-in routed endpoint (US1). Normalizes the
/// request, routes it to the cheapest capable model among providers the operator can actually reach,
/// dispatches, translates the response back to OpenAI shape, and attaches routing-receipt headers.
/// </summary>
public static class ChatCompletionsEndpoint
{
    private const string DefaultTenant = "default";

    public static IEndpointRouteBuilder MapChatCompletions(this IEndpointRouteBuilder app)
    {
        app.MapPost("/v1/chat/completions", HandleAsync);
        return app;
    }

    private static async Task<IResult> HandleAsync(
        HttpContext http,
        IEnumerable<IFormatAdapter> adapters,
        IEnumerable<IUpstreamClient> upstreams,
        IRoutingPolicy policy,
        RoutingDefaults defaults,
        OmnisRouter.Api.Routing.IProviderCredentialResolver credentials,
        CancellationToken cancellationToken)
    {
        var adapter = adapters.FirstOrDefault(a => a.Format == ClientFormat.OpenAI)
            ?? throw new OmnisException(500, "adapter_unavailable", "OpenAI adapter is not registered.");

        JsonElement body;
        try
        {
            body = await http.Request.ReadFromJsonAsync<JsonElement>(cancellationToken);
        }
        catch (JsonException)
        {
            throw new OmnisException(400, "invalid_request", "Request body is not valid JSON.");
        }

        var request = adapter.ToInternal(body);

        // Only route to providers we can actually reach (an upstream client is registered).
        var upstreamByProvider = upstreams
            .GroupBy(u => u.Provider)
            .ToDictionary(g => g.Key, g => g.First());
        var pool = defaults.CandidatePool.Where(m => upstreamByProvider.ContainsKey(m.Provider)).ToList();
        if (pool.Count == 0)
        {
            throw new OmnisException(503, "no_routable_models", "No candidate models have a reachable upstream client configured.");
        }

        var strongDefault = pool.Contains(defaults.StrongDefault) ? defaults.StrongDefault : pool[^1];
        var routingContext = new RoutingContext { TenantId = DefaultTenant, CandidatePool = pool, StrongDefault = strongDefault };

        var decision = policy.Decide(request, routingContext);
        WriteReceiptHeaders(http.Response, decision);

        var upstream = upstreamByProvider[decision.Chosen.Provider];
        var credential = await credentials.ResolveAsync(DefaultTenant, decision.Chosen.Provider, cancellationToken);

        if (request.Stream)
        {
            var events = upstream.StreamAsync(request, decision.Chosen, credential, cancellationToken);
            return TypedResults.ServerSentEvents(adapter.ToClientStream(events, decision, cancellationToken));
        }

        var response = await upstream.SendAsync(request, decision.Chosen, credential, cancellationToken);
        var json = adapter.ToClientResponse(response, decision);
        return Results.Content(json.GetRawText(), "application/json");
    }

    private static void WriteReceiptHeaders(HttpResponse response, ModelDecision d)
    {
        var h = response.Headers;
        h["X-Omnis-Model"] = d.Chosen.ToString();
        h["X-Omnis-Confidence"] = d.Confidence.ToString("0.####", CultureInfo.InvariantCulture);
        h["X-Omnis-Cluster"] = d.ClusterId.ToString(CultureInfo.InvariantCulture);
        h["X-Omnis-Policy"] = d.PolicyVersion;
        h["X-Omnis-Decision"] = d.Decision.ToString();
        h["X-Omnis-Reason"] = d.Reason.ToString();
        h["X-Omnis-Cost-Usd"] = d.EstCostUsd.ToString("0.######", CultureInfo.InvariantCulture);
        h["X-Omnis-Cost-Delta-Vs-Big"] = d.EstCostDeltaVsBigUsd.ToString("0.######", CultureInfo.InvariantCulture);
        h["X-Omnis-Session-Pin"] = d.SessionPinApplied ? "applied" : "none";
        if (!string.IsNullOrEmpty(d.CapabilityNotice))
        {
            h["X-Omnis-Capability-Notice"] = d.CapabilityNotice;
        }
    }
}
