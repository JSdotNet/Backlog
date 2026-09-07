using Backlog.Modules.Dashboard.Abstractions.Services;
using Backlog.SharedKernel;

namespace Backlog.Modules.Dashboard.UI.Adapters;

/// <summary>
/// The machines the filter can offer, which today is this one.
/// </summary>
/// <remarks>
/// <para>
/// One row, and that is the honest list rather than a placeholder. No session record
/// has left this device yet — ADR 0005's replication is not built — so any other
/// machine this directory named would be one the dashboard has no figures for, and a
/// filter whose options can only ever empty the surface is worse than a filter with one
/// option beside "All machines".
/// </para>
/// <para>
/// When records do replicate, the widening happens here: this adapter unions the local
/// device with the machines seen in the records that arrived. The port is what makes
/// that a change to one class rather than to the pane, the scope and the derivation.
/// </para>
/// <para>
/// The identity is read per call rather than captured, matching
/// <c>SettingsRepositoryDirectory</c> — the source itself reads once, so this costs
/// nothing and does not pin an answer the day the source stops being fixed.
/// </para>
/// </remarks>
public sealed class DeviceMachineDirectory(IDeviceIdentitySource identity) : IMachineDirectory
{
    public IReadOnlyList<DashboardMachine> Machines =>
        [new DashboardMachine(identity.Current.Id.ToString(), identity.Current.Name)];
}
