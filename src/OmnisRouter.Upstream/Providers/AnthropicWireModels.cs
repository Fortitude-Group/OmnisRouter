using System.Text.Json;
using System.Text.Json.Serialization;

namespace OmnisRouter.Upstream.Providers;

// Wire-shape DTOs for the Anthropic Messages API (request, non-streaming response). Property names
// mirror the Anthropic JSON field names exactly via JsonPropertyName; the neutral-model mapping
// lives in AnthropicRequestMapper / AnthropicResponseMapper. Streaming events are parsed manually
// (via JsonDocument) in AnthropicStreamState since each named event has a distinct shape — a single
// flat DTO would need every field optional and would obscure which fields belong to which event.

internal sealed class AnthropicMessagesRequest
{
    [JsonPropertyName("model")]
    public required string Model { get; set; }

    [JsonPropertyName("max_tokens")]
    public required int MaxTokens { get; set; }

    [JsonPropertyName("messages")]
    public required List<AnthropicMessage> Messages { get; set; }

    /// <summary>Either a plain string or a <see cref="List{AnthropicContentBlock}"/> — runtime-typed for serialization.</summary>
    [JsonPropertyName("system")]
    public object? System { get; set; }

    [JsonPropertyName("tools")]
    public List<AnthropicTool>? Tools { get; set; }

    /// <summary>An anonymous <c>{ type = "auto" | "any" | "none" | "tool", name? }</c> object.</summary>
    [JsonPropertyName("tool_choice")]
    public object? ToolChoice { get; set; }

    [JsonPropertyName("temperature")]
    public double? Temperature { get; set; }

    [JsonPropertyName("stream")]
    public bool Stream { get; set; }

    [JsonPropertyName("thinking")]
    public AnthropicThinkingConfig? Thinking { get; set; }
}

internal sealed class AnthropicThinkingConfig
{
    [JsonPropertyName("type")]
    public required string Type { get; set; }

    [JsonPropertyName("budget_tokens")]
    public int? BudgetTokens { get; set; }
}

internal sealed class AnthropicMessage
{
    [JsonPropertyName("role")]
    public required string Role { get; set; }

    [JsonPropertyName("content")]
    public required List<AnthropicContentBlock> Content { get; set; }
}

/// <summary>
/// A single Anthropic content block. Reused for every block <c>type</c> (text, image, tool_use,
/// tool_result, thinking, redacted_thinking) with all type-specific fields optional, and for the
/// nested <c>content</c> of a <c>tool_result</c> block.
/// </summary>
internal sealed class AnthropicContentBlock
{
    [JsonPropertyName("type")]
    public required string Type { get; set; }

    /// <summary>Text for a <c>text</c> block.</summary>
    [JsonPropertyName("text")]
    public string? Text { get; set; }

    /// <summary>Source for an <c>image</c> block.</summary>
    [JsonPropertyName("source")]
    public AnthropicImageSource? Source { get; set; }

    /// <summary>Tool-use id for a <c>tool_use</c> block.</summary>
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    /// <summary>Tool name for a <c>tool_use</c> block.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>Tool-call input (a JSON object) for a <c>tool_use</c> block.</summary>
    [JsonPropertyName("input")]
    public JsonElement? Input { get; set; }

    /// <summary>Correlating id for a <c>tool_result</c> block.</summary>
    [JsonPropertyName("tool_use_id")]
    public string? ToolUseId { get; set; }

    /// <summary>Either a plain string or a <see cref="List{AnthropicContentBlock}"/> — the nested content of a <c>tool_result</c> block.</summary>
    [JsonPropertyName("content")]
    public object? ToolResultContent { get; set; }

    [JsonPropertyName("is_error")]
    public bool? IsError { get; set; }

    /// <summary>Reasoning text for a <c>thinking</c> block.</summary>
    [JsonPropertyName("thinking")]
    public string? Thinking { get; set; }

    /// <summary>Provider/model-bound continuity signature for a <c>thinking</c> block.</summary>
    [JsonPropertyName("signature")]
    public string? Signature { get; set; }

    /// <summary>Opaque encrypted payload for a <c>redacted_thinking</c> block.</summary>
    [JsonPropertyName("data")]
    public string? Data { get; set; }

    [JsonPropertyName("cache_control")]
    public AnthropicCacheControl? CacheControl { get; set; }
}

internal sealed class AnthropicImageSource
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "base64";

    [JsonPropertyName("media_type")]
    public string? MediaType { get; set; }

    [JsonPropertyName("data")]
    public string? Data { get; set; }
}

internal sealed class AnthropicCacheControl
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "ephemeral";

    /// <summary>"1h" for a guaranteed pin; omitted (null) defaults to the 5-minute ephemeral TTL.</summary>
    [JsonPropertyName("ttl")]
    public string? Ttl { get; set; }
}

internal sealed class AnthropicTool
{
    [JsonPropertyName("name")]
    public required string Name { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("input_schema")]
    public JsonElement InputSchema { get; set; }
}

internal sealed class AnthropicUsage
{
    [JsonPropertyName("input_tokens")]
    public int InputTokens { get; set; }

    [JsonPropertyName("output_tokens")]
    public int OutputTokens { get; set; }

    [JsonPropertyName("cache_creation_input_tokens")]
    public int? CacheCreationInputTokens { get; set; }

    [JsonPropertyName("cache_read_input_tokens")]
    public int? CacheReadInputTokens { get; set; }
}

internal sealed class AnthropicMessagesResponse
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("role")]
    public string? Role { get; set; }

    [JsonPropertyName("model")]
    public string? Model { get; set; }

    [JsonPropertyName("content")]
    public List<AnthropicContentBlock> Content { get; set; } = [];

    [JsonPropertyName("stop_reason")]
    public string? StopReason { get; set; }

    [JsonPropertyName("stop_sequence")]
    public string? StopSequence { get; set; }

    [JsonPropertyName("usage")]
    public AnthropicUsage? Usage { get; set; }
}
