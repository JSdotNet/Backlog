using Backlog.Modules.Sync.Abstractions.DataTransferObjects;

namespace Backlog.Infrastructure.Sync;

/// <summary>
/// The kind tokens of documents this build no longer keeps but may still find on the task
/// feed: a replica holds what older builds pushed, and nothing deletes it there.
/// <para>
/// <c>sprint-series</c> was Progress's whole sprint series, one document per series. Local
/// ADR 0035 retires sprints, so a desktop reads such a document and drops it — counted as
/// nothing to do, never written, and never mistaken for a task. The phone's
/// <c>TaskFold</c> skips the same token; the literal is duplicated there for the reason
/// <see cref="RoadmapReplicaDocuments"/> gives.
/// </para>
/// </summary>
public static class RetiredReplicaDocuments
{
    /// <summary>The kind token a retired sprint series document carries.</summary>
    public const string SprintSeriesType = "sprint-series";

    /// <summary>Whether a document is one of the retired kinds. Ordinal, as every token on
    /// the feed is compared.</summary>
    public static bool IsRetired(TaskChange change)
    {
        ArgumentNullException.ThrowIfNull(change);
        return string.Equals(change.Task.Type, SprintSeriesType, StringComparison.Ordinal);
    }
}
