using Backlog.Modules.Progress.Abstractions;
using Backlog.Modules.Progress.Abstractions.DataTransferObjects;
using Backlog.Modules.Progress.Abstractions.Services;
using Backlog.Modules.Progress.Features.AttachWork;
using Backlog.Modules.Progress.Services;
using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Progress.Features.ListGoals;

/// <summary>Every goal with its counts, the items done from <paramref name="WindowStart"/>
/// to <paramref name="WindowEnd"/> counted beside them when both are given.</summary>
public sealed record ListGoalsQuery(DateOnly? WindowStart = null, DateOnly? WindowEnd = null);

/// <summary>One goal by its id.</summary>
public sealed record GetGoalQuery(Guid GoalId);

/// <summary>The goal that holds an item, or none.</summary>
public sealed record FindGoalOfQuery(GoalItemDto Item);

/// <summary>
/// Reads the goals, and for the list counts each one's items from the current state of
/// its work (<see cref="GoalCounting"/>). One read of Tasks and one of the pull requests
/// for every goal at once, so a list of ten goals asks each owner once; nothing read is
/// kept.
/// </summary>
public sealed class ListGoalsQueryHandler(IGoalRepository repository, IProgressTaskSource tasks, IGoalPullRequestReader pullRequests)
    : IQueryHandler<ListGoalsQuery, IReadOnlyList<GoalSummaryDto>>,
      IQueryHandler<GetGoalQuery, Result<GoalDto>>,
      IQueryHandler<FindGoalOfQuery, Result<GoalDto?>>
{
    public async Task<IReadOnlyList<GoalSummaryDto>> Handle(ListGoalsQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var all = await repository.ListAsync(cancellationToken).ConfigureAwait(false);
        if (all.Count == 0) return [];

        IReadOnlyList<ProgressTaskDto> read = all.Any(goal => goal.Attachments.Any(a => a.Kind != GoalAttachmentKind.PullRequest))
            ? await tasks.ReadAsync(GoalCounting.TaskQuery(all), cancellationToken).ConfigureAwait(false)
            : [];

        var counted = all.ToDictionary(goal => goal.Id, goal => GoalCounting.TasksOf(goal, all, read));
        var asked = all.SelectMany(goal => GoalCounting.PullRequestsOf(goal, counted[goal.Id]))
            .DistinctBy(pull => $"{pull.Repository}#{pull.Number}", StringComparer.OrdinalIgnoreCase)
            .ToList();

        IReadOnlyList<LinkedPullRequestDto> answers = asked.Count == 0
            ? []
            : [.. (await pullRequests.ReadAsync(asked, cancellationToken).ConfigureAwait(false)).Select(LinkedPullRequestDto.Of)];

        DayWindow? window = query is { WindowStart: { } first, WindowEnd: { } last } ? new DayWindow(first, last) : null;

        return
        [
            .. all.Select(goal => new GoalSummaryDto(
                goal.ToDto(),
                GoalCounting.Count(goal, all, read, answers, window).ToDto()))
        ];
    }

    public async Task<Result<GoalDto>> Handle(GetGoalQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var goal = await repository.GetAsync(query.GoalId, cancellationToken).ConfigureAwait(false);
        return goal is null ? ProgressErrors.GoalNotFound(query.GoalId) : goal.ToDto();
    }

    public async Task<Result<GoalDto?>> Handle(FindGoalOfQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var item = AttachWorkCommandHandler.AttachmentOf(query.Item, DateOnly.MinValue);
        if (item.IsFailure) return Result.Failure<GoalDto?>(item.Error);

        var all = await repository.ListAsync(cancellationToken).ConfigureAwait(false);
        return Result.Success(GoalAssignment.HolderOf(all, item.Value)?.ToDto());
    }
}
