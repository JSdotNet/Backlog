using Backlog.Modules.Progress.Abstractions;
using Backlog.Modules.Progress.Abstractions.DataTransferObjects;
using Backlog.Modules.Progress.Abstractions.Services;
using Backlog.Modules.Progress.DomainModels;

namespace Backlog.Modules.Progress.UnitTests;

/// <summary>Tasks as the adapter over Tasks would answer them: an id in any repository,
/// or a plan tag in one of the asked repositories.</summary>
internal sealed class FakeTaskSource : IProgressTaskSource
{
    public List<ProgressTaskDto> Tasks { get; } = [];

    public ProgressTaskQuery? LastQuery { get; private set; }

    public Task<IReadOnlyList<ProgressTaskDto>> ReadAsync(ProgressTaskQuery query, CancellationToken cancellationToken = default)
    {
        LastQuery = query;
        var ids = query.TaskIds.ToHashSet();

        return Task.FromResult<IReadOnlyList<ProgressTaskDto>>(
        [
            .. Tasks.Where(task =>
                ids.Contains(task.Id)
                || (task.PlanTags.Any(tag => query.PlanTags.Any(plan => PlanTags.Same(plan, tag)))
                    && (query.AnyRepository || task.RepositoryAliases.Any(alias => query.RepositoryAliases.Contains(alias, StringComparer.OrdinalIgnoreCase))))
                || query.CompletedInRange(task.CompletedOn)
                || query.DueInRange(task.DueOn))
        ]);
    }
}

internal sealed class FakePullRequests : IGoalPullRequestReader
{
    public List<GoalPullRequestDto> Known { get; } = [];

    public Task<IReadOnlyList<GoalPullRequestDto>> ReadAsync(
        IReadOnlyList<PullRequestRefDto> pullRequests,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<GoalPullRequestDto>>(
        [
            .. pullRequests.Select(pull =>
                Known.FirstOrDefault(known => known.Repository == pull.Repository && known.Number == pull.Number)
                ?? GoalPullRequestDto.Unread(pull))
        ]);
}

/// <summary>Builds the tasks the scenarios use, in the repository <c>backlog</c> unless
/// told otherwise.</summary>
internal static class Make
{
    public static ProgressTaskDto Task(
        string title,
        ProgressTaskStatus status = ProgressTaskStatus.Ready,
        string? plan = null,
        string repository = "backlog",
        bool blocked = false,
        DateOnly? completedOn = null,
        DateOnly? createdOn = null,
        IReadOnlyList<string>? tags = null,
        params PullRequestRefDto[] pullRequests) =>
        new(
            Guid.NewGuid(),
            title,
            status,
            blocked,
            plan is null ? [] : [PlanTags.Bare(plan)],
            [repository],
            [.. (tags ?? []), .. (plan is null ? Array.Empty<string>() : ["+" + PlanTags.Bare(plan)])],
            completedOn,
            createdOn,
            pullRequests);
}
