using Backlog.Domain;
using Backlog.Storage;

namespace Backlog.UI.Services;

/// <summary>
/// Global, always-visible persistence state for the quick-edit list, per the
/// save-state indicator vocabulary in
/// <c>.design/interaction-guidelines.md#save-state-indicator-vocabulary</c>.
/// Offline/Conflict states are out of scope here because this desktop slice
/// talks to a single local file store with no sync layer yet.
/// </summary>
public enum AppSaveState
{
    Saved,
    Saving,
    Error
}

/// <summary>
/// Drives the quick-edit backlog list: every entry is an inline-editable row
/// in the master list (title, type, priority, status, tags, sub-items) so all
/// currently supported fields can be edited without leaving the list. There is
/// no Save or Apply button anywhere — text edits auto-save on a debounce and
/// flush on blur; discrete changes (dropdowns, checkboxes, reorder) save
/// immediately. See <c>.design/interaction-guidelines.md#auto-save-no-save-buttons</c>.
/// </summary>
public sealed class BacklogDesktopState : IDisposable
{
    private const int DebounceMilliseconds = 750;

    private readonly IBacklogRepository _repository;
    private readonly Dictionary<Guid, BacklogEntry> _entries = new();
    private readonly Dictionary<Guid, Timer> _debounceTimers = new();

    public BacklogDesktopState(IBacklogRepository repository)
    {
        _repository = repository;
    }

    /// <summary>Raised whenever rows or save state change from a background
    /// callback (a debounce timer) so the component can re-render.</summary>
    public event Action? Changed;

    public IReadOnlyList<EntryType> Types { get; } = Enum.GetValues<EntryType>();

    public IReadOnlyList<Priority> Priorities { get; } = Enum.GetValues<Priority>();

    public List<StatusFilterOption> StatusFilters { get; } =
    [
        new("All", string.Empty),
        new("Draft", "draft"),
        new("Ready", "ready"),
        new("In progress", "in_progress"),
        new("Done", "done"),
        new("Archived", "archived")
    ];

    public List<EntryRow> Rows { get; private set; } = [];

    public List<EntryRow> FilteredRows { get; private set; } = [];

    public string SelectedStatusFilterWire { get; private set; } = string.Empty;

    public AppSaveState SaveState { get; private set; } = AppSaveState.Saved;

    public string SaveStateLabel => SaveState switch
    {
        AppSaveState.Saving => "Saving…",
        AppSaveState.Error => "Couldn't save",
        _ => "Saved"
    };

    public async Task InitializeAsync()
    {
        await ReloadRowsAsync();
    }

    public void SetStatusFilter(string? wire)
    {
        SelectedStatusFilterWire = wire ?? string.Empty;
        ApplyFilter();
    }

    /// <summary>Inserts a new, unsaved draft row at the top of the list. It is
    /// only persisted once the title becomes non-empty (the domain requires a
    /// title), so quick discrete edits made before that point are held locally.</summary>
    public void NewRow()
    {
        var row = new EntryRow
        {
            IsExpanded = true,
            Status = EntryStatus.Draft
        };
        Rows.Insert(0, row);
        ApplyFilter();
    }

    public void ToggleExpanded(EntryRow row) => row.IsExpanded = !row.IsExpanded;

    // --- Debounced text fields (title / content / tags) --------------------

    public void OnTitleInput(EntryRow row, string value)
    {
        row.Title = value;
        ScheduleDebouncedSave(row);
    }

    public void OnContentInput(EntryRow row, string value)
    {
        row.ContentMd = value;
        ScheduleDebouncedSave(row);
    }

    public void OnTagsInput(EntryRow row, string value)
    {
        row.TagsText = value;
        ScheduleDebouncedSave(row);
    }

    /// <summary>Flushes any pending debounce immediately on blur, per the
    /// idle/blur-flush rule.</summary>
    public async Task FlushAsync(EntryRow row)
    {
        CancelDebounce(row);
        await SaveRowAsync(row);
    }

    // --- Discrete fields (save immediately, no debounce) --------------------

    public async Task OnTypeChangedAsync(EntryRow row, EntryType type)
    {
        row.Type = type;
        CancelDebounce(row);
        await SaveRowAsync(row);
    }

    public async Task OnPriorityChangedAsync(EntryRow row, Priority priority)
    {
        row.Priority = priority;
        CancelDebounce(row);
        await SaveRowAsync(row);
    }

    public async Task OnStatusTargetChangedAsync(EntryRow row, EntryStatus? target)
    {
        if (row.Id is not { } id || target is not { } value || !_entries.TryGetValue(id, out var entry))
        {
            return;
        }

        if (!entry.CanChangeStatusTo(value))
        {
            return;
        }

        entry.ChangeStatus(value);
        await PersistExistingAsync(row, entry);
    }

