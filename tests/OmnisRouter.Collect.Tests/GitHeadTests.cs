using OmnisRouter.Collect;

namespace OmnisRouter.Collect.Tests;

public class GitHeadTests : IDisposable
{
    private const string Sha = "1234567890abcdef1234567890abcdef12345678";
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "omnis-git", Guid.NewGuid().ToString("n"));

    private string NewRepoDir()
    {
        var d = Path.Combine(_dir, Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(d);
        return d;
    }

    private static void WriteGit(string repo, string relativePath, string content)
    {
        var full = Path.Combine(repo, ".git", relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }

    [Fact]
    public void Resolves_head_through_a_loose_ref()
    {
        var repo = NewRepoDir();
        WriteGit(repo, "HEAD", "ref: refs/heads/main\n");
        WriteGit(repo, "refs/heads/main", Sha + "\n");

        Assert.Equal(Sha, GitHead.Resolve(repo));
    }

    [Fact]
    public void Resolves_head_from_a_subdirectory()
    {
        var repo = NewRepoDir();
        WriteGit(repo, "HEAD", "ref: refs/heads/main\n");
        WriteGit(repo, "refs/heads/main", Sha);
        var sub = Path.Combine(repo, "src", "deep");
        Directory.CreateDirectory(sub);

        Assert.Equal(Sha, GitHead.Resolve(sub));
    }

    [Fact]
    public void Resolves_a_detached_head()
    {
        var repo = NewRepoDir();
        WriteGit(repo, "HEAD", Sha + "\n");

        Assert.Equal(Sha, GitHead.Resolve(repo));
    }

    [Fact]
    public void Resolves_head_through_packed_refs()
    {
        var repo = NewRepoDir();
        WriteGit(repo, "HEAD", "ref: refs/heads/main\n");
        WriteGit(repo, "packed-refs", "# pack-refs with: peeled fully-peeled sorted\n" + Sha + " refs/heads/main\n");

        Assert.Equal(Sha, GitHead.Resolve(repo));
    }

    [Fact]
    public void Returns_null_outside_a_repository()
    {
        Assert.Null(GitHead.Resolve(_dir));
    }

    [Fact]
    public void Returns_null_for_null_or_empty()
    {
        Assert.Null(GitHead.Resolve(null));
        Assert.Null(GitHead.Resolve("   "));
    }

    [Fact]
    public void Reads_this_repository_head_as_a_real_sha()
    {
        // The test binary runs inside the OmnisRouter checkout; walking up must find a real HEAD.
        var here = AppContext.BaseDirectory;
        var sha = GitHead.Resolve(here);

        Assert.NotNull(sha);
        Assert.InRange(sha!.Length, 7, 64);
        Assert.All(sha, c => Assert.True(Uri.IsHexDigit(c)));
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
