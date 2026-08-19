using System.Net;
using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OmnisRouter.Api.Auth;
using OmnisRouter.Store;
using OmnisRouter.Store.Entities;

namespace OmnisRouter.Api.Tests;

/// <summary>
/// T054 — proves <c>ProviderKey.ApiKey</c> never lands on disk as plaintext (the store's EF
/// <c>ValueConverter</c> AES-256-GCM-encrypts it, see <see cref="OmnisRouterDbContext"/>), and that
/// the app can still round-trip the plaintext back out through its own DbContext.
/// </summary>
public sealed class ByokEncryptionAtRestTests
{
    private const string PlaintextKey = "sk-SUPER-SECRET-plaintext-1234567890";

    private static async Task SeedTokenAsync(OmnisApiFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OmnisRouterDbContext>();
        db.RouterTokens.Add(new RouterToken
        {
            Id = Guid.NewGuid().ToString("n"),
            TenantId = "default",
            HashedToken = RouterTokenHasher.Hash("test-token"),
            Name = "byok",
            CreatedAt = DateTimeOffset.UnixEpoch,
        });
        await db.SaveChangesAsync();
    }

    private static HttpRequestMessage PostKey(string label, string apiKey) => new(HttpMethod.Post, "/v1/keys")
    {
        Content = new StringContent(
            $$"""{"provider":"openai","label":"{{label}}","api_key":"{{apiKey}}"}""",
            Encoding.UTF8, "application/json"),
        Headers = { Authorization = new("Bearer", "test-token") },
    };

    /// <summary>Reads the raw <c>ApiKeyEncrypted</c> column bytes for every row via a second,
    /// independent <see cref="SqliteConnection"/> — bypassing the app's DbContext (and therefore its
    /// decrypting value converter) entirely.</summary>
    private static async Task<List<byte[]>> ReadRawEncryptedColumnsAsync(string dbPath)
    {
        await using var connection = new SqliteConnection($"Data Source={dbPath}");
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT ApiKeyEncrypted FROM ProviderKeys ORDER BY CreatedAt;";

        var blobs = new List<byte[]>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            blobs.Add((byte[])reader["ApiKeyEncrypted"]);
        }

        return blobs;
    }

    private static bool ContainsSubsequence(byte[] haystack, byte[] needle)
    {
        if (needle.Length == 0 || needle.Length > haystack.Length)
        {
            return false;
        }

        for (var i = 0; i <= haystack.Length - needle.Length; i++)
        {
            var match = true;
            for (var j = 0; j < needle.Length; j++)
            {
                if (haystack[i + j] != needle[j])
                {
                    match = false;
                    break;
                }
            }

            if (match)
            {
                return true;
            }
        }

        return false;
    }

    [Fact]
    public async Task Stored_key_bytes_never_contain_the_plaintext_and_round_trip_through_the_app_matches()
    {
        using var factory = new OmnisApiFactory();
        await SeedTokenAsync(factory);
        var client = factory.CreateClient();

        var response = await client.SendAsync(PostKey("primary", PlaintextKey));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var plaintextBytes = Encoding.UTF8.GetBytes(PlaintextKey);
        var blobs = await ReadRawEncryptedColumnsAsync(factory.DbPath);

        Assert.Single(blobs);
        Assert.False(
            ContainsSubsequence(blobs[0], plaintextBytes),
            "Encrypted column bytes must never contain the plaintext API key.");

        // Round-trip: reading back through the app's own DbContext (whose value converter
        // decrypts transparently) must recover the exact plaintext.
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OmnisRouterDbContext>();
        var stored = await db.ProviderKeys.SingleAsync(k => k.Label == "primary");
        Assert.Equal(PlaintextKey, stored.ApiKey);
    }

    [Fact]
    public async Task Encrypting_the_same_plaintext_twice_produces_different_ciphertext_via_a_fresh_nonce()
    {
        using var factory = new OmnisApiFactory();
        await SeedTokenAsync(factory);
        var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.Created, (await client.SendAsync(PostKey("first", PlaintextKey))).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await client.SendAsync(PostKey("second", PlaintextKey))).StatusCode);

        var blobs = await ReadRawEncryptedColumnsAsync(factory.DbPath);
        Assert.Equal(2, blobs.Count);
        Assert.False(blobs[0].AsSpan().SequenceEqual(blobs[1]),
            "Two encryptions of the same plaintext must differ (fresh nonce per call).");

        // Both still decrypt to the same plaintext through the app.
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OmnisRouterDbContext>();
        var keys = await db.ProviderKeys.Where(k => k.Label == "first" || k.Label == "second").ToListAsync();
        Assert.All(keys, k => Assert.Equal(PlaintextKey, k.ApiKey));
    }
}
