using System.Security.Cryptography;
using System.Text;
using OmnisRouter.Core.Model;

namespace OmnisRouter.CacheHygiene;

/// <summary>
/// Builds the stable cacheable prefix from a neutral request — the system blocks and tool definitions,
/// which is what Anthropic caches and where the CRLF, trailing-whitespace, and tool-ordering drift live
/// (the wild wins CacheScope found in instruction files). Returns null when the request is not using
/// caching, so a non-cacheable request is skipped (spec edge case). Uses control characters as
/// delimiters so content can never collide with a separator.
/// </summary>
public static class PrefixExtractor
{
    private const char Unit = '';    // between tool fields
    private const char Record = '';  // between blocks/tools
    private const char Group = '';   // between the system group and the tools group

    public static byte[]? Extract(ChatRequest request)
    {
        if (!UsesCaching(request))
        {
            return null;
        }

        var sb = new StringBuilder();
        foreach (var block in request.System)
        {
            sb.Append(block.Text).Append(Record);
        }

        sb.Append(Group);

        foreach (var tool in request.Tools)
        {
            sb.Append(tool.Name).Append(Unit)
              .Append(tool.Description).Append(Unit)
              .Append(tool.JsonSchema).Append(Record);
        }

        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    /// <summary>The lineage key: the caller's session identity, else a structure-stable hash (research D4).</summary>
    public static string LineageKey(ChatRequest request)
    {
        if (!string.IsNullOrEmpty(request.SessionId))
        {
            return request.SessionId;
        }

        // No session id: key on the tool set + model, which stays stable across content drift so the
        // lineage still groups the run (never key on the prefix itself — that changes on every drift).
        var signature = string.Join('|', request.Tools.Select(t => t.Name)) + "#" + (request.Model ?? string.Empty);
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(signature)));
        return "struct:" + hash[..16];
    }

    private static bool UsesCaching(ChatRequest request) =>
        (request.CapabilitiesUsed & RequestCapabilities.CachePinGuaranteed) != 0
        || request.System.Any(p => p.Cache is not null)
        || request.Messages.Any(m => m.Parts.OfType<TextPart>().Any(p => p.Cache is not null));
}
