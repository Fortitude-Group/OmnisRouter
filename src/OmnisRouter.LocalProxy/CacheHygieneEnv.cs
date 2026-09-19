namespace OmnisRouter.LocalProxy;

/// <summary>
/// Maps the tray's persisted cache-hygiene <see cref="RouterSettings"/> to the configuration environment
/// the supervised router binds on start (feature 005, contracts/router-settings.md). Pure and
/// deterministic, so it is unit-tested without the WinForms window. Produces only configuration keys and
/// values, never prompt content.
/// </summary>
public static class CacheHygieneEnv
{
    /// <summary>The fix classes the router understands, by C# <c>FixClass</c> enum name. Values outside
    /// this set are dropped rather than forwarded, so a stale setting can never inject an unknown fix.</summary>
    public static readonly IReadOnlySet<string> KnownFixes =
        new HashSet<string>(StringComparer.Ordinal) { "LineEnding", "TrailingWhitespace", "ToolOrdering" };

    public static IReadOnlyDictionary<string, string> Map(RouterSettings settings)
    {
        var env = new Dictionary<string, string>(StringComparer.Ordinal);

        var i = 0;
        foreach (var fix in settings.EnabledFixes)
        {
            if (KnownFixes.Contains(fix))
            {
                env[$"CacheHygiene__EnabledFixes__{i}"] = fix;
                i++;
            }
        }

        env["CacheHygiene__Billing"] = string.IsNullOrWhiteSpace(settings.Billing) ? "PayAsYouGo" : settings.Billing;
        env["OmnisVigil__EmitCacheWaste"] = settings.EmitCacheWaste ? "true" : "false";
        return env;
    }
}
