using OmnisRouter.LocalProxy;

namespace OmnisRouter.LocalProxy.Tests;

public class CacheHygieneNudgeTests
{
    private static CacheHygieneSummaryInfo Summary(decimal recoveredToday, string source = "routed", bool measuring = true) =>
        new(
            measuring,
            source,
            SinceStart: new CachePeriodInfo(0.10m, recoveredToday, 1000, 500, 4, 2),
            Today: new CachePeriodInfo(0.05m, recoveredToday, 500, 250, 2, 1),
            PricingVersion: "2026-11-01",
            FxDate: "2026-11-01",
            UsdGbp: 0.79m,
            ShadowPrice: false);

    [Fact]
    public void Shows_next_to_a_real_saving_when_vigil_is_not_connected()
    {
        Assert.Equal(CacheHygieneDisplay.NudgeText, CacheHygieneDisplay.SignupNudge(Summary(0.42m), vigilConnected: false));
    }

    [Fact]
    public void Shows_at_exactly_the_threshold()
    {
        Assert.NotNull(CacheHygieneDisplay.SignupNudge(Summary(CacheHygieneDisplay.NudgeMinRecoveredGbp), vigilConnected: false));
    }

    [Fact]
    public void Never_shows_once_vigil_is_connected()
    {
        Assert.Null(CacheHygieneDisplay.SignupNudge(Summary(5m), vigilConnected: true));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(0.0099)]
    public void Stays_quiet_below_a_penny_recovered_today(double recovered)
    {
        Assert.Null(CacheHygieneDisplay.SignupNudge(Summary((decimal)recovered), vigilConnected: false));
    }

    [Fact]
    public void Stays_quiet_when_not_measuring_or_in_collect_mode()
    {
        Assert.Null(CacheHygieneDisplay.SignupNudge(null, vigilConnected: false));
        Assert.Null(CacheHygieneDisplay.SignupNudge(Summary(1m, measuring: false), vigilConnected: false));
        Assert.Null(CacheHygieneDisplay.SignupNudge(Summary(1m, source: "collect"), vigilConnected: false));
    }
}
