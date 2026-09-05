using System.Text.Json;
using System.Text.Json.Serialization;

namespace OmnisRouter.Collect;

/// <summary>
/// The tray's per-user configuration, persisted to <c>%APPDATA%\OmnisRouter\collect.json</c>. The
/// project key is stored as a DPAPI blob in <see cref="ProtectedKey"/>, never in plain text
/// (FR-011). See <c>specs/002-collect-tray-app/contracts/config-schema.md</c>. The CLI does not use
/// this file; it resolves <c>--url/--key</c> or the OmnisVigil config section as before (FR-020).
/// </summary>
public sealed class CollectConfig
{
    public const string DefaultEndpoint = "https://app.omnisvigil.com";

    public int SchemaVersion { get; set; } = 1;

    public string Endpoint { get; set; } = DefaultEndpoint;

    /// <summary>DPAPI ciphertext of the project key (base64). Never the raw key.</summary>
    public string? ProtectedKey { get; set; }

    public string? Root { get; set; }

    public int IntervalSeconds { get; set; } = 15;

    public long LogMaxBytes { get; set; } = 1_048_576;

    public int LogMaxFiles { get; set; } = 3;

    public bool Paused { get; set; }

    [JsonIgnore]
    public static string Directory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OmnisRouter");

    [JsonIgnore]
    public static string FilePath => Path.Combine(Directory, "collect.json");

    [JsonIgnore]
    public static bool Exists => File.Exists(FilePath);

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>Load the config, or null when it is missing or unreadable (both route to onboarding).</summary>
    public static CollectConfig? Load() => Load(FilePath);

    /// <summary>Load from a specific path (used by tests so the real user config is never touched).</summary>
    public static CollectConfig? Load(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            return JsonSerializer.Deserialize<CollectConfig>(File.ReadAllText(path), Json);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public void Save() => Save(FilePath);

    public void Save(string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
        {
            System.IO.Directory.CreateDirectory(dir);
        }

        File.WriteAllText(path, JsonSerializer.Serialize(this, Json));
    }

    /// <summary>Encrypt and store the raw project key (Windows DPAPI).</summary>
    public void SetKey(string plaintextKey) => ProtectedKey = ProtectedSecret.Protect(plaintextKey);

    /// <summary>Decrypt the stored key. Returns false if absent or undecryptable (wrong user, corrupt).</summary>
    public bool TryResolveKey(out string key)
    {
        key = "";
        if (string.IsNullOrWhiteSpace(ProtectedKey))
        {
            return false;
        }

        try
        {
            key = ProtectedSecret.Unprotect(ProtectedKey);
            return !string.IsNullOrWhiteSpace(key);
        }
        catch (Exception ex) when (ex is FormatException or System.Security.Cryptography.CryptographicException or PlatformNotSupportedException)
        {
            return false;
        }
    }

    public string ResolveRoot() => string.IsNullOrWhiteSpace(Root)
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "projects")
        : Root;

    /// <summary>Validate for use. Returns the first problem, or null when good to run.</summary>
    public string? Validate()
    {
        if (SchemaVersion != 1)
        {
            return $"Unsupported config version {SchemaVersion}.";
        }

        if (!Uri.TryCreate(Endpoint, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return "Endpoint must be an absolute http(s) URL.";
        }

        if (!TryResolveKey(out _))
        {
            return "The project key is missing or could not be read for this user.";
        }

        return null;
    }

    public CollectEngineOptions ToEngineOptions() =>
        new(ResolveRoot(), Since: null, Batch: 1000, Watch: true, Interval: Math.Max(2, IntervalSeconds));
}
