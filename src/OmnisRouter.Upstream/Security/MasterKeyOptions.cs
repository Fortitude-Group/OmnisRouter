namespace OmnisRouter.Upstream.Security;

/// <summary>Options for <see cref="LocalFileMasterKeyProvider"/>.</summary>
public sealed record MasterKeyOptions
{
    /// <summary>
    /// Path to the file holding the local master key material. Defaults to
    /// <c>%APPDATA%/OmnisRouter/master.key</c> (or the equivalent per-user application-data
    /// directory on non-Windows platforms).
    /// </summary>
    public string KeyFilePath { get; init; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "OmnisRouter",
        "master.key");
}
