using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using OmnisRouter.Core.Model;
using OmnisRouter.Core.Routing;

namespace OmnisRouter.Adapters.Gemini;

/// <summary>
/// Re-frames neutral stream events into Gemini <c>streamGenerateContent?alt=sse</c> frames.
/// Unlike Anthropic's block-bracketed events or OpenAI's flat delta chunks, Gemini streams whole
/// <c>GenerateContentResponse</c> objects per chunk (research.md R2) — there is no dedicated small
/// "delta" wire shape. This re-framer accumulates neutral text into growing per-block state and
/// emits a full snapshot object (all parts assembled so far) on every text delta; tool-call
/// arguments accumulate silently (Gemini function calls arrive atomic, not char-by-char) and are
/// folded into the parts array once complete. The terminal chunk additionally carries
/// <c>finishReason</c> and <c>usageMetadata</c>. There is no <c>[DONE]</c> sentinel in Gemini SSE.
/// </summary>
public static class GeminiStream
{
    public static async IAsyncEnumerable<SseItem<string>> ToClientStream(
        IAsyncEnumerable<NeutralStreamEvent> events,
        ModelDecision receipt,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var blockOrder = new List<int>();
        var textByBlock = new Dictionary<int, StringBuilder>();
        var toolNameByBlock = new Dictionary<int, string>();
        var toolArgsByBlock = new Dictionary<int, StringBuilder>();

        await foreach (var evt in events.WithCancellation(cancellationToken))
        {
            switch (evt)
            {
                case StreamBlockStart { BlockKind: "text" } blockStart:
                    if (!textByBlock.ContainsKey(blockStart.Index))
                    {
                        textByBlock[blockStart.Index] = new StringBuilder();
                        blockOrder.Add(blockStart.Index);
                    }

                    break;

                case StreamBlockStart { BlockKind: "tool_use" } blockStart:
                    if (!toolNameByBlock.ContainsKey(blockStart.Index))
                    {
                        toolNameByBlock[blockStart.Index] = blockStart.ToolName ?? "";
                        toolArgsByBlock[blockStart.Index] = new StringBuilder();
                        blockOrder.Add(blockStart.Index);
                    }

                    break;

                case StreamTextDelta textDelta:
                    if (!textByBlock.TryGetValue(textDelta.Index, out var textSb))
                    {
                        textSb = new StringBuilder();
                        textByBlock[textDelta.Index] = textSb;
                        blockOrder.Add(textDelta.Index);
                    }

                    textSb.Append(textDelta.Text);
                    yield return BuildSnapshotChunk(
                        blockOrder, textByBlock, toolNameByBlock, toolArgsByBlock, finishReason: null, usage: null);
                    break;

                case StreamToolArgsDelta argsDelta:
                    if (toolArgsByBlock.TryGetValue(argsDelta.Index, out var argsSb))
                    {
                        argsSb.Append(argsDelta.PartialJson);
                    }

                    break;

                case StreamMessageStop stop:
                    yield return BuildSnapshotChunk(
                        blockOrder,
                        textByBlock,
                        toolNameByBlock,
                        toolArgsByBlock,
                        finishReason: GeminiAdapter.MapFinishReason(stop.StopReason),
                        usage: stop.Usage);
                    break;

                default:
                    // StreamMessageStart / StreamBlockStop / StreamThinkingDelta / StreamError have no
                    // Gemini whole-object snapshot to (re-)emit beyond what the surrounding
                    // text/tool-arg deltas already produce (see type-level remarks).
                    break;
            }
        }
    }

    private static SseItem<string> BuildSnapshotChunk(
        List<int> blockOrder,
        Dictionary<int, StringBuilder> textByBlock,
        Dictionary<int, string> toolNameByBlock,
        Dictionary<int, StringBuilder> toolArgsByBlock,
        string? finishReason,
        Usage? usage)
    {
        var parts = new JsonArray();

        foreach (var index in blockOrder)
        {
            if (textByBlock.TryGetValue(index, out var textSb))
            {
                parts.Add(new JsonObject { ["text"] = textSb.ToString() });
                continue;
            }

            if (toolNameByBlock.TryGetValue(index, out var name))
            {
                var argsJson = toolArgsByBlock.TryGetValue(index, out var argsSb) ? argsSb.ToString() : "";
                JsonNode? args;
                try
                {
                    args = JsonNode.Parse(string.IsNullOrEmpty(argsJson) ? "{}" : argsJson);
                }
                catch (JsonException)
                {
                    args = new JsonObject();
                }

                parts.Add(new JsonObject
                {
                    ["functionCall"] = new JsonObject { ["name"] = name, ["args"] = args },
                });
            }
        }

        var candidate = new JsonObject
        {
            ["content"] = new JsonObject { ["parts"] = parts, ["role"] = "model" },
            ["index"] = 0,
        };

        if (finishReason is not null)
        {
            candidate["finishReason"] = finishReason;
        }

        var root = new JsonObject { ["candidates"] = new JsonArray(candidate) };

        if (usage is not null)
        {
            root["usageMetadata"] = new JsonObject
            {
                ["promptTokenCount"] = usage.InputTokens,
                ["candidatesTokenCount"] = usage.OutputTokens,
                ["totalTokenCount"] = usage.InputTokens + usage.OutputTokens,
            };
        }

        return new SseItem<string>(root.ToJsonString());
    }
}
