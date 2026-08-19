using OmnisRouter.Core.Abstractions;
using OmnisRouter.Core.Model;

namespace OmnisRouter.RoutingModel.Build.Build;

/// <summary>
/// Builds the per-cluster policy table (research.md R4): for each cluster, find its dominant labeled
/// domain (majority vote over the dataset prompts k-means assigned to it), look up that domain's
/// bench-results quality row, keep candidates within the relative quality band
/// <c>q &gt;= qmax*(1-epsilon)</c>, and rank survivors ascending by cost using the pinned pricing
/// snapshot. A cluster is marked <c>low_confidence</c> when it has fewer than <c>--min-samples</c>
/// dataset prompts, or when its dominant domain has no bench-results row (can't be scored) --
/// callers/operators should treat those rows cautiously (ClusterScorerPolicy already escalates to the
/// strong default whenever a cluster's candidate list ends up empty).
/// </summary>
internal static class PolicyTableBuilder
{
    public static PolicyBuildResult Build(
        KMeansResult kmeans,
        IReadOnlyList<PromptRecord> prompts,
        BenchResults benchResults,
        IReadOnlyList<ModelRef> candidatePool,
        IPricingBook pricing,
        int k,
        int dim,
        double epsilon,
        int minSamples,
        string policyVersion,
        int representativeInputTokens,
        int representativeOutputTokens)
    {
        var clusterPromptIndices = new List<int>[k];
        for (var c = 0; c < k; c++)
        {
            clusterPromptIndices[c] = [];
        }

        for (var i = 0; i < kmeans.Assignments.Length; i++)
        {
            clusterPromptIndices[kmeans.Assignments[i]].Add(i);
        }

        var clusters = new List<PolicyOutputClusterDto>(k);
        var lowConfidenceCount = 0;

        for (var c = 0; c < k; c++)
        {
            var indices = clusterPromptIndices[c];
            var sampleCount = indices.Count;
            var dominantDomain = DominantDomain(indices, prompts);
            var hasDomainScores = dominantDomain is not null && benchResults.HasDomain(dominantDomain);

            var candidates = hasDomainScores
                ? RankCandidates(dominantDomain!, benchResults, candidatePool, pricing, epsilon, representativeInputTokens, representativeOutputTokens)
                : [];

            var lowConfidence = sampleCount < minSamples || !hasDomainScores || candidates.Count == 0;
            if (lowConfidence)
            {
                lowConfidenceCount++;
            }

            clusters.Add(new PolicyOutputClusterDto
            {
                ClusterId = c,
                LowConfidence = lowConfidence,
                SampleCount = sampleCount,
                DominantDomain = dominantDomain,
                Candidates = candidates,
            });
        }

        var file = new PolicyOutputFileDto
        {
            PolicyVersion = policyVersion,
            K = k,
            Dim = dim,
            Clusters = clusters,
        };

        return new PolicyBuildResult(file, lowConfidenceCount);
    }

    /// <summary>Majority-vote domain label among a cluster's assigned prompts; ties broken alphabetically (deterministic).</summary>
    private static string? DominantDomain(List<int> indices, IReadOnlyList<PromptRecord> prompts)
    {
        if (indices.Count == 0)
        {
            return null;
        }

        var counts = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var i in indices)
        {
            var domain = prompts[i].Domain;
            counts[domain] = counts.GetValueOrDefault(domain) + 1;
        }

        string? best = null;
        var bestCount = -1;
        foreach (var (domain, count) in counts) // SortedDictionary enumerates keys ascending -> deterministic tie-break
        {
            if (count > bestCount)
            {
                bestCount = count;
                best = domain;
            }
        }

        return best;
    }

    private static List<PolicyOutputCandidateDto> RankCandidates(
        string domain,
        BenchResults benchResults,
        IReadOnlyList<ModelRef> pool,
        IPricingBook pricing,
        double epsilon,
        int inputTokens,
        int outputTokens)
    {
        var scored = new List<(ModelRef Model, double Quality, decimal Cost)>();
        foreach (var model in pool)
        {
            var key = $"{model.Provider.ToString().ToLowerInvariant()}/{model.ModelId}";
            var quality = benchResults.Quality(domain, key);
            if (quality is null)
            {
                continue; // no bench-results row for this candidate in this domain -- not scoreable, excluded
            }

            var cost = pricing.EstimateUsd(model, inputTokens, outputTokens);
            scored.Add((model, quality.Value, cost));
        }

        if (scored.Count == 0)
        {
            return [];
        }

        var qMax = scored.Max(s => s.Quality);
        var band = qMax * (1 - epsilon);

        var survivors = scored
            .Where(s => s.Quality >= band)
            .OrderBy(s => s.Cost)
            .ThenBy(s => s.Model.Provider)
            .ThenBy(s => s.Model.ModelId, StringComparer.Ordinal)
            .ToList();

        var result = new List<PolicyOutputCandidateDto>(survivors.Count);
        for (var i = 0; i < survivors.Count; i++)
        {
            var s = survivors[i];
            result.Add(new PolicyOutputCandidateDto
            {
                Provider = s.Model.Provider.ToString().ToLowerInvariant(),
                ModelId = s.Model.ModelId,
                PredictedQuality = s.Quality,
                RankByCost = i + 1,
            });
        }

        return result;
    }
}
