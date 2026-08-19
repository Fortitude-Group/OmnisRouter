using OmnisRouter.Routing.Tests.Stubs;

namespace OmnisRouter.Routing.Tests;

public class DeterministicEmbedderTests
{
    [Fact]
    public void Dimension_Is384()
    {
        var embedder = new DeterministicEmbedder();

        Assert.Equal(384, embedder.Dimension);
    }

    [Fact]
    public void Embed_IsDeterministicAcrossCalls()
    {
        var embedder = new DeterministicEmbedder();

        var first = embedder.Embed("route this request");
        var second = embedder.Embed("route this request");

        Assert.Equal(first, second);
    }

    [Fact]
    public void Embed_ReturnsUnitLengthVector()
    {
        var embedder = new DeterministicEmbedder();

        var vector = embedder.Embed("route this request");
        var norm = Math.Sqrt(vector.Sum(x => (double)x * x));

        Assert.Equal(384, vector.Length);
        Assert.InRange(norm, 0.999, 1.001);
    }

    [Fact]
    public void Embed_DiffersForDifferentText()
    {
        var embedder = new DeterministicEmbedder();

        var a = embedder.Embed("hello world");
        var b = embedder.Embed("goodbye world");

        Assert.NotEqual(a, b);
    }
}
