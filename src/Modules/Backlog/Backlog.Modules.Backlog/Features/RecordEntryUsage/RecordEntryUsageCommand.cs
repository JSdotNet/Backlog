using Backlog.SharedKernel.Handlers;

namespace Backlog.Modules.Backlog.Features.RecordEntryUsage;

/// <summary>
/// Notes that an entry was actually used for something — copied into a prompt,
/// handed to an agent. This is what makes the Productivity context's
/// <c>AIWorkLogged</c> signal possible (see <c>.domain/context-map.md</c>), so it
/// is recorded on the entry rather than left in a log nobody reads.
/// </summary>
public sealed record RecordEntryUsageCommand(Guid Id, string Action);

public sealed class RecordEntryUsageCommandHandler(IBacklogRepository entries)
    : ICommandHandler<RecordEntryUsageCommand>
{
    public async Task Handle(RecordEntryUsageCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var entry = await entries.GetAsync(command.Id, cancellationToken);
        if (entry is null) return;

        entry.RecordUsage(command.Action);
        await entries.SaveAsync(entry, cancellationToken);
    }
}
