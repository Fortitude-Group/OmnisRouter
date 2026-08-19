using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using OmnisRouter.Core.Model;
using OmnisRouter.Core.Routing;

namespace OmnisRouter.Adapters.OpenAI;

/// <summary>
/// Re-frames neutral stream events into OpenAI <c>chat.completion.chunk</c> SSE frames, terminated
/// by the literal <c>[DONE]</c> sentinel (contracts/wire-formats.md; research.md R2). OpenAI's
/// streaming granularity is flat, unnamed delta chunks — unlike Anthropic's explicit
/// block-start/stop bracketing — so <see cref="StreamBlockStop"/> and <see cref="StreamThinkingDelta"/>
/// (no Chat Completions thinking item exists) have no frame to re-frame into and are dropped.
/// </summary>
public static class OpenAiStream
{
    private const string DeterministicId = "chatcmpl-omnisrouter";

    public static async IAsyncEnumerable<SseItem<string>> ToClientStream(
        IAsyncEnumerable<NeutralStreamEvent> events,
        ModelDecision receipt,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var modelId = receipt.Chosen.ModelId;

        // OpenAI's tool_calls[] delta is keyed by a dense 0-based "index" per the choice, distinct
        // from the neutral model's per-block Index; map the latter to the former as tool blocks open.
        var toolCallIndexByBlock = new Dictionary<int, int>();
        var nextToolCallIndex = 0;
        var roleSent = false;

        await foreach (var evt in events.WithCancellation(cancellationToken))
        {
            switch (evt)
            {
                case StreamMessageStart:
                    if (!roleSent)
                    {
                        yield return BuildChunk(modelId, new JsonObject { ["role"] = "assistant" }, finishReason: null);
                        roleSent = true;
                    }

                    break;

                case StreamBlockStart blockStart when blockStart.BlockKind == "tool_use":
                    var toolIndex = nextToolCallIndex++;
                    toolCallIndexByBlock[blockStart.Index] = toolIndex;

                    var toolStartDelta = new JsonObject
                    {
                        ["tool_calls"] = new JsonArray(new JsonObject
                        {
                            ["index"] = toolIndex,
                            ["id"] = blockStart.ToolId,
                            ["type"] = "function",
                            ["function"] = new JsonObject
                            {
                                ["name"] = blockStart.ToolName,
                                ["arguments"] = "",
                            },
                        }),
                    };
                    yield return BuildChunk(modelId, toolStartDelta, finishReason: null);
                    break;

                case StreamTextDelta textDelta:
                    yield return BuildChunk(
                        modelId,
                        new JsonObject { ["content"] = textDelta.Text },
                        finishReason: null);
                    break;

                case StreamToolArgsDelta argsDelta:
                    var index = toolCallIndexByBlock.TryGetValue(argsDelta.Index, out var mapped)
                        ? mapped
                        : argsDelta.Index;

                    var argsDeltaObj = new JsonObject
                    {
                        ["tool_calls"] = new JsonArray(new JsonObject
                        {
                            ["index"] = index,
                            ["function"] = new JsonObject { ["arguments"] = argsDelta.PartialJson },
                        }),
                    };
                    yield return BuildChunk(modelId, argsDeltaObj, finishReason: null);
                    break;

                case StreamMessageStop stop:
                    yield return BuildChunk(
                        modelId,
                        new JsonObject(),
                        finishReason: OpenAiAdapter.MapFinishReason(stop.StopReason));
                    break;

                default:
                    // StreamBlockStop / StreamThinkingDelta / StreamError have no Chat Completions
                    // chunk analog (see type-level remarks) — nothing to emit.
                    break;
            }
        }

        yield return new SseItem<string>("[DONE]");
    }

    private static SseItem<string> BuildChunk(string modelId, JsonObject delta, string? finishReason)
    {
        var choice = new JsonObject
        {
            ["index"] = 0,
            ["delta"] = delta,
            ["finish_reason"] = finishReason,
        };

        var chunk = new JsonObject
        {
            ["id"] = DeterministicId,
            ["object"] = "chat.completion.chunk",
            ["created"] = 0,
            ["model"] = modelId,
            ["choices"] = new JsonArray(choice),
        };

        return new SseItem<string>(chunk.ToJsonString());
    }
}
