using Microsoft.Extensions.DependencyInjection;
using OmnisRouter.Core.Abstractions;

namespace OmnisRouter.Upstream.Security;

public static class ServiceCollectionExtensions
{
    /// <summary>Registers the local-file BYOK cipher (<see cref="ISecretCipher"/> + <see cref="IMasterKeyProvider"/>).</summary>
    public static IServiceCollection AddOmnisByok(
        this IServiceCollection services, Action<MasterKeyOptions>? configureMasterKey = null)
    {
        var options = new MasterKeyOptions();
        configureMasterKey?.Invoke(options);

        services.AddSingleton(options);
        services.AddSingleton<IMasterKeyProvider, LocalFileMasterKeyProvider>();
        services.AddSingleton<ISecretCipher, AesGcmSecretCipher>();

        return services;
    }
}
