namespace OmnisRouter.Routing;

/// <summary>
/// Resolves the <c>config/</c>, <c>routing/</c>, and <c>models/</c> data directories, which live next
/// to the binary in production (Docker/publish) and at the repo root in dev.
/// <para>
/// Resolution is anchored to the <c>OmnisRouter.slnx</c> marker rather than a loose current-directory
/// probe, because a name like <c>routing</c> collides case-insensitively with the
/// <c>OmnisRouter.Api.Routing</c> source folder on Windows — a loose probe would match the wrong one.
/// </para>
/// </summary>
public static class RepoLocator
{
    public static string Resolve(string relativePath)
    {
        // 1. Next to the published binary (Docker/self-host layout copies config/ + routing/ here).
        var beside = Path.Combine(AppContext.BaseDirectory, relativePath);
        if (Exists(beside))
        {
            return beside;
        }

        // 2. Marker-anchored: walk up from the binary dir, then the current dir, for the solution
        //    marker + the data dir in the SAME directory. This ignores same-named source folders.
        foreach (var start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            if (WalkUp(start, relativePath) is { } found)
            {
                return found;
            }
        }

        // 3. Loose current-directory probe (only reached when no marker was found).
        if (Exists(relativePath))
        {
            return Path.GetFullPath(relativePath);
        }

        // 4. Last resort.
        return Path.GetFullPath(relativePath);
    }

    private static string? WalkUp(string startDirectory, string relativePath)
    {
        var dir = new DirectoryInfo(startDirectory);
        while (dir is not null)
        {
            var marker = Path.Combine(dir.FullName, "OmnisRouter.slnx");
            var candidate = Path.Combine(dir.FullName, relativePath);
            if (File.Exists(marker) && Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        return null;
    }

    private static bool Exists(string path) => File.Exists(path) || Directory.Exists(path);
}
