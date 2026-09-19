using OmnisRouter.CacheHygiene;

namespace OmnisRouter.CacheHygiene.Tests;

public class CacheHygieneTallyTests
{
    private sealed class ManualClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 9, 18, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;   // make the day boundary deterministic
    }

    private static readonly PricingStamp Stamp = new("2026-08-15", "2026-08-15", 0.8m, ShadowPrice: false);

    private static CacheHygieneResult Miss(decimal waste, decimal saved = 0m, int recomputed = 0, int savedTokens = 0) => new()
    {
        Missed = true,
        WasteGbp = waste,
        SavedGbp = saved,
        RecomputedTokens = recomputed,
        SavedTokens = savedTokens,
        Pricing = Stamp,
    };

    [Fact]
    public void Accumulates_waste_and_saving_into_both_buckets()
    {
        var tally = new CacheHygieneTally(new ManualClock());
        tally.Record(Miss(waste: 0.02m, recomputed: 200));
        tally.Record(Miss(waste: 0m, saved: 0.01m, savedTokens: 100));

        var s = tally.Snapshot(measurementEnabled: true);

        Assert.Equal(0.02m, s.SinceStart.AvoidableWasteGbp);
        Assert.Equal(0.01m, s.SinceStart.RecoveredGbp);
        Assert.Equal(200, s.SinceStart.RecomputedTokens);
        Assert.Equal(100, s.SinceStart.SavedTokens);
        Assert.Equal(s.SinceStart, s.Today);   // same day, so today mirrors since-start
    }

    [Fact]
    public void Counts_avoidable_misses_and_recoveries_separately()
    {
        var tally = new CacheHygieneTally(new ManualClock());
        tally.Record(Miss(waste: 0.02m));          // avoidable miss, no recovery
        tally.Record(Miss(waste: 0m, saved: 0.01m)); // recovery, no waste (unavoidable pays £0)
        tally.Record(Miss(waste: 0m));             // unavoidable miss: neither counted

        var s = tally.Snapshot(true);

        Assert.Equal(1, s.SinceStart.MissCount);
        Assert.Equal(1, s.SinceStart.RecoveryCount);
    }

    [Fact]
    public void Today_resets_at_the_day_boundary_but_since_start_persists()
    {
        var clock = new ManualClock();
        var tally = new CacheHygieneTally(clock);
        tally.Record(Miss(waste: 0.05m));

        clock.Now = clock.Now.AddDays(1);   // next day
        var s = tally.Snapshot(true);

        Assert.Equal(0.05m, s.SinceStart.AvoidableWasteGbp);   // retained
        Assert.Equal(0m, s.Today.AvoidableWasteGbp);           // reset
        Assert.Equal(0, s.Today.MissCount);
    }

    [Fact]
    public void Snapshot_carries_the_latest_pricing_stamp()
    {
        var tally = new CacheHygieneTally(new ManualClock());
        tally.Record(Miss(waste: 0.02m));

        var s = tally.Snapshot(true);

        Assert.NotNull(s.Pricing);
        Assert.Equal("2026-08-15", s.Pricing!.PricingVersion);
        Assert.False(s.Pricing.ShadowPrice);
    }

    [Fact]
    public void Fresh_tally_is_all_zero()
    {
        var s = new CacheHygieneTally(new ManualClock()).Snapshot(true);

        Assert.Equal(CachePeriod.Empty, s.SinceStart);
        Assert.Equal(CachePeriod.Empty, s.Today);
        Assert.Null(s.Pricing);
    }
}
