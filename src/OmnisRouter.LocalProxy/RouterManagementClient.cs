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

/// <summary>The figures for one period in the cache-hygiene summary. Content-free scalars.</summary>
public sealed record CachePeriodInfo(
    [property: JsonPropertyName("avoidable_waste_gbp")] decimal AvoidableWasteGbp,
    [property: JsonPropertyName("recovered_gbp")] decimal RecoveredGbp,
    [property: JsonPropertyName("recomputed_tokens")] long RecomputedTokens,
    [property: JsonPropertyName("saved_tokens")] long SavedTokens,
    [property: JsonPropertyName("miss_count")] int MissCount,
    [property: JsonPropertyName("recovery_count")] int RecoveryCount);

/// <summary>
/// The content-free cache-hygiene summary the tray shows (contracts/cache-hygiene-summary.md). Every £
/// figure is backed by the pricing/FX/shadow basis carried alongside it.
/// </summary>
public sealed record CacheHygieneSummaryInfo(
    [property: JsonPropertyName("measurement_enabled")] bool MeasurementEnabled,
    [property: JsonPropertyName("source")] string Source,
    [property: JsonPropertyName("since_start")] CachePeriodInfo SinceStart,
    [property: JsonPropertyName("today")] CachePeriodInfo Today,
    [property: JsonPropertyName("pricing_version")] string? PricingVersion,
    [property: JsonPropertyName("fx_date")] string? FxDate,
    [property: JsonPropertyName("usd_gbp")] decimal? UsdGbp,
    [property: JsonPropertyName("shadow_price")] bool ShadowPrice);

/// <summary>The enabled byte-mutating fix classes, local intent versus effective (policy-resolved).</summary>
public sealed record CacheFixStateInfo(
    [property: JsonPropertyName("local")] IReadOnlyList<string> Local,
    [property: JsonPropertyName("effective")] IReadOnlyList<string> Effective,
    [property: JsonPropertyName("policy_overrides")] bool PolicyOverrides);

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

    /// <summary>
    /// The cache-hygiene summary, or null when it cannot be obtained (router stopped, unreachable, or
    /// errored). Fail-open by design: the tray renders "not measuring" rather than surfacing an error.
    /// </summary>
    public async Task<CacheHygieneSummaryInfo?> GetCacheSummaryAsync(CancellationToken ct = default)
    {
        try
        {
            using var response = await SendAsync(HttpMethod.Get, "/v1/analytics/cache-hygiene/summary", content: null, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            return await response.Content.ReadFromJsonAsync<CacheHygieneSummaryInfo>(ResponseJson, ct).ConfigureAwait(false);
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    /// <summary>The current fix state (local vs effective), or null when it cannot be obtained.</summary>
    public async Task<CacheFixStateInfo?> GetCacheFixesAsync(CancellationToken ct = default)
    {
        try
        {
            using var response = await SendAsync(HttpMethod.Get, "/v1/cache-hygiene/fixes", content: null, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            return await response.Content.ReadFromJsonAsync<CacheFixStateInfo>(ResponseJson, ct).ConfigureAwait(false);
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    /// <summary>Set the local enabled fix classes; returns the resulting state (so the tray sees any policy override).</summary>
    public async Task<CacheFixStateInfo?> SetCacheFixesAsync(IReadOnlyList<string> enabled, CancellationToken ct = default)
    {
        var body = JsonContent.Create(new { enabled });
        using var response = await SendAsync(HttpMethod.Put, "/v1/cache-hygiene/fixes", body, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw await FailureAsync(response, ct).ConfigureAwait(false);
        }

        return await response.Content.ReadFromJsonAsync<CacheFixStateInfo>(ResponseJson, ct).ConfigureAwait(false);
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
