using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OmnisRouter.Core.Abstractions;
using OmnisRouter.Core.Model;
using OmnisRouter.Routing.Embedding;
using OmnisRouter.Routing.Model;

namespace OmnisRouter.Routing;

/// <summary>Default candidate pool + escalation target the endpoints use to build a RoutingContext.</summary>
public sealed record RoutingDefaults(IReadOnlyList<ModelRef> CandidatePool, ModelRef StrongDefault);

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the routing pipeline: model catalog, embedder (ONNX if the asset is present, else a
    /// deterministic hashing fallback for dev/CI), the loaded routing model, and the cluster-scorer policy.
    /// </summary>
    /// <param name="requireOnnxEmbedder">
    /// When true, a missing ONNX embedder asset is a fatal startup error instead of a silent
    /// HashingEmbedder fallback — the composition root passes <c>IsProduction()</c> here. The
    /// <c>Routing:Embedder:RequireOnnx</c> config key overrides this either way.
    /// </param>
    public static IServiceCollection AddOmnisRouting(
        this IServiceCollection services, IConfiguration configuration, bool requireOnnxEmbedder = false)
    {
        var modelsPath = RepoLocator.Resolve(configuration["Routing:ModelsConfigPath"] ?? "config/models.yaml");
        var routingDir = RepoLocator.Resolve(configuration["Routing:Directory"] ?? "routing");
        var modelVersion = configuration["Routing:ModelVersion"] ?? "v3-omnisbench-2026-08-20";
        var strongDefaultKey = configuration["Routing:StrongDefault"] ?? "anthropic/claude-opus-5";

        var catalog = ModelCatalog.LoadFromFile(modelsPath);
        services.AddSingleton(catalog);

        var options = new ClusterScorerOptions();
        configuration.GetSection("Routing:ClusterScorer").Bind(options);
        services.AddSingleton(options);

        // Embedder: real ONNX when the pinned asset is configured and present; otherwise a
        // deterministic hashing fallback so the pipeline runs in dev/CI without the model download.
        // Auto-detect the pinned ONNX asset under models/ (fetched by scripts/fetch-embedder.ps1 or
        // baked into the Docker image); config keys override the location.
        var onnxModel = configuration["Routing:Embedder:ModelPath"]
            ?? RepoLocator.Resolve(Path.Combine("models", "bge-small-en-v1.5", "model.onnx"));
        var onnxVocab = configuration["Routing:Embedder:VocabPath"]
            ?? RepoLocator.Resolve(Path.Combine("models", "bge-small-en-v1.5", "vocab.txt"));
        var dimension = configuration.GetValue<int?>("Routing:Embedder:Dimension") ?? 384;
        var onnxAvailable = !string.IsNullOrWhiteSpace(onnxModel) && !string.IsNullOrWhiteSpace(onnxVocab)
            && File.Exists(onnxModel) && File.Exists(onnxVocab);

        // Fail fast rather than route incorrectly. The routing centroids are built from the ONNX
        // embedder, so the HashingEmbedder lives in a different vector space: with it, every request
        // scores near-zero confidence and escalates to the strong default instead of routing. In
        // production a missing asset is therefore fatal, not a silent degrade. Override with the
        // Routing:Embedder:RequireOnnx config key (true forces it even in dev; false allows the fallback).
        var requireOnnx = configuration.GetValue<bool?>("Routing:Embedder:RequireOnnx") ?? requireOnnxEmbedder;
        if (requireOnnx && !onnxAvailable)
        {
            throw new InvalidOperationException(
                $"ONNX embedder asset not found (model '{onnxModel}', vocab '{onnxVocab}'). The routing " +
                "centroids are built from this model, so the HashingEmbedder fallback would route " +
                "incorrectly. Provide the asset (scripts/fetch-embedder.ps1; the Docker image ships it), " +
                "or set Routing:Embedder:RequireOnnx=false to explicitly allow the non-production fallback.");
        }

        services.AddSingleton<IEmbedder>(sp =>
        {
            if (onnxAvailable)
            {
                return new OnnxEmbedder(new OnnxEmbedderOptions
                {
                    ModelPath = onnxModel,
                    VocabPath = onnxVocab,
                    Dimension = dimension,
                });
            }

            sp.GetService<ILoggerFactory>()?.CreateLogger("OmnisRouter.Routing")
                .LogWarning("ONNX embedder asset not configured/found; using the non-production HashingEmbedder fallback.");
            return new HashingEmbedder(dimension);
        });

        // Load the routing model once, validating its candidates against the catalog pool.
        services.AddSingleton(sp =>
        {
            var embedder = sp.GetRequiredService<IEmbedder>();
            return RoutingModelLoader.Load(routingDir, modelVersion, embedder.Dimension, catalog.Models);
        });

        var strongDefault = ResolveStrongDefault(catalog, strongDefaultKey);
        services.AddSingleton(new RoutingDefaults(catalog.Models, strongDefault));

        services.AddSingleton<IRoutingPolicy, ClusterScorerPolicy>();
        return services;
    }

    private static ModelRef ResolveStrongDefault(ModelCatalog catalog, string key)
    {
        var slash = key.IndexOf('/');
        if (slash > 0
            && Enum.TryParse<Provider>(key[..slash], ignoreCase: true, out var provider))
        {
            var modelId = key[(slash + 1)..];
            var match = catalog.Models.FirstOrDefault(m => m.Provider == provider && m.ModelId == modelId);
            if (match is not null)
            {
                return match;
            }
        }

        // Fall back to the highest-capability model in the pool if the configured key is absent.
        return catalog.Models.LastOrDefault()
               ?? throw new InvalidOperationException("Candidate pool is empty; cannot resolve a strong default model.");
    }
}
