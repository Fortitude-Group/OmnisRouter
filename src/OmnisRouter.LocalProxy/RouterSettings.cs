using System.Text.Json;
using System.Text.Json.Serialization;
using OmnisRouter.ClientLink;

namespace OmnisRouter.LocalProxy;

/// <summary>
/// Tray-owned per-user configuration, persisted to <c>%APPDATA%\OmnisRouter\router.json</c>, kept
/// separate from the collector's <c>collect.json</c> (FR-019). JSON, camelCase, indented. The router
/// token is only ever held as <see cref="ProtectedToken"/> ciphertext; use <see cref="GetToken"/> /
/// <see cref="SetToken"/> to cross that boundary through an <see cref="ISecretProtector"/>.
/// </summary>
public sealed class RouterSettings
{
    public const int CurrentSchemaVersion = 1;
    private const int MinPort = 1024;
    private const int MaxPort = 65535;

    private int _port = 8787;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public bool Enabled { get; set; }

    /// <summary>Whether the user has seen and accepted the first-enable per-token billing
    /// confirmation (FR-007). Shown once; persisted so it never repeats.</summary>
    public bool RoutingConfirmed { get; set; }

    /// <summary>Loopback port. Valid range 1024-65535 (data-model.md).</summary>
    public int Port
    {
        get => _port;
        set
        {
            if (ValidatePort(value) is { } error)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, error);
            }

            _port = value;
        }
    }

    /// <summary>Non-throwing port check for the settings window: returns an error message for an
    /// out-of-range value, or null when it is valid (data-model.md, FR-017).</summary>
    public static string? ValidatePort(int port) =>
        port is < MinPort or > MaxPort ? $"Port must be between {MinPort} and {MaxPort}." : null;

    /// <summary>DPAPI-protected (CurrentUser) base64 of the router token. Never plaintext.</summary>
    public string? ProtectedToken { get; set; }

    public List<ConnectedClient> ConnectedClients { get; set; } = [];

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        // Persist the client kind as its name ("claudeCode"), not an int, so router.json stays
        // legible and stable across enum reordering (data-model.md).
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static string DefaultPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OmnisRouter", "router.json");

    /// <summary>Load settings from <paramref name="path"/>, or defaults when the file is absent.
    /// <paramref name="protector"/> is accepted for signature symmetry with <see cref="Save"/> and
    /// callers that want to validate the token can decrypt straight after loading; the stored
    /// <see cref="ProtectedToken"/> ciphertext round-trips through Load/Save unchanged either way.</summary>
    public static RouterSettings Load(string path, ISecretProtector protector)
    {
        if (!File.Exists(path))
        {
            return new RouterSettings();
        }

        return JsonSerializer.Deserialize<RouterSettings>(File.ReadAllText(path), Json) ?? new RouterSettings();
    }

    public void Save(string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        File.WriteAllText(path, JsonSerializer.Serialize(this, Json));
    }

    /// <summary>Decrypt the stored token, or null when none has been set yet.</summary>
    public string? GetToken(ISecretProtector protector) =>
        string.IsNullOrEmpty(ProtectedToken) ? null : protector.Unprotect(ProtectedToken);

    /// <summary>Encrypt and store the router token. Never keeps the plaintext.</summary>
    public void SetToken(string token, ISecretProtector protector) => ProtectedToken = protector.Protect(token);
}
