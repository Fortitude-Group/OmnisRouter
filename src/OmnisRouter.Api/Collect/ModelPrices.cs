namespace OmnisRouter.Api.Collect;

/// <summary>
/// Published API list prices per model, used to turn a transcript's token counts into the USD
/// shadow cost of subscription usage. Rates are USD per million tokens; a model id is matched to
/// a tier by case-insensitive substring, first match wins, so order specific matches first.
/// </summary>
internal static class ModelPrices
{
    private sealed record Rate(decimal Input, decimal Output, decimal CacheRead, decimal CacheWrite);

    // Cache read is 0.1x input, cache write 1.25x input, on every current Claude tier.
    private static readonly (string Match, Rate Rate)[] Table =
    {
        ("opus", new Rate(15m, 75m, 1.5m, 18.75m)),
        ("sonnet", new Rate(3m, 15m, 0.30m, 3.75m)),
        ("haiku", new Rate(0.80m, 4m, 0.08m, 1.00m)),
        ("fable", new Rate(1.00m, 5m, 0.10m, 1.25m)),   // estimate; adjust when published
    };

    // Fallback for an unrecognised model: the Sonnet tier, so cost is neither zero nor wild.
    private static readonly Rate Fallback = new(3m, 15m, 0.30m, 3.75m);

    public static double CostUsd(string modelId, long input, long output, long cacheRead, long cacheWrite)
    {
        var rate = Find(modelId) ?? Fallback;
        var usd =
            (input * rate.Input) + (output * rate.Output) +
            (cacheRead * rate.CacheRead) + (cacheWrite * rate.CacheWrite);
        return (double)(usd / 1_000_000m);
    }

    public static bool IsKnown(string modelId) => Find(modelId) is not null;

    private static Rate? Find(string modelId)
    {
        foreach (var (match, rate) in Table)
        {
            if (modelId.Contains(match, StringComparison.OrdinalIgnoreCase))
            {
                return rate;
            }
        }

        return null;
    }
}
