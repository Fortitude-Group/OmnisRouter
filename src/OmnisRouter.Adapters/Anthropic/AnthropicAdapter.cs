using System.Net.Http;
using System.Net.ServerSentEvents;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using OmnisRouter.Core.Abstractions;
using OmnisRouter.Core.Model;
using OmnisRouter.Core.Routing;

namespace OmnisRouter.Adapters.Anthropic;

/// <summary>
/// CLIENT-facing Anthropic Messages wire adapter (see contracts/wire-formats.md and research.md R2).
/// Owns parsing an inbound Anthropic <c>POST /v1/messages</c> request into the neutral
/// <see cref="ChatRequest"/> and rendering neutral responses/streams back into Anthropic shape for
/// the client. Does <b>not</b> call upstream providers — a separate <c>IUpstreamClient</c> owns
/// the chosen-provider wire I/O; <see cref="FromInternal"/> exists only for interface
/// completeness (nothing in this project dispatches it).
/// </summary>
public sealed class AnthropicAdapter : IFormatAdapter
{
    /// <summary>
    /// Deterministic id stand-in so non-streaming and streaming responses are byte-stable for
    /// golden-file tests (never derived from a clock or RNG).
    /// </summary>
    private const string DeterministicId = "msg_omnisrouter";

    /// <summary>Anthropic requires <c>max_tokens</c> on every request; substituted when the neutral request omits it.</summary>
    private const int DefaultMaxTokens = 4096;

    public ClientFormat Format => ClientFormat.Anthropic;

    public ChatRequest ToInternal(JsonElement body, string? pathModel = null)
    {
        var systemParts = new List<TextPart>();
        if (body.TryGetProperty("system", out var systemEl))
        {
            systemParts.AddRange(ParseSystem(systemEl));
        }

        var messages = new List<Message>();
        var capabilities = RequestCapabilities.None;
        var thinkingSignatureSeen = false;
        var thinkingBlockSeen = false;

        if (body.TryGetProperty("messages", out var messagesEl) && messagesEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var messageEl in messagesEl.EnumerateArray())
            {
                var roleStr = messageEl.TryGetProperty("role", out var roleEl) ? roleEl.GetString() : null;
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
                        foreach (var blockEl in contentEl.EnumerateArray())
                        {
                            var part = ParseContentBlock(blockEl, ref capabilities, ref thinkingSignatureSeen);
                            if (part is null)
                            {
                                continue;
                            }

                            parts.Add(part);
                            if (part is ThinkingPart)
                            {
                                thinkingBlockSeen = true;
                            }
                        }
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
                var name = toolEl.TryGetProperty("name", out var nameEl) ? nameEl.GetString() ?? "" : "";
                var description = toolEl.TryGetProperty("description", out var descEl) ? descEl.GetString() ?? "" : "";
                var schema = toolEl.TryGetProperty("input_schema", out var schemaEl) ? schemaEl.GetRawText() : "{}";
                var strict = toolEl.TryGetProperty("strict", out var strictEl) && strictEl.ValueKind == JsonValueKind.True;

                tools.Add(new Tool(name, description, schema) { Strict = strict });
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
        if (body.TryGetProperty("tool_choice", out var toolChoiceEl) && toolChoiceEl.ValueKind == JsonValueKind.Object)
        {
            var kind = toolChoiceEl.TryGetProperty("type", out var kindEl) ? kindEl.GetString() : null;
            toolChoice = kind switch
            {
                "auto" => ToolChoice.Auto,
                "any" => ToolChoice.Any,
                "none" => ToolChoice.None,
                "tool" => ToolChoice.ForTool(
                    toolChoiceEl.TryGetProperty("name", out var tcNameEl) ? tcNameEl.GetString() ?? "" : ""),
                _ => null,
            };
        }

        ThinkingConfig? thinkingConfig = null;
        if (body.TryGetProperty("thinking", out var thinkingEl) && thinkingEl.ValueKind == JsonValueKind.Object)
        {
            var type = thinkingEl.TryGetProperty("type", out var typeEl) ? typeEl.GetString() : null;
            int? budgetTokens = thinkingEl.TryGetProperty("budget_tokens", out var budgetEl) &&
                                 budgetEl.ValueKind == JsonValueKind.Number
                ? budgetEl.GetInt32()
                : null;

            thinkingConfig = new ThinkingConfig { Enabled = type == "enabled", BudgetTokens = budgetTokens };

            if (thinkingConfig.Enabled)
            {
                thinkingBlockSeen = true;
            }

            if (budgetTokens.HasValue)
            {
                capabilities |= RequestCapabilities.NumericReasoningBudget;
            }
        }

        if (thinkingBlockSeen)
        {
            capabilities |= RequestCapabilities.Thinking;
        }

        if (thinkingSignatureSeen)
        {
            capabilities |= RequestCapabilities.ThinkingWithSignature;
        }

        var hasGuaranteedCachePin = systemParts.Any(p => p.Cache?.Ttl == CacheTtl.OneHour) ||
                                     messages.SelectMany(m => m.Parts).OfType<TextPart>()
                                         .Any(p => p.Cache?.Ttl == CacheTtl.OneHour);
        if (hasGuaranteedCachePin)
        {
            capabilities |= RequestCapabilities.CachePinGuaranteed;
        }

        var model = body.TryGetProperty("model", out var modelEl) ? modelEl.GetString() : null;
        var stream = body.TryGetProperty("stream", out var streamEl) && streamEl.ValueKind == JsonValueKind.True;

        int? maxTokens = body.TryGetProperty("max_tokens", out var maxTokensEl) &&
                         maxTokensEl.ValueKind == JsonValueKind.Number
            ? maxTokensEl.GetInt32()
            : null;

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
            Thinking = thinkingConfig,
            Stream = stream,
            MaxTokens = maxTokens,
            Temperature = temperature,
            CapabilitiesUsed = capabilities,
            OriginFormat = ClientFormat.Anthropic,
        };
    }

