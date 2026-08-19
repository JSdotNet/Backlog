using Backlog.Modules.Backlog.Abstractions.DataTransferObjects;
using Backlog.Modules.Backlog.DomainModels;
using Backlog.Modules.Backlog.Services;
using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Backlog.Features.LinkTaskToIssue;

/// <summary>
/// Remembers that an entry became something outside this system — today a GitHub
/// issue.
/// <para>
/// The projection is recorded on the aggregate, so the association survives a
/// restart and travels with the markdown file. The module never calls GitHub
/// itself; the host does that and reports back what it created.
/// </para>
/// </summary>
public sealed record LinkTaskToIssueCommand(Guid Id, string RepoId, string ExternalId, string TargetType);

public sealed class LinkTaskToIssueCommandHandler(ITaskRepository entries)
    : ICommandHandler<LinkTaskToIssueCommand, Result<TaskItemDto>>
{
    public static readonly Error NotFound = Error.NotFound(
        "entry.not_found",
        "That entry no longer exists.");

    public async Task<Result<TaskItemDto>> Handle(
        LinkTaskToIssueCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var entry = await entries.GetAsync(command.Id, cancellationToken);
        if (entry is null) return NotFound;

        entry.SetRepoIds([command.RepoId]);
        entry.AddProjectionRef(new ProjectionRef(command.RepoId, command.ExternalId, command.TargetType));

        await entries.SaveAsync(entry, cancellationToken);
        return entry.ToDto();
    }
}