    public async Task AddSubItemAsync(EntryRow row)
    {
        if (row.Id is not { } id || string.IsNullOrWhiteSpace(row.NewSubItemTitle) || !_entries.TryGetValue(id, out var entry))
        {
            return;
        }

        entry.AddSubItem(row.NewSubItemTitle.Trim());
        row.NewSubItemTitle = string.Empty;
        await PersistExistingAsync(row, entry);
    }

    public async Task ToggleSubItemAsync(EntryRow row, Guid subItemId)
    {
        if (row.Id is not { } id || !_entries.TryGetValue(id, out var entry))
        {
            return;
        }

        entry.ToggleSubItem(subItemId);
        await PersistExistingAsync(row, entry);
    }

    public async Task RemoveSubItemAsync(EntryRow row, Guid subItemId)
    {
        if (row.Id is not { } id || !_entries.TryGetValue(id, out var entry))
        {
            return;
        }

        entry.RemoveSubItem(subItemId);
        await PersistExistingAsync(row, entry);
    }

    public async Task MoveSubItemUpAsync(EntryRow row, Guid subItemId)
    {
        if (row.Id is not { } id || !_entries.TryGetValue(id, out var entry))
        {
            return;
        }

        var item = entry.SubItems.FirstOrDefault(x => x.Id == subItemId);
        if (item is null || item.Order <= 0) return;

        entry.ReorderSubItem(subItemId, item.Order - 1);
        await PersistExistingAsync(row, entry);
    }

    public async Task MoveSubItemDownAsync(EntryRow row, Guid subItemId)
    {
        if (row.Id is not { } id || !_entries.TryGetValue(id, out var entry))
        {
            return;
        }

        var item = entry.SubItems.FirstOrDefault(x => x.Id == subItemId);
        if (item is null || item.Order >= entry.TotalSubItemCount - 1) return;

        entry.ReorderSubItem(subItemId, item.Order + 1);
        await PersistExistingAsync(row, entry);
    }

    /// <summary>Explicit, deliberate destructive action — distinct from the
    /// forbidden "Save" gesture, so it stays as a confirmed button.</summary>
    public async Task DeleteRowAsync(EntryRow row)
    {
        CancelDebounce(row);

        if (row.Id is { } id)
        {
            SetSaveState(AppSaveState.Saving);
            try
            {
                await _repository.DeleteAsync(id);
                _entries.Remove(id);
                SetSaveState(AppSaveState.Saved);
            }
            catch
            {
                SetSaveState(AppSaveState.Error);
                return;
            }
        }

        Rows.Remove(row);
        ApplyFilter();
    }

    public string FormatStatus(EntryStatus status) => status switch
    {
        EntryStatus.Draft => "Draft",
        EntryStatus.Ready => "Ready",
        EntryStatus.InProgress => "In progress",
        EntryStatus.Done => "Done",
        EntryStatus.Archived => "Archived",
        _ => status.ToString()
    };

    public string FormatType(EntryType type) => type switch
    {
        EntryType.Prompt => "Prompt",
        EntryType.Task => "Task",
        EntryType.Idea => "Idea",
        EntryType.FollowUp => "Follow-up",
        _ => type.ToString()
    };

    public string FormatPriority(Priority priority) => priority.ToString();

    public void Dispose()
    {
        foreach (var timer in _debounceTimers.Values)
        {
            timer.Dispose();
        }

        _debounceTimers.Clear();
    }

    // --- Internals ------------------------------------------------------

    private void ScheduleDebouncedSave(EntryRow row)
    {
        CancelDebounce(row);

        var timer = new Timer(_ => OnDebounceElapsed(row), null, DebounceMilliseconds, Timeout.Infinite);
        _debounceTimers[row.Key] = timer;
    }

    private async void OnDebounceElapsed(EntryRow row)
    {
        _debounceTimers.Remove(row.Key);
        await SaveRowAsync(row);
        Changed?.Invoke();
    }

    private void CancelDebounce(EntryRow row)
    {
        if (_debounceTimers.Remove(row.Key, out var timer))
        {
            timer.Dispose();
        }
    }

    private async Task SaveRowAsync(EntryRow row)
    {
        var tags = ParseTags(row.TagsText);

        if (row.Id is not { } id)
        {
            if (string.IsNullOrWhiteSpace(row.Title))
            {
                // Nothing to persist yet — the domain requires a title.
                return;
            }

            var entry = new BacklogEntry(row.Title.Trim(), row.ContentMd, row.Type, row.Priority, tags: tags);
            SetSaveState(AppSaveState.Saving);
            try
            {
                await _repository.SaveAsync(entry);
            }
            catch
            {
                SetSaveState(AppSaveState.Error);
                return;
            }

            _entries[entry.Id] = entry;
            row.Id = entry.Id;
            RefreshRowFromEntry(row, entry);
            SetSaveState(AppSaveState.Saved);
            FlashSaved(row);
            return;
        }

        if (!_entries.TryGetValue(id, out var existing))
        {
            return;
        }

        existing.Rename(string.IsNullOrWhiteSpace(row.Title) ? existing.Title : row.Title.Trim());
        existing.UpdateContent(row.ContentMd);
        existing.ChangeType(row.Type);
        existing.ChangePriority(row.Priority);
        existing.SetTags(tags);

        await PersistExistingAsync(row, existing, flash: true);
    }

