using Backlog.Modules.Backlog.Abstractions;

namespace Backlog.Modules.Backlog.DomainModels;

/// <summary>
/// Aggregate root of the Backlog Management bounded context. A refined,
/// actionable item and the single consistency boundary for its sub-items,
/// projections, and usage history. All mutations to owned collections and the
/// lifecycle status go through this root.
/// </summary>
public sealed class TaskItem
{
    // Allowed lifecycle transitions per .domain/backlog/flow.md.
    private static readonly Dictionary<EntryStatus, EntryStatus[]> AllowedTransitions = new()
    {
        [EntryStatus.Draft] = new[] { EntryStatus.Ready },
        [EntryStatus.Ready] = new[] { EntryStatus.InProgress, EntryStatus.Draft },
        [EntryStatus.InProgress] = new[] { EntryStatus.Done, EntryStatus.Ready },
        [EntryStatus.Done] = new[] { EntryStatus.Archived, EntryStatus.InProgress },
        [EntryStatus.Archived] = new[] { EntryStatus.Draft },
    };

    private readonly List<SubItem> _subItems = new();
    private readonly List<UsageEvent> _usageEvents = new();
    private readonly List<ProjectionRef> _projectionRefs = new();
    private readonly List<string> _repoIds = new();
    private readonly List<string> _tags = new();
    private readonly List<string> _dependsOn = new();

    /// <summary>Creates a new, manually authored entry. It starts at
    /// <see cref="EntryStatus.Draft"/> with no source inbox id.</summary>
    public TaskItem(
        string title,
        string contentMd,
        EntryType type,
        Priority priority = Priority.Medium,
        IEnumerable<string>? repoIds = null,
        IEnumerable<string>? tags = null)
        : this(
            Guid.NewGuid(),
            title,
            contentMd,
            type,
            EntryStatus.Draft,
            priority,
            repoIds,
            tags,
            sourceInboxId: null,
            createdAt: DateTimeOffset.UtcNow)
    {
    }

    // Full constructor, also used by storage to rehydrate a persisted entry.
    public TaskItem(
        Guid id,
        string title,
        string contentMd,
        EntryType type,
        EntryStatus status,
        Priority priority,
        IEnumerable<string>? repoIds,
        IEnumerable<string>? tags,
        string? sourceInboxId,
        DateTimeOffset createdAt,
        Guid? recurrenceSourceId = null)
    {
        if (string.IsNullOrWhiteSpace(title))
            throw new ArgumentException("Title is required.", nameof(title));

        Id = id;
        Title = title;
        ContentMd = contentMd ?? string.Empty;
        Type = type;
        Status = status;
        Priority = priority;
        SourceInboxId = sourceInboxId;
        CreatedAt = createdAt;
        RecurrenceSourceId = recurrenceSourceId;

        if (repoIds is not null) _repoIds.AddRange(repoIds);
        if (tags is not null) _tags.AddRange(tags);
    }

    public Guid Id { get; }

    public string Title { get; private set; }

    public string ContentMd { get; private set; }

    public EntryType Type { get; private set; }

    public EntryStatus Status { get; private set; }

    public Priority Priority { get; private set; }

    public string? SourceInboxId { get; }

    /// <summary>The entry this one was spawned from as the next occurrence of a
    /// repeat, or null when it was not. Provenance in the same spirit as
    /// <see cref="SourceInboxId"/> and carrying no invariant: the entry it names
    /// is a separate aggregate that may since have been archived or deleted, so
    /// this is set once at birth and never edited afterwards.</summary>
    public Guid? RecurrenceSourceId { get; }

    public DateTimeOffset CreatedAt { get; }

    /// <summary>Manual rank within the backlog. Lower sorts first. Entries that
    /// have never been ranked share the default 0 and fall back to recency.</summary>
    public int Order { get; private set; }

    /// <summary>Free-form grouping the entry belongs to — "repos", "projects",
    /// "inbox", or whatever vocabulary the person actually uses. Deliberately a
    /// string rather than an enum: the taxonomy is theirs, not ours. Null means
    /// unfiled.</summary>
    public string? Area { get; private set; }

    /// <summary>The rough size of the entry in story points, or null when nobody
    /// has estimated it yet. Null and zero are different answers on purpose: zero
    /// is "this is trivial", a genuine estimate, while null is "no one has said" —
    /// so the field is nullable rather than defaulting to a size the reader never
    /// chose. Deliberately a bare count with no unit: the number means whatever the
    /// team's points mean, and the model has no opinion on the scale beyond
    /// refusing one that cannot exist. Often filled in later by an agent, so it has
    /// to be as writable from a parsed token as from a person's keystroke.</summary>
    public int? Effort { get; private set; }

