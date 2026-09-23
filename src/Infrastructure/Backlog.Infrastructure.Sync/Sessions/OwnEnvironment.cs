using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.SharedKernel;

namespace Backlog.Infrastructure.Sync.Sessions;

/// <summary>
/// Which of the held records are this machine's own, and the environment id they are
/// shown under.
/// <para>
/// The held set keeps this machine's records beside the other machines' so that a
/// session whose transcript is gone is still answered. The service stamps them with
/// the machine id it issued at pairing, which is not the installation identity the
/// local readers stamp; one environment would then read as two. This maps the one
/// onto the other, and leaves every other machine's id exactly as it arrived.
/// </para>
/// </summary>
internal sealed class OwnEnvironment(IDeviceCredentialStore? credentials, IDeviceIdentitySource? identity)
{
    /// <summary>The local identity for a record of this machine's, or null to keep
    /// the record's own machine id.</summary>
    public string? EnvironmentFor(SessionRecordEntry entry) =>
        identity is not null && credentials?.Current is { } me && entry.MachineId == me.DeviceId
            ? identity.Current.Id.ToString()
            : null;
}
