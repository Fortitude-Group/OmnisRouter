namespace OmnisRouter.LocalProxy;

/// <summary>
/// The tray's links to OmnisVigil. Each place that links carries its own <c>utm_medium</c>, so the
/// hosted /signup endpoint (which logs utm_source and utm_medium per click) can count which place
/// sends people: <c>tray</c> for the menu item, <c>receipt</c> for the line under the savings in the
/// popup, <c>setup</c> for the "Get a key" button. Nothing is tracked in the app itself.
/// </summary>
public static class VigilLinks
{
    /// <summary>The hosted OmnisVigil host. Only links to it are tagged.</summary>
    public const string HostedHost = "app.omnisvigil.com";

    public const string Tray = "tray";
    public const string Receipt = "receipt";
    public const string Setup = "setup";

    /// <summary>The hosted sign-up link, tagged with where the click came from.</summary>
    public static string Signup(string medium) =>
        $"https://{HostedHost}/signup?utm_source=omnisrouter&utm_medium={Uri.EscapeDataString(medium)}";

    /// <summary>
    /// Where "Get a key" goes: the tagged sign-up link when the dashboard is the hosted one (or blank),
    /// otherwise the self-hosted dashboard URL as typed, untouched.
    /// </summary>
    public static string GetKey(string? endpoint, string medium)
    {
        var trimmed = endpoint?.Trim() ?? string.Empty;
        if (trimmed.Length == 0 || IsHosted(trimmed))
        {
            return Signup(medium);
        }

        return trimmed;
    }

    private static bool IsHosted(string endpoint) =>
        Uri.TryCreate(endpoint, UriKind.Absolute, out var uri)
        && string.Equals(uri.Host, HostedHost, StringComparison.OrdinalIgnoreCase);
}
