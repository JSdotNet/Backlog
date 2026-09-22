using Backlog.Modules.Sessions.Abstractions;
using Backlog.SharedKernel;

namespace Backlog.Modules.Sessions.UI.Adapters;

/// <summary>
/// The delivery runs on this machine: what the dashboard servers left in the profile
/// of whoever is signed in.
/// <para>
/// A thin port implementation over <see cref="DeliveryRunReader"/>, on the shape
/// <see cref="LocalAgentSessionSource"/> has: the reader takes a folder so a test can
/// hand it a fixture, and this class is the one that knows the folder is
/// <c>~/.claude</c>. Every run is stamped with this device's identity, for the reason
/// <see cref="LocalAgentSessionSource"/> stamps its sessions: a run file names no
/// machine any more than a transcript does, and a file read off this disk was written
/// on it. A run that becomes a row of its own needs that fact as much as a session
/// row does — it is what a machine filter and a grouping by environment read.
/// </para>
/// </summary>
internal sealed class LocalDeliveryRunSource : IDeliveryRunSource
{
    private readonly DeliveryRunReader _reader;

    /// <summary>What a host composes: the dashboards' folders under the signed-in
    /// profile, and this device's identity. Both dashboards write beside Claude's own
    /// files, which is why this is the Claude home and not a folder of its own.</summary>
    internal LocalDeliveryRunSource(IDeviceIdentitySource identity)
        : this(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude"),
            IdOf(identity),
            identity.Current.Name)
    {
    }

    /// <summary>Every input named, so the reading can be tested against a fixture.</summary>
    internal LocalDeliveryRunSource(string home, string environmentId, string environment)
    {
        _reader = new DeliveryRunReader(home, environmentId, environment);
    }

    /// <summary>The Guid in its plain "D" form, spelled the way the session source
    /// spells it: a row that filters by environment compares these ordinally, and a
    /// run and a session on one machine must agree on the machine.</summary>
    private static string IdOf(IDeviceIdentitySource identity)
    {
        ArgumentNullException.ThrowIfNull(identity);

        return identity.Current.Id.ToString();
    }

    public Task<DeliveryRunCatalog> GetRunsAsync(CancellationToken cancellationToken = default) =>
        _reader.ReadAsync(cancellationToken);
}
