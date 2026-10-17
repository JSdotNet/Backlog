using Backlog.Modules.Progress.Abstractions.DataTransferObjects;

namespace Backlog.Modules.Progress.Abstractions.Services;

/// <summary>
/// The tasks a sprint names, read from Tasks
/// (<c>.devbook/domain/progress/dependencies.md</c>, Progress = customer).
/// <para>
/// A port the module asks outward through, answered by an adapter that reads
/// Tasks' published surface only. Progress never sees the task aggregate and
/// never writes a task: this is a read, on every reading, and nothing is kept.
/// </para>
/// </summary>
public interface ISprintTaskSource
{
    /// <summary>
    /// The tasks with one of <paramref name="query"/>'s ids, in whatever repository,
    /// and every task filed under one of its plan tags in one of its repositories,
    /// finished or not — and, in any repository, every task completed in the query's
    /// completion range or due in its due range, when it gives them. Each task once. An id
    /// no live task has is left out.
    /// </summary>
    Task<IReadOnlyList<SprintTaskDto>> ReadAsync(SprintTaskQuery query, CancellationToken cancellationToken = default);
}

/// <summary>What a sprint asks Tasks for. Plan tags are compared without the
/// <c>+</c> sigil and without regard to case; so are repository aliases.</summary>
/// <param name="AnyRepository">Whether a plan tag brings in its tasks in every repository,
/// <paramref name="RepositoryAliases"/> aside — how a goal's attached plan counts its
/// entries (<c>.devbook/domain/progress/domain.md#goal-attachment</c>). Last and defaulted,
/// so a sprint's query reads as it did.</param>
/// <param name="CompletedFrom">With <paramref name="CompletedThrough"/>, also selects every
/// task completed from the one day through the other, both included, in any repository —
/// what the stand-up's Finished reads the work no goal holds from.</param>
/// <param name="CompletedThrough">The last completion day selected; see <paramref name="CompletedFrom"/>.</param>
/// <param name="DueFrom">With <paramref name="DueThrough"/>, also selects every task due from
/// the one day through the other, both included, in any repository — what Planned reads the
/// work due in the next window from.</param>
/// <param name="DueThrough">The last due day selected; see <paramref name="DueFrom"/>.</param>
public sealed record SprintTaskQuery(
    IReadOnlyCollection<Guid> TaskIds,
    IReadOnlyCollection<string> PlanTags,
    IReadOnlyCollection<string> RepositoryAliases,
    bool AnyRepository = false,
    DateOnly? CompletedFrom = null,
    DateOnly? CompletedThrough = null,
    DateOnly? DueFrom = null,
    DateOnly? DueThrough = null)
{
    /// <summary>Whether <paramref name="completedOn"/> falls in the completion range, when one is given.</summary>
    public bool CompletedInRange(DateOnly? completedOn) =>
        CompletedFrom is { } from && CompletedThrough is { } through && completedOn is { } on && on >= from && on <= through;

    /// <summary>Whether <paramref name="dueOn"/> falls in the due range, when one is given.</summary>
    public bool DueInRange(DateOnly? dueOn) =>
        DueFrom is { } from && DueThrough is { } through && dueOn is { } on && on >= from && on <= through;
}
