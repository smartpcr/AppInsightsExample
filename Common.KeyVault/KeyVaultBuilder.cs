using System.IO;

namespace Common.KeyVault
{
    using System;
    using System.Security.Cryptography.X509Certificates;
    using Microsoft.Azure.KeyVault;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.IdentityModel.Clients.ActiveDirectory;


    /// <summary>
    /// inject <see cref="IKeyVaultClient"/>
    /// </summary>
    public static class KeyVaultBuilder
    {
        public static IServiceCollection AddKeyVault(this IServiceCollection services, IConfiguration configuration)
        {
            var vaultSettings = new VaultSettings();
            configuration.Bind("Vault", vaultSettings);
            KeyVaultClient.AuthenticationCallback callback = async (authority, resource, scope) =>
            {
                var authContext = new AuthenticationContext(authority);
                var clientCertFile = Path.Combine(
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".secrets"),
                    vaultSettings.ClientCertFile);
                var certificate = new X509Certificate2(clientCertFile);
                var clientCred = new ClientAssertionCertificate(vaultSettings.ClientId, certificate);
                var result = await authContext.AcquireTokenAsync(resource, clientCred);

                if (result == null)
                    throw new InvalidOperationException("Failed to obtain the JWT token");

                return result.AccessToken;
            };
            var kvClient = new KeyVaultClient(callback);
            services.AddSingleton<IKeyVaultClient>(kvClient);

            return services;
        }

        public static string GetSecret(this IServiceCollection services, IConfiguration configuration, string secretName)
        {
            var serviceProvider = services.BuildServiceProvider();
            var kvClient = serviceProvider.GetRequiredService<IKeyVaultClient>();
            var vaultSettings = new VaultSettings();
            configuration.Bind("Vault", vaultSettings);
            var instrumentationKey = kvClient.GetSecretAsync(
                $"https://{vaultSettings.Name}.vault.azure.net",
                secretName)
                .GetAwaiter().GetResult();
            return instrumentationKey.Value;
        }
    }
}