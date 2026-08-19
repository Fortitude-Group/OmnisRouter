using System.Security.Cryptography;
using OmnisRouter.Core.Abstractions;

namespace OmnisRouter.Upstream.Security;

/// <summary>
/// AES-256-GCM authenticated encryption for BYOK secrets at rest. Blob layout:
/// <c>keyVersion(4, little-endian int32) ‖ nonce(12) ‖ tag(16) ‖ ciphertext</c>. A fresh random
/// 12-byte nonce is generated for every call to <see cref="Encrypt"/>.
/// </summary>
public sealed class AesGcmSecretCipher : ISecretCipher
{
    private const int KeyVersionLength = sizeof(int);
    private const int NonceLength = 12;
    private const int TagLength = 16;

    private readonly IMasterKeyProvider _masterKeyProvider;

    public AesGcmSecretCipher(IMasterKeyProvider masterKeyProvider)
    {
        _masterKeyProvider = masterKeyProvider;
    }

    public byte[] Encrypt(ReadOnlySpan<byte> plaintext, ReadOnlySpan<byte> associatedData = default)
    {
        var key = _masterKeyProvider.GetCurrentKey(out var keyVersion);

        Span<byte> nonce = stackalloc byte[NonceLength];
        RandomNumberGenerator.Fill(nonce);

        var ciphertext = new byte[plaintext.Length];
        Span<byte> tag = stackalloc byte[TagLength];

        using (var aesGcm = new AesGcm(key, TagLength))
        {
            aesGcm.Encrypt(nonce, plaintext, ciphertext, tag, associatedData);
        }

        var blob = new byte[KeyVersionLength + NonceLength + TagLength + ciphertext.Length];
        var offset = 0;

        WriteKeyVersion(blob.AsSpan(offset, KeyVersionLength), keyVersion);
        offset += KeyVersionLength;

        nonce.CopyTo(blob.AsSpan(offset, NonceLength));
        offset += NonceLength;

        tag.CopyTo(blob.AsSpan(offset, TagLength));
        offset += TagLength;

        ciphertext.CopyTo(blob.AsSpan(offset));

        return blob;
    }

    public byte[] Decrypt(ReadOnlySpan<byte> blob, ReadOnlySpan<byte> associatedData = default)
    {
        if (blob.Length < KeyVersionLength + NonceLength + TagLength)
        {
            throw new CryptographicException("Encrypted blob is too short to contain a valid header.");
        }

        var offset = 0;

        var keyVersion = ReadKeyVersion(blob.Slice(offset, KeyVersionLength));
        offset += KeyVersionLength;

        var nonce = blob.Slice(offset, NonceLength);
        offset += NonceLength;

        var tag = blob.Slice(offset, TagLength);
        offset += TagLength;

        var ciphertext = blob[offset..];

        var key = _masterKeyProvider.GetKey(keyVersion);
        var plaintext = new byte[ciphertext.Length];

        using (var aesGcm = new AesGcm(key, TagLength))
        {
            aesGcm.Decrypt(nonce, ciphertext, tag, plaintext, associatedData);
        }

        return plaintext;
    }

    private static void WriteKeyVersion(Span<byte> destination, int keyVersion) =>
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(destination, keyVersion);

    private static int ReadKeyVersion(ReadOnlySpan<byte> source) =>
        System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(source);
}
