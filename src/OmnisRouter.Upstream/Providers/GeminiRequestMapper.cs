using System.Text.Json;
using OmnisRouter.Core.Model;

namespace OmnisRouter.Upstream.Providers;

/// <summary>Maps the neutral <see cref="ChatRequest"/> into a Gemini <c>generateContent</c> wire request.</summary>
internal static class GeminiRequestMapper
{
    public static GeminiGenerateContentRequest ToWireRequest(ChatRequest request)
    {
        var wireRequest = new GeminiGenerateContentRequest
        {
            Contents = request.Messages.Select(ToWireContent).Where(c => c is not null).Select(c => c!).ToList(),
        };

        if (request.System.Count > 0)
        {
            wireRequest.SystemInstruction = new GeminiContent
            {
                Parts = [new GeminiPart { Text = string.Concat(request.System.Select(p => p.Text)) }],
            };
        }

        if (request.Tools.Count > 0)
        {
            wireRequest.Tools =
            [
                new GeminiTool { FunctionDeclarations = request.Tools.Select(ToWireFunctionDeclaration).ToList() },
            ];
        }

        if (request.ToolChoice is { } toolChoice)
        {
            wireRequest.ToolConfig = new GeminiToolConfig
            {
                FunctionCallingConfig = ToWireFunctionCallingConfig(toolChoice),
            };
        }

        if (request.MaxTokens is not null || request.Temperature is not null || request.Thinking is not null)
        {
            wireRequest.GenerationConfig = new GeminiGenerationConfig
            {
                MaxOutputTokens = request.MaxTokens,
                Temperature = request.Temperature,
                ThinkingConfig = request.Thinking is { } thinking
                    ? new GeminiThinkingConfig { ThinkingBudget = thinking.BudgetTokens, IncludeThoughts = thinking.Enabled }
                    : null,
            };
        }

        return wireRequest;
    }

    private static GeminiContent? ToWireContent(Message message)
    {
        if (message.Role == Role.Tool)
        {
            var toolResult = message.Parts.OfType<ToolResultPart>().FirstOrDefault();
            if (toolResult is null)
            {
                return null;
            }

            return new GeminiContent
            {
                Role = "user",
                Parts =
                [
                    new GeminiPart
                    {
                        FunctionResponse = new GeminiFunctionResponse
                        {
                            Name = ExtractFunctionName(toolResult.ToolUseId),
                            Response = BuildResponseBody(toolResult),
                        },
                    },
                ],
            };
        }

        var parts = new List<GeminiPart>();
        foreach (var part in message.Parts)
        {
            switch (part)
            {
                case TextPart text:
                    parts.Add(new GeminiPart { Text = text.Text });
                    break;
                case ImagePart { Url: not null } image:
                    parts.Add(new GeminiPart
                    {
                        FileData = new GeminiFileData { MimeType = image.MediaType, FileUri = image.Url },
                    });
                    break;
                case ImagePart image:
                    parts.Add(new GeminiPart
                    {
                        InlineData = new GeminiInlineData { MimeType = image.MediaType, Data = image.Base64 ?? "" },
                    });
                    break;
                case ToolUsePart toolUse:
                    parts.Add(new GeminiPart
                    {
                        FunctionCall = new GeminiFunctionCall { Name = toolUse.Name, Args = ParseArgs(toolUse.InputJson) },
                    });
                    break;
            }
        }

        if (parts.Count == 0)
        {
            return null;
        }

        return new GeminiContent
        {
            Role = message.Role == Role.Assistant ? "model" : "user",
            Parts = parts,
        };
    }

    /// <summary>
    /// Best-effort recovery of the function name from a synthesized <c>{name}_{index}</c> tool-use
    /// id — Gemini's <c>functionResponse</c> needs a name, but the neutral <see cref="ToolResultPart"/>
    /// only carries the id it was correlated against.
    /// </summary>
    private static string ExtractFunctionName(string toolUseId)
    {
        var lastUnderscore = toolUseId.LastIndexOf('_');
        return lastUnderscore > 0 ? toolUseId[..lastUnderscore] : toolUseId;
    }

    private static JsonElement BuildResponseBody(ToolResultPart toolResult)
    {
        var text = string.Concat(toolResult.Content.OfType<TextPart>().Select(t => t.Text));
        if (string.IsNullOrEmpty(text))
        {
            return JsonDocument.Parse("{}").RootElement.Clone();
        }

        try
        {
            using var doc = JsonDocument.Parse(text);
            return doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            using var doc = JsonDocument.Parse(JsonSerializer.Serialize(new { result = text }));
            return doc.RootElement.Clone();
        }
    }

    private static JsonElement ParseArgs(string inputJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrEmpty(inputJson) ? "{}" : inputJson);
            return doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            using var doc = JsonDocument.Parse("{}");
            return doc.RootElement.Clone();
        }
    }

    private static GeminiFunctionDeclaration ToWireFunctionDeclaration(Tool tool)
    {
        using var schemaDoc = JsonDocument.Parse(tool.JsonSchema);
        return new GeminiFunctionDeclaration
        {
            Name = tool.Name,
            Description = tool.Description,
            Parameters = schemaDoc.RootElement.Clone(),
        };
    }

    private static GeminiFunctionCallingConfig ToWireFunctionCallingConfig(ToolChoice toolChoice) => toolChoice.Kind switch
    {
        ToolChoiceKind.Auto => new GeminiFunctionCallingConfig { Mode = "AUTO" },
        ToolChoiceKind.None => new GeminiFunctionCallingConfig { Mode = "NONE" },
        ToolChoiceKind.Any => new GeminiFunctionCallingConfig { Mode = "ANY" },
        ToolChoiceKind.Specific => new GeminiFunctionCallingConfig
        {
            Mode = "ANY",
            AllowedFunctionNames = [toolChoice.Name ?? ""],
        },
        _ => new GeminiFunctionCallingConfig { Mode = "AUTO" },
    };
}
