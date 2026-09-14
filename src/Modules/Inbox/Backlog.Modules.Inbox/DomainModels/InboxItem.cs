using Backlog.Modules.Inbox.Abstractions;

namespace Backlog.Modules.Inbox.DomainModels;

/// <summary>
/// Aggregate root of the Inbox bounded context: one captured thought and every
/// decision taken about it since. All mutation of the tags, the repositories,
/// the filing and the lifecycle goes through this root.
/// <para>
/// Two things never change once the item exists: <see cref="CapturedAt"/> and
/// <see cref="SourceUrl"/>. They are what the capture <em>was</em>, and an
/// invariant of the domain (<c>.domain/inbox/domain.md</c>) rather than a
/// convenience — so they have no setter at all instead of a guarded one.
/// </para>
/// <para>
/// Two things here are not about the capture. <see cref="ReplicaBacked"/> says
/// the id is a replica capture's, and <see cref="ReplicaAckPending"/> says the
/// replica has not yet been told this desktop dealt with it. Both are sync
/// bookkeeping that happens to be cheapest to keep on the item, because the
/// events that set the flag — archive, route — are the item's own; nothing
/// outside the outbox reads either.
/// </para>
/// </summary>
public sealed class InboxItem
{
    private readonly List<InboxTag> _tags = [];
    private readonly List<string> _repoIds = [];

    /// <summary>A thought captured on this machine, or through a channel with no
    /// replica behind it. Born <see cref="InboxStatus.Unprocessed"/> with a fresh
    /// id.</summary>
    public static InboxItem Capture(
        string title,
        InboxSource source,
        string? sourceUrl,
        ContentKind kind,
        DateTimeOffset capturedAt,
        DateTimeOffset receivedAt,
        bool replicaBacked) =>
        new(Guid.CreateVersion7(), title, string.Empty, sourceUrl, capturedAt, receivedAt, kind, source, replicaBacked);

    /// <summary>A capture pulled from the replica. It <em>reuses the capture's
    /// id</em>, which is what makes intake idempotent by primary key: the same
    /// document arriving again is the same row, and the desktop's own tombstone
    /// echo finds the item it acknowledged.</summary>
    public static InboxItem FromCapture(
        Guid captureId,
        string title,
        InboxSource source,
        string? sourceUrl,
        ContentKind kind,
        DateTimeOffset capturedAt,
        DateTimeOffset receivedAt) =>
        new(captureId, title, string.Empty, sourceUrl, capturedAt, receivedAt, kind, source, replicaBacked: true);

