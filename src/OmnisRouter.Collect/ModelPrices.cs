namespace OmnisRouter.Collect;

/// <summary>
/// Published API list prices per model, used to turn a transcript's token counts into the USD
/// shadow cost of subscription usage. Rates are USD per million tokens; a model id is matched to
/// a tier by case-insensitive substring, first match wins, so order specific matches first.
/// Cache-write shadow figures (<see cref="CacheWriteShadowUsd"/>) use the 5-minute rate and the
/// base (short prompt) tier, since they carry no 1-hour split or prompt length.
/// </summary>
public static class ModelPrices
{
    // Rates are per million tokens. CacheWrite is the 5-minute write; the 1-hour write is always 2x
    // input (see CacheWrite1hMultiplier). Long, when set, reprices the WHOLE request (output too)
    // once its prompt length is strictly over LongThreshold.
    private sealed record Rate(
        decimal Input, decimal Output, decimal CacheRead, decimal CacheWrite,
        long LongThreshold = 0, Rate? Long = null);

    private const decimal CacheWrite1hMultiplier = 2m;

    // Haiku 5.5 over 100,000 prompt tokens: 5x the base rates on every token type.
    private static readonly Rate Haiku55Long = new(0.50m, 2.50m, 0.05m, 0.625m);

    // Published Anthropic list prices, checked 2026-10-19 (platform.claude.com pricing page).
    // Order matters, specific fragments first. The 5-minute write is 1.25x input everywhere. The read
    // is 0.1x input, except 0.05x on Opus and Sonnet 5.5 and 0.025x on Fable 5.1. Dated ids
    // (claude-opus-4-1-20250805) match on the same fragments.
    private static readonly (string Match, Rate Rate)[] Table =
    {
        ("opus-5-5", new Rate(4m, 20m, 0.20m, 5m)),
        ("sonnet-5-5", new Rate(2m, 10m, 0.10m, 2.50m)),
        ("haiku-5-5", new Rate(0.10m, 0.50m, 0.01m, 0.125m, 100_000, Haiku55Long)),
        ("fable-5-1", new Rate(10m, 50m, 0.25m, 12.50m)),
        ("fable", new Rate(10m, 50m, 1m, 12.50m)),
        ("opus-5", new Rate(5m, 25m, 0.50m, 6.25m)),
        ("opus-4-5", new Rate(5m, 25m, 0.50m, 6.25m)),
        ("opus-4-6", new Rate(5m, 25m, 0.50m, 6.25m)),
        ("opus-4-7", new Rate(5m, 25m, 0.50m, 6.25m)),
        ("opus-4-8", new Rate(5m, 25m, 0.50m, 6.25m)),
        ("opus-4", new Rate(15m, 75m, 1.5m, 18.75m)),    // Opus 4 and 4.1
        ("3-opus", new Rate(15m, 75m, 1.5m, 18.75m)),    // claude-3-opus ids put the version first
        ("opus", new Rate(5m, 25m, 0.50m, 6.25m)),        // an unknown newer Opus gets the current rate
        ("sonnet-5", new Rate(2m, 10m, 0.20m, 2.50m)),
        ("sonnet", new Rate(3m, 15m, 0.30m, 3.75m)),
        ("haiku-4-5", new Rate(1m, 5m, 0.10m, 1.25m)),
        ("3-5-haiku", new Rate(0.80m, 4m, 0.08m, 1.00m)),
        ("haiku", new Rate(1m, 5m, 0.10m, 1.25m)),
    };

    // Fallback for an unrecognised model: the Sonnet tier, so cost is neither zero nor wild.
    private static readonly Rate Fallback = new(3m, 15m, 0.30m, 3.75m);

    /// <summary>Cost with every cache write priced at the 5-minute rate.</summary>
    public static double CostUsd(string modelId, long input, long output, long cacheRead, long cacheWrite)
        => CostUsd(modelId, input, output, cacheRead, cacheWrite, 0);

    /// <summary>
    /// Cost with <paramref name="cacheWrite1h"/> of the <paramref name="cacheWrite"/> tokens priced at
    /// the 1-hour write rate (2x input) and the rest at the 5-minute rate. Claude Code transcripts split
    /// these as <c>ephemeral_1h_input_tokens</c> and <c>ephemeral_5m_input_tokens</c>.
    /// </summary>
    public static double CostUsd(
        string modelId, long input, long output, long cacheRead, long cacheWrite, long cacheWrite1h)
    {
        var rate = Find(modelId) ?? Fallback;

        // Prompt length counts every input token, cache reads and writes included.
        if (rate.Long is not null && input + cacheRead + cacheWrite > rate.LongThreshold)
        {
            rate = rate.Long;
        }

        var oneHour = Math.Clamp(cacheWrite1h, 0, Math.Max(cacheWrite, 0));
        var usd =
            (input * rate.Input) + (output * rate.Output) + (cacheRead * rate.CacheRead) +
            ((cacheWrite - oneHour) * rate.CacheWrite) +
            (oneHour * rate.Input * CacheWrite1hMultiplier);
        return (double)(usd / 1_000_000m);
    }

    /// <summary>
    /// The USD shadow cost of the cache-write premium on <paramref name="cacheWrite"/> tokens: the extra
    /// a write costs over a read (write rate minus read rate). This is the coarse "cache re-write" figure
    /// collect mode can show without prefix bytes — it cannot tell an avoidable re-write from a first
    /// write, so it reports the gross premium, honestly labelled as an estimate, not a bill.
    /// </summary>
    public static double CacheWriteShadowUsd(string modelId, long cacheWrite)
    {
        if (cacheWrite <= 0)
        {
            return 0d;
        }

        var rate = Find(modelId) ?? Fallback;
        var premium = rate.CacheWrite - rate.CacheRead;
        return (double)(cacheWrite * premium / 1_000_000m);
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