    public HttpRequestMessage FromInternal(ChatRequest request, ModelRef model)
    {
        var root = new JsonObject
        {
            ["model"] = model.ModelId,
            ["max_tokens"] = request.MaxTokens ?? DefaultMaxTokens,
            ["messages"] = BuildMessagesArray(request),
            ["stream"] = request.Stream,
        };

        var system = BuildSystem(request.System);
        if (system is not null)
        {
            root["system"] = system;
        }

        if (request.Tools.Count > 0)
        {
            root["tools"] = BuildToolsArray(request.Tools);
        }

        if (request.ToolChoice is { } toolChoice)
        {
            root["tool_choice"] = BuildToolChoice(toolChoice);
        }

        if (request.Temperature is { } temperature)
        {
            root["temperature"] = temperature;
        }

        if (request.Thinking is { } thinking)
        {
            root["thinking"] = new JsonObject
            {
                ["type"] = thinking.Enabled ? "enabled" : "disabled",
                ["budget_tokens"] = thinking.BudgetTokens,
            };
        }

        return new HttpRequestMessage(HttpMethod.Post, new Uri("v1/messages", UriKind.Relative))
        {
            Content = new StringContent(root.ToJsonString(), Encoding.UTF8, "application/json"),
        };
    }

    public JsonElement ToClientResponse(ChatResponse response, ModelDecision receipt)
    {
        var contentArray = new JsonArray();
        foreach (var part in response.Content)
        {
            var block = BuildResponseContentBlock(part);
            if (block is not null)
            {
                contentArray.Add(block);
            }
        }

        var root = new JsonObject
        {
            ["id"] = DeterministicId,
            ["type"] = "message",
            ["role"] = "assistant",
            ["model"] = receipt.Chosen.ModelId,
            ["content"] = contentArray,
            ["stop_reason"] = MapStopReason(response.StopReason),
            ["stop_sequence"] = null,
            ["usage"] = new JsonObject
            {
                ["input_tokens"] = response.Usage.InputTokens,
                ["output_tokens"] = response.Usage.OutputTokens,
                ["cache_creation_input_tokens"] = response.Usage.CacheCreationTokens,
                ["cache_read_input_tokens"] = response.Usage.CacheReadTokens,
            },
        };

        using var doc = JsonDocument.Parse(root.ToJsonString());
        return doc.RootElement.Clone();
    }

