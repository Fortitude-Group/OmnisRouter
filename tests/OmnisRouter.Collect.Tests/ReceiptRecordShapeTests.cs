using System.Text.Json.Nodes;
using OmnisRouter.Collect;

namespace OmnisRouter.Collect.Tests;

/// <summary>
/// Pins the ingest record's shape (FR-022): the tray must post byte-identical receipts to the CLI.
/// If any of these change, the OmnisVigil ingest contract has changed and the contract doc must too.
/// </summary>
public class ReceiptRecordShapeTests
{
    private static JsonObject Build() => ReceiptRecord.From(
        new UsageEntry(
            Id: "m1",
            Timestamp: new DateTimeOffset(2026, 9, 5, 10, 0, 0, TimeSpan.Zero),
            Model: "claude-sonnet-4-6",
            InputTokens: 100, OutputTokens: 50, CacheReadTokens: 10, CacheCreationTokens: 5,
            SessionId: "s1", Project: "proj", Branch: "main", RequestId: "req-1"),
        cost: 0.00123);

    [Fact]
    public void Has_exactly_the_expected_top_level_keys()
    {
        var keys = Build().Select(kv => kv.Key).OrderBy(k => k).ToArray();
        var expected = new[]
        {
            "actual_cost_delta_vs_big_usd", "actual_cost_usd", "chosen_model_id", "chosen_provider",
            "client_format", "cluster_id", "confidence", "decision", "est_cost_delta_vs_big_usd",
            "est_cost_usd", "id", "latency_ms", "margin", "outcome", "policy_version", "reason",
            "request_hash", "router_id", "session_id", "session_pin_applied", "tags", "tenant_id",
            "timestamp", "top1_sim", "top2_sim", "usage",
        }.OrderBy(k => k).ToArray();

        Assert.Equal(expected, keys);
    }

    [Fact]
    public void Carries_the_fixed_collector_fields()
    {
        var r = Build();
        Assert.Equal("local", (string?)r["tenant_id"]);
        Assert.Equal("claude-code-local", (string?)r["router_id"]);
        Assert.Equal("anthropic", (string?)r["client_format"]);
        Assert.Equal("anthropic", (string?)r["chosen_provider"]);
        Assert.Equal("claude-sonnet-4-6", (string?)r["chosen_model_id"]);
        Assert.Equal("DIRECT", (string?)r["decision"]);
        Assert.Equal("claude-code-transcript", (string?)r["reason"]);
        Assert.Equal("collector", (string?)r["policy_version"]);
        Assert.Equal("success", (string?)r["outcome"]);
        Assert.Equal("2026-09-05T10:00:00.0000000+00:00", (string?)r["timestamp"]);
        Assert.Equal("req-1", (string?)r["request_hash"]);
        Assert.Equal(0.00123, (double)r["est_cost_usd"]!);
        Assert.Equal(0.00123, (double)r["actual_cost_usd"]!);
    }

    [Fact]
    public void Usage_block_maps_all_four_token_counts()
    {
        var usage = (JsonObject)Build()["usage"]!;
        Assert.Equal(100, (int)usage["input_tokens"]!);
        Assert.Equal(50, (int)usage["output_tokens"]!);
        Assert.Equal(5, (int)usage["cache_creation_tokens"]!);
        Assert.Equal(10, (int)usage["cache_read_tokens"]!);
    }

    [Fact]
    public void Tags_block_carries_project_and_branch_with_null_placeholders()
    {
        var tags = (JsonObject)Build()["tags"]!;
        Assert.Equal("proj", (string?)tags["project"]);
        Assert.Equal("claude-code", (string?)tags["client_name"]);
        Assert.Equal("main", (string?)tags["branch"]);
        Assert.Null(tags["team"]);
        Assert.Null(tags["commit"]);
    }

    [Fact]
    public void Request_hash_falls_back_to_id_when_request_id_is_absent()
    {
        var r = ReceiptRecord.From(
            new UsageEntry("only-id", DateTimeOffset.UnixEpoch, "claude-haiku-4-5", 1, 1, 0, 0, null, null, null, null),
            cost: 0);
        Assert.Equal("only-id", (string?)r["request_hash"]);
    }
}
