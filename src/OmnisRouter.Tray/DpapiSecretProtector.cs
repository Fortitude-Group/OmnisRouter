using OmnisRouter.Collect;
using OmnisRouter.LocalProxy;

namespace OmnisRouter.Tray;

/// <summary>
/// Windows DPAPI (<c>CurrentUser</c> scope) implementation of <see cref="ISecretProtector"/>, delegating
/// to the collector's existing <see cref="ProtectedSecret"/> wrapper so the router token and the
/// collector's project key are protected the same way, on the same machine-user pairing.
/// </summary>
internal sealed class DpapiSecretProtector : ISecretProtector
{
    public string Protect(string plaintext) => ProtectedSecret.Protect(plaintext);

    public string Unprotect(string protectedValue) => ProtectedSecret.Unprotect(protectedValue);
}
