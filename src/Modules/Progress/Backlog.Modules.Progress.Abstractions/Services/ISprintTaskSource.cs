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
    /// finished or not. Each task once. An id no live task has is left out.
    /// </summary>
    Task<IReadOnlyList<SprintTaskDto>> ReadAsync(SprintTaskQuery query, CancellationToken cancellationToken = default);
}

/// <summary>What a sprint asks Tasks for. Plan tags are compared without the
/// <c>+</c> sigil and without regard to case; so are repository aliases.</summary>
/// <param name="AnyRepository">Whether a plan tag brings in its tasks in every repository,
/// <paramref name="RepositoryAliases"/> aside — how a goal's attached plan counts its
/// entries (<c>.devbook/domain/progress/domain.md#goal-attachment</c>). Last and defaulted,
/// so a sprint's query reads as it did.</param>
public sealed record SprintTaskQuery(
    IReadOnlyCollection<Guid> TaskIds,
    IReadOnlyCollection<string> PlanTags,
    IReadOnlyCollection<string> RepositoryAliases,
    bool AnyRepository = false);
