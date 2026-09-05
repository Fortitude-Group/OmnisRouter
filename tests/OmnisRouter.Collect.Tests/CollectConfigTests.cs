using OmnisRouter.Collect;

namespace OmnisRouter.Collect.Tests;

public class CollectConfigTests
{
    private static string TempPath() =>
        Path.Combine(Path.GetTempPath(), "omnis-cfg", Guid.NewGuid().ToString("n"), "collect.json");

    [Fact]
    public void Missing_file_loads_as_null_to_trigger_onboarding()
    {
        Assert.Null(CollectConfig.Load(TempPath()));
    }

    [Fact]
    public void Corrupt_file_loads_as_null_rather_than_throwing()
    {
        var path = TempPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{ not valid json");
        Assert.Null(CollectConfig.Load(path));
    }

    [Fact]
    public void Round_trips_all_fields()
    {
        var path = TempPath();
        var cfg = new CollectConfig
        {
            Endpoint = "https://vigil.example.com",
            ProtectedKey = "blob==",
            Root = @"C:\transcripts",
            IntervalSeconds = 30,
            LogMaxBytes = 2048,
            LogMaxFiles = 5,
            Paused = true,
        };
        cfg.Save(path);

        var loaded = CollectConfig.Load(path)!;
        Assert.Equal("https://vigil.example.com", loaded.Endpoint);
        Assert.Equal("blob==", loaded.ProtectedKey);
        Assert.Equal(@"C:\transcripts", loaded.Root);
        Assert.Equal(30, loaded.IntervalSeconds);
        Assert.Equal(2048, loaded.LogMaxBytes);
        Assert.Equal(5, loaded.LogMaxFiles);
        Assert.True(loaded.Paused);
    }

    [Fact]
    public void Saved_file_never_contains_the_raw_key()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;   // DPAPI is Windows-only; the CLI path never encrypts
        }

        var path = TempPath();
        var cfg = new CollectConfig { Endpoint = "https://vigil.example.com" };
        cfg.SetKey("ovk_super_secret_value");
        cfg.Save(path);

        var text = File.ReadAllText(path);
        Assert.DoesNotContain("ovk_super_secret_value", text);
        Assert.Contains("protectedKey", text);
    }

    [Fact]
    public void Dpapi_round_trips_the_key_for_this_user()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var cfg = new CollectConfig { Endpoint = "https://vigil.example.com" };
        cfg.SetKey("ovk_abc123");

        Assert.True(cfg.TryResolveKey(out var key));
        Assert.Equal("ovk_abc123", key);
    }

    [Theory]
    [InlineData("not-a-url")]
    [InlineData("ftp://vigil.example.com")]
    [InlineData("")]
    public void Validate_rejects_a_non_http_endpoint(string endpoint)
    {
        var cfg = new CollectConfig { Endpoint = endpoint, ProtectedKey = "x" };
        Assert.NotNull(cfg.Validate());
    }

    [Fact]
    public void Validate_rejects_a_missing_key()
    {
        var cfg = new CollectConfig { Endpoint = "https://vigil.example.com", ProtectedKey = null };
        Assert.Equal("The project key is missing or could not be read for this user.", cfg.Validate());
    }

    [Fact]
    public void Default_endpoint_is_the_dashboard()
    {
        Assert.Equal("https://app.omnisvigil.com", new CollectConfig().Endpoint);
    }

    [Fact]
    public void Resolve_root_defaults_to_the_claude_projects_folder()
    {
        var root = new CollectConfig().ResolveRoot();
        Assert.EndsWith(Path.Combine(".claude", "projects"), root);
    }
}
