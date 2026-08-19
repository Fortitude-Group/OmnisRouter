using System.Text.Json;
using OmnisRouter.Core.Model;

namespace OmnisRouter.Upstream.Providers;

/// <summary>
/// Accumulates Gemini <c>streamGenerateContent</c> whole-object chunks into neutral stream events.
/// Gemini has no explicit block bracketing or per-frame delta wire shape (research.md R2): every
/// chunk is a full <c>GenerateContentResponse</c>. For text, this tracks the text seen so far per
/// part position and defensively computes each new chunk's marginal delta whether the upstream
/// resent the full accumulated text (the previous text is then a prefix of the new one) or sent
/// only the incremental fragment (which is appended outright). Function calls arrive atomic (a
/// complete <c>functionCall</c> in one part) so they are opened and their args emitted the first
/// time they are seen at a given part position, then ignored on any later repeat of that position.
/// </summary>
internal sealed class GeminiStreamState
{
    private readonly List<int> _openBlockIndexesInOrder = [];
    private readonly Dictionary<int, string> _accumulatedTextByBlock = [];
    private readonly HashSet<int> _emittedToolPartPositions = [];
    private int _nextBlockIndex;
    private int? _textBlockIndex;
    private string? _finishReason;
    private Usage? _usage;
    private bool _hasToolCall;

    public IEnumerable<NeutralStreamEvent> Process(GeminiGenerateContentResponse chunk)
    {
        if (chunk.UsageMetadata is not null)
        {
            _usage = GeminiResponseMapper.ToUsage(chunk.UsageMetadata);
        }

        var candidate = chunk.Candidates is { Count: > 0 } candidates ? candidates[0] : null;
        if (candidate is null)
        {
            yield break;
        }

        if (candidate.FinishReason is not null)
        {
            _finishReason = candidate.FinishReason;
        }

        var parts = candidate.Content?.Parts;
        if (parts is null)
        {
            yield break;
        }

        var toolPartPosition = 0;

        foreach (var part in parts)
        {
            if (part.Text is not null)
            {
                if (_textBlockIndex is null)
                {
                    _textBlockIndex = OpenBlock();
                    yield return new StreamBlockStart(_textBlockIndex.Value, "text");
                }

                var blockIndex = _textBlockIndex.Value;
                var previous = _accumulatedTextByBlock.TryGetValue(blockIndex, out var prevText) ? prevText : "";

                string delta;
                string newAccumulated;
                if (part.Text.StartsWith(previous, StringComparison.Ordinal))
                {
                    // Upstream resent the full accumulated text (or repeated it unchanged) — the
                    // marginal delta is whatever was appended since (empty when unchanged).
                    delta = part.Text[previous.Length..];
                    newAccumulated = part.Text;
                }
                else
                {
                    // Upstream sent only the incremental fragment — append it outright.
                    delta = part.Text;
                    newAccumulated = previous + part.Text;
                }

                _accumulatedTextByBlock[blockIndex] = newAccumulated;
                if (delta.Length > 0)
                {
                    yield return new StreamTextDelta(blockIndex, delta);
                }

                continue;
            }

            if (part.FunctionCall is not null)
            {
                var position = toolPartPosition++;
                if (!_emittedToolPartPositions.Add(position))
                {
                    continue;
                }

                _hasToolCall = true;
                var blockIndex = OpenBlock();
                var name = part.FunctionCall.Name ?? "";
                var toolId = $"{name}_{blockIndex}";
                var argsJson = part.FunctionCall.Args.ValueKind == JsonValueKind.Undefined
                    ? "{}"
                    : part.FunctionCall.Args.GetRawText();

                yield return new StreamBlockStart(blockIndex, "tool_use", toolId, name);
                yield return new StreamToolArgsDelta(blockIndex, argsJson);
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

        var stopReason = GeminiResponseMapper.ToStopReason(_finishReason);
        if (stopReason == StopReason.EndTurn && _hasToolCall)
        {
            stopReason = StopReason.ToolUse;
        }

        yield return new StreamMessageStop(stopReason, _usage ?? new Usage());
    }

    private int OpenBlock()
    {
        var index = _nextBlockIndex++;
        _openBlockIndexesInOrder.Add(index);
        return index;
    }
}
