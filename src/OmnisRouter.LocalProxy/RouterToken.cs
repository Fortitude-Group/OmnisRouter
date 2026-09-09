using System.Security.Cryptography;

namespace OmnisRouter.LocalProxy;

/// <summary>Generates the single bearer credential for the local router (data-model.md,
/// RouterManagementToken). Generated once by the tray on first enable.</summary>
public static class RouterToken
{
    private const int EntropyBytes = 32;

    /// <summary>A high-entropy, URL-safe token: 32 random bytes, base64url, no padding.</summary>
    public static string Generate() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(EntropyBytes))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
}
