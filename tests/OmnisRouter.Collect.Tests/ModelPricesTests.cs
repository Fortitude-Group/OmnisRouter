using OmnisRouter.Collect;

namespace OmnisRouter.Collect.Tests;

public class ModelPricesTests
{
    [Theory]
    [InlineData("claude-opus-4-8", 5d, 25d, 0.50d, 6.25d)]
    [InlineData("claude-opus-4-5-20251101", 5d, 25d, 0.50d, 6.25d)]
    [InlineData("claude-opus-5", 5d, 25d, 0.50d, 6.25d)]
    [InlineData("claude-opus-4-1-20250805", 15d, 75d, 1.5d, 18.75d)]
    [InlineData("claude-opus-4-20250514", 15d, 75d, 1.5d, 18.75d)]
    [InlineData("claude-sonnet-5", 2d, 10d, 0.20d, 2.50d)]
    [InlineData("claude-sonnet-4-6", 3d, 15d, 0.30d, 3.75d)]
    [InlineData("claude-haiku-4-5", 1d, 5d, 0.10d, 1.25d)]
    [InlineData("claude-3-5-haiku-20241022", 0.80d, 4d, 0.08d, 1.00d)]
    [InlineData("claude-fable-5-1", 10d, 50d, 0.25d, 12.50d)]
    [InlineData("claude-fable-5", 10d, 50d, 1d, 12.50d)]
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

    [Theory]
    [InlineData("claude-opus-5-5", 4d, 20d, 0.20d, 5d)]
    [InlineData("claude-sonnet-5-5", 2d, 10d, 0.10d, 2.50d)]
    [InlineData("claude-haiku-5-5", 0.10d, 0.50d, 0.01d, 0.125d)]
    public void Prices_the_5_5_generation_at_its_published_rate(string model, double inRate, double outRate, double cacheReadRate, double cacheWriteRate)
    {
        // 100,000 tokens of each type, the most Haiku 5.5 can take before its long-prompt tier applies,
        // so each rate is isolated at the base price (a tenth of the per-million figure).
        Assert.Equal(inRate / 10, ModelPrices.CostUsd(model, 100_000, 0, 0, 0), 9);
        Assert.Equal(outRate / 10, ModelPrices.CostUsd(model, 0, 100_000, 0, 0), 9);
        Assert.Equal(cacheReadRate / 10, ModelPrices.CostUsd(model, 0, 0, 100_000, 0), 9);
        Assert.Equal(cacheWriteRate / 10, ModelPrices.CostUsd(model, 0, 0, 0, 100_000), 9);
    }

    [Fact]
    public void Opus_5_5_does_not_fall_through_to_the_older_opus_tier()
    {
        Assert.True(ModelPrices.CostUsd("claude-opus-5-5", 1_000_000, 0, 0, 0) < ModelPrices.CostUsd("claude-opus-4-8", 1_000_000, 0, 0, 0));
    }

    [Fact]
    public void Cache_read_is_0_05x_input_on_opus_and_sonnet_5_5_and_0_1x_on_haiku_5_5()
    {
        Assert.Equal(0.05d, ModelPrices.CostUsd("claude-opus-5-5", 0, 0, 1_000_000, 0) / ModelPrices.CostUsd("claude-opus-5-5", 1_000_000, 0, 0, 0), 9);
        Assert.Equal(0.05d, ModelPrices.CostUsd("claude-sonnet-5-5", 0, 0, 1_000_000, 0) / ModelPrices.CostUsd("claude-sonnet-5-5", 1_000_000, 0, 0, 0), 9);
        Assert.Equal(0.10d, ModelPrices.CostUsd("claude-haiku-5-5", 0, 0, 1_000_000, 0) / ModelPrices.CostUsd("claude-haiku-5-5", 1_000_000, 0, 0, 0), 9);
    }

    [Fact]
    public void One_hour_cache_writes_cost_2x_input_and_the_rest_stay_at_the_5_minute_rate()
    {
        // Opus 5.5: 1M writes, 400k of them 1h. 600k * 5 + 400k * (2 * 4) = 3.0 + 3.2 = 6.2 USD.
        Assert.Equal(6.2d, ModelPrices.CostUsd("claude-opus-5-5", 0, 0, 0, 1_000_000, 400_000), 9);
        // All 1h on Sonnet 5.5: 1M * 2 * 2 = 4.0.
        Assert.Equal(4.0d, ModelPrices.CostUsd("claude-sonnet-5-5", 0, 0, 0, 1_000_000, 1_000_000), 9);
        // The 1h share is a subset of the writes, so it never exceeds them.
        Assert.Equal(4.0d, ModelPrices.CostUsd("claude-sonnet-5-5", 0, 0, 0, 1_000_000, 5_000_000), 9);
    }

    [Fact]
    public void Haiku_5_5_at_exactly_100k_prompt_tokens_pays_the_base_rates()
    {
        // 60k input + 30k cache read + 10k cache write = 100,000 (not over).
        var expected = ((60_000 * 0.10d) + (1_000_000 * 0.50d) + (30_000 * 0.01d) + (10_000 * 0.125d)) / 1_000_000d;
        Assert.Equal(expected, ModelPrices.CostUsd("claude-haiku-5-5", 60_000, 1_000_000, 30_000, 10_000), 12);
    }

    [Fact]
    public void Haiku_5_5_over_100k_prompt_tokens_reprices_the_whole_request_including_output()
    {
        // 100,001 prompt tokens counting cache read and write; every token type at the tier rates.
        var expected = ((60_000 * 0.50d) + (1_000_000 * 2.50d) + (30_001 * 0.05d) + (10_000 * 0.625d)) / 1_000_000d;
        Assert.Equal(expected, ModelPrices.CostUsd("claude-haiku-5-5", 60_000, 1_000_000, 30_001, 10_000), 12);
    }

    [Fact]
    public void Long_prompts_on_opus_5_5_stay_linear_because_it_has_no_tier()
    {
        Assert.Equal(
            ModelPrices.CostUsd("claude-opus-5-5", 100, 0, 0, 0) * 3_000,
            ModelPrices.CostUsd("claude-opus-5-5", 300_000, 0, 0, 0),
            9);
    }
}
