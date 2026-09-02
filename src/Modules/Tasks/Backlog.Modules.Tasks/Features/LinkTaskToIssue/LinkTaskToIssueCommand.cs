using Backlog.Modules.Tasks.Abstractions.DataTransferObjects;
using Backlog.Modules.Tasks.DomainModels;
using Backlog.Modules.Tasks.Services;
using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Tasks.Features.LinkTaskToIssue;

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

        entry.SetRepoIds(MergedRepoIds(entry, command.RepoId));
        entry.AddProjectionRef(new ProjectionRef(command.RepoId, command.ExternalId, command.TargetType));

        await entries.SaveAsync(entry, cancellationToken);
        return entry.ToDto();
    }

    /// <summary>
    /// The entry's existing targets, plus the one just pushed to.
    /// <para>
    /// A merge rather than a replacement. An entry may name several repositories
    /// (<c>.domain/backlog/features.md#multi-repo-targeting</c>), and this used to
    /// set the whole list to the one repository the push went to — so pushing a
    /// two-target entry silently dropped the other target, and the person's next
    /// look at the row showed one repository where they had typed two. It is the
    /// only place <c>repo_ids</c> is written outside the text path, and the loss
    /// used to be masked by the store holding a mixture of aliases and ids.
    /// </para>
    /// <para>
    /// Order is preserved and the existing spelling wins, because the stored value
    /// came from the registry and the pushed one names the same repository.
    /// Compared without regard to case for the reason ids are compared that way
    /// everywhere: GitHub is case-preserving but not case-sensitive, so two
    /// casings are one target.
    /// </para>
    /// </summary>
    private static IReadOnlyList<string> MergedRepoIds(TaskItem entry, string repoId) =>
        entry.RepoIds.Any(existing => string.Equals(existing, repoId, StringComparison.OrdinalIgnoreCase))
            ? [.. entry.RepoIds]
            : [.. entry.RepoIds, repoId];
}
