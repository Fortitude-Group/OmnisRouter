using OmnisRouter.Core.Abstractions;

namespace OmnisRouter.Routing.Embedding;

/// <summary>
/// Deterministic, dependency-free fallback embedder for dev/CI when the pinned ONNX model asset is
/// not present. It produces a stable unit vector from a hashed bag-of-tokens — enough to exercise the
/// full routing pipeline, but NOT semantically meaningful. Production uses <see cref="OnnxEmbedder"/>.
/// </summary>
public sealed class HashingEmbedder(int dimension = 384) : IEmbedder
{
    public int Dimension => dimension;

    public float[] Embed(string text)
    {
        var vector = new float[dimension];
        foreach (var token in Tokenize(text))
        {
            var h = Fnv1a(token);
            var index = (int)(h % (uint)dimension);
            var sign = (h & 1) == 0 ? 1f : -1f;
            vector[index] += sign;
        }

        Normalize(vector);
        return vector;
    }

    private static IEnumerable<string> Tokenize(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            yield return "\0empty";
            yield break;
        }

        foreach (var token in text.ToLowerInvariant().Split(
                     [' ', '\t', '\n', '\r', '.', ',', ';', ':', '!', '?', '(', ')', '[', ']', '{', '}', '"', '\''],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            yield return token;
        }
    }

    private static uint Fnv1a(string s)
    {
        const uint offset = 2166136261;
        const uint prime = 16777619;
        var hash = offset;
        foreach (var c in s)
        {
            hash ^= c;
            hash *= prime;
        }

        return hash;
    }

    private static void Normalize(float[] v)
    {
        double sumSq = 0;
        foreach (var x in v)
        {
            sumSq += x * (double)x;
        }

        var norm = Math.Sqrt(sumSq);
        if (norm <= 0)
        {
            v[0] = 1f; // avoid a zero vector (undefined cosine)
            return;
        }

        for (var i = 0; i < v.Length; i++)
        {
            v[i] = (float)(v[i] / norm);
        }
    }
}
