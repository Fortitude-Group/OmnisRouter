namespace OmnisRouter.ClientLink;

/// <summary>
/// Applies the pure connect/revert transforms of an <see cref="IClientLink"/> to disk and to the
/// user environment (T033). It reads the client's current config, writes a timestamped backup before
/// editing, writes the transformed content back, and sets/restores user environment variables through
/// an injected <see cref="IUserEnvironment"/> — the only side-effecting layer, kept out of the links
/// so the transforms stay pure and golden-testable. The tray persists the returned
/// <see cref="ConnectedClient"/> in <c>router.json</c> and hands it back to <see cref="Revert"/>.
/// </summary>
public sealed class ClientLinkService(IUserEnvironment environment)
{
    private static readonly IReadOnlyDictionary<string, string?> NoPriorEnv =
        new Dictionary<string, string?>();

    /// <summary>
    /// Connect <paramref name="link"/> to the router at <paramref name="root"/> using
    /// <paramref name="token"/>: apply the file transform (with a backup), set any required user
    /// environment variables (capturing their prior values), and return the record to persist.
    /// </summary>
    public ConnectedClient Connect(IClientLink link, string root, string token)
    {
        var currentContent = ReadCurrentContent(link);
        var result = link.Connect(root, token, currentContent);

        string? backupPath = null;
        if (link.ConfigPath is { } configPath && result.NewFileContent is { } newContent)
        {
            backupPath = BackUpIfPresent(configPath);
            WriteFile(configPath, newContent);
        }

        // Capture the prior value of each environment variable before overwriting it, so revert can
        // restore it exactly (or remove it when it was absent).
        Dictionary<string, string?>? priorEnv = null;
        foreach (var (name, value) in result.EnvironmentVariablesToSet)
        {
            priorEnv ??= new Dictionary<string, string?>();
            priorEnv[name] = environment.Get(name);
            environment.Set(name, value);
        }

        return new ConnectedClient(
            link.Kind,
            DateTimeOffset.UtcNow,
            result.PriorState,
            backupPath,
            priorEnv ?? NoPriorEnv);
    }

    /// <summary>
    /// Revert a previously connected client using the <paramref name="record"/> captured at connect:
    /// restore the config file to its pre-connect state and restore or remove the environment
    /// variables the connect touched.
    /// </summary>
    public void Revert(IClientLink link, ConnectedClient record)
    {
        if (link.ConfigPath is { } configPath)
        {
            var currentContent = ReadCurrentContent(link);
            var reverted = link.Revert(currentContent, record.PriorState);
            if (reverted is not null)
            {
                WriteFile(configPath, reverted);
            }
        }

        foreach (var (name, priorValue) in record.PriorEnvironmentValues)
        {
            if (priorValue is null)
            {
                environment.Unset(name);
            }
            else
            {
                environment.Set(name, priorValue);
            }
        }
    }

    private static string? ReadCurrentContent(IClientLink link) =>
        link.ConfigPath is { } path && File.Exists(path) ? File.ReadAllText(path) : null;

    private static string? BackUpIfPresent(string configPath)
    {
        if (!File.Exists(configPath))
        {
            return null;
        }

        var backupPath = $"{configPath}.bak-{DateTimeOffset.UtcNow:yyyy-MM-ddTHH-mm-ss-fffZ}";
        File.Copy(configPath, backupPath, overwrite: false);
        return backupPath;
    }

    private static void WriteFile(string path, string content)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        File.WriteAllText(path, content);
    }
}
