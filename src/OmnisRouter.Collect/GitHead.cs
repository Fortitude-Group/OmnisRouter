namespace OmnisRouter.Collect;

/// <summary>
/// Resolves the current commit SHA of the git repository a working directory sits in, by reading
/// git's plumbing files directly (no <c>git</c> process, no dependency). Used to stamp the commit on
/// live-watched receipts: Claude Code transcripts record the branch but not the commit, so for usage
/// seen while watching we read HEAD now. Backfilled history is left without a commit, because today's
/// HEAD is not the commit an old entry ran on. Any failure returns null (attribution is best-effort).
/// </summary>
public static class GitHead
{
    public static string? Resolve(string? startDir)
    {
        if (string.IsNullOrWhiteSpace(startDir))
        {
            return null;
        }

        try
        {
            var gitDir = FindGitDir(startDir);
            if (gitDir is null)
            {
                return null;
            }

            var headPath = Path.Combine(gitDir, "HEAD");
            if (!File.Exists(headPath))
            {
                return null;
            }

            var head = File.ReadAllText(headPath).Trim();
            if (head.StartsWith("ref:", StringComparison.Ordinal))
            {
                return ResolveRef(gitDir, head[4..].Trim());
            }

            return LooksLikeSha(head) ? head : null;   // detached HEAD
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    // Walk up from startDir to the repo. ".git" is a directory normally, or a file ("gitdir: …") in a worktree.
    private static string? FindGitDir(string startDir)
    {
        for (var dir = new DirectoryInfo(startDir); dir is not null; dir = dir.Parent)
        {
            var dotGit = Path.Combine(dir.FullName, ".git");
            if (Directory.Exists(dotGit))
            {
                return dotGit;
            }

            if (File.Exists(dotGit))
            {
                var content = File.ReadAllText(dotGit).Trim();
                const string prefix = "gitdir:";
                if (content.StartsWith(prefix, StringComparison.Ordinal))
                {
                    var target = content[prefix.Length..].Trim();
                    return Path.GetFullPath(Path.IsPathRooted(target) ? target : Path.Combine(dir.FullName, target));
                }
            }
        }

        return null;
    }

    private static string? ResolveRef(string gitDir, string refName)
    {
        // Loose ref in this git dir.
        var loose = Path.Combine(gitDir, refName.Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(loose))
        {
            var sha = File.ReadAllText(loose).Trim();
            return LooksLikeSha(sha) ? sha : null;
        }

        // A worktree keeps HEAD locally but refs in the shared common dir.
        var commonDirFile = Path.Combine(gitDir, "commondir");
        if (File.Exists(commonDirFile))
        {
            var common = File.ReadAllText(commonDirFile).Trim();
            var commonDir = Path.GetFullPath(Path.IsPathRooted(common) ? common : Path.Combine(gitDir, common));
            var commonLoose = Path.Combine(commonDir, refName.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(commonLoose))
            {
                var sha = File.ReadAllText(commonLoose).Trim();
                return LooksLikeSha(sha) ? sha : null;
            }

            var fromCommonPacked = FromPackedRefs(commonDir, refName);
            if (fromCommonPacked is not null)
            {
                return fromCommonPacked;
            }
        }

        // Packed refs in this git dir.
        return FromPackedRefs(gitDir, refName);
    }

    private static string? FromPackedRefs(string gitDir, string refName)
    {
        var packed = Path.Combine(gitDir, "packed-refs");
        if (!File.Exists(packed))
        {
            return null;
        }

        foreach (var line in File.ReadLines(packed))
        {
            if (line.Length == 0 || line[0] is '#' or '^')
            {
                continue;
            }

            var space = line.IndexOf(' ');
            if (space > 0 && line[(space + 1)..].Trim() == refName)
            {
                var sha = line[..space].Trim();
                return LooksLikeSha(sha) ? sha : null;
            }
        }

        return null;
    }

    private static bool LooksLikeSha(string s)
    {
        if (s.Length is < 7 or > 64)
        {
            return false;
        }

        foreach (var c in s)
        {
            if (!Uri.IsHexDigit(c))
            {
                return false;
            }
        }

        return true;
    }
}
