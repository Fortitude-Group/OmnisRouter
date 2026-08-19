using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OmnisRouter.Api.Routing;
using OmnisRouter.Core.Abstractions;
using OmnisRouter.Core.Model;
using OmnisRouter.Core.Routing;
using OmnisRouter.Routing;

namespace OmnisRouter.Api.Endpoints;

/// <summary>
/// <c>POST /v1/chat/completions</c> — the OpenAI drop-in routed endpoint (US1). Normalizes the
/// request, routes it to the cheapest capable model among reachable providers, dispatches,
/// translates the response back to OpenAI shape, attaches routing-receipt headers, and records a
/// content-free decision-log entry (US2).
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
        IProviderCredentialResolver credentials,
        IDecisionLog decisionLog,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
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
        var requestHash = HashRequest(body);

        var routingContext = RoutingPipeline.BuildContext(upstreams, defaults, DefaultTenant, out var upstreamByProvider);
        var decision = policy.Decide(request, routingContext);
        WriteReceiptHeaders(http.Response, decision);

        var upstream = upstreamByProvider[decision.Chosen.Provider];
        var credential = await credentials.ResolveAsync(DefaultTenant, decision.Chosen.Provider, cancellationToken);

        if (request.Stream)
        {
            var events = upstream.StreamAsync(request, decision.Chosen, credential, cancellationToken);
            await decisionLog.AppendAsync(
                BuildLogEntry(request, decision, requestHash, RequestOutcome.Success, stopwatch.ElapsedMilliseconds),
                cancellationToken);
            return TypedResults.ServerSentEvents(adapter.ToClientStream(events, decision, cancellationToken));
        }

        try
        {
            var response = await upstream.SendAsync(request, decision.Chosen, credential, cancellationToken);
            await decisionLog.AppendAsync(
                BuildLogEntry(request, decision, requestHash, RequestOutcome.Success, stopwatch.ElapsedMilliseconds),
                cancellationToken);
            var json = adapter.ToClientResponse(response, decision);
            return Results.Content(json.GetRawText(), "application/json");
        }
        catch (OperationCanceledException)
        {
            await decisionLog.AppendAsync(
                BuildLogEntry(request, decision, requestHash, RequestOutcome.Cancelled, stopwatch.ElapsedMilliseconds),
                CancellationToken.None);
            throw;
        }
        catch
        {
            await decisionLog.AppendAsync(
                BuildLogEntry(request, decision, requestHash, RequestOutcome.UpstreamError, stopwatch.ElapsedMilliseconds),
                CancellationToken.None);
            throw;
        }
    }

    private static DecisionLogEntry BuildLogEntry(
        ChatRequest request, ModelDecision d, string requestHash, RequestOutcome outcome, long latencyMs) => new()
    {
        TenantId = DefaultTenant,
        Timestamp = DateTimeOffset.UtcNow,
        SessionId = request.SessionId,
        RequestHash = requestHash,
        ClientFormat = request.OriginFormat,
        ClusterId = d.ClusterId,
        ChosenProvider = d.Chosen.Provider,
        ChosenModelId = d.Chosen.ModelId,
        Confidence = d.Confidence,
        Top1Sim = d.Top1CosineSim,
        Top2Sim = d.Top2CosineSim,
        Margin = d.Margin,
        Decision = d.Decision,
        Reason = d.Reason,
        PolicyVersion = d.PolicyVersion,
        EstCostUsd = d.EstCostUsd,
        EstCostDeltaVsBigUsd = d.EstCostDeltaVsBigUsd,
        SessionPinApplied = d.SessionPinApplied,
        Outcome = outcome,
        LatencyMs = (int)Math.Min(latencyMs, int.MaxValue),
    };

    private static string HashRequest(JsonElement body)
    {
        var bytes = Encoding.UTF8.GetBytes(body.GetRawText());
        return Convert.ToHexStringLower(SHA256.HashData(bytes));
    }

    internal static void WriteReceiptHeaders(HttpResponse response, ModelDecision d)
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
