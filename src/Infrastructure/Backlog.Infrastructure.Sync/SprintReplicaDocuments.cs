using Backlog.Modules.Progress.Abstractions.DataTransferObjects;
using Backlog.Modules.Progress.Abstractions.Services;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;

namespace Backlog.Infrastructure.Sync;

/// <summary>
/// How a sprint series is written on the task feed: a whole document under the series'
/// own id with the kind token <c>sprint-series</c>, on the terms the roadmap's plan
/// travels on (local ADR 0018). The wire half of the arrangement only — what a series
/// says, and which copy wins, is Progress's, behind <see cref="ISprintSeriesReplication"/>.
/// <para>
/// One document per series rather than one for every series, as the series is
/// Progress's consistency boundary: two series edited on two devices both survive, and a
/// closed sprint never arrives without the sprint it opened. The id is the series' own,
/// so — unlike the roadmap's two constant ids — no literal here may never change.
/// </para>
/// <para>
/// The kind token is a literal duplicated where it is read, for the reason
/// <see cref="RoadmapReplicaDocuments"/> gives: the phone's <c>TaskFold</c> skips the
/// same token, and neither side may see the other.
/// </para>
/// </summary>
public static class SprintReplicaDocuments
{
    /// <summary>The kind token a sprint series document carries.</summary>
    public const string SeriesType = "sprint-series";

    /// <summary>What the activity log calls a series whose name this build could not
    /// read.</summary>
    public const string UnnamedTitle = "Sprint series";

    /// <summary>Whether a document is a sprint series'. Ordinal, as every token on the
    /// feed is compared.</summary>
    public static bool IsSeries(TaskChange change)
    {
        ArgumentNullException.ThrowIfNull(change);
        return string.Equals(change.Task.Type, SeriesType, StringComparison.Ordinal);
    }

    /// <summary>
    /// The key a series' push mark is kept under in
    /// <see cref="TaskSyncState.DocumentWatermarks"/>: one per series, because each is
    /// its own document with its own stamp. Prefixed by the kind token so it can never
    /// meet the roadmap's or the GitHub settings' marks, which are keyed by token alone.
    /// </summary>
    public static string MarkOf(Guid seriesId) => $"{SeriesType}/{seriesId:D}";

    /// <summary>The series as a task-shaped change, the shape
    /// <see cref="WholeDocumentChange"/> describes, titled by the series' name.</summary>
    public static TaskChange ToChange(SprintSeriesReplicaCopyDto copy)
    {
        ArgumentNullException.ThrowIfNull(copy);

        return WholeDocumentChange.Of(copy.Id, TitleOf(copy.Name), SeriesType, copy.Content, copy.UpdatedAt);
    }

    /// <summary>The copy a pulled document carries, as Progress takes it: the id, the
    /// title as the name, the stored text verbatim, and the document's stamp.</summary>
    public static SprintSeriesReplicaCopyDto ToCopy(TaskChange change)
    {
        ArgumentNullException.ThrowIfNull(change);

        return new SprintSeriesReplicaCopyDto(
            change.Id,
            change.Task.Title ?? string.Empty,
            change.Task.ContentMd ?? string.Empty,
            change.UpdatedAt);
    }

    private static string TitleOf(string? name) => string.IsNullOrWhiteSpace(name) ? UnnamedTitle : name;
}