    /// <summary>The calendar day the entry is committed to, or null when it is not
    /// committed to one. A date rather than an instant: "due Friday" is a promise
    /// about a day, and an instant would move the deadline whenever the device
    /// changed timezone.</summary>
    public DateOnly? DueOn { get; private set; }

    /// <summary>When the person asked to be reminded, held as wall-clock intent —
    /// <see cref="DateTimeKind.Unspecified"/> on purpose, so 09:00 means 09:00
    /// wherever they are when it arrives. Deliberately not a
    /// <see cref="DateTimeOffset"/>: an offset would pin the reminder to the zone
    /// it was written in, which is the opposite of what was asked for. Recording
    /// the request is all the aggregate does; delivering it is somebody
    /// else's job.</summary>
    public DateTime? RemindAt { get; private set; }

    /// <summary>The shape of the repeat, or null when the entry happens once. The
    /// date of the next occurrence is never stored — it is calculated from
    /// <see cref="DueOn"/> when an occurrence completes.</summary>
    public Recurrence? Recurrence { get; private set; }

    /// <summary>The date this entry was picked for My Day, or null when it was
    /// not picked. A date rather than a flag, so membership is a comparison
    /// against the reader's current local date and yesterday's list expires by
    /// arithmetic rather than by an overnight sweep.</summary>
    public DateOnly? InMyDayOn { get; private set; }

    /// <summary>Which reading of the body the person last asked for, or null when
    /// they have never said. Held on the aggregate and not in a view-model because
    /// the entry's markdown is canonical: the preference is written on the metadata
    /// line, so it has to survive the round trip through here or the next save
    /// deletes it. It carries no invariant and nothing in the lifecycle reads it —
    /// see <see cref="EntryView"/> for why a presentation preference lives in the
    /// text at all.</summary>
    public EntryView? View { get; private set; }

    /// <summary>The one place attached to this task, or null when nothing is.
    /// One and not a list, and a path rather than a copy — see
    /// <see cref="Attachment"/>, which holds both of those decisions and the
    /// reasons for them.</summary>
    public Attachment? Attachment { get; private set; }

    public IReadOnlyList<string> RepoIds => _repoIds;

    public IReadOnlyList<string> Tags => _tags;

    /// <summary>The entries this one waits on, named by id. Plain strings for the
    /// same reason <see cref="RepoIds"/> are: every entry is its own aggregate
    /// root, so a dependency is a weak reference across a boundary rather than an
    /// object graph — and an id that resolves to nothing still blocks, because
    /// dropping it would let a chain claim to be ready when the step it waits on
    /// is merely out of view.</summary>
    public IReadOnlyList<string> DependsOn => _dependsOn;

    public IReadOnlyList<SubItem> SubItems => _subItems;

    public IReadOnlyList<UsageEvent> UsageEvents => _usageEvents;

    public IReadOnlyList<ProjectionRef> ProjectionRefs => _projectionRefs;

    // --- Scalar edits -------------------------------------------------------

    public void Rename(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
            throw new ArgumentException("Title is required.", nameof(title));
        Title = title;
    }

    public void UpdateContent(string contentMd) => ContentMd = contentMd ?? string.Empty;

    public void ChangeType(EntryType type) => Type = type;

    public void ChangePriority(Priority priority) => Priority = priority;

    /// <summary>Sets the manual rank used to order the backlog by hand.</summary>
    public void SetOrder(int order) => Order = order;

    /// <summary>Files the entry under an area, or clears it. Blank is stored as
    /// null so "unfiled" has exactly one representation.</summary>
    public void SetArea(string? area) =>
        Area = string.IsNullOrWhiteSpace(area) ? null : area.Trim();

    /// <summary>Estimates the entry at a number of story points, or clears the
    /// estimate with null. A negative size is refused rather than clamped: unlike
    /// <see cref="SetArea"/>, which normalises a blank to null because "unfiled" is
    /// a legal place, a negative count names no legal size the caller could have
    /// meant, so silently rounding it to zero would invent an estimate. Any
    /// non-negative number is accepted — the Fibonacci presets the UI offers are a
    /// convenience, not the rule, because a value written by hand or by an agent
    /// still has to load.</summary>
    public void SetEffort(int? effort)
    {
        if (effort is < 0)
            throw new ArgumentOutOfRangeException(nameof(effort), effort, "Effort cannot be negative.");
        Effort = effort;
    }

    public void SetRepoIds(IEnumerable<string> repoIds)
    {
        _repoIds.Clear();
        if (repoIds is not null) _repoIds.AddRange(repoIds);
    }

    /// <summary>The shared <c>#tag</c> the plan this entry was imported from was
    /// filed under, or null when the entry was not imported (or was imported from
    /// a plan with no shared tag). Provenance in the same spirit as
    /// <see cref="SourceInboxId"/>, but mutable rather than constructor-only: an
    /// entry re-saved through the ordinary text-save path has to keep carrying it,
    /// the same way <see cref="Area"/> and <see cref="Effort"/> survive an
    /// unrelated edit.</summary>
    public string? ImportPlanId { get; private set; }

    /// <summary>The <c>id:</c> token the imported plan gave this entry, or null
    /// when the entry carries none. Together with <see cref="ImportPlanId"/> this
    /// is what a later re-import matches an entry against.</summary>
    public string? ImportItemId { get; private set; }

    /// <summary>Files the entry under a plan, or clears it. Blank is stored as
    /// null, the same normalization <see cref="SetArea"/> applies.</summary>
    public void SetImportPlanId(string? importPlanId) =>
        ImportPlanId = string.IsNullOrWhiteSpace(importPlanId) ? null : importPlanId.Trim();

    /// <summary>Records the plan's local id for this entry, or clears it.</summary>
    public void SetImportItemId(string? importItemId) =>
        ImportItemId = string.IsNullOrWhiteSpace(importItemId) ? null : importItemId.Trim();

    public void SetTags(IEnumerable<string> tags)
    {
        _tags.Clear();
        if (tags is not null) _tags.AddRange(tags);
    }

    // --- Scheduling ---------------------------------------------------------
    //
    // None of these is load-bearing for the lifecycle, and all of them are
    // clearable: an entry losing its due date is an ordinary edit rather than an
    // exception, so null is a value here and not a missing argument.

    public void SetDueOn(DateOnly? dueOn) => DueOn = dueOn;

    /// <summary>Records the reminder as wall-clock intent. A value that arrives
    /// tagged Utc or Local is stripped back to Unspecified rather than converted:
    /// the clock reading is what was asked for, and honouring the tag would
    /// silently move the reminder.</summary>
    public void SetReminder(DateTime? remindAt) =>
        RemindAt = remindAt is { } value ? DateTime.SpecifyKind(value, DateTimeKind.Unspecified) : null;

    public void SetRecurrence(Recurrence? recurrence) => Recurrence = recurrence;

    public void SetInMyDayOn(DateOnly? inMyDayOn) => InMyDayOn = inMyDayOn;

    /// <summary>
    /// Attaches a place, or detaches whatever was attached.
    /// <para>
    /// One setter and not an <c>Attach</c>/<c>Detach</c> pair, because there is
    /// only ever one attachment: attaching a second place is replacing the first,
    /// so a method named <c>Attach</c> would be a method whose name promised
    /// something the model cannot do. Null detaches, the same way null clears
    /// every other optional field here.
    /// </para>
    /// <para>
    /// Nothing is checked. Whether the path resolves is the file system's answer
    /// and it is a different answer on a different machine — a task written on the
    /// desktop and read on the phone would fail a check here that has nothing to
    /// do with whether the task is valid.
    /// </para>
    /// </summary>
    public void SetAttachment(Attachment? attachment) => Attachment = attachment;

    /// <summary>Records which reading of the body was asked for, or clears it back
    /// to "never said". Grouped with the scheduling setters because it behaves like
    /// them — absent by default, clearable, and load-bearing for nothing — not
    /// because it is one of them.</summary>
    public void SetView(EntryView? view) => View = view;

    /// <summary>Replaces the whole dependency list. Ids are stored as written —
    /// trimmed of surrounding space and of blanks, but never validated against
    /// anything, because an id naming no visible entry is precisely the case that
    /// must keep blocking.</summary>
    public void SetDependsOn(IEnumerable<string>? dependsOn)
    {
        _dependsOn.Clear();
        if (dependsOn is null) return;

        _dependsOn.AddRange(dependsOn
            .Select(id => (id ?? string.Empty).Trim())
            .Where(id => id.Length > 0)
            .Distinct(StringComparer.Ordinal));
    }

    // --- Lifecycle ----------------------------------------------------------

    /// <summary>Returns true if an entry at <paramref name="from"/> may move
    /// directly to <paramref name="to"/>.</summary>
    public static bool IsTransitionAllowed(EntryStatus from, EntryStatus to) =>
        from == to || (AllowedTransitions.TryGetValue(from, out var allowed) && Array.IndexOf(allowed, to) >= 0);

