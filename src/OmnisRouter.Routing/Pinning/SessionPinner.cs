using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using OmnisRouter.Core.Abstractions;
using OmnisRouter.Core.Model;

namespace OmnisRouter.Routing.Pinning;

/// <summary>Options for <see cref="SessionPinner"/>. Mutable (settable, not init-only) so it can be
/// configured via an <c>Action&lt;SessionPinnerOptions&gt;</c> at DI registration time.</summary>
public sealed class SessionPinnerOptions
{
    /// <summary>
    /// HMAC key used to derive a session key when the client doesn't send
    /// <c>X-Omnis-Session-Id</c>. If left unset (empty), <see cref="Guardrails.GuardServiceCollectionExtensions"/>
    /// generates a random secret at startup — fine for v1, but note that a random secret is not
    /// stable across process restarts, so derived (non-header) pins reset on every restart.
    /// </summary>
    public string ServerSecret { get; set; } = string.Empty;

    /// <summary>How long a pin stays live after it's created/refreshed. Default 30 minutes.</summary>
    public TimeSpan Ttl { get; set; } = TimeSpan.FromMinutes(30);
}

/// <summary>
/// Session pinning to keep upstream prompt caches warm across turns (research.md R4).
/// Thread-safe, in-memory; entries carry an absolute expiry and are keyed by session key.
/// </summary>
public sealed class SessionPinner : ISessionPinner
{
    private sealed record PinEntry(ModelRef Model, int ClusterId, DateTimeOffset ExpiresAt);

    private readonly SessionPinnerOptions _options;
    private readonly ConcurrentDictionary<string, PinEntry> _pins = new();

    public SessionPinner(SessionPinnerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

    /// <summary>
    /// Client <c>X-Omnis-Session-Id</c> (surfaced on <see cref="ChatRequest.SessionId"/>) wins if
    /// present; otherwise derive <c>HMAC-SHA256(secret, tenantId ‖ "\n" ‖ systemText ‖ "\n" ‖ firstUserText)</c>
    /// truncated to the first 16 bytes (128 bits), lowercase hex. Only the system prompt and the
    /// first user message are hashed — never the growing transcript, or every turn would mint a
    /// new key and defeat pinning.
    /// </summary>
    public string ResolveKey(ChatRequest request, string tenantId)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(tenantId);

        if (!string.IsNullOrEmpty(request.SessionId))
        {
            return request.SessionId;
        }

        var systemText = ConcatenateText(request.System);
        var firstUserText = FirstUserText(request.Messages);

        var payload = $"{tenantId}\n{systemText}\n{firstUserText}";
        var payloadBytes = Encoding.UTF8.GetBytes(payload);
        var keyBytes = Encoding.UTF8.GetBytes(_options.ServerSecret);

        var hash = HMACSHA256.HashData(keyBytes, payloadBytes);
        return Convert.ToHexStringLower(hash.AsSpan(0, 16));
    }

    /// <summary>The pinned model for this session, or null if there is no live pin, it expired, or it's for a different cluster.</summary>
    public ModelRef? GetPin(string sessionKey, int clusterId)
    {
        ArgumentNullException.ThrowIfNull(sessionKey);

        if (!_pins.TryGetValue(sessionKey, out var entry))
        {
            return null;
        }

        if (entry.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            _pins.TryRemove(sessionKey, out _);
            return null;
        }

        return entry.ClusterId == clusterId ? entry.Model : null;
    }

    /// <summary>Stores/refreshes the pin for this session with a fresh TTL.</summary>
    public void Pin(string sessionKey, ModelRef model, int clusterId)
    {
        ArgumentNullException.ThrowIfNull(sessionKey);
        ArgumentNullException.ThrowIfNull(model);

        _pins[sessionKey] = new PinEntry(model, clusterId, DateTimeOffset.UtcNow + _options.Ttl);
    }

    private static string ConcatenateText(IReadOnlyList<TextPart> parts)
    {
        if (parts.Count == 0)
        {
            return string.Empty;
        }

        var sb = new StringBuilder();
        foreach (var part in parts)
        {
            sb.Append(part.Text);
        }

        return sb.ToString();
    }

    private static string FirstUserText(IReadOnlyList<Message> messages)
    {
        foreach (var message in messages)
        {
            if (message.Role != Role.User)
            {
                continue;
            }

            var sb = new StringBuilder();
            foreach (var part in message.Parts)
            {
                if (part is TextPart text)
                {
                    sb.Append(text.Text);
                }
            }

            return sb.ToString();
        }

        return string.Empty;
    }
}
