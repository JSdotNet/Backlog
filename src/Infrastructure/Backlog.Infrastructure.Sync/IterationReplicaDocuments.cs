using Backlog.Modules.Progress.Abstractions.DataTransferObjects;
using Backlog.Modules.Progress.Abstractions.Services;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;

namespace Backlog.Infrastructure.Sync;

/// <summary>
/// How the iteration settings are written on the task feed (local ADR 0036, §1): one
/// whole document — the length and the two-week start together — under one constant id,
/// with the kind token <c>iteration-settings</c>. The wire half of the arrangement only —
/// what the document says, and which copy wins, is the store's, behind
/// <see cref="IIterationSettingsReplication"/>.
/// <para>
/// The kind token is a literal duplicated where it is read, for the reason
/// <see cref="RoadmapReplicaDocuments"/> gives: the phone's <c>TaskFold</c> keeps the same
/// token off its task rows, and neither side may see the other.
/// </para>
/// </summary>
public static class IterationReplicaDocuments
{
    /// <summary>The kind token the settings document carries. Persisted on every
    /// replica, so never to be renamed.</summary>
    public const string SettingsType = "iteration-settings";

    /// <summary>What the activity log, and any listing of the feed, calls it.</summary>
    public const string Title = "Iteration settings";

    /// <summary>
    /// The document's id — the same literal on every device, chosen once and <b>never
    /// to be changed</b>, for the reason <see cref="MyDayReplicaDocuments.PreferencesId"/>
    /// gives: a build writing the settings under another id would fork them.
    /// </summary>
    public static readonly Guid SettingsId = Guid.Parse("7c3e1f5a-9d2b-4a6e-b8f0-3e5d7a9c1b36");

    /// <summary>Whether a document is the settings'. Ordinal, as every token on the
    /// feed is compared.</summary>
    public static bool Is(TaskChange change)
    {
        ArgumentNullException.ThrowIfNull(change);
        return string.Equals(change.Task.Type, SettingsType, StringComparison.Ordinal);
    }

    /// <summary>The settings as a task-shaped change, the shape
    /// <see cref="WholeDocumentChange"/> describes: the constant id, the file's stamp,
    /// its JSON verbatim, no My Day date, never a tombstone.</summary>
    public static TaskChange ToChange(IterationSettingsCopyDto copy)
    {
        ArgumentNullException.ThrowIfNull(copy);

        return WholeDocumentChange.Of(SettingsId, Title, SettingsType, copy.Content, copy.UpdatedAt);
    }

    /// <summary>The copy a pulled document carries: its text verbatim and its
    /// stamp.</summary>
    public static IterationSettingsCopyDto ToCopy(TaskChange change)
    {
        ArgumentNullException.ThrowIfNull(change);

        return new IterationSettingsCopyDto(change.Task.ContentMd ?? string.Empty, change.UpdatedAt);
    }
}
