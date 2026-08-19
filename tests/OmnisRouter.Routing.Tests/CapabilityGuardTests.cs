using OmnisRouter.Core.Abstractions;
using OmnisRouter.Core.Model;
using OmnisRouter.Routing.Guardrails;

namespace OmnisRouter.Routing.Tests;

public class CapabilityGuardTests
{
    private static readonly ModelRef FullyCapable = new(Provider.Anthropic, "claude-opus-4-8")
    {
        Capabilities = ModelCapabilities.Vision | ModelCapabilities.Tools | ModelCapabilities.ParallelTools
            | ModelCapabilities.StrictSchema | ModelCapabilities.PromptCachePin | ModelCapabilities.Thinking
            | ModelCapabilities.Streaming,
    };

    private static readonly ModelRef NoVision = new(Provider.OpenAI, "gpt-5-text-only")
    {
        Capabilities = ModelCapabilities.Tools | ModelCapabilities.StrictSchema,
    };

    private static readonly ModelRef GeminiBasic = new(Provider.Gemini, "gemini-2.5-pro")
    {
        Capabilities = ModelCapabilities.Vision | ModelCapabilities.Tools,
    };

    private static readonly ModelRef NoCache = new(Provider.OpenAI, "gpt-5")
    {
        Capabilities = ModelCapabilities.Vision | ModelCapabilities.Tools,
    };

    private static readonly ModelRef OpenAiModel = new(Provider.OpenAI, "gpt-5")
    {
        Capabilities = ModelCapabilities.Vision | ModelCapabilities.Tools,
    };

    private static ChatRequest RequestWith(RequestCapabilities caps) => new()
    {
        OriginFormat = ClientFormat.OpenAI,
        Messages = [new Message(Role.User, [new TextPart("hi")])],
        CapabilitiesUsed = caps,
    };

    [Fact]
    public void Vision_request_against_non_vision_candidate_refuses()
    {
        var guard = new CapabilityGuard();

        var result = guard.Check(RequestWith(RequestCapabilities.Vision), NoVision);

        Assert.False(result.Allowed);
        Assert.True(result.Fatal);
        Assert.Equal("vision_unsupported", result.Code);
    }

    [Fact]
    public void Strict_schema_request_against_gemini_without_strict_refuses()
    {
        var guard = new CapabilityGuard();

        var result = guard.Check(RequestWith(RequestCapabilities.StrictSchema), GeminiBasic);

        Assert.False(result.Allowed);
        Assert.True(result.Fatal);
        Assert.Equal("strict_unsupported", result.Code);
    }

    [Fact]
    public void Parallel_same_tool_request_against_gemini_refuses()
    {
        var guard = new CapabilityGuard();

        var result = guard.Check(RequestWith(RequestCapabilities.ParallelSameTool), GeminiBasic);

        Assert.False(result.Allowed);
        Assert.True(result.Fatal);
        Assert.Equal("parallel_same_tool_unsupported", result.Code);
    }

    [Fact]
    public void Cache_pin_request_against_no_cache_candidate_refuses()
    {
        var guard = new CapabilityGuard();

        var result = guard.Check(RequestWith(RequestCapabilities.CachePinGuaranteed), NoCache);

        Assert.False(result.Allowed);
        Assert.True(result.Fatal);
        Assert.Equal("cache_pin_unsupported", result.Code);
    }

    [Fact]
    public void Thinking_request_against_openai_refuses()
    {
        var guard = new CapabilityGuard();

        var result = guard.Check(RequestWith(RequestCapabilities.Thinking), OpenAiModel);

        Assert.False(result.Allowed);
        Assert.True(result.Fatal);
        Assert.Equal("thinking_unsupported", result.Code);
    }

    [Fact]
    public void Thinking_with_signature_against_non_thinking_candidate_refuses()
    {
        var guard = new CapabilityGuard();

        var result = guard.Check(RequestWith(RequestCapabilities.ThinkingWithSignature), OpenAiModel);

        Assert.False(result.Allowed);
        Assert.True(result.Fatal);
        Assert.Equal("thinking_signature_unsupported", result.Code);
    }

    [Fact]
    public void Remote_image_url_against_non_openai_candidate_is_a_non_fatal_notice()
    {
        var guard = new CapabilityGuard();

        var result = guard.Check(RequestWith(RequestCapabilities.RemoteImageUrl), FullyCapable);

        Assert.True(result.Allowed);
        Assert.False(result.Fatal);
        Assert.Equal("remote_image_will_be_fetched", result.Code);
    }

    [Fact]
    public void Numeric_reasoning_budget_against_openai_is_a_non_fatal_notice()
    {
        var guard = new CapabilityGuard();

        var result = guard.Check(RequestWith(RequestCapabilities.NumericReasoningBudget), OpenAiModel);

        Assert.True(result.Allowed);
        Assert.False(result.Fatal);
        Assert.Equal("reasoning_budget_approximated", result.Code);
    }

    [Fact]
    public void Fully_capable_candidate_returns_ok()
    {
        var guard = new CapabilityGuard();

        var caps = RequestCapabilities.Vision | RequestCapabilities.StrictSchema
            | RequestCapabilities.ParallelSameTool | RequestCapabilities.CachePinGuaranteed
            | RequestCapabilities.Thinking;

        var result = guard.Check(RequestWith(caps), FullyCapable);

        Assert.Equal(GuardResult.Ok, result);
    }

    [Fact]
    public void No_capabilities_used_returns_ok_even_against_a_minimal_candidate()
    {
        var guard = new CapabilityGuard();

        var result = guard.Check(RequestWith(RequestCapabilities.None), NoVision);

        Assert.Equal(GuardResult.Ok, result);
    }
}
