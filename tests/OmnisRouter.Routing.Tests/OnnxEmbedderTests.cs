using OmnisRouter.Routing;
using OmnisRouter.Routing.Embedding;

namespace OmnisRouter.Routing.Tests;

/// <summary>
/// Exercises the real pinned ONNX embedder (bge-small-en-v1.5, int8) when the model asset is present.
/// Skips cleanly when it isn't (CI without the asset), so it never blocks the suite.
/// </summary>
public class OnnxEmbedderTests
{
    private static string? ModelPath()
    {
        var model = RepoLocator.Resolve(Path.Combine("models", "bge-small-en-v1.5", "model.onnx"));
        var vocab = RepoLocator.Resolve(Path.Combine("models", "bge-small-en-v1.5", "vocab.txt"));
        return File.Exists(model) && File.Exists(vocab) ? model : null;
    }

    private static OnnxEmbedder Create() => new(new OnnxEmbedderOptions
    {
        ModelPath = RepoLocator.Resolve(Path.Combine("models", "bge-small-en-v1.5", "model.onnx")),
        VocabPath = RepoLocator.Resolve(Path.Combine("models", "bge-small-en-v1.5", "vocab.txt")),
    });

    private static double Cosine(float[] a, float[] b)
    {
        double dot = 0;
        for (var i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
        }

        return dot; // both are unit-length
    }

    [Fact]
    public void Produces_384d_unit_vectors_that_are_semantically_meaningful()
    {
        if (ModelPath() is null)
        {
            return; // asset not present — skip
        }

        using var embedder = Create();
        Assert.Equal(384, embedder.Dimension);

        var code = embedder.Embed("Write a Python function that reverses a linked list.");
        var codeRelated = embedder.Embed("Implement an algorithm in Python to invert a singly linked list.");
        var unrelated = embedder.Embed("What is the best time of year to visit the Amalfi coast?");

        // Unit length.
        Assert.InRange(Math.Sqrt(code.Sum(x => (double)x * x)), 0.99, 1.01);

        // Semantically related coding prompts are closer than an unrelated travel prompt.
        var simRelated = Cosine(code, codeRelated);
        var simUnrelated = Cosine(code, unrelated);
        Assert.True(simRelated > simUnrelated,
            $"expected related ({simRelated:F3}) > unrelated ({simUnrelated:F3})");
        Assert.True(simRelated > 0.6, $"related coding prompts should be clearly similar, was {simRelated:F3}");
    }

    [Fact]
    public void Is_deterministic()
    {
        if (ModelPath() is null)
        {
            return;
        }

        using var embedder = Create();
        var a = embedder.Embed("deterministic check");
        var b = embedder.Embed("deterministic check");
        Assert.Equal(a, b);
    }
}
