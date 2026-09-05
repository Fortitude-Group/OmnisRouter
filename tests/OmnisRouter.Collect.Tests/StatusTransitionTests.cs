using System.Collections.Concurrent;
using OmnisRouter.Collect;

namespace OmnisRouter.Collect.Tests;

public class StatusTransitionTests
{
    [Fact]
    public async Task Pause_then_resume_moves_through_paused_and_back_to_watching()
    {
        using var dir = new TranscriptDir();
        dir.Write("a.jsonl", "m1");

        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromSeconds(5));

        var signals = new ConcurrentQueue<CollectSignal>();
        var engine = new CollectEngine(
            new CollectEngineOptions(dir.Root, null, 1000, Watch: true, Interval: 0),
            new FakeReceiptSink(), "https://vigil.test", new FakeClock());
        engine.Emitted += e => signals.Enqueue(e.Signal);

        var run = engine.RunAsync(cts.Token);
        await Wait.Until(() => engine.Status.State == CollectState.Watching);

        engine.Pause();
        Assert.Equal(CollectState.Paused, engine.Status.State);

        engine.Resume();
        Assert.Equal(CollectState.Watching, engine.Status.State);

        cts.Cancel();
        await run;

        Assert.Contains(CollectSignal.Paused, signals);
        Assert.Contains(CollectSignal.Resumed, signals);
        Assert.Contains(CollectSignal.WatchStarted, signals);
        Assert.Contains(CollectSignal.Stopped, signals);
    }

    [Fact]
    public async Task Today_counters_reset_when_the_local_date_rolls_over()
    {
        using var dir = new TranscriptDir();   // empty; entries arrive while watching

        var clock = new FakeClock
        {
            Local = new DateTimeOffset(2026, 9, 5, 23, 59, 0, TimeSpan.Zero),
        };

        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromSeconds(5));

        var sink = new FakeReceiptSink();
        sink.OnPost = call =>
        {
            if (call == 1)
            {
                // Advance past midnight, then drop a second entry for the new day.
                clock.Local = new DateTimeOffset(2026, 9, 6, 0, 1, 0, TimeSpan.Zero);
                dir.Write("a.jsonl", "m2");
            }
            else if (call >= 2)
            {
                cts.Cancel();
            }
        };

        var engine = new CollectEngine(
            new CollectEngineOptions(dir.Root, null, 1000, Watch: true, Interval: 0),
            sink, "https://vigil.test", clock);

        var run = engine.RunAsync(cts.Token);
        await Wait.Until(() => engine.Status.State == CollectState.Watching);
        dir.Write("a.jsonl", "m1");           // first entry, on day one
        await run;

        Assert.Equal(2, engine.Status.SessionReceipts);   // both posted this session
        Assert.Equal(1, engine.Status.TodayReceipts);     // but "today" reset at the boundary
    }

    [Fact]
    public async Task A_failing_endpoint_shows_error_then_recovers()
    {
        using var dir = new TranscriptDir();

        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromSeconds(5));

        var signals = new ConcurrentQueue<CollectSignal>();
        var sawError = false;
        var sink = new FakeReceiptSink { FailUntilCall = 1 };
        sink.OnPost = call => { if (call >= 2) { cts.Cancel(); } };

        var engine = new CollectEngine(
            new CollectEngineOptions(dir.Root, null, 1000, Watch: true, Interval: 0),
            sink, "https://vigil.test", new FakeClock());
        engine.Emitted += e =>
        {
            signals.Enqueue(e.Signal);
            if (e.Status.State == CollectState.Error)
            {
                sawError = true;
            }
        };

        var run = engine.RunAsync(cts.Token);
        await Wait.Until(() => engine.Status.State is CollectState.Watching or CollectState.Error);
        dir.WriteMany("a.jsonl", "m1");
        await run;

        Assert.Contains(CollectSignal.TickFailed, signals);
        Assert.True(sawError, "expected the engine to enter the Error state at least once");
        Assert.Null(engine.Status.LastError);                       // cleared on recovery
        Assert.NotEqual(CollectState.Error, engine.Status.State);
    }
}
