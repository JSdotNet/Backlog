using Azure;
using Azure.Security.KeyVault.Secrets;

namespace Backlog.Modules.Sync.Api.Security;

/// <summary>
/// Named vault secrets read once at startup into configuration keys of the
/// service's choosing, so a secret's vault name and the setting it lands on are
/// written down here rather than derived from a naming convention.
/// <para>
/// A secret the vault does not hold yet is left out rather than failing the
/// start: <c>github-oauth-client-secret</c> is stored by the person after the
/// GitHub OAuth App exists, and until then nothing reads
/// <c>GitHub:ClientSecret</c>. The signing key is not read here — it has
/// versions and a rotation window, which <see cref="KeyVaultSigningKeySource"/>
/// handles.
/// </para>
/// </summary>
internal sealed class KeyVaultSecretsConfigurationSource(SecretClient client) : IConfigurationSource
{
    /// <summary>Vault secret name to configuration key. <c>GitHub:ClientSecret</c>
    /// is <c>GitHub__ClientSecret</c> as an environment variable.</summary>
    internal static readonly IReadOnlyDictionary<string, string> Secrets = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["github-oauth-client-secret"] = "GitHub:ClientSecret",
    };

    public IConfigurationProvider Build(IConfigurationBuilder builder) => new Provider(client);

    private sealed class Provider(SecretClient client) : ConfigurationProvider
    {
        public override void Load()
        {
            var data = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

            foreach (var (secretName, key) in Secrets)
            {
                try
                {
                    data[key] = client.GetSecret(secretName).Value.Value;
                }
                catch (RequestFailedException failure) when (failure.Status == 404)
                {
                    // Not stored yet; see the class remarks.
                }
            }

            Data = data;
        }
    }
}
