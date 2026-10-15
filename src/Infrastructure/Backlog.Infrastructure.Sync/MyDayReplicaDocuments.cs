using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.Modules.Tasks.Abstractions.DataTransferObjects;
using Backlog.Modules.Tasks.Abstractions.Services;

namespace Backlog.Infrastructure.Sync;

/// <summary>
/// How the My Day preferences are written on the task feed (local ADR 0032, §1): one
/// whole document under one constant id, with the kind token <c>my-day-preferences</c>.
/// The wire half of the arrangement only — what the document says, and which copy wins,
/// is the store's, behind <see cref="IMyDayPreferencesReplication"/>.
/// <para>
/// The kind token is a literal duplicated where it is read, for the reason
/// <see cref="RoadmapReplicaDocuments"/> gives: the phone's <c>TaskFold</c> keeps the same
/// token off its task rows and folds the document itself, and neither side may see the
/// other.
/// </para>
/// </summary>
public static class MyDayReplicaDocuments
{
    /// <summary>The kind token the preferences document carries. Persisted on every
    /// replica, so never to be renamed.</summary>
    public const string PreferencesType = "my-day-preferences";

    /// <summary>What the activity log, and any listing of the feed, calls it.</summary>
    public const string Title = "My Day preferences";

    /// <summary>
    /// The document's id — the same literal on every device, chosen once and <b>never
    /// to be changed</b>. The replica keys a document by owner and id, so a build
    /// writing the preferences under another id would fork them: two documents on the
    /// replica, each device taking whichever it pulled last.
    /// </summary>
    public static readonly Guid PreferencesId = Guid.Parse("4d7a9c2e-5b1f-4e3a-8c6d-2f0e1a3b5c01");

    /// <summary>Whether a document is the preferences'. Ordinal, as every token on the
    /// feed is compared.</summary>
    public static bool Is(TaskChange change)
    {
        ArgumentNullException.ThrowIfNull(change);
        return string.Equals(change.Task.Type, PreferencesType, StringComparison.Ordinal);
    }

    /// <summary>The preferences as a task-shaped change, the shape
    /// <see cref="WholeDocumentChange"/> describes: the constant id, the file's stamp,
    /// its JSON verbatim, no My Day date, never a tombstone.</summary>
    public static TaskChange ToChange(MyDayPreferencesCopyDto copy)
    {
        ArgumentNullException.ThrowIfNull(copy);

        return WholeDocumentChange.Of(PreferencesId, Title, PreferencesType, copy.Content, copy.UpdatedAt);
    }

    /// <summary>The copy a pulled document carries: its text verbatim and its
    /// stamp.</summary>
    public static MyDayPreferencesCopyDto ToCopy(TaskChange change)
    {
        ArgumentNullException.ThrowIfNull(change);

        return new MyDayPreferencesCopyDto(change.Task.ContentMd ?? string.Empty, change.UpdatedAt);
    }
}
