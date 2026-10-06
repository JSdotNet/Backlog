namespace Backlog.Modules.Tasks.Abstractions.Connectors;

/// <summary>
/// One repository or product a person connected, how it behaves, and how far its
/// sync has got.
/// <para>
/// Each question the design left open is a setting here, so two targets may answer
/// it differently (ADR 0020, §9). The defaults are the record's: nothing skipped,
/// the title following the source, a fifteen-minute interval, Promote to plan
/// archiving the original, and nothing completed at the source.
/// </para>
/// <para>
/// <b>No credentials.</b> A target names what to fetch, never how to sign in: the
/// file it is kept in is plain text meant to be read and hand-edited, and a token
/// belongs in the platform's credential store, which is the connector adapter's.
/// </para>
/// </summary>
/// <param name="ConnectorId">The connector this target is fetched through.</param>
/// <param name="Target">What is fetched — a repository, a product slug — in the
/// connector's own spelling.</param>
/// <param name="Enabled">Whether the sync runs for it. A disabled target keeps its
/// settings and its tasks.</param>
public sealed record ConnectedTarget(string ConnectorId, string Target, bool Enabled = true)
{
    /// <summary>The default <see cref="SyncInterval"/>.</summary>
    public static readonly TimeSpan DefaultSyncInterval = TimeSpan.FromMinutes(15);

    /// <summary>On the first sync, items untouched for longer than this are not
    /// brought in; null brings everything. Read once, to set
    /// <see cref="IgnoreUntouchedBefore"/>.</summary>
    public TimeSpan? SkipUntouchedOlderThan { get; init; }

    /// <summary>Whether each sync overwrites the title with the source's. Off means
    /// the task keeps whatever title it has.</summary>
    public bool TitleFollowsSource { get; init; } = true;

    /// <summary>How long after a sync the desktop's timer runs the next one.</summary>
    public TimeSpan SyncInterval { get; init; } = DefaultSyncInterval;

    /// <summary>Whether Promote to plan archives the task it promoted, rather than
    /// keeping it as an umbrella step.</summary>
    public bool PromoteArchivesOriginal { get; init; } = true;

    /// <summary>
    /// Whether finishing one of this target's tasks here finishes its item at the
    /// source too. Off by default: closing something in another system is a step a
    /// person opts into per target, and only a connector whose
    /// <see cref="TaskConnectorCapabilities.CanComplete"/> is set is asked.
    /// </summary>
    public bool CompleteAtSource { get; init; }

    /// <summary>When the last successful sync started fetching, or null before the
    /// first. The next fetch asks for what closed since then.</summary>
    public DateTimeOffset? LastSyncedAt { get; init; }

    /// <summary>The cut-off <see cref="SkipUntouchedOlderThan"/> set at the first
    /// sync: an item with no task yet that was last changed before it is never
    /// created. Fixed once, so the window does not slide and drop an item that was
    /// recent enough the day the target was connected.</summary>
    public DateTimeOffset? IgnoreUntouchedBefore { get; init; }

    /// <summary>Whether this is the target named by the pair. Connector ids are
    /// compared exactly; targets without regard to case, because a repository
    /// name is case-preserving but not case-sensitive.</summary>
    public bool Is(string connectorId, string target) =>
        string.Equals(ConnectorId, connectorId, StringComparison.Ordinal)
        && string.Equals(Target, target, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Where the connected targets are kept. The host's: the desktop keeps them in a
/// JSON file beside its other per-user settings.
/// </summary>
public interface IConnectedTargets
{
    /// <summary>Raised after a save or a removal.</summary>
    event Action? Changed;

    /// <summary>Every connected target, in the order they were added.</summary>
    IReadOnlyList<ConnectedTarget> List();

    /// <summary>The target named by the pair, or null.</summary>
    ConnectedTarget? Get(string connectorId, string target);

    /// <summary>Adds the target, or replaces the one with the same connector and
    /// target. Answers a sentence for the person when it could not be kept for next
    /// time, and null when it was.</summary>
    string? Save(ConnectedTarget target);

    /// <summary>
    /// Replaces the target named by the pair with <paramref name="change"/> applied to
    /// what the store holds at that moment, as one step under the store's own lock.
    /// Does nothing when no such target is connected — an update never adds one.
    /// Answers a sentence when it could not be kept for next time, and null when it
    /// was.
    /// <para>
    /// What the sync records its progress with. A read followed by a
    /// <see cref="Save"/> would write back whatever it read, over a setting the
    /// person changed between the two.
    /// </para>
    /// </summary>
    string? Update(string connectorId, string target, Func<ConnectedTarget, ConnectedTarget> change);

    /// <summary>Removes the target named by the pair; nothing when there is none. The
    /// tasks it brought in stay. Answers a sentence when the removal could not be kept
    /// for next time, and null when it was.</summary>
    string? Remove(string connectorId, string target);
}
