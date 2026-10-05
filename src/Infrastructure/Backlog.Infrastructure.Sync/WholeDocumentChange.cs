using Backlog.Modules.Sync.Abstractions.DataTransferObjects;

namespace Backlog.Infrastructure.Sync;

/// <summary>
/// A whole document that is not a task, written as a task-shaped change on the task
/// feed (local ADR 0018, Decision §1; local ADR 0020 reuses it): its constant id, its
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