    /// <summary>The statuses this entry may move to right now, excluding its
    /// current one. Callers use this to explain a refusal rather than just
    /// swallow it.</summary>
    public static IReadOnlyList<EntryStatus> NextStatusesFrom(EntryStatus from) =>
        AllowedTransitions.TryGetValue(from, out var allowed) ? allowed : [];

    /// <summary>Returns true if the entry may currently transition to <paramref name="target"/>.</summary>
    public bool CanChangeStatusTo(EntryStatus target) => IsTransitionAllowed(Status, target);

    /// <summary>Sets the current status from canonical metadata without walking the lifecycle graph.</summary>
    public void SetStatus(EntryStatus target) => Status = target;

    /// <summary>Moves the entry to <paramref name="target"/> if the transition is
    /// permitted by the lifecycle; throws otherwise.</summary>
    public void ChangeStatus(EntryStatus target)
    {
        if (target == Status) return;
        if (!CanChangeStatusTo(target))
            throw new InvalidStatusTransitionException(Status, target);
        Status = target;
    }

    // --- Sub-items ----------------------------------------------------------

    public SubItem AddSubItem(string title, string? notes = null)
    {
        var subItem = new SubItem(Guid.NewGuid(), title, _subItems.Count, notes);
        _subItems.Add(subItem);
        return subItem;
    }

    public void RemoveSubItem(Guid subItemId)
    {
        var subItem = FindSubItem(subItemId);
        _subItems.Remove(subItem);
        Reindex();
    }

    public void ToggleSubItem(Guid subItemId)
    {
        var subItem = FindSubItem(subItemId);
        subItem.Status = subItem.Status == SubItemStatus.Done ? SubItemStatus.Pending : SubItemStatus.Done;
    }

    public void SetSubItemStatus(Guid subItemId, SubItemStatus status) => FindSubItem(subItemId).Status = status;

    public void UpdateSubItem(Guid subItemId, string title, string? notes)
    {
        if (string.IsNullOrWhiteSpace(title))
            throw new ArgumentException("Sub-item title is required.", nameof(title));
        var subItem = FindSubItem(subItemId);
        subItem.Title = title;
        subItem.Notes = notes;
    }

    /// <summary>Moves a sub-item to a new zero-based position, reindexing the rest.</summary>
    public void ReorderSubItem(Guid subItemId, int newIndex)
    {
        var subItem = FindSubItem(subItemId);
        if (newIndex < 0 || newIndex >= _subItems.Count)
            throw new ArgumentOutOfRangeException(nameof(newIndex));

        _subItems.Remove(subItem);
        _subItems.Insert(newIndex, subItem);
        Reindex();
    }

    /// <summary>Number of completed sub-items.</summary>
    public int CompletedSubItemCount => _subItems.Count(s => s.Status == SubItemStatus.Done);

    /// <summary>Total number of sub-items.</summary>
    public int TotalSubItemCount => _subItems.Count;

    /// <summary>Fraction of sub-items completed (0..1). Zero when there are none.</summary>
    public double Progress => _subItems.Count == 0 ? 0d : (double)CompletedSubItemCount / _subItems.Count;

    // --- Usage & projections ------------------------------------------------

    public UsageEvent RecordUsage(string action)
    {
        var usageEvent = new UsageEvent(DateTimeOffset.UtcNow, action);
        _usageEvents.Add(usageEvent);
        return usageEvent;
    }

    public void AddProjectionRef(ProjectionRef projectionRef)
    {
        ArgumentNullException.ThrowIfNull(projectionRef);
        _projectionRefs.Add(projectionRef);
    }

    // Rehydration helpers for storage: populate owned collections without
    // re-running command-side logic.
    public void LoadSubItem(SubItem subItem) => _subItems.Add(subItem);

    public SubItem CreateSubItemForLoad(Guid id, string title, SubItemStatus status, string? notes, int order)
        => new(id, title, order, notes, status);

    public void LoadUsageEvent(UsageEvent usageEvent) => _usageEvents.Add(usageEvent);

    // --- Internals ----------------------------------------------------------

    private SubItem FindSubItem(Guid subItemId) =>
        _subItems.FirstOrDefault(s => s.Id == subItemId)
        ?? throw new InvalidOperationException($"Sub-item {subItemId} not found.");

    private void Reindex()
    {
        for (var i = 0; i < _subItems.Count; i++)
            _subItems[i].Order = i;
    }
}
