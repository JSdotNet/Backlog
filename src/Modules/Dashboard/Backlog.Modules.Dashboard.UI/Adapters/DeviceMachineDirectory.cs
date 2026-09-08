using Backlog.Modules.Dashboard.Abstractions.Services;
using Backlog.SharedKernel;

namespace Backlog.Modules.Dashboard.UI.Adapters;

/// <summary>
/// The machines the filter can offer: this device, and every machine that has sent
/// this one a session record.
/// </summary>
/// <remarks>
/// <para>
/// This is the widening the one-row version of this adapter said would happen here.
/// It reads the same port the sessions part reads, so the options and the figures
/// cannot disagree: a machine is offered exactly when there are records behind it to
/// filter to, and a filter option that can only ever empty the surface is not
/// something this can produce.
/// </para>
/// <para>
/// <strong>The union is on the machine id, never on the name.</strong> Two machines
/// can be called the same thing and one machine can be renamed, so a union on the
/// name would offer one option that filters to half of what it names, or two options
/// for one machine. The local device is seeded into that set before the records are
/// read, which also settles the case a name could not: a record this device pushed
/// and later received back names this environment, and it must not turn into a second
/// row for the machine the reader is sitting at.
/// </para>
/// <para>
/// This device first, then the rest by name. Not by how many records each sent — a
/// list that reorders itself as sessions arrive makes the reader re-find the option
/// they were about to choose — and this device leads because it is the one they are
/// sitting at.
/// </para>
/// <para>
/// The identity is read per call rather than captured, matching
/// <c>SettingsRepositoryDirectory</c> — the source itself reads once, so this costs
/// nothing and does not pin an answer the day the source stops being fixed.
/// </para>
/// </remarks>
public sealed class DeviceMachineDirectory(IDeviceIdentitySource identity, IAssistantSessionSource sessions)
    : IMachineDirectory
{
    public async Task<IReadOnlyList<DashboardMachine>> GetMachinesAsync(
        CancellationToken cancellationToken = default)
    {
        var local = new DashboardMachine(identity.Current.Id.ToString(), identity.Current.Name);

        AssistantSessionReport report;

        try
        {
            report = await sessions.GetSessionsAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // The reader closing the dashboard, not a source failing. Let it travel.
            throw;
        }
        catch (Exception)
        {
            // Narrower rather than empty, and deliberately not swallowed: the sessions
            // part asks this same source and reports the failure on screen with its
            // reason. The filter's own job is to narrow what that part shows, so a
            // source that cannot answer leaves it with the one machine it can name
            // without the source at all. Losing this device here would take the filter
            // — and the reader's way back to "All machines" — down over a fault they
            // are already being told about.
            return [local];
        }

        var seen = new HashSet<string>(StringComparer.Ordinal) { local.Id };
        var replicated = new List<DashboardMachine>();

        foreach (var session in report.Sessions)
        {
            if (!string.IsNullOrWhiteSpace(session.MachineId) && seen.Add(session.MachineId))
            {
                replicated.Add(new DashboardMachine(session.MachineId, Named(session)));
            }
        }

        return
        [
            local,
            .. replicated
                .OrderBy(machine => machine.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(machine => machine.Id, StringComparer.Ordinal)
        ];
    }

    /// <summary>
    /// What the option is called. The machine's own name, and its id where the record
    /// arrived without one: an option with an empty label is one a reader cannot tell
    /// from the "All machines" line above it, and an id at least identifies the
    /// machine it filters to.
    /// </summary>
    private static string Named(AssistantSession session) =>
        string.IsNullOrWhiteSpace(session.MachineName) ? session.MachineId : session.MachineName;
}
