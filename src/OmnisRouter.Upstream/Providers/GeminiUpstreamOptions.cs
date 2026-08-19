namespace OmnisRouter.Upstream.Providers;

/// <summary>Configuration for the Gemini upstream wire client.</summary>
public sealed class GeminiUpstreamOptions
{
    /// <summary>Base URL for the Gemini API. Must end with a trailing slash.</summary>
    public string BaseUrl { get; set; } = "https://generativelanguage.googleapis.com/";
}
