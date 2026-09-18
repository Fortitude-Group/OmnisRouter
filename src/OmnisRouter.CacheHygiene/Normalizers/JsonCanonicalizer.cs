using System.Text.Json;
using System.Text.Json.Nodes;

namespace OmnisRouter.CacheHygiene;

/// <summary>
/// Canonicalises a JSON document's object key order (recursively), preserving array order. Object key
/// order is never semantically meaningful in JSON, so this is provably safe; array order can be
/// meaningful, so it is left untouched. Returns the input unchanged if it is not parseable JSON.
/// </summary>
internal static class JsonCanonicalizer
{
    public static string Canonicalize(string json)
    {
        try
        {
            var node = JsonNode.Parse(json);
            return node is null ? json : Sort(node).ToJsonString();
        }
        catch (JsonException)
        {
            return json;   // not JSON we can safely reorder — leave it alone
        }
    }

    private static JsonNode Sort(JsonNode node) => node switch
    {
        JsonObject obj => SortObject(obj),
        JsonArray arr => SortArray(arr),
        _ => node.DeepClone(),
    };

    private static JsonObject SortObject(JsonObject obj)
    {
        var result = new JsonObject();
        foreach (var pair in obj.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            result[pair.Key] = pair.Value is null ? null : Sort(pair.Value);
        }

        return result;
    }

    private static JsonArray SortArray(JsonArray arr)
    {
        var result = new JsonArray();
        foreach (var item in arr)
        {
            result.Add(item is null ? null : Sort(item));   // order preserved
        }

        return result;
    }
}
