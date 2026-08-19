using OmnisRouter.Core.Abstractions;
using OmnisRouter.Core.Model;

namespace OmnisRouter.Routing.Guardrails;

/// <summary>
/// Pre-dispatch capability guardrails (research.md R2): refuse to route a request onto a
/// candidate that would silently drop a capability the request actually exercises, and surface
/// non-fatal degradations as notices instead of dropping them quietly. Table-driven so each rule
/// stays traceable to its research.md R2 citation.
/// </summary>
public sealed class CapabilityGuard : ICapabilityGuard
{
    /// <summary>
    /// One hard-refusal rule: a bit the request must exercise, evaluated against the candidate,
    /// with the result code/message to return when it fires.
    /// </summary>
    private readonly record struct RefusalRule(
        RequestCapabilities RequiredRequestFlag,
        Func<ModelRef, bool> CandidateFails,
        string Code,
        string Message);

    /// <summary>One non-fatal notice rule, same shape as <see cref="RefusalRule"/> but never blocks routing.</summary>
    private readonly record struct NoticeRule(
        RequestCapabilities RequiredRequestFlag,
        Func<ModelRef, bool> CandidateTriggers,
        string Code,
        string Message);

    // research.md R2, "Guardrail rules (enforced pre-dispatch, return 4xx not a silent downgrade)".
    private static readonly RefusalRule[] RefusalRules =
    [
        // "vision→non-vision model"
        new RefusalRule(
            RequestCapabilities.Vision,
            candidate => !candidate.Supports(ModelCapabilities.Vision),
            "vision_unsupported",
            "Request includes image content but the candidate model does not support vision."),

        // "strict:true→Gemini" (StrictSchema is honored on Anthropic/OpenAI; only Gemini lacks the guarantee).
        new RefusalRule(
            RequestCapabilities.StrictSchema,
            candidate => !candidate.Supports(ModelCapabilities.StrictSchema) && candidate.Provider == Provider.Gemini,
            "strict_unsupported",
            "Request requires strict:true tool schemas but the candidate (Gemini) cannot guarantee strict JSON schema adherence."),

        // "parallel same-named tool calls→Gemini" (Gemini functionResponse matches by name only,
        // so parallel same-named calls cannot be disambiguated).
        new RefusalRule(
            RequestCapabilities.ParallelSameTool,
            candidate => !candidate.Supports(ModelCapabilities.ParallelTools) && candidate.Provider == Provider.Gemini,
            "parallel_same_tool_unsupported",
            "Request may issue parallel calls to the same tool name but the candidate (Gemini) cannot disambiguate them by id."),

        // "explicit guaranteed cache pin→provider that can't honor it" (OpenAI caching is
        // automatic/unaddressable; only providers advertising PromptCachePin can honor a pin).
        new RefusalRule(
            RequestCapabilities.CachePinGuaranteed,
            candidate => !candidate.Supports(ModelCapabilities.PromptCachePin),
            "cache_pin_unsupported",
            "Request requires a guaranteed cache_control pin but the candidate cannot honor explicit cache pinning."),

        // "thinking blocks→OpenAI Chat Completions" (generalized: any candidate without Thinking).
        new RefusalRule(
            RequestCapabilities.Thinking,
            candidate => !candidate.Supports(ModelCapabilities.Thinking),
            "thinking_unsupported",
            "Request enables extended thinking but the candidate model does not support it."),

        // "multi-turn reasoning with a signature→a different model/provider than produced it"
        // (cross-model continuity itself is enforced by the routing policy/session pin, not here;
        // this guard only refuses routing a signature-bearing request to a candidate that has no
        // thinking support at all, per the task spec).
        new RefusalRule(
            RequestCapabilities.ThinkingWithSignature,
            candidate => !candidate.Supports(ModelCapabilities.Thinking),
            "thinking_signature_unsupported",
            "Request carries a thinking signature but the candidate model does not support thinking."),
    ];

    // research.md R2 non-fatal degradations — surfaced, never silently dropped.
    private static readonly NoticeRule[] NoticeRules =
    [
        // "remote image-URL→provider that can't dereference it (unless the router fetches+re-encodes)":
        // only OpenAI dereferences a remote image_url natively; every other provider requires the
        // router to fetch + re-encode, which is a degradation worth surfacing, not a refusal.
        new NoticeRule(
            RequestCapabilities.RemoteImageUrl,
            candidate => candidate.Provider != Provider.OpenAI,
            "remote_image_will_be_fetched",
            "Request references a remote image URL; the router will fetch and re-encode it for the candidate provider."),

        // "numeric reasoning budget→enum-only provider (map + surface the approximation)":
        // OpenAI's reasoning vocabulary is effort-enum-only (no numeric budget_tokens/thinkingBudget).
        new NoticeRule(
            RequestCapabilities.NumericReasoningBudget,
            candidate => candidate.Provider == Provider.OpenAI,
            "reasoning_budget_approximated",
            "Request specifies a numeric reasoning budget; the candidate (OpenAI) only supports an effort enum, so the budget will be approximated."),
    ];

    public GuardResult Check(ChatRequest request, ModelRef candidate)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(candidate);

        foreach (var rule in RefusalRules)
        {
            if (RequestExercises(request, rule.RequiredRequestFlag) && rule.CandidateFails(candidate))
            {
                return GuardResult.Refuse(rule.Code, rule.Message);
            }
        }

        foreach (var rule in NoticeRules)
        {
            if (RequestExercises(request, rule.RequiredRequestFlag) && rule.CandidateTriggers(candidate))
            {
                return GuardResult.Notice(rule.Code, rule.Message);
            }
        }

        return GuardResult.Ok;
    }

    private static bool RequestExercises(ChatRequest request, RequestCapabilities flag) =>
        (request.CapabilitiesUsed & flag) == flag;
}
