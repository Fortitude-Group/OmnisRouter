using System.Text.Json;
using System.Text.Json.Serialization;

namespace OmnisRouter.Upstream.Providers;

// Wire-shape DTOs for the OpenAI Chat Completions API (request, non-streaming response, and
// streaming chunk). Property names mirror the OpenAI JSON field names exactly via
// JsonPropertyName; the neutral-model mapping lives in OpenAiRequestMapper / OpenAiResponseMapper.

internal sealed class OpenAiChatRequest
{
    [JsonPropertyName("model")]
    public required string Model { get; set; }

    [JsonPropertyName("messages")]
    public required List<OpenAiMessage> Messages { get; set; }

    [JsonPropertyName("tools")]
    public List<OpenAiTool>? Tools { get; set; }

    [JsonPropertyName("tool_choice")]
    public object? ToolChoice { get; set; }

    [JsonPropertyName("max_completion_tokens")]
    public int? MaxCompletionTokens { get; set; }

    [JsonPropertyName("temperature")]
    public double? Temperature { get; set; }

    [JsonPropertyName("stream")]
    public bool Stream { get; set; }

    [JsonPropertyName("stream_options")]
    public OpenAiStreamOptions? StreamOptions { get; set; }
}

internal sealed class OpenAiStreamOptions
{
    [JsonPropertyName("include_usage")]
    public bool IncludeUsage { get; set; }
}

internal sealed class OpenAiMessage
{
    [JsonPropertyName("role")]
    public required string Role { get; set; }

    /// <summary>Either a plain string or a <see cref="List{OpenAiContentPart}"/> — runtime-typed for serialization.</summary>
    [JsonPropertyName("content")]
    public object? Content { get; set; }

    [JsonPropertyName("tool_calls")]
    public List<OpenAiToolCall>? ToolCalls { get; set; }

    [JsonPropertyName("tool_call_id")]
    public string? ToolCallId { get; set; }
}

internal sealed class OpenAiContentPart
{
    [JsonPropertyName("type")]
    public required string Type { get; set; }

    [JsonPropertyName("text")]
    public string? Text { get; set; }

    [JsonPropertyName("image_url")]
    public OpenAiImageUrl? ImageUrl { get; set; }
}

internal sealed class OpenAiImageUrl
{
    [JsonPropertyName("url")]
    public required string Url { get; set; }

    [JsonPropertyName("detail")]
    public string? Detail { get; set; }
}

internal sealed class OpenAiTool
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "function";

    [JsonPropertyName("function")]
    public required OpenAiFunctionDef Function { get; set; }
}

internal sealed class OpenAiFunctionDef
{
    [JsonPropertyName("name")]
    public required string Name { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("parameters")]
    public JsonElement Parameters { get; set; }

    [JsonPropertyName("strict")]
    public bool? Strict { get; set; }
}

internal sealed class OpenAiToolCall
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; } = "function";

    [JsonPropertyName("function")]
    public OpenAiFunctionCall? Function { get; set; }
}

internal sealed class OpenAiFunctionCall
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("arguments")]
    public string? Arguments { get; set; }
}

internal sealed class OpenAiUsage
{
    [JsonPropertyName("prompt_tokens")]
    public int PromptTokens { get; set; }

    [JsonPropertyName("completion_tokens")]
    public int CompletionTokens { get; set; }

    [JsonPropertyName("prompt_tokens_details")]
    public OpenAiPromptTokensDetails? PromptTokensDetails { get; set; }
}

internal sealed class OpenAiPromptTokensDetails
{
    [JsonPropertyName("cached_tokens")]
    public int CachedTokens { get; set; }
}

internal sealed class OpenAiChatCompletionResponse
{
    [JsonPropertyName("choices")]
    public List<OpenAiChoice> Choices { get; set; } = [];

    [JsonPropertyName("usage")]
    public OpenAiUsage? Usage { get; set; }
}

internal sealed class OpenAiChoice
{
    [JsonPropertyName("message")]
    public OpenAiResponseMessage? Message { get; set; }

    [JsonPropertyName("finish_reason")]
    public string? FinishReason { get; set; }
}

internal sealed class OpenAiResponseMessage
{
    [JsonPropertyName("content")]
    public string? Content { get; set; }

    [JsonPropertyName("tool_calls")]
    public List<OpenAiToolCall>? ToolCalls { get; set; }
}

internal sealed class OpenAiChatCompletionChunk
{
    [JsonPropertyName("choices")]
    public List<OpenAiChunkChoice>? Choices { get; set; }

    [JsonPropertyName("usage")]
    public OpenAiUsage? Usage { get; set; }
}

internal sealed class OpenAiChunkChoice
{
    [JsonPropertyName("delta")]
    public OpenAiChunkDelta? Delta { get; set; }

    [JsonPropertyName("finish_reason")]
    public string? FinishReason { get; set; }
}

internal sealed class OpenAiChunkDelta
{
    [JsonPropertyName("content")]
    public string? Content { get; set; }

    [JsonPropertyName("tool_calls")]
    public List<OpenAiToolCallDelta>? ToolCalls { get; set; }
}

internal sealed class OpenAiToolCallDelta
{
    [JsonPropertyName("index")]
    public int Index { get; set; }

    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("function")]
    public OpenAiFunctionCallDelta? Function { get; set; }
}

internal sealed class OpenAiFunctionCallDelta
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("arguments")]
    public string? Arguments { get; set; }
}
