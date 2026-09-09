namespace OmnisRouter.ClientLink;

/// <summary>
/// The user-scoped environment variables a client link may need to set (Codex reads its token from
/// <c>OMNISROUTER_API_KEY</c>). Abstracted so the executor stays cross-platform and testable: the
/// tray supplies a Windows implementation over <c>Environment.*EnvironmentVariable(..., User)</c>,
/// and tests supply an in-memory fake.
/// </summary>
public interface IUserEnvironment
{
    /// <summary>The current value of the user environment variable, or <c>null</c> if it is unset.</summary>
    string? Get(string name);

    /// <summary>Set the user environment variable.</summary>
    void Set(string name, string value);

    /// <summary>Remove the user environment variable.</summary>
    void Unset(string name);
}
