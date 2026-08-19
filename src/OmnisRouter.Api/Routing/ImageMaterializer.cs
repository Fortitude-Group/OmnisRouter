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
        // SSRF guard (security-review F1): only http(s), and the connect-time IP guard on the
        // configured HttpClient handler blocks non-public address space + DNS-rebinding.
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            throw new OmnisException(400, "unsafe_image_url", "Image URL must be an absolute http(s) URL.");
        }

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(SafeImageFetch.Timeout);

            using var response = await httpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            response.EnsureSuccessStatusCode();

            if (response.Content.Headers.ContentLength is > SafeImageFetch.MaxBytes)
            {
                throw new OmnisException(400, "image_too_large", $"Image exceeds the {SafeImageFetch.MaxBytes} byte limit.");
            }

            var bytes = await ReadCappedAsync(response, timeout.Token);
            var mediaType = response.Content.Headers.ContentType?.MediaType ?? fallbackMediaType;
            return (mediaType, Convert.ToBase64String(bytes));
        }
        catch (OmnisException)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or IOException)
        {
            // Never route to a target that can't safely fetch the referenced image (don't 500).
            throw new OmnisException(400, "image_fetch_failed", "Could not fetch the referenced image URL.");
        }
    }

    private static async Task<byte[]> ReadCappedAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > SafeImageFetch.MaxBytes)
            {
                throw new OmnisException(400, "image_too_large", $"Image exceeds the {SafeImageFetch.MaxBytes} byte limit.");
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }
}
