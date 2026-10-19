using OmnisRouter.Core.Model;
using OmnisRouter.Store.Pricing;

namespace OmnisRouter.Api.Tests;

/// <summary>
/// Long-context tiers: a request strictly over the threshold pays the tier rates for everything,
/// output included. Cache tokens count toward prompt length. Snapshots without a tier stay flat.
/// </summary>
public class PricingBookTierTests
{
    private const string TieredYaml =
        """
        snapshot_date: "2026-10-19"
        pricing:
          - provider: anthropic
            model_id: tiered
            input_per_1k: 1.0
            output_per_1k: 2.0
            cache_read_per_1k: 0.1
            cache_write_per_1k: 1.25
            long_context:
              threshold_tokens: 100000
              input_per_1k: 5.0
              output_per_1k: 10.0
              cache_read_per_1k: 0.5
              cache_write_per_1k: 6.25
          - provider: anthropic
            model_id: flat
            input_per_1k: 1.0
            output_per_1k: 2.0
            cache_read_per_1k: 0.1
            cache_write_per_1k: 1.25
        """;

    private static readonly ModelRef Tiered = new(Provider.Anthropic, "tiered");
    private static readonly ModelRef Flat = new(Provider.Anthropic, "flat");

    private static PricingBook BookFrom(string yaml)
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        File.WriteAllText(Path.Combine(dir, "2026-10-19.yaml"), yaml);
        return new PricingBook(new PricingBookOptions { PricingDirectory = dir });
    }

    private static string ShippedPricingDir()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "config", "pricing");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new DirectoryNotFoundException("config/pricing not found above the test binaries.");
    }

    [Fact]
    public void Under_the_threshold_prices_at_base_rates()
    {
        var book = BookFrom(TieredYaml);

        // 99,999 in, 1,000 out: 99.999 * 1.0 + 1 * 2.0
        Assert.Equal(101.999m, book.EstimateUsd(Tiered, 99_999, 1_000));
    }

    [Fact]
    public void Exactly_at_the_threshold_is_not_over_and_prices_at_base_rates()
    {
        var book = BookFrom(TieredYaml);

        // 100,000 in, 1,000 out: 100 * 1.0 + 1 * 2.0
        Assert.Equal(102m, book.EstimateUsd(Tiered, 100_000, 1_000));
    }

    [Fact]
    public void Over_the_threshold_prices_the_whole_request_including_output_at_tier_rates()
    {
        var book = BookFrom(TieredYaml);

        // 100,001 in, 1,000 out: 100.001 * 5.0 + 1 * 10.0
        Assert.Equal(510.005m, book.EstimateUsd(Tiered, 100_001, 1_000));
    }

    [Fact]
    public void Cache_tokens_count_toward_prompt_length_and_push_a_request_over()
    {
        var book = BookFrom(TieredYaml);

        // 50k input + 40k cache read + 10,001 cache write = 100,001 prompt tokens: over.
        var over = new Usage { InputTokens = 50_000, OutputTokens = 1_000, CacheReadTokens = 40_000, CacheCreationTokens = 10_001 };
        // 50*5 + 1*10 + 40*0.5 + 10.001*6.25
        Assert.Equal(250m + 10m + 20m + 62.50625m, book.EstimateUsd(Tiered, over));

        // One token fewer is exactly at the threshold: base rates.
        var at = new Usage { InputTokens = 50_000, OutputTokens = 1_000, CacheReadTokens = 40_000, CacheCreationTokens = 10_000 };
        // 50*1 + 1*2 + 40*0.1 + 10*1.25
        Assert.Equal(50m + 2m + 4m + 12.5m, book.EstimateUsd(Tiered, at));
    }

    [Fact]
    public void An_entry_without_a_long_context_block_is_flat_however_long_the_prompt()
    {
        var book = BookFrom(TieredYaml);
        var usage = new Usage { InputTokens = 500_000, OutputTokens = 1_000, CacheReadTokens = 100_000, CacheCreationTokens = 10_000 };

        // 500*1 + 1*2 + 100*0.1 + 10*1.25
        Assert.Equal(524.5m, book.EstimateUsd(Flat, usage));
        Assert.Equal(502m, book.EstimateUsd(Flat, 500_000, 1_000));
    }

    [Fact]
    public void Cache_write_premium_stays_at_the_base_rate()
    {
        var book = BookFrom(TieredYaml);

        Assert.Equal((1.25m - 0.1m) / 1000m, book.CacheWritePremiumUsdPerToken(Tiered));
    }

    [Fact]
    public void Shipped_latest_snapshot_prices_haiku_5_5_across_its_tier()
    {
        var book = new PricingBook(new PricingBookOptions { PricingDirectory = ShippedPricingDir() });
        var haiku = new ModelRef(Provider.Anthropic, "claude-haiku-5-5");

        Assert.Equal("2026-10-19", book.SnapshotDate);
        Assert.Equal((0.0001m * 100m) + (0.0005m * 1m), book.EstimateUsd(haiku, 100_000, 1_000));
        Assert.Equal((0.0005m * 100.001m) + (0.0025m * 1m), book.EstimateUsd(haiku, 100_001, 1_000));
    }

    [Fact]
    public void The_2026_08_15_snapshot_still_loads_flat()
    {
        var book = new PricingBook(new PricingBookOptions { PricingDirectory = ShippedPricingDir(), SnapshotDate = "2026-08-15" });
        var opus = new ModelRef(Provider.Anthropic, "claude-opus-5");

        Assert.Equal((0.005m * 500m) + (0.025m * 1m), book.EstimateUsd(opus, 500_000, 1_000));
    }
}
