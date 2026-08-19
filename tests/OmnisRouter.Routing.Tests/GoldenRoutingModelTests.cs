using OmnisRouter.Core.Abstractions;
using OmnisRouter.Core.Model;
using OmnisRouter.Core.Routing;
using OmnisRouter.Routing.Model;
using OmnisRouter.Routing.Tests.Stubs;

namespace OmnisRouter.Routing.Tests;

/// <summary>
/// T063: loads the committed, reproducibly-built <c>v1-2026-08-19</c> routing model (T065/T066,
/// routing/centroids-v1-2026-08-19.bin + routing/policy-v1-2026-08-19.json) via the same
/// <see cref="RoutingModelLoader"/> the app uses, and asserts that <see cref="ClusterScorerPolicy"/>
/// decisions for a handful of FIXED prompts (via the deterministic embedder stub, not the real ONNX
/// embedder) are stable. If a future rebuild of the model silently changes routing behavior for these
/// prompts, this test breaks -- that's the point (guards against silent routing drift).
/// </summary>
public class GoldenRoutingModelTests
{
    private const string Version = "v1-2026-08-19";

    /// <summary>Deterministic placeholder pricing -- only the persisted policy-table order (already
    /// cost-ranked at build time) drives which candidate is "cheapest capable"; the dollar figures
    /// here never affect that choice, only the receipt's cost fields.</summary>
    private sealed class FixedPricingBook : IPricingBook
    {
        public string SnapshotDate => "2026-08-15";
        public decimal EstimateUsd(ModelRef model, int inputTokens, int outputTokens) => 0.001m;
    }

    // `OmnisRouter.Routing.Model.RoutingModel` fully qualified throughout this file rather than relying
    // on `using OmnisRouter.Routing.Model;`: this test assembly also references the
    // `OmnisRouter.RoutingModel.Build` project (BuildReproducibilityTests.cs), whose same-named
    // `OmnisRouter.RoutingModel` namespace is otherwise reachable unqualified here too, since it nests
    // directly under this file's enclosing `OmnisRouter` namespace.
    private static (OmnisRouter.Routing.Model.RoutingModel Model, IReadOnlyList<ModelRef> Pool) LoadShippedModel()
    {
        var routingDir = RepoLocator.Resolve("routing");
        var modelsPath = RepoLocator.Resolve("config/models.yaml");
        var catalog = ModelCatalog.LoadFromFile(modelsPath);
        var model = RoutingModelLoader.Load(routingDir, Version, 384, catalog.Models);
        return (model, catalog.Models);
    }

    /// <summary>
    /// Confidence floor forced to 0 so decisions exercise the real per-cluster ranked candidate list
    /// (cheapest-capable selection) instead of always escalating to the strong default -- DeterministicEmbedder
    /// is a non-semantic stub, so confidences against real (semantically-fit) centroids would be
    /// unreliable, but the point of this test is to freeze *which candidate the shipped policy table
    /// ranks first per cluster*, which is independent of the confidence gate.
    /// </summary>
    private static ClusterScorerPolicy BuildPolicy(OmnisRouter.Routing.Model.RoutingModel model)
        => new(new DeterministicEmbedder(), model, new FixedPricingBook(), new ClusterScorerOptions { ConfidenceFloor = 0.0 });

    private static ChatRequest RequestOf(string text) => new()
    {
        OriginFormat = ClientFormat.OpenAI,
        Messages = [new Message(Role.User, [new TextPart(text)])],
    };

    private static RoutingContext ContextOf(IReadOnlyList<ModelRef> pool)
    {
        var strongDefault = pool.First(m => m.Provider == Provider.Anthropic && m.ModelId == "claude-opus-4-8");
        return new RoutingContext { CandidatePool = pool, StrongDefault = strongDefault };
    }

    [Fact]
    public void Shipped_model_loads_cleanly_via_the_loader()
    {
        var (model, _) = LoadShippedModel();

        Assert.Equal("v1-2026-08-19", model.PolicyVersion);
        Assert.Equal(8, model.K);
        Assert.Equal(384, model.Dim);
        Assert.Equal(8, model.Clusters.Count);
    }

    public static IEnumerable<object[]> FixedPrompts()
    {
        yield return ["Write a Python function to reverse a linked list.", 2, "openai", "gpt-5", RoutingDecisionKind.Routed, RoutingReason.CheapestCapable];
        yield return ["What's a good recipe for a weeknight vegetarian dinner?", 2, "openai", "gpt-5", RoutingDecisionKind.Routed, RoutingReason.CheapestCapable];
        yield return ["Implement quicksort in C++.", 5, "openai", "gpt-5", RoutingDecisionKind.Routed, RoutingReason.CheapestCapable];
    }

    [Theory]
    [MemberData(nameof(FixedPrompts))]
    public void Decision_for_fixed_prompt_is_stable(
        string prompt, int expectedClusterId, string expectedProvider, string expectedModelId,
        RoutingDecisionKind expectedKind, RoutingReason expectedReason)
    {
        var (model, pool) = LoadShippedModel();
        var policy = BuildPolicy(model);

        var decision = policy.Decide(RequestOf(prompt), ContextOf(pool));

        Assert.Equal(expectedClusterId, decision.ClusterId);
        Assert.Equal(expectedProvider, decision.Chosen.Provider.ToString().ToLowerInvariant());
        Assert.Equal(expectedModelId, decision.Chosen.ModelId);
        Assert.Equal(expectedKind, decision.Decision);
        Assert.Equal(expectedReason, decision.Reason);
        Assert.Equal("v1-2026-08-19", decision.PolicyVersion);
    }

    [Fact]
    public void Decision_is_deterministic_across_repeated_calls()
    {
        var (model, pool) = LoadShippedModel();
        var policy = BuildPolicy(model);
        var request = RequestOf("Write a Python function to reverse a linked list.");

        var first = policy.Decide(request, ContextOf(pool));
        var second = policy.Decide(request, ContextOf(pool));

        Assert.Equal(first.ClusterId, second.ClusterId);
        Assert.Equal(first.Chosen, second.Chosen);
        Assert.Equal(first.Confidence, second.Confidence);
        Assert.Equal(first.PolicyVersion, second.PolicyVersion);
    }
}
