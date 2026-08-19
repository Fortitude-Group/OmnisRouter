using OmnisRouter.Core.Abstractions;
using OmnisRouter.Core.Model;
using OmnisRouter.Core.Routing;
using OmnisRouter.Routing;
using OmnisRouter.Routing.Model;
using OmnisRouter.Routing.Tests.Stubs;

namespace OmnisRouter.Routing.Tests;

public class ClusterScorerTests
{
    private static readonly ModelRef Cheap = new(Provider.OpenAI, "gpt-5-mini") { Capabilities = ModelCapabilities.Tools };
    private static readonly ModelRef Strong = new(Provider.Anthropic, "claude-opus-4-8") { Capabilities = ModelCapabilities.Tools };

    private static readonly IReadOnlyList<ModelRef> Pool = [Cheap, Strong];

    private sealed class FakePricingBook : IPricingBook
    {
        public string SnapshotDate => "2026-08-15";

        public decimal EstimateUsd(ModelRef model, int inputTokens, int outputTokens)
            => model.ModelId == "gpt-5-mini" ? 0.001m : 0.05m;
    }

    private static RoutingModel BuildModel(IEmbedder embedder, string clusterAText, string clusterBText)
    {
        // Centroids are the embedder's own vectors for two anchor texts, so those texts land in
        // their cluster with cosine ~1 (unit vectors → dot product).
        var centroids = new[] { embedder.Embed(clusterAText), embedder.Embed(clusterBText) };
        var candidates = new List<PolicyCandidate>
        {
            new(Provider.OpenAI, "gpt-5-mini", 0.7, 1),   // cheapest → rank 1
            new(Provider.Anthropic, "claude-opus-4-8", 0.95, 2),
        };

        return new RoutingModel
        {
            PolicyVersion = "test-v1",
            K = 2,
            Dim = embedder.Dimension,
            Centroids = centroids,
            Clusters =
            [
                new ClusterPolicy(0, candidates),
                new ClusterPolicy(1, candidates),
            ],
        };
    }

    private static ChatRequest RequestOf(string text) => new()
    {
        OriginFormat = ClientFormat.OpenAI,
        Messages = [new Message(Role.User, [new TextPart(text)])],
    };

    private static RoutingContext Context() => new() { CandidatePool = Pool, StrongDefault = Strong };

    [Fact]
    public void Routes_to_cheapest_capable_and_stamps_policy_version()
    {
        var embedder = new DeterministicEmbedder();
        var model = BuildModel(embedder, "write a python function", "what is the weather");
        var policy = new ClusterScorerPolicy(embedder, model, new FakePricingBook(), new ClusterScorerOptions { ConfidenceFloor = 0.0 });

        var decision = policy.Decide(RequestOf("write a python function"), Context());

        Assert.Equal(RoutingDecisionKind.Routed, decision.Decision);
        Assert.Equal(RoutingReason.CheapestCapable, decision.Reason);
        Assert.Equal(Cheap, decision.Chosen);
        Assert.Equal(0, decision.ClusterId);
        Assert.Equal("test-v1", decision.PolicyVersion);
        Assert.Equal("2026-08-15", decision.PricingSnapshotDate);
        Assert.NotEmpty(decision.Alternatives);
    }

    [Fact]
    public void Escalates_to_strong_default_below_confidence_floor()
    {
        var embedder = new DeterministicEmbedder();
        var model = BuildModel(embedder, "write a python function", "what is the weather");
        // Floor above any achievable confidence → always escalate.
        var policy = new ClusterScorerPolicy(embedder, model, new FakePricingBook(), new ClusterScorerOptions { ConfidenceFloor = 1.1 });

        var decision = policy.Decide(RequestOf("write a python function"), Context());

        Assert.Equal(RoutingDecisionKind.Escalated, decision.Decision);
        Assert.Equal(RoutingReason.ConfidenceBelowFloor, decision.Reason);
        Assert.Equal(Strong, decision.Chosen);
    }

    [Fact]
    public void Cost_delta_vs_big_is_negative_when_cheaper_model_chosen()
    {
        var embedder = new DeterministicEmbedder();
        var model = BuildModel(embedder, "write a python function", "what is the weather");
        var policy = new ClusterScorerPolicy(embedder, model, new FakePricingBook(), new ClusterScorerOptions { ConfidenceFloor = 0.0 });

        var decision = policy.Decide(RequestOf("write a python function"), Context());

        Assert.True(decision.EstCostDeltaVsBigUsd < 0, "choosing the cheap model should show savings vs the strongest candidate");
        Assert.True(decision.Confidence is >= 0 and <= 1);
    }

    [Fact]
    public void Decision_is_deterministic_for_same_input()
    {
        var embedder = new DeterministicEmbedder();
        var model = BuildModel(embedder, "write a python function", "what is the weather");
        var policy = new ClusterScorerPolicy(embedder, model, new FakePricingBook(), new ClusterScorerOptions { ConfidenceFloor = 0.0 });

        var d1 = policy.Decide(RequestOf("write a python function"), Context());
        var d2 = policy.Decide(RequestOf("write a python function"), Context());

        Assert.Equal(d1.ClusterId, d2.ClusterId);
        Assert.Equal(d1.Chosen, d2.Chosen);
        Assert.Equal(d1.Confidence, d2.Confidence);
    }
}
