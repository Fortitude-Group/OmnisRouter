using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OmnisRouter.Core.Abstractions;

namespace OmnisRouter.Routing.Tests;

/// <summary>
/// The routing centroids are built from the ONNX embedder, so a HashingEmbedder fallback would route
/// incorrectly. AddOmnisRouting must fail fast (not silently degrade) when the ONNX asset is missing
/// and it is required — the production posture — while still allowing the fallback for dev/CI.
/// </summary>
public class EmbedderGuardTests
{
    // Point the embedder asset at paths that definitely don't exist, so the "ONNX available" check is
    // false regardless of whether this machine has fetched the real 34MB model.
    private static IConfiguration MissingAssetConfig(params (string Key, string? Value)[] extra)
    {
        var dict = new Dictionary<string, string?>
        {
            ["Routing:Embedder:ModelPath"] = Path.Combine(Path.GetTempPath(), "omnis-no-such-model.onnx"),
            ["Routing:Embedder:VocabPath"] = Path.Combine(Path.GetTempPath(), "omnis-no-such-vocab.txt"),
        };
        foreach (var (key, value) in extra)
        {
            dict[key] = value;
        }

        return new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
    }

    [Fact]
    public void RequireOnnx_WithMissingAsset_ThrowsAtRegistration()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            new ServiceCollection().AddOmnisRouting(MissingAssetConfig(), requireOnnxEmbedder: true));

        Assert.Contains("ONNX embedder asset not found", ex.Message);
    }

    [Fact]
    public void NoRequire_WithMissingAsset_FallsBackToHashingEmbedder()
    {
        var services = new ServiceCollection().AddOmnisRouting(MissingAssetConfig(), requireOnnxEmbedder: false);

        using var provider = services.BuildServiceProvider();
        var embedder = provider.GetRequiredService<IEmbedder>();

        Assert.Equal(384, embedder.Dimension);
    }

    [Fact]
    public void RequireOnnxConfigFalse_OverridesRequirement()
    {
        // The explicit config opt-out wins even when the composition root asked to require ONNX.
        var config = MissingAssetConfig(("Routing:Embedder:RequireOnnx", "false"));

        var ex = Record.Exception(() =>
            new ServiceCollection().AddOmnisRouting(config, requireOnnxEmbedder: true));

        Assert.Null(ex);
    }

    [Fact]
    public void RequireOnnxConfigTrue_ThrowsEvenWhenNotRequiredByHost()
    {
        // ...and forcing it on (e.g. to catch a missing asset in dev) wins the other way too.
        var config = MissingAssetConfig(("Routing:Embedder:RequireOnnx", "true"));

        Assert.Throws<InvalidOperationException>(() =>
            new ServiceCollection().AddOmnisRouting(config, requireOnnxEmbedder: false));
    }
}
