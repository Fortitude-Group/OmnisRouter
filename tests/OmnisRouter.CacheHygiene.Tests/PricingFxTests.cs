using OmnisRouter.Core.Model;
using OmnisRouter.Store.Pricing;

namespace OmnisRouter.CacheHygiene.Tests;

public class PricingFxTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "omnis-fx", Guid.NewGuid().ToString("n"));

    private PricingBook LoadWithFx()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "2026-08-15.yaml"), """
            snapshot_date: "2026-08-15"
            fx:
              usd_gbp: 0.79
              fx_date: "2026-08-15"
            pricing:
              - provider: anthropic
                model_id: claude-haiku-4-5
                input_per_1k: 0.001
                output_per_1k: 0.005
                cache_write_per_1k: 0.00125
                cache_read_per_1k: 0.0001
            """);
        return new PricingBook(new PricingBookOptions { PricingDirectory = _dir, SnapshotDate = "2026-08-15" });
    }

    [Fact]
    public void Loads_the_fx_rate_and_date()
    {
        var book = LoadWithFx();
        Assert.Equal(0.79m, book.UsdGbp);
        Assert.Equal("2026-08-15", book.FxDate);
        Assert.Equal("2026-08-15", book.SnapshotDate);
    }

    [Fact]
    public void Cache_write_premium_is_write_minus_read_per_token()
    {
        var book = LoadWithFx();
        var model = new ModelRef(Provider.Anthropic, "claude-haiku-4-5");
        // (0.00125 - 0.0001) / 1000
        Assert.Equal((0.00125m - 0.0001m) / 1000m, book.CacheWritePremiumUsdPerToken(model));
    }

    [Fact]
    public void Unknown_model_has_no_premium()
    {
        var book = LoadWithFx();
        Assert.Equal(0m, book.CacheWritePremiumUsdPerToken(new ModelRef(Provider.OpenAI, "nope")));
    }

    [Fact]
    public void A_snapshot_without_fx_yields_zero_rate()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "2026-01-01.yaml"), """
            snapshot_date: "2026-01-01"
            pricing:
              - provider: anthropic
                model_id: m
                input_per_1k: 0.001
                output_per_1k: 0.002
            """);
        var book = new PricingBook(new PricingBookOptions { PricingDirectory = _dir, SnapshotDate = "2026-01-01" });
        Assert.Equal(0m, book.UsdGbp);
        Assert.Equal(string.Empty, book.FxDate);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
