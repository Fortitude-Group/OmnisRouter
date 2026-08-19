using System.Text.Json.Serialization;

namespace OmnisRouter.RoutingModel.Build.Build;

/// <summary>
/// DTOs for the emitted <c>policy-&lt;ver&gt;.json</c> (routing/FORMAT.md). Field names/casing match
/// <c>OmnisRouter.Routing.Model.PolicyFile</c> exactly so <c>RoutingModelLoader</c> reads this file
/// with no special-casing; the three extra debug fields on each cluster (<c>low_confidence</c>,
/// <c>sample_count</c>, <c>dominant_domain</c>) are additional properties the loader's
/// case-insensitive <see cref="System.Text.Json.JsonSerializer"/> deserialization simply ignores.
/// </summary>
internal sealed class PolicyOutputCandidateDto
{
    [JsonPropertyName("provider")] public required string Provider { get; init; }
    [JsonPropertyName("model_id")] public required string ModelId { get; init; }
    [JsonPropertyName("predicted_quality")] public double PredictedQuality { get; init; }
    [JsonPropertyName("rank_by_cost")] public int RankByCost { get; init; }
}

internal sealed class PolicyOutputClusterDto
{
    [JsonPropertyName("cluster_id")] public int ClusterId { get; init; }

    /// <summary>True when this cluster had fewer than <c>--min-samples</c> dataset prompts, or no bench-results row for its dominant domain.</summary>
    [JsonPropertyName("low_confidence")] public bool LowConfidence { get; init; }

    [JsonPropertyName("sample_count")] public int SampleCount { get; init; }
    [JsonPropertyName("dominant_domain")] public string? DominantDomain { get; init; }
    [JsonPropertyName("candidates")] public required List<PolicyOutputCandidateDto> Candidates { get; init; }
}

internal sealed class PolicyOutputFileDto
{
    [JsonPropertyName("policy_version")] public required string PolicyVersion { get; init; }
    [JsonPropertyName("k")] public int K { get; init; }
    [JsonPropertyName("dim")] public int Dim { get; init; }
    [JsonPropertyName("clusters")] public required List<PolicyOutputClusterDto> Clusters { get; init; }
}

/// <summary>Result of <see cref="PolicyTableBuilder.Build"/>: the file to serialize plus a build-time summary.</summary>
internal sealed record PolicyBuildResult(PolicyOutputFileDto File, int LowConfidenceClusterCount);
