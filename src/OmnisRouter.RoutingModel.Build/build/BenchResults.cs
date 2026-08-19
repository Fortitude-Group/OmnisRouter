using System.Text.Json;
using System.Text.Json.Serialization;

namespace OmnisRouter.RoutingModel.Build.Build;

/// <summary>
/// Per-domain benchmark quality scores (0-1) for each candidate model, keyed
/// <c>"&lt;provider&gt;/&lt;model_id&gt;"</c> (provider lowercased, matching
/// <c>config/pricing/&lt;date&gt;.yaml</c> casing). In the real pipeline this file is produced by
/// OmnisBench running every candidate in <c>config/models.yaml</c> against the labeled dataset; here
/// it is a small hand-authored sample (routing/datasets/sample-bench-results.json) with the exact
/// same shape, so the build pipeline is identical either way (see routing/BUILD.md).
/// </summary>
public sealed class BenchResults
{
    private readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, double>> _byDomain;

    private BenchResults(IReadOnlyDictionary<string, IReadOnlyDictionary<string, double>> byDomain)
    {
        _byDomain = byDomain;
    }

    public static BenchResults Load(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Bench-results file not found: {path}", path);
        }

        var json = File.ReadAllText(path);
        var file = JsonSerializer.Deserialize<BenchResultsFileDto>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                   ?? throw new InvalidDataException($"Bench-results file '{path}' is empty or malformed.");

        if (file.Domains is null || file.Domains.Count == 0)
        {
            throw new InvalidDataException($"Bench-results file '{path}' has no 'domains'.");
        }

        var byDomain = new Dictionary<string, IReadOnlyDictionary<string, double>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (domain, scores) in file.Domains)
        {
            byDomain[domain] = new Dictionary<string, double>(scores, StringComparer.OrdinalIgnoreCase);
        }

        return new BenchResults(byDomain);
    }

    /// <summary>All domains this bench-results file has scores for.</summary>
    public IReadOnlyCollection<string> Domains => (IReadOnlyCollection<string>)_byDomain.Keys;

    public bool HasDomain(string domain) => _byDomain.ContainsKey(domain);

    /// <summary>Quality for <paramref name="candidateKey"/> ("provider/model_id") within <paramref name="domain"/>, or null if absent.</summary>
    public double? Quality(string domain, string candidateKey)
    {
        if (!_byDomain.TryGetValue(domain, out var scores))
        {
            return null;
        }

        return scores.TryGetValue(candidateKey, out var q) ? q : null;
    }

    private sealed class BenchResultsFileDto
    {
        [JsonPropertyName("domains")] public Dictionary<string, Dictionary<string, double>>? Domains { get; init; }
    }
}
