using OmnisRouter.Core.Model;

namespace OmnisRouter.Api.Routing;

/// <summary>
/// Rewrites remote-URL image parts into inline base64 (research.md R2 guardrail rule 2). Used when a
/// request carries an OpenAI-style remote <c>image_url</c> but the chosen provider (Anthropic, Gemini)
/// cannot dereference a bare URL — the router fetches and re-encodes rather than forwarding a URL the
/// target can't fetch.
/// </summary>
public interface IImageMaterializer
{
    /// <summary>Returns the request with remote-URL images fetched+inlined; unchanged if none apply.</summary>
    Task<ChatRequest> MaterializeAsync(ChatRequest request, CancellationToken cancellationToken);
}

public sealed class ImageMaterializer(HttpClient httpClient) : IImageMaterializer
{
    public async Task<ChatRequest> MaterializeAsync(ChatRequest request, CancellationToken cancellationToken)
    {
        if (!HasRemoteImage(request))
        {
            return request;
        }

        var newMessages = new List<Message>(request.Messages.Count);
        foreach (var message in request.Messages)
        {
            List<ContentPart>? rewritten = null;
            for (var i = 0; i < message.Parts.Count; i++)
            {
                if (message.Parts[i] is ImagePart { Base64: null, Url: { } url } image)
                {
                    rewritten ??= [.. message.Parts];
                    var (mediaType, base64) = await FetchAsync(url, image.MediaType, cancellationToken);
                    rewritten[i] = image with { Base64 = base64, Url = null, MediaType = mediaType };
                }
            }

            newMessages.Add(rewritten is null ? message : message with { Parts = rewritten });
        }

        return request with { Messages = newMessages };
    }

    private static bool HasRemoteImage(ChatRequest request) =>
        request.Messages.Any(m => m.Parts.Any(p => p is ImagePart { Base64: null, Url: not null }));

    private async Task<(string MediaType, string Base64)> FetchAsync(string url, string fallbackMediaType, CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();
        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        var mediaType = response.Content.Headers.ContentType?.MediaType ?? fallbackMediaType;
        return (mediaType, Convert.ToBase64String(bytes));
    }
}
