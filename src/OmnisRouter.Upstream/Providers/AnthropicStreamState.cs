using System.Text.Json;
using OmnisRouter.Core.Model;

namespace OmnisRouter.Upstream.Providers;

/// <summary>
/// Accumulates Anthropic's named-event SSE stream into neutral stream events. Unlike OpenAI's flat
/// unnamed chunks (which require synthesizing block-open/close bracketing), Anthropic already emits
/// explicit <c>content_block_start</c>/<c>content_block_stop</c> events, so this is a straight
/// event-to-event translation with no block-index bookkeeping needed. Usage arrives in two halves —
/// <c>input_tokens</c> (+ cache accounting) on <c>message_start</c>, <c>output_tokens</c> on
/// <c>message_delta</c> — which this accumulates and emits together on <c>message_stop</c>.
/// </summary>
internal sealed class AnthropicStreamState
{
    private int _inputTokens;
    private int _cacheCreationTokens;
    private int _cacheReadTokens;
    private int _outputTokens;
    private StopReason _stopReason = StopReason.EndTurn;

    public IEnumerable<NeutralStreamEvent> Process(string eventType, JsonElement data)
    {
        switch (eventType)
        {
            case "message_start":
                if (data.TryGetProperty("message", out var messageEl) &&
                    messageEl.TryGetProperty("usage", out var startUsageEl))
                {
                    ApplyUsage(startUsageEl);
                }

                break;

            case "content_block_start":
            {
                var index = data.GetProperty("index").GetInt32();
                var block = data.GetProperty("content_block");
                var kind = block.TryGetProperty("type", out var kindEl) ? kindEl.GetString() ?? "text" : "text";
                var toolId = kind == "tool_use" && block.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
                var toolName = kind == "tool_use" && block.TryGetProperty("name", out var nameEl) ? nameEl.GetString() : null;

                return [new StreamBlockStart(index, kind, toolId, toolName)];
            }

            case "content_block_delta":
            {
                var index = data.GetProperty("index").GetInt32();
                var delta = data.GetProperty("delta");
                var deltaType = delta.TryGetProperty("type", out var deltaTypeEl) ? deltaTypeEl.GetString() : null;

                return deltaType switch
                {
                    "text_delta" => [new StreamTextDelta(index, GetString(delta, "text"))],
                    "input_json_delta" => [new StreamToolArgsDelta(index, GetString(delta, "partial_json"))],
                    "thinking_delta" => [new StreamThinkingDelta(index, Text: GetString(delta, "thinking"))],
                    "signature_delta" => [new StreamThinkingDelta(index, Signature: GetString(delta, "signature"))],
                    _ => [],
                };
            }

            case "content_block_stop":
                return [new StreamBlockStop(data.GetProperty("index").GetInt32())];

            case "message_delta":
                if (data.TryGetProperty("delta", out var deltaEl) &&
                    deltaEl.TryGetProperty("stop_reason", out var stopReasonEl) &&
                    stopReasonEl.ValueKind == JsonValueKind.String)
                {
                    _stopReason = AnthropicResponseMapper.ToStopReason(stopReasonEl.GetString());
                }

                if (data.TryGetProperty("usage", out var deltaUsageEl))
                {
                    ApplyUsage(deltaUsageEl);
                }

                break;

            case "message_stop":
                return
                [
                    new StreamMessageStop(
                        _stopReason,
                        new Usage
                        {
                            InputTokens = _inputTokens,
                            OutputTokens = _outputTokens,
                            CacheCreationTokens = _cacheCreationTokens,
                            CacheReadTokens = _cacheReadTokens,
                        }),
                ];
        }

        return [];
    }

    private void ApplyUsage(JsonElement usageEl)
    {
        if (TryGetInt(usageEl, "input_tokens", out var inputTokens))
        {
            _inputTokens = inputTokens;
        }

        if (TryGetInt(usageEl, "output_tokens", out var outputTokens))
        {
            _outputTokens = outputTokens;
        }

        if (TryGetInt(usageEl, "cache_creation_input_tokens", out var cacheCreationTokens))
        {
            _cacheCreationTokens = cacheCreationTokens;
        }

        if (TryGetInt(usageEl, "cache_read_input_tokens", out var cacheReadTokens))
        {
            _cacheReadTokens = cacheReadTokens;
        }
    }

    private static bool TryGetInt(JsonElement obj, string propertyName, out int value)
    {
        if (obj.TryGetProperty(propertyName, out var el) && el.ValueKind == JsonValueKind.Number)
        {
            value = el.GetInt32();
            return true;
        }

        value = 0;
        return false;
    }

    private static string GetString(JsonElement obj, string propertyName) =>
        obj.TryGetProperty(propertyName, out var el) ? el.GetString() ?? "" : "";
}
