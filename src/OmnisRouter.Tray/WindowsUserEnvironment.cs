using System.Runtime.Versioning;
using OmnisRouter.ClientLink;

namespace OmnisRouter.Tray;

/// <summary>
/// The Windows implementation of <see cref="IUserEnvironment"/> used by the client-link executor:
/// user-scoped environment variables (<c>EnvironmentVariableTarget.User</c>), so a value written here
/// (e.g. Codex's <c>OMNISROUTER_API_KEY</c>) persists for the user and is picked up by new processes,
/// matching the npm installer's behaviour without a console.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class WindowsUserEnvironment : IUserEnvironment
{
    public string? Get(string name) =>
        Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.User);

    public void Set(string name, string value) =>
        Environment.SetEnvironmentVariable(name, value, EnvironmentVariableTarget.User);

    public void Unset(string name) =>
        Environment.SetEnvironmentVariable(name, null, EnvironmentVariableTarget.User);
}
