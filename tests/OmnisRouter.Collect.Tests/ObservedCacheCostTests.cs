using OmnisRouter.Collect;

namespace OmnisRouter.Collect.Tests;

/// <summary>
/// US3 (feature 005): collect mode shows the shadow cost of observed cache re-writes. It reads token
/// counts only (content-free), so it prices the gross cache-write premium and never classifies cause or
/// avoidability — the figure is honestly an estimate, not a bill.
/// </summary>
public class ObservedCacheCostTests
{
    [Fact]
    public void CacheWriteShadowUsd_prices_the_write_minus_read_premium()
    {
        // haiku: write 1.00, read 0.08 per million => premium 0.92; 1M write tokens => $0.92.
        Assert.Equal(0.92, ModelPrices.CacheWriteShadowUsd("claude-haiku-4-5", 1_000_000), 4);
    }

    [Fact]
    public void CacheWriteShadowUsd_is_zero_for_no_writes()
    {
        Assert.Equal(0d, ModelPrices.CacheWriteShadowUsd("claude-sonnet-4-6", 0));
    }

    [Fact]
    public void CacheLine_shows_a_shadow_estimate_and_never_a_bill()
    {
        var line = StatusFormat.CacheLine(new CollectionStatus
        {
            TodayCacheCreationTokens = 1000,
            TodayCacheWriteShadowUsd = 0.5,
        });

        Assert.Contains("shadow", line);
        Assert.Contains("estimate, not a bill", line);
        Assert.Contains("$0.5", line);
        Assert.DoesNotContain("avoidable", line);   // no cause classification in collect mode
    }

    [Fact]
    public void CacheLine_says_none_when_no_writes_observed()
    {
        Assert.Contains("none observed", StatusFormat.CacheLine(new CollectionStatus { TodayCacheCreationTokens = 0 }));
    }

    [Fact]
    public async Task Backfill_accumulates_observed_cache_write_tokens_and_shadow_cost()
    {
        using var dir = new TranscriptDir();
        dir.Write("a.jsonl", "m1", model: "claude-sonnet-4-6", cacheCreate: 1_000_000);

        var options = new CollectEngineOptions(
            dir.Root, Since: null, Batch: 1000, Watch: false, Interval: 0, ExcludedClients: new ExcludedClientsSet());
        var engine = new CollectEngine(options, new FakeReceiptSink(), "https://vigil.test", new FakeClock());

        await engine.RunAsync(CancellationToken.None);

        Assert.Equal(1_000_000, engine.Status.TodayCacheCreationTokens);
        Assert.Equal(3.45, engine.Status.TodayCacheWriteShadowUsd, 3);   // sonnet premium (3.75 - 0.30) per M
    }

    [Fact]
    public async Task Today_cache_figures_reset_at_the_day_boundary()
    {
        using var dir = new TranscriptDir();
        var clock = new FakeClock { Local = new DateTimeOffset(2026, 9, 18, 10, 0, 0, TimeSpan.Zero) };
        var options = new CollectEngineOptions(
            dir.Root, Since: null, Batch: 1, Watch: true, Interval: 0, ExcludedClients: new ExcludedClientsSet());
        var sink = new FakeReceiptSink();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var engine = new CollectEngine(options, sink, "https://vigil.test", clock);
        var run = engine.RunAsync(cts.Token);

        dir.Write("a.jsonl", "m1", model: "claude-sonnet-4-6", cacheCreate: 1_000_000);
        await Wait.Until(() => engine.Status.TodayCacheCreationTokens == 1_000_000, timeoutMs: 15000);

        // Next day: a new entry rolls "today" so only the new write counts.
        clock.Local = clock.Local.AddDays(1);
        dir.Write("b.jsonl", "m2", model: "claude-sonnet-4-6", cacheCreate: 2_000_000);
        await Wait.Until(() => engine.Status.TodayCacheCreationTokens == 2_000_000, timeoutMs: 15000);

        Assert.Equal(2_000_000, engine.Status.TodayCacheCreationTokens);

        cts.Cancel();
        try { await run; } catch (OperationCanceledException) { }
    }
}
