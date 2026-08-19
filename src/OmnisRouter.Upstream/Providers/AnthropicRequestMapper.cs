using System.Text.Json;
using OmnisRouter.Core.Model;

namespace OmnisRouter.Upstream.Providers;

/// <summary>
/// Maps the neutral <see cref="ChatRequest"/> into an Anthropic Messages wire request. Anthropic
/// requires <c>max_tokens</c> on every request (research.md R2 / task spec) — the router substitutes
/// <see cref="DefaultMaxTokens"/> when the neutral request doesn't carry one.
/// </summary>
internal static class AnthropicRequestMapper
{
    private const int DefaultMaxTokens = 4096;

    public static AnthropicMessagesRequest ToWireRequest(ChatRequest request, ModelRef model, bool stream) =>
        new()
        {
            Model = model.ModelId,
            MaxTokens = request.MaxTokens ?? DefaultMaxTokens,
            Messages = request.Messages.Select(ToWireMessage).ToList(),
            System = ToWireSystem(request.System),
            Tools = request.Tools.Count > 0 ? request.Tools.Select(ToWireTool).ToList() : null,
            ToolChoice = request.ToolChoice is { } toolChoice ? ToWireToolChoice(toolChoice) : null,
            Temperature = request.Temperature,
            Stream = stream,
            Thinking = request.Thinking is { } thinking ? ToWireThinking(thinking) : null,
        };

    /// <summary>A system with no cache breakpoints serializes as a plain string; anything cached becomes a block array.</summary>
    private static object? ToWireSystem(IReadOnlyList<TextPart> system)
    {
        if (system.Count == 0)
        {
            return null;
        }

        if (system.All(p => p.Cache is null))
        {
            return string.Concat(system.Select(p => p.Text));
        }

        return system.Select(ToWireTextBlock).ToList();
    }

    private static AnthropicMessage ToWireMessage(Message message) => new()
    {
        // Anthropic Messages has only user/assistant roles; system rides top-level and tool
        // results/tool_use blocks ride inside user/assistant content respectively.
        Role = message.Role == Role.Assistant ? "assistant" : "user",
        Content = message.Parts.Select(ToWireContentBlock).ToList(),
    };

    private static AnthropicContentBlock ToWireTextBlock(TextPart part) => new()
    {
        Type = "text",
        Text = part.Text,
        CacheControl = ToWireCacheControl(part.Cache),
    };

    private static AnthropicCacheControl? ToWireCacheControl(CacheDirective? cache) => cache is null
        ? null
        : new AnthropicCacheControl
        {
            Type = "ephemeral",
            Ttl = cache.Ttl == CacheTtl.OneHour ? "1h" : null,
        };

    private static AnthropicContentBlock ToWireContentBlock(ContentPart part) => part switch
    {
        TextPart text => ToWireTextBlock(text),
        ImagePart image => new AnthropicContentBlock
        {
            Type = "image",
            Source = new AnthropicImageSource
            {
                Type = "base64",
                MediaType = image.MediaType,
                Data = image.Base64 ?? "",
            },
        },
        ToolUsePart toolUse => new AnthropicContentBlock
        {
            Type = "tool_use",
            Id = toolUse.Id,
            Name = toolUse.Name,
            Input = ParseInputJson(toolUse.InputJson),
        },
        ToolResultPart toolResult => new AnthropicContentBlock
        {
            Type = "tool_result",
            ToolUseId = toolResult.ToolUseId,
            IsError = toolResult.IsError ? true : null,
            ToolResultContent = BuildToolResultContent(toolResult.Content),
        },
        ThinkingPart { Redacted: true } thinking => new AnthropicContentBlock
        {
            Type = "redacted_thinking",
            Data = thinking.Signature,
        },
        ThinkingPart thinking => new AnthropicContentBlock
        {
            Type = "thinking",
            Thinking = thinking.Text,
            Signature = thinking.Signature,
        },
        _ => throw new NotSupportedException($"Unsupported content part: {part.GetType().Name}"),
    };

    private static JsonElement ParseInputJson(string json)
    {
        using var doc = JsonDocument.Parse(string.IsNullOrEmpty(json) ? "{}" : json);
        return doc.RootElement.Clone();
    }

    /// <summary>A single text result serializes as a plain string; anything richer becomes a content-block array.</summary>
    private static object BuildToolResultContent(IReadOnlyList<ContentPart> parts) =>
        parts is [TextPart singleText] ? singleText.Text : parts.Select(ToWireContentBlock).ToList();

    private static AnthropicTool ToWireTool(Tool tool)
    {
        using var schemaDoc = JsonDocument.Parse(tool.JsonSchema);
        return new AnthropicTool
        {
            Name = tool.Name,
            Description = tool.Description,
            InputSchema = schemaDoc.RootElement.Clone(),
        };
    }

    private static object ToWireToolChoice(ToolChoice toolChoice) => toolChoice.Kind switch
    {
        ToolChoiceKind.Auto => new { type = "auto" },
        ToolChoiceKind.Any => new { type = "any" },
        ToolChoiceKind.None => new { type = "none" },
        ToolChoiceKind.Specific => new { type = "tool", name = toolChoice.Name },
        _ => new { type = "auto" },
    };

    private static AnthropicThinkingConfig ToWireThinking(ThinkingConfig thinking) => new()
    {
        Type = thinking.Enabled ? "enabled" : "disabled",
        BudgetTokens = thinking.BudgetTokens,
    };
}
