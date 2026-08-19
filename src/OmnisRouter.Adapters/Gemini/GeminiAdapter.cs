using System.Net.Http;
using System.Net.ServerSentEvents;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using OmnisRouter.Core.Abstractions;
using OmnisRouter.Core.Model;
using OmnisRouter.Core.Routing;

namespace OmnisRouter.Adapters.Gemini;

/// <summary>
/// CLIENT-facing Gemini <c>generateContent</c> wire adapter (see contracts/wire-formats.md and
/// research.md R2). Owns parsing an inbound Gemini request into the neutral <see cref="ChatRequest"/>
/// and rendering neutral responses/streams back into Gemini shape for the client. Does <b>not</b>
/// call upstream providers — a separate <c>IUpstreamClient</c> owns the chosen-provider wire I/O;
/// <see cref="FromInternal"/> exists only for interface completeness (nothing in this project
/// dispatches it).
/// </summary>
public sealed class GeminiAdapter : IFormatAdapter
{
    public ClientFormat Format => ClientFormat.Gemini;

    public ChatRequest ToInternal(JsonElement body, string? pathModel = null)
    {
        var systemParts = new List<TextPart>();
        if (body.TryGetProperty("systemInstruction", out var systemInstructionEl))
        {
            systemParts.AddRange(ExtractTextParts(systemInstructionEl));
        }

        var messages = new List<Message>();
        var capabilities = RequestCapabilities.None;

        // Gemini's functionResponse carries only a name, not an id (research.md R2), so a call and
        // its later result are correlated FIFO by name against the ids synthesized for functionCall
        // parts as they are encountered.
        var pendingToolCallIdsByName = new Dictionary<string, Queue<string>>(StringComparer.Ordinal);
        var toolCallCounter = 0;

        if (body.TryGetProperty("contents", out var contentsEl) && contentsEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var contentEl in contentsEl.EnumerateArray())
            {
                var roleStr = contentEl.TryGetProperty("role", out var roleEl) ? roleEl.GetString() : null;
                var role = MapRole(roleStr);

                var parts = new List<ContentPart>();
                var toolResultParts = new List<ToolResultPart>();

                if (contentEl.TryGetProperty("parts", out var partsEl) && partsEl.ValueKind == JsonValueKind.Array)
                {
                    var namesInTurn = new List<string>();

                    foreach (var partEl in partsEl.EnumerateArray())
                    {
                        if (partEl.TryGetProperty("text", out var textEl))
                        {
                            var text = textEl.GetString();
                            if (!string.IsNullOrEmpty(text))
                            {
                                parts.Add(new TextPart(text));
                            }

                            continue;
                        }

                        if (partEl.TryGetProperty("inlineData", out var inlineDataEl))
                        {
                            var mimeType = inlineDataEl.TryGetProperty("mimeType", out var mimeEl)
                                ? mimeEl.GetString() ?? "image/png"
                                : "image/png";
                            var data = inlineDataEl.TryGetProperty("data", out var dataEl)
                                ? dataEl.GetString() ?? ""
                                : "";

                            parts.Add(new ImagePart(mimeType) { Base64 = data });
                            capabilities |= RequestCapabilities.Vision;
                            continue;
                        }

                        if (partEl.TryGetProperty("fileData", out var fileDataEl))
                        {
                            var mimeType = fileDataEl.TryGetProperty("mimeType", out var mimeEl)
                                ? mimeEl.GetString() ?? "application/octet-stream"
                                : "application/octet-stream";
                            var fileUri = fileDataEl.TryGetProperty("fileUri", out var uriEl)
                                ? uriEl.GetString() ?? ""
                                : "";

                            parts.Add(new ImagePart(mimeType) { Url = fileUri });
                            capabilities |= RequestCapabilities.Vision;
                            capabilities |= RequestCapabilities.RemoteImageUrl;
                            continue;
                        }

                        if (partEl.TryGetProperty("functionCall", out var functionCallEl))
                        {
                            var name = functionCallEl.TryGetProperty("name", out var nameEl)
                                ? nameEl.GetString() ?? ""
                                : "";
                            var argsJson = functionCallEl.TryGetProperty("args", out var argsEl)
                                ? argsEl.GetRawText()
                                : "{}";

                            var id = $"{name}_{toolCallCounter++}";
                            parts.Add(new ToolUsePart(id, name, argsJson));
                            namesInTurn.Add(name);

                            if (!pendingToolCallIdsByName.TryGetValue(name, out var queue))
                            {
                                queue = new Queue<string>();
                                pendingToolCallIdsByName[name] = queue;
                            }

                            queue.Enqueue(id);
                            continue;
                        }

                        if (partEl.TryGetProperty("functionResponse", out var functionResponseEl))
                        {
                            var name = functionResponseEl.TryGetProperty("name", out var nameEl)
                                ? nameEl.GetString() ?? ""
                                : "";
                            var responseJson = functionResponseEl.TryGetProperty("response", out var responseEl)
                                ? responseEl.GetRawText()
                                : "{}";

                            var toolUseId = pendingToolCallIdsByName.TryGetValue(name, out var pending) && pending.Count > 0
                                ? pending.Dequeue()
                                : $"{name}_{toolCallCounter++}";

                            toolResultParts.Add(new ToolResultPart(toolUseId, [new TextPart(responseJson)]));
                        }
                    }

                    if (namesInTurn.Count != namesInTurn.Distinct().Count())
                    {
                        capabilities |= RequestCapabilities.ParallelSameTool;
                    }
                }

                foreach (var toolResult in toolResultParts)
                {
                    messages.Add(new Message(Role.Tool, [toolResult]));
                }

                if (parts.Count > 0)
                {
                    messages.Add(new Message(role, parts));
                }
            }
        }

