using OmnisRouter.Core.Model;

namespace OmnisRouter.CacheHygiene;

/// <summary>
/// Applies a text transform to the cacheable prefix — every system block's text and each tool's
/// description and JSON schema — and rebuilds the request only if something changed. Message content is
/// never touched. Shared by the text normalisers so they can never disagree about what they rewrite.
/// </summary>
internal static class PrefixRewrite
{
    public static bool Apply(ChatRequest request, Func<string, string> transform, out ChatRequest normalised)
    {
        var changed = false;

        var system = new List<TextPart>(request.System.Count);
        foreach (var part in request.System)
        {
            var text = transform(part.Text);
            if (text != part.Text)
            {
                changed = true;
                system.Add(part with { Text = text });
            }
            else
            {
                system.Add(part);
            }
        }

        var tools = new List<Tool>(request.Tools.Count);
        foreach (var tool in request.Tools)
        {
            var description = transform(tool.Description);
            var schema = transform(tool.JsonSchema);
            if (description != tool.Description || schema != tool.JsonSchema)
            {
                changed = true;
                tools.Add(tool with { Description = description, JsonSchema = schema });
            }
            else
            {
                tools.Add(tool);
            }
        }

        if (!changed)
        {
            normalised = request;
            return false;
        }

        normalised = request with { System = system, Tools = tools };
        return true;
    }
}
