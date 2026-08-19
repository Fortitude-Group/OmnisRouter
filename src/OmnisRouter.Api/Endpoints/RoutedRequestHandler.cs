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
/// The shared routed-request pipeline behind all three ingress formats: normalize → route →
/// capability-guard → BYOK → (materialize remote images for providers that can't deref) → dispatch
/// → translate back to the client's format → receipt headers → content-free decision-log entry.
/// </summary>
internal static class RoutedRequestHandler
{
    internal const string DefaultTenant = "default";

    public static async Task<IResult> ExecuteAsync(
        HttpContext http,
        ClientFormat format,
        string? pathModel,
        bool? forceStream,
        IEnumerable<IFormatAdapter> adapters,
        IEnumerable<IUpstreamClient> upstreams,
        IRoutingPolicy policy,
        RoutingDefaults defaults,
        IProviderCredentialResolver credentials,
        IDecisionLog decisionLog,
        ICapabilityGuard guard,
        IImageMaterializer materializer,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var adapter = adapters.FirstOrDefault(a => a.Format == format)
            ?? throw new OmnisException(500, "adapter_unavailable", $"{format} adapter is not registered.");

        JsonElement body;
        try
        {
            body = await http.Request.ReadFromJsonAsync<JsonElement>(cancellationToken);
        }
        catch (JsonException)
        {
            throw new OmnisException(400, "invalid_request", "Request body is not valid JSON.");
        }

        var request = adapter.ToInternal(body, pathModel);
        if (forceStream is { } stream)
        {
            request = request with { Stream = stream };
        }

        var requestHash = HashRequest(body);
        var keyedProviders = await credentials.ConfiguredProvidersAsync(DefaultTenant, cancellationToken);
        var routingContext = RoutingPipeline.BuildContext(upstreams, defaults, DefaultTenant, keyedProviders, out var upstreamByProvider);
        var decision = policy.Decide(request, routingContext);

        // Final guardrail: refuse (don't silently degrade) if even the chosen model can't serve the
        // request — e.g. a vision request when no reachable model has vision (research.md R2).
        if (guard.Check(request, decision.Chosen) is { Allowed: false, Code: { } code, Message: { } message })
        {
            throw new OmnisException(400, code, message);
        }

        WriteReceiptHeaders(http.Response, decision);

        var upstream = upstreamByProvider[decision.Chosen.Provider];
        var credential = await credentials.ResolveAsync(DefaultTenant, decision.Chosen.Provider, cancellationToken);

        // Providers other than OpenAI can't dereference a remote image URL — fetch+inline first.
        var dispatchRequest = decision.Chosen.Provider == Provider.OpenAI
            ? request
            : await materializer.MaterializeAsync(request, cancellationToken);

        if (request.Stream)
        {
            var events = upstream.StreamAsync(dispatchRequest, decision.Chosen, credential, cancellationToken);
            await decisionLog.AppendAsync(
                BuildLogEntry(request, decision, requestHash, RequestOutcome.Success, stopwatch.ElapsedMilliseconds),
                cancellationToken);
            return TypedResults.ServerSentEvents(adapter.ToClientStream(events, decision, cancellationToken));
        }

        try
        {
            var response = await upstream.SendAsync(dispatchRequest, decision.Chosen, credential, cancellationToken);
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

    private static string HashRequest(JsonElement body) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(body.GetRawText())));

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
