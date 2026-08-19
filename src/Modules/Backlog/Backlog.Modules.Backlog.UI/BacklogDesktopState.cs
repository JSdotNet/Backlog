using Backlog.Modules.Backlog.Abstractions;
using Backlog.Modules.Backlog.Abstractions.DataTransferObjects;
using Backlog.Modules.Backlog.Abstractions.Services;
using Backlog.Infrastructure.Copilot;
using Backlog.Infrastructure.GitHub;
using Backlog.SharedKernel.Results;

using Backlog.UI.Components.Markdown;

namespace Backlog.Desktop.UI.BacklogManagement;

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

    private readonly IBacklogStore _store;
    private readonly IBacklogEntries _entryUseCases;
    private readonly GitHubIntegration _gitHub;
    private readonly BacklogIssues _issues;
    private readonly BacklogCopilotCli _copilot;
    private readonly RepositoryBacklogSource? _repositoryBacklog;

    /// <summary>The last saved state of each persisted row, as the module
    /// describes it. Held so a badge or a GitHub link can be read without
    /// re-parsing text — never to change an entry, which only ever happens by
    /// saving its text through <see cref="IBacklogEntries"/>.</summary>
    private readonly Dictionary<Guid, BacklogEntryDto> _entries = new();
    private readonly Dictionary<Guid, Timer> _debounceTimers = new();

    /// <summary>How many sub-items <see cref="EditingRow"/> had when its editor
    /// opened, or -1 when no entry is being written in. See
    /// <see cref="BeginEdit"/>.</summary>
    private int _editingSubItemCount = -1;

    public BacklogDesktopState(
        IBacklogStore store,
        IBacklogEntries entryUseCases,
        GitHubIntegration gitHub,
        BacklogCopilotCli? copilot = null,
        RepositoryBacklogSource? repositoryBacklog = null)
    {
        _store = store;
        _entryUseCases = entryUseCases;
        _gitHub = gitHub;
        _issues = new BacklogIssues(gitHub);
        _copilot = copilot ?? BacklogCopilotCli.Unavailable;
        _repositoryBacklog = repositoryBacklog;
        _store.RootChanged += OnRootChanged;

        // Repository-authored rows come from a configured repository's .backlog
        // folder, so what invalidates them is a repository settings change. The
        // storage root moving is the other half and is already OnRootChanged,
        // which reloads everything rather than only these rows.
        if (_repositoryBacklog is not null)
        {
            _gitHub.Settings.Changed += OnRepositoryKnowledgeChanged;
        }
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

    /// <summary>The repository currently scoping repository-authored backlog and
    /// knowledge, or empty for all configured repositories.</summary>
    public string SelectedRepositoryAlias { get; private set; } = string.Empty;

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

    public IReadOnlyList<GitHubRepositoryRef> Repositories => _gitHub.Repositories;

    public AppSaveState SaveState { get; private set; } = AppSaveState.Saved;

    /// <summary>The one row currently showing its raw markdown. Everything else
    /// shows the rendered document.</summary>
    public EntryRow? EditingRow { get; private set; }

    public EditingSubItem? EditingSubItem { get; private set; }

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

    /// <summary>Loads the backlog for a freshly opened view.
    /// <para>
    /// The load is announced like any other, because the list is not the
    /// component that starts it: the shell does, and the list is a
    /// parameterless child that Blazor will not re-render just because its
    /// parent did. Reading an empty store happens to finish before the first
    /// render and would paper over that; reading a store with anything in it
    /// does not, and the list would sit on "Nothing here yet." over a backlog
    /// that was already in memory.
    /// </para></summary>
    public async Task InitializeAsync()
    {
        await ReloadRowsAsync();
        Changed?.Invoke();
    }

    public void SetStatusFilter(string? wire)
    {
        SelectedStatusFilterWire = wire ?? string.Empty;
        ApplyFilter();
    }

    public void SetRepositoryFilter(string? repositoryAlias)
    {
        var alias = repositoryAlias ?? string.Empty;
        var repository = alias.Length == 0 ? null : _gitHub.Settings.Current.Find(alias);
        if (alias.Length > 0 && repository is null)
        {
            alias = string.Empty;
        }
        else if (repository is not null)
        {
            alias = repository.Alias;
        }

        if (string.Equals(SelectedRepositoryAlias, alias, StringComparison.Ordinal)) return;

        SelectedRepositoryAlias = alias;
        ApplyFilter();
        Changed?.Invoke();
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
        // Reserved for future truly read-only sources; local and repository
        // backlog rows are both editable.
        if (row.IsReadOnly) return;
        if (ReferenceEquals(EditingRow, row)) return;
        EditingSubItem = null;
        EditingRow = row;

        // The editor shows the entry without its sub-items, so it has to be
        // agreed up front which chapters those are. Working it out afresh from
        // the text on every keystroke means a `##` heading somebody has just
        // typed counts as one, and the chapter below it is handed back a second
        // time — once per keystroke.
        _editingSubItemCount = EntryTextParser.CountSubItems(row.RawText);
        FocusPending = true;
    }

    public string EntryEditText(EntryRow row) =>
        ReferenceEquals(EditingRow, row)
            ? EntryTextParser.GetParentText(row.RawText, _editingSubItemCount)
            : row.RawText;

    /// <summary>Called on every keystroke; schedules a debounced parse+save.</summary>
    public void OnRawTextInput(EntryRow row, string value)
    {
        row.RawText = ReferenceEquals(EditingRow, row)
            ? EntryTextParser.ReplaceParentText(row.RawText, value, _editingSubItemCount)
            : value;
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
            _editingSubItemCount = -1;
        }

        // An entry someone opened and left untouched was never an entry. Drop
        // it rather than leaving an "Untitled" husk in the list.
        // Repository-origin rows are always retained — they come from a file.
        if (!row.IsPersisted && row.Origin is null && row.IsUntouched)
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

        if (row.Origin is { } origin)
            await SaveRepositoryRowAsync(row, origin);
        else
            await SaveRowAsync(row, isFlush: true);

        // What was just typed can change which area an entry belongs to, so the
        // area chips are rebuilt against the text as it now stands.
        ApplyFilter();
        Changed?.Invoke();
    }

    public bool IsEditingSubItem(EntryRow row, int subItemIndex) =>
        EditingSubItem is { } editing && ReferenceEquals(editing.Row, row) && editing.Index == subItemIndex;

    public string SubItemEditText(EntryRow row, int subItemIndex) =>
        EntryTextParser.GetSubItemText(row.RawText, subItemIndex);

    public void BeginSubItemEdit(EntryRow row, int subItemIndex)
    {
        if (row.IsReadOnly) return;
        if (subItemIndex < 0 || subItemIndex >= row.PreviewSubItems.Count) return;

        EditingRow = null;
        _editingSubItemCount = -1;
        EditingSubItem = new EditingSubItem(row, subItemIndex);
        FocusPending = true;
        Changed?.Invoke();
    }

    public void OnSubItemRawTextInput(EntryRow row, int subItemIndex, string value)
    {
        if (!IsEditingSubItem(row, subItemIndex)) return;

        row.RawText = EntryTextParser.ReplaceSubItemText(row.RawText, subItemIndex, value);
        ScheduleDebouncedSave(row);
    }

    public async Task EndSubItemEditAsync(EntryRow row, int subItemIndex)
    {
        CancelDebounce(row);

        if (IsEditingSubItem(row, subItemIndex))
        {
            EditingSubItem = null;
        }

        if (row.Origin is { } origin)
            await SaveRepositoryRowAsync(row, origin);
        else
            await SaveRowAsync(row, isFlush: true);

        ApplyFilter();
        Changed?.Invoke();
    }

    /// <summary>Explicit, deliberate destructive action — distinct from the
    /// forbidden "Save" gesture, so it stays as a confirmed button.</summary>
    public async Task DeleteRowAsync(EntryRow row)
    {
        if (row.IsReadOnly) return;

        CancelDebounce(row);

        if (ReferenceEquals(EditingRow, row))
        {
            EditingRow = null;
            _editingSubItemCount = -1;
        }

        if (EditingSubItem is { } editing && ReferenceEquals(editing.Row, row)) EditingSubItem = null;

        if (row.Id is { } id)
        {
            SetSaveState(AppSaveState.Saving);
            try
            {
                await _entryUseCases.DeleteAsync(id);
                _entries.Remove(id);
                SetSaveState(AppSaveState.Saved);
            }
            catch
            {
                SetSaveState(AppSaveState.Error);
                return;
            }

            Rows.Remove(row);
            await NormalizeOrderAsync();
            ApplyFilter();
            return;
        }

        var removedIndex = Rows.IndexOf(row);
        Rows.Remove(row);

        if (row.Origin is { } origin)
        {
            SetSaveState(AppSaveState.Saving);
            try
            {
                RepositoryBacklogWriter.DeleteSegment(origin.FilePath, origin.SegmentIndex);
                ReloadRepositoryFileRows(origin, row.PreviewArea, removedIndex);
                SetSaveState(AppSaveState.Saved);
            }
            catch
            {
                Rows.Insert(Math.Max(removedIndex, 0), row);
                SetSaveState(AppSaveState.Error);
            }

            ApplyFilter();
            Changed?.Invoke();
            return;
        }

        await NormalizeOrderAsync();
        ApplyFilter();
    }

    // --- Reordering ------------------------------------------------------

    public void BeginDrag(EntryRow row)
    {
        if (row.IsReadOnly) return;
        DraggedRow = row;
    }

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
        if (row.IsReadOnly) return;

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

    /// <summary>Folds an entry's details away, or brings them back. Metadata and
    /// title stay visible, so the compact view remains scannable.</summary>
    public void ToggleEntry(EntryRow row)
    {
        if (!row.HasExpandableContent) return;

        row.EntryCollapsed = !row.EntryCollapsed;
        Changed?.Invoke();
    }

    /// <summary>Toggles a rendered checklist item directly from read mode. This
    /// is a discrete edit, so it persists immediately rather than waiting for the
    /// text debounce.</summary>
    public async Task ToggleTaskItemAsync(EntryRow row, int taskIndex)
    {
        if (row.IsReadOnly) return;

        var rewritten = EntryTextParser.ToggleChecklistItem(row.RawText, taskIndex);
        if (string.Equals(rewritten, row.RawText, StringComparison.Ordinal)) return;

        CancelDebounce(row);
        row.RawText = rewritten;

        if (row.Origin is { } origin)
            await SaveRepositoryRowAsync(row, origin);
        else
            await SaveRowAsync(row, isFlush: true);

        ApplyFilter();
        Changed?.Invoke();
    }

    public async Task ToggleSubItemAsync(EntryRow row, int subItemIndex)
    {
        if (row.IsReadOnly) return;

        var rewritten = EntryTextParser.ToggleSubItem(row.RawText, subItemIndex);
        if (string.Equals(rewritten, row.RawText, StringComparison.Ordinal)) return;

        CancelDebounce(row);
        row.RawText = rewritten;

        if (row.Origin is { } origin)
            await SaveRepositoryRowAsync(row, origin);
        else
            await SaveRowAsync(row, isFlush: true);

        ApplyFilter();
        Changed?.Invoke();
    }

    public async Task ChangeTypeAsync(EntryRow row, EntryType type) =>
        await RewriteMetadataAsync(row, EntryTextParser.WithType(row.RawText, type));

    public async Task ChangePriorityAsync(EntryRow row, Priority priority) =>
        await RewriteMetadataAsync(row, EntryTextParser.WithPriority(row.RawText, priority));

    public async Task ChangeStatusAsync(EntryRow row, EntryStatus status) =>
        await RewriteMetadataAsync(row, EntryTextParser.WithStatus(row.RawText, status, cascadeSubItems: true), forceWhenEqual: row.Status != status);

    // No per-sub-item type, priority, status or tag edits. A sub-item carries a
    // title, a status, notes and an order and nothing else, and the four
    // rewrites that used to live here wrote tokens that EntryTextSync then
    // discarded on the next save — the UI was editing values the domain had no
    // room for. Sub-item completion is still expressible, through the checkbox
    // and ToggleSubItemAsync above.

    public async Task ChangeAreaAsync(EntryRow row, string? area) =>
        await RewriteMetadataAsync(row, EntryTextParser.WithArea(row.RawText, area));

    public async Task ChangeTagsAsync(EntryRow row, IEnumerable<string> tags) =>
        await RewriteMetadataAsync(row, EntryTextParser.WithTags(row.RawText, tags));

    public async Task ChangeTagsAsync(EntryRow row, string tags) =>
        await RewriteMetadataAsync(row, EntryTextParser.WithTags(row.RawText, tags));

    private async Task RewriteMetadataAsync(EntryRow row, string rewritten, bool forceWhenEqual = false)
    {
        if (row.IsReadOnly) return;
        if (string.Equals(rewritten, row.RawText, StringComparison.Ordinal) && !forceWhenEqual) return;

        CancelDebounce(row);
        row.RawText = rewritten;

        if (row.Origin is { } origin)
        {
            await SaveRepositoryRowAsync(row, origin);
        }
        else
        {
            await SaveRowAsync(row, isFlush: true);
        }

        ApplyFilter();
        Changed?.Invoke();
    }

    public void BeginSubItemDrag(EntryRow row, int index)
    {
        if (row.IsReadOnly) return;

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
        if (row.IsReadOnly) return;
        if (from == to || from < 0 || to < 0 || to >= row.PreviewSubItems.Count) return;

        var rewritten = EntryTextParser.MoveSubItem(row.RawText, from, to);
        if (string.Equals(rewritten, row.RawText, StringComparison.Ordinal)) return;

        CancelDebounce(row);
        row.RawText = rewritten;
        if (focusAfter) SubItemFocus = (row, to);

        // A re-rank is a finished gesture, not a keystroke: save it now rather
        // than on a debounce that a drag never generates.
        if (row.Origin is { } origin)
            await SaveRepositoryRowAsync(row, origin);
        else
            await SaveRowAsync(row, isFlush: true);

        ApplyFilter();
        Changed?.Invoke();
    }

    public void Dispose()
    {
        _store.RootChanged -= OnRootChanged;

        if (_repositoryBacklog is not null)
        {
            _gitHub.Settings.Changed -= OnRepositoryKnowledgeChanged;
        }

        foreach (var timer in _debounceTimers.Values)
        {
            timer.Dispose();
        }

        _debounceTimers.Clear();
    }

    // --- GitHub -----------------------------------------------------------

    /// <summary>True once at least one repository is configured. Until then the
    /// list shows nothing about GitHub at all.</summary>
    public bool GitHubConfigured => _gitHub.IsConfigured;

    /// <summary>The repository this row's area names, or null when the area is
    /// just a pile. What makes an entry pushable is that its <c>`@area`</c>
    /// matches a repository configured in Settings.</summary>
    public GitHubRepositoryRef? RepositoryFor(EntryRow row) => _gitHub.ResolveRepository(row.PreviewArea);

    /// <summary>True when the rows currently on screen include anything linked
    /// to GitHub, which is what makes a whole-list sync worth offering.</summary>
    public bool HasLinkedRows => Rows.Any(r => r.IssueLink is not null);

    public bool GitHubSyncing { get; private set; }

    /// <summary>Creates the GitHub issue for an entry and remembers the link on
    /// the entry itself, so the association survives a restart and travels with
    /// the markdown file.</summary>
    public async Task PushToGitHubAsync(EntryRow row, GitHubRepositoryRef? repositoryOverride = null)
    {
        if (row.GitHubBusy) return;
        if (row.Id is not { } id || !_entries.TryGetValue(id, out var entry)) return;

        var repository = repositoryOverride ?? RepositoryFor(row);
        if (repository is null) return;

        row.GitHubBusy = true;
        row.GitHubError = null;
        Changed?.Invoke();

        try
        {
            var link = await _issues.PushAsync(entry, repository);

            // GitHub made the issue; the module is what remembers it, and hands
            // back the entry with the projection already on it.
            var linked = await _entryUseCases.LinkToIssueAsync(
                id,
                link.RepoFullName,
                link.IssueNumber.ToString(),
                EntryProjectionDto.IssueTargetType);

            if (linked.TryGetValue(out var updated)) _entries[id] = updated;

            row.IssueLink = link;
            SetSaveState(AppSaveState.Saved);
        }
        catch (Exception ex) when (ex is GitHubException or GitHubNotConfiguredException)
        {
            row.GitHubError = ex.Message;
        }
        catch (Exception)
        {
            row.GitHubError = "Couldn't push to GitHub.";
        }
        finally
        {
            row.GitHubBusy = false;
            Changed?.Invoke();
        }

        // The issue exists now; showing it open straight away saves a round of
        // "did that work?".
        if (row.IssueLink is not null && row.GitHubError is null)
        {
            await RefreshGitHubAsync(row);
        }
    }

    public async Task PushSubItemToGitHubAsync(EntryRow row, int subItemIndex, GitHubRepositoryRef? repositoryOverride = null)
    {
        if (row.GitHubBusy) return;
        if (row.Id is not { } id || !_entries.TryGetValue(id, out var entry)) return;

        var repository = repositoryOverride ?? RepositoryFor(row);
        if (repository is null) return;

        var subItem = EntryTextParser.Parse(row.RawText).SubItems.ElementAtOrDefault(subItemIndex);
        if (subItem is null) return;

        row.GitHubBusy = true;
        row.GitHubError = null;
        Changed?.Invoke();

        try
        {
            await _issues.PushSubItemAsync(entry.Title, subItem, repository);
            SetSaveState(AppSaveState.Saved);
        }
        catch (Exception ex) when (ex is GitHubException or GitHubNotConfiguredException)
        {
            row.GitHubError = ex.Message;
        }
        catch (Exception)
        {
            row.GitHubError = "Couldn't push that sub-item to GitHub.";
        }
        finally
        {
            row.GitHubBusy = false;
            Changed?.Invoke();
        }
    }

    /// <summary>Re-reads one entry's issue state and the pull requests that
    /// reference it.</summary>
    public async Task RefreshGitHubAsync(EntryRow row)
    {
        if (row.IssueLink is not { } link || row.GitHubBusy) return;

        row.GitHubBusy = true;
        row.GitHubError = null;
        Changed?.Invoke();

        try
        {
            row.Snapshot = await _gitHub.RefreshAsync(link);
        }
        catch (Exception ex) when (ex is GitHubException or GitHubNotConfiguredException)
        {
            row.GitHubError = ex.Message;
        }
        catch (Exception)
        {
            row.GitHubError = "Couldn't read that issue from GitHub.";
        }
        finally
        {
            row.GitHubBusy = false;
            Changed?.Invoke();
        }
    }

    /// <summary>Refreshes every linked row. Explicit rather than automatic on
    /// load: the backlog must open instantly and offline, so nothing about it
    /// waits on the network until asked.</summary>
    public async Task SyncGitHubAsync()
    {
        if (GitHubSyncing) return;

        GitHubSyncing = true;
        Changed?.Invoke();

        try
        {
            foreach (var row in Rows.Where(r => r.IssueLink is not null).ToList())
            {
                await RefreshGitHubAsync(row);
            }
        }
        finally
        {
            GitHubSyncing = false;
            Changed?.Invoke();
        }
    }

    // --- GitHub Copilot CLI -----------------------------------------------

    /// <summary>Starts the GitHub Copilot CLI with this entry as the task brief.</summary>
    public async Task StartCopilotCliAsync(EntryRow row)
    {
        if (row.CopilotBusy) return;
        if (row.Id is not { } id || !_entries.ContainsKey(id)) return;

        row.CopilotBusy = true;
        row.CopilotError = null;
        Changed?.Invoke();

        try
        {
            await _copilot.StartFromEntryAsync(row.RawText, _store.RootDirectory);
            await _entryUseCases.RecordUsageAsync(id, BacklogCopilotCli.UsageAction);
            SetSaveState(AppSaveState.Saved);
        }
        catch (CopilotCliException ex)
        {
            row.CopilotError = ex.Message;
        }
        catch (Exception)
        {
            row.CopilotError = "Couldn't start GitHub Copilot CLI.";
        }
        finally
        {
            row.CopilotBusy = false;
            Changed?.Invoke();
        }
    }

    /// <summary>The backlog moved to a different folder, so everything held in
    /// memory is about the old one. Start over from the new store.</summary>
    private async void OnRootChanged()
    {
        EditingRow = null;
        _editingSubItemCount = -1;
        EditingSubItem = null;
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

        if (row.Origin is { } origin)
            await SaveRepositoryRowAsync(row, origin);
        else
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
    /// Writes a repository-authored row back to its source <c>.backlog</c> file,
    /// translating backlog sigils to knowledge meta vocabulary and preserving
    /// non-status meta fields. Errors are surfaced through the save-state
    /// indicator.</summary>
    private Task SaveRepositoryRowAsync(EntryRow row, RepositoryBacklogOrigin origin)
    {
        SetSaveState(AppSaveState.Saving);
        try
        {
            RepositoryBacklogWriter.SaveRowToSource(origin, row.RawText);
            SetSaveState(AppSaveState.Saved);
            FlashSaved(row);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or FileNotFoundException)
        {
            SetSaveState(AppSaveState.Error);
        }
        catch (Exception)
        {
            SetSaveState(AppSaveState.Error);
        }

        return Task.CompletedTask;
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

    /// <summary>Hands one entry's text to the module and takes back whatever it
    /// made of it. Everything about what a token means, which fields change, and
    /// what a new entry starts as lives behind that call — this only has to know
    /// how to show the answer and how to say "saving".</summary>
    private async Task ApplySegmentAsync(EntryRow row, string text, bool rewriteText)
    {
        SetSaveState(AppSaveState.Saving);

        Result<BacklogEntryDto> saved;
        try
        {
            saved = await _entryUseCases.SaveFromTextAsync(row.Id, text, Math.Max(Rows.IndexOf(row), 0));
        }
        catch
        {
            SetSaveState(AppSaveState.Error);
            return;
        }

        if (saved.IsFailure)
        {
            // Nothing has gone wrong: an entry still being typed has no title
            // yet, and one deleted from under us has nowhere to go. Neither is
            // worth alarming anybody about, so the text is simply held locally.
            SetSaveState(AppSaveState.Saved);
            return;
        }

        var entry = saved.Value;
        _entries[entry.Id] = entry;
        row.Id = entry.Id;
        RefreshRowFromEntry(row, entry, rewriteText);
        SetSaveState(AppSaveState.Saved);
        FlashSaved(row);
    }

    /// <summary>Writes each row's list position back as its rank. Which of them
    /// actually moved is the module's problem, not the list's.</summary>
    private async Task NormalizeOrderAsync()
    {
        var ids = Rows.Select(row => row.Id).OfType<Guid>().ToList();
        if (ids.Count == 0) return;

        try
        {
            await _entryUseCases.ReorderAsync(ids);
        }
        catch
        {
            SetSaveState(AppSaveState.Error);
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

    private static void RefreshRowFromEntry(EntryRow row, BacklogEntryDto entry, bool rewriteText)
    {
        row.Type = entry.Type;
        row.Priority = entry.Priority;
        row.Status = entry.Status;
        row.Area = entry.Area;
        row.Tags = entry.Tags;
        row.SubItemCount = entry.TotalSubItems;
        row.CompletedSubItemCount = entry.CompletedSubItems;
        row.IssueLink = BacklogIssues.FindLink(entry);

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
        var rows = new List<EntryRow>();

        foreach (var entry in await _entryUseCases.ListAsync())
        {
            _entries[entry.Id] = entry;

            var row = new EntryRow { Id = entry.Id };
            RefreshRowFromEntry(row, entry, rewriteText: true);
            rows.Add(row);
        }

        rows.AddRange(LoadRepositoryRows());

        Rows = rows;
        ApplyFilter();
    }

    /// <summary>Entries the configured repositories authored in their own
    /// <c>.backlog</c> folders. They are filed under the repository's alias so
    /// the global repository scope can keep them with matching local entries.</summary>
    private List<EntryRow> LoadRepositoryRows()
    {
        if (_repositoryBacklog is null) return [];

        var rows = new List<EntryRow>();

        foreach (var repository in _gitHub.Repositories)
        {
            foreach (var document in _repositoryBacklog.Load(repository.Alias))
            {
                rows.Add(MapRepositoryRow(document));
            }
        }

        return rows;
    }

    private void ReloadRepositoryFileRows(RepositoryBacklogOrigin origin, string? repositoryAlias, int insertAt)
    {
        if (_repositoryBacklog is null || string.IsNullOrWhiteSpace(repositoryAlias)) return;

        Rows.RemoveAll(row => string.Equals(row.Origin?.FilePath, origin.FilePath, StringComparison.OrdinalIgnoreCase));

        var reloaded = _repositoryBacklog.Load(repositoryAlias)
            .Where(document => string.Equals(document.FilePath, origin.FilePath, StringComparison.OrdinalIgnoreCase))
            .Select(MapRepositoryRow)
            .ToList();

        var normalizedInsertAt = insertAt < 0 ? Rows.Count : Math.Min(insertAt, Rows.Count);
        Rows.InsertRange(normalizedInsertAt, reloaded);
    }

    private static EntryRow MapRepositoryRow(RepositoryBacklogDocument document) =>
        new()
        {
            RawText = document.RawText,
            Area = document.Area,
            Status = document.Status ?? EntryStatus.Draft,
            Origin = new RepositoryBacklogOrigin(
                document.RepositoryFullName,
                document.RelativePath,
                document.FilePath,
                document.SegmentIndex)
        };

    /// <summary>A repository was added, pointed somewhere else, or had its
    /// knowledge folders turned on or off — which repository entries exist is
    /// now a different answer.</summary>
    private async void OnRepositoryKnowledgeChanged()
    {
        await ReloadRowsAsync();
        Changed?.Invoke();
    }

    private void ApplyFilter()
    {
        IEnumerable<EntryRow> rows = Rows;

        if (SelectedRepositoryAlias.Length > 0)
        {
            rows = rows.Where(RowBelongsToSelectedRepository);
        }

        var repositoryScopedRows = rows.ToList();
        RebuildAreaFilters(repositoryScopedRows);
        rows = repositoryScopedRows;

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

    private bool RowBelongsToSelectedRepository(EntryRow row) =>
        string.Equals(row.PreviewArea, SelectedRepositoryAlias, StringComparison.Ordinal);

    /// <summary>Areas exist because somebody typed one, so the filter is
    /// rebuilt from what is actually in the current repository scope. Configured
    /// repository aliases are hidden here because repository selection is a
    /// global scope, not another area/tag chip.</summary>
    private void RebuildAreaFilters(IReadOnlyList<EntryRow> scopedRows)
    {
        var repositoryAliases = _gitHub.Repositories.Select(repository => repository.Alias).ToHashSet(StringComparer.Ordinal);
        var options = new List<AreaFilterOption> { new("All", string.Empty, scopedRows.Count) };

        var used = scopedRows
            .Select(r => r.PreviewArea)
            .Where(a => !string.IsNullOrEmpty(a))
            .Where(a => !repositoryAliases.Contains(a!))
            .GroupBy(a => a!, StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal);

        foreach (var group in used)
        {
            options.Add(new AreaFilterOption(group.Key, group.Key, group.Count()));
        }

        var unfiled = scopedRows.Count(r => string.IsNullOrEmpty(r.PreviewArea));
        if (unfiled > 0 && options.Count > 1)
        {
            options.Add(new AreaFilterOption("Unfiled", UnfiledArea, unfiled));
        }

        AreaFilters = options;

        if (SelectedRepositoryAlias.Length > 0 && _gitHub.Settings.Current.Find(SelectedRepositoryAlias) is null)
        {
            SelectedRepositoryAlias = string.Empty;
        }

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

public sealed record EditingSubItem(EntryRow Row, int Index);

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
    private readonly HashSet<int> _collapsedSubItems = [];
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

    /// <summary>The GitHub issue this entry was pushed to, or null. Persisted on
    /// the entry as a <c>ProjectionRef</c>, so it survives a restart.</summary>
    public GitHubIssueLink? IssueLink { get; set; }

    /// <summary>Last known issue and pull-request state. Deliberately not
    /// persisted: it is a view of something GitHub owns, and a stale copy in the
    /// markdown file would be worse than an empty one.</summary>
    public GitHubIssueSnapshot? Snapshot { get; set; }

    /// <summary>Set while a push or refresh is in flight, so the control can say
    /// so instead of looking dead.</summary>
    public bool GitHubBusy { get; set; }

    /// <summary>Why the last GitHub call failed, in words fit to read. Shown on
    /// the entry rather than as a toast, because it is about this entry.</summary>
    public string? GitHubError { get; set; }

    /// <summary>Set while the Copilot CLI process is being started for this entry.</summary>
    public bool CopilotBusy { get; set; }

    /// <summary>Why the last Copilot CLI launch failed, in words fit to read.</summary>
    public string? CopilotError { get; set; }

    public bool IsPersisted => Id.HasValue;

    /// <summary>Where this row came from when the local store did not write it —
    /// a repository's committed <c>.backlog</c> file.</summary>
    public RepositoryBacklogOrigin? Origin { get; set; }

    /// <summary>True for rows the app only reads. Local and repository-authored
    /// entries are both editable; this property is reserved for future sources
    /// that truly cannot be written back.</summary>
    public bool IsReadOnly => false;

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

    /// <summary>Whether the rendered body and sub-item cards under this entry are
    /// folded away. Per row and in memory only — a fold is a way of looking at
    /// the list right now, not something worth writing into someone's markdown.</summary>
    public bool EntryCollapsed { get; set; }

    public bool IsSubItemCollapsed(int subItemIndex) => _collapsedSubItems.Contains(subItemIndex);

    public void ToggleSubItemCollapsed(int subItemIndex)
    {
        if (subItemIndex < 0 || subItemIndex >= PreviewSubItems.Count) return;

        if (!_collapsedSubItems.Add(subItemIndex))
        {
            _collapsedSubItems.Remove(subItemIndex);
        }
    }

    public bool HasExpandableContent
    {
        get
        {
            Render();
            return _bodyBlocks.Count > 0 || _subItems.Count > 0;
        }
    }

    /// <summary>Title-only entries are naturally single-line. Any richer entry
    /// can opt into that same layout by being folded.</summary>
    public bool UsesOneLineLayout => !HasExpandableContent || EntryCollapsed;

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

    /// <summary>Direct metadata editing can jump states, so typed statuses are no
    /// longer refused by the preview.</summary>
    public EntryStatus? BlockedStatus
    {
        get { Render(); return null; }
    }

    public IReadOnlyList<string> PreviewTags
    {
        get { Render(); return _parsed!.Tags.Count > 0 ? _parsed.Tags : Tags; }
    }

    public IReadOnlyList<string> PreviewMetadataTags
    {
        get { Render(); return _parsed!.MetadataTags; }
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
                new("priority", EntryTextParser.PriorityToken(PreviewPriority), _parsed.Priority is not null),
                new("status", EntryTextParser.StatusToken(PreviewStatus), _parsed.Status is not null)
            };

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
        _blocks = MarkdownPreview.Parse(_parsed.Body, PreviewArea, EntryMarkdownMetadataReader.Instance);
        _bodyBlocks = [.. _blocks.TakeWhile(b => b is not MdSubItem)];
        _subItems = [.. _blocks.OfType<MdSubItem>()];
        _collapsedSubItems.RemoveWhere(index => index < 0 || index >= _subItems.Count);
    }
}
