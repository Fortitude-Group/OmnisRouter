using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OmnisRouter.Vigil;

namespace OmnisRouter.Api.Tests;

public class OmnisVigilConfigTests
{
    private static IServiceProvider Build(params (string Key, string Value)[] settings)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(settings.ToDictionary(s => s.Key, s => (string?)s.Value))
            .Build();
        return new ServiceCollection().AddOmnisVigil(config).BuildServiceProvider();
    }

    [Fact]
    public void Binds_options_and_defaults_router_id_to_machine_name()
    {
        var sp = Build(
            ("OmnisVigil:Enabled", "true"),
            ("OmnisVigil:Endpoint", "https://ingest.example"),
            ("OmnisVigil:BatchSize", "50"));

        var opts = sp.GetRequiredService<OmnisVigilOptions>();
        Assert.True(opts.Enabled);
        Assert.Equal("https://ingest.example", opts.Endpoint);
        Assert.Equal(50, opts.BatchSize);
        Assert.Equal(45, opts.PolicyPollSeconds); // default preserved when not set

        Assert.Equal(Environment.MachineName, sp.GetRequiredService<RouterIdentity>().RouterId);
    }

    [Fact]
    public void Uses_explicit_router_id_when_set()
    {
        var sp = Build(("OmnisVigil:RouterId", "router-eu-1"));
        Assert.Equal("router-eu-1", sp.GetRequiredService<RouterIdentity>().RouterId);
    }

    [Fact]
    public void Disabled_by_default_when_section_absent()
    {
        var sp = Build(("Unrelated:Key", "x"));
        Assert.False(sp.GetRequiredService<OmnisVigilOptions>().Enabled);
    }
}
