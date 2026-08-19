using OmnisRouter.Core.Model;

namespace OmnisRouter.Core.Abstractions;

/// <summary>A decrypted, ready-to-use upstream credential. Never logged; held only in-flight.</summary>
public sealed record ProviderCredential(Provider Provider, string ApiKey);

/// <summary>Calls one upstream provider. Streaming path uses ResponseHeadersRead and no retry (research.md R3).</summary>
public interface IUpstreamClient
{
    Provider Provider { get; }

    Task<ChatResponse> SendAsync(
        ChatRequest request, ModelRef model, ProviderCredential credential, CancellationToken cancellationToken);

    IAsyncEnumerable<NeutralStreamEvent> StreamAsync(
        ChatRequest request, ModelRef model, ProviderCredential credential, CancellationToken cancellationToken);
}

/// <summary>AES-256-GCM authenticated encryption for BYOK secrets at rest.</summary>
public interface ISecretCipher
{
    /// <summary>Returns <c>keyVersion ‖ nonce(12) ‖ tag(16) ‖ ciphertext</c>.</summary>
    byte[] Encrypt(ReadOnlySpan<byte> plaintext, ReadOnlySpan<byte> associatedData = default);

    byte[] Decrypt(ReadOnlySpan<byte> blob, ReadOnlySpan<byte> associatedData = default);
}

/// <summary>Supplies the master key for <see cref="ISecretCipher"/>. Local-file in v1; KMS in hosted mode.</summary>
public interface IMasterKeyProvider
{
    /// <summary>The current key and its version (for new encryptions).</summary>
    byte[] GetCurrentKey(out int keyVersion);

    /// <summary>A specific historical key version (for decrypting older rows during rotation).</summary>
    byte[] GetKey(int keyVersion);
}
