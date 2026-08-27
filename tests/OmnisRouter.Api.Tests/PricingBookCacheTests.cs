using OmnisRouter.Core.Model;
using OmnisRouter.Store.Pricing;

namespace OmnisRouter.Api.Tests;

/// <summary>
/// The cache-aware <c>EstimateUsd(model, usage)</c> overload is the figure the savings ledger and
/// the OmnisVigil receipt report. Cache economics dominate real agent traffic, so this locks the
/// arithmetic and the fallback behaviour.
/// </summary>
public class PricingBookCacheTests
{
    private static PricingBook BookFrom(string yaml)
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        File.WriteAllText(Path.Combine(dir, "2026-08-15.yaml"), yaml);
        return new PricingBook(new PricingBookOptions { PricingDirectory = dir });
    }

    [Fact]
    public void EstimateUsd_from_usage_prices_input_output_cache_read_and_cache_creation()
    {
        var book = BookFrom(
            """
            snapshot_date: "2026-08-15"
            pricing:
              - provider: openai
                model_id: test-model
                input_per_1k: 1.0
                output_per_1k: 2.0
                cache_read_per_1k: 0.1
                cache_write_per_1k: 1.25
            """);
        var model = new ModelRef(Provider.OpenAI, "test-model");
        var usage = new Usage
        {
            InputTokens = 1000,
            OutputTokens = 1000,
            CacheReadTokens = 1000,
            CacheCreationTokens = 1000,
        };

        // 1*1.0 + 1*2.0 + 1*0.1 + 1*1.25
        Assert.Equal(4.35m, book.EstimateUsd(model, usage));
    }

    [Fact]
    public void EstimateUsd_from_usage_charges_cache_read_at_the_input_rate_when_no_cache_rate_is_set()
    {
        var book = BookFrom(
            """
            snapshot_date: "2026-08-15"
            pricing:
              - provider: openai
                model_id: no-cache-rates
                input_per_1k: 3.0
                output_per_1k: 4.0
            """);
        var model = new ModelRef(Provider.OpenAI, "no-cache-rates");
        var usage = new Usage { InputTokens = 0, OutputTokens = 0, CacheReadTokens = 1000, CacheCreationTokens = 0 };

        // Cache read falls back to the input rate (never free), so 1000 tok * 3.0/1k = 3.0
        Assert.Equal(3.0m, book.EstimateUsd(model, usage));
    }
}
