using System.Text.Json;
using System.Text.Json.Nodes;

namespace OmnisRouter.ClientLink;

/// <summary>
/// Wires Claude Code to the local router by merging <c>ANTHROPIC_BASE_URL</c> and
/// <c>ANTHROPIC_AUTH_TOKEN</c> into the top-level <c>env</c> object of
/// <c>~/.claude/settings.json</c> (contracts/client-link.md "Claude Code").
/// </summary>
public sealed class ClaudeCodeLink : IClientLink
{
    private const string BaseUrlKey = "ANTHROPIC_BASE_URL";
    private const string AuthTokenKey = "ANTHROPIC_AUTH_TOKEN";

    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    private readonly string _home;

    public ClaudeCodeLink(string? homeDirectory = null)
    {
        _home = homeDirectory ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }

    public ClientKind Kind => ClientKind.ClaudeCode;

    public string? ConfigPath => Path.Combine(_home, ".claude", "settings.json");

    public bool IsInstalled => File.Exists(ConfigPath);

    public ClientLinkResult Connect(string root, string token, string? currentContent)
    {
        var rootObject = ParseOrEmpty(currentContent);

        var env = GetOrCreateEnvObject(rootObject);

        var priorValues = new Dictionary<string, string?>
        {
            [BaseUrlKey] = GetStringOrNull(env, BaseUrlKey),
            [AuthTokenKey] = GetStringOrNull(env, AuthTokenKey),
        };

        env[BaseUrlKey] = root;
        env[AuthTokenKey] = token;

        return new ClientLinkResult
        {
            NewFileContent = Serialize(rootObject),
            PriorState = new ClientPriorState { PriorValues = priorValues },
        };
    }

    public string? Revert(string? currentContent, ClientPriorState priorState)
    {
        if (string.IsNullOrWhiteSpace(currentContent))
        {
            return currentContent;
        }

        var rootObject = ParseOrEmpty(currentContent);
        var env = GetOrCreateEnvObject(rootObject);

        foreach (var (key, priorValue) in priorState.PriorValues)
        {
            if (priorValue is null)
            {
                env.Remove(key);
            }
            else
            {
                env[key] = priorValue;
            }
        }

        return Serialize(rootObject);
    }

    private static JsonObject ParseOrEmpty(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return new JsonObject();
        }

        JsonNode? node;
        try
        {
            node = JsonNode.Parse(content);
        }
        catch (JsonException ex)
        {
            throw new ClientLinkException(
                $"Refusing to write: settings.json exists but is not valid JSON ({ex.Message}).");
        }

        if (node is not JsonObject obj)
        {
            throw new ClientLinkException(
                "Refusing to write: settings.json exists but is not a JSON object.");
        }

        return obj;
    }

    private static JsonObject GetOrCreateEnvObject(JsonObject rootObject)
    {
        if (rootObject[Names.Env] is JsonObject existingEnv)
        {
            return existingEnv;
        }

        var env = new JsonObject();
        rootObject[Names.Env] = env;
        return env;
    }

    private static string? GetStringOrNull(JsonObject env, string key) =>
        env.TryGetPropertyValue(key, out var value) ? value?.GetValue<string>() : null;

    private static string Serialize(JsonObject rootObject) =>
        rootObject.ToJsonString(WriteOptions).ReplaceLineEndings("\n") + "\n";

    private static class Names
    {
        public const string Env = "env";
    }
}
