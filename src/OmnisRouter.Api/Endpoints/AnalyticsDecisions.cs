using System.Globalization;
using System.Text.Json.Nodes;
using OmnisRouter.Core.Abstractions;
using OmnisRouter.Core.Model;
using OmnisRouter.Core.Routing;

namespace OmnisRouter.Api.Endpoints;

/// <summary>
/// <c>GET /v1/analytics/routing-decisions</c> — streams the decision log as NDJSON (FR-009).
/// Content-free: rows carry a non-reversible request hash, never prompt/response text or keys.
/// </summary>
public static class AnalyticsDecisionsEndpoint
{
    private const string DefaultTenant = "default";

    public static IEndpointRouteBuilder MapAnalyticsDecisions(this IEndpointRouteBuilder app)
    {
        app.MapGet("/v1/analytics/routing-decisions", async (HttpContext http, IDecisionLog log, CancellationToken cancellationToken) =>
        {
            var q = http.Request.Query;
            var query = new DecisionQuery
            {
                TenantId = DefaultTenant,
                From = ParseDate(q["from"]),
                To = ParseDate(q["to"]),
                ClusterId = ParseInt(q["cluster_id"]),
                Decision = ParseDecision(q["decision"]),
                Provider = ParseProvider(q["provider"]),
                Limit = ParseInt(q["limit"]) ?? 10_000,
                Cursor = q["cursor"].FirstOrDefault(),
            };

            http.Response.ContentType = "application/x-ndjson";
            await using var writer = new StreamWriter(http.Response.Body, leaveOpen: true);
            await foreach (var entry in log.ExportAsync(query, cancellationToken))
            {
                await writer.WriteLineAsync(ToJson(entry).ToJsonString());
            }

            await writer.FlushAsync(cancellationToken);
        });

        return app;
    }

    private static JsonObject ToJson(DecisionLogEntry e) => new()
    {
        ["id"] = e.Id,
        ["timestamp"] = e.Timestamp.ToString("O", CultureInfo.InvariantCulture),
        ["session_id"] = e.SessionId,
        ["request_hash"] = e.RequestHash,
        ["client_format"] = e.ClientFormat.ToString().ToLowerInvariant(),
        ["cluster_id"] = e.ClusterId,
        ["chosen_provider"] = e.ChosenProvider.ToString().ToLowerInvariant(),
        ["chosen_model_id"] = e.ChosenModelId,
        ["confidence"] = e.Confidence,
        ["top1_sim"] = e.Top1Sim,
        ["top2_sim"] = e.Top2Sim,
        ["margin"] = e.Margin,
        ["decision"] = e.Decision == RoutingDecisionKind.Escalated ? "ESCALATED" : "ROUTED",
        ["reason"] = e.Reason.ToString(),
        ["policy_version"] = e.PolicyVersion,
        ["est_cost_usd"] = decimal.ToDouble(e.EstCostUsd),
        ["est_cost_delta_vs_big_usd"] = decimal.ToDouble(e.EstCostDeltaVsBigUsd),
        ["session_pin_applied"] = e.SessionPinApplied,
        ["outcome"] = e.Outcome.ToString().ToLowerInvariant(),
        ["latency_ms"] = e.LatencyMs,
    };

    private static DateTimeOffset? ParseDate(string? v) =>
        DateTimeOffset.TryParse(v, CultureInfo.InvariantCulture, out var d) ? d : null;

    private static int? ParseInt(string? v) =>
        int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : null;

    private static RoutingDecisionKind? ParseDecision(string? v) => v?.ToUpperInvariant() switch
    {
        "ROUTED" => RoutingDecisionKind.Routed,
        "ESCALATED" => RoutingDecisionKind.Escalated,
        _ => null,
    };

    private static Provider? ParseProvider(string? v) =>
        Enum.TryParse<Provider>(v, ignoreCase: true, out var p) ? p : null;
}
