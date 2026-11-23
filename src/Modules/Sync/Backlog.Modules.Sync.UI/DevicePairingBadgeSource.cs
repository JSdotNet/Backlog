using Backlog.Infrastructure.Sync;
using Backlog.SharedKernel;

namespace Backlog.Modules.Sync.UI;

/// <summary>
/// The Devices page's value in the settings menu: <see cref="Paired"/> or
/// <see cref="NotPaired"/> — the state the page's headline says in a sentence.
/// </summary>
/// <remarks>
/// <para>
/// A device is paired while it holds a credential the sync service has not
/// rejected, the page's own rule; a credential the service refused, or a key this
/// machine cannot use, reads as not paired. Follows both the credential store and
/// the token provider's two verdicts, by passing their events through, so pairing,
/// forgetting the device, a refused credential and a lost key all move the badge.
/// </para>
/// <para>
/// A host that keeps no credential draws no badge.
/// </para>
/// </remarks>
public sealed class DevicePairingBadgeSource(
    IDeviceCredentialStore? credentials = null,
    SyncTokenProvider? tokens = null) : ISettingsBadgeSource
{
    /// <summary>The badge of a device that holds a credential the service accepts.</summary>
    public const string Paired = "Paired";

    /// <summary>The badge of a device with no credential, or one the service refused.</summary>
    public const string NotPaired = "Not paired";

    public string? Badge => credentials is null
        ? null
        : credentials.Current is not null && tokens?.CredentialRejected != true && tokens?.KeyUnavailable != true ? Paired : NotPaired;

    public event Action? Changed
    {
        add
        {
            if (credentials is not null) credentials.Changed += value;
            if (tokens is null) return;

            tokens.CredentialRejectedChanged += value;
            tokens.KeyUnavailableChanged += value;
        }
        remove
        {
            if (credentials is not null) credentials.Changed -= value;
            if (tokens is null) return;

            tokens.CredentialRejectedChanged -= value;
            tokens.KeyUnavailableChanged -= value;
        }
    }
}
