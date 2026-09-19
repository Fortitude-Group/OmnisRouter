using OmnisRouter.LocalProxy;

namespace OmnisRouter.LocalProxy.Tests;

public sealed class CacheHygieneEnvTests
{
    [Fact]
    public void Enabled_fixes_map_to_indexed_enum_name_keys()
    {
        var env = CacheHygieneEnv.Map(new RouterSettings { EnabledFixes = ["LineEnding", "ToolOrdering"] });

        Assert.Equal("LineEnding", env["CacheHygiene__EnabledFixes__0"]);
        Assert.Equal("ToolOrdering", env["CacheHygiene__EnabledFixes__1"]);
    }

    [Fact]
    public void No_fix_keys_when_the_set_is_empty()
    {
        var env = CacheHygieneEnv.Map(new RouterSettings());

        Assert.DoesNotContain(env.Keys, k => k.StartsWith("CacheHygiene__EnabledFixes", StringComparison.Ordinal));
    }

    [Fact]
    public void Unknown_fix_names_are_dropped()
    {
        var env = CacheHygieneEnv.Map(new RouterSettings { EnabledFixes = ["LineEnding", "nonsense", "DropTables"] });

        // Only the one known fix survives, and indices stay contiguous.
        Assert.Equal("LineEnding", env["CacheHygiene__EnabledFixes__0"]);
        Assert.DoesNotContain("CacheHygiene__EnabledFixes__1", env.Keys);
    }

    [Fact]
    public void Billing_maps_to_its_key_and_defaults_to_pay_as_you_go()
    {
        Assert.Equal("Subscription", CacheHygieneEnv.Map(new RouterSettings { Billing = "Subscription" })["CacheHygiene__Billing"]);
        Assert.Equal("PayAsYouGo", CacheHygieneEnv.Map(new RouterSettings { Billing = "" })["CacheHygiene__Billing"]);
    }

    [Fact]
    public void Emit_maps_to_the_vigil_gate_flag()
    {
        Assert.Equal("true", CacheHygieneEnv.Map(new RouterSettings { EmitCacheWaste = true })["OmnisVigil__EmitCacheWaste"]);
        Assert.Equal("false", CacheHygieneEnv.Map(new RouterSettings { EmitCacheWaste = false })["OmnisVigil__EmitCacheWaste"]);
    }

    [Fact]
    public void Map_produces_only_configuration_keys()
    {
        var env = CacheHygieneEnv.Map(new RouterSettings { EnabledFixes = ["LineEnding"], EmitCacheWaste = true, Billing = "Subscription" });

        Assert.All(env.Keys, k =>
            Assert.True(k.StartsWith("CacheHygiene__", StringComparison.Ordinal) || k.StartsWith("OmnisVigil__", StringComparison.Ordinal)));
    }
}
