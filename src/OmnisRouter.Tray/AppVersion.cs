using System.Reflection;

namespace OmnisRouter.Tray;

/// <summary>
/// The running version, for display. Taken from the assembly's informational version (set from the
/// build's <c>Version</c>, which the release stamps from the git tag), with any build-metadata suffix
/// trimmed. Falls back to the numeric assembly version.
/// </summary>
internal static class AppVersion
{
    public static string Display { get; } = Resolve();

    private static string Resolve()
    {
        var asm = typeof(AppVersion).Assembly;

        var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(info))
        {
            var plus = info.IndexOf('+');   // strip "+<commit>" source-link metadata
            return plus >= 0 ? info[..plus] : info;
        }

        var v = asm.GetName().Version;
        return v is null ? "" : $"{v.Major}.{v.Minor}.{v.Build}";
    }
}
