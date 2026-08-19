using OmnisRouter.Core.Abstractions;
using OmnisRouter.Core.Model;
using YamlDotNet.Serialization;

namespace OmnisRouter.Store.Pricing;

/// <summary>Options for <see cref="PricingBook"/>.</summary>
public sealed record PricingBookOptions
{
    /// <summary>Directory holding dated pricing snapshots (<c>&lt;date&gt;.yaml</c>). Defaults to <c>config/pricing</c>.</summary>
    public string PricingDirectory { get; init; } = Path.Combine("config", "pricing");

    /// <summary>
    /// Snapshot date to load (e.g. "2026-08-15"), matching a <c>&lt;date&gt;.yaml</c> file in
    /// <see cref="PricingDirectory"/>. When null, the lexicographically latest file is used
    /// (dated filenames sort chronologically as ISO-8601 strings).
    /// </summary>
    public string? SnapshotDate { get; init; }
}

/// <summary>
/// Loads a pinned, dated pricing snapshot (<c>config/pricing/&lt;date&gt;.yaml</c>) and estimates
/// cost from per-1k-token rates. Cost math stays reproducible and explainable (Principle XII) and
/// matches OmnisBench's snapshot format.
/// </summary>
public sealed class PricingBook : IPricingBook
{
    private readonly Dictionary<string, PricingEntry> _byKey;

    public PricingBook(PricingBookOptions options)
    {
        var path = ResolveSnapshotPath(options);
        var yaml = File.ReadAllText(path);

        // Property names carry explicit [YamlMember] aliases (see below) rather than relying on a
        // naming-convention transform, since "input_per_1k" doesn't round-trip cleanly through the
        // built-in underscored-convention algorithm (it would expect "input_per1_k").
        var deserializer = new DeserializerBuilder()
            .IgnoreUnmatchedProperties()
            .Build();

        var snapshot = deserializer.Deserialize<PricingSnapshotFile>(yaml)
            ?? throw new InvalidOperationException($"Pricing snapshot '{path}' is empty or malformed.");

        if (string.IsNullOrWhiteSpace(snapshot.SnapshotDate))
        {
            throw new InvalidOperationException($"Pricing snapshot '{path}' is missing 'snapshot_date'.");
        }

        SnapshotDate = snapshot.SnapshotDate;

        _byKey = new Dictionary<string, PricingEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in snapshot.Pricing ?? [])
        {
            if (string.IsNullOrWhiteSpace(entry.Provider) || string.IsNullOrWhiteSpace(entry.ModelId))
            {
                continue;
            }

            _byKey[$"{entry.Provider}/{entry.ModelId}"] = entry;
        }
    }

    public string SnapshotDate { get; }

    public decimal EstimateUsd(ModelRef model, int inputTokens, int outputTokens)
    {
        if (!_byKey.TryGetValue(model.PricingKey, out var entry))
        {
            throw new KeyNotFoundException(
                $"No pricing entry for '{model.PricingKey}' in snapshot '{SnapshotDate}'.");
        }

        var inputCost = (decimal)inputTokens / 1000m * entry.InputPer1K;
        var outputCost = (decimal)outputTokens / 1000m * entry.OutputPer1K;

        return inputCost + outputCost;
    }

    private static string ResolveSnapshotPath(PricingBookOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.SnapshotDate))
        {
            var explicitPath = Path.Combine(options.PricingDirectory, $"{options.SnapshotDate}.yaml");
            if (!File.Exists(explicitPath))
            {
                throw new FileNotFoundException(
                    $"Pricing snapshot for date '{options.SnapshotDate}' not found.", explicitPath);
            }

            return explicitPath;
        }

        if (!Directory.Exists(options.PricingDirectory))
        {
            throw new DirectoryNotFoundException(
                $"Pricing directory '{options.PricingDirectory}' does not exist.");
        }

        var latest = Directory.EnumerateFiles(options.PricingDirectory, "*.yaml")
            .OrderByDescending(f => Path.GetFileNameWithoutExtension(f), StringComparer.Ordinal)
            .FirstOrDefault();

        return latest ?? throw new FileNotFoundException(
            $"No pricing snapshot (*.yaml) found under '{options.PricingDirectory}'.");
    }

    private sealed class PricingSnapshotFile
    {
        [YamlMember(Alias = "snapshot_date")]
        public string? SnapshotDate { get; set; }

        [YamlMember(Alias = "pricing")]
        public List<PricingEntry>? Pricing { get; set; }
    }

    private sealed class PricingEntry
    {
        [YamlMember(Alias = "provider")]
        public string? Provider { get; set; }

        [YamlMember(Alias = "model_id")]
        public string? ModelId { get; set; }

        [YamlMember(Alias = "input_per_1k")]
        public decimal InputPer1K { get; set; }

        [YamlMember(Alias = "output_per_1k")]
        public decimal OutputPer1K { get; set; }

        [YamlMember(Alias = "cache_write_per_1k")]
        public decimal? CacheWritePer1K { get; set; }

        [YamlMember(Alias = "cache_read_per_1k")]
        public decimal? CacheReadPer1K { get; set; }
    }
}
