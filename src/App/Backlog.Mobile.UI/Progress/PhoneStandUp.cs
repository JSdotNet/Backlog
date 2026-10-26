using Backlog.Mobile.UI.Tasks;
using Backlog.Modules.Progress.Abstractions;
using Backlog.Modules.Progress.Abstractions.DataTransferObjects;
using Backlog.Modules.Tasks.Abstractions;
using Backlog.Modules.Tasks.Abstractions.DataTransferObjects;
using Backlog.SharedKernel;

namespace Backlog.Mobile.UI.Progress;

/// <summary>
/// The stand-up on the phone (<c>.devbook/domain/progress/features.md#goals-on-the-phone</c>):
/// read from the goals and impediments the phone holds, its synced tasks, and the pull
/// request statuses, pace and working week a desktop last sent in the stand-up context —
/// by <see cref="StandUpReading"/>, the rule code the desktop reads it with, so the two never
/// disagree about the same work (local ADR 0035 §8). Stores nothing and decides nothing of
/// its own: the mapping of a synced task into the words the rules read is all it adds.
/// </summary>
public sealed class PhoneStandUp(ProgressViewProjection progress, TaskViewProjection tasks)
{
    /// <summary>
    /// The stand-up for the phone's today by <paramref name="length"/>, or by the length a
    /// desktop last read by, else Week. Two-week windows count from
    /// <paramref name="twoWeekStart"/>, else from the desktop's start.
    /// </summary>
    public StandUpDto Read(IterationLength? length = null, DateOnly? twoWeekStart = null) =>
        Read(progress.Today, length, twoWeekStart);

    /// <inheritdoc cref="Read(IterationLength?, DateOnly?)"/>
    public StandUpDto Read(DateOnly today, IterationLength? length = null, DateOnly? twoWeekStart = null)
    {
        var context = progress.Context();

        return StandUpReading.Read(new StandUpInputs(
            today,
            length ?? context?.Length ?? IterationLength.Week,
            twoWeekStart ?? context?.TwoWeekStart,
            context?.Week ?? WorkingHours.Default,
            [.. progress.Goals().Select(view => view.Goal)],
            progress.Impediments(),
            Work(tasks.Live()),
            [.. (context?.PullRequests ?? []).Select(LinkedPullRequestDto.Of)],
            context?.PointsPerWeek));
    }

    /// <summary>
    /// The synced tasks in the words the stand-up's rules read, as the desktop's own task
    /// source gives them: the status token as the status, the blocked mark, the roadmap slugs
    /// its tags carry by the roadmap's own rule, the repository ids (the phone keeps no aliases), the linked pull
    /// requests, and whether an <c>after:</c> dependency, resolved against every task the
    /// phone holds, still waits on open work. A capture, a note and every whole document the
    /// desktop replicates never reach here — the task fold keeps no row for one.
    /// </summary>
    public static IReadOnlyList<ProgressTaskDto> Work(IReadOnlyList<TaskViewRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        var index = new DependencyResolution.CandidateIndex(
            [.. rows.Select(row => new DependencyResolution.Candidate(row.Id, row.Task.ImportItemId, row.Task.ImportPlanId))]);
        var byId = new Dictionary<Guid, TaskViewRow>();
        foreach (var row in rows) byId.TryAdd(row.Id, row);

        return [.. rows.Select(row => Task(row) with { WaitsOnOpenWork = WaitsOnOpenWork(row, index, byId) })];
    }

    private static ProgressTaskDto Task(TaskViewRow row)
    {
        var task = row.Task;

        return new ProgressTaskDto(
            row.Id,
            task.Title,
            StatusOf(task.Status),
            task.BlockedSince is not null,
            PlanTagMatch.SlugsCarried(task.Tags),
            [.. task.RepoIds.Distinct(StringComparer.OrdinalIgnoreCase)],
            [.. task.Tags],
            task.CompletedOn,
            DateOnly.FromDateTime(task.CreatedAt.LocalDateTime),
            [.. task.ProjectionRefs
                .Where(projection => string.Equals(projection.TargetType, EntryProjectionDto.PullRequestTargetType, StringComparison.Ordinal))
                .Select(projection => new PullRequestRefDto(projection.RepoId, projection.ExternalId))
                .Distinct()],
            task.StartedOn,
            task.BlockedSince,
            task.DueOn,
            task.Effort,
            task.Order);
    }

    /// <summary>The status token as Progress reads it, through the Tasks token map the desktop
    /// writes it with; a token this build does not know reads as archived, as the desktop's
    /// source reads every status it does not list.</summary>
    private static ProgressTaskStatus StatusOf(string? token)
    {
        EntryStatus status;
        try
        {
            status = EnumMap.ParseStatus(token?.Trim() ?? string.Empty);
        }
        catch (FormatException)
        {
            return ProgressTaskStatus.Archived;
        }

        return status switch
        {
            EntryStatus.Draft => ProgressTaskStatus.Draft,
            EntryStatus.Ready => ProgressTaskStatus.Ready,
            EntryStatus.InProgress => ProgressTaskStatus.InProgress,
            EntryStatus.Done => ProgressTaskStatus.Done,
            _ => ProgressTaskStatus.Archived
        };
    }

    /// <summary>Whether one of the task's dependencies names a task that is not done —
    /// neither Done nor completed — or names no task the phone holds.</summary>
    private static bool WaitsOnOpenWork(TaskViewRow row, DependencyResolution.CandidateIndex index, Dictionary<Guid, TaskViewRow> byId)
    {
        if (row.Task.DependsOn is not { Count: > 0 } declared) return false;

        return DependencyResolution.ResolveAll(declared, index, row.Task.ImportPlanId, row.Task.Tags)
            .Any(value => !Guid.TryParse(value, out var id)
                || !byId.TryGetValue(id, out var predecessor)
                || (StatusOf(predecessor.Task.Status) != ProgressTaskStatus.Done && predecessor.Task.CompletedOn is null));
    }
}
