using System.Net.Http;
using System.Net.ServerSentEvents;
using System.Text.Json;
using OmnisRouter.Core.Model;
using OmnisRouter.Core.Routing;

namespace OmnisRouter.Core.Abstractions;

/// <summary>
/// Ingress/egress translation for one wire format. Adapters own all provider quirks; the neutral
/// <see cref="ChatRequest"/>/<see cref="ChatResponse"/> model is the pivot. See
/// contracts/internal-interfaces.md and research.md R2 for the cross-format rules.
/// </summary>
public interface IFormatAdapter
{
    /// <summary>Which format this adapter speaks.</summary>
    ClientFormat Format { get; }

    /// <summary>Ingress: raw client body → neutral request (with capabilities derived).</summary>
    ChatRequest ToInternal(JsonElement body, string? pathModel = null);

    /// <summary>Egress to upstream: neutral request → an upstream HTTP request for the chosen model.</summary>
    HttpRequestMessage FromInternal(ChatRequest request, ModelRef model);

    /// <summary>Translate a non-streaming neutral response back into this format's wire shape.</summary>
    JsonElement ToClientResponse(ChatResponse response, ModelDecision receipt);

    /// <summary>Re-frame neutral stream events into this format's SSE shape.</summary>
    IAsyncEnumerable<SseItem<string>> ToClientStream(
        IAsyncEnumerable<NeutralStreamEvent> events,
        ModelDecision receipt,
        CancellationToken cancellationToken);
}
