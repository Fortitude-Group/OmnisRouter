namespace OmnisRouter.LocalProxy;

/// <summary>
/// Seam over the platform secret store. The Windows DPAPI implementation (matching the collector's
/// <c>ProtectedSecret</c>) lives in the tray; this library only depends on the interface, so it stays
/// cross-platform and CI-tested (Principle III).
/// </summary>
public interface ISecretProtector
{
    string Protect(string plaintext);

    string Unprotect(string protectedValue);
}
