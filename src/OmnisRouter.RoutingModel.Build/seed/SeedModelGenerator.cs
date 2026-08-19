using System.Text;
using System.Text.Json;

namespace OmnisRouter.RoutingModel.Build.Seed;

/// <summary>
/// Produces the seed routing-model artifacts (<c>routing/centroids-seed.bin</c> +
/// <c>routing/policy-seed.json</c>) described in <c>routing/FORMAT.md</c>. This is NOT the
/// reproducible offline build (T065/T066) — it is a small, deterministic placeholder pair so
/// US1 can route end-to-end (via the loader in T025) before that pipeline exists. The seed is
/// superseded by the real trained model in Phase 7.
/// </summary>
public static class SeedModelGenerator
{
    /// <summary>Stamped into every routing decision as <c>policy_version</c> while the seed is active.</summary>
    public const string PolicyVersion = "seed-2026-08-19";

    /// <summary>Cluster count (k). Deliberately tiny for the seed — the real build tunes this.</summary>
    public const int ClusterCount = 4;

    /// <summary>Embedding dimensionality. Must match the pinned embedder (bge-small-en-v1.5).</summary>
    public const int Dimension = 384;

    /// <summary>Fixed seed for the pseudo-random centroids — never use unseeded <see cref="Random"/> here.</summary>
    private const int RandomSeed = 20260819;

    private const string BinaryMagic = "OMRC"; // "OmnisRouter Centroids"
    private const int BinaryFormatVersion = 1;

    /// <summary>
    /// The seed candidate pool, mirrored from <c>config/models.yaml</c> capabilities and the
    /// <c>config/pricing/2026-08-15.yaml</c> snapshot. <see cref="SeedCandidate.PredictedQuality"/>
    /// values are placeholder estimates (not benchmark-derived) pending OmnisBench (T065/T066).
    /// </summary>
    private static readonly IReadOnlyList<SeedCandidate> CandidatePool =
    [
        new("openrouter", "meta-llama/llama-3.3-70b-instruct", PredictedQuality: 0.55, InputPer1k: 0.00012, OutputPer1k: 0.0003),
        new("openai", "gpt-5-mini", PredictedQuality: 0.65, InputPer1k: 0.00025, OutputPer1k: 0.002),
        new("gemini", "gemini-2.5-flash", PredictedQuality: 0.70, InputPer1k: 0.0003, OutputPer1k: 0.0025),
        new("anthropic", "claude-haiku-4-5", PredictedQuality: 0.72, InputPer1k: 0.001, OutputPer1k: 0.005),
        new("openai", "gpt-5", PredictedQuality: 0.90, InputPer1k: 0.00125, OutputPer1k: 0.01),
        new("anthropic", "claude-opus-4-8", PredictedQuality: 0.95, InputPer1k: 0.015, OutputPer1k: 0.075),
    ];

    /// <summary>Writes both seed artifacts into <paramref name="routingDir"/> (creating it if needed).</summary>
    public static void Generate(string routingDir)
    {
        Directory.CreateDirectory(routingDir);

        var centroids = GenerateCentroids();
        WriteCentroidsBinary(Path.Combine(routingDir, "centroids-seed.bin"), centroids);
        WritePolicyJson(Path.Combine(routingDir, "policy-seed.json"));
    }

    private static float[][] GenerateCentroids()
    {
        var rng = new Random(RandomSeed);
        var centroids = new float[ClusterCount][];

        for (var c = 0; c < ClusterCount; c++)
        {
            var vector = new float[Dimension];
            for (var i = 0; i < Dimension; i++)
            {
                // NextDouble() is in [0, 1); shift to [-1, 1) so the raw vector isn't all-positive.
                vector[i] = (float)((rng.NextDouble() * 2.0) - 1.0);
            }

            Normalize(vector);
            centroids[c] = vector;
        }

        return centroids;
    }

    private static void Normalize(float[] vector)
    {
        var sumSquares = 0.0;
        foreach (var v in vector)
        {
            sumSquares += (double)v * v;
        }

        var norm = Math.Sqrt(sumSquares);
        if (norm <= 0)
        {
            return;
        }

        for (var i = 0; i < vector.Length; i++)
        {
            vector[i] = (float)(vector[i] / norm);
        }
    }

    /// <summary>
    /// Binary layout (all little-endian, see routing/FORMAT.md):
    /// bytes 0-3 magic "OMRC" (ASCII, not null-terminated), bytes 4-7 int32 format version,
    /// bytes 8-11 int32 k, bytes 12-15 int32 dim, then k*dim float32 centroid components,
    /// row-major (centroid 0's dim floats, then centroid 1's, ...).
    /// </summary>
    private static void WriteCentroidsBinary(string path, float[][] centroids)
    {
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var writer = new BinaryWriter(stream);

        writer.Write(Encoding.ASCII.GetBytes(BinaryMagic));
        writer.Write(BinaryFormatVersion);
        writer.Write(ClusterCount);
        writer.Write(Dimension);

        foreach (var centroid in centroids)
        {
            foreach (var component in centroid)
            {
                writer.Write(component);
            }
        }
    }

    private static void WritePolicyJson(string path)
    {
        var rankedByCost = CandidatePool.OrderBy(c => c.TotalPer1k).ToList();
        var candidateJson = rankedByCost
            .Select((c, index) => new
            {
                provider = c.Provider,
                model_id = c.ModelId,
                predicted_quality = c.PredictedQuality,
                rank_by_cost = index + 1,
            })
            .ToList();

        // The seed has no real per-cluster specialization yet (that comes from the offline build
        // in T065/T066) — every cluster gets the same cheapest-capable-first candidate list.
        var clusters = Enumerable.Range(0, ClusterCount)
            .Select(clusterId => new
            {
                cluster_id = clusterId,
                candidates = candidateJson,
            })
            .ToList();

        var policy = new
        {
            policy_version = PolicyVersion,
            k = ClusterCount,
            dim = Dimension,
            clusters,
        };

        var json = JsonSerializer.Serialize(policy, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json);
    }
}
