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

    /// <summary>
    /// Escalate to the strong default when top-1 confidence falls below this floor. Calibrated to
    /// 0.20 for the shipped k=8 bge-small model (softmax-over-k confidence concentrates lower as k
    /// grows); re-fit against held-out data per docs/calibration.md when k or the embedder changes.
    /// </summary>
    public double ConfidenceFloor { get; init; } = 0.20;

    /// <summary>Output-token estimate used for cost math when the request sets no max.</summary>
    public int DefaultOutputTokensEstimate { get; init; } = 512;
}

/// <summary>
/// v1 routing policy: embed the prompt → nearest centroid (cosine) → temperature-scaled softmax
/// confidence → cheapest capable candidate from the cluster's policy row, escalating to the strong
/// default when confidence is below the floor. Honors (in priority order) reasoning-continuity pins
/// (a prior thinking signature forces its origin model), then session pins (warm cache), then the
/// capability guard (skips candidates that can't serve the request). Every decision carries a full
/// receipt stamped with the routing model's <c>policy_version</c> (research.md R4).
/// </summary>
public sealed class ClusterScorerPolicy(
    IEmbedder embedder,
    RoutingModel model,
    IPricingBook pricing,
    ClusterScorerOptions options,
    ICapabilityGuard? guard = null,
    ISessionPinner? pinner = null)
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

        var poolByKey = context.CandidatePool.ToDictionary(m => (m.Provider, m.ModelId));

        // Guard-filtered, cost-ranked alternatives the operator can actually reach.
        var alternatives = new List<Alternative>();
        ModelRef? strongest = null;
        double strongestQuality = double.NegativeInfinity;
        foreach (var candidate in model.Clusters[top1].Candidates)
        {
            if (!poolByKey.TryGetValue((candidate.Provider, candidate.ModelId), out var modelRef))
            {
                continue;
            }

            if (guard?.Check(request, modelRef) is { Allowed: false }) // candidate can't serve this request
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

        var sessionKey = pinner?.ResolveKey(request, context.TenantId);
        var (chosen, decisionKind, reason, pinApplied, pinReason) =
            Select(request, context, alternatives, confidence, top1, sessionKey);

        var chosenCost = pricing.EstimateUsd(chosen, estInputTokens, estOutputTokens);
        var strongestCost = strongest is null ? chosenCost : pricing.EstimateUsd(strongest, estInputTokens, estOutputTokens);
        var withDeltas = alternatives.Select(a => a with { EstCostDeltaUsd = a.EstCostUsd - chosenCost }).ToList();

        // Non-fatal capability degradation on the chosen model surfaces as a receipt notice.
        var notice = guard?.Check(request, chosen);
        var capabilityNotice = notice is { Allowed: true, Fatal: false, Code: { } code } ? code : null;

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
            SessionPinApplied = pinApplied,
            SessionPinReason = pinReason,
            PricingSnapshotDate = pricing.SnapshotDate,
            CapabilityNotice = capabilityNotice,
        };
    }

    private (ModelRef Chosen, RoutingDecisionKind Kind, RoutingReason Reason, bool PinApplied, SessionPinReason? PinReason) Select(
        ChatRequest request, RoutingContext context, List<Alternative> alternatives, double confidence, int clusterId, string? sessionKey)
    {
        // 1. Reasoning continuity: a prior thinking signature forces its origin model (research.md R2).
        var continuity = FindContinuityModel(request, context.CandidatePool);
        if (continuity is not null)
        {
            return (continuity, RoutingDecisionKind.Routed, RoutingReason.SessionPinned, true, SessionPinReason.WarmCache);
        }

        // 2. Session pin: keep the conversation warm on the same upstream while the cluster is unchanged.
        if (pinner is not null && sessionKey is not null)
        {
            var pinned = pinner.GetPin(sessionKey, clusterId);
            if (pinned is not null && context.CandidatePool.Contains(pinned))
            {
                return (pinned, RoutingDecisionKind.Routed, RoutingReason.SessionPinned, true, SessionPinReason.WarmCache);
            }
        }

        // 3. Confidence gate / capability exhaustion → escalate to the strong default.
        if (confidence < options.ConfidenceFloor || alternatives.Count == 0)
        {
            var reason = alternatives.Count == 0 ? RoutingReason.LowConfidenceCluster : RoutingReason.ConfidenceBelowFloor;
            pinner?.Pin(sessionKey!, context.StrongDefault, clusterId);
            return (context.StrongDefault, RoutingDecisionKind.Escalated, reason, false, null);
        }

        // 4. Cheapest capable.
        var chosen = alternatives[0].Model;
        if (sessionKey is not null)
        {
            pinner?.Pin(sessionKey, chosen, clusterId);
        }

        return (chosen, RoutingDecisionKind.Routed, RoutingReason.CheapestCapable, false, null);
    }

    /// <summary>Find the origin model of a prior thinking signature, if it is in the pool.</summary>
    private static ModelRef? FindContinuityModel(ChatRequest request, IReadOnlyList<ModelRef> pool)
    {
        foreach (var message in request.Messages)
        {
            foreach (var part in message.Parts)
            {
                if (part is ThinkingPart { Signature: not null, OriginProvider: { } provider, OriginModelId: { } modelId })
                {
                    var match = pool.FirstOrDefault(m => m.Provider == provider && m.ModelId == modelId);
                    if (match is not null)
                    {
                        return match;
                    }
                }
            }
        }

        return null;
    }

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
