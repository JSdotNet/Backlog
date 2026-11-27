using Backlog.Modules.Goals.Abstractions;
using Backlog.Modules.Goals.Abstractions.DataTransferObjects;
using Backlog.Modules.Goals.Abstractions.Services;
using Backlog.Modules.Goals.DomainModels;

namespace Backlog.Modules.Goals.UnitTests;

/// <summary>Tasks as the adapter over Tasks would answer them: an id in any repository,
/// or a plan tag in one of the asked repositories.</summary>
internal sealed class FakeTaskSource : IGoalsTaskSource
{
    public List<GoalsTaskDto> Tasks { get; } = [];

    public GoalsTaskQuery? LastQuery { get; private set; }

    public Task<IReadOnlyList<GoalsTaskDto>> ReadAsync(GoalsTaskQuery query, CancellationToken cancellationToken = default)
    {
        LastQuery = query;
        var ids = query.TaskIds.ToHashSet();

        return Task.FromResult<IReadOnlyList<GoalsTaskDto>>(
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

    /// <summary>Every pull request the last reading asked about.</summary>
    public IReadOnlyList<PullRequestRefDto> Asked { get; private set; } = [];

    public Task<IReadOnlyList<GoalPullRequestDto>> ReadAsync(
        IReadOnlyList<PullRequestRefDto> pullRequests,
        CancellationToken cancellationToken = default)
    {
        Asked = [.. pullRequests];
        return Task.FromResult<IReadOnlyList<GoalPullRequestDto>>(
        [
            .. pullRequests.Select(pull =>
                Known.FirstOrDefault(known => known.Repository == pull.Repository && known.Number == pull.Number)
                ?? GoalPullRequestDto.Unread(pull))
        ]);
    }
}

/// <summary>Builds the tasks the scenarios use, in the repository <c>backlog</c> unless
/// told otherwise.</summary>
internal static class Make
{
    public static GoalsTaskDto Task(
        string title,
        GoalsTaskStatus status = GoalsTaskStatus.Ready,
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
