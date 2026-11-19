using Backlog.Modules.Sync.Api.Options;
using Microsoft.Extensions.Options;

namespace Backlog.Modules.Sync.Api.Security;

/// <summary>
/// The signing key from configuration — user-secrets, or the key a Development
/// run generates for itself. Only a Development host uses it: anywhere else the
/// options refuse to start without <see cref="SyncTokenOptions.KeyVaultUri"/>.
/// One key and no previous one, because there is no rotation to bridge.
/// </summary>
internal sealed class ConfigurationSigningKeySource(IOptions<SyncTokenOptions> options) : ISigningKeySource
{
    public Task<SigningKeys> LoadAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new SigningKeys(options.Value.DecodeSigningKey()));
}
