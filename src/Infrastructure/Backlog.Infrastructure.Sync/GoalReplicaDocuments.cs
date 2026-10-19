using Backlog.Modules.Progress.Abstractions.DataTransferObjects;
using Backlog.Modules.Progress.Abstractions.Services;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;

namespace Backlog.Infrastructure.Sync;

/// <summary>
/// How a goal is written on the task feed: a whole document under the goal's own id with
/// the kind token <c>goal</c> (local ADR 0035 §9), on the terms the roadmap's plan travels
/// on (local ADR 0018). The wire half only — what a goal says, and which
/// copy wins, is Progress's, behind <see cref="IGoalReplication"/>.
/// <para>
/// The kind token is a literal duplicated where it is read, for the reason
/// <see cref="RoadmapReplicaDocuments"/> gives: the phone's <c>TaskFold</c> skips the same
/// token, and neither side may see the other. It is persisted on every replica, so it is
/// never renamed.
/// </para>
/// </summary>
public static class GoalReplicaDocuments
{
    /// <summary>The kind token a goal document carries.</summary>
    public const string GoalType = "goal";

    /// <summary>What the activity log calls a goal whose title this build could not read.</summary>
    public const string UntitledTitle = "Goal";

    /// <summary>Whether a document is a goal's. Ordinal, as every token on the feed is compared.</summary>
    public static bool IsGoal(TaskChange change)
    {
        ArgumentNullException.ThrowIfNull(change);
        return string.Equals(change.Task.Type, GoalType, StringComparison.Ordinal);
    }

    /// <summary>The key a goal's push mark is kept under in
    /// <see cref="TaskSyncState.DocumentWatermarks"/>: one per goal, prefixed by the kind
    /// token so it never meets another kind's marks.</summary>
    public static string MarkOf(Guid goalId) => $"{GoalType}/{goalId:D}";

    /// <summary>The goal as a task-shaped change, titled by the goal's title.</summary>
    public static TaskChange ToChange(GoalReplicaCopyDto copy)
    {
        ArgumentNullException.ThrowIfNull(copy);

        return WholeDocumentChange.Of(
            copy.Id,
            string.IsNullOrWhiteSpace(copy.Title) ? UntitledTitle : copy.Title,
            GoalType,
            copy.Content,
            copy.UpdatedAt);
    }

    /// <summary>The copy a pulled document carries, as Progress takes it.</summary>
    public static GoalReplicaCopyDto ToCopy(TaskChange change)
    {
        ArgumentNullException.ThrowIfNull(change);

        return new GoalReplicaCopyDto(
            change.Id,
            change.Task.Title ?? string.Empty,
            change.Task.ContentMd ?? string.Empty,
            change.UpdatedAt);
    }
}
