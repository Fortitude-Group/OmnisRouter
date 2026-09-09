namespace OmnisRouter.LocalProxy;

/// <summary>
/// Resolves the bundled server executable and its working directory (contracts/router-management.md
/// launch contract). Base directories are constructor-injectable so tests point them at temp dirs
/// instead of the real install/AppData.
/// </summary>
public sealed class RouterPaths
{
    private readonly string _baseDirectory;
    private readonly string _appDataDirectory;

    public RouterPaths()
        : this(AppContext.BaseDirectory, Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData))
    {
    }

    public RouterPaths(string baseDirectory, string appDataDirectory)
    {
        _baseDirectory = baseDirectory;
        _appDataDirectory = appDataDirectory;
    }

    /// <summary>Bundled server executable, alongside the tray install.</summary>
    public string ServerExecutablePath => Path.Combine(_baseDirectory, "omnisrouter.exe");

    /// <summary>Router working directory (<c>omnisrouter.db</c>, <c>master.key</c> land here).</summary>
    public string WorkingDirectory => Path.Combine(_appDataDirectory, "OmnisRouter", "router");

    public string EnsureWorkingDirectory()
    {
        Directory.CreateDirectory(WorkingDirectory);
        return WorkingDirectory;
    }
}
