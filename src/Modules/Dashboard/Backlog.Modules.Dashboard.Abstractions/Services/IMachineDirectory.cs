namespace Backlog.Modules.Dashboard.Abstractions.Services;

/// <summary>One machine the filter can offer.</summary>
/// <param name="Id">The stable identifier, and what
/// <see cref="DashboardScope.MachineId"/> holds. Not the name: a machine can be
/// renamed and two machines can share a name, so a filter keyed on the name would
/// quietly merge or split what it is filtering.</param>
/// <param name="Name">What a person recognises in a list — what the operating system
/// currently calls the box.</param>
public sealed record DashboardMachine(string Id, string Name);

/// <summary>
/// PORT — which machines the filter can offer.
/// <para>
/// The dashboard does not own the list of machines any more than it owns the list of
/// repositories, so it asks. Today the adapter answers with exactly one row — this
/// one — because a session record has not left this device yet, and offering machines
/// nobody has any figures for would be a filter that can only ever empty the surface.
/// The port is what lets that widen without the pane changing: when ADR 0005's
/// replication lands, the adapter unions this device with the machines seen in the
/// records that arrived.
/// </para>
/// </summary>
public interface IMachineDirectory
{
    IReadOnlyList<DashboardMachine> Machines { get; }
}
