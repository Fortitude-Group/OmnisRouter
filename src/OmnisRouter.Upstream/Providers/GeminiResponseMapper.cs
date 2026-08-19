using System.Text.Json;
using OmnisRouter.Core.Model;

namespace OmnisRouter.Upstream.Providers;

/// <summary>Maps Gemini <c>GenerateContentResponse</c> wire responses/usage/finish-reason into the neutral model.</summary>
internal static class GeminiResponseMapper
{
    public static ChatResponse ToChatResponse(GeminiGenerateContentResponse wire, ModelRef model)
    {
        var candidate = wire.Candidates is { Count: > 0 } candidates ? candidates[0] : null;
        var content = new List<ContentPart>();
        var toolCallIndex = 0;

        if (candidate?.Content?.Parts is { } parts)
        {
            foreach (var part in parts)
            {
                if (!string.IsNullOrEmpty(part.Text))
                {
                    content.Add(new TextPart(part.Text));
                }
                else if (part.FunctionCall is not null)
                {
                    var name = part.FunctionCall.Name ?? "";
                    var argsJson = part.FunctionCall.Args.ValueKind == JsonValueKind.Undefined
                        ? "{}"
                        : part.FunctionCall.Args.GetRawText();

                    content.Add(new ToolUsePart($"{name}_{toolCallIndex++}", name, argsJson));
                }
            }
        }

        var stopReason = ToStopReason(candidate?.FinishReason);
        if (stopReason == StopReason.EndTurn && content.Any(c => c is ToolUsePart))
        {
            // Gemini has no dedicated "tool call" finish reason — a function call still reports
            // "STOP" — so infer ToolUse from the presence of a functionCall part instead.
            stopReason = StopReason.ToolUse;
        }

        return new ChatResponse
        {
            Content = content,
            StopReason = stopReason,
            Usage = ToUsage(wire.UsageMetadata),
            ServedBy = model,
        };
    }

    public static Usage ToUsage(GeminiUsageMetadata? usage) => usage is null
        ? new Usage()
        : new Usage
        {
            InputTokens = usage.PromptTokenCount,
            OutputTokens = usage.CandidatesTokenCount,
        };

    public static StopReason ToStopReason(string? finishReason) => finishReason switch
    {
        null => StopReason.EndTurn,
        "STOP" => StopReason.EndTurn,
        "MAX_TOKENS" => StopReason.MaxTokens,
        "SAFETY" => StopReason.Refusal,
        "RECITATION" => StopReason.Refusal,
        "PROHIBITED_CONTENT" => StopReason.Refusal,
        "SPII" => StopReason.Refusal,
        "BLOCKLIST" => StopReason.Refusal,
        _ => StopReason.Error,
    };
}
