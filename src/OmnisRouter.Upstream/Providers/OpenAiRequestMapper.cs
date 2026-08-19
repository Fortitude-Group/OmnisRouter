using System.Text.Json;
using OmnisRouter.Core.Model;

namespace OmnisRouter.Upstream.Providers;

/// <summary>Maps the neutral <see cref="ChatRequest"/> into an OpenAI Chat Completions wire request.</summary>
internal static class OpenAiRequestMapper
{
    public static OpenAiChatRequest ToWireRequest(ChatRequest request, ModelRef model, bool stream)
    {
        var messages = new List<OpenAiMessage>(request.Messages.Count + 1);

        if (request.System.Count > 0)
        {
            messages.Add(new OpenAiMessage
            {
                Role = "system",
                Content = string.Concat(request.System.Select(p => p.Text)),
            });
        }

        messages.AddRange(request.Messages.Select(ToWireMessage));

        var wireRequest = new OpenAiChatRequest
        {
            Model = model.ModelId,
            Messages = messages,
            Stream = stream,
            Temperature = request.Temperature,
            MaxCompletionTokens = request.MaxTokens,
            Tools = request.Tools.Count > 0 ? request.Tools.Select(ToWireTool).ToList() : null,
            ToolChoice = request.ToolChoice is { } toolChoice ? ToWireToolChoice(toolChoice) : null,
            StreamOptions = stream ? new OpenAiStreamOptions { IncludeUsage = true } : null,
        };

        return wireRequest;
    }

    private static OpenAiMessage ToWireMessage(Message message)
    {
        var role = message.Role switch
        {
            Role.System => "system",
            Role.User => "user",
            Role.Assistant => "assistant",
            Role.Tool => "tool",
            _ => throw new NotSupportedException($"Unsupported role: {message.Role}"),
        };

        if (message.Role == Role.Tool)
        {
            var toolResult = message.Parts.OfType<ToolResultPart>().FirstOrDefault();
            if (toolResult is not null)
            {
                return new OpenAiMessage
                {
                    Role = role,
                    ToolCallId = toolResult.ToolUseId,
                    Content = ExtractText(toolResult.Content),
                };
            }
        }

        var toolCalls = message.Parts.OfType<ToolUsePart>()
            .Select(t => new OpenAiToolCall
            {
                Id = t.Id,
                Type = "function",
                Function = new OpenAiFunctionCall { Name = t.Name, Arguments = t.InputJson },
            })
            .ToList();

        return new OpenAiMessage
        {
            Role = role,
            Content = BuildContent(message.Parts),
            ToolCalls = toolCalls.Count > 0 ? toolCalls : null,
        };
    }

    /// <summary>A single text part serializes as a plain string; anything richer becomes a content-part array.</summary>
    private static object? BuildContent(IReadOnlyList<ContentPart> parts)
    {
        var textAndImageParts = parts.Where(p => p is TextPart or ImagePart).ToList();

        if (textAndImageParts.Count == 0)
        {
            return null;
        }

        if (textAndImageParts is [TextPart singleText])
        {
            return singleText.Text;
        }

        return textAndImageParts.Select(part => part switch
        {
            TextPart text => new OpenAiContentPart { Type = "text", Text = text.Text },
            ImagePart image => new OpenAiContentPart
            {
                Type = "image_url",
                ImageUrl = new OpenAiImageUrl
                {
                    Url = image.Url ?? $"data:{image.MediaType};base64,{image.Base64}",
                    Detail = image.Detail,
                },
            },
            _ => throw new NotSupportedException($"Unsupported content part: {part.GetType().Name}"),
        }).ToList();
    }

    private static string ExtractText(IReadOnlyList<ContentPart> parts) =>
        string.Concat(parts.OfType<TextPart>().Select(t => t.Text));

    private static OpenAiTool ToWireTool(Tool tool)
    {
        using var schemaDoc = JsonDocument.Parse(tool.JsonSchema);
        return new OpenAiTool
        {
            Function = new OpenAiFunctionDef
            {
                Name = tool.Name,
                Description = tool.Description,
                Parameters = schemaDoc.RootElement.Clone(),
                Strict = tool.Strict ? true : null,
            },
        };
    }

    private static object ToWireToolChoice(ToolChoice toolChoice) => toolChoice.Kind switch
    {
        ToolChoiceKind.Auto => "auto",
        ToolChoiceKind.None => "none",
        ToolChoiceKind.Any => "required",
        ToolChoiceKind.Specific => new { type = "function", function = new { name = toolChoice.Name } },
        _ => "auto",
    };
}
