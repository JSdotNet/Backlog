using Azure.Security.KeyVault.Secrets;
using Backlog.Modules.Sync.Api.Options;
using Microsoft.Extensions.Options;

namespace Backlog.Modules.Sync.Api.Security;

/// <summary>
/// The signing key from Key Vault, read with the service's managed identity
/// (local ADR 0052; the role is Key Vault Secrets User on the one vault).
/// <para>
/// Rotation is a new version of the one secret
/// (<see cref="SyncTokenOptions.SigningKeySecretName"/>), never a second
/// secret: the latest enabled version is the current key, and the newest
/// enabled version before it is the previous key. The previous key is accepted
/// until the current version's creation time plus one token lifetime plus one
/// refresh interval — an instance can keep signing with the old key until its
/// next refresh, and a token it minted then lives one lifetime more. After
/// that the previous key is not even fetched.
/// </para>
/// </summary>
internal sealed class KeyVaultSigningKeySource(SecretClient client, IOptions<SyncTokenOptions> options, TimeProvider clock)
    : ISigningKeySource
{
    public async Task<SigningKeys> LoadAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var name = SyncTokenOptions.SigningKeySecretName;

        KeyVaultSecret current = await client.GetSecretAsync(name, cancellationToken: cancellationToken).ConfigureAwait(false);
        var currentKey = Decode(current.Value);

        if (current.Properties.CreatedOn is not { } rotatedAt)
        {
            return new SigningKeys(currentKey);
        }

        var acceptedUntil = rotatedAt
            + TimeSpan.FromMinutes(settings.LifetimeMinutes)
            + TimeSpan.FromMinutes(settings.KeyRefreshMinutes);

        if (clock.GetUtcNow() >= acceptedUntil)
        {
            return new SigningKeys(currentKey);
        }

        SecretProperties? previous = null;
        await foreach (var version in client.GetPropertiesOfSecretVersionsAsync(name, cancellationToken).ConfigureAwait(false))
        {
            if (version.Enabled == true
                && version.Version != current.Properties.Version
                && version.CreatedOn < rotatedAt
                && (previous is null || version.CreatedOn > previous.CreatedOn))
            {
                previous = version;
            }
        }

        if (previous is null)
        {
            return new SigningKeys(currentKey);
        }

        KeyVaultSecret previousSecret = await client
            .GetSecretAsync(name, previous.Version, cancellationToken)
            .ConfigureAwait(false);

        return new SigningKeys(currentKey, Decode(previousSecret.Value), acceptedUntil);
    }

    private static byte[] Decode(string value) =>
        new SyncTokenOptions { SigningKey = value }.DecodeSigningKey();
}
