using Backlog.Modules.Progress.Abstractions;
using Backlog.Modules.Progress.Abstractions.DataTransferObjects;
using Backlog.Modules.Progress.Abstractions.Services;
using Backlog.Modules.Progress.Features.ReadSprintProgress;
using Backlog.Modules.Progress.Services;
using Backlog.SharedKernel;
using Backlog.SharedKernel.Handlers;

namespace Backlog.Modules.Progress.Features.ReadStandUp;

/// <summary>The stand-up for <paramref name="Today"/>, read by <paramref name="Length"/>.</summary>
public sealed record ReadStandUpQuery(DateOnly Today, IterationLength Length);

/// <summary>
/// The <c>.devbook/domain/progress/domain.md#stand-up-reading</c> on the desktop: it gathers
/// the goals, the impediments, one read of Tasks and one of the pull requests, and hands
/// them to <see cref="StandUpReading"/>, the rule code the phone reads the stand-up with too
/// (local ADR 0035 §8). Nothing read is kept.
/// <list type="bullet">
/// <item><b>Goals</b>: every goal with its progress — the items done inside last as its
/// <c>+n</c> — and its <see cref="GoalForecasting">forecast</see> from the four windows
/// ending with last.</item>
/// <item><b>Finished</b>: each goal's counted items done inside last, then under No goal every
/// task no goal counts whose status is Done and that was completed inside last.</item>
/// <item><b>Planned</b>: for each goal, its items in progress (a blocked one is not), its open
/// items due inside next, and for each plan it holds the first entry in plan order that is
/// open, not in progress, not blocked, not archived and waits on no open work; then under No
/// goal the open tasks no goal counts that are due inside next. Each item once.</item>
/// <item><b>Impediments</b>: the open ones in the order raised, each with its age in worked
/// days and the first goal counting a task it holds back, and how many were resolved
/// inside last.</item>
/// </list>
/// A group with no item is left out. The pull requests of tasks no goal counts are not read:
/// such a task is finished by its status alone.
/// </summary>
public sealed class ReadStandUpQueryHandler(
    IGoalRepository goals,
    IImpedimentRepository impediments,
    ISprintTaskSource tasks,
    IGoalPullRequestReader pullRequests,
    IIterationSettings iteration,
    IStandUpPace pace,
    IWorkingHoursSettings? workingHours = null)
    : IQueryHandler<ReadStandUpQuery, StandUpDto>
{
    public async Task<StandUpDto> Handle(ReadStandUpQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var week = workingHours?.Current ?? WorkingHours.Default;
        var twoWeekStart = iteration.TwoWeekStart;

        IReadOnlyList<GoalDto> all = [.. (await goals.ListAsync(cancellationToken).ConfigureAwait(false)).Select(goal => goal.ToDto())];
        var read = await tasks.ReadAsync(
            StandUpReading.TaskQuery(all, query.Today, query.Length, twoWeekStart, week),
            cancellationToken).ConfigureAwait(false);

        var asked = GoalItems.PullRequestsOf(all, read);
        IReadOnlyList<LinkedPullRequestDto> answers = asked.Count == 0
            ? []
            : [.. (await pullRequests.ReadAsync(asked, cancellationToken).ConfigureAwait(false)).Select(ReadSprintProgressQueryHandler.Linked)];

        IReadOnlyList<ImpedimentDto> raised = [.. (await impediments.ListAsync(cancellationToken).ConfigureAwait(false)).Select(impediment => impediment.ToDto())];
        var pointsPerWeek = await pace.PointsPerWeekAsync(cancellationToken).ConfigureAwait(false);

        return StandUpReading.Read(new StandUpInputs(
            query.Today, query.Length, twoWeekStart, week, all, raised, read, answers, pointsPerWeek));
    }
}
