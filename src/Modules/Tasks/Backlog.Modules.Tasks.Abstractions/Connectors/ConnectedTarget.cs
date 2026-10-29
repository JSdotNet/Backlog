using Backlog.Modules.Tasks.Abstractions.Services;

namespace Backlog.Modules.Tasks.Abstractions.Connectors;

/// <summary>
/// One repository or product a person connected, how it behaves, and how far its
/// sync has got.
/// <para>
/// Each question the design left open is a setting here, so two targets may answer
/// it differently (ADR 0020, §9). The defaults are the record's: nothing skipped,
/// the title following the source, a fifteen-minute interval, and nothing completed
/// at the source.
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

    /// <summary>
    /// Whether finishing one of this target's tasks here finishes its item at the
    /// source too. Off by default: closing something in another system is a step a
    /// person opts into per target, and only a connector whose
    /// <see cref="TaskConnectorCapabilities.CanComplete"/> is set is asked.
    /// </summary>
    public bool CompleteAtSource { get; init; }

    /// <summary>
    /// Whether only the questions assigned to the person reach them from this target,
    /// rather than those and the unassigned ones they may answer. Off by default, and
    /// offered only where the connector's <see cref="TaskConnectorCapabilities.HasQuestions"/>
    /// is set: a source that asks nothing has nothing to narrow.
    /// </summary>
    public bool OnlyQuestionsAssignedToMe { get; init; }

    /// <summary>When the last successful sync started fetching, or null before the
    /// first. The next fetch asks for what closed since then.</summary>
    public DateTimeOffset? LastSyncedAt { get; init; }

    /// <summary>The cut-off <see cref="SkipUntouchedOlderThan"/> set at the first
    /// sync: an item with no task yet that was last changed before it is never
    /// created. Fixed once, so the window does not slide and drop an item that was
    /// recent enough the day the target was connected.</summary>
    public DateTimeOffset? IgnoreUntouchedBefore { get; init; }

    /// <summary>Why the last sync failed, in the sentence the person reads, or null
    /// when it succeeded. Set by a failed sync without moving
    /// <see cref="LastSyncedAt"/>, so the next one still asks from the last success,
    /// and cleared by the next sync that succeeds.</summary>
    public string? LastSyncError { get; init; }

    /// <summary>When the failure in <see cref="LastSyncError"/> happened, or null
    /// when there is none.</summary>
    public DateTimeOffset? LastSyncFailedAt { get; init; }

    /// <summary>
    /// The repository this target's tasks are filed under, as its <c>owner/name</c>,
    /// or null for the connector's default: a target that is itself a repository
    /// (<see cref="TaskConnectorCapabilities.TargetIsRepository"/>) files under
    /// itself, and any other under none. Set, it wins for every connector — a
    /// spec-manager product's tasks filed under the repository the product is built
    /// in, a GitHub repository's under another one (ADR 0020, §9).
    /// <para>
    /// It fills a task filed under no repository and never overwrites one that is.
    /// Blank reads as null, and the spelling is trimmed.
    /// </para>
    /// </summary>
    public string? RepositoryId
    {
        get => _repositoryId;
        init => _repositoryId = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    /// <summary>
    /// The task status each of this target's source statuses becomes, keyed by the
    /// status's name as <see cref="SourceItem.SourceStateName"/> spells it and
    /// compared without regard to case or the spaces around it. A name the map does
    /// not have takes the fixed map from its normalised state (ADR 0020, §4 and §9).
    /// Empty by default, which is the fixed map for every status.
    /// <para>
    /// What a product whose statuses normalise badly needs: one with no status
    /// flagged as work reads every status after the first as Active, so without a
    /// map each of its tasks lands in In progress.
    /// </para>
    /// <para>
    /// Compared by its entries, so two targets with the same map are equal and a
    /// store can tell a save that changes nothing.
    /// </para>
    /// </summary>
    public IReadOnlyDictionary<string, EntryStatus> StatusMap
    {
        get => _statusMap;
        init => _statusMap = SourceStatusMap.Of(value);
    }

    private readonly string? _repositoryId;
    private readonly SourceStatusMap _statusMap = SourceStatusMap.Empty;

    /// <summary>The status <see cref="StatusMap"/> gives the source status named
    /// <paramref name="sourceStateName"/>, or null when it gives none.</summary>
    public EntryStatus? MappedStatusOf(string? sourceStateName) =>
        _statusMap.Find(sourceStateName);

    /// <summary>The repository this target's tasks are filed under, as named rather
    /// than resolved: <see cref="RepositoryId"/> when set, else the target itself
    /// when its connector's targets are repositories, else null.</summary>
    /// <param name="targetIsRepository">The connector's
    /// <see cref="TaskConnectorCapabilities.TargetIsRepository"/>.</param>
    public string? FilesUnder(bool targetIsRepository) =>
        RepositoryId ?? (targetIsRepository ? Target.Trim() : null);

    /// <summary>
    /// The repository <see cref="FilesUnder"/> names, when the registry knows it and
    /// it is archived; null otherwise. A target with one is paused: the sync, its
    /// timer and its card's Sync now all read this, so the pause is derived on every
    /// read and never written — <see cref="Enabled"/> keeps what the person set, and
    /// Unarchive resumes the sync with nothing to undo (local ADR 0029 §5).
    /// </summary>
    public TasksRepositoryRef? ArchivedRepository(bool targetIsRepository, IRepositoryDirectory? repositories) =>
        repositories is not null && FilesUnder(targetIsRepository) is { } named
            && repositories.Resolve(named) is { IsArchived: true } repository
            ? repository
            : null;

    /// <summary>Whether this is the target named by the pair. Connector ids are
    /// compared exactly; targets without regard to case, because a repository
    /// name is case-preserving but not case-sensitive.</summary>
    public bool Is(string connectorId, string target) =>
        string.Equals(ConnectorId, connectorId, StringComparison.Ordinal)
        && string.Equals(Target, target, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// <see cref="StatusMap"/>'s value: read-only, keyed without regard to case,
    /// blank names dropped and the rest trimmed, and equal to another holding the
    /// same entries — a dictionary compares by reference, which would make every
    /// target read back from a file differ from the one saved.
    /// </summary>
    private sealed class SourceStatusMap : IReadOnlyDictionary<string, EntryStatus>, IEquatable<SourceStatusMap>
    {
        public static readonly SourceStatusMap Empty = new([]);

        private readonly Dictionary<string, EntryStatus> _entries;

        private SourceStatusMap(Dictionary<string, EntryStatus> entries) => _entries = entries;

        /// <summary>The map <paramref name="entries"/> describe; a later entry for a
        /// name wins over an earlier one.</summary>
        public static SourceStatusMap Of(IEnumerable<KeyValuePair<string, EntryStatus>>? entries)
        {
            if (entries is SourceStatusMap map) return map;

            var cleaned = new Dictionary<string, EntryStatus>(StringComparer.OrdinalIgnoreCase);
            foreach (var (name, status) in entries ?? [])
            {
                if (!string.IsNullOrWhiteSpace(name)) cleaned[name.Trim()] = status;
            }

            return cleaned.Count == 0 ? Empty : new(cleaned);
        }

        public EntryStatus? Find(string? name) =>
            name is not null && _entries.TryGetValue(name.Trim(), out var status) ? status : null;

        public EntryStatus this[string key] => _entries[key.Trim()];

        public IEnumerable<string> Keys => _entries.Keys;

        public IEnumerable<EntryStatus> Values => _entries.Values;

        public int Count => _entries.Count;

        public bool ContainsKey(string key) => _entries.ContainsKey(key.Trim());

        public bool TryGetValue(string key, out EntryStatus value) => _entries.TryGetValue(key.Trim(), out value);

        public IEnumerator<KeyValuePair<string, EntryStatus>> GetEnumerator() => _entries.GetEnumerator();

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

        public bool Equals(SourceStatusMap? other) =>
            other is not null
            && other.Count == Count
            && _entries.All(entry => other._entries.TryGetValue(entry.Key, out var status) && status == entry.Value);

        public override bool Equals(object? obj) => Equals(obj as SourceStatusMap);

        /// <summary>Independent of order, and of the case of each name.</summary>
        public override int GetHashCode() =>
            _entries.Aggregate(Count, (hash, entry) =>
                hash ^ HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(entry.Key), entry.Value));
    }
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
