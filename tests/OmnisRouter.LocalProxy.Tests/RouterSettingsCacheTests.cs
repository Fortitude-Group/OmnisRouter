using OmnisRouter.LocalProxy;

namespace OmnisRouter.LocalProxy.Tests;

public sealed class RouterSettingsCacheTests
{
    private sealed class FakeSecretProtector : ISecretProtector
    {
        public string Protect(string plaintext) => plaintext;
        public string Unprotect(string protectedValue) => protectedValue;
    }

    private static string TempPath() => Path.Combine(Path.GetTempPath(), $"router-settings-{Guid.NewGuid():N}.json");

    [Fact]
    public void Cache_fields_default_when_absent()
    {
        var s = RouterSettings.Load(TempPath(), new FakeSecretProtector());

        Assert.Empty(s.EnabledFixes);
        Assert.False(s.EmitCacheWaste);
        Assert.Equal("PayAsYouGo", s.Billing);
    }

    [Fact]
    public void Cache_fields_round_trip_through_save_and_load()
    {
        var path = TempPath();
        try
        {
            var settings = new RouterSettings
            {
                EnabledFixes = ["LineEnding", "ToolOrdering"],
                EmitCacheWaste = true,
                Billing = "Subscription",
            };
            settings.Save(path);

            var loaded = RouterSettings.Load(path, new FakeSecretProtector());

            Assert.Equal(["LineEnding", "ToolOrdering"], loaded.EnabledFixes);
            Assert.True(loaded.EmitCacheWaste);
            Assert.Equal("Subscription", loaded.Billing);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
