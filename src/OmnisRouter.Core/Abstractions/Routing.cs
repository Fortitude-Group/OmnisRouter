using OmnisRouter.Core.Model;
using OmnisRouter.Core.Routing;

namespace OmnisRouter.Core.Abstractions;

/// <summary>In-process sentence embedder (ONNX, no network hop) — pinned <c>bge-small-en-v1.5</c>.</summary>
public interface IEmbedder
{
    /// <summary>Embedding dimensionality (384 for bge-small).</summary>
    int Dimension { get; }

    /// <summary>Embed a single text into a unit-length vector.</summary>
    float[] Embed(string text);
}

/// <summary>Per-request context available to a routing policy.</summary>
public sealed record RoutingContext
{
    public string TenantId { get; init; } = "default";

    /// <summary>Candidate models the router may choose among.</summary>
    public required IReadOnlyList<ModelRef> CandidatePool { get; init; }

    /// <summary>Model used when the router escalates (low confidence).</summary>
    public required ModelRef StrongDefault { get; init; }

    /// <summary>Optional caller cost preference: "low" | "balanced" | "max".</summary>
    public string? CostTier { get; init; }
}

/// <summary>Chooses a model for a request. <c>ClusterScorerPolicy</c> is the v1 default.</summary>
public interface IRoutingPolicy
{
    string Name { get; }

    ModelDecision Decide(ChatRequest request, RoutingContext context);
}

/// <summary>Result of a pre-dispatch capability check.</summary>
public sealed record GuardResult
{
    /// <summary>True when the candidate may serve the request as-is.</summary>
    public bool Allowed { get; init; } = true;

    /// <summary>True = hard refusal (4xx); false = non-fatal degradation surfaced as a notice.</summary>
    public bool Fatal { get; init; }

    /// <summary>Stable code, e.g. "vision_unsupported", "thinking_signature_cross_model".</summary>
    public string? Code { get; init; }

    public string? Message { get; init; }

    public static GuardResult Ok { get; } = new();

    public static GuardResult Refuse(string code, string message) =>
        new() { Allowed = false, Fatal = true, Code = code, Message = message };

    public static GuardResult Notice(string code, string message) =>
        new() { Allowed = true, Fatal = false, Code = code, Message = message };
}

/// <summary>Pre-dispatch guardrails: refuse (don't silently drop) capabilities a candidate can't honor.</summary>
public interface ICapabilityGuard
{
    /// <summary>Returns <see cref="GuardResult.Ok"/> if routing is allowed; otherwise a refusal/notice.</summary>
    GuardResult Check(ChatRequest request, ModelRef candidate);
}

/// <summary>Session pinning to keep upstream prompt caches warm across turns.</summary>
public interface ISessionPinner
{
    /// <summary>Client <c>X-Omnis-Session-Id</c> if present, else derived HMAC key (see research.md R4).</summary>
    string ResolveKey(ChatRequest request, string tenantId);

    /// <summary>The pinned model for this session/cluster, or null if none or the cluster changed.</summary>
    ModelRef? GetPin(string sessionKey, int clusterId);

    void Pin(string sessionKey, ModelRef model, int clusterId);
}
