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

    private readonly BacklogStore _store;
    private readonly Dictionary<Guid, BacklogEntry> _entries = new();
    private readonly Dictionary<Guid, Timer> _debounceTimers = new();

    public BacklogDesktopState(BacklogStore store)
    {
        _store = store;
        _store.RootChanged += OnRootChanged;
    }

    private IBacklogRepository Repository => _store.Repository;

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

    /// <summary>The selected area, or empty for all. <see cref="UnfiledArea"/>
    /// selects the entries that have no area at all.</summary>
    public string SelectedArea { get; private set; } = string.Empty;

    /// <summary>Sentinel for "entries with no area" — a real area can never be
    /// this because the parser lower-cases and would never produce a leading
    /// space.</summary>
    public const string UnfiledArea = " unfiled";

    /// <summary>The areas actually in use, in alphabetical order. There is no
    /// fixed taxonomy: an area exists because somebody typed it.</summary>
    public List<AreaFilterOption> AreaFilters { get; private set; } = [];

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
        "Title\n`task` `*medium` `!draft`\n\nWrite the details here… use #tags,\n- [ ] a checklist,\n\n## or a heading for a sub-item.";

    /// <summary>Ensures the first line of an entry is a top-level heading. The
    /// first thing typed is always the title, so it is written as one rather
    /// than leaving someone to remember the <c>#</c> — and doing it as text
    /// keeps the markdown honest instead of hiding a title field behind it.
    /// Text inside a leading fence is left alone.</summary>
    public static string EnsureTitleHeading(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return raw;

        var normalized = raw.Replace("\r\n", "\n").Replace('\r', '\n');
        var lines = normalized.Split('\n');

        var first = -1;
        for (var i = 0; i < lines.Length; i++)
        {
            if (lines[i].Trim().Length == 0) continue;
            first = i;
            break;
        }

        if (first < 0) return raw;

        var line = lines[first];
        var trimmed = line.TrimStart();

        if (trimmed.StartsWith('#') || trimmed.StartsWith("```", StringComparison.Ordinal)) return raw;

        lines[first] = "# " + trimmed;
        return string.Join('\n', lines);
    }

    public async Task InitializeAsync()
    {
        await ReloadRowsAsync();
    }

    public void SetStatusFilter(string? wire)
    {
        SelectedStatusFilterWire = wire ?? string.Empty;
        ApplyFilter();
    }

    public void SetAreaFilter(string? area)
    {
        SelectedArea = area ?? string.Empty;
        ApplyFilter();
    }

    /// <summary>Appends a new, unsaved draft row at the end of the list and
    /// opens it for editing. It is only persisted once a title line is typed
    /// (the domain requires a title), so free typing before that is held
    /// locally. When an area is being filtered, the new entry starts already
    /// filed there — otherwise it would vanish the moment it saved.</summary>
    public void NewRow()
    {
        var row = new EntryRow();

        if (SelectedArea.Length > 0 && SelectedArea != UnfiledArea)
        {
            row.RawText = $"# \n`task` `*medium` `!draft` `@{SelectedArea}`\n";
            row.SeedText = row.RawText;
        }

        Rows.Add(row);
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
        if (!row.IsPersisted && row.IsUntouched)
        {
            Rows.Remove(row);
            ApplyFilter();
            Changed?.Invoke();
            return;
        }

        // Whatever went on line one is the title, so it is written as a heading
        // now rather than silently treated as one. Done on flush, not on every
        // keystroke, so the caret never jumps mid-word.
        row.RawText = EnsureTitleHeading(row.RawText);

        await SaveRowAsync(row, isFlush: true);

        // What was just typed can change which area an entry belongs to, so the
        // area chips are rebuilt against the text as it now stands.
        ApplyFilter();
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
                await Repository.DeleteAsync(id);
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

    // --- Sub-items -------------------------------------------------------

    /// <summary>The entry whose sub-item is being dragged, if any. Sub-items only
    /// ever re-rank within their own entry — a sub-item belongs to the thing it
    /// is written under, so dropping one on a different entry would mean moving
    /// text between two documents, which is a rewrite, not a re-rank.</summary>
    public EntryRow? SubItemDragRow { get; private set; }

    /// <summary>Index of the sub-item being dragged within
    /// <see cref="SubItemDragRow"/>, or -1.</summary>
    public int SubItemDragIndex { get; private set; } = -1;

    public bool IsDraggingSubItem(EntryRow row, int index) =>
        ReferenceEquals(SubItemDragRow, row) && SubItemDragIndex == index;

    /// <summary>Folds an entry's sub-items away, or brings them back. The count
    /// stays visible either way, so a folded entry never hides that it has
    /// work under it.</summary>
    public void ToggleSubItems(EntryRow row)
    {
        row.SubItemsCollapsed = !row.SubItemsCollapsed;
        Changed?.Invoke();
    }

    public void BeginSubItemDrag(EntryRow row, int index)
    {
        SubItemDragRow = row;
        SubItemDragIndex = index;
    }

    public void EndSubItemDrag()
    {
        SubItemDragRow = null;
        SubItemDragIndex = -1;
    }

    /// <summary>Drops the dragged sub-item immediately before or after the
    /// sub-item at <paramref name="targetIndex"/> in the same entry.</summary>
    public async Task DropSubItemAsync(EntryRow row, int targetIndex, bool before)
    {
        var from = SubItemDragIndex;
        var source = SubItemDragRow;
        EndSubItemDrag();

        if (source is null || !ReferenceEquals(source, row) || from < 0) return;

        var to = before ? targetIndex : targetIndex + 1;
        if (to > from) to--;

        await ReorderSubItemAsync(row, from, to);
    }

    /// <summary>Keyboard equivalent of a sub-item drag. The moved card is
    /// re-focused afterwards so a run of arrow presses keeps carrying the same
    /// sub-item instead of grabbing whatever slid into the old slot.</summary>
    public async Task MoveSubItemAsync(EntryRow row, int index, int delta) =>
        await ReorderSubItemAsync(row, index, index + delta, focusAfter: true);

    /// <summary>The sub-item grip waiting to be re-focused after a keyboard
    /// move, or null. The component consumes it on its next render.</summary>
    public (EntryRow Row, int Index)? SubItemFocus { get; private set; }

    public void ConsumeSubItemFocus() => SubItemFocus = null;

    private async Task ReorderSubItemAsync(EntryRow row, int from, int to, bool focusAfter = false)
    {
        if (from == to || from < 0 || to < 0 || to >= row.PreviewSubItems.Count) return;

        var rewritten = EntryTextParser.MoveSubItem(row.RawText, from, to);
        if (string.Equals(rewritten, row.RawText, StringComparison.Ordinal)) return;

        CancelDebounce(row);
        row.RawText = rewritten;
        if (focusAfter) SubItemFocus = (row, to);

        // A re-rank is a finished gesture, not a keystroke: save it now rather
        // than on a debounce that a drag never generates.
        await SaveRowAsync(row, isFlush: true);
        ApplyFilter();
        Changed?.Invoke();
    }

    public void Dispose()
    {
        _store.RootChanged -= OnRootChanged;

        foreach (var timer in _debounceTimers.Values)
        {
            timer.Dispose();
        }

        _debounceTimers.Clear();
    }

    /// <summary>The backlog moved to a different folder, so everything held in
    /// memory is about the old one. Start over from the new store.</summary>
    private async void OnRootChanged()
    {
        EditingRow = null;
        DraggedRow = null;
        EndSubItemDrag();
        _entries.Clear();

        await ReloadRowsAsync();
        Changed?.Invoke();
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
            entry.SetArea(parsed.Area);
            EntryTextParser.SyncSubItems(entry, parsed.SubItems);

            SetSaveState(AppSaveState.Saving);
            try
            {
                await Repository.SaveAsync(entry);
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
        existing.SetArea(parsed.Area);

        if (parsed.Status is { } targetStatus && existing.CanChangeStatusTo(targetStatus))
        {
            existing.ChangeStatus(targetStatus);
        }

        EntryTextParser.SyncSubItems(existing, parsed.SubItems);

        SetSaveState(AppSaveState.Saving);
        try
        {
            await Repository.SaveAsync(existing);
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
                await Repository.SaveAsync(entry);
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
        row.Area = entry.Area;
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
        var summaries = await Repository.ListAsync();
        var rows = new List<EntryRow>();

        foreach (var summary in summaries)
        {
            var entry = await Repository.GetAsync(summary.Id);
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
        RebuildAreaFilters();

        IEnumerable<EntryRow> rows = Rows;

        if (!string.IsNullOrWhiteSpace(SelectedStatusFilterWire))
        {
            rows = rows.Where(x => StatusWire(x.PreviewStatus) == SelectedStatusFilterWire);
        }

        if (SelectedArea == UnfiledArea)
        {
            rows = rows.Where(x => string.IsNullOrEmpty(x.PreviewArea));
        }
        else if (SelectedArea.Length > 0)
        {
            rows = rows.Where(x => x.PreviewArea == SelectedArea);
        }

        // A row being written right now always stays put, even if what was just
        // typed no longer matches the filter — having an entry disappear
        // mid-sentence is never what someone meant.
        FilteredRows = [.. rows.Union(Rows.Where(r => ReferenceEquals(r, EditingRow))).OrderBy(Rows.IndexOf)];
    }

    /// <summary>Areas exist because somebody typed one, so the filter is
    /// rebuilt from what is actually in the list.</summary>
    private void RebuildAreaFilters()
    {
        var options = new List<AreaFilterOption> { new("All", string.Empty, Rows.Count) };

        var used = Rows
            .Select(r => r.PreviewArea)
            .Where(a => !string.IsNullOrEmpty(a))
            .GroupBy(a => a!, StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal);

        foreach (var group in used)
        {
            options.Add(new AreaFilterOption(group.Key, group.Key, group.Count()));
        }

        var unfiled = Rows.Count(r => string.IsNullOrEmpty(r.PreviewArea));
        if (unfiled > 0 && options.Count > 1)
        {
            options.Add(new AreaFilterOption("Unfiled", UnfiledArea, unfiled));
        }

        AreaFilters = options;

        // An area stops existing when its last entry leaves it.
        if (SelectedArea.Length > 0 && options.All(o => o.Value != SelectedArea))
        {
            SelectedArea = string.Empty;
        }
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

/// <summary>One entry in the area filter. <see cref="Count"/> is shown so the
/// filter doubles as a sense of where the work actually is.</summary>
public sealed record AreaFilterOption(string Label, string Value, int Count);

/// <summary>One thing the app read out of an entry's meta line. <paramref
/// name="Explicit"/> distinguishes what was actually typed from what is merely
/// the current value, so the hint can show a default without claiming it was
/// asked for.</summary>
public sealed record MetaReading(string Kind, string Value, bool Explicit, string? Note = null);

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
    private IReadOnlyList<MdBlock> _bodyBlocks = [];
    private IReadOnlyList<MdSubItem> _subItems = [];
    private EntryTextParser.ParsedEntry? _parsed;

    public Guid Key { get; } = Guid.NewGuid();

    public Guid? Id { get; set; }

    public string RawText { get; set; } = string.Empty;

    public EntryType Type { get; set; } = EntryType.Task;

    public Priority Priority { get; set; } = Priority.Medium;

    public EntryStatus Status { get; set; } = EntryStatus.Draft;

    /// <summary>Free-form area the entry is filed under, or null for unfiled.</summary>
    public string? Area { get; set; }

    public IReadOnlyList<string> Tags { get; set; } = [];

    public int SubItemCount { get; set; }

    public int CompletedSubItemCount { get; set; }

    public bool IsPersisted => Id.HasValue;

    /// <summary>True briefly after a successful save, driving the inline
    /// saved-confirmation flash on the whole entry.</summary>
    public bool JustSaved { get; set; }

    public bool IsEmpty => string.IsNullOrWhiteSpace(RawText);

    /// <summary>Text this row was pre-filled with when it was created (e.g. the
    /// area it inherits from the active filter). Used to tell a genuinely
    /// untouched row from one that was actually written in.</summary>
    public string? SeedText { get; set; }

    /// <summary>True when nothing has really been written here — either the row
    /// is blank, or it still holds exactly the text it was seeded with.</summary>
    public bool IsUntouched =>
        IsEmpty || (SeedText is not null && string.Equals(SeedText, RawText, StringComparison.Ordinal));

    public string ProgressText => SubItemCount == 0 ? string.Empty : $"{CompletedSubItemCount}/{SubItemCount}";

    /// <summary>The rendered body shown when this row is not being edited. Stops
    /// at the first sub-item: sub-items are items in their own right and are laid
    /// out below the entry rather than inside its body.</summary>
    public IReadOnlyList<MdBlock> PreviewBlocks
    {
        get { Render(); return _bodyBlocks; }
    }

    /// <summary>The entry's sub-items, in the order they are written. Each is
    /// rendered as its own draggable card beneath the entry.</summary>
    public IReadOnlyList<MdSubItem> PreviewSubItems
    {
        get { Render(); return _subItems; }
    }

    /// <summary>Whether the sub-item cards under this entry are folded away. Per
    /// row and in memory only — a fold is a way of looking at the list right now,
    /// not something worth writing into someone's markdown.</summary>
    public bool SubItemsCollapsed { get; set; }

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
        get { Render(); return _parsed!.Status is { } typed && BacklogEntry.IsTransitionAllowed(Status, typed) ? typed : Status; }
    }

    /// <summary>The status that was typed but which the lifecycle will not accept
    /// from the entry's current one, or null when what was typed will stick.
    /// Surfaced in the editor hint so a refused word is never silently dropped.</summary>
    public EntryStatus? BlockedStatus
    {
        get
        {
            Render();
            return _parsed!.Status is { } typed && !BacklogEntry.IsTransitionAllowed(Status, typed) ? typed : null;
        }
    }

    public IReadOnlyList<string> PreviewTags
    {
        get { Render(); return _parsed!.Tags.Count > 0 ? _parsed.Tags : Tags; }
    }

    public string? PreviewArea
    {
        get { Render(); return _parsed!.Area ?? Area; }
    }

    /// <summary>What the app actually understood from the meta line, in plain
    /// words. Shown live under the editor so nobody has to guess which token
    /// became the status.</summary>
    public IReadOnlyList<MetaReading> MetaReadings
    {
        get
        {
            Render();

            var readings = new List<MetaReading>
            {
                new("type", EntryTextParser.TypeToken(PreviewType), _parsed!.Type is not null),
                new("priority", EntryTextParser.PriorityToken(PreviewPriority), _parsed.Priority is not null)
            };

            if (BlockedStatus is { } blocked)
            {
                var next = string.Join(" or ", BacklogEntry.NextStatusesFrom(Status).Select(EntryTextParser.StatusToken));
                readings.Add(new MetaReading(
                    "status",
                    EntryTextParser.StatusToken(Status),
                    true,
                    $"stays {EntryTextParser.StatusToken(Status)} — {EntryTextParser.StatusToken(blocked)} is not a legal next step; try {next}"));
            }
            else
            {
                readings.Add(new MetaReading("status", EntryTextParser.StatusToken(PreviewStatus), _parsed.Status is not null));
            }

            if (!string.IsNullOrEmpty(PreviewArea))
            {
                readings.Add(new MetaReading("area", PreviewArea!, _parsed.Area is not null));
            }

            foreach (var tag in PreviewTags)
            {
                readings.Add(new MetaReading("tag", tag, true));
            }

            return readings;
        }
    }

    private void Render()
    {
        if (_renderedFrom is not null && string.Equals(_renderedFrom, RawText, StringComparison.Ordinal)) return;

        _renderedFrom = RawText;
        _parsed = EntryTextParser.Parse(RawText);
        _blocks = MarkdownPreview.Parse(_parsed.Body);
        _bodyBlocks = [.. _blocks.TakeWhile(b => b is not MdSubItem)];
        _subItems = [.. _blocks.OfType<MdSubItem>()];
    }
}
