using Backlog.Modules.Tasks.Abstractions.DataTransferObjects;
using Backlog.Modules.Tasks.DomainModels;
using Backlog.Modules.Tasks.Services;
using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Tasks.Features.UnlinkTaskFromIssue;

/// <summary>
/// Forgets that an entry became something outside this system — the counterpart of
/// <see cref="LinkTaskToIssue.LinkTaskToIssueCommand"/>, so a session or pull request
/// linked to the wrong entry can be taken off it again.
/// <para>
/// Idempotent: a link the entry does not hold leaves it as it was and still
/// succeeds, the way linking what it already holds does. Only the projection is
/// removed; the entry's repositories are left alone, since the person may have
/// meant the repository whatever happened to the link.
/// </para>
/// </summary>
public sealed record UnlinkTaskFromIssueCommand(Guid Id, string RepoId, string ExternalId, string TargetType);

public sealed class UnlinkTaskFromIssueCommandHandler(ITaskRepository entries)
    : ICommandHandler<UnlinkTaskFromIssueCommand, Result<TaskItemDto>>
{
    public static readonly Error NotFound = Error.NotFound(
        "entry.not_found",
        "That entry no longer exists.");

    public async Task<Result<TaskItemDto>> Handle(
        UnlinkTaskFromIssueCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var entry = await entries.GetAsync(command.Id, cancellationToken);
        if (entry is null) return NotFound;

        // A session is one link whatever repository it was recorded under — the way
        // link_session and the task list decide it is already linked — so every ref
        // to it goes, and none is left behind to keep the card on screen.
        var anyRepository = string.Equals(command.TargetType, EntryProjectionDto.SessionTargetType, StringComparison.OrdinalIgnoreCase);

        if (entry.RemoveProjectionRef(new ProjectionRef(command.RepoId, command.ExternalId, command.TargetType), anyRepository))
        {
            await entries.SaveAsync(entry, cancellationToken);
        }

        return entry.ToDto();
    }
}