    private async Task PersistExistingAsync(EntryRow row, BacklogEntry entry, bool flash = false)
    {
        SetSaveState(AppSaveState.Saving);
        try
        {
            await _repository.SaveAsync(entry);
        }
        catch
        {
            SetSaveState(AppSaveState.Error);
            return;
        }

        RefreshRowFromEntry(row, entry);
        SetSaveState(AppSaveState.Saved);
        if (flash) FlashSaved(row);
    }

    private async void FlashSaved(EntryRow row)
    {
        row.JustSaved = true;
        Changed?.Invoke();
        await Task.Delay(900);
        row.JustSaved = false;
        Changed?.Invoke();
    }

    private void SetSaveState(AppSaveState state)
    {
        SaveState = state;
    }

    private static void RefreshRowFromEntry(EntryRow row, BacklogEntry entry)
    {
        row.Status = entry.Status;
        row.AvailableStatusTargets =
        [
            .. Enum.GetValues<EntryStatus>().Where(target => entry.CanChangeStatusTo(target))
        ];
        row.SubItems =
        [
            .. entry.SubItems
                .OrderBy(x => x.Order)
                .Select(x => new SubItemRow(x.Id, x.Title, x.Status == SubItemStatus.Done, x.Order))
        ];
    }

    private async Task ReloadRowsAsync()
    {
        var summaries = await _repository.ListAsync();
        var rows = new List<EntryRow>();

        foreach (var summary in summaries)
        {
            var entry = await _repository.GetAsync(summary.Id);
            if (entry is null) continue;

            _entries[entry.Id] = entry;

            var row = new EntryRow
            {
                Id = entry.Id,
                Title = entry.Title,
                ContentMd = entry.ContentMd,
                TagsText = string.Join(", ", entry.Tags),
                Type = entry.Type,
                Priority = entry.Priority,
                Status = entry.Status
            };
            RefreshRowFromEntry(row, entry);
            rows.Add(row);
        }

        Rows = rows;
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        if (string.IsNullOrWhiteSpace(SelectedStatusFilterWire))
        {
            FilteredRows = [.. Rows];
            return;
        }

        FilteredRows = [.. Rows.Where(x => StatusWire(x.Status) == SelectedStatusFilterWire)];
    }

    private static string StatusWire(EntryStatus status) => status switch
    {
        EntryStatus.Draft => "draft",
        EntryStatus.Ready => "ready",
        EntryStatus.InProgress => "in_progress",
        EntryStatus.Done => "done",
        EntryStatus.Archived => "archived",
        _ => status.ToString().ToLowerInvariant()
    };

    private static IReadOnlyList<string> ParseTags(string text)
    {
        return text
            .Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
    }
}

public sealed record StatusFilterOption(string Label, string Wire);

public sealed record SubItemRow(Guid Id, string Title, bool IsDone, int Order);

/// <summary>An inline-editable row in the quick-edit list. <see cref="Key"/> is
/// a stable client-side identity used for <c>@key</c> and debounce tracking,
/// independent of <see cref="Id"/> which is null until the row is first saved.</summary>
public sealed class EntryRow
{
    public Guid Key { get; } = Guid.NewGuid();

    public Guid? Id { get; set; }

    public string Title { get; set; } = string.Empty;

    public string ContentMd { get; set; } = string.Empty;

    public string TagsText { get; set; } = string.Empty;

    public EntryType Type { get; set; } = EntryType.Task;

    public Priority Priority { get; set; } = Priority.Medium;

    public EntryStatus Status { get; set; } = EntryStatus.Draft;

    public List<EntryStatus> AvailableStatusTargets { get; set; } = [];

    public List<SubItemRow> SubItems { get; set; } = [];

    public string NewSubItemTitle { get; set; } = string.Empty;

    public bool IsExpanded { get; set; }

    public bool IsPersisted => Id.HasValue;

    /// <summary>True briefly after a successful save, driving the inline
    /// "ease-bounce" saved-flash per the Inline Editing pattern.</summary>
    public bool JustSaved { get; set; }

    public int CompletedSubItemCount => SubItems.Count(x => x.IsDone);

    public string ProgressText => $"{CompletedSubItemCount}/{SubItems.Count}";
}
