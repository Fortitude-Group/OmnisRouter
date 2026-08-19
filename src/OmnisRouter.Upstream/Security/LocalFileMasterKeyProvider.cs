using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using OmnisRouter.Core.Abstractions;

namespace OmnisRouter.Upstream.Security;

/// <summary>
/// Local-file <see cref="IMasterKeyProvider"/> for v1 (no external KMS). On first use, generates a
/// random 256-bit master key and persists it to <see cref="MasterKeyOptions.KeyFilePath"/>, restricting
/// file permissions to the current user where the OS supports it. Internally keyed by version so
/// future rotation only needs to add entries to the map; v1 always has exactly one version (1).
/// </summary>
public sealed class LocalFileMasterKeyProvider : IMasterKeyProvider
{
    private const int KeySizeBytes = 32; // 256-bit
    private const int CurrentKeyVersion = 1;

    private readonly MasterKeyOptions _options;
    private readonly object _sync = new();
    private IReadOnlyDictionary<int, byte[]>? _keysByVersion;

    public LocalFileMasterKeyProvider(MasterKeyOptions options)
    {
        _options = options;
    }

    public byte[] GetCurrentKey(out int keyVersion)
    {
        EnsureLoaded();
        keyVersion = CurrentKeyVersion;
        return _keysByVersion![CurrentKeyVersion];
    }

    public byte[] GetKey(int keyVersion)
    {
        EnsureLoaded();

        if (!_keysByVersion!.TryGetValue(keyVersion, out var key))
        {
            throw new KeyNotFoundException($"No master key material for version {keyVersion}.");
        }

        return key;
    }

    private void EnsureLoaded()
    {
        if (_keysByVersion is not null)
        {
            return;
        }

        lock (_sync)
        {
            if (_keysByVersion is not null)
            {
                return;
            }

            var keyFilePath = _options.KeyFilePath;
            byte[] key;

            if (File.Exists(keyFilePath))
            {
                key = File.ReadAllBytes(keyFilePath);

                if (key.Length != KeySizeBytes)
                {
                    throw new InvalidOperationException(
                        $"Master key file '{keyFilePath}' does not contain a {KeySizeBytes * 8}-bit key.");
                }
            }
            else
            {
                key = RandomNumberGenerator.GetBytes(KeySizeBytes);

                var directory = Path.GetDirectoryName(keyFilePath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllBytes(keyFilePath, key);
                RestrictPermissions(keyFilePath);
            }

            // v1 always has exactly one version; the map shape is what future rotation extends.
            _keysByVersion = new Dictionary<int, byte[]> { [CurrentKeyVersion] = key };
        }
    }

    private static void RestrictPermissions(string path)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                RestrictPermissionsWindows(path);
            }
            else if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
            {
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
        }
        catch
        {
            // Best-effort: some filesystems/OSes don't support ACL/mode restriction. The key file
            // is still written; permission hardening is a defense-in-depth layer, not a hard gate.
        }
    }

    [SupportedOSPlatform("windows")]
    private static void RestrictPermissionsWindows(string path)
    {
        var fileInfo = new FileInfo(path);
        var fileSecurity = new FileSecurity();
        fileSecurity.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);

        var currentUser = WindowsIdentity.GetCurrent().User;
        if (currentUser is not null)
        {
            fileSecurity.AddAccessRule(new FileSystemAccessRule(
                currentUser, FileSystemRights.FullControl, AccessControlType.Allow));
        }

        fileInfo.SetAccessControl(fileSecurity);
    }
}
