using OmnisRouter.Collect;

namespace OmnisRouter.Collect.Tests;

public class StatusFormattingTests
{
    private static CollectionStatus Status(CollectState state, int today = 0, DateTimeOffset? lastPost = null, string? error = null)
        => new() { State = state, TodayReceipts = today, LastPostUtc = lastPost, LastError = error };

    [Fact]
    public void Tooltip_shows_last_post_and_today_when_watching()
    {
        var s = Status(CollectState.Watching, today: 49075, lastPost: new DateTimeOffset(2026, 9, 5, 13, 1, 0, TimeSpan.Zero));
        var tip = StatusFormat.Tooltip(s);
        Assert.StartsWith("OmnisRouter · last posted ", tip);
        Assert.Contains("49,075 today", tip);
    }

    [Fact]
    public void Tooltip_before_first_post_says_watching_with_zero()
    {
        Assert.Equal("OmnisRouter · watching · 0 today", StatusFormat.Tooltip(Status(CollectState.Watching)));
    }

    [Fact]
    public void Tooltip_reflects_paused_and_error()
    {
        Assert.Equal("OmnisRouter · paused", StatusFormat.Tooltip(Status(CollectState.Paused)));
        Assert.Equal("OmnisRouter · can't reach OmnisVigil · retrying", StatusFormat.Tooltip(Status(CollectState.Error, error: "boom")));
    }

    [Fact]
    public void Popup_state_line_names_each_state()
    {
        Assert.Equal("Watching", StatusFormat.StateLine(Status(CollectState.Watching)));
        Assert.Equal("Paused", StatusFormat.StateLine(Status(CollectState.Paused)));
        Assert.Equal("Backfilling…", StatusFormat.StateLine(Status(CollectState.Backfilling)));
        Assert.Equal("Error: disk full", StatusFormat.StateLine(Status(CollectState.Error, error: "disk full")));
    }

    [Fact]
    public void Popup_last_posted_line_handles_never_posted()
    {
        Assert.Equal("Last posted: not yet", StatusFormat.LastPostedLine(Status(CollectState.Watching)));
    }

    [Fact]
    public void Popup_today_line_formats_the_count()
    {
        Assert.Equal("Today: 1,234 receipts", StatusFormat.TodayLine(Status(CollectState.Watching, today: 1234)));
    }
}
