using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OmnisRouter.Core.Model;
using OmnisRouter.Store;
using OmnisRouter.Store.Entities;

namespace OmnisRouter.Api.Endpoints;

/// <summary>
/// BYOK key management (US5, T059): add/list/delete the tenant's provider API keys. The store's
/// <c>ApiKey</c> value converter (see <see cref="OmnisRouterDbContext"/>) encrypts on save and
/// decrypts on read — these endpoints never accept back or return the plaintext key value or the
/// ciphertext; only id/provider/label/created_at ever cross the wire.
/// </summary>
public static class KeysEndpoints
{
    private const string DefaultTenant = "default";

    public static IEndpointRouteBuilder MapKeys(this IEndpointRouteBuilder app)
    {
        app.MapPost("/v1/keys", CreateKeyAsync);
        app.MapGet("/v1/keys", ListKeysAsync);
        app.MapDelete("/v1/keys/{id}", DeleteKeyAsync);

        return app;
    }

    private static async Task<IResult> CreateKeyAsync(
        HttpContext http, OmnisRouterDbContext db, CancellationToken cancellationToken)
    {
        JsonElement body;
        try
        {
            body = await http.Request.ReadFromJsonAsync<JsonElement>(cancellationToken);
        }
        catch (JsonException)
        {
            throw new OmnisException(400, "invalid_request", "Request body is not valid JSON.");
        }

        var providerRaw = TryGetString(body, "provider");
        var label = TryGetString(body, "label");
        var apiKey = TryGetString(body, "api_key");

        if (string.IsNullOrWhiteSpace(label) || string.IsNullOrWhiteSpace(apiKey))
        {
            throw new OmnisException(400, "invalid_request", "provider, label and api_key are required.");
        }

        if (!TryParseProvider(providerRaw, out var provider))
        {
            throw new OmnisException(400, "invalid_request",
                "provider must be one of: openai, anthropic, gemini, openrouter.");
        }

        var key = new ProviderKey
        {
            Id = Guid.NewGuid().ToString("n"),
            TenantId = ResolveTenantId(http),
            Provider = provider,
            Label = label,
            ApiKey = apiKey,
            KeyVersion = 1,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        db.ProviderKeys.Add(key);
        await db.SaveChangesAsync(cancellationToken);

        return Results.Created($"/v1/keys/{key.Id}", ToResponse(key));
    }

    private static async Task<IResult> ListKeysAsync(
        HttpContext http, OmnisRouterDbContext db, CancellationToken cancellationToken)
    {
        var tenantId = ResolveTenantId(http);

        // Order client-side: SQLite can't ORDER BY a DateTimeOffset column in SQL (see
        // ProviderCredentialResolver for the same constraint).
        var keys = await db.ProviderKeys
            .Where(k => k.TenantId == tenantId)
            .ToListAsync(cancellationToken);

        var data = keys
            .OrderByDescending(k => k.CreatedAt)
            .Select(ToResponse);

        return Results.Ok(data);
    }

    private static async Task<IResult> DeleteKeyAsync(
        HttpContext http, string id, OmnisRouterDbContext db, CancellationToken cancellationToken)
    {
        var tenantId = ResolveTenantId(http);

        var key = await db.ProviderKeys
            .SingleOrDefaultAsync(k => k.Id == id && k.TenantId == tenantId, cancellationToken);

        if (key is null)
        {
            throw new OmnisException(404, "not_found", "Provider key not found.");
        }

        db.ProviderKeys.Remove(key);
        await db.SaveChangesAsync(cancellationToken);

        return Results.NoContent();
    }

    private static object ToResponse(ProviderKey key) => new
    {
        id = key.Id,
        provider = key.Provider.ToString().ToLowerInvariant(),
        label = key.Label,
        created_at = key.CreatedAt,
    };

    private static string ResolveTenantId(HttpContext http) =>
        http.Items.TryGetValue("OmnisTenantId", out var value) && value is string tenantId
            ? tenantId
            : DefaultTenant;

    private static bool TryParseProvider(string? value, out Provider provider) =>
        Enum.TryParse(value, ignoreCase: true, out provider) && Enum.IsDefined(provider);

    private static string? TryGetString(JsonElement body, string propertyName) =>
        body.ValueKind == JsonValueKind.Object
        && body.TryGetProperty(propertyName, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
