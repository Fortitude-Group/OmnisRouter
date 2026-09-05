using System.Security.Cryptography;
using System.Text;

namespace OmnisRouter.Collect;

/// <summary>
/// Wraps a secret (the OmnisVigil project key) with Windows DPAPI at <c>CurrentUser</c> scope, so it
/// is stored as ciphertext tied to the logged-in user and never appears in plain text or on a command
/// line (FR-011). DPAPI is Windows-only; the CLI never calls this (it takes <c>--key</c> directly),
/// so the library stays loadable cross-platform and this throws clearly if reached elsewhere.
/// </summary>
public static class ProtectedSecret
{
    public static string Protect(string plaintext)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("DPAPI secret protection is only available on Windows.");
        }

        var blob = ProtectedData.Protect(Encoding.UTF8.GetBytes(plaintext), optionalEntropy: null, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(blob);
    }

    public static string Unprotect(string protectedBase64)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("DPAPI secret protection is only available on Windows.");
        }

        var blob = ProtectedData.Unprotect(Convert.FromBase64String(protectedBase64), optionalEntropy: null, DataProtectionScope.CurrentUser);
        return Encoding.UTF8.GetString(blob);
    }
}
