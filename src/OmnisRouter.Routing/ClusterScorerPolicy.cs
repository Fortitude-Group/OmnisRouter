using OmnisRouter.Core.Abstractions;
using OmnisRouter.Core.Model;
using OmnisRouter.Core.Routing;
using OmnisRouter.Routing.Model;

namespace OmnisRouter.Routing;

/// <summary>Tunable knobs for <see cref="ClusterScorerPolicy"/> (calibrated offline — see research.md R4).</summary>
public sealed record ClusterScorerOptions
{
    /// <summary>Temperature for the softmax over negative cosine distances.</summary>
    public double Temperature { get; init; } = 0.15;

    /// <summary>Escalate to the strong default when top-1 confidence falls below this floor.</summary>
    public double ConfidenceFloor { get; init; } = 0.55;

    /// <summary>Output-token estimate used for cost math when the request sets no max.</summary>
    public int DefaultOutputTokensEstimate { get; init; } = 512;
}

/// <summary>
/// v1 routing policy: embed the prompt → nearest centroid (cosine) → temperature-scaled softmax
/// confidence → cheapest-capable candidate from the cluster's policy row, escalating to the strong
/// default when confidence is below the floor. Every decision is stamped with the routing model's
/// <c>policy_version</c> and carries a full receipt (research.md R4, contracts/routing-receipt.schema.json).
/// </summary>
public sealed class ClusterScorerPolicy(IEmbedder embedder, RoutingModel model, IPricingBook pricing, ClusterScorerOptions options)
    : IRoutingPolicy
{
    public string Name => "cluster-scorer";

    public ModelDecision Decide(ChatRequest request, RoutingContext context)
    {
        var text = ExtractRoutingText(request);
        var vector = embedder.Embed(text);

        var (top1, top1Sim, top2Sim) = NearestCentroids(vector);
        var confidence = SoftmaxTop1Confidence(vector, top1);

        var estInputTokens = EstimateInputTokens(text);
        var estOutputTokens = request.MaxTokens ?? options.DefaultOutputTokensEstimate;

        var cluster = model.Clusters[top1];
        var poolByKey = context.CandidatePool.ToDictionary(m => (m.Provider, m.ModelId));

        // Rank the alternatives (cheapest-first) that the operator actually has in the pool.
        var alternatives = new List<Alternative>();
        ModelRef? strongest = null;
        double strongestQuality = double.NegativeInfinity;
        foreach (var candidate in cluster.Candidates)
        {
            if (!poolByKey.TryGetValue((candidate.Provider, candidate.ModelId), out var modelRef))
            {
                continue;
            }

            var cost = pricing.EstimateUsd(modelRef, estInputTokens, estOutputTokens);
            alternatives.Add(new Alternative(modelRef, candidate.PredictedQuality, cost, 0m));
            if (candidate.PredictedQuality > strongestQuality)
            {
                strongestQuality = candidate.PredictedQuality;
                strongest = modelRef;
            }
        }

        var belowFloor = confidence < options.ConfidenceFloor;
        var noCandidate = alternatives.Count == 0;

        ModelRef chosen;
        RoutingDecisionKind decisionKind;
        RoutingReason reason;

        if (belowFloor || noCandidate)
        {
            chosen = context.StrongDefault;
            decisionKind = RoutingDecisionKind.Escalated;
            reason = noCandidate ? RoutingReason.LowConfidenceCluster : RoutingReason.ConfidenceBelowFloor;
        }
        else
        {
            chosen = alternatives[0].Model; // cheapest-capable (candidates are cost-ranked)
            decisionKind = RoutingDecisionKind.Routed;
            reason = RoutingReason.CheapestCapable;
        }

        var chosenCost = pricing.EstimateUsd(chosen, estInputTokens, estOutputTokens);
        var strongestCost = strongest is null ? chosenCost : pricing.EstimateUsd(strongest, estInputTokens, estOutputTokens);

        // Fill in each alternative's delta versus the chosen model.
        var withDeltas = alternatives
            .Select(a => a with { EstCostDeltaUsd = a.EstCostUsd - chosenCost })
            .ToList();

        return new ModelDecision
        {
            Chosen = chosen,
            PolicyVersion = model.PolicyVersion,
            ClusterId = top1,
            Confidence = confidence,
            ConfidenceFloor = options.ConfidenceFloor,
            Top1CosineSim = top1Sim,
            Top2CosineSim = top2Sim,
            Decision = decisionKind,
            Reason = reason,
            Alternatives = withDeltas,
            EstCostUsd = chosenCost,
            EstCostDeltaVsBigUsd = chosenCost - strongestCost,
            SessionPinApplied = false,
            PricingSnapshotDate = pricing.SnapshotDate,
        };
    }

    /// <summary>Concatenate the intent-bearing text (system + user turns) for embedding.</summary>
    private static string ExtractRoutingText(ChatRequest request)
    {
        var parts = new List<string>();
        foreach (var s in request.System)
        {
            parts.Add(s.Text);
        }

        foreach (var message in request.Messages)
        {
            if (message.Role is Role.User or Role.System)
            {
                foreach (var part in message.Parts)
                {
                    if (part is TextPart text)
                    {
                        parts.Add(text.Text);
                    }
                }
            }
        }

        return string.Join('\n', parts);
    }

    private (int Top1, double Top1Sim, double Top2Sim) NearestCentroids(float[] vector)
    {
        var best = -1;
        double bestSim = double.NegativeInfinity;
        double secondSim = double.NegativeInfinity;

        for (var c = 0; c < model.K; c++)
        {
            var sim = Dot(vector, model.Centroids[c]);
            if (sim > bestSim)
            {
                secondSim = bestSim;
                bestSim = sim;
                best = c;
            }
            else if (sim > secondSim)
            {
                secondSim = sim;
            }
        }

        if (double.IsNegativeInfinity(secondSim))
        {
            secondSim = bestSim;
        }

        return (best, bestSim, secondSim);
    }

    private double SoftmaxTop1Confidence(float[] vector, int top1)
    {
        // p_i = exp(-d_i / T) / Σ exp(-d_j / T), with d_i = 1 - cosine_sim_i. Confidence = p_top1.
        double sumExp = 0;
        double topExp = 0;
        for (var c = 0; c < model.K; c++)
        {
            var distance = 1.0 - Dot(vector, model.Centroids[c]);
            var e = Math.Exp(-distance / options.Temperature);
            sumExp += e;
            if (c == top1)
            {
                topExp = e;
            }
        }

        return sumExp <= 0 ? 0 : topExp / sumExp;
    }

    private static double Dot(float[] a, float[] b)
    {
        double sum = 0;
        for (var i = 0; i < a.Length; i++)
        {
            sum += a[i] * b[i];
        }

        return sum;
    }

    private static int EstimateInputTokens(string text) => Math.Max(1, text.Length / 4);
}
