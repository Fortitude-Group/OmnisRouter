using System.Globalization;
using System.Text.Json.Nodes;

namespace OmnisRouter.Collect;

/// <summary>
/// Builds the content-free ingest record for a transcript usage entry, matching the routed path's
/// wire shape. No routing happened, so there is no realised saving; the model-mix saving is derived
/// by OmnisVigil. The shape here is pinned by a test (FR-022) — do not change it without changing
/// the contract.
/// </summary>
public static class ReceiptRecord
{
    public static JsonObject From(UsageEntry e, double cost, string? commit = null) => new()
    {
        ["id"] = e.Id,
        ["timestamp"] = e.Timestamp.ToString("O", CultureInfo.InvariantCulture),
        ["tenant_id"] = "local",
        ["router_id"] = "claude-code-local",
        ["session_id"] = e.SessionId,
        ["request_hash"] = e.RequestId ?? e.Id,
        ["client_format"] = "anthropic",
        ["cluster_id"] = 0,
        ["chosen_provider"] = "anthropic",
        ["chosen_model_id"] = e.Model,
        ["confidence"] = 1.0,
        ["top1_sim"] = null,
        ["top2_sim"] = null,
        ["margin"] = null,
        ["decision"] = "DIRECT",
        ["reason"] = "claude-code-transcript",
        ["policy_version"] = "collector",
        ["est_cost_usd"] = cost,
        ["est_cost_delta_vs_big_usd"] = 0.0,
        ["actual_cost_usd"] = cost,
        ["actual_cost_delta_vs_big_usd"] = 0.0,
        ["usage"] = new JsonObject
        {
            ["input_tokens"] = ToInt(e.InputTokens),
            ["output_tokens"] = ToInt(e.OutputTokens),
            ["cache_creation_tokens"] = ToInt(e.CacheCreationTokens),
            ["cache_read_tokens"] = ToInt(e.CacheReadTokens),
        },
        ["session_pin_applied"] = false,
        ["outcome"] = "success",
        ["latency_ms"] = 0,
        ["tags"] = new JsonObject
        {
            ["project"] = e.Project,
            ["team"] = null,
            ["client_name"] = "claude-code",
            ["commit"] = commit,
            ["branch"] = e.Branch,
        },
    };

    private static int ToInt(long v) => v > int.MaxValue ? int.MaxValue : (int)v;
}
