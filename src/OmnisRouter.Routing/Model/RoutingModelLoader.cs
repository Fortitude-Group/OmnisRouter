using System.Text.Json;
using OmnisRouter.Core.Model;

namespace OmnisRouter.Routing.Model;

/// <summary>
/// Loads a matched <c>centroids-&lt;ver&gt;.bin</c> + <c>policy-&lt;ver&gt;.json</c> pair from the routing
/// directory (format: routing/FORMAT.md). Validates dimensionality, cluster coverage, and that every
/// referenced candidate exists in the candidate pool. Deterministic: same files → same model.
/// </summary>
public static class RoutingModelLoader
{
    private const string Magic = "OMRC";
    private const int SupportedFormatVersion = 1;

    /// <summary>Load the model with version <paramref name="version"/> (e.g. "seed" or "2026-08-15.3").</summary>
    public static RoutingModel Load(string routingDirectory, string version, int expectedDim, IReadOnlyCollection<ModelRef> candidatePool)
    {
        var binPath = Path.Combine(routingDirectory, $"centroids-{version}.bin");
        var jsonPath = Path.Combine(routingDirectory, $"policy-{version}.json");

        var (k, dim, centroids) = ReadCentroids(binPath);
        var policy = ReadPolicy(jsonPath);

        if (dim != expectedDim)
        {
            throw new InvalidDataException($"Routing model dim {dim} != embedder dim {expectedDim} ({binPath}).");
        }

        if (policy.K != k || policy.Dim != dim)
        {
            throw new InvalidDataException($"policy-{version}.json (k={policy.K}, dim={policy.Dim}) does not match centroids-{version}.bin (k={k}, dim={dim}).");
        }

        var clusters = BuildClusters(policy, k, candidatePool, version);

        return new RoutingModel
        {
            PolicyVersion = policy.PolicyVersion,
            K = k,
            Dim = dim,
            Centroids = centroids,
            Clusters = clusters,
        };
    }

    private static (int K, int Dim, float[][] Centroids) ReadCentroids(string binPath)
    {
        using var reader = new BinaryReader(File.OpenRead(binPath));
        var magic = new string(reader.ReadChars(4));
        if (magic != Magic)
        {
            throw new InvalidDataException($"Bad magic '{magic}' in {binPath}; expected '{Magic}'.");
        }

        var formatVersion = reader.ReadInt32();
        if (formatVersion != SupportedFormatVersion)
        {
            throw new InvalidDataException($"Unsupported routing-model format version {formatVersion} in {binPath}.");
        }

        var k = reader.ReadInt32();
        var dim = reader.ReadInt32();
        if (k <= 0 || dim <= 0)
        {
            throw new InvalidDataException($"Invalid k={k}/dim={dim} in {binPath}.");
        }

        var centroids = new float[k][];
        for (var c = 0; c < k; c++)
        {
            var row = new float[dim];
            for (var i = 0; i < dim; i++)
            {
                row[i] = reader.ReadSingle();
            }

            centroids[c] = row;
        }

        return (k, dim, centroids);
    }

    private static readonly JsonSerializerOptions PolicyJsonOptions = new() { PropertyNameCaseInsensitive = true };

    private static PolicyFile ReadPolicy(string jsonPath)
    {
        var json = File.ReadAllText(jsonPath);
        return JsonSerializer.Deserialize<PolicyFile>(json, PolicyJsonOptions)
               ?? throw new InvalidDataException($"Empty policy file {jsonPath}.");
    }

    private static IReadOnlyList<ClusterPolicy> BuildClusters(
        PolicyFile policy, int k, IReadOnlyCollection<ModelRef> candidatePool, string version)
    {
        var poolKeys = candidatePool
            .Select(m => (m.Provider, m.ModelId))
            .ToHashSet();

        var byId = new Dictionary<int, ClusterPolicy>(policy.Clusters.Count);
        foreach (var cluster in policy.Clusters)
        {
            var candidates = new List<PolicyCandidate>(cluster.Candidates.Count);
            foreach (var candidate in cluster.Candidates.OrderBy(c => c.RankByCost))
            {
                if (!Enum.TryParse<Provider>(candidate.Provider, ignoreCase: true, out var provider))
                {
                    throw new InvalidDataException($"policy-{version}.json cluster {cluster.ClusterId}: unknown provider '{candidate.Provider}'.");
                }

                if (!poolKeys.Contains((provider, candidate.ModelId)))
                {
                    throw new InvalidDataException(
                        $"policy-{version}.json cluster {cluster.ClusterId}: candidate {provider}/{candidate.ModelId} is not in the candidate pool (config/models.yaml).");
                }

                candidates.Add(new PolicyCandidate(provider, candidate.ModelId, candidate.PredictedQuality, candidate.RankByCost));
            }

            byId[cluster.ClusterId] = new ClusterPolicy(cluster.ClusterId, candidates);
        }

        var ordered = new ClusterPolicy[k];
        for (var id = 0; id < k; id++)
        {
            ordered[id] = byId.TryGetValue(id, out var cp)
                ? cp
                : throw new InvalidDataException($"policy-{version}.json is missing cluster_id {id} (k={k}).");
        }

        return ordered;
    }
}
