using OmnisRouter.Core.Abstractions;
using OmnisRouter.Core.Model;
using OmnisRouter.Core.Routing;
using OmnisRouter.Routing.Model;
using OmnisRouter.Routing.Tests.Stubs;

namespace OmnisRouter.Routing.Tests;

/// <summary>
/// T063: loads the committed, reproducibly-built shipped routing model via the same
/// <see cref="RoutingModelLoader"/> the app uses, and asserts that <see cref="ClusterScorerPolicy"/>
/// decisions are LOADABLE, VALID, and DETERMINISTIC. Guards against silent routing drift: an
/// incompatible model (e.g. referencing a candidate no longer in the pool) fails to load, and any
/// non-determinism in the decision path fails here. Specific cluster ids are intentionally NOT pinned
/// — they are meaningless under the non-semantic <see cref="DeterministicEmbedder"/> stub and change
/// legitimately on every model rebuild.
/// </summary>
public class GoldenRoutingModelTests
{
    private const string Version = "v3-omnisbench-2026-08-20";

    private sealed class FixedPricingBook : IPricingBook
    {
        public string SnapshotDate => "2026-08-15";
        public decimal EstimateUsd(ModelRef model, int inputTokens, int outputTokens) => 0.001m;
    }

    private static (OmnisRouter.Routing.Model.RoutingModel Model, IReadOnlyList<ModelRef> Pool) LoadShippedModel()
    {
        var routingDir = RepoLocator.Resolve("routing");
        var catalog = ModelCatalog.LoadFromFile(RepoLocator.Resolve("config/models.yaml"));
        var model = RoutingModelLoader.Load(routingDir, Version, 384, catalog.Models);
        return (model, catalog.Models);
    }

    // Floor forced to 0 so decisions exercise the per-cluster ranked list (cheapest-capable) rather
    // than always escalating; the stub embedder is non-semantic so confidence isn't meaningful here.
    private static ClusterScorerPolicy BuildPolicy(OmnisRouter.Routing.Model.RoutingModel model)
        => new(new DeterministicEmbedder(), model, new FixedPricingBook(), new ClusterScorerOptions { ConfidenceFloor = 0.0 });

    private static ChatRequest RequestOf(string text) => new()
    {
        OriginFormat = ClientFormat.OpenAI,
        Messages = [new Message(Role.User, [new TextPart(text)])],
    };

    private static RoutingContext ContextOf(IReadOnlyList<ModelRef> pool) => new()
    {
        CandidatePool = pool,
        StrongDefault = pool.First(m => m.Provider == Provider.Anthropic && m.ModelId == "claude-opus-5"),
    };

    [Fact]
    public void Shipped_model_loads_cleanly_via_the_loader()
    {
        var (model, _) = LoadShippedModel();

        Assert.Equal(Version, model.PolicyVersion);
        Assert.Equal(8, model.K);
        Assert.Equal(384, model.Dim);
        Assert.Equal(8, model.Clusters.Count);
    }

    [Theory]
    [InlineData("Write a Python function to reverse a linked list.")]
    [InlineData("What's a good recipe for a weeknight vegetarian dinner?")]
    [InlineData("Implement quicksort in C++.")]
    public void Decision_for_fixed_prompt_is_valid_and_stable(string prompt)
    {
        var (model, pool) = LoadShippedModel();
        var policy = BuildPolicy(model);
        var request = RequestOf(prompt);

        var first = policy.Decide(request, ContextOf(pool));
        var second = policy.Decide(request, ContextOf(pool));

        // Valid: a real decision stamped with the shipped model, choosing a model in the pool.
        Assert.Equal(Version, first.PolicyVersion);
        Assert.InRange(first.ClusterId, 0, model.K - 1);
        Assert.Contains(first.Chosen, pool);

        // Deterministic: identical across repeated calls (guards against silent drift).
        Assert.Equal(first.ClusterId, second.ClusterId);
        Assert.Equal(first.Chosen, second.Chosen);
        Assert.Equal(first.Decision, second.Decision);
        Assert.Equal(first.Confidence, second.Confidence);
    }
}
