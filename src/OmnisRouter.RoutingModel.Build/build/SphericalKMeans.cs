namespace OmnisRouter.RoutingModel.Build.Build;

/// <summary>Result of fitting <see cref="SphericalKMeans"/>: final unit-length centroids and the cluster assignment per input vector.</summary>
public sealed record KMeansResult(float[][] Centroids, int[] Assignments);

/// <summary>
/// Deterministic spherical k-means: assigns by cosine similarity (a plain dot product, since every
/// input/centroid vector is unit-length) and updates each centroid as the mean of its assigned
/// vectors, re-normalized to unit length. Fully reproducible given the same inputs, <paramref
/// name="seed"/> is the only source of "randomness" (initial centroid selection), and every tie
/// (nearest-centroid ties, empty-cluster reseeding) is broken by lowest index so two runs over the
/// same data always converge to byte-identical centroids (T064).
/// </summary>
public static class SphericalKMeans
{
    public static KMeansResult Fit(IReadOnlyList<float[]> vectors, int k, int dimension, int seed, int maxIterations = 100)
    {
        if (vectors.Count < k)
        {
            throw new InvalidOperationException(
                $"Cannot fit k={k} clusters from only {vectors.Count} prompts; add more dataset rows or lower --k.");
        }

        var centroids = InitializeCentroids(vectors, k, dimension, seed);
        var assignments = new int[vectors.Count];

        for (var iteration = 0; iteration < maxIterations; iteration++)
        {
            var changed = AssignAll(vectors, centroids, assignments);
            var reseeded = UpdateCentroids(vectors, assignments, centroids, k, dimension);

            if (!changed && !reseeded)
            {
                break;
            }
        }

        // Final assignment pass so `assignments` always matches the returned `centroids` exactly
        // (the loop above can end mid-update after a reseed).
        AssignAll(vectors, centroids, assignments);

        return new KMeansResult(centroids, assignments);
    }

    private static float[][] InitializeCentroids(IReadOnlyList<float[]> vectors, int k, int dimension, int seed)
    {
        // Deterministic seeded Fisher-Yates shuffle of indices, then take the first k as initial
        // centroids. Same seed + same vectors -> same initial centroids, every time.
        var indices = new int[vectors.Count];
        for (var i = 0; i < indices.Length; i++)
        {
            indices[i] = i;
        }

        var rng = new Random(seed);
        for (var i = indices.Length - 1; i > 0; i--)
        {
            var j = rng.Next(i + 1);
            (indices[i], indices[j]) = (indices[j], indices[i]);
        }

        var centroids = new float[k][];
        for (var c = 0; c < k; c++)
        {
            centroids[c] = (float[])vectors[indices[c]].Clone();
        }

        _ = dimension; // vectors are already `dimension`-wide; kept for signature clarity/callers.
        return centroids;
    }

    /// <summary>Assigns every vector to its nearest centroid (cosine, i.e. max dot product). Returns true if any assignment changed.</summary>
    private static bool AssignAll(IReadOnlyList<float[]> vectors, float[][] centroids, int[] assignments)
    {
        var changed = false;
        for (var i = 0; i < vectors.Count; i++)
        {
            var best = NearestCentroid(vectors[i], centroids);
            if (assignments[i] != best)
            {
                changed = true;
            }

            assignments[i] = best;
        }

        return changed;
    }

    private static int NearestCentroid(float[] vector, float[][] centroids)
    {
        var best = 0;
        var bestSim = double.NegativeInfinity;
        for (var c = 0; c < centroids.Length; c++)
        {
            var sim = Dot(vector, centroids[c]);
            if (sim > bestSim) // strict '>' + ascending scan => deterministic lowest-index tie-break
            {
                bestSim = sim;
                best = c;
            }
        }

        return best;
    }

    /// <summary>Recomputes each centroid as the normalized mean of its assigned vectors. Returns true if any empty cluster was reseeded.</summary>
    private static bool UpdateCentroids(IReadOnlyList<float[]> vectors, int[] assignments, float[][] centroids, int k, int dimension)
    {
        var sums = new double[k][];
        var counts = new int[k];
        for (var c = 0; c < k; c++)
        {
            sums[c] = new double[dimension];
        }

        for (var i = 0; i < vectors.Count; i++)
        {
            var c = assignments[i];
            counts[c]++;
            var v = vectors[i];
            for (var d = 0; d < dimension; d++)
            {
                sums[c][d] += v[d];
            }
        }

        var reseeded = false;
        for (var c = 0; c < k; c++)
        {
            if (counts[c] == 0)
            {
                // Deterministic empty-cluster reseed: hand this centroid the point currently farthest
                // (lowest cosine similarity) from its own assigned centroid, breaking ties by lowest index.
                var farthest = FarthestFromOwnCentroid(vectors, assignments, centroids);
                centroids[c] = (float[])vectors[farthest].Clone();
                assignments[farthest] = c;
                reseeded = true;
                continue;
            }

            var mean = new float[dimension];
            for (var d = 0; d < dimension; d++)
            {
                mean[d] = (float)(sums[c][d] / counts[c]);
            }

            Normalize(mean);
            centroids[c] = mean;
        }

        return reseeded;
    }

    private static int FarthestFromOwnCentroid(IReadOnlyList<float[]> vectors, int[] assignments, float[][] centroids)
    {
        var farthestIndex = 0;
        var worstSim = double.PositiveInfinity;
        for (var i = 0; i < vectors.Count; i++)
        {
            var sim = Dot(vectors[i], centroids[assignments[i]]);
            if (sim < worstSim)
            {
                worstSim = sim;
                farthestIndex = i;
            }
        }

        return farthestIndex;
    }

    private static double Dot(float[] a, float[] b)
    {
        double sum = 0;
        for (var i = 0; i < a.Length; i++)
        {
            sum += a[i] * b[i];
        }

        return sum;
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
            vector[0] = 1f;
            return;
        }

        for (var i = 0; i < vector.Length; i++)
        {
            vector[i] = (float)(vector[i] / norm);
        }
    }
}
