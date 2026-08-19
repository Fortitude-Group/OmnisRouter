using System.Diagnostics;
using OmnisRouter.Core.Abstractions;
using OmnisRouter.Core.Model;
using OmnisRouter.Routing;
using OmnisRouter.Routing.Embedding;
using OmnisRouter.Routing.Model;

namespace OmnisRouter.Routing.Tests;

/// <summary>
/// SC-003: routing overhead must stay well within the 50ms p95 budget. This measures the in-process
/// decision path (embed → nearest centroid → policy lookup) — the part OmnisRouter adds on top of the
/// upstream call. The pinned ONNX embedder adds a few ms on top in production (research.md R1); this
/// bounds the routing logic itself, which must be a small fraction of the budget.
/// </summary>
public class LatencyBenchmark
{
    private const double BudgetMs = 50.0;

    private static readonly IReadOnlyList<ModelRef> Pool =
    [
        new(Provider.OpenAI, "gpt-5-mini") { Capabilities = ModelCapabilities.Tools },
        new(Provider.Anthropic, "claude-opus-4-8") { Capabilities = ModelCapabilities.Tools },
    ];

    private sealed class FlatPricing : IPricingBook
    {
        public string SnapshotDate => "2026-08-15";
        public decimal EstimateUsd(ModelRef model, int inputTokens, int outputTokens) =>
            model.ModelId == "gpt-5-mini" ? 0.001m : 0.05m;
    }

    [Fact]
    public void Routing_overhead_is_well_within_the_50ms_budget()
    {
        var embedder = new HashingEmbedder();
        var k = 8;
        var rng = new Random(12345);
        var centroids = new float[k][];
        for (var c = 0; c < k; c++)
        {
            centroids[c] = embedder.Embed($"anchor prompt for cluster {c} with some varied words {rng.Next()}");
        }

        var candidates = new List<PolicyCandidate>
        {
            new(Provider.OpenAI, "gpt-5-mini", 0.7, 1),
            new(Provider.Anthropic, "claude-opus-4-8", 0.95, 2),
        };
        var clusters = Enumerable.Range(0, k).Select(i => new ClusterPolicy(i, candidates)).ToList();

        var model = new OmnisRouter.Routing.Model.RoutingModel
        {
            PolicyVersion = "bench",
            K = k,
            Dim = embedder.Dimension,
            Centroids = centroids,
            Clusters = clusters,
        };

        var policy = new ClusterScorerPolicy(embedder, model, new FlatPricing(), new ClusterScorerOptions());
        var context = new RoutingContext { CandidatePool = Pool, StrongDefault = Pool[^1] };

        var prompts = Enumerable.Range(0, 200)
            .Select(i => $"Please help me with task number {i}: write, analyze, or explain something moderately long about topic {i % 37}.")
            .Select(t => new ChatRequest { OriginFormat = ClientFormat.OpenAI, Messages = [new Message(Role.User, [new TextPart(t)])] })
            .ToArray();

        // Warm up (JIT).
        foreach (var r in prompts.Take(20))
        {
            policy.Decide(r, context);
        }

        var samples = new List<double>(prompts.Length);
        foreach (var r in prompts)
        {
            var sw = Stopwatch.StartNew();
            policy.Decide(r, context);
            sw.Stop();
            samples.Add(sw.Elapsed.TotalMilliseconds);
        }

        samples.Sort();
        var p95 = samples[(int)(samples.Count * 0.95)];
        Assert.True(p95 < BudgetMs, $"routing-decision p95 was {p95:F3}ms, over the {BudgetMs}ms budget");
    }
}
