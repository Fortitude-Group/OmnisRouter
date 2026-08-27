using OmnisRouter.Core.Model;
using OmnisRouter.Core.Routing;
using OmnisRouter.Vigil;

namespace OmnisRouter.Api.Tests;

public class IngestRecordMapperTests
{
    // The closed set of fields the ingest schema allows. Vigil rejects any record with a field
    // outside this set, so the mapper must never emit one. Duplicated here on purpose: if someone
    // adds a field to the mapper that isn't in the contract, this test fails.
    private static readonly HashSet<string> SchemaFields =
    [
        "id", "timestamp", "tenant_id", "router_id", "session_id", "request_hash", "client_format",
        "cluster_id", "chosen_provider", "chosen_model_id", "confidence", "top1_sim", "top2_sim",
        "margin", "decision", "reason", "policy_version", "est_cost_usd", "est_cost_delta_vs_big_usd",
        "actual_cost_usd", "actual_cost_delta_vs_big_usd", "usage", "pricing_snapshot_date",
        "session_pin_applied", "outcome", "latency_ms", "tags",
    ];

    private static DecisionLogEntry FullEntry() => new()
    {
        Id = "abc",
        TenantId = "default",
        Timestamp = DateTimeOffset.UnixEpoch,
        RequestHash = "hash",
        ClientFormat = ClientFormat.OpenAI,
        ClusterId = 3,
        ChosenProvider = Provider.OpenAI,
        ChosenModelId = "gpt-5-nano",
        Confidence = 0.4,
        PolicyVersion = "v5",
        EstCostUsd = 0.01m,
        EstCostDeltaVsBigUsd = -0.09m,
        Outcome = RequestOutcome.Success,
        LatencyMs = 12,
        ActualInputTokens = 100,
        ActualOutputTokens = 20,
        ActualCacheCreationTokens = 0,
        ActualCacheReadTokens = 500,
        ActualCostUsd = 0.008m,
        ActualCostDeltaVsBigUsd = -0.07m,
        TagProject = "web",
        TagClientName = "claude-code",
    };

    [Fact]
    public void ToRecord_emits_only_schema_fields_and_stamps_router_id()
    {
        var obj = IngestRecordMapper.ToRecord(FullEntry(), "router-eu-1");

        foreach (var key in obj.Select(kv => kv.Key))
        {
            Assert.Contains(key, SchemaFields);
        }

        Assert.Equal("router-eu-1", obj["router_id"]!.GetValue<string>());
        Assert.Equal("abc", obj["id"]!.GetValue<string>());
        Assert.Equal("ROUTED", obj["decision"]!.GetValue<string>());
        Assert.Equal("openai", obj["chosen_provider"]!.GetValue<string>());

        var usage = obj["usage"]!.AsObject();
        Assert.Equal(500, usage["cache_read_tokens"]!.GetValue<int>());
        Assert.Equal(100, usage["input_tokens"]!.GetValue<int>());

        var tags = obj["tags"]!.AsObject();
        Assert.Equal("web", tags["project"]!.GetValue<string>());
        Assert.Equal("claude-code", tags["client_name"]!.GetValue<string>());
    }

    [Fact]
    public void ToRecord_nulls_usage_and_tags_when_absent()
    {
        var entry = new DecisionLogEntry
        {
            Id = "x",
            TenantId = "default",
            RequestHash = "h",
            ChosenModelId = "m",
            PolicyVersion = "v",
            Outcome = RequestOutcome.UpstreamError,
        };

        var obj = IngestRecordMapper.ToRecord(entry, "r");

        Assert.Null(obj["usage"]);
        Assert.Null(obj["tags"]);
        Assert.Null(obj["actual_cost_usd"]);
    }
}
