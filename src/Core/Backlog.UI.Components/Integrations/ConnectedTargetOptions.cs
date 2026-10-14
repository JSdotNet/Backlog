using Backlog.UI.Components.Selects;

namespace Backlog.UI.Components.Integrations;

/// <summary>
/// One connected repository or product as <see cref="ConnectedTargetEditor"/> edits
/// it: which connector it came through, what it is called, and how its sync
/// behaves.
/// <para>
/// The library's own shape rather than the Tasks module's <c>ConnectedTarget</c>,
/// because this library references no module. The host maps one onto the other;
/// every connector's targets have the same options, so the editor needs nothing
/// connector-specific beyond the descriptor's name, icon and tone — and, as data the
/// host hands in, the statuses the source lists for its status map.
/// </para>
/// </summary>
/// <param name="ConnectorName">The connector descriptor's display name.</param>
/// <param name="Target">The repository or product, in the connector's spelling.</param>
/// <param name="Enabled">Whether the target is synced at all.</param>
/// <param name="TitleFollowsSource">Whether a linked task's title follows the
/// source until a person renames it.</param>
/// <param name="PromoteArchivesOriginal">Whether promoting a linked task to a plan
/// archives the original.</param>
/// <param name="SyncIntervalMinutes">How often the target is synced.</param>
/// <param name="SkipUntouchedOlderThanDays">Items untouched for longer than this
/// many days are not brought in; null brings every open item.</param>
/// <param name="CompleteAtSource">Whether finishing a task here finishes its item at
/// the source. Offered only where <see cref="CanCompleteAtSource"/> says the
/// connector can.</param>
public sealed record ConnectedTargetOptions(
    string ConnectorName,
    string Target,
    bool Enabled = true,
    bool TitleFollowsSource = true,
    bool PromoteArchivesOriginal = true,
    int SyncIntervalMinutes = 15,
    int? SkipUntouchedOlderThanDays = null,
    bool CompleteAtSource = false)
{
    /// <summary>The descriptor's icon name, drawn when it names a provider mark.</summary>
    public string? Icon { get; init; }

    /// <summary>Whether the connector can finish an item at its source, so the card
    /// offers <see cref="CompleteAtSource"/>. The connector's capability, handed in by
    /// the host, rather than an option a person sets: a switch for something the
    /// source cannot do would do nothing.</summary>
    public bool CanCompleteAtSource { get; init; }

    /// <summary>When the target last synced, already formatted by the host, or null
    /// when it never has.</summary>
    public string? LastSynced { get; init; }

    /// <summary>Why the last sync failed, in the sentence the sync gave, or null
    /// when it did not. Shown on the card until a sync succeeds, so a target that
    /// has stopped syncing says why without anybody pressing Sync now.</summary>
    public string? LastSyncError { get; init; }

    /// <summary>When that sync failed, already formatted by the host, or null.</summary>
    public string? LastSyncFailed { get; init; }

    /// <summary>The repository the target's new tasks are filed under, as the value
    /// of one of <see cref="RepositoryChoices"/>, or null for the connector's
    /// default. A stored value the choices no longer hold is still offered, so
    /// editing another option never rewrites it.</summary>
    public string? RepositoryId { get; init; }

    /// <summary>The repositories a person may pick, handed in by the host from its
    /// registry. With none, and nothing stored, the card draws no repository
    /// picker: there is nothing to pick.</summary>
    public IReadOnlyList<SelectorOption> RepositoryChoices { get; init; } = [];

    /// <summary>Whether the target is itself a repository, so the picker's empty
    /// choice reads as filing under it rather than under none. The connector's
    /// capability, handed in by the host.</summary>
    public bool TargetIsRepository { get; init; }

    /// <summary>One row per status the source's items can be in, in the source's
    /// order, each with the task status it is mapped to. Empty draws no Statuses
    /// group, which is every connector whose statuses need no map.</summary>
    public IReadOnlyList<SourceStatusRow> StatusRows { get; init; } = [];

    /// <summary>The task statuses a row may be mapped to, handed in by the host:
    /// this library knows no task status of its own.</summary>
    public IReadOnlyList<SelectorOption> StatusChoices { get; init; } = [];

    /// <summary>The sync intervals the editor offers, in minutes. A stored interval
    /// that is none of these is still offered, so editing another option never
    /// rewrites it.</summary>
    public static IReadOnlyList<int> IntervalChoices { get; } = [5, 15, 30, 60, 240];

    /// <summary>The ages the editor offers for skipping untouched items, in days.</summary>
    public static IReadOnlyList<int> SkipChoices { get; } = [30, 90, 180, 365];
}
