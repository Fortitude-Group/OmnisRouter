using OmnisRouter.Core.Model;

namespace OmnisRouter.Upstream.Providers;

/// <summary>Maps Anthropic Messages wire responses/usage/stop-reason into the neutral model.</summary>
internal static class AnthropicResponseMapper
{
    public static ChatResponse ToChatResponse(AnthropicMessagesResponse wire, ModelRef model)
    {
        var content = new List<ContentPart>();
        foreach (var block in wire.Content)
        {
            var part = ToContentPart(block, model.Provider);
            if (part is not null)
            {
                content.Add(part);
            }
        }

        return new ChatResponse
        {
            Content = content,
            StopReason = ToStopReason(wire.StopReason),
            Usage = ToUsage(wire.Usage),
            ServedBy = model,
        };
    }

    /// <summary>
    /// Maps one response content block. Unrecognized block types (e.g. server-tool blocks outside
    /// this slice's scope) are dropped rather than throwing — an upstream addition should never
    /// break response parsing.
    /// </summary>
    public static ContentPart? ToContentPart(AnthropicContentBlock block, Provider originProvider) => block.Type switch
    {
        "text" => new TextPart(block.Text ?? ""),
        "tool_use" => new ToolUsePart(block.Id ?? "", block.Name ?? "", block.Input?.GetRawText() ?? "{}"),
        "thinking" => new ThinkingPart
        {
            Text = block.Thinking,
            Signature = block.Signature,
            Redacted = false,
            OriginProvider = originProvider,
        },
        "redacted_thinking" => new ThinkingPart
        {
            Signature = block.Data,
            Redacted = true,
            OriginProvider = originProvider,
        },
        _ => null,
    };

    public static Usage ToUsage(AnthropicUsage? usage) => usage is null
        ? new Usage()
        : new Usage
        {
            InputTokens = usage.InputTokens,
            OutputTokens = usage.OutputTokens,
            CacheCreationTokens = usage.CacheCreationInputTokens ?? 0,
            CacheReadTokens = usage.CacheReadInputTokens ?? 0,
        };

    public static StopReason ToStopReason(string? stopReason) => stopReason switch
    {
        null => StopReason.EndTurn,
        "end_turn" => StopReason.EndTurn,
        "tool_use" => StopReason.ToolUse,
        "max_tokens" => StopReason.MaxTokens,
        "stop_sequence" => StopReason.StopSequence,
        "refusal" => StopReason.Refusal,
        "pause_turn" => StopReason.EndTurn,
        _ => StopReason.Error,
    };
}
