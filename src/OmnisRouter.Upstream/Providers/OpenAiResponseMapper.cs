using OmnisRouter.Core.Model;

namespace OmnisRouter.Upstream.Providers;

/// <summary>Maps OpenAI Chat Completions wire responses/usage/finish-reason into the neutral model.</summary>
internal static class OpenAiResponseMapper
{
    public static ChatResponse ToChatResponse(OpenAiChatCompletionResponse wire, ModelRef model)
    {
        var choice = wire.Choices.Count > 0 ? wire.Choices[0] : null;
        var content = new List<ContentPart>();

        var text = choice?.Message?.Content;
        if (!string.IsNullOrEmpty(text))
        {
            content.Add(new TextPart(text));
        }

        if (choice?.Message?.ToolCalls is { Count: > 0 } toolCalls)
        {
            content.AddRange(toolCalls.Select(tc => new ToolUsePart(
                tc.Id ?? string.Empty,
                tc.Function?.Name ?? string.Empty,
                tc.Function?.Arguments ?? "{}")));
        }

        return new ChatResponse
        {
            Content = content,
            StopReason = ToStopReason(choice?.FinishReason),
            Usage = ToUsage(wire.Usage),
            ServedBy = model,
        };
    }

    public static Usage ToUsage(OpenAiUsage? usage) => usage is null
        ? new Usage()
        : new Usage
        {
            InputTokens = usage.PromptTokens,
            OutputTokens = usage.CompletionTokens,
            CacheReadTokens = usage.PromptTokensDetails?.CachedTokens ?? 0,
        };

    public static StopReason ToStopReason(string? finishReason) => finishReason switch
    {
        null => StopReason.EndTurn,
        "stop" => StopReason.EndTurn,
        "length" => StopReason.MaxTokens,
        "tool_calls" => StopReason.ToolUse,
        "content_filter" => StopReason.Refusal,
        _ => StopReason.Error,
    };
}
