using OmnisRouter.Collect;

namespace OmnisRouter.Collect.Tests;

public class CollectEngineIdempotencyTests
{
    private static CollectEngineOptions Backfill(string root, bool watch = false, int interval = 0)
        => new(root, Since: null, Batch: 1000, Watch: watch, Interval: interval);

    [Fact]
    public async Task Backfill_posts_each_unique_id_once_and_dedupes_repeats()
    {
        using var dir = new TranscriptDir();
        dir.Write("a.jsonl", "m1");
        dir.Write("a.jsonl", "m1");   // same id repeated in the same file
        dir.Write("b.jsonl", "m2");

        var sink = new FakeReceiptSink();
        var engine = new CollectEngine(Backfill(dir.Root), sink, "https://vigil.test", new FakeClock());

        await engine.RunAsync(CancellationToken.None);

        Assert.Equal(new[] { "m1", "m2" }, sink.PostedIds.OrderBy(x => x));
        Assert.Equal(2, engine.Status.SessionReceipts);
        Assert.Equal(CollectState.Stopped, engine.Status.State);
    }

    [Fact]
    public async Task Dry_run_sink_counts_every_unique_entry()
    {
        using var dir = new TranscriptDir();
        dir.Write("a.jsonl", "m1");
        dir.Write("b.jsonl", "m2");
        dir.Write("c.jsonl", "m3");

        var engine = new CollectEngine(Backfill(dir.Root), new NullReceiptSink(), "dry", new FakeClock());
        await engine.RunAsync(CancellationToken.None);

        Assert.Equal(3, engine.Status.SessionReceipts);
        Assert.Equal(0, engine.Status.Duplicates);
    }

    [Fact]
    public async Task Watch_does_not_repost_already_seen_entries()
    {
        using var dir = new TranscriptDir();
        dir.Write("a.jsonl", "m1");
        dir.Write("a.jsonl", "m2");

        var sink = new FakeReceiptSink();
        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(400));

        var engine = new CollectEngine(Backfill(dir.Root, watch: true), sink, "https://vigil.test", new FakeClock());
        await engine.RunAsync(cts.Token);

        // Backfill posts once; every subsequent watch tick re-scans the same file but posts nothing.
        Assert.Equal(1, sink.Calls);
        Assert.Equal(new[] { "m1", "m2" }, sink.PostedIds.OrderBy(x => x));
    }

    [Fact]
    public async Task A_failed_tick_rolls_back_and_the_next_tick_reposts_exactly_once()
    {
        using var dir = new TranscriptDir();   // empty at first, so backfill posts nothing (no throw)

        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromSeconds(5));   // safety net

        var sink = new FakeReceiptSink { FailUntilCall = 1 };
        sink.OnPost = call => { if (call >= 2) { cts.Cancel(); } };

        var engine = new CollectEngine(Backfill(dir.Root, watch: true), sink, "https://vigil.test", new FakeClock());

        var run = engine.RunAsync(cts.Token);
        await Wait.Until(() => engine.Status.State is CollectState.Watching or CollectState.Error);
        dir.WriteMany("late.jsonl", "m1", "m2");   // both entries appear at once, after watch starts
        await run;

        // First post threw and was rolled back; the retry posted both ids exactly once.
        Assert.Equal(new[] { "m1", "m2" }, sink.PostedIds.OrderBy(x => x));
        Assert.Equal(2, engine.Status.SessionReceipts);
        Assert.Null(engine.Status.LastError);                 // cleared on the good tick
        Assert.NotEqual(CollectState.Error, engine.Status.State);
    }
}
