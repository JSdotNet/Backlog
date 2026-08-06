using System.Text.RegularExpressions;
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
/// Drives the quick-edit backlog list. Every entry is a single block of plain
/// markdown text — there is no separate title/type/priority/status/tags form
/// control anywhere. The block looks like:
/// <code>
/// # Title
/// `type` `priority` `status`
///
/// Free-form body, may include #tags and
/// - [ ] sub-item checklist lines
/// </code>
/// A debounced background parser (<see cref="EntryTextParser"/>) reads that
/// text and applies it to the domain entry, so typing plain markdown *is* the
/// editing gesture — there is no Save or Apply button anywhere. Text saves on
/// a debounce and flushes on blur, per
/// <c>.design/interaction-guidelines.md#auto-save-no-save-buttons</c>.
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
        "# Title\n`task` `medium` `draft`\n\nWrite the details here… use #tags and\n- [ ] sub-items.";

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
    /// only persisted once a title line is typed (the domain requires a
    /// title), so free typing before that point is held locally.</summary>
    public void NewRow()
    {
        Rows.Insert(0, new EntryRow());
        ApplyFilter();
    }

    /// <summary>Called on every keystroke; schedules a debounced parse+save.</summary>
    public void OnRawTextInput(EntryRow row, string value)
    {
        row.RawText = value;
        ScheduleDebouncedSave(row);
    }

    /// <summary>Flushes any pending debounce immediately on blur, per the
    /// idle/blur-flush rule.</summary>
    public async Task FlushAsync(EntryRow row)
    {
        CancelDebounce(row);
        await SaveRowAsync(row);
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
        var parsed = EntryTextParser.Parse(row.RawText);

        if (row.Id is not { } id)
        {
            if (string.IsNullOrWhiteSpace(parsed.Title))
            {
                // Nothing to persist yet — the domain requires a title. Keep
                // holding the typed text locally until one appears.
                return;
            }

            // The domain always creates new entries at Draft — apply the
            // typed status as an initial transition (ignored gracefully if
            // it isn't a legal move straight out of Draft).
            var entry = new BacklogEntry(parsed.Title, parsed.Body, parsed.Type ?? EntryType.Task, parsed.Priority ?? Priority.Medium, tags: parsed.Tags);
            if (parsed.Status is { } initialStatus && entry.CanChangeStatusTo(initialStatus))
            {
                entry.ChangeStatus(initialStatus);
            }

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
            RefreshRowFromEntry(row, entry);
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
        row.Type = entry.Type;
        row.Priority = entry.Priority;
        row.Status = entry.Status;
        row.Tags = entry.Tags;
        row.SubItemCount = entry.TotalSubItemCount;
        row.CompletedSubItemCount = entry.CompletedSubItemCount;
        // Re-derive the canonical text from the just-saved entry so the
        // editor reflects any graceful corrections (e.g. an unknown status
        // token that was ignored) without disturbing an in-progress edit.
        row.RawText = EntryTextParser.ToRawText(entry);
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
}

public sealed record StatusFilterOption(string Label, string Wire);

/// <summary>A row in the quick-edit list. <see cref="Key"/> is a stable
/// client-side identity used for <c>@key</c> and debounce tracking,
/// independent of <see cref="Id"/> which is null until the row is first
/// saved. <see cref="RawText"/> is the single source of truth the user types
/// into — there is no separate title/type/status/tags field anywhere.</summary>
public sealed class EntryRow
{
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

    public string ProgressText => SubItemCount == 0 ? string.Empty : $"{CompletedSubItemCount}/{SubItemCount}";
}

/// <summary>
/// Reads and writes the plain-markdown format the quick-edit list is built
/// on. This is deliberately independent from <c>Backlog.Storage.EnumMap</c>
/// (internal to that assembly) — the tokens here are the human-typed
/// vocabulary shown in the UI (e.g. <c>follow-up</c>, <c>in-progress</c>),
/// normalized the same way (case/space/hyphen/underscore-insensitive) so any
/// spelling of a known value is recognized.
/// </summary>
internal static class EntryTextParser
{
    private static readonly Regex MetaLineRegex = new(@"^(\s*`[^`\n]+`\s*)+$", RegexOptions.Compiled);
    private static readonly Regex TokenRegex = new(@"`([^`]+)`", RegexOptions.Compiled);
    private static readonly Regex TagRegex = new(@"(?<!\S)#([A-Za-z][\w-]*)", RegexOptions.Compiled);
    private static readonly Regex SubItemRegex = new(@"^[ \t]*-[ \t]+\[( |x|X)\][ \t]+(.+?)[ \t]*$", RegexOptions.Compiled | RegexOptions.Multiline);

    private static readonly Dictionary<string, EntryType> TypeTokens = new()
    {
        ["prompt"] = EntryType.Prompt,
        ["task"] = EntryType.Task,
        ["idea"] = EntryType.Idea,
        ["followup"] = EntryType.FollowUp
    };

    private static readonly Dictionary<string, Priority> PriorityTokens = new()
    {
        ["low"] = Priority.Low,
        ["medium"] = Priority.Medium,
        ["high"] = Priority.High,
        ["critical"] = Priority.Critical
    };

    private static readonly Dictionary<string, EntryStatus> StatusTokens = new()
    {
        ["draft"] = EntryStatus.Draft,
        ["ready"] = EntryStatus.Ready,
        ["inprogress"] = EntryStatus.InProgress,
        ["done"] = EntryStatus.Done,
        ["archived"] = EntryStatus.Archived
    };

    public sealed record ParsedEntry(
        string Title,
        EntryType? Type,
        Priority? Priority,
        EntryStatus? Status,
        string Body,
        IReadOnlyList<string> Tags,
        IReadOnlyList<(string Title, bool Done)> SubItems);

    /// <summary>Parses the whole raw text block. Tolerant by design: an
    /// unrecognized or missing meta token simply leaves that field
    /// unspecified (the caller keeps the previous value) rather than
    /// blocking or corrupting the rest of the edit.</summary>
    public static ParsedEntry Parse(string raw)
    {
        var lines = (raw ?? string.Empty).Replace("\r\n", "\n").Split('\n');
        var i = 0;

        while (i < lines.Length && string.IsNullOrWhiteSpace(lines[i])) i++;

        var title = string.Empty;
        if (i < lines.Length)
        {
            var line = lines[i].Trim();
            title = line.StartsWith("# ", StringComparison.Ordinal) ? line[2..].Trim() : line;
            i++;
        }

        while (i < lines.Length && string.IsNullOrWhiteSpace(lines[i])) i++;

        EntryType? type = null;
        Priority? priority = null;
        EntryStatus? status = null;

        if (i < lines.Length && MetaLineRegex.IsMatch(lines[i].Trim()))
        {
            foreach (Match match in TokenRegex.Matches(lines[i]))
            {
                var token = Normalize(match.Groups[1].Value);
                if (TypeTokens.TryGetValue(token, out var t)) type = t;
                else if (PriorityTokens.TryGetValue(token, out var p)) priority = p;
                else if (StatusTokens.TryGetValue(token, out var s)) status = s;
            }

            i++;
        }

        while (i < lines.Length && string.IsNullOrWhiteSpace(lines[i])) i++;

        var body = string.Join('\n', lines.Skip(i)).TrimEnd('\n');

        var tags = TagRegex.Matches(body)
            .Select(m => m.Groups[1].Value.ToLowerInvariant())
            .Distinct()
            .ToList();

        var subItems = SubItemRegex.Matches(body)
            .Select(m => (Title: m.Groups[2].Value.Trim(), Done: m.Groups[1].Value is "x" or "X"))
            .Where(x => x.Title.Length > 0)
            .ToList();

        return new ParsedEntry(title, type, priority, status, body, tags, subItems);
    }

    /// <summary>Syncs a parsed checklist onto the entry's structured
    /// sub-items by position — the checklist text is the single source of
    /// truth; nothing outside this entry references a sub-item's id, so
    /// re-deriving identity from position on every save is safe.</summary>
    public static void SyncSubItems(BacklogEntry entry, IReadOnlyList<(string Title, bool Done)> parsedItems)
    {
        var existing = entry.SubItems.OrderBy(s => s.Order).ToList();

        for (var idx = existing.Count - 1; idx >= parsedItems.Count; idx--)
        {
            entry.RemoveSubItem(existing[idx].Id);
        }

        existing = entry.SubItems.OrderBy(s => s.Order).ToList();

        for (var idx = 0; idx < parsedItems.Count; idx++)
        {
            var (title, done) = parsedItems[idx];
            var wantStatus = done ? SubItemStatus.Done : SubItemStatus.Pending;

            if (idx < existing.Count)
            {
                var item = existing[idx];
                if (!string.Equals(item.Title, title, StringComparison.Ordinal))
                {
                    entry.UpdateSubItem(item.Id, title, item.Notes);
                }

                if (item.Status != wantStatus)
                {
                    entry.SetSubItemStatus(item.Id, wantStatus);
                }
            }
            else
            {
                var newItem = entry.AddSubItem(title);
                if (done)
                {
                    entry.SetSubItemStatus(newItem.Id, SubItemStatus.Done);
                }
            }
        }
    }

    /// <summary>Builds the canonical raw-text form of an entry — the inverse
    /// of <see cref="Parse"/> — so the editor always reflects exactly what
    /// was saved.</summary>
    public static string ToRawText(BacklogEntry entry)
    {
        var meta = $"`{TypeToken(entry.Type)}` `{PriorityToken(entry.Priority)}` `{StatusToken(entry.Status)}`";
        var body = entry.ContentMd.TrimEnd('\n');
        return body.Length == 0
            ? $"# {entry.Title}\n{meta}\n"
            : $"# {entry.Title}\n{meta}\n\n{body}\n";
    }

    private static string TypeToken(EntryType type) => type switch
    {
        EntryType.Prompt => "prompt",
        EntryType.Task => "task",
        EntryType.Idea => "idea",
        EntryType.FollowUp => "follow-up",
        _ => type.ToString().ToLowerInvariant()
    };

    private static string PriorityToken(Priority priority) => priority.ToString().ToLowerInvariant();

    private static string StatusToken(EntryStatus status) => status switch
    {
        EntryStatus.Draft => "draft",
        EntryStatus.Ready => "ready",
        EntryStatus.InProgress => "in-progress",
        EntryStatus.Done => "done",
        EntryStatus.Archived => "archived",
        _ => status.ToString().ToLowerInvariant()
    };

    private static string Normalize(string value) =>
        value.Trim().ToLowerInvariant().Replace("_", string.Empty).Replace("-", string.Empty).Replace(" ", string.Empty);
}
