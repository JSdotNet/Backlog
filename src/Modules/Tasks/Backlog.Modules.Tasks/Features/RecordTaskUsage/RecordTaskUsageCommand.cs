using Backlog.SharedKernel.Changes;
using Backlog.SharedKernel.Handlers;

namespace Backlog.Modules.Tasks.Features.RecordTaskUsage;

/// <summary>
/// Notes that an entry was actually used for something — copied into a prompt,
/// handed to an agent. This is what makes the Productivity context's
/// <c>AIWorkLogged</c> signal possible (see <c>.devbook/domain/context-map.md</c>), so it
/// is recorded on the entry rather than left in a log nobody reads.
/// </summary>
public sealed record RecordTaskUsageCommand(Guid Id, string Action);

public sealed class RecordTaskUsageCommandHandler(ITaskRepository entries)
    : ICommandHandler<RecordTaskUsageCommand>
{
    public async Task Handle(RecordTaskUsageCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var entry = await entries.GetAsync(command.Id, cancellationToken);
        if (entry is null) return;

        entry.RecordUsage(command.Action);

        // Noting a use changes nothing a person would take back, so the change
        // journal skips it (local ADR 0061 §2).
        using (ChangeContext.Unjournalled())
        {
            await entries.SaveAsync(entry, cancellationToken);
        }
    }
}
