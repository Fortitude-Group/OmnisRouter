using System.Net.Http;
using System.Net.ServerSentEvents;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using OmnisRouter.Core.Abstractions;
using OmnisRouter.Core.Model;
using OmnisRouter.Core.Routing;

namespace OmnisRouter.Adapters.OpenAI;

/// <summary>
/// CLIENT-facing OpenAI Chat Completions wire adapter (see contracts/wire-formats.md and
/// research.md R2). Owns parsing an inbound OpenAI request into the neutral
/// <see cref="ChatRequest"/> and rendering neutral responses/streams back into OpenAI shape for
/// the client. Does <b>not</b> call upstream providers — a separate <c>IUpstreamClient</c> owns
/// the chosen-provider wire I/O; <see cref="FromInternal"/> exists only for interface
/// completeness (nothing in this project dispatches it).
/// </summary>
public sealed class OpenAiAdapter : IFormatAdapter
{
    /// <summary>
    /// Deterministic id/created stand-ins so non-streaming and streaming responses are byte-stable
    /// for golden-file tests (never derived from a clock or RNG).
    /// </summary>
    private const string DeterministicId = "chatcmpl-omnisrouter";

    public ClientFormat Format => ClientFormat.OpenAI;

    public ChatRequest ToInternal(JsonElement body, string? pathModel = null)
    {
        var systemParts = new List<TextPart>();
        var messages = new List<Message>();
        var capabilities = RequestCapabilities.None;

        if (body.TryGetProperty("messages", out var messagesEl) && messagesEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var messageEl in messagesEl.EnumerateArray())
            {
                var roleStr = messageEl.TryGetProperty("role", out var roleEl) ? roleEl.GetString() : null;

                if (roleStr == "system")
                {
                    systemParts.AddRange(ExtractTextParts(messageEl));
                    continue;
                }

                if (roleStr == "tool")
                {
                    var toolCallId = messageEl.TryGetProperty("tool_call_id", out var toolCallIdEl)
                        ? toolCallIdEl.GetString() ?? ""
                        : "";
                    var resultText = ExtractContentAsText(messageEl);
                    messages.Add(new Message(
                        Role.Tool,
                        [new ToolResultPart(toolCallId, [new TextPart(resultText)])]));
                    continue;
                }

                var parts = new List<ContentPart>();

                if (messageEl.TryGetProperty("content", out var contentEl))
                {
                    if (contentEl.ValueKind == JsonValueKind.String)
                    {
                        var text = contentEl.GetString();
                        if (!string.IsNullOrEmpty(text))
                        {
                            parts.Add(new TextPart(text));
                        }
                    }
                    else if (contentEl.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var partEl in contentEl.EnumerateArray())
                        {
                            var partType = partEl.TryGetProperty("type", out var typeEl) ? typeEl.GetString() : null;

                            if (partType == "text" && partEl.TryGetProperty("text", out var textEl))
                            {
                                var text = textEl.GetString() ?? "";
                                parts.Add(new TextPart(text));
                            }
                            else if (partType == "image_url" && partEl.TryGetProperty("image_url", out var imageUrlEl))
                            {
                                var url = imageUrlEl.TryGetProperty("url", out var urlEl) ? urlEl.GetString() ?? "" : "";
                                var detail = imageUrlEl.TryGetProperty("detail", out var detailEl)
                                    ? detailEl.GetString()
                                    : null;

                                var imagePart = ParseImageUrl(url, detail);
                                parts.Add(imagePart);

                                capabilities |= RequestCapabilities.Vision;
                                if (imagePart.Url is not null)
                                {
                                    capabilities |= RequestCapabilities.RemoteImageUrl;
                                }
                            }
                        }
                    }
                }

                if (roleStr == "assistant" &&
                    messageEl.TryGetProperty("tool_calls", out var toolCallsEl) &&
                    toolCallsEl.ValueKind == JsonValueKind.Array)
                {
                    var namesInTurn = new List<string>();

                    foreach (var toolCallEl in toolCallsEl.EnumerateArray())
                    {
                        var id = toolCallEl.TryGetProperty("id", out var idEl) ? idEl.GetString() ?? "" : "";
                        var name = "";
                        var arguments = "{}";

                        if (toolCallEl.TryGetProperty("function", out var functionEl))
                        {
                            name = functionEl.TryGetProperty("name", out var nameEl) ? nameEl.GetString() ?? "" : "";
                            arguments = functionEl.TryGetProperty("arguments", out var argsEl)
                                ? argsEl.GetString() ?? "{}"
                                : "{}";
                        }

                        parts.Add(new ToolUsePart(id, name, arguments));
                        namesInTurn.Add(name);
                    }

                    if (namesInTurn.Count != namesInTurn.Distinct().Count())
                    {
                        capabilities |= RequestCapabilities.ParallelSameTool;
                    }
                }

                messages.Add(new Message(MapRole(roleStr), parts));
            }
        }

        var tools = new List<Tool>();
        if (body.TryGetProperty("tools", out var toolsEl) && toolsEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var toolEl in toolsEl.EnumerateArray())
            {
                if (!toolEl.TryGetProperty("function", out var functionEl))
                {
                    continue;
                }

                var name = functionEl.TryGetProperty("name", out var nameEl) ? nameEl.GetString() ?? "" : "";
                var description = functionEl.TryGetProperty("description", out var descEl)
                    ? descEl.GetString() ?? ""
                    : "";
                var parameters = functionEl.TryGetProperty("parameters", out var paramsEl)
                    ? paramsEl.GetRawText()
                    : "{}";
                var strict = functionEl.TryGetProperty("strict", out var strictEl) &&
                             strictEl.ValueKind == JsonValueKind.True;

                tools.Add(new Tool(name, description, parameters) { Strict = strict });
            }
        }

        if (tools.Count > 0)
        {
            capabilities |= RequestCapabilities.Tools;
        }

        if (tools.Any(t => t.Strict))
        {
            capabilities |= RequestCapabilities.StrictSchema;
        }

        ToolChoice? toolChoice = null;
        if (body.TryGetProperty("tool_choice", out var toolChoiceEl))
        {
            if (toolChoiceEl.ValueKind == JsonValueKind.String)
            {
                toolChoice = toolChoiceEl.GetString() switch
                {
                    "auto" => ToolChoice.Auto,
                    "none" => ToolChoice.None,
                    "required" => ToolChoice.Any,
                    _ => ToolChoice.Auto,
                };
            }
            else if (toolChoiceEl.ValueKind == JsonValueKind.Object &&
                     toolChoiceEl.TryGetProperty("function", out var toolChoiceFnEl) &&
                     toolChoiceFnEl.TryGetProperty("name", out var toolChoiceNameEl))
            {
                toolChoice = ToolChoice.ForTool(toolChoiceNameEl.GetString() ?? "");
            }
        }

        var model = body.TryGetProperty("model", out var modelEl) ? modelEl.GetString() : null;
        var stream = body.TryGetProperty("stream", out var streamEl) && streamEl.ValueKind == JsonValueKind.True;

        int? maxTokens = null;
        if (body.TryGetProperty("max_completion_tokens", out var maxCompletionTokensEl) &&
            maxCompletionTokensEl.ValueKind == JsonValueKind.Number)
        {
            maxTokens = maxCompletionTokensEl.GetInt32();
        }
        else if (body.TryGetProperty("max_tokens", out var maxTokensEl) &&
                 maxTokensEl.ValueKind == JsonValueKind.Number)
        {
            maxTokens = maxTokensEl.GetInt32();
        }

        double? temperature = body.TryGetProperty("temperature", out var temperatureEl) &&
                               temperatureEl.ValueKind == JsonValueKind.Number
            ? temperatureEl.GetDouble()
            : null;

        return new ChatRequest
        {
            Model = model ?? pathModel,
            System = systemParts,
            Messages = messages,
            Tools = tools,
            ToolChoice = toolChoice,
            Stream = stream,
            MaxTokens = maxTokens,
            Temperature = temperature,
            CapabilitiesUsed = capabilities,
            OriginFormat = ClientFormat.OpenAI,
        };
    }

    public HttpRequestMessage FromInternal(ChatRequest request, ModelRef model)
    {
        var root = new JsonObject
        {
            ["model"] = model.ModelId,
            ["messages"] = BuildMessagesArray(request),
            ["stream"] = request.Stream,
        };

        if (request.Tools.Count > 0)
        {
            root["tools"] = BuildToolsArray(request.Tools);
        }

        if (request.ToolChoice is { } toolChoice)
        {
            root["tool_choice"] = BuildToolChoice(toolChoice);
        }

        if (request.MaxTokens is { } maxTokens)
        {
            root["max_tokens"] = maxTokens;
        }

        if (request.Temperature is { } temperature)
        {
            root["temperature"] = temperature;
        }

        return new HttpRequestMessage(HttpMethod.Post, new Uri("chat/completions", UriKind.Relative))
        {
            Content = new StringContent(root.ToJsonString(), Encoding.UTF8, "application/json"),
        };
    }

    public JsonElement ToClientResponse(ChatResponse response, ModelDecision receipt)
    {
        var text = string.Concat(response.Content.OfType<TextPart>().Select(t => t.Text));

        var messageObj = new JsonObject
        {
            ["role"] = "assistant",
            ["content"] = text.Length > 0 ? JsonValue.Create(text) : null,
        };

        var toolUseParts = response.Content.OfType<ToolUsePart>().ToList();
        if (toolUseParts.Count > 0)
        {
            messageObj["tool_calls"] = BuildToolCallsArray(toolUseParts);
        }

        var choice = new JsonObject
        {
            ["index"] = 0,
            ["message"] = messageObj,
            ["finish_reason"] = MapFinishReason(response.StopReason),
        };

        var root = new JsonObject
        {
            ["id"] = DeterministicId,
            ["object"] = "chat.completion",
            ["created"] = 0,
            ["model"] = receipt.Chosen.ModelId,
            ["choices"] = new JsonArray(choice),
            ["usage"] = new JsonObject
            {
                ["prompt_tokens"] = response.Usage.InputTokens,
                ["completion_tokens"] = response.Usage.OutputTokens,
                ["total_tokens"] = response.Usage.InputTokens + response.Usage.OutputTokens,
            },
        };

        using var doc = JsonDocument.Parse(root.ToJsonString());
        return doc.RootElement.Clone();
    }

    public IAsyncEnumerable<SseItem<string>> ToClientStream(
        IAsyncEnumerable<NeutralStreamEvent> events,
        ModelDecision receipt,
        CancellationToken cancellationToken) =>
        OpenAiStream.ToClientStream(events, receipt, cancellationToken);

    /// <summary>Shared with <see cref="OpenAiStream"/> so non-streaming and streaming finish reasons agree.</summary>
    internal static string MapFinishReason(StopReason reason) => reason switch
    {
        StopReason.EndTurn => "stop",
        StopReason.ToolUse => "tool_calls",
        StopReason.MaxTokens => "length",
        StopReason.StopSequence => "stop",
        StopReason.Refusal => "content_filter",
        StopReason.Error => "stop",
        _ => "stop",
    };

    private static Role MapRole(string? role) => role switch
    {
        "system" => Role.System,
        "user" => Role.User,
        "assistant" => Role.Assistant,
        "tool" => Role.Tool,
        _ => Role.User,
    };

    private static List<TextPart> ExtractTextParts(JsonElement messageEl)
    {
        var parts = new List<TextPart>();

        if (!messageEl.TryGetProperty("content", out var contentEl))
        {
            return parts;
        }

        if (contentEl.ValueKind == JsonValueKind.String)
        {
            var text = contentEl.GetString();
            if (!string.IsNullOrEmpty(text))
            {
                parts.Add(new TextPart(text));
            }
        }
        else if (contentEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var partEl in contentEl.EnumerateArray())
            {
                if (partEl.TryGetProperty("type", out var typeEl) &&
                    typeEl.GetString() == "text" &&
                    partEl.TryGetProperty("text", out var textEl))
                {
                    var text = textEl.GetString();
                    if (!string.IsNullOrEmpty(text))
                    {
                        parts.Add(new TextPart(text));
                    }
                }
            }
        }

        return parts;
    }

    private static string ExtractContentAsText(JsonElement messageEl)
    {
        if (!messageEl.TryGetProperty("content", out var contentEl))
        {
            return "";
        }

        if (contentEl.ValueKind == JsonValueKind.String)
        {
            return contentEl.GetString() ?? "";
        }

        if (contentEl.ValueKind == JsonValueKind.Array)
        {
            var sb = new StringBuilder();
            foreach (var partEl in contentEl.EnumerateArray())
            {
                if (partEl.TryGetProperty("type", out var typeEl) &&
                    typeEl.GetString() == "text" &&
                    partEl.TryGetProperty("text", out var textEl))
                {
                    sb.Append(textEl.GetString());
                }
            }

            return sb.ToString();
        }

        return "";
    }

    /// <summary>
    /// Parses an OpenAI <c>image_url.url</c> value — either a <c>data:</c> base64 URI or a remote
    /// URL — into the neutral <see cref="ImagePart"/> (research.md R2: OpenAI is the only format
    /// that supports a remote, unfetched image URL).
    /// </summary>
    private static ImagePart ParseImageUrl(string url, string? detail)
    {
        const string dataPrefix = "data:";

        if (url.StartsWith(dataPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var commaIndex = url.IndexOf(',');
            var header = commaIndex >= 0 ? url[dataPrefix.Length..commaIndex] : url[dataPrefix.Length..];
            var base64 = commaIndex >= 0 ? url[(commaIndex + 1)..] : "";
            var mediaType = header.Split(';')[0];
            if (string.IsNullOrEmpty(mediaType))
            {
                mediaType = "image/png";
            }

            return new ImagePart(mediaType) { Base64 = base64, Detail = detail };
        }

        return new ImagePart(InferMediaTypeFromUrl(url)) { Url = url, Detail = detail };
    }

    private static string InferMediaTypeFromUrl(string url)
    {
        var lower = url.ToLowerInvariant();
        if (lower.EndsWith(".png", StringComparison.Ordinal))
        {
            return "image/png";
        }

        if (lower.EndsWith(".jpg", StringComparison.Ordinal) || lower.EndsWith(".jpeg", StringComparison.Ordinal))
        {
            return "image/jpeg";
        }

        if (lower.EndsWith(".gif", StringComparison.Ordinal))
        {
            return "image/gif";
        }

        if (lower.EndsWith(".webp", StringComparison.Ordinal))
        {
            return "image/webp";
        }

        return "image/jpeg";
    }

    private static JsonArray BuildMessagesArray(ChatRequest request)
    {
        var array = new JsonArray();

        if (request.System.Count > 0)
        {
            var systemText = string.Join("\n\n", request.System.Select(p => p.Text));
            array.Add(new JsonObject { ["role"] = "system", ["content"] = systemText });
        }

        foreach (var message in request.Messages)
        {
            array.Add(BuildMessage(message));
        }

        return array;
    }

    private static JsonObject BuildMessage(Message message)
    {
        var toolResult = message.Parts.OfType<ToolResultPart>().FirstOrDefault();
        if (message.Role == Role.Tool && toolResult is not null)
        {
            var text = string.Concat(toolResult.Content.OfType<TextPart>().Select(t => t.Text));
            return new JsonObject
            {
                ["role"] = "tool",
                ["tool_call_id"] = toolResult.ToolUseId,
                ["content"] = text,
            };
        }

        var roleStr = message.Role switch
        {
            Role.System => "system",
            Role.User => "user",
            Role.Assistant => "assistant",
            Role.Tool => "tool",
            _ => "user",
        };

        var obj = new JsonObject { ["role"] = roleStr };

        var textParts = message.Parts.OfType<TextPart>().ToList();
        var imageParts = message.Parts.OfType<ImagePart>().ToList();
        var toolUseParts = message.Parts.OfType<ToolUsePart>().ToList();

        if (imageParts.Count > 0)
        {
            var contentArray = new JsonArray();
            foreach (var textPart in textParts)
            {
                contentArray.Add(new JsonObject { ["type"] = "text", ["text"] = textPart.Text });
            }

            foreach (var imagePart in imageParts)
            {
                var url = imagePart.Url ?? $"data:{imagePart.MediaType};base64,{imagePart.Base64}";
                var imageUrlObj = new JsonObject { ["url"] = url };
                if (imagePart.Detail is not null)
                {
                    imageUrlObj["detail"] = imagePart.Detail;
                }

                contentArray.Add(new JsonObject { ["type"] = "image_url", ["image_url"] = imageUrlObj });
            }

            obj["content"] = contentArray;
        }
        else if (textParts.Count > 0)
        {
            obj["content"] = string.Concat(textParts.Select(t => t.Text));
        }

        if (toolUseParts.Count > 0)
        {
            obj["tool_calls"] = BuildToolCallsArray(toolUseParts);
        }

        return obj;
    }

    private static JsonArray BuildToolCallsArray(IEnumerable<ToolUsePart> toolUseParts)
    {
        var array = new JsonArray();
        foreach (var toolUse in toolUseParts)
        {
            array.Add(new JsonObject
            {
                ["id"] = toolUse.Id,
                ["type"] = "function",
                ["function"] = new JsonObject
                {
                    ["name"] = toolUse.Name,
                    ["arguments"] = toolUse.InputJson,
                },
            });
        }

        return array;
    }

    private static JsonArray BuildToolsArray(IReadOnlyList<Tool> tools)
    {
        var array = new JsonArray();
        foreach (var tool in tools)
        {
            var functionObj = new JsonObject
            {
                ["name"] = tool.Name,
                ["description"] = tool.Description,
                ["parameters"] = JsonNode.Parse(tool.JsonSchema),
            };

            if (tool.Strict)
            {
                functionObj["strict"] = true;
            }

            array.Add(new JsonObject { ["type"] = "function", ["function"] = functionObj });
        }

        return array;
    }

    private static JsonNode? BuildToolChoice(ToolChoice toolChoice) => toolChoice.Kind switch
    {
        ToolChoiceKind.Auto => JsonValue.Create("auto"),
        ToolChoiceKind.None => JsonValue.Create("none"),
        ToolChoiceKind.Any => JsonValue.Create("required"),
        ToolChoiceKind.Specific => new JsonObject
        {
            ["type"] = "function",
            ["function"] = new JsonObject { ["name"] = toolChoice.Name },
        },
        _ => JsonValue.Create("auto"),
    };
}
