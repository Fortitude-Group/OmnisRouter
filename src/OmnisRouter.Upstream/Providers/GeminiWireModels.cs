using System.Text.Json;
using System.Text.Json.Serialization;

namespace OmnisRouter.Upstream.Providers;

// Wire-shape DTOs for the Gemini generateContent/streamGenerateContent API (request, non-streaming
// response, and streaming chunk — the streaming chunk is the same GenerateContentResponse shape,
// per research.md R2). Property names mirror the Gemini JSON field names exactly via
// JsonPropertyName; the neutral-model mapping lives in GeminiRequestMapper / GeminiResponseMapper.

internal sealed class GeminiGenerateContentRequest
{
    [JsonPropertyName("contents")]
    public required List<GeminiContent> Contents { get; set; }

    [JsonPropertyName("systemInstruction")]
    public GeminiContent? SystemInstruction { get; set; }

    [JsonPropertyName("tools")]
    public List<GeminiTool>? Tools { get; set; }

    [JsonPropertyName("toolConfig")]
    public GeminiToolConfig? ToolConfig { get; set; }

    [JsonPropertyName("generationConfig")]
    public GeminiGenerationConfig? GenerationConfig { get; set; }
}

internal sealed class GeminiContent
{
    [JsonPropertyName("role")]
    public string? Role { get; set; }

    [JsonPropertyName("parts")]
    public List<GeminiPart> Parts { get; set; } = [];
}

internal sealed class GeminiPart
{
    [JsonPropertyName("text")]
    public string? Text { get; set; }

    [JsonPropertyName("inlineData")]
    public GeminiInlineData? InlineData { get; set; }

    [JsonPropertyName("fileData")]
    public GeminiFileData? FileData { get; set; }

    [JsonPropertyName("functionCall")]
    public GeminiFunctionCall? FunctionCall { get; set; }

    [JsonPropertyName("functionResponse")]
    public GeminiFunctionResponse? FunctionResponse { get; set; }
}

internal sealed class GeminiInlineData
{
    [JsonPropertyName("mimeType")]
    public required string MimeType { get; set; }

    [JsonPropertyName("data")]
    public required string Data { get; set; }
}

internal sealed class GeminiFileData
{
    [JsonPropertyName("mimeType")]
    public string? MimeType { get; set; }

    [JsonPropertyName("fileUri")]
    public required string FileUri { get; set; }
}

internal sealed class GeminiFunctionCall
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("args")]
    public JsonElement Args { get; set; }
}

internal sealed class GeminiFunctionResponse
{
    [JsonPropertyName("name")]
    public required string Name { get; set; }

    [JsonPropertyName("response")]
    public JsonElement Response { get; set; }
}

internal sealed class GeminiTool
{
    [JsonPropertyName("functionDeclarations")]
    public required List<GeminiFunctionDeclaration> FunctionDeclarations { get; set; }
}

internal sealed class GeminiFunctionDeclaration
{
    [JsonPropertyName("name")]
    public required string Name { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("parameters")]
    public JsonElement Parameters { get; set; }
}

internal sealed class GeminiToolConfig
{
    [JsonPropertyName("functionCallingConfig")]
    public required GeminiFunctionCallingConfig FunctionCallingConfig { get; set; }
}

internal sealed class GeminiFunctionCallingConfig
{
    [JsonPropertyName("mode")]
    public required string Mode { get; set; }

    [JsonPropertyName("allowedFunctionNames")]
    public List<string>? AllowedFunctionNames { get; set; }
}

internal sealed class GeminiGenerationConfig
{
    [JsonPropertyName("maxOutputTokens")]
    public int? MaxOutputTokens { get; set; }

    [JsonPropertyName("temperature")]
    public double? Temperature { get; set; }

    [JsonPropertyName("thinkingConfig")]
    public GeminiThinkingConfig? ThinkingConfig { get; set; }
}

internal sealed class GeminiThinkingConfig
{
    [JsonPropertyName("thinkingBudget")]
    public int? ThinkingBudget { get; set; }

    [JsonPropertyName("includeThoughts")]
    public bool? IncludeThoughts { get; set; }
}

internal sealed class GeminiUsageMetadata
{
    [JsonPropertyName("promptTokenCount")]
    public int PromptTokenCount { get; set; }

    [JsonPropertyName("candidatesTokenCount")]
    public int CandidatesTokenCount { get; set; }

    [JsonPropertyName("totalTokenCount")]
    public int TotalTokenCount { get; set; }
}

internal sealed class GeminiGenerateContentResponse
{
    [JsonPropertyName("candidates")]
    public List<GeminiCandidate>? Candidates { get; set; }

    [JsonPropertyName("usageMetadata")]
    public GeminiUsageMetadata? UsageMetadata { get; set; }
}

internal sealed class GeminiCandidate
{
    [JsonPropertyName("content")]
    public GeminiContent? Content { get; set; }

    [JsonPropertyName("finishReason")]
    public string? FinishReason { get; set; }

    [JsonPropertyName("index")]
    public int Index { get; set; }
}
