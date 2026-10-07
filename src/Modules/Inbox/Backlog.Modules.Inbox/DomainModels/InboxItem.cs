using Backlog.Modules.Inbox.Abstractions;

namespace Backlog.Modules.Inbox.DomainModels;

/// <summary>
/// Aggregate root of the Inbox bounded context: one captured thought and every
/// decision taken about it since. All mutation of the tags, the repositories,
/// the filing and the lifecycle goes through this root.
/// <para>
/// Two things never change once the item exists: <see cref="CapturedAt"/> and
/// <see cref="SourceUrl"/>. They are what the capture <em>was</em>, and an
/// invariant of the domain (<c>.devbook/domain/inbox/domain.md</c>) rather than a
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
    private readonly List<InboxAttachment> _attachments = [];
    private readonly List<string> _dismissedSuggestions = [];

    /// <summary>A thought captured on this machine, or through a channel with no
    /// replica behind it. Born <see cref="InboxStatus.Unprocessed"/> with a fresh
    /// id. <paramref name="bodyMd"/> is whatever was written beneath the title —
    /// the Add dialog's notes — and is empty for a bare one-line capture.</summary>
    public static InboxItem Capture(
        string title,
        InboxSource source,
        string? sourceUrl,
        ContentKind kind,
        DateTimeOffset capturedAt,
        DateTimeOffset receivedAt,
        bool replicaBacked,
        string? bodyMd = null) =>
        new(Guid.CreateVersion7(), title, bodyMd ?? string.Empty, sourceUrl, capturedAt, receivedAt, kind, source, replicaBacked);

    /// <summary>A capture that arrived with an id of its own — pulled from the
    /// replica, or read off a feed. It <em>reuses the capture's id</em>, which
    /// is what makes intake idempotent by primary key: the same document
    /// arriving again is the same row, the desktop's own tombstone echo finds
    /// the item it acknowledged, and a feed read twice adds nothing.
    /// <paramref name="bodyMd"/> is whatever the channel offered beneath the
    /// title, and empty when it offered nothing. <paramref name="replicaBacked"/>
    /// is true for the replica's own captures and false for a feed's: the id
    /// is reused either way, but only the first has a document behind it to
    /// acknowledge.</summary>
    public static InboxItem FromCapture(
        Guid captureId,
        string title,
        InboxSource source,
        string? sourceUrl,
        ContentKind kind,
        DateTimeOffset capturedAt,
        DateTimeOffset receivedAt,
        string? bodyMd = null,
        bool replicaBacked = true) =>
        new(captureId, title, bodyMd ?? string.Empty, sourceUrl, capturedAt, receivedAt, kind, source, replicaBacked);

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
        Title = Collapse(title);
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
        EditedAt = receivedAt;

        // A note made on this desktop has a phone that has not heard of it yet.
        // One that arrived from the replica is already there.
        NotePushPending = kind is ContentKind.Note && !replicaBacked;
    }

    public Guid Id { get; }

    /// <summary>Fixed at capture for every kind but <see cref="ContentKind.Note"/>,
    /// whose title the person may change with <see cref="EditNote"/>.</summary>
    public string Title { get; private set; }

    /// <summary>Fixed at capture for every kind but <see cref="ContentKind.Note"/>,
    /// as <see cref="Title"/> is.</summary>
    public string BodyMd { get; private set; }

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

    /// <summary>The files the capture arrived with, in the order they were
    /// recorded. Distinct by id; see <see cref="RecordAttachments"/>.</summary>
    public IReadOnlyList<InboxAttachment> Attachments => _attachments;

    public Guid? ListId { get; private set; }

    /// <summary>The suggestions the reader turned down for this item, by key —
    /// <c>tag:sync</c>, <c>repository:owner/name</c>, <c>destination:tasks</c> —
    /// lower case, each once. Classification leaves out whatever is here, which
    /// is what keeps a rejected suggestion from coming back.</summary>
    public IReadOnlyList<string> DismissedSuggestions => _dismissedSuggestions;

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

    /// <summary>The item this one was archived as a duplicate of, or null. Set
    /// only by <see cref="Archive(DateTimeOffset, Guid?)"/> with an item named,
    /// and never cleared: an archived item stays archived, and so does what it
    /// was archived as.</summary>
    public Guid? DuplicateOf { get; private set; }

    /// <summary>
    /// The note's own last-write-wins stamp: when its title, body or files last
    /// changed, or when it was archived or deleted. It is the <c>updated_at</c> a
    /// note document carries on the task feed
    /// (<c>.devbook/arc42/06-runtime-view.md#mobile-note-sync</c>). Kept apart from
    /// <see cref="UpdatedAt"/>, which every mutator restamps (filing, tagging, a
    /// file arriving): a note pushed for such a change would carry a stamp later
    /// than an edit the phone made meanwhile, and overwrite it with the old text.
    /// Kept on every item; only a note's is read.
    /// </summary>
    public DateTimeOffset EditedAt { get; private set; }

    /// <summary>A note: the one kind that syncs both ways with the phone.</summary>
    public bool IsNote => Kind is ContentKind.Note;

    /// <summary>
    /// True between this desktop changing a live note and the replica taking the
    /// change: the note waits to be pushed. A flag on the item, as
    /// <see cref="ReplicaAckPending"/> is, rather than a watermark over
    /// <see cref="EditedAt"/>, because a note's stamp may come from the phone's
    /// clock, and one clock running ahead would hide the other's edits from a
    /// watermark. A note taken in from the phone never sets it, so the desktop
    /// does not echo the phone's own copy back.
    /// </summary>
    public bool NotePushPending { get; private set; }

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

    /// <summary>
    /// Follows a repository that was renamed on GitHub: an assignment to
    /// <paramref name="oldId"/> becomes one to <paramref name="newId"/>. Answers
    /// whether anything moved, so a caller can count and skip the write when
    /// nothing did.
    /// <para>
    /// The assignment only. <see cref="Routing"/> is the record of where the
    /// item went and is never edited — see <see cref="RoutingTarget"/>. Ids match
    /// the way the registry matches them, without regard to case, and the list is
    /// de-duplicated afterwards as <see cref="SetRepoIds"/> would.
    /// </para>
    /// </summary>
    public bool RenameRepository(string oldId, string newId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(oldId);
        ArgumentException.ThrowIfNullOrWhiteSpace(newId);

        if (!_repoIds.Any(id => string.Equals(id, oldId, StringComparison.OrdinalIgnoreCase))) return false;

        SetRepoIds(_repoIds
            .Select(id => string.Equals(id, oldId, StringComparison.OrdinalIgnoreCase) ? newId : id)
            .ToList());
        return true;
    }

    /// <summary>Files the item in a list, or back in the unfiled inbox. Allowed
    /// in every state, archived included: filing is not triage, and a person
    /// tidying an archive is not reopening anything.</summary>
    public void MoveToList(Guid? listId)
    {
        ListId = listId;
        Touch();
    }

    // --- Suggestions --------------------------------------------------------

    /// <summary>Records that the reader turned a suggestion down, and answers
    /// whether it was new. Kept by key, not by the suggestion's wording, so the
    /// same tag suggested again for another reason is still the one refused.
    /// Allowed in every state: a refusal is the reader's, whatever became of the
    /// item since.</summary>
    public bool DismissSuggestion(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var normalized = NormalizeSuggestionKey(key);
        if (_dismissedSuggestions.Contains(normalized, StringComparer.Ordinal)) return false;

        _dismissedSuggestions.Add(normalized);
        Touch();
        return true;
    }

    /// <summary>Whether the reader turned down the suggestion with this key.</summary>
    public bool IsSuggestionDismissed(string key) =>
        !string.IsNullOrWhiteSpace(key) && _dismissedSuggestions.Contains(NormalizeSuggestionKey(key), StringComparer.Ordinal);

    /// <summary>Restores the refusals a persisted item was written with. No rule
    /// and no stamp, for the reason <see cref="LoadTags"/> gives.</summary>
    public void LoadDismissedSuggestions(IEnumerable<string> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);

        _dismissedSuggestions.Clear();
        _dismissedSuggestions.AddRange(keys
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Select(NormalizeSuggestionKey)
            .Distinct(StringComparer.Ordinal));
    }

    private static string NormalizeSuggestionKey(string key) => key.Trim().ToLowerInvariant();

    // --- Attachments --------------------------------------------------------

    /// <summary>
    /// Records the files a capture names, and answers how many were new.
    /// <para>
    /// By id, and only ever added. A file already on the item is left exactly as
    /// it is — what the capture said about it, and whether this machine has it —
    /// so a replayed page, or the same capture offered twice, records nothing
    /// twice and cannot reset a file that was downloaded back to waiting. The
    /// capture's metadata is what it <em>was</em>, like <see cref="CapturedAt"/>,
    /// and nothing edits it afterwards. Allowed in every state: a file belongs to
    /// the thought however it has since been decided.
    /// </para>
    /// </summary>
    public int RecordAttachments(IEnumerable<InboxAttachment> named)
    {
        ArgumentNullException.ThrowIfNull(named);

        var added = 0;

        foreach (var attachment in named)
        {
            ArgumentNullException.ThrowIfNull(attachment);
            if (_attachments.Any(existing => existing.Id == attachment.Id)) continue;

            _attachments.Add(attachment);
            added++;
        }

        if (added > 0) Touch();

        return added;
    }

    /// <summary>Records that the file is on this machine at
    /// <paramref name="localPath"/>, clearing any earlier failure.</summary>
    public void MarkAttachmentDownloaded(Guid attachmentId, string localPath, DateTimeOffset at)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(localPath);

        Replace(attachmentId, attachment => attachment.Downloaded(localPath.Trim(), at));
        Touch(at);
    }

    /// <summary>Records why the file could not be fetched. The file is then not
    /// on this machine — a failure and a local copy never stand together — and
    /// the item is otherwise untouched: a file that did not arrive does not make
    /// the thought any less captured.</summary>
    public void MarkAttachmentFailed(Guid attachmentId, string error, DateTimeOffset at)
    {
        var reason = string.IsNullOrWhiteSpace(error) ? "The file could not be downloaded." : error.Trim();

        Replace(attachmentId, attachment => attachment.Failed(reason));
        Touch(at);
    }

    /// <summary>
    /// The bare file name <paramref name="attachmentId"/> is kept under in the
    /// item's folder: the capture's name with what no file system accepts taken
    /// out, and — when another file on the item would take the same name — the
    /// first eight characters of its id before the extension. The first file
    /// recorded under a name keeps it, so the answer is the same on every call
    /// and a retry writes where the first attempt did.
    /// </summary>
    public string AttachmentFileName(Guid attachmentId)
    {
        var index = _attachments.FindIndex(attachment => attachment.Id == attachmentId);
        if (index < 0) throw new ArgumentException("The attachment is not on this item.", nameof(attachmentId));

        var name = SafeFileName(_attachments[index].Name, attachmentId);
        var takenEarlier = _attachments
            .Take(index)
            .Any(earlier => string.Equals(SafeFileName(earlier.Name, earlier.Id), name, StringComparison.OrdinalIgnoreCase));

        if (!takenEarlier) return name;

        var dot = name.LastIndexOf('.');
        var suffix = "-" + attachmentId.ToString("N")[..8];
        return dot > 0 ? name[..dot] + suffix + name[dot..] : name + suffix;
    }

    /// <summary>Restores the files a persisted item was written with, as they
    /// are. No rule and no stamp, for the reason <see cref="LoadTags"/> gives.</summary>
    public void LoadAttachments(IEnumerable<InboxAttachment> attachments)
    {
        ArgumentNullException.ThrowIfNull(attachments);

        _attachments.Clear();
        _attachments.AddRange(attachments);
    }

    private void Replace(Guid attachmentId, Func<InboxAttachment, InboxAttachment> change)
    {
        var index = _attachments.FindIndex(attachment => attachment.Id == attachmentId);
        if (index < 0) throw new ArgumentException("The attachment is not on this item.", nameof(attachmentId));

        _attachments[index] = change(_attachments[index]);
    }

    /// <summary>The characters Windows refuses in a file name, which is the
    /// strictest of the file systems a workspace lands on; the same list on every
    /// platform, so one item's folder reads the same wherever it is synced.</summary>
    private static readonly char[] UnsafeFileNameChars = ['<', '>', ':', '"', '/', '\\', '|', '?', '*'];

    private static string SafeFileName(string name, Guid id)
    {
        var cleaned = new string([.. name.Select(c => char.IsControl(c) || UnsafeFileNameChars.Contains(c) ? '_' : c)])
            .Trim()
            .TrimEnd('.');

        // A name that is nothing but dots or spaces is no name at all.
        return cleaned.Trim('.', ' ', '_').Length == 0 ? id.ToString("N") : cleaned;
    }

    // --- Lifecycle ----------------------------------------------------------

    /// <summary>Puts the item aside until <paramref name="until"/>, or until a
    /// person brings it back when there is no date. From unprocessed,
    /// triaged-but-not-routed, or deferred — deferring a deferred item is how its
    /// review date changes. A routed item has left, and an archived one is closed.</summary>
    public void Defer(DateOnly? until, DateTimeOffset now)
    {
        if (IsRouted || Status is not (InboxStatus.Unprocessed or InboxStatus.Triaged or InboxStatus.Deferred))
            throw new InvalidInboxTransitionException(Status, "deferred");

        Status = InboxStatus.Deferred;
        DeferredUntil = until;
        Touch(now);
    }

    /// <summary>Whether the item is deferred with a review date that
    /// <paramref name="today"/> has reached. An undated deferral is never due.</summary>
    public bool IsDue(DateOnly today) =>
        Status is InboxStatus.Deferred && DeferredUntil is { } until && until <= today;

    /// <summary>Brings the item back into the queue when its review date has been
    /// reached, and answers whether it did. The rule the resurface sweep applies
    /// to every item; anything not due is left exactly as it was.</summary>
    public bool ResurfaceIfDue(DateOnly today, DateTimeOffset now)
    {
        if (!IsDue(today)) return false;

        Resurface(now);
        return true;
    }

    /// <summary>Brings a deferred item back into the queue, whatever its date:
    /// the sweep through <see cref="ResurfaceIfDue"/>, a person through
    /// "Return to inbox".</summary>
    public void Resurface(DateTimeOffset now)
    {
        if (Status is not InboxStatus.Deferred)
            throw new InvalidInboxTransitionException(Status, "resurfaced");

        Status = InboxStatus.Unprocessed;
        DeferredUntil = null;
        Touch(now);
    }

    /// <summary>
    /// Dismisses the item. From any state but the two terminal ones — routed and
    /// archived. Marks the replica acknowledgement pending when the item came
    /// from one, because the phone is still offering it.
    /// <para>
    /// With <paramref name="duplicateOf"/> it is dismissed as the same capture as
    /// that item, and remembers which (<see cref="DuplicateOf"/>). Two rules on
    /// top of archiving's: an item is never a duplicate of itself, and only an
    /// open item — unprocessed or deferred — is archived as one, because that is
    /// a triage decision and an item already triaged has had its decision. The
    /// item named may be in any state; whether it still exists is the handler's
    /// to check, since the aggregate cannot see another item.
    /// </para>
    /// </summary>
    public void Archive(DateTimeOffset now, Guid? duplicateOf = null)
    {
        if (IsRouted || Status is InboxStatus.Archived)
            throw new InvalidInboxTransitionException(Status, "archived");

        if (duplicateOf is { } original)
        {
            if (original == Id) throw new ArgumentException("An item cannot be a duplicate of itself.", nameof(duplicateOf));
            if (!IsOpen) throw new InvalidInboxTransitionException(Status, "archived as a duplicate");

            DuplicateOf = original;
        }

        Status = InboxStatus.Archived;
        DeferredUntil = null;

        // A note owes the phone its tombstone wherever it was made: every note
        // reaches the phone, so archiving any of them is what takes it away.
        if (ReplicaBacked || IsNote) ReplicaAckPending = true;

        if (IsNote)
        {
            // The tombstone replaces whatever live copy went out; the archived
            // note is no longer pushed live.
            EditedAt = NextEdit(now);
            NotePushPending = false;
        }
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

        // A note is never acknowledged away (.devbook/domain/inbox/domain.md#note):
        // routed, it stays on the phone and keeps syncing. Only archiving or
        // deleting it ends that.
        if (ReplicaBacked && !IsNote) ReplicaAckPending = true;
        Touch(now);
    }

    /// <summary>
    /// Records that the item is already a task the backlog has — "Link to
    /// task…" — rather than making it one. The same routing a route records,
    /// naming that one task, so the item leaves the queue as decided and says
    /// where it went; nothing is created, and the task is not touched, because
    /// its source is fixed when it is made.
    /// <para>
    /// Only from an open item, as archiving as a duplicate is: linking is a
    /// triage decision. <paramref name="repoIds"/> are the task's repositories,
    /// which is where the work the item became actually lives.
    /// </para>
    /// </summary>
    public void LinkToTask(Guid taskId, IReadOnlyList<string> repoIds, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(repoIds);

        if (taskId == Guid.Empty) throw new ArgumentException("A task id is required.", nameof(taskId));
        if (IsRouted || !IsOpen) throw new InvalidInboxTransitionException(Status, "linked to a task");

        RouteToBacklog([taskId], repoIds, now);
    }

    // --- Notes ----------------------------------------------------------------

    /// <summary>
    /// Changes a note's title and body: the reader editing it on the desktop.
    /// Only a <see cref="ContentKind.Note"/> changes after it is captured
    /// (<c>.devbook/domain/inbox/domain.md#content-kind</c>), and only while it
    /// still syncs: an archived note has left the phone and is not edited back
    /// onto it. A routed note may be edited, because routing does not end its
    /// syncing. Stamps <see cref="EditedAt"/>, so the edit is pushed to the phone.
    /// </summary>
    public void EditNote(string title, string? bodyMd, DateTimeOffset now)
    {
        if (!IsNote) throw new InvalidInboxTransitionException(Status, "edited as a note");
        if (Status is InboxStatus.Archived) throw new InvalidInboxTransitionException(Status, "edited");
        if (string.IsNullOrWhiteSpace(title)) throw new ArgumentException("Title is required.", nameof(title));

        Title = Collapse(title);
        BodyMd = bodyMd?.Trim() ?? string.Empty;
        EditedAt = NextEdit(now);
        NotePushPending = true;
        Touch(now);
    }

    /// <summary>
    /// Takes the phone's copy of a note, pulled from the replica, when it is the
    /// later one: its stamp after <see cref="EditedAt"/>. Answers whether it
    /// changed anything. The stamp is the note's, not this machine's clock, so
    /// the copy is pushed back under the same stamp and the replica keeps the
    /// one it has. Last write wins by the document's stamp
    /// (<c>.devbook/arc42/adr/0005-azure-hosted-task-replica-for-multi-device-sync.md</c>).
    /// </summary>
    public bool ApplyNote(string title, string? bodyMd, DateTimeOffset editedAt)
    {
        if (!IsNote) throw new InvalidInboxTransitionException(Status, "edited as a note");
        if (string.IsNullOrWhiteSpace(title)) throw new ArgumentException("Title is required.", nameof(title));
        if (editedAt <= EditedAt) return false;

        Title = Collapse(title);
        BodyMd = bodyMd?.Trim() ?? string.Empty;
        EditedAt = editedAt;
        Touch();
        return true;
    }

    /// <summary>
    /// The phone edited a note this desktop has already archived. The archive
    /// stands, because a note stops syncing when the desktop archives it, so its
    /// tombstone is owed again, stamped now and so later than the phone's edit.
    /// Otherwise the phone would keep showing the copy it edited.
    /// </summary>
    public void RestateArchive(DateTimeOffset now)
    {
        if (!IsNote || Status is not InboxStatus.Archived) return;

        ReplicaAckPending = true;
        EditedAt = NextEdit(now);
        Touch(now);
    }

    /// <summary>The replica took the note as it was at <paramref name="pushedAt"/>.
    /// Cleared only when nothing changed it since, so an edit made while the push
    /// was in flight still goes out on the next one.</summary>
    public void MarkNotePushed(DateTimeOffset pushedAt)
    {
        if (EditedAt != pushedAt) return;

        NotePushPending = false;
        Touch();
    }

    /// <summary>Stamps a note that arrived from the phone with the phone's own
    /// stamp rather than the moment it landed here, so the copy is pushed back
    /// under the stamp the replica already holds. Called once, right after
    /// <see cref="FromCapture"/>.</summary>
    public void StampEdited(DateTimeOffset editedAt) => EditedAt = editedAt;

    /// <summary>The outbox drained: the replica has the tombstone.</summary>
    public void MarkReplicaAcknowledged()
    {
        ReplicaAckPending = false;
        Touch();
    }

    /// <summary>
    /// Removes the item for good, from any state. Not Archive: nothing is kept,
    /// so there is no terminal state to protect, and a routed item's entries
    /// live in Tasks whatever happens to the item that produced them.
    /// <para>
    /// An item that came from the replica still owes it an acknowledgement
    /// when the phone may be offering it — while it was open, or when an
    /// earlier decision's tombstone has not left yet. The store keeps that
    /// acknowledgement, and nothing else of the item, until the outbox drains it.
    /// </para>
    /// </summary>
    public void Delete(DateTimeOffset now)
    {
        if (Deleted) throw new InvalidInboxTransitionException(Status, "deleted again");

        if (ReplicaBacked && IsOpen) ReplicaAckPending = true;

        // A note's tombstone is owed from any state: a routed note is still on
        // the phone, and a second tombstone for one archived earlier is harmless.
        if (IsNote)
        {
            ReplicaAckPending = true;
            EditedAt = NextEdit(now);
            NotePushPending = false;
        }

        Deleted = true;
        Touch(now);
    }

    /// <summary>True once <see cref="Delete"/> has run. Never stored: a
    /// deleted item has no row to store it in.</summary>
    public bool Deleted { get; private set; }

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
        DateTimeOffset updatedAt,
        Guid? duplicateOf = null,
        DateTimeOffset? editedAt = null,
        bool notePushPending = false)
    {
        Status = status;
        DuplicateOf = duplicateOf;
        DeferredUntil = deferredUntil;
        Routing = routing;
        ReplicaAckPending = replicaAckPending;
        UpdatedAt = updatedAt;

        // A row written before the stamp existed has none; the item's own stamp
        // is the nearest thing to when its text last changed.
        EditedAt = editedAt ?? updatedAt;
        NotePushPending = notePushPending;
    }

    // --- Internals ----------------------------------------------------------

    /// <summary>
    /// The stamp a change made here carries: now, but never at or before the copy
    /// it replaces. The copy may carry the phone's stamp, and a desktop clock
    /// behind the phone's would otherwise stamp the edit, or the archive's
    /// tombstone, older than what the replica holds, and the replica would refuse it.
    /// </summary>
    private DateTimeOffset NextEdit(DateTimeOffset now) => now > EditedAt ? now : EditedAt.AddTicks(1);

    /// <summary>A title is one line: runs of whitespace collapse to a space.</summary>
    private static string Collapse(string title) =>
        string.Join(' ', title.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    /// <summary>The mutators that carry no instant of their own stamp the wall
    /// clock, as <c>TaskItem.Touch</c> does; the lifecycle steps take the
    /// handler's clock so a test can place them.</summary>
    private void Touch() => UpdatedAt = DateTimeOffset.UtcNow;

    private void Touch(DateTimeOffset now) => UpdatedAt = now;
}
