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
/// Drives the quick-edit backlog list. An entry is a single block of plain
/// markdown text — there is no title/type/priority/status/tags form control
/// anywhere. Focus one entry and you get the raw text; leave it and you get the
/// rendered document, so the markdown is always right there but never in the
/// way.
/// <para>
/// Text saves on a debounce while typing and flushes the moment focus leaves,
/// per <c>.design/interaction-guidelines.md#auto-save-no-save-buttons</c>.
/// Entries can be re-ranked by dragging either grip, or from the keyboard with
/// the arrow keys while a grip is focused — every drag has a keyboard
/// equivalent.
/// </para>
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

    /// <summary>The one row currently showing its raw markdown. Everything else
    /// shows the rendered document.</summary>
    public EntryRow? EditingRow { get; private set; }

    /// <summary>The row being dragged, if any — drives the drop indicators.</summary>
    public EntryRow? DraggedRow { get; private set; }

    /// <summary>Set when a newly opened editor still needs the caret placed in
    /// it; the component consumes this after its next render.</summary>
    public bool FocusPending { get; set; }

    public string SaveStateLabel => SaveState switch
    {
        AppSaveState.Saving => "Saving…",
        AppSaveState.Error => "Couldn't save",
        _ => "Saved"
    };

    /// <summary>Placeholder text shown (via the native textarea placeholder,
    /// never as literal boilerplate to delete) teaching the plain-text format
    /// for a brand-new, empty entry.</summary>
    public const string NewEntryPlaceholder =
        "# Title\n`task` `medium` `draft`\n\nWrite the details here… use #tags,\n- [ ] a checklist,\n\n## or a heading for a sub-item.";

    public async Task InitializeAsync()
    {
        await ReloadRowsAsync();
    }

    public void SetStatusFilter(string? wire)
    {
        SelectedStatusFilterWire = wire ?? string.Empty;
        ApplyFilter();
    }

    /// <summary>Inserts a new, unsaved draft row at the top of the list and
    /// opens it for editing. It is only persisted once a title line is typed
    /// (the domain requires a title), so free typing before that is held
    /// locally.</summary>
    public void NewRow()
    {
        var row = new EntryRow();
        Rows.Insert(0, row);
        BeginEdit(row);
        ApplyFilter();
    }

    // --- Editing ---------------------------------------------------------

    /// <summary>Swaps a row from its rendered form to raw markdown.</summary>
    public void BeginEdit(EntryRow row)
    {
        if (ReferenceEquals(EditingRow, row)) return;
        EditingRow = row;
        FocusPending = true;
    }

    /// <summary>Called on every keystroke; schedules a debounced parse+save.</summary>
    public void OnRawTextInput(EntryRow row, string value)
    {
        row.RawText = value;
        ScheduleDebouncedSave(row);
    }

    /// <summary>Leaves the editor: flushes any pending debounce immediately and
    /// returns the row to its rendered form. This is what makes tabbing out of
    /// an entry save it.</summary>
    public async Task EndEditAsync(EntryRow row)
    {
        CancelDebounce(row);

        if (ReferenceEquals(EditingRow, row))
        {
            EditingRow = null;
        }

        // An entry someone opened and left untouched was never an entry. Drop
        // it rather than leaving an "Untitled" husk in the list.
        if (!row.IsPersisted && row.IsEmpty)
        {
            Rows.Remove(row);
            ApplyFilter();
            Changed?.Invoke();
            return;
        }

        await SaveRowAsync(row, isFlush: true);
        Changed?.Invoke();
    }

    /// <summary>Explicit, deliberate destructive action — distinct from the
    /// forbidden "Save" gesture, so it stays as a confirmed button.</summary>
    public async Task DeleteRowAsync(EntryRow row)
    {
        CancelDebounce(row);

        if (ReferenceEquals(EditingRow, row)) EditingRow = null;

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
        await NormalizeOrderAsync();
        ApplyFilter();
    }

    // --- Reordering ------------------------------------------------------

    public void BeginDrag(EntryRow row) => DraggedRow = row;

    public void EndDrag() => DraggedRow = null;

    /// <summary>Drops the dragged row immediately before or after
    /// <paramref name="target"/>.</summary>
    public async Task DropAsync(EntryRow target, bool before)
    {
        var dragged = DraggedRow;
        DraggedRow = null;

        if (dragged is null || ReferenceEquals(dragged, target)) return;

        Rows.Remove(dragged);
        var index = Rows.IndexOf(target);
        if (index < 0) index = Rows.Count;
        Rows.Insert(before ? index : index + 1, dragged);

        await NormalizeOrderAsync();
        ApplyFilter();
        Changed?.Invoke();
    }

    /// <summary>Keyboard equivalent of a drag: moves a row one slot up or down
    /// among the rows currently visible under the active filter.</summary>
    public async Task MoveAsync(EntryRow row, int delta)
    {
        var visible = FilteredRows;
        var from = visible.IndexOf(row);
        var to = from + delta;
        if (from < 0 || to < 0 || to >= visible.Count) return;

        var anchor = visible[to];
        Rows.Remove(row);
        var anchorIndex = Rows.IndexOf(anchor);
        Rows.Insert(delta < 0 ? anchorIndex : anchorIndex + 1, row);

        await NormalizeOrderAsync();
        ApplyFilter();
        Changed?.Invoke();
    }

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
        await SaveRowAsync(row, isFlush: false);
        Changed?.Invoke();
    }

    private void CancelDebounce(EntryRow row)
    {
        if (_debounceTimers.Remove(row.Key, out var timer))
        {
            timer.Dispose();
        }
    }

    /// <summary>
    /// Parses and persists a row. While typing (<paramref name="isFlush"/>
    /// false) only the first entry's worth of text is applied and the raw text
    /// is left exactly as typed. On flush, text that grew a second top-level
    /// heading is split off into its own entries — doing that only on flush
    /// keeps the list from rearranging itself under the caret mid-sentence.
    /// </summary>
    private async Task SaveRowAsync(EntryRow row, bool isFlush)
    {
        var segments = EntryTextParser.SplitSegments(row.RawText);
        List<string> overflow = segments.Count > 1 ? [.. segments.Skip(1)] : [];

        if (isFlush && overflow.Count > 0)
        {
            row.RawText = segments[0];
        }

        await ApplySegmentAsync(row, segments.Count > 0 ? segments[0] : row.RawText, rewriteText: isFlush);

        if (!isFlush || overflow.Count == 0) return;

        var insertAt = Rows.IndexOf(row) + 1;
        foreach (var segment in overflow)
        {
            var spawned = new EntryRow { RawText = segment };
            Rows.Insert(insertAt++, spawned);
            await ApplySegmentAsync(spawned, segment, rewriteText: true);
        }

        await NormalizeOrderAsync();
        ApplyFilter();
    }

    private async Task ApplySegmentAsync(EntryRow row, string text, bool rewriteText)
    {
        var parsed = EntryTextParser.Parse(text);

        if (row.Id is not { } id)
        {
            if (string.IsNullOrWhiteSpace(parsed.Title))
            {
                // Nothing to persist yet — the domain requires a title. Keep
                // holding the typed text locally until one appears.
                return;
            }

            // The domain always creates new entries at Draft — apply the typed
            // status as an initial transition (ignored gracefully if it isn't a
            // legal move straight out of Draft).
            var entry = new BacklogEntry(parsed.Title, parsed.Body, parsed.Type ?? EntryType.Task, parsed.Priority ?? Priority.Medium, tags: parsed.Tags);
            if (parsed.Status is { } initialStatus && entry.CanChangeStatusTo(initialStatus))
            {
                entry.ChangeStatus(initialStatus);
            }

            entry.SetOrder(Math.Max(Rows.IndexOf(row), 0));
            EntryTextParser.SyncSubItems(entry, parsed.SubItems);

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
            RefreshRowFromEntry(row, entry, rewriteText);
            SetSaveState(AppSaveState.Saved);
            FlashSaved(row);
            return;
        }

        if (!_entries.TryGetValue(id, out var existing))
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(parsed.Title))
        {
            existing.Rename(parsed.Title);
        }

        existing.UpdateContent(parsed.Body);
        existing.ChangeType(parsed.Type ?? existing.Type);
        existing.ChangePriority(parsed.Priority ?? existing.Priority);
        existing.SetTags(parsed.Tags);

        if (parsed.Status is { } targetStatus && existing.CanChangeStatusTo(targetStatus))
        {
            existing.ChangeStatus(targetStatus);
        }

        EntryTextParser.SyncSubItems(existing, parsed.SubItems);

        SetSaveState(AppSaveState.Saving);
        try
        {
            await _repository.SaveAsync(existing);
        }
        catch
        {
            SetSaveState(AppSaveState.Error);
            return;
        }

        RefreshRowFromEntry(row, existing, rewriteText);
        SetSaveState(AppSaveState.Saved);
        FlashSaved(row);
    }

    /// <summary>Writes each row's list position back as its rank, saving only
    /// the entries whose rank actually moved.</summary>
    private async Task NormalizeOrderAsync()
    {
        for (var index = 0; index < Rows.Count; index++)
        {
            if (Rows[index].Id is not { } id) continue;
            if (!_entries.TryGetValue(id, out var entry)) continue;
            if (entry.Order == index) continue;

            entry.SetOrder(index);
            try
            {
                await _repository.SaveAsync(entry);
            }
            catch
            {
                SetSaveState(AppSaveState.Error);
                return;
            }
        }
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

    private static void RefreshRowFromEntry(EntryRow row, BacklogEntry entry, bool rewriteText)
    {
        row.Type = entry.Type;
        row.Priority = entry.Priority;
        row.Status = entry.Status;
        row.Tags = entry.Tags;
        row.SubItemCount = entry.TotalSubItemCount;
        row.CompletedSubItemCount = entry.CompletedSubItemCount;

        // Re-derive the canonical text from the just-saved entry so the editor
        // reflects any graceful corrections (e.g. an unknown status token that
        // was ignored). Only ever on flush — rewriting text under a live caret
        // would fight the person typing.
        if (rewriteText)
        {
            row.RawText = EntryTextParser.ToRawText(entry);
        }
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

            var row = new EntryRow { Id = entry.Id };
            RefreshRowFromEntry(row, entry, rewriteText: true);
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
}

public sealed record StatusFilterOption(string Label, string Wire);

/// <summary>A row in the quick-edit list. <see cref="Key"/> is a stable
/// client-side identity used for <c>@key</c> and debounce tracking, independent
/// of <see cref="Id"/> which is null until the row is first saved.
/// <see cref="RawText"/> is the single source of truth the user types into —
/// there is no separate title/type/status/tags field anywhere, and the rendered
/// form shown when the row is not focused is derived from it.</summary>
public sealed class EntryRow
{
    private string? _renderedFrom;
    private IReadOnlyList<MdBlock> _blocks = [];
    private EntryTextParser.ParsedEntry? _parsed;

    public Guid Key { get; } = Guid.NewGuid();

    public Guid? Id { get; set; }

    public string RawText { get; set; } = string.Empty;

    public EntryType Type { get; set; } = EntryType.Task;

    public Priority Priority { get; set; } = Priority.Medium;

    public EntryStatus Status { get; set; } = EntryStatus.Draft;

    public IReadOnlyList<string> Tags { get; set; } = [];

    public int SubItemCount { get; set; }

    public int CompletedSubItemCount { get; set; }

    public bool IsPersisted => Id.HasValue;

    /// <summary>True briefly after a successful save, driving the inline
    /// saved-confirmation flash on the whole entry.</summary>
    public bool JustSaved { get; set; }

    public bool IsEmpty => string.IsNullOrWhiteSpace(RawText);

    public string ProgressText => SubItemCount == 0 ? string.Empty : $"{CompletedSubItemCount}/{SubItemCount}";

    /// <summary>The rendered body shown when this row is not being edited.</summary>
    public IReadOnlyList<MdBlock> PreviewBlocks
    {
        get { Render(); return _blocks; }
    }

    public string PreviewTitle
    {
        get { Render(); return _parsed!.Title; }
    }

    // Badge values for the read view. These prefer what is currently typed over
    // what was last saved, so the badges never lag behind the text.

    public EntryType PreviewType
    {
        get { Render(); return _parsed!.Type ?? Type; }
    }

    public Priority PreviewPriority
    {
        get { Render(); return _parsed!.Priority ?? Priority; }
    }

    public EntryStatus PreviewStatus
    {
        get { Render(); return _parsed!.Status ?? Status; }
    }

    public IReadOnlyList<string> PreviewTags
    {
        get { Render(); return _parsed!.Tags.Count > 0 ? _parsed.Tags : Tags; }
    }

    private void Render()
    {
        if (_renderedFrom is not null && string.Equals(_renderedFrom, RawText, StringComparison.Ordinal)) return;

        _renderedFrom = RawText;
        _parsed = EntryTextParser.Parse(RawText);
        _blocks = MarkdownPreview.Parse(_parsed.Body);
    }
}