        var tools = new List<Tool>();
        if (body.TryGetProperty("tools", out var toolsEl) && toolsEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var toolEl in toolsEl.EnumerateArray())
            {
                if (!toolEl.TryGetProperty("functionDeclarations", out var functionDeclarationsEl) ||
                    functionDeclarationsEl.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var functionDeclarationEl in functionDeclarationsEl.EnumerateArray())
                {
                    var name = functionDeclarationEl.TryGetProperty("name", out var nameEl)
                        ? nameEl.GetString() ?? ""
                        : "";
                    var description = functionDeclarationEl.TryGetProperty("description", out var descEl)
                        ? descEl.GetString() ?? ""
                        : "";
                    var parameters = functionDeclarationEl.TryGetProperty("parameters", out var paramsEl)
                        ? paramsEl.GetRawText()
                        : "{}";

                    tools.Add(new Tool(name, description, parameters));
                }
            }
        }

        if (tools.Count > 0)
        {
            capabilities |= RequestCapabilities.Tools;
        }

        ToolChoice? toolChoice = null;
        if (body.TryGetProperty("toolConfig", out var toolConfigEl) &&
            toolConfigEl.TryGetProperty("functionCallingConfig", out var functionCallingConfigEl))
        {
            var mode = functionCallingConfigEl.TryGetProperty("mode", out var modeEl) ? modeEl.GetString() : null;
            var allowedNames = functionCallingConfigEl.TryGetProperty("allowedFunctionNames", out var allowedEl) &&
                                allowedEl.ValueKind == JsonValueKind.Array
                ? allowedEl.EnumerateArray().Select(e => e.GetString() ?? "").Where(s => s.Length > 0).ToList()
                : [];

            toolChoice = mode switch
            {
                "NONE" => Core.Model.ToolChoice.None,
                "ANY" when allowedNames.Count == 1 => Core.Model.ToolChoice.ForTool(allowedNames[0]),
                "ANY" => Core.Model.ToolChoice.Any,
                "AUTO" => Core.Model.ToolChoice.Auto,
                _ => Core.Model.ToolChoice.Auto,
            };
        }

        int? maxTokens = null;
        double? temperature = null;
        ThinkingConfig? thinking = null;

        if (body.TryGetProperty("generationConfig", out var generationConfigEl))
        {
            if (generationConfigEl.TryGetProperty("maxOutputTokens", out var maxTokensEl) &&
                maxTokensEl.ValueKind == JsonValueKind.Number)
            {
                maxTokens = maxTokensEl.GetInt32();
            }

            if (generationConfigEl.TryGetProperty("temperature", out var temperatureEl) &&
                temperatureEl.ValueKind == JsonValueKind.Number)
            {
                temperature = temperatureEl.GetDouble();
            }

            if (generationConfigEl.TryGetProperty("thinkingConfig", out var thinkingConfigEl))
            {
                int? budgetTokens = thinkingConfigEl.TryGetProperty("thinkingBudget", out var budgetEl) &&
                                     budgetEl.ValueKind == JsonValueKind.Number
                    ? budgetEl.GetInt32()
                    : null;
                var includeThoughts = thinkingConfigEl.TryGetProperty("includeThoughts", out var includeEl) &&
                                       includeEl.ValueKind == JsonValueKind.True;

                thinking = new ThinkingConfig
                {
                    Enabled = includeThoughts || budgetTokens is > 0,
                    BudgetTokens = budgetTokens,
                };

                capabilities |= RequestCapabilities.Thinking;
                if (budgetTokens is not null)
                {
                    capabilities |= RequestCapabilities.NumericReasoningBudget;
                }
            }
        }

        var model = body.TryGetProperty("model", out var modelEl) ? modelEl.GetString() : null;

        return new ChatRequest
        {
            Model = model ?? pathModel,
            System = systemParts,
            Messages = messages,
            Tools = tools,
            ToolChoice = toolChoice,
            Thinking = thinking,
            Stream = false,
            MaxTokens = maxTokens,
            Temperature = temperature,
            CapabilitiesUsed = capabilities,
            OriginFormat = ClientFormat.Gemini,
        };
    }

    public HttpRequestMessage FromInternal(ChatRequest request, ModelRef model)
    {
        var root = new JsonObject
        {
            ["contents"] = BuildContentsArray(request.Messages),
        };

        if (request.System.Count > 0)
        {
            root["systemInstruction"] = new JsonObject
            {
                ["parts"] = new JsonArray(
                    request.System.Select(p => (JsonNode)new JsonObject { ["text"] = p.Text }).ToArray()),
            };
        }

        if (request.Tools.Count > 0)
        {
            root["tools"] = new JsonArray(
                new JsonObject { ["functionDeclarations"] = BuildFunctionDeclarationsArray(request.Tools) });
        }

        if (request.ToolChoice is { } toolChoice)
        {
            root["toolConfig"] = new JsonObject { ["functionCallingConfig"] = BuildFunctionCallingConfig(toolChoice) };
        }

        var generationConfig = BuildGenerationConfig(request);
        if (generationConfig is not null)
        {
            root["generationConfig"] = generationConfig;
        }

        var suffix = request.Stream
            ? $"v1beta/models/{model.ModelId}:streamGenerateContent?alt=sse"
            : $"v1beta/models/{model.ModelId}:generateContent";

        return new HttpRequestMessage(HttpMethod.Post, new Uri(suffix, UriKind.Relative))
        {
            Content = new StringContent(root.ToJsonString(), Encoding.UTF8, "application/json"),
        };
    }

    public JsonElement ToClientResponse(ChatResponse response, ModelDecision receipt)
    {
        var parts = new JsonArray();
        foreach (var part in response.Content)
        {
            switch (part)
            {
                case TextPart { Text.Length: > 0 } text:
                    parts.Add(new JsonObject { ["text"] = text.Text });
                    break;
                case ToolUsePart toolUse:
                    parts.Add(new JsonObject
                    {
                        ["functionCall"] = new JsonObject
                        {
                            ["name"] = toolUse.Name,
                            ["args"] = ParseArgsOrEmpty(toolUse.InputJson),
                        },
                    });
                    break;
            }
        }

        var candidate = new JsonObject
        {
            ["content"] = new JsonObject { ["parts"] = parts, ["role"] = "model" },
            ["finishReason"] = MapFinishReason(response.StopReason),
            ["index"] = 0,
        };

        var root = new JsonObject
        {
            ["candidates"] = new JsonArray(candidate),
            ["usageMetadata"] = new JsonObject
            {
                ["promptTokenCount"] = response.Usage.InputTokens,
                ["candidatesTokenCount"] = response.Usage.OutputTokens,
                ["totalTokenCount"] = response.Usage.InputTokens + response.Usage.OutputTokens,
            },
        };

        using var doc = JsonDocument.Parse(root.ToJsonString());
        return doc.RootElement.Clone();
    }

    public IAsyncEnumerable<SseItem<string>> ToClientStream(
        IAsyncEnumerable<NeutralStreamEvent> events,
        ModelDecision receipt,
        CancellationToken cancellationToken) =>
        GeminiStream.ToClientStream(events, receipt, cancellationToken);

    /// <summary>Shared with <see cref="GeminiStream"/> so non-streaming and streaming finish reasons agree.</summary>
    internal static string MapFinishReason(StopReason reason) => reason switch
    {
        StopReason.EndTurn => "STOP",
        StopReason.ToolUse => "STOP",
        StopReason.MaxTokens => "MAX_TOKENS",
        StopReason.StopSequence => "STOP",
        StopReason.Refusal => "SAFETY",
        StopReason.Error => "OTHER",
        _ => "STOP",
    };

    private static Role MapRole(string? role) => role switch
    {
        "model" => Role.Assistant,
        "user" => Role.User,
        _ => Role.User,
    };

    private static List<TextPart> ExtractTextParts(JsonElement contentEl)
    {
        var parts = new List<TextPart>();

        if (!contentEl.TryGetProperty("parts", out var partsEl) || partsEl.ValueKind != JsonValueKind.Array)
        {
            return parts;
        }

        foreach (var partEl in partsEl.EnumerateArray())
        {
            if (partEl.TryGetProperty("text", out var textEl))
            {
                var text = textEl.GetString();
                if (!string.IsNullOrEmpty(text))
                {
                    parts.Add(new TextPart(text));
                }
            }
        }

        return parts;
    }

    private static JsonArray BuildContentsArray(IReadOnlyList<Message> messages)
    {
        var array = new JsonArray();

        foreach (var message in messages)
        {
            if (message.Role == Role.Tool)
            {
                foreach (var toolResult in message.Parts.OfType<ToolResultPart>())
                {
                    array.Add(new JsonObject
                    {
                        ["role"] = "user",
                        ["parts"] = new JsonArray(new JsonObject
                        {
                            ["functionResponse"] = new JsonObject
                            {
                                ["name"] = ExtractFunctionName(toolResult.ToolUseId),
                                ["response"] = BuildFunctionResponseBody(toolResult),
                            },
                        }),
                    });
                }

                continue;
            }

            var partsArray = BuildPartsArray(message.Parts);
            if (partsArray.Count == 0)
            {
                continue;
            }

            array.Add(new JsonObject
            {
                ["role"] = message.Role == Role.Assistant ? "model" : "user",
                ["parts"] = partsArray,
            });
        }

        return array;
    }

    private static JsonArray BuildPartsArray(IReadOnlyList<ContentPart> parts)
    {
        var array = new JsonArray();

        foreach (var part in parts)
        {
            switch (part)
            {
                case TextPart text:
                    array.Add(new JsonObject { ["text"] = text.Text });
                    break;
                case ImagePart image:
                    array.Add(BuildImagePartNode(image));
                    break;
                case ToolUsePart toolUse:
                    array.Add(new JsonObject
                    {
                        ["functionCall"] = new JsonObject
                        {
                            ["name"] = toolUse.Name,
                            ["args"] = ParseArgsOrEmpty(toolUse.InputJson),
                        },
                    });
                    break;
            }
        }

        return array;
    }

    private static JsonObject BuildImagePartNode(ImagePart image)
    {
        if (image.Url is not null)
        {
            return new JsonObject
            {
                ["fileData"] = new JsonObject { ["mimeType"] = image.MediaType, ["fileUri"] = image.Url },
            };
        }

        return new JsonObject
        {
            ["inlineData"] = new JsonObject { ["mimeType"] = image.MediaType, ["data"] = image.Base64 ?? "" },
        };
    }

    /// <summary>
    /// Best-effort recovery of the function name from a synthesized <c>{name}_{index}</c> tool-use
    /// id (see <see cref="ToInternal"/>) — Gemini's <c>functionResponse</c> needs a name, but the
    /// neutral <see cref="ToolResultPart"/> only carries the id it was correlated against.
    /// </summary>
    private static string ExtractFunctionName(string toolUseId)
    {
        var lastUnderscore = toolUseId.LastIndexOf('_');
        return lastUnderscore > 0 ? toolUseId[..lastUnderscore] : toolUseId;
    }

    private static JsonNode? BuildFunctionResponseBody(ToolResultPart toolResult)
    {
        var text = string.Concat(toolResult.Content.OfType<TextPart>().Select(t => t.Text));
        if (string.IsNullOrEmpty(text))
        {
            return new JsonObject();
        }

        try
        {
            return JsonNode.Parse(text);
        }
        catch (JsonException)
        {
            return new JsonObject { ["result"] = text };
        }
    }

    private static JsonNode? ParseArgsOrEmpty(string inputJson)
    {
        try
        {
            return JsonNode.Parse(string.IsNullOrEmpty(inputJson) ? "{}" : inputJson);
        }
        catch (JsonException)
        {
            return new JsonObject();
        }
    }

    private static JsonArray BuildFunctionDeclarationsArray(IReadOnlyList<Tool> tools)
    {
        var array = new JsonArray();
        foreach (var tool in tools)
        {
            array.Add(new JsonObject
            {
                ["name"] = tool.Name,
                ["description"] = tool.Description,
                ["parameters"] = JsonNode.Parse(tool.JsonSchema),
            });
        }

        return array;
    }

    private static JsonObject BuildFunctionCallingConfig(ToolChoice toolChoice) => toolChoice.Kind switch
    {
        ToolChoiceKind.Auto => new JsonObject { ["mode"] = "AUTO" },
        ToolChoiceKind.None => new JsonObject { ["mode"] = "NONE" },
        ToolChoiceKind.Any => new JsonObject { ["mode"] = "ANY" },
        ToolChoiceKind.Specific => new JsonObject
        {
            ["mode"] = "ANY",
            ["allowedFunctionNames"] = new JsonArray(JsonValue.Create(toolChoice.Name)),
        },
        _ => new JsonObject { ["mode"] = "AUTO" },
    };

    private static JsonObject? BuildGenerationConfig(ChatRequest request)
    {
        if (request.MaxTokens is null && request.Temperature is null && request.Thinking is null)
        {
            return null;
        }

        var obj = new JsonObject();

        if (request.MaxTokens is { } maxTokens)
        {
            obj["maxOutputTokens"] = maxTokens;
        }

        if (request.Temperature is { } temperature)
        {
            obj["temperature"] = temperature;
        }

        if (request.Thinking is { } thinking)
        {
            var thinkingObj = new JsonObject { ["includeThoughts"] = thinking.Enabled };
            if (thinking.BudgetTokens is { } budgetTokens)
            {
                thinkingObj["thinkingBudget"] = budgetTokens;
            }

            obj["thinkingConfig"] = thinkingObj;
        }

        return obj;
    }
}
