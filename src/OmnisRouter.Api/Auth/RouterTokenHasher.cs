using System.Security.Cryptography;
using System.Text;

namespace OmnisRouter.Api.Auth;

/// <summary>
/// Hashes router tokens for at-rest comparison against
/// <see cref="OmnisRouter.Store.Entities.RouterToken.HashedToken"/>.
/// <para>
/// v1 scheme: SHA-256 over the UTF-8 token bytes, rendered as lowercase hex. The
/// <c>RouterToken</c> entity doesn't pin a specific hashing scheme itself (it just documents that
/// the raw token is never stored), so this is the scheme its <c>HashedToken</c> column is expected
/// to hold; token issuance (US5 T059) must hash with this same helper.
/// </para>
/// </summary>
public static class RouterTokenHasher
{
    public static string Hash(string rawToken)
    {
        var bytes = Encoding.UTF8.GetBytes(rawToken);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexStringLower(hash);
    }
}
