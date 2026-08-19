using System.Text;
using OmnisRouter.Core.Abstractions;

namespace OmnisRouter.Routing.Tests.Stubs;

/// <summary>
/// Deterministic stub of <see cref="IEmbedder"/> for routing tests. Derives a fixed, reproducible
/// 384-dim unit vector from the input text's stable hash so routing tests never depend on the real
/// ONNX embedder (T024) and always yield the same nearest-centroid decision for the same input,
/// across machines and process runs.
/// </summary>
public sealed class DeterministicEmbedder : IEmbedder
{
    public int Dimension => 384;

    public float[] Embed(string text)
    {
        var seed = StableHash(text ?? string.Empty);
        var rng = new Random(seed);

        var vector = new float[Dimension];
        for (var i = 0; i < Dimension; i++)
        {
            // NextDouble() is in [0, 1); shift to [-1, 1) so the raw vector isn't all-positive.
            vector[i] = (float)((rng.NextDouble() * 2.0) - 1.0);
        }

        Normalize(vector);
        return vector;
    }

    /// <summary>
    /// FNV-1a over UTF-8 bytes. Deliberately not <c>string.GetHashCode()</c>, which is randomized
    /// per-process in .NET and would break reproducibility across test runs.
    /// </summary>
    private static int StableHash(string text)
    {
        unchecked
        {
            const uint fnvOffsetBasis = 2166136261;
            const uint fnvPrime = 16777619;

            var hash = fnvOffsetBasis;
            foreach (var b in Encoding.UTF8.GetBytes(text))
            {
                hash ^= b;
                hash *= fnvPrime;
            }

            return (int)hash;
        }
    }

    private static void Normalize(float[] vector)
    {
        var sumSquares = 0.0;
        foreach (var v in vector)
        {
            sumSquares += (double)v * v;
        }

        var norm = Math.Sqrt(sumSquares);
        if (norm <= 0)
        {
            return;
        }

        for (var i = 0; i < vector.Length; i++)
        {
            vector[i] = (float)(vector[i] / norm);
        }
    }
}
