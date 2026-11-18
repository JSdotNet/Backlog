using Backlog.Modules.Progress.Abstractions;
using Backlog.Modules.Progress.Abstractions.DataTransferObjects;
using Backlog.Modules.Progress.Abstractions.Services;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;

namespace Backlog.Infrastructure.Sync;

/// <summary>
/// How the stand-up context is written on the task feed: one whole document under one
/// constant id with the kind token <c>stand-up-context</c>, on the terms the My Day
/// preferences travel on (<see cref="MyDayReplicaDocuments"/>). A desktop writes it for the
/// phone, which reads pull request statuses, the pace and the working week from it
/// (local ADR 0035 §8); a desktop takes nothing from it.
/// <para>
/// The kind token is a literal duplicated where it is read, for the reason
/// <see cref="RoadmapReplicaDocuments"/> gives: the phone's <c>TaskFold</c> skips the same
/// token. It and the id are persisted on every replica, so neither is ever changed.
/// </para>
/// </summary>
public static class StandUpContextReplicaDocuments
{
    /// <summary>The kind token the stand-up context carries.</summary>
    public const string ContextType = "stand-up-context";

    /// <summary>What the activity log, and any listing of the feed, calls it.</summary>
    public const string Title = "Stand-up context";

    /// <summary>The document's id — the same literal on every device, chosen once and never
    /// to be changed, for the reason <see cref="MyDayReplicaDocuments.PreferencesId"/> gives.</summary>
    public static readonly Guid ContextId = Guid.Parse("8b3f2d61-7c4e-4a95-b0d8-5e1c9a7f3b02");

    /// <summary>Whether a document is the stand-up context. Ordinal, as every token on the feed is compared.</summary>
    public static bool Is(TaskChange change)
    {
        ArgumentNullException.ThrowIfNull(change);
        return string.Equals(change.Task.Type, ContextType, StringComparison.Ordinal);
    }

    /// <summary>The context as a task-shaped change, the shape <see cref="WholeDocumentChange"/> describes.</summary>
    public static TaskChange ToChange(StandUpContextCopyDto copy)
    {
        ArgumentNullException.ThrowIfNull(copy);
        return WholeDocumentChange.Of(ContextId, Title, ContextType, copy.Content, copy.UpdatedAt);
    }
}

/// <summary>
/// How an impediment edit the phone asks for is written on the task feed: one document per
/// edit, under an id of its own, with the kind token <c>impediment-edit</c> and the
/// <see cref="ImpedimentEditDto"/> as its text (local ADR 0035 §8). A command, not the
/// impediment: a desktop applies it through Progress's impediment features and then retires
/// it with a tombstone, so no device that pulls the feed later applies it again.
/// <para>
/// The kind token is a literal the phone duplicates, for the reason
/// <see cref="StandUpContextReplicaDocuments"/> gives; persisted, so never renamed.
/// </para>
/// </summary>
public static class ImpedimentEditReplicaDocuments
{
    /// <summary>The kind token an impediment edit carries.</summary>
    public const string EditType = "impediment-edit";

    /// <summary>What the activity log calls an edit whose title this build could not read.</summary>
    public const string UntitledTitle = "Impediment edit";

    /// <summary>Whether a document is an impediment edit. Ordinal, as every token on the feed is compared.</summary>
    public static bool Is(TaskChange change)
    {
        ArgumentNullException.ThrowIfNull(change);
        return string.Equals(change.Task.Type, EditType, StringComparison.Ordinal);
    }

    /// <summary>The edit as a task-shaped change under <paramref name="id"/>, stamped when
    /// the person asked for it and titled <paramref name="title"/> for the sync log.</summary>
    public static TaskChange ToChange(Guid id, ImpedimentEditDto edit, string? title = null)
    {
        ArgumentNullException.ThrowIfNull(edit);

        return WholeDocumentChange.Of(
            id,
            string.IsNullOrWhiteSpace(title) ? UntitledTitle : title,
            EditType,
            ProgressDocuments.WriteEdit(edit),
            edit.AskedAt);
    }

    /// <summary>The edit a document carries, or null when this build cannot read it.</summary>
    public static ImpedimentEditDto? ToEdit(TaskChange change)
    {
        ArgumentNullException.ThrowIfNull(change);
        return ProgressDocuments.ReadEdit(change.Task.ContentMd);
    }

    /// <summary>The tombstone that retires <paramref name="change"/>: the same document,
    /// deleted at <paramref name="now"/> — or one tick after its own stamp, on a desktop whose
    /// clock is behind the phone's, so the replica takes it as the later write.</summary>
    public static TaskChange Retire(TaskChange change, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(change);

        var stamp = now > change.UpdatedAt ? now : change.UpdatedAt.AddTicks(1);
        return change with { UpdatedAt = stamp, DeletedAt = stamp };
    }
}