    public IAsyncEnumerable<SseItem<string>> ToClientStream(
        IAsyncEnumerable<NeutralStreamEvent> events,
        ModelDecision receipt,
        CancellationToken cancellationToken) =>
        AnthropicStream.ToClientStream(events, receipt, cancellationToken);

    /// <summary>Shared with <see cref="AnthropicStream"/> so non-streaming and streaming stop reasons agree.</summary>
    internal static string MapStopReason(StopReason reason) => reason switch
    {
        StopReason.EndTurn => "end_turn",
        StopReason.ToolUse => "tool_use",
        StopReason.MaxTokens => "max_tokens",
        StopReason.StopSequence => "stop_sequence",
        StopReason.Refusal => "refusal",
        StopReason.Error => "end_turn",
        _ => "end_turn",
    };

    private static JsonObject? BuildResponseContentBlock(ContentPart part) => part switch
    {
        TextPart text => new JsonObject { ["type"] = "text", ["text"] = text.Text },
        ToolUsePart toolUse => new JsonObject
        {
            ["type"] = "tool_use",
            ["id"] = toolUse.Id,
            ["name"] = toolUse.Name,
            ["input"] = JsonNode.Parse(string.IsNullOrEmpty(toolUse.InputJson) ? "{}" : toolUse.InputJson),
        },
        ThinkingPart { Redacted: true } thinking => new JsonObject { ["type"] = "redacted_thinking", ["data"] = thinking.Signature },
        ThinkingPart thinking => new JsonObject
        {
            ["type"] = "thinking",
            ["thinking"] = thinking.Text,
            ["signature"] = thinking.Signature,
        },
        _ => null,
    };

    private static Role MapRole(string? role) => role switch
    {
        "assistant" => Role.Assistant,
        _ => Role.User,
    };

    private static List<TextPart> ParseSystem(JsonElement systemEl)
    {
        var parts = new List<TextPart>();

        if (systemEl.ValueKind == JsonValueKind.String)
        {
            var text = systemEl.GetString();
            if (!string.IsNullOrEmpty(text))
            {
                parts.Add(new TextPart(text));
            }
        }
        else if (systemEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var blockEl in systemEl.EnumerateArray())
            {
                var type = blockEl.TryGetProperty("type", out var typeEl) ? typeEl.GetString() : null;
                if (type != "text")
                {
                    continue;
                }

                var text = blockEl.TryGetProperty("text", out var textEl) ? textEl.GetString() ?? "" : "";
                parts.Add(new TextPart(text) { Cache = ParseCacheControl(blockEl) });
            }
        }

