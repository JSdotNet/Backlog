namespace Backlog.Domain;

/// <summary>
/// Aggregate root of the Backlog Management bounded context. A refined,
/// actionable item and the single consistency boundary for its sub-items,
/// projections, and usage history. All mutations to owned collections and the
/// lifecycle status go through this root.
/// </summary>
public sealed class BacklogEntry
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

    /// <summary>Creates a new, manually authored entry. It starts at
    /// <see cref="EntryStatus.Draft"/> with no source inbox id.</summary>
    public BacklogEntry(
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
    public BacklogEntry(
        Guid id,
        string title,
        string contentMd,
        EntryType type,
        EntryStatus status,
        Priority priority,
        IEnumerable<string>? repoIds,
        IEnumerable<string>? tags,
        string? sourceInboxId,
        DateTimeOffset createdAt)
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

    public DateTimeOffset CreatedAt { get; }

    /// <summary>Manual rank within the backlog. Lower sorts first. Entries that
    /// have never been ranked share the default 0 and fall back to recency.</summary>
    public int Order { get; private set; }

    public IReadOnlyList<string> RepoIds => _repoIds;

    public IReadOnlyList<string> Tags => _tags;

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

    public void SetRepoIds(IEnumerable<string> repoIds)
    {
        _repoIds.Clear();
        if (repoIds is not null) _repoIds.AddRange(repoIds);
    }

    public void SetTags(IEnumerable<string> tags)
    {
        _tags.Clear();
        if (tags is not null) _tags.AddRange(tags);
    }

    // --- Lifecycle ----------------------------------------------------------

    /// <summary>Returns true if the entry may currently transition to <paramref name="target"/>.</summary>
    public bool CanChangeStatusTo(EntryStatus target) =>
        AllowedTransitions.TryGetValue(Status, out var allowed) && Array.IndexOf(allowed, target) >= 0;

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
