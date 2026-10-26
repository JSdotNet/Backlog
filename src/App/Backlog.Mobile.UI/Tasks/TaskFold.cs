using Backlog.Modules.Sync.Abstractions.DataTransferObjects;

namespace Backlog.Mobile.UI.Tasks;

/// <summary>
/// The task feed, folded into one row per task. No domain rules: which copy of a
/// task wins is decided by its stamps and nothing else, the same whole-document
/// last-write-wins the replica keeps (.devbook/arc42/adr/0005).
/// <para>
/// The fold is order-free, which is what lets a page arrive twice, or late: the
/// later <see cref="TaskChange.UpdatedAt"/> wins, an equal stamp goes to the
/// higher <see cref="TaskChangeRecord.ServerTimestamp"/> and then to the tombstone,
/// and a deletion is kept as a hidden row rather than an absence — an absence
/// would let the older copy behind it back in.
/// </para>
/// </summary>
public static class TaskFold
{
    /// <summary>The type token a capture document carries. A capture is the
    /// Inbox's, not a task (.devbook/arc42/adr/0009), and the Tasks tab never
    /// shows one.</summary>
    public const string CaptureType = "capture";

    /// <summary>The kind tokens of the roadmap's two documents — the whole plan and
    /// the pace it is drawn at (.devbook/arc42/adr/0018). The phone has no roadmap to
    /// apply them to, so it keeps no row it would never list. Literals duplicated from
    /// the desktop's sync client, for the reason <see cref="CaptureType"/> is.</summary>
    public const string RoadmapPlanType = "roadmap-plan";

    /// <inheritdoc cref="RoadmapPlanType"/>
    public const string PlanningPaceType = "planning-pace";

    /// <summary>The kind tokens of the GitHub settings' two documents — the
    /// repository registry and the account identities (.devbook/arc42/adr/0021). The
    /// phone keeps no GitHub settings, so it keeps no row for either. Literals
    /// duplicated for the reason <see cref="CaptureType"/> is.</summary>
    public const string RepositoryRegistryType = "repository-registry";

    /// <inheritdoc cref="RepositoryRegistryType"/>
    public const string GitHubAccountsType = "github-accounts";

    /// <summary>The kind token of a retired sprint series — what desktops pushed before
    /// local ADR 0035 retired sprints. A replica may still hold one, so the phone reads it
    /// and drops it, keeping no row. A literal duplicated for the reason
    /// <see cref="CaptureType"/> is.</summary>
    public const string SprintSeriesType = "sprint-series";

    /// <summary>The kind token of a goal — Progress's whole goal, synced between desktops
    /// (.devbook/arc42/adr/0035). Never a task row; the phone's goal pages read it from
    /// their own read model once they exist. A literal duplicated for the reason
    /// <see cref="CaptureType"/> is; persisted on every replica, so never to be renamed.</summary>
    public const string GoalType = "goal";

    /// <summary>The kind token of an impediment — Progress's whole impediment, synced
    /// between desktops (.devbook/arc42/adr/0035). Never a task row, for the reason
    /// <see cref="GoalType"/> gives; persisted on every replica, so never to be renamed.</summary>
    public const string ImpedimentType = "impediment";

    /// <summary>The kind token of the stand-up context — what a desktop writes for the phone
    /// to read the stand-up by: pull request statuses, the pace and the working week (local
    /// ADR 0035 §8). Never a task row; <see cref="Progress.ProgressFold"/> keeps it. A literal
    /// duplicated for the reason <see cref="CaptureType"/> is; persisted, so never renamed.</summary>
    public const string StandUpContextType = "stand-up-context";

    /// <summary>The kind token of an impediment edit the phone queues for a desktop to apply
    /// (local ADR 0035 §8). Never a task row; persisted, so never renamed.</summary>
    public const string ImpedimentEditType = "impediment-edit";

    /// <summary>The kind token of the My Day preferences — Clear My Day overnight, set
    /// on a desktop (.devbook/arc42/adr/0032). Never a row: the projection keeps the
    /// document whole beside the rows, as <see cref="MyDayPreferencesDocument"/>. A
    /// literal duplicated for the reason <see cref="CaptureType"/> is; persisted on
    /// every replica, so never to be renamed.</summary>
    public const string MyDayPreferencesType = "my-day-preferences";

    /// <summary>The kind token of the iteration settings — the iteration length and the
    /// two-week start, set on a desktop and carried between desktops
    /// (.devbook/arc42/adr/0036). Never a row: the phone reads the iteration from the
    /// stand-up context a desktop writes instead. A literal duplicated for the reason
    /// <see cref="CaptureType"/> is; persisted on every replica, so never to be renamed.</summary>
    public const string IterationSettingsType = "iteration-settings";

    /// <summary>Every kind the Tasks tab never lists. A note is the Inbox's, and
    /// <see cref="Notes.NoteFold"/> folds it into its own view.</summary>
    private static readonly HashSet<string> NotTasks = new(StringComparer.OrdinalIgnoreCase)
    {
        CaptureType,
        Notes.NoteFold.NoteType,
        RoadmapPlanType,
        PlanningPaceType,
        RepositoryRegistryType,
        GitHubAccountsType,
        SprintSeriesType,
        MyDayPreferencesType,
        IterationSettingsType,
        GoalType,
        ImpedimentType,
        StandUpContextType,
        ImpedimentEditType,
    };

    /// <summary>Folds <paramref name="records"/> into <paramref name="rows"/>, and
    /// answers with the rows that changed — the ones to write.</summary>
    public static IReadOnlyList<TaskViewRow> Apply(IDictionary<Guid, TaskViewRow> rows, IEnumerable<TaskChangeRecord> records)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(records);

        var changed = new Dictionary<Guid, TaskViewRow>();

        foreach (var record in records)
        {
            var change = record.Change;
            if (!IsTask(change.Task.Type)) continue;

            var incoming = new TaskViewRow(change.Id, change.UpdatedAt, change.DeletedAt, record.ServerTimestamp, change.Task);

            if (rows.TryGetValue(change.Id, out var current) && !Wins(incoming, current)) continue;

            rows[change.Id] = incoming;
            changed[change.Id] = incoming;
        }

        return [.. changed.Values];
    }

    /// <summary>Whether a document of this kind is one the Tasks tab could list: not
    /// the Inbox's capture, and none of the whole documents the desktop
    /// replicates.</summary>
    private static bool IsTask(string? type) => type is null || !NotTasks.Contains(type);

    /// <summary>Whether <paramref name="incoming"/> replaces <paramref name="current"/>.
    /// A strict order over every field that can differ, so two phones folding the
    /// same records in different orders end on the same row.</summary>
    public static bool Wins(TaskViewRow incoming, TaskViewRow current)
    {
        ArgumentNullException.ThrowIfNull(incoming);
        ArgumentNullException.ThrowIfNull(current);

        if (incoming.UpdatedAt != current.UpdatedAt) return incoming.UpdatedAt > current.UpdatedAt;
        if (incoming.ServerTimestamp != current.ServerTimestamp) return incoming.ServerTimestamp > current.ServerTimestamp;

        return incoming.DeletedAt is not null && current.DeletedAt is null;
    }
}
