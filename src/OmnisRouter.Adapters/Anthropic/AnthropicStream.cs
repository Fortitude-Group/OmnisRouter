using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using OmnisRouter.Core.Model;
using OmnisRouter.Core.Routing;

namespace OmnisRouter.Adapters.Anthropic;

/// <summary>
/// Re-frames neutral stream events into Anthropic's named-event SSE shape (contracts/wire-formats.md;
/// research.md R2): <c>message_start</c> → (<c>content_block_start</c> / <c>content_block_delta</c> /
/// <c>content_block_stop</c>)* → <c>message_delta</c> → <c>message_stop</c>. Unlike OpenAI's flat
/// unnamed delta chunks, Anthropic's explicit block bracketing means every neutral event — including
/// <see cref="StreamThinkingDelta"/>, which OpenAI Chat Completions has no frame for — maps directly
/// onto a named Anthropic event.
/// </summary>
public static class AnthropicStream
{
    private const string DeterministicId = "msg_omnisrouter";

    public static async IAsyncEnumerable<SseItem<string>> ToClientStream(
        IAsyncEnumerable<NeutralStreamEvent> events,
        ModelDecision receipt,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var modelId = receipt.Chosen.ModelId;

        await foreach (var evt in events.WithCancellation(cancellationToken))
        {
            switch (evt)
            {
                case StreamMessageStart:
                    var messageStart = new JsonObject
                    {
                        ["type"] = "message_start",
                        ["message"] = new JsonObject
                        {
                            ["id"] = DeterministicId,
                            ["type"] = "message",
                            ["role"] = "assistant",
                            ["model"] = modelId,
                            ["content"] = new JsonArray(),
                            ["stop_reason"] = null,
                            ["stop_sequence"] = null,
                            ["usage"] = new JsonObject { ["input_tokens"] = 0, ["output_tokens"] = 0 },
                        },
                    };
                    yield return new SseItem<string>(messageStart.ToJsonString(), "message_start");
                    break;

                case StreamBlockStart blockStart:
                    var contentBlock = blockStart.BlockKind switch
                    {
                        "tool_use" => new JsonObject
                        {
                            ["type"] = "tool_use",
                            ["id"] = blockStart.ToolId,
                            ["name"] = blockStart.ToolName,
                            ["input"] = new JsonObject(),
                        },
                        "thinking" => new JsonObject { ["type"] = "thinking", ["thinking"] = "", ["signature"] = "" },
                        _ => new JsonObject { ["type"] = "text", ["text"] = "" },
                    };

                    var blockStartObj = new JsonObject
                    {
                        ["type"] = "content_block_start",
                        ["index"] = blockStart.Index,
                        ["content_block"] = contentBlock,
                    };
                    yield return new SseItem<string>(blockStartObj.ToJsonString(), "content_block_start");
                    break;

                case StreamTextDelta textDelta:
                    yield return BuildContentBlockDelta(
                        textDelta.Index,
                        new JsonObject { ["type"] = "text_delta", ["text"] = textDelta.Text });
                    break;

                case StreamToolArgsDelta argsDelta:
                    yield return BuildContentBlockDelta(
                        argsDelta.Index,
                        new JsonObject { ["type"] = "input_json_delta", ["partial_json"] = argsDelta.PartialJson });
                    break;

                case StreamThinkingDelta thinkingDelta:
                    if (thinkingDelta.Text is not null)
                    {
                        yield return BuildContentBlockDelta(
                            thinkingDelta.Index,
                            new JsonObject { ["type"] = "thinking_delta", ["thinking"] = thinkingDelta.Text });
                    }

                    if (thinkingDelta.Signature is not null)
                    {
                        yield return BuildContentBlockDelta(
                            thinkingDelta.Index,
                            new JsonObject { ["type"] = "signature_delta", ["signature"] = thinkingDelta.Signature });
                    }

                    break;

                case StreamBlockStop blockStop:
                    var blockStopObj = new JsonObject { ["type"] = "content_block_stop", ["index"] = blockStop.Index };
                    yield return new SseItem<string>(blockStopObj.ToJsonString(), "content_block_stop");
                    break;

                case StreamMessageStop stop:
                    var messageDelta = new JsonObject
                    {
                        ["type"] = "message_delta",
                        ["delta"] = new JsonObject
                        {
                            ["stop_reason"] = AnthropicAdapter.MapStopReason(stop.StopReason),
                            ["stop_sequence"] = null,
                        },
                        ["usage"] = new JsonObject { ["output_tokens"] = stop.Usage.OutputTokens },
                    };
                    yield return new SseItem<string>(messageDelta.ToJsonString(), "message_delta");

                    var messageStop = new JsonObject { ["type"] = "message_stop" };
                    yield return new SseItem<string>(messageStop.ToJsonString(), "message_stop");
                    break;

                case StreamError error:
                    var errorObj = new JsonObject
                    {
                        ["type"] = "error",
                        ["error"] = new JsonObject { ["type"] = error.Type, ["message"] = error.Message },
                    };
                    yield return new SseItem<string>(errorObj.ToJsonString(), "error");
                    break;
            }
        }
    }

    private static SseItem<string> BuildContentBlockDelta(int index, JsonObject delta)
    {
        var obj = new JsonObject
        {
            ["type"] = "content_block_delta",
            ["index"] = index,
            ["delta"] = delta,
        };
        return new SseItem<string>(obj.ToJsonString(), "content_block_delta");
    }
}
