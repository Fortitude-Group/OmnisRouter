using OmnisRouter.Collect;

namespace OmnisRouter.Collect.Tests;

public class ModelPricesTests
{
    [Theory]
    [InlineData("claude-opus-4-8", 15d, 75d, 1.5d, 18.75d)]
    [InlineData("claude-sonnet-4-6", 3d, 15d, 0.30d, 3.75d)]
    [InlineData("claude-haiku-4-5", 0.80d, 4d, 0.08d, 1.00d)]
    [InlineData("claude-fable-5-1", 1.00d, 5d, 0.10d, 1.25d)]
    public void Prices_each_tier_at_its_published_rate(string model, double inRate, double outRate, double cacheReadRate, double cacheWriteRate)
    {
        // One million of each token type isolates each rate.
        Assert.Equal(inRate, ModelPrices.CostUsd(model, 1_000_000, 0, 0, 0), 6);
        Assert.Equal(outRate, ModelPrices.CostUsd(model, 0, 1_000_000, 0, 0), 6);
        Assert.Equal(cacheReadRate, ModelPrices.CostUsd(model, 0, 0, 1_000_000, 0), 6);
        Assert.Equal(cacheWriteRate, ModelPrices.CostUsd(model, 0, 0, 0, 1_000_000), 6);
    }

    [Fact]
    public void Sums_all_token_types()
    {
        // Sonnet: 100 in, 50 out, 10 cacheRead, 5 cacheWrite, per-million rates 3/15/0.30/3.75.
        var expected = ((100 * 3d) + (50 * 15d) + (10 * 0.30d) + (5 * 3.75d)) / 1_000_000d;
        Assert.Equal(expected, ModelPrices.CostUsd("claude-sonnet-4-6", 100, 50, 10, 5), 12);
    }

    [Fact]
    public void Unknown_model_falls_back_to_the_sonnet_tier()
    {
        Assert.False(ModelPrices.IsKnown("gpt-5"));
        Assert.Equal(ModelPrices.CostUsd("claude-sonnet-4-6", 1000, 500, 0, 0), ModelPrices.CostUsd("gpt-5", 1000, 500, 0, 0), 12);
    }

    [Fact]
    public void Matches_tier_case_insensitively()
    {
        Assert.True(ModelPrices.IsKnown("CLAUDE-OPUS-4-8"));
    }
}
