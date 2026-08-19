using System.Text;

namespace OmnisRouter.RoutingModel.Build.Build;

/// <summary>
/// Writes <c>centroids-&lt;ver&gt;.bin</c> in the format described by <c>routing/FORMAT.md</c> --
/// the same little-endian layout <c>SeedModelGenerator</c> uses (magic "OMRC", format version, k,
/// dim, then k*dim row-major float32 components) so the loader never special-cases seed vs. built
/// artifacts.
/// </summary>
internal static class CentroidsBinaryWriter
{
    private const string BinaryMagic = "OMRC";
    private const int BinaryFormatVersion = 1;

    public static void Write(string path, float[][] centroids, int k, int dim)
    {
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var writer = new BinaryWriter(stream);

        writer.Write(Encoding.ASCII.GetBytes(BinaryMagic));
        writer.Write(BinaryFormatVersion);
        writer.Write(k);
        writer.Write(dim);

        foreach (var centroid in centroids)
        {
            foreach (var component in centroid)
            {
                writer.Write(component);
            }
        }
    }
}
