using Backlog.Modules.Progress.Abstractions;
using Backlog.Modules.Progress.Abstractions.DataTransferObjects;
using Backlog.Modules.Progress.Abstractions.Services;
using Backlog.Modules.Progress.DomainModels;
using Backlog.SharedKernel;

namespace Backlog.Modules.Progress.UnitTests;

/// <summary>The series kept in memory, as the SQLite row would keep them: saved whole
/// and handed back as stored.</summary>
internal sealed class InMemorySprintSeriesRepository : ISprintSeriesRepository
{
    private readonly List<SprintSeries> _all = [];

    public int Saves { get; private set; }

    public Task<IReadOnlyList<SprintSeries>> ListAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<SprintSeries>>([.. _all]);

    public Task<SprintSeries?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(_all.FirstOrDefault(series => series.Id == id));

    public Task SaveAsync(SprintSeries series, CancellationToken cancellationToken = default)
    {
        Saves++;
        _all.RemoveAll(existing => existing.Id == series.Id);
        _all.Add(series);
        return Task.CompletedTask;
    }

    public int Deletes { get; private set; }

    public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        Deletes++;
        _all.RemoveAll(existing => existing.Id == id);
        return Task.CompletedTask;
    }
}

/// <summary>Tasks as the adapter over Tasks would answer them: an id in any repository,
/// or a plan tag in one of the asked repositories.</summary>
internal sealed class FakeTaskSource : ISprintTaskSource
{
    public List<SprintTaskDto> Tasks { get; } = [];

    public SprintTaskQuery? LastQuery { get; private set; }

    public Task<IReadOnlyList<SprintTaskDto>> ReadAsync(SprintTaskQuery query, CancellationToken cancellationToken = default)
    {
        LastQuery = query;
        var ids = query.TaskIds.ToHashSet();

        return Task.FromResult<IReadOnlyList<SprintTaskDto>>(
        [
            .. Tasks.Where(task =>
                ids.Contains(task.Id)
                || (task.PlanTags.Any(tag => query.PlanTags.Any(plan => PlanTags.Same(plan, tag)))
                    && (query.AnyRepository || task.RepositoryAliases.Any(alias => query.RepositoryAliases.Contains(alias, StringComparer.OrdinalIgnoreCase)))))
        ]);
    }
}

internal sealed class FakePullRequests : ISprintPullRequestReader
{
    public List<LinkedPullRequestDto> Known { get; } = [];

    public Task<IReadOnlyList<LinkedPullRequestDto>> ReadAsync(
        IReadOnlyList<PullRequestRefDto> pullRequests,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<LinkedPullRequestDto>>(
        [
            .. pullRequests.Select(pull =>
                Known.FirstOrDefault(known => known.Repository == pull.Repository && known.Number == pull.Number)
                ?? LinkedPullRequestDto.Unread(pull))
        ]);
}

internal sealed class FakeFeatures(bool progressOn) : IAppFeatureSettings
{
    public bool ProgressOn { get; set; } = progressOn;

    public event Action? Changed;

    public AppFeatureSettings Current { get; } = new();

    public string SettingsPath => "features.json";

    public bool IsEnabled(string key) => key == ProgressFeatures.Progress && ProgressOn;

    public string? SetEnabled(string key, bool enabled)
    {
        ProgressOn = enabled;
        Changed?.Invoke();
        return null;
    }
}

/// <summary>Builds the tasks the scenarios use, over the requirements' series: the
/// repositories <c>backlog</c> and <c>docs</c>, two-week sprints from a Monday.</summary>
internal static class Make
{
    /// <summary>Monday 12 October 2026, the first day of the requirements' Sprint 4.</summary>
    public static readonly DateOnly Monday = new(2026, 10, 12);

    public static SprintTaskDto Task(
        string title,
        SprintTaskStatus status = SprintTaskStatus.Ready,
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

    public static SprintSeries Series(params string[] repositories) =>
        SprintSeries.Create(Guid.NewGuid(), repositories.Length == 0 ? ["backlog", "docs"] : repositories).Value;

    public static SprintSeries SeriesWithOpenSprint(DateOnly? start = null)
    {
        var series = Series();
        _ = series.StartSprint(start ?? Monday, "Ship the sprint model").Value;
        return series;
    }
}
