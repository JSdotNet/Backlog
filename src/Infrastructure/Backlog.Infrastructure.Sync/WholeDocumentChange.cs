using Backlog.Modules.Sync.Abstractions.DataTransferObjects;

namespace Backlog.Infrastructure.Sync;

/// <summary>
/// A whole document that is not a task, written as a task-shaped change on the task
/// feed (local ADR 0018, Decision §1; local ADR 0021 reuses it): its constant id, its
/// stamp, its kind token, the stored text verbatim as <c>ContentMd</c>, the Tasks
/// defaults for status and priority so no field but the type is unusual, and nothing
/// else. Never a tombstone — a cleared document is an empty one, sent like any other.
/// <para>
/// One shape for every such document, so the roadmap's two and the GitHub settings'
/// two cannot drift apart in what they leave unset.
/// </para>
/// </summary>
internal static class WholeDocumentChange
{
    public static TaskChange Of(Guid id, string title, string type, string content, DateTimeOffset updatedAt) =>
        new(id, updatedAt, DeletedAt: null, new TaskPayload(
            title,
            content,
            type,
            Status: "draft",
            Priority: "medium",
            Order: 0,
            Area: null,
            CreatedAt: updatedAt,
            SourceInboxId: null,
            RecurrenceSourceId: null,
            DueOn: null,
            RemindAt: null,
            Recurrence: null,
            InMyDayOn: null,
            View: null,
            Effort: null,
            ImportPlanId: null,
            ImportItemId: null,
            AttachmentPath: null,
            Tags: [],
            RepoIds: [],
            DependsOn: [],
            SubItems: [],
            UsageEvents: [],
            ProjectionRefs: []));
}

/// <summary>
/// One kind of whole document on the task feed: its kind token and the title the activity
/// log shows it under — the goals', the impediments', the iteration settings' and the My Day
/// preferences' alike. The wire half the per-kind document classes share; what the document
/// says, and which copy wins, stays behind each kind's port.
/// </summary>
/// <param name="Type">The kind token. Persisted on every replica, so never renamed.</param>
/// <param name="Title">The title a document goes under where it carries none of its own.</param>
internal sealed record WholeDocumentKind(string Type, string Title)
{
    /// <summary>Whether a document is of this kind. Ordinal, as every token on the feed is compared.</summary>
    public bool Is(TaskChange change)
    {
        ArgumentNullException.ThrowIfNull(change);
        return string.Equals(change.Task.Type, Type, StringComparison.Ordinal);
    }

    /// <summary>The key one document's push mark is kept under in
    /// <see cref="TaskSyncState.DocumentWatermarks"/>: prefixed by the kind token so it
    /// never meets another kind's marks.</summary>
    public string MarkOf(Guid id) => $"{Type}/{id:D}";

    /// <summary>The document as a task-shaped change under <paramref name="id"/>, titled by
    /// <paramref name="title"/>, or by <see cref="Title"/> where that is blank.</summary>
    public TaskChange ToChange(Guid id, string? title, string content, DateTimeOffset updatedAt) =>
        WholeDocumentChange.Of(id, string.IsNullOrWhiteSpace(title) ? Title : title, Type, content, updatedAt);

    /// <summary>The title a pulled document carries, or empty.</summary>
    public static string TitleOf(TaskChange change) => change.Task.Title ?? string.Empty;

    /// <summary>The text a pulled document carries verbatim, or empty.</summary>
    public static string ContentOf(TaskChange change) => change.Task.ContentMd ?? string.Empty;
}
