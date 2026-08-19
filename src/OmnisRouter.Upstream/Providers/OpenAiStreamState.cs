using OmnisRouter.Core.Model;

namespace OmnisRouter.Upstream.Providers;

/// <summary>
/// Accumulates OpenAI <c>chat.completion.chunk</c> deltas into neutral stream events. OpenAI keys
/// each tool-call delta by its position in that choice's <c>tool_calls</c> array; this tracks the
/// mapping from that wire index to the neutral content-block index assigned on first sight.
/// </summary>
internal sealed class OpenAiStreamState
{
    private readonly Dictionary<int, int> _toolCallBlockIndexByWireIndex = [];
    private readonly List<int> _openBlockIndexesInOrder = [];
    private int _nextBlockIndex;
    private int? _textBlockIndex;
    private string? _finishReason;
    private Usage? _usage;

    public IEnumerable<NeutralStreamEvent> Process(OpenAiChatCompletionChunk chunk)
    {
        if (chunk.Usage is not null)
        {
            _usage = OpenAiResponseMapper.ToUsage(chunk.Usage);
        }

        var choice = chunk.Choices is { Count: > 0 } choices ? choices[0] : null;
        if (choice is null)
        {
            yield break;
        }

        if (choice.FinishReason is not null)
        {
            _finishReason = choice.FinishReason;
        }

        var delta = choice.Delta;
        if (delta is null)
        {
            yield break;
        }

        if (!string.IsNullOrEmpty(delta.Content))
        {
            if (_textBlockIndex is null)
            {
                _textBlockIndex = OpenBlock();
                yield return new StreamBlockStart(_textBlockIndex.Value, "text");
            }

            yield return new StreamTextDelta(_textBlockIndex.Value, delta.Content);
        }

        if (delta.ToolCalls is not { Count: > 0 } toolCallDeltas)
        {
            yield break;
        }

        foreach (var toolCallDelta in toolCallDeltas)
        {
            if (!_toolCallBlockIndexByWireIndex.TryGetValue(toolCallDelta.Index, out var blockIndex))
            {
                blockIndex = OpenBlock();
                _toolCallBlockIndexByWireIndex[toolCallDelta.Index] = blockIndex;
                yield return new StreamBlockStart(
                    blockIndex, "tool_use", toolCallDelta.Id, toolCallDelta.Function?.Name);
            }

            var argumentsDelta = toolCallDelta.Function?.Arguments;
            if (!string.IsNullOrEmpty(argumentsDelta))
            {
                yield return new StreamToolArgsDelta(blockIndex, argumentsDelta);
            }
        }
    }

    /// <summary>Closes every opened block (in open order) and emits the terminal message-stop event.</summary>
    public IEnumerable<NeutralStreamEvent> Finalize()
    {
        foreach (var blockIndex in _openBlockIndexesInOrder)
        {
            yield return new StreamBlockStop(blockIndex);
        }

        yield return new StreamMessageStop(OpenAiResponseMapper.ToStopReason(_finishReason), _usage ?? new Usage());
    }

    private int OpenBlock()
    {
        var index = _nextBlockIndex++;
        _openBlockIndexesInOrder.Add(index);
        return index;
    }
}
