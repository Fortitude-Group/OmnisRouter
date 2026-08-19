namespace OmnisRouter.Upstream.Providers;

/// <summary>Configuration for the OpenAI upstream wire client.</summary>
public sealed class OpenAiUpstreamOptions
{
    /// <summary>Base URL for the OpenAI API. Must end with a trailing slash.</summary>
    public string BaseUrl { get; set; } = "https://api.openai.com/v1/";
}