    /// <summary>Full constructor, also used by storage to rehydrate a persisted
    /// item. Born unprocessed and unstamped beyond <paramref name="receivedAt"/>;
    /// storage restores the rest through the load-only paths
    /// (<see cref="LoadTags"/>, then <see cref="LoadState"/> last) and the
    /// setters whose rules a stored row cannot fail.
    /// <para>
    /// The title is stored as one line: every run of whitespace, line breaks
    /// included, becomes a single space. A capture typed on a phone can carry
    /// a newline, and a title is drawn on one row here and written on one line
    /// when it becomes entry text — where line two is the metadata line, so a
    /// break in the title would put the tokens in the body.
    /// </para></summary>
    public InboxItem(
        Guid id,
        string title,
        string bodyMd,
        string? sourceUrl,
        DateTimeOffset capturedAt,
        DateTimeOffset receivedAt,
        ContentKind kind,
        InboxSource source,
        bool replicaBacked)
    {
        if (string.IsNullOrWhiteSpace(title))
            throw new ArgumentException("Title is required.", nameof(title));
        ArgumentNullException.ThrowIfNull(source);

        Id = id;
        Title = string.Join(' ', title.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        BodyMd = bodyMd ?? string.Empty;
        SourceUrl = string.IsNullOrWhiteSpace(sourceUrl) ? null : sourceUrl.Trim();
        CapturedAt = capturedAt;
        ReceivedAt = receivedAt;
        Kind = kind;
        KindSlug = InboxEnumMap.ToWire(kind);
        Source = source;
        ReplicaBacked = replicaBacked;
        Status = InboxStatus.Unprocessed;

        // Born already stamped, for the reason TaskItem is: an item saved with
        // no further mutation would otherwise carry the default 0001-01-01.
        UpdatedAt = receivedAt;
    }

    public Guid Id { get; }

    public string Title { get; }

    public string BodyMd { get; }

    /// <summary>Invariant: preserved unchanged from capture.</summary>
    public string? SourceUrl { get; }

    /// <summary>Invariant: preserved unchanged from capture. What the phone
    /// said, not when this desktop heard it — that is <see cref="ReceivedAt"/>.</summary>
    public DateTimeOffset CapturedAt { get; }

    public DateTimeOffset ReceivedAt { get; }

    public InboxStatus Status { get; private set; }

    public DateOnly? DeferredUntil { get; private set; }

    public ContentKind Kind { get; private set; }

    /// <summary>The kind's raw token. Equal to <c>InboxEnumMap.ToWire(Kind)</c>
    /// for every kind this build knows; for one it does not, the word the pane
    /// shows instead of "Text".</summary>
    public string KindSlug { get; private set; }

    public InboxSource Source { get; }

    public IReadOnlyList<InboxTag> Tags => _tags;

    public IReadOnlyList<string> RepoIds => _repoIds;

    public Guid? ListId { get; private set; }

    public RoutingTarget? Routing { get; private set; }

    /// <summary>True when <see cref="Id"/> is a replica capture id — the item
    /// arrived through sync, and the replica will want to hear what became of it.</summary>
    public bool ReplicaBacked { get; }

    /// <summary>True between this desktop deciding about a replica-backed item
    /// and the tombstone for it being accepted by the replica.</summary>
    public bool ReplicaAckPending { get; private set; }

    /// <summary>When this item last changed. Restamped by every mutator; not
    /// synced, so the stamp is bookkeeping rather than a tie-break.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>"Routed" as <c>flow.md</c> defines it: triaged, with a target.</summary>
    public bool IsRouted => Routing is not null;

    /// <summary>What a list counts: items still waiting for a decision.</summary>
    public bool IsOpen => Status is InboxStatus.Unprocessed or InboxStatus.Deferred;

    // --- Reading the capture ------------------------------------------------

    /// <summary>Records what the capture turned out to be, with the raw slug
    /// beside it. The slug is stored verbatim so an unknown one survives a
    /// round trip through this build unchanged.</summary>
    public void SetKind(ContentKind kind, string slug)
    {
        Kind = kind;
        KindSlug = string.IsNullOrWhiteSpace(slug) ? InboxEnumMap.ToWire(kind) : slug.Trim();
        Touch();
    }

    /// <summary>Replaces the tags. Names are stored bare and de-duplicated by
    /// canonical name; a name that reads as a person (<c>@bob</c>) is refused,
    /// because a person is a <see cref="Source"/> and putting one among the tags
    /// would draw <c>#@bob</c> on the chip. The check runs on the bare name —
    /// after the <c>#</c> is stripped — so <c>#@bob</c> is caught as the
    /// person it is rather than stored as <c>@bob</c>.</summary>
    public void SetTags(IEnumerable<InboxTag> tags)
    {
        ArgumentNullException.ThrowIfNull(tags);

        var cleaned = new List<InboxTag>();

        foreach (var tag in tags)
        {
            var name = (tag.Name ?? string.Empty).Trim().TrimStart('#').Trim();
            if (name.StartsWith('@'))
                throw new ArgumentException($"'{name}' names a person, and a person is not a tag.", nameof(tags));

            if (name.Length == 0) continue;

            var candidate = new InboxTag(name, tag.AutoGenerated);
            if (!cleaned.Contains(candidate)) cleaned.Add(candidate);
        }

        _tags.Clear();
        _tags.AddRange(cleaned);
        Touch();
    }

    /// <summary>Replaces the repositories the item will route to. Registry ids,
    /// distinct without regard to case, because the registry compares them that
    /// way and two spellings of one repository would make two entries.</summary>
    public void SetRepoIds(IEnumerable<string> repoIds)
    {
        ArgumentNullException.ThrowIfNull(repoIds);

        _repoIds.Clear();
        _repoIds.AddRange(repoIds
            .Select(id => (id ?? string.Empty).Trim())
            .Where(id => id.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase));
        Touch();
    }

    /// <summary>Files the item in a list, or back in the unfiled inbox. Allowed
    /// in every state, archived included: filing is not triage, and a person
    /// tidying an archive is not reopening anything.</summary>
    public void MoveToList(Guid? listId)
    {
        ListId = listId;
        Touch();
    }

    // --- Lifecycle ----------------------------------------------------------

    /// <summary>Puts the item aside. From unprocessed or triaged-but-not-routed;
    /// a routed item has left, and an archived one is closed.</summary>
    public void Defer(DateOnly? until, DateTimeOffset now)
    {
        if (IsRouted || Status is not (InboxStatus.Unprocessed or InboxStatus.Triaged))
            throw new InvalidInboxTransitionException(Status, "deferred");

        Status = InboxStatus.Deferred;
        DeferredUntil = until;
        Touch(now);
    }

    /// <summary>Brings a deferred item back into the queue. Not wired to a
    /// scheduler in this scope; the transition exists because the lifecycle
    /// names it.</summary>
    public void Resurface(DateTimeOffset now)
    {
        if (Status is not InboxStatus.Deferred)
            throw new InvalidInboxTransitionException(Status, "resurfaced");

        Status = InboxStatus.Unprocessed;
        DeferredUntil = null;
        Touch(now);
    }

    /// <summary>Dismisses the item. From any state but the two terminal ones —
    /// routed and archived. Marks the replica acknowledgement pending when the
    /// item came from one, because the phone is still offering it.</summary>
    public void Archive(DateTimeOffset now)
    {
        if (IsRouted || Status is InboxStatus.Archived)
            throw new InvalidInboxTransitionException(Status, "archived");

        Status = InboxStatus.Archived;
        DeferredUntil = null;
        if (ReplicaBacked) ReplicaAckPending = true;
        Touch(now);
    }

    /// <summary>
    /// Records that the item became backlog entries. Exactly once: routing is
    /// the terminal outcome of triage (<c>flow.md</c>), and a second routing
    /// would be a second set of entries for one thought with no record of the
    /// first. The status becomes <see cref="InboxStatus.Triaged"/> here rather
    /// than through a separate step, because in this product deciding
    /// <em>is</em> routing.
    /// </summary>
    public void RouteToBacklog(IReadOnlyList<Guid> taskIds, IReadOnlyList<string> repoIds, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(taskIds);
        ArgumentNullException.ThrowIfNull(repoIds);

        if (IsRouted) throw new InvalidInboxTransitionException(Status, "routed again");
        if (Status is InboxStatus.Archived) throw new InvalidInboxTransitionException(Status, "routed");

        Routing = new RoutingTarget(RoutingDomain.Tasks, [.. repoIds], [.. taskIds], now);
        Status = InboxStatus.Triaged;
        DeferredUntil = null;
        if (ReplicaBacked) ReplicaAckPending = true;
        Touch(now);
    }

    /// <summary>The outbox drained: the replica has the tombstone.</summary>
    public void MarkReplicaAcknowledged()
    {
        ReplicaAckPending = false;
        Touch();
    }

    // --- Rehydration --------------------------------------------------------

    /// <summary>
    /// Restores the tags a persisted item was written with, as they are. No
    /// rule and no stamp, for the reason <c>TaskItem.LoadSubItem</c> gives
    /// none: a row was valid when it was written, and a rule added since — the
    /// person check in <see cref="SetTags"/> is one — must not turn a stored
    /// row into an exception on read, which would take the whole inbox down
    /// with it. Storage calls this; nothing else should.
    /// </summary>
    public void LoadTags(IEnumerable<InboxTag> tags)
    {
        ArgumentNullException.ThrowIfNull(tags);

        _tags.Clear();
        _tags.AddRange(tags);
    }

    /// <summary>
    /// Restores the state a persisted item was written with, without treating
    /// the load as an edit. <b>Storage must call this last</b>, for the reason
    /// <c>TaskItem.LoadStamps</c> gives: every setter above restamps
    /// <see cref="UpdatedAt"/> to now, so a read that ended anywhere but here
    /// would overwrite the stamp it had just read. The lifecycle fields are here
    /// as well because the guarded mutators refuse the very transitions a stored
    /// row may already have made — an archived item cannot be re-archived on
    /// load.
    /// </summary>
    public void LoadState(
        InboxStatus status,
        DateOnly? deferredUntil,
        RoutingTarget? routing,
        bool replicaAckPending,
        DateTimeOffset updatedAt)
    {
        Status = status;
        DeferredUntil = deferredUntil;
        Routing = routing;
        ReplicaAckPending = replicaAckPending;
        UpdatedAt = updatedAt;
    }

    // --- Internals ----------------------------------------------------------

    /// <summary>The mutators that carry no instant of their own stamp the wall
    /// clock, as <c>TaskItem.Touch</c> does; the lifecycle steps take the
    /// handler's clock so a test can place them.</summary>
    private void Touch() => UpdatedAt = DateTimeOffset.UtcNow;

    private void Touch(DateTimeOffset now) => UpdatedAt = now;
}
