using OmnisRouter.RoutingModel.Build.Build;

namespace OmnisRouter.Routing.Tests;

/// <summary>
/// T064: invokes the offline routing-model builder (T065) twice, against the same bundled sample
/// dataset/bench-results/config inputs but two different (temp) output directories, and asserts the
/// two runs produce byte-identical <c>centroids-&lt;ver&gt;.bin</c> and equal
/// <c>policy-&lt;ver&gt;.json</c> -- proving the build (embedding, k-means, quality-band filtering,
/// cost ranking) is fully deterministic, which is what makes the shipped model in routing/
/// reproducible (FR-006, routing/README.md).
/// </summary>
public class BuildReproducibilityTests : IDisposable
{
    private readonly string _outDirA;
    private readonly string _outDirB;

    public BuildReproducibilityTests()
    {
        _outDirA = Directory.CreateTempSubdirectory("omr-build-repro-a-").FullName;
        _outDirB = Directory.CreateTempSubdirectory("omr-build-repro-b-").FullName;
    }

    public void Dispose()
    {
        TryDelete(_outDirA);
        TryDelete(_outDirB);
        GC.SuppressFinalize(this);
    }

    private static void TryDelete(string dir)
    {
        try
        {
            Directory.Delete(dir, recursive: true);
        }
        catch (IOException)
        {
            // Best-effort cleanup; a leftover temp dir doesn't fail the test.
        }
    }

    private static RoutingModelBuildOptions OptionsFor(string outDir)
    {
        var defaults = RoutingModelBuildOptions.Defaults(RepoRootFromMarker());
        return defaults with { OutDirectory = outDir, Version = "repro-test" };

        static string RepoRootFromMarker()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "OmnisRouter.slnx")))
            {
                dir = dir.Parent;
            }

            return dir?.FullName
                   ?? throw new InvalidOperationException("Could not locate repo root (OmnisRouter.slnx) for the reproducibility test.");
        }
    }

    [Fact]
    public void Same_inputs_produce_byte_identical_centroids_and_equal_policy()
    {
        var optionsA = OptionsFor(_outDirA);
        var optionsB = OptionsFor(_outDirB);

        var resultA = RoutingModelBuilder.Run(optionsA);
        var resultB = RoutingModelBuilder.Run(optionsB);

        var centroidsA = File.ReadAllBytes(resultA.CentroidsPath);
        var centroidsB = File.ReadAllBytes(resultB.CentroidsPath);
        Assert.Equal(centroidsA, centroidsB);

        var policyA = File.ReadAllText(resultA.PolicyPath);
        var policyB = File.ReadAllText(resultB.PolicyPath);
        Assert.Equal(policyA, policyB);

        // Sanity: both runs actually processed the same bundled dataset and agree on shape.
        Assert.Equal(resultA.PromptCount, resultB.PromptCount);
        Assert.Equal(resultA.K, resultB.K);
        Assert.Equal(resultA.LowConfidenceClusterCount, resultB.LowConfidenceClusterCount);
    }

    [Fact]
    public void Rerunning_into_the_same_directory_overwrites_with_identical_bytes()
    {
        var options = OptionsFor(_outDirA);

        var first = RoutingModelBuilder.Run(options);
        var firstCentroids = File.ReadAllBytes(first.CentroidsPath);
        var firstPolicy = File.ReadAllText(first.PolicyPath);

        var second = RoutingModelBuilder.Run(options);
        var secondCentroids = File.ReadAllBytes(second.CentroidsPath);
        var secondPolicy = File.ReadAllText(second.PolicyPath);

        Assert.Equal(firstCentroids, secondCentroids);
        Assert.Equal(firstPolicy, secondPolicy);
    }
}
