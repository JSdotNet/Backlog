namespace Backlog.UI.Components.Integrations;

/// <summary>
/// One connected repository or product as <see cref="ConnectedTargetEditor"/> edits
/// it: which connector it came through, what it is called, and how its sync
/// behaves.
/// <para>
/// The library's own shape rather than the Tasks module's <c>ConnectedTarget</c>,
/// because this library references no module. The host maps one onto the other;
/// every connector's targets have the same options, so the editor needs nothing
/// connector-specific beyond the descriptor's name, icon and tone.
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
public sealed record ConnectedTargetOptions(
    string ConnectorName,
    string Target,
    bool Enabled = true,
    bool TitleFollowsSource = true,
    bool PromoteArchivesOriginal = true,
    int SyncIntervalMinutes = 15,
    int? SkipUntouchedOlderThanDays = null)
{
    /// <summary>The descriptor's icon name, drawn when it names a provider mark.</summary>
    public string? Icon { get; init; }

    /// <summary>When the target last synced, already formatted by the host, or null
    /// when it never has.</summary>
    public string? LastSynced { get; init; }

    /// <summary>The sync intervals the editor offers, in minutes. A stored interval
    /// that is none of these is still offered, so editing another option never
    /// rewrites it.</summary>
    public static IReadOnlyList<int> IntervalChoices { get; } = [5, 15, 30, 60, 240];

    /// <summary>The ages the editor offers for skipping untouched items, in days.</summary>
    public static IReadOnlyList<int> SkipChoices { get; } = [30, 90, 180, 365];
}