        return parts;
    }

    /// <summary>
    /// Parses one Anthropic content block into a neutral <see cref="ContentPart"/>, recursing into
    /// nested <c>tool_result</c> content. Mutates <paramref name="capabilities"/> (Vision) and
    /// <paramref name="thinkingSignatureSeen"/> in place since the caller derives request-level
    /// capabilities from every block across every message.
    /// </summary>
    private static ContentPart? ParseContentBlock(
        JsonElement blockEl, ref RequestCapabilities capabilities, ref bool thinkingSignatureSeen)
    {
        var type = blockEl.TryGetProperty("type", out var typeEl) ? typeEl.GetString() : null;

        switch (type)
        {
            case "text":
            {
                var text = blockEl.TryGetProperty("text", out var textEl) ? textEl.GetString() ?? "" : "";
                return new TextPart(text) { Cache = ParseCacheControl(blockEl) };
            }

            case "image":
            {
                if (blockEl.TryGetProperty("source", out var sourceEl) &&
                    sourceEl.TryGetProperty("type", out var sourceTypeEl) &&
                    sourceTypeEl.GetString() == "base64")
                {
                    var mediaType = sourceEl.TryGetProperty("media_type", out var mtEl)
                        ? mtEl.GetString() ?? "image/png"
                        : "image/png";
                    var data = sourceEl.TryGetProperty("data", out var dataEl) ? dataEl.GetString() ?? "" : "";

                    capabilities |= RequestCapabilities.Vision;
                    return new ImagePart(mediaType) { Base64 = data };
                }

                return null;
            }

            case "tool_use":
            {
                var id = blockEl.TryGetProperty("id", out var idEl) ? idEl.GetString() ?? "" : "";
                var name = blockEl.TryGetProperty("name", out var nameEl) ? nameEl.GetString() ?? "" : "";
                var input = blockEl.TryGetProperty("input", out var inputEl) ? inputEl.GetRawText() : "{}";
                return new ToolUsePart(id, name, input);
            }

            case "tool_result":
            {
                var toolUseId = blockEl.TryGetProperty("tool_use_id", out var tuEl) ? tuEl.GetString() ?? "" : "";
                var isError = blockEl.TryGetProperty("is_error", out var errEl) && errEl.ValueKind == JsonValueKind.True;
                var resultParts = new List<ContentPart>();

                if (blockEl.TryGetProperty("content", out var contentEl))
                {
                    if (contentEl.ValueKind == JsonValueKind.String)
                    {
                        var text = contentEl.GetString();
                        if (!string.IsNullOrEmpty(text))
                        {
                            resultParts.Add(new TextPart(text));
                        }
                    }
                    else if (contentEl.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var innerBlockEl in contentEl.EnumerateArray())
                        {
                            var innerPart = ParseContentBlock(innerBlockEl, ref capabilities, ref thinkingSignatureSeen);
                            if (innerPart is not null)
                            {
                                resultParts.Add(innerPart);
                            }
                        }
                    }
                }

                return new ToolResultPart(toolUseId, resultParts) { IsError = isError };
            }

            case "thinking":
            {
                var text = blockEl.TryGetProperty("thinking", out var thinkingTextEl) ? thinkingTextEl.GetString() : null;
                var signature = blockEl.TryGetProperty("signature", out var sigEl) ? sigEl.GetString() : null;
                if (signature is not null)
                {
                    thinkingSignatureSeen = true;
                }

                return new ThinkingPart
                {
                    Text = text,
                    Signature = signature,
                    Redacted = false,
                    OriginProvider = Provider.Anthropic,
                };
            }

            case "redacted_thinking":
            {
                // Anthropic's redacted_thinking block carries an opaque encrypted `data` payload
                // (not a `signature`); the neutral ThinkingPart has no dedicated slot for that
                // payload, so it rides in Signature — the only opaque provider-bound string field
                // available — alongside Redacted=true.
                var data = blockEl.TryGetProperty("data", out var dataEl) ? dataEl.GetString() : null;
                return new ThinkingPart { Signature = data, Redacted = true, OriginProvider = Provider.Anthropic };
            }

            default:
                return null;
        }
    }

    private static CacheDirective? ParseCacheControl(JsonElement blockEl)
    {
        if (!blockEl.TryGetProperty("cache_control", out var cacheEl) || cacheEl.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var ttl = cacheEl.TryGetProperty("ttl", out var ttlEl) ? ttlEl.GetString() : null;
        return new CacheDirective(ttl == "1h" ? CacheTtl.OneHour : CacheTtl.FiveMinutes);
    }

    private static JsonNode? BuildSystem(IReadOnlyList<TextPart> system)
    {
        if (system.Count == 0)
        {
            return null;
        }

        if (system.All(p => p.Cache is null))
        {
            return JsonValue.Create(string.Concat(system.Select(p => p.Text)));
        }

        var array = new JsonArray();
        foreach (var part in system)
        {
            array.Add(BuildTextBlock(part));
        }

        return array;
    }

    private static JsonObject BuildTextBlock(TextPart part)
    {
        var obj = new JsonObject { ["type"] = "text", ["text"] = part.Text };
        if (part.Cache is { } cache)
        {
            obj["cache_control"] = BuildCacheControl(cache);
        }

        return obj;
    }

    private static JsonObject BuildCacheControl(CacheDirective cache)
    {
        var obj = new JsonObject { ["type"] = "ephemeral" };
        if (cache.Ttl == CacheTtl.OneHour)
        {
            obj["ttl"] = "1h";
        }

        return obj;
    }

    private static JsonArray BuildMessagesArray(ChatRequest request)
    {
        var array = new JsonArray();
        foreach (var message in request.Messages)
        {
            array.Add(BuildMessage(message));
        }

        return array;
    }

    private static JsonObject BuildMessage(Message message) => new()
    {
        ["role"] = message.Role == Role.Assistant ? "assistant" : "user",
        ["content"] = BuildContentArray(message.Parts),
    };

    private static JsonArray BuildContentArray(IReadOnlyList<ContentPart> parts)
    {
        var array = new JsonArray();
        foreach (var part in parts)
        {
            array.Add(BuildContentBlock(part));
        }

        return array;
    }

    private static JsonObject BuildContentBlock(ContentPart part) => part switch
    {
        TextPart text => BuildTextBlock(text),
        ImagePart image => new JsonObject
        {
            ["type"] = "image",
            ["source"] = new JsonObject
            {
                ["type"] = "base64",
                ["media_type"] = image.MediaType,
                ["data"] = image.Base64 ?? "",
            },
        },
        ToolUsePart toolUse => new JsonObject
        {
            ["type"] = "tool_use",
            ["id"] = toolUse.Id,
            ["name"] = toolUse.Name,
            ["input"] = JsonNode.Parse(string.IsNullOrEmpty(toolUse.InputJson) ? "{}" : toolUse.InputJson),
        },
        ToolResultPart toolResult => BuildToolResultBlock(toolResult),
        ThinkingPart { Redacted: true } thinking => new JsonObject { ["type"] = "redacted_thinking", ["data"] = thinking.Signature },
        ThinkingPart thinking => new JsonObject
        {
            ["type"] = "thinking",
            ["thinking"] = thinking.Text,
            ["signature"] = thinking.Signature,
        },
        _ => throw new NotSupportedException($"Unsupported content part: {part.GetType().Name}"),
    };

    private static JsonObject BuildToolResultBlock(ToolResultPart toolResult)
    {
        var obj = new JsonObject
        {
            ["type"] = "tool_result",
            ["tool_use_id"] = toolResult.ToolUseId,
        };

        if (toolResult.IsError)
        {
            obj["is_error"] = true;
        }

        obj["content"] = toolResult.Content is [TextPart singleText]
            ? JsonValue.Create(singleText.Text)
            : BuildContentArray(toolResult.Content);

        return obj;
    }

    private static JsonArray BuildToolsArray(IReadOnlyList<Tool> tools)
    {
        var array = new JsonArray();
        foreach (var tool in tools)
        {
            array.Add(new JsonObject
            {
                ["name"] = tool.Name,
                ["description"] = tool.Description,
                ["input_schema"] = JsonNode.Parse(tool.JsonSchema),
            });
        }

        return array;
    }

    private static JsonNode BuildToolChoice(ToolChoice toolChoice) => toolChoice.Kind switch
    {
        ToolChoiceKind.Auto => new JsonObject { ["type"] = "auto" },
        ToolChoiceKind.Any => new JsonObject { ["type"] = "any" },
        ToolChoiceKind.None => new JsonObject { ["type"] = "none" },
        ToolChoiceKind.Specific => new JsonObject { ["type"] = "tool", ["name"] = toolChoice.Name },
        _ => new JsonObject { ["type"] = "auto" },
    };
}
