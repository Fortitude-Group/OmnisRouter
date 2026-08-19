using System.Text.Json;
using OmnisRouter.Core.Abstractions;
using OmnisRouter.Routing.Embedding;
using OmnisRouter.Routing.Model;
using OmnisRouter.Store.Pricing;

namespace OmnisRouter.RoutingModel.Build.Build;

/// <summary>Summary of one <see cref="RoutingModelBuilder.Run"/> call, for CLI reporting and tests.</summary>
public sealed record RoutingModelBuildResult(
    string Version,
    string OutDirectory,
    string CentroidsPath,
    string PolicyPath,
    int K,
    int Dim,
    int PromptCount,
    int LowConfidenceClusterCount);

/// <summary>
/// Orchestrates the reproducible offline routing-model build (T065/T066): load the labeled dataset,
/// embed every prompt, cluster deterministically, derive the per-cluster policy table from the
/// bench-results + pinned pricing snapshot, and emit the matched <c>centroids-&lt;ver&gt;.bin</c> /
/// <c>policy-&lt;ver&gt;.json</c> pair (routing/FORMAT.md). Given the same inputs and the same
/// <c>--version</c>, two calls to <see cref="Run"/> always produce byte-identical artifacts (T064) --
/// every step (embedding, k-means, quality-band filtering, cost ranking) is deterministic.
/// </summary>
public static class RoutingModelBuilder
{
    public static RoutingModelBuildResult Run(RoutingModelBuildOptions options)
    {
        var prompts = PromptDataset.Load(options.DatasetPath);
        var benchResults = BenchResults.Load(options.BenchResultsPath);
        var catalog = ModelCatalog.LoadFromFile(options.ModelsConfigPath);

        var pricing = new PricingBook(new PricingBookOptions
        {
            PricingDirectory = options.PricingDirectory,
            SnapshotDate = options.PricingSnapshotDate,
        });

        // HashingEmbedder is the same deterministic, dependency-free fallback used in dev/CI (see
        // OmnisRouter.Routing.ServiceCollectionExtensions.AddOmnisRouting). Production would pin the
        // ONNX bge-small-en-v1.5 embedder (OnnxEmbedder, research.md R1) instead -- the k-means +
        // policy-table pipeline below is unchanged either way; only the embedding vectors would differ.
        IEmbedder embedder = new HashingEmbedder(dimension: 384);

        var vectors = new float[prompts.Count][];
        for (var i = 0; i < prompts.Count; i++)
        {
            vectors[i] = embedder.Embed(prompts[i].Text);
        }

        var kmeans = SphericalKMeans.Fit(vectors, options.K, embedder.Dimension, options.RandomSeed);

        var policyResult = PolicyTableBuilder.Build(
            kmeans,
            prompts,
            benchResults,
            catalog.Models,
            pricing,
            options.K,
            embedder.Dimension,
            options.Epsilon,
            options.MinSamples,
            options.Version,
            options.RepresentativeInputTokens,
            options.RepresentativeOutputTokens);

        Directory.CreateDirectory(options.OutDirectory);
        var centroidsPath = Path.Combine(options.OutDirectory, $"centroids-{options.Version}.bin");
        var policyPath = Path.Combine(options.OutDirectory, $"policy-{options.Version}.json");

        CentroidsBinaryWriter.Write(centroidsPath, kmeans.Centroids, options.K, embedder.Dimension);

        var json = JsonSerializer.Serialize(policyResult.File, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(policyPath, json);

        return new RoutingModelBuildResult(
            options.Version,
            options.OutDirectory,
            centroidsPath,
            policyPath,
            options.K,
            embedder.Dimension,
            prompts.Count,
            policyResult.LowConfidenceClusterCount);
    }
}
