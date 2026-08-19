namespace OmnisRouter.Upstream.Providers;

/// <summary>Configuration for the Anthropic upstream wire client.</summary>
public sealed class AnthropicUpstreamOptions
{
    /// <summary>Base URL for the Anthropic API. Must end with a trailing slash.</summary>
    public string BaseUrl { get; set; } = "https://api.anthropic.com/";

    /// <summary>Anthropic Messages API version, sent as the <c>anthropic-version</c> header on every request.</summary>
    public string AnthropicVersion { get; set; } = "2023-06-01";
}
