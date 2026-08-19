using System.Text.Json.Serialization;

namespace OmnisRouter.Routing.Model;

/// <summary>DTO mirror of <c>policy-&lt;ver&gt;.json</c> (see routing/FORMAT.md).</summary>
public sealed class PolicyFile
{
    [JsonPropertyName("policy_version")] public string PolicyVersion { get; init; } = "";
    [JsonPropertyName("k")] public int K { get; init; }
    [JsonPropertyName("dim")] public int Dim { get; init; }
    [JsonPropertyName("clusters")] public List<PolicyClusterDto> Clusters { get; init; } = [];
}

public sealed class PolicyClusterDto
{
    [JsonPropertyName("cluster_id")] public int ClusterId { get; init; }
    [JsonPropertyName("candidates")] public List<PolicyCandidateDto> Candidates { get; init; } = [];
}

public sealed class PolicyCandidateDto
{
    [JsonPropertyName("provider")] public string Provider { get; init; } = "";
    [JsonPropertyName("model_id")] public string ModelId { get; init; } = "";
    [JsonPropertyName("predicted_quality")] public double PredictedQuality { get; init; }
    [JsonPropertyName("rank_by_cost")] public int RankByCost { get; init; }
}
