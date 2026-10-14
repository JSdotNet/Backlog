using Backlog.Modules.Progress.Abstractions;
using Backlog.Modules.Progress.Abstractions.DataTransferObjects;
using Backlog.Modules.Progress.Abstractions.Services;
using Backlog.Modules.Progress.DomainModels;
using Backlog.Modules.Progress.Services;
using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Progress.Features.ReadSprintProgress;

/// <summary>One sprint read against its members' current state; the open sprint when
/// <paramref name="Number"/> is null.</summary>
public sealed record ReadSprintProgressQuery(Guid SeriesId, int? Number = null);

/// <summary>
/// Reads the sprint's members from Tasks through <see cref="ISprintTaskSource"/> and
/// their pull requests through <see cref="ISprintPullRequestReader"/>, then hands both to
/// <see cref="SprintProgress"/>. Nothing is stored.
/// </summary>
public sealed class ReadSprintProgressQueryHandler(
    ISprintSeriesRepository repository,
    ISprintTaskSource tasks,
    ISprintPullRequestReader pullRequests,
    TimeProvider time)
    : IQueryHandler<ReadSprintProgressQuery, Result<SprintProgressDto>>
{
    public async Task<Result<SprintProgressDto>> Handle(ReadSprintProgressQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var series = await repository.GetAsync(query.SeriesId, cancellationToken).ConfigureAwait(false);
        if (series is null) return ProgressErrors.SeriesNotFound(query.SeriesId);

        var sprint = query.Number is { } number ? series.SprintNumber(number) : series.OpenSprint;
        if (sprint is null) return ProgressErrors.SprintNotFound(query.Number ?? SprintSequence.NextNumber(series.Sprints));

        return await ReadAsync(series, sprint, tasks, pullRequests, Today(time), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The reading itself, shared with closing, which needs the same members
    /// and the same done rule to know what is unfinished.</summary>
    internal static async Task<SprintProgressDto> ReadAsync(
        SprintSeries series,
        Sprint sprint,
        ISprintTaskSource tasks,
        ISprintPullRequestReader pullRequests,
        DateOnly today,
        CancellationToken cancellationToken)
    {
        var (taskIds, planTags) = SprintProgress.Query(sprint);
        var read = await tasks
            .ReadAsync(new SprintTaskQuery(taskIds, planTags, [.. series.Repositories]), cancellationToken)
            .ConfigureAwait(false);

        var members = SprintProgress.Members(series, sprint, read);
        var linked = members.SelectMany(task => task.PullRequests).Distinct().ToList();
        var pulls = linked.Count == 0
            ? []
            : await pullRequests.ReadAsync(linked, cancellationToken).ConfigureAwait(false);

        return SprintProgress.Read(series, sprint, members, pulls, today);
    }

    internal static DateOnly Today(TimeProvider time) => DateOnly.FromDateTime(time.GetLocalNow().DateTime);
}
