using System.Globalization;
using System.Text.Json.Nodes;
using OmnisRouter.Core.Routing;

namespace OmnisRouter.Vigil;

/// <summary>
/// Maps a content-free <see cref="DecisionLogEntry"/> to the OmnisVigil ingest-record JSON
/// (docs/contracts/omnisvigil-ingest-record.schema.json). Emits exactly the agreed schema fields
/// and nothing else: Vigil deserialises with unknown members disallowed and rejects (and flags) any
/// record carrying a stray field, so this mapper must never leak an internal or content-bearing key.
/// </summary>
public static class IngestRecordMapper
{
    public static JsonObject ToRecord(DecisionLogEntry e, string routerId) => new()
    {
        ["id"] = e.Id,
        ["timestamp"] = e.Timestamp.ToString("O", CultureInfo.InvariantCulture),
        ["tenant_id"] = e.TenantId,
        ["router_id"] = routerId,
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
        ["reason"] = ToReasonCode(e.Reason),
        ["policy_version"] = e.PolicyVersion,
        ["est_cost_usd"] = decimal.ToDouble(e.EstCostUsd),
        ["est_cost_delta_vs_big_usd"] = decimal.ToDouble(e.EstCostDeltaVsBigUsd),
        ["actual_cost_usd"] = e.ActualCostUsd.HasValue ? (double?)decimal.ToDouble(e.ActualCostUsd.Value) : null,
        ["actual_cost_delta_vs_big_usd"] = e.ActualCostDeltaVsBigUsd.HasValue ? (double?)decimal.ToDouble(e.ActualCostDeltaVsBigUsd.Value) : null,
        ["usage"] = BuildUsage(e),
        ["session_pin_applied"] = e.SessionPinApplied,
        ["outcome"] = e.Outcome.ToString().ToLowerInvariant(),
        ["latency_ms"] = e.LatencyMs,
        ["tags"] = BuildTags(e),
    };

    private static JsonObject? BuildUsage(DecisionLogEntry e)
    {
        if (e.ActualInputTokens is null && e.ActualOutputTokens is null
            && e.ActualCacheCreationTokens is null && e.ActualCacheReadTokens is null)
        {
            return null;
        }

        return new JsonObject
        {
            ["input_tokens"] = e.ActualInputTokens ?? 0,
            ["output_tokens"] = e.ActualOutputTokens ?? 0,
            ["cache_creation_tokens"] = e.ActualCacheCreationTokens ?? 0,
            ["cache_read_tokens"] = e.ActualCacheReadTokens ?? 0,
        };
    }

    private static JsonObject? BuildTags(DecisionLogEntry e)
    {
        if (e.TagProject is null && e.TagTeam is null && e.TagClientName is null
            && e.TagCommit is null && e.TagBranch is null)
        {
            return null;
        }

        return new JsonObject
        {
            ["project"] = e.TagProject,
            ["team"] = e.TagTeam,
            ["client_name"] = e.TagClientName,
            ["commit"] = e.TagCommit,
            ["branch"] = e.TagBranch,
        };
    }

    private static string ToReasonCode(RoutingReason reason) => reason switch
    {
        RoutingReason.CheapestCapable => "cheapest_capable",
        RoutingReason.ConfidenceBelowFloor => "confidence_below_floor",
        RoutingReason.LowConfidenceCluster => "low_confidence_cluster",
        RoutingReason.CapabilityGuardrail => "capability_guardrail",
        RoutingReason.SessionPinned => "session_pinned",
        _ => "cheapest_capable",
    };
}
