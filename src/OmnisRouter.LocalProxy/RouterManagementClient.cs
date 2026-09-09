using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OmnisRouter.LocalProxy;

/// <summary>A provider key as returned by the router (never the key material itself).</summary>
public sealed record ProviderKeyInfo(
    string Id,
    string Provider,
    string Label,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastUsedAt = null);

/// <summary>
/// Typed client over the router's loopback management API (contracts/router-management.md). Never
/// logs or exposes the raw api_key. The caller sets the client's <see cref="HttpClient.BaseAddress"/>.
/// </summary>
public sealed class RouterManagementClient
{
    private static readonly string[] ValidProviders = ["anthropic", "openai", "gemini", "openrouter"];

    private static readonly JsonSerializerOptions ResponseJson = new() { PropertyNameCaseInsensitive = true };

    private readonly HttpClient _http;
    private readonly string _token;

    public RouterManagementClient(HttpClient httpClient, string token)
    {
        _http = httpClient;
        _token = token;
    }

    public async Task<bool> IsHealthyAsync(CancellationToken ct = default)
    {
        using var response = await SendAsync(HttpMethod.Get, "/health", content: null, ct).ConfigureAwait(false);
        return response.IsSuccessStatusCode;
    }

    public async Task<bool> IsReadyAsync(CancellationToken ct = default)
    {
        using var response = await SendAsync(HttpMethod.Get, "/readyz", content: null, ct).ConfigureAwait(false);
        return response.IsSuccessStatusCode;
    }

    public async Task<ProviderKeyInfo> CreateKeyAsync(string provider, string label, string apiKey, CancellationToken ct = default)
    {
        if (!ValidProviders.Contains(provider, StringComparer.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                $"Unknown provider '{provider}'. Must be one of: {string.Join(", ", ValidProviders)}.", nameof(provider));
        }

        var body = JsonContent.Create(new { provider = provider.ToLowerInvariant(), label, api_key = apiKey });
        using var response = await SendAsync(HttpMethod.Post, "/v1/keys", body, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw await FailureAsync(response, ct).ConfigureAwait(false);
        }

        var dto = await response.Content.ReadFromJsonAsync<KeyDto>(ResponseJson, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Empty response from POST /v1/keys.");
        return dto.ToInfo();
    }

    public async Task<IReadOnlyList<ProviderKeyInfo>> ListKeysAsync(CancellationToken ct = default)
    {
        using var response = await SendAsync(HttpMethod.Get, "/v1/keys", content: null, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw await FailureAsync(response, ct).ConfigureAwait(false);
        }

        var dtos = await response.Content.ReadFromJsonAsync<List<KeyDto>>(ResponseJson, ct).ConfigureAwait(false) ?? [];
        return dtos.Select(d => d.ToInfo()).ToList();
    }

    public async Task DeleteKeyAsync(string id, CancellationToken ct = default)
    {
        using var response = await SendAsync(HttpMethod.Delete, $"/v1/keys/{Uri.EscapeDataString(id)}", content: null, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw await FailureAsync(response, ct).ConfigureAwait(false);
        }
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, HttpContent? content, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, path) { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
        return await _http.SendAsync(request, ct).ConfigureAwait(false);
    }

    private static async Task<Exception> FailureAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        return new HttpRequestException($"Router returned {(int)response.StatusCode} ({response.StatusCode}): {body}");
    }

    private sealed record KeyDto(
        string Id,
        string Provider,
        string Label,
        [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt,
        [property: JsonPropertyName("last_used_at")] DateTimeOffset? LastUsedAt)
    {
        public ProviderKeyInfo ToInfo() => new(Id, Provider, Label, CreatedAt, LastUsedAt);
    }
}
