namespace OmnisRouter.Routing;

/// <summary>
/// Resolves the <c>config/</c> and <c>routing/</c> data directories, which live at the repo root in
/// dev and alongside the published binary in production. Tries the path relative to the current
/// directory, then walks up from the app base directory looking for the solution/marker.
/// </summary>
public static class RepoLocator
{
    public static string Resolve(string relativePath)
    {
        if (File.Exists(relativePath) || Directory.Exists(relativePath))
        {
            return Path.GetFullPath(relativePath);
        }

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var marker = Path.Combine(dir.FullName, "OmnisRouter.slnx");
            var candidate = Path.Combine(dir.FullName, relativePath);
            if (File.Exists(marker) && (File.Exists(candidate) || Directory.Exists(candidate)))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        return Path.GetFullPath(relativePath);
    }
}
