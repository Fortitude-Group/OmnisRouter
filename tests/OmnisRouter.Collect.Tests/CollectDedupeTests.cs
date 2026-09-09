using OmnisRouter.Collect;

namespace OmnisRouter.Collect.Tests;

/// <summary>
/// US4 dedupe: while a client is connected to the local router proxy, the tray excludes it from the
/// collector's scope so its routed traffic is reported once — by the router's receipts — and not also
/// tailed from the client's transcripts. The exclusion set is live (a reference the tray mutates), so
/// disconnecting a client returns it to collector scope without restarting the engine.
/// </summary>
public class CollectDedupeTests
{
    [Fact]
    public async Task Backfill_posts_nothing_while_ClaudeCode_is_excluded()
    {
        using var dir = new TranscriptDir();
        dir.Write("a.jsonl", "m1");
        dir.Write("b.jsonl", "m2");

        var excluded = new HashSet<string> { CollectSource.ClaudeCode };
        var options = new CollectEngineOptions(dir.Root, Since: null, Batch: 1000, Watch: false, Interval: 0, ExcludedClients: excluded);
        var sink = new FakeReceiptSink();
        var engine = new CollectEngine(options, sink, "https://vigil.test", new FakeClock());

        await engine.RunAsync(CancellationToken.None);

        Assert.Empty(sink.PostedIds);
        Assert.Equal(0, engine.Status.SessionReceipts);
    }

    [Fact]
    public async Task Disconnecting_ClaudeCode_returns_it_to_collector_scope_without_restart()
    {
        using var dir = new TranscriptDir();
        dir.Write("a.jsonl", "m1");

        // Start with Claude Code connected (excluded): the shared set is what the tray mutates.
        var excluded = new HashSet<string> { CollectSource.ClaudeCode };
        var options = new CollectEngineOptions(dir.Root, Since: null, Batch: 1000, Watch: true, Interval: 0, ExcludedClients: excluded);
        var sink = new FakeReceiptSink();
        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromSeconds(10));   // safety net

        var engine = new CollectEngine(options, sink, "https://vigil.test", new FakeClock());
        var run = engine.RunAsync(cts.Token);
        await Wait.Until(() => engine.Status.State == CollectState.Watching);

        // Excluded: nothing is collected even though a transcript exists.
        Assert.Empty(sink.PostedIds);

        // "Disconnect" Claude Code, then new usage appears — it must now be collected.
        excluded.Clear();
        dir.Write("a.jsonl", "m2");

        await Wait.Until(() => sink.PostedIds.Contains("m2"));
        cts.Cancel();
        await run;

        Assert.Contains("m2", sink.PostedIds);
    }
}
