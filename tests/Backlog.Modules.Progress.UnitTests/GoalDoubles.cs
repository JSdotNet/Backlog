using System.Text.Json;

using Backlog.Modules.Progress.Abstractions;
using Backlog.Modules.Progress.Abstractions.DataTransferObjects;
using Backlog.Modules.Progress.DomainModels;
using Backlog.SharedKernel;

namespace Backlog.Modules.Progress.UnitTests;

/// <summary>The goals kept in memory, as the SQLite row would keep them: saved whole and
/// handed back as stored, in the person's order (<see cref="GoalOrder"/>). A goal saved
/// without the moment it was set is keyed by when it was first saved, as the SQLite store
/// keys it by its row's stamp.</summary>
internal sealed class InMemoryGoalRepository : IGoalRepository
{
    private readonly List<Goal> _all = [];

    public int Saves { get; private set; }

    public int Deletes { get; private set; }

    public Task<IReadOnlyList<Goal>> ListAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Goal>>(
        [
            .. _all
                .Select((goal, saved) => (Goal: goal, Key: GoalOrder.Key(goal.Rank, goal.CreatedAt ?? FirstSaved.AddTicks(saved))))
                .OrderBy(entry => entry.Key)
                .ThenBy(entry => entry.Goal.Id)
                .Select(entry => entry.Goal)
        ]);

    /// <summary>The stand-in for the stamp of a goal saved without a date: after every goal
    /// the tests set with one.</summary>
    private static readonly DateTimeOffset FirstSaved = new(2100, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public Task<Goal?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(_all.FirstOrDefault(goal => goal.Id == id));

    public Task SaveAsync(Goal goal, CancellationToken cancellationToken = default)
    {
        Saves++;
        var index = _all.FindIndex(existing => existing.Id == goal.Id);
        if (index < 0) _all.Add(goal);
        else _all[index] = goal;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        Deletes++;
        _all.RemoveAll(existing => existing.Id == id);
        return Task.CompletedTask;
    }
}

/// <summary>A replica store over plain text: a copy reads as a goal when its content is
/// JSON naming the copy's id, as the SQLite store's document does.</summary>
internal sealed class InMemoryGoalReplicaStore : IGoalReplicaStore
{
    public List<GoalReplicaCopyDto> Rows { get; } = [];

    public Task<IReadOnlyList<GoalReplicaCopyDto>> ReadAllAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<GoalReplicaCopyDto>>([.. Rows]);

    public Task<GoalReplicaCopyDto?> ReadAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(Rows.FirstOrDefault(row => row.Id == id));

    public Task<bool> TryWriteAsync(GoalReplicaCopyDto copy, CancellationToken cancellationToken = default)
    {
        if (!Reads(copy)) return Task.FromResult(false);

        Rows.RemoveAll(row => row.Id == copy.Id);
        Rows.Add(copy);
        return Task.FromResult(true);
    }

    private static bool Reads(GoalReplicaCopyDto copy)
    {
        try
        {
            using var document = JsonDocument.Parse(copy.Content);
            return document.RootElement.TryGetProperty("id", out var id) && id.GetGuid() == copy.Id;
        }
        catch (Exception ex) when (ex is JsonException or FormatException or InvalidOperationException)
        {
            return false;
        }
    }
}

/// <summary>The goals the scenarios use: the requirements' "Customers get paid faster",
/// due on 31 October 2026.</summary>
internal static class MakeGoal
{
    public static readonly DateOnly Due = new(2026, 10, 31);

    public static readonly DateOnly Today = new(2026, 10, 17);

    public static Goal Named(string title = "Customers get paid faster", params GoalAttachment[] holds)
    {
        var goal = Goal.Create(Guid.NewGuid(), title, null, Due).Value;
        foreach (var item in holds) goal.Attach(item);
        return goal;
    }

    public static GoalAttachment Plan(string tag) => GoalAttachment.ForPlan(tag, Today).Value;

    public static GoalAttachment Task(Guid id) => GoalAttachment.ForTask(id, Today).Value;

    public static GoalAttachment PullRequest(string repository, int number) =>
        GoalAttachment.ForPullRequest(repository, number, Today).Value;
}
