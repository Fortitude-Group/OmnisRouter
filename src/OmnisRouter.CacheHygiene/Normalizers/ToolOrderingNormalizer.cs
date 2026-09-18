using OmnisRouter.Core.Model;

namespace OmnisRouter.CacheHygiene;

/// <summary>
/// Sorts tool definitions by name and canonicalises each tool's JSON schema key order. Safe: the wire
/// format presents tools as an unordered set (selection is by name), and JSON object key order is not
/// meaningful. The model is given the same set of tools.
/// </summary>
public sealed class ToolOrderingNormalizer : INormalizer
{
    public FixClass Class => FixClass.ToolOrdering;

    public bool TryNormalize(ChatRequest request, out ChatRequest normalised)
    {
        if (request.Tools.Count == 0)
        {
            normalised = request;
            return false;
        }

        var sorted = request.Tools
            .OrderBy(t => t.Name, StringComparer.Ordinal)
            .Select(t => t with { JsonSchema = JsonCanonicalizer.Canonicalize(t.JsonSchema) })
            .ToList();

        if (sorted.SequenceEqual(request.Tools))
        {
            normalised = request;
            return false;
        }

        normalised = request with { Tools = sorted };
        return true;
    }
}
