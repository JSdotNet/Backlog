using Backlog.Modules.DevPc.Abstractions;
using Backlog.Modules.Sessions.Abstractions;
using Backlog.SharedKernel;

namespace Backlog.Infrastructure.FileSystem.DevPc;

/// <summary>
/// The machines this installation knows: this device, and every machine that has
/// sent this one a session record (local ADR 0073 §7).
/// </summary>
/// <remarks>
/// <para>
/// A join of the kernel's device identity and Sessions' records, which is why it sits
/// here and not in Dev PC Management: the module owns the <see cref="Machine"/> and
/// asks; this answers. It replaces the machine directory Insights kept for its own
/// filter, so the filter now offers Dev PCs' machines.
/// </para>
/// <para>
/// <strong>The union is on the id, never on the name.</strong> Two machines can share
/// a name and one can be renamed. This device is seeded first, so a record it pushed
/// and received back never becomes a second row for it. A record whose environment id
/// is not a device identity names no machine and is passed over.
/// </para>
/// <para>
/// This device first, then the rest by name, then by id, so the list does not reorder
/// itself as sessions arrive. <see cref="Current"/> is built from the identity on
/// every read: the source holds the identity, so this pins nothing.
/// </para>
/// </remarks>
public sealed class SessionMachineRegistry(IDeviceIdentitySource identity, IAgentSessionSource? sessions)
    : IMachineRegistry
{
    public Machine Current => Machine.FromDevice(identity.Current);

    public async Task<IReadOnlyList<Machine>> GetMachinesAsync(
        DateTimeOffset since,
        CancellationToken cancellationToken = default)
    {
        var local = Current;

        if (sessions is null) return [local];

        AgentSessionCatalog catalog;

        try
        {
            catalog = await sessions
                .GetSessionsAsync(AgentSessionQuery.Since(since), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // The reader closing the screen, not a source failing. Let it travel.
            throw;
        }
        catch (Exception)
        {
            // Narrower rather than empty: whoever reads the records for figures reports
            // the failure with its reason, and losing this device here would take the
            // reader's own machine off the list over a fault they are already told of.
            return [local];
        }

        var seen = new HashSet<MachineId> { local.Id };
        var replicated = new List<Machine>();

        foreach (var session in catalog.Sessions)
        {
            if (MachineId.TryParse(session.EnvironmentId, out var id) && seen.Add(id))
            {
                replicated.Add(new Machine(id, Named(session, id)));
            }
        }

        return
        [
            local,
            .. replicated
                .OrderBy(machine => machine.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(machine => machine.Id.ToString(), StringComparer.Ordinal)
        ];
    }

    /// <summary>The machine's own name, or its id where the record arrived without
    /// one: a machine nobody can tell from the next is no use on a list.</summary>
    private static string Named(AgentSession session, MachineId id) =>
        string.IsNullOrWhiteSpace(session.Environment) ? id.ToString() : session.Environment;
}
