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
/// repositories, so it asks. Every option it offers has to be a machine the figures
/// behind the surface can account for; a machine nobody has figures for is a filter
/// option that can only ever empty the screen. So the answer is this device unioned
/// with the machines named by the records that arrived — which is what ADR 0005's
/// replication made possible and what this port was shaped for.
/// </para>
/// <para>
/// <strong>A method, and asynchronous, unlike <see cref="IRepositoryDirectory"/>
/// beside it.</strong> That one reads a settings store already in memory; this one
/// cannot be answered without reading the records, and the read is the same one
/// <see cref="IAssistantSessionSource"/> does. A property would leave an adapter with
/// two ways out and both are dishonest: block on the read inside a getter, or answer
/// from a cache filled at some earlier moment and let a property that looks free
/// claim a currency it does not have. The shape of the contract says which of those
/// it is, so it says the true one — a caller can see it costs something, and can pass
/// the token that stops it.
/// </para>
/// <para>
/// Consequently the pane reads this once when it opens rather than on every render. A
/// machine whose first record arrives while the dashboard is open turns up the next
/// time it is opened, which is the same currency every other figure on that surface
/// has: nothing here polls.
/// </para>
/// </summary>
public interface IMachineDirectory
{
    /// <summary>Every machine the filter may offer, this device first.</summary>
    Task<IReadOnlyList<DashboardMachine>> GetMachinesAsync(CancellationToken cancellationToken = default);
}
