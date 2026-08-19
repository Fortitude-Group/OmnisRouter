using System.Text.Json.Nodes;
using OmnisRouter.Core.Routing;

namespace OmnisRouter.Api.Routing;

/// <summary>
/// Serializes a <see cref="ModelDecision"/> to the public routing-receipt JSON shape
/// (contracts/routing-receipt.schema.json). Used by <c>POST /v1/route</c>.
/// </summary>
public static class ReceiptJson
{
    public static JsonObject ToJson(ModelDecision d)
    {
        var alternatives = new JsonArray();
        foreach (var alt in d.Alternatives)
        {
            alternatives.Add(new JsonObject
            {
                ["provider"] = alt.Model.Provider.ToString().ToLowerInvariant(),
                ["model_id"] = alt.Model.ModelId,
                ["predicted_quality"] = alt.PredictedQuality,
                ["est_cost_usd"] = decimal.ToDouble(alt.EstCostUsd),
                ["est_cost_delta_usd"] = decimal.ToDouble(alt.EstCostDeltaUsd),
            });
        }

        var obj = new JsonObject
        {
            ["policy_version"] = d.PolicyVersion,
            ["cluster_id"] = d.ClusterId,
            ["confidence"] = d.Confidence,
            ["confidence_floor"] = d.ConfidenceFloor,
            ["top1_cosine_sim"] = d.Top1CosineSim,
            ["top2_cosine_sim"] = d.Top2CosineSim,
            ["margin"] = d.Margin,
            ["decision"] = d.Decision == RoutingDecisionKind.Escalated ? "ESCALATED" : "ROUTED",
            ["reason"] = ToReasonCode(d.Reason),
            ["chosen"] = new JsonObject
            {
                ["provider"] = d.Chosen.Provider.ToString().ToLowerInvariant(),
                ["model_id"] = d.Chosen.ModelId,
            },
            ["alternatives"] = alternatives,
            ["est_cost_usd"] = decimal.ToDouble(d.EstCostUsd),
            ["est_cost_delta_vs_big_usd"] = decimal.ToDouble(d.EstCostDeltaVsBigUsd),
            ["session_pin_applied"] = d.SessionPinApplied,
            ["session_pin_reason"] = d.SessionPinReason switch
            {
                OmnisRouter.Core.Routing.SessionPinReason.WarmCache => "warm_cache",
                OmnisRouter.Core.Routing.SessionPinReason.ClusterChangedUnpinned => "cluster_changed_unpinned",
                _ => null,
            },
        };

        if (d.PricingSnapshotDate is not null)
        {
            obj["pricing_snapshot_date"] = d.PricingSnapshotDate;
        }

        return obj;
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
