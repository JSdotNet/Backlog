using Backlog.Modules.Backlog.Abstractions;
using Backlog.Modules.Backlog.Abstractions.DataTransferObjects;
using Backlog.Modules.Backlog.Abstractions.Services;
using Backlog.Infrastructure.Copilot;
using Backlog.Infrastructure.GitHub;
using Backlog.SharedKernel.Results;

using System.Globalization;

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
/// Drives the backlog list and the detail pane beside it. An entry is still a
/// single block of plain markdown text, and every control over one is still a
/// rewrite of that text: a title, a note, a step's name, a step's notes, a token
/// on the metadata line. The text is the entry, so a field-shaped control whose
/// write did not go through it would be a second source of truth.
/// <para>
/// The markdown itself is one toggle away rather than what a click opens — an
/// escape hatch per <c>.design/content-editing.md#raw-markdown-escape-hatch</c>
/// rather than the primary mode <c>#editing-model</c> rules out.
/// </para>
/// <para>
/// Text saves on a debounce while typing and flushes the moment focus leaves,
/// per <c>.design/interaction-guidelines.md#auto-save-no-save-buttons</c>;
/// discrete changes save immediately. Re-ranking — for entries and for steps —
/// is the shared task list's gesture, pointer and keyboard alike, and arrives
/// here as "this row landed on that one".
/// </para>
/// </summary>
public sealed class BacklogDesktopState : IDisposable
{
    private const int DebounceMilliseconds = 750;

    /// <summary>How long the tick beside a just-saved row stays up. See
    /// <see cref="FlashSaved"/>.</summary>
    private const int FlashMilliseconds = 900;

    private readonly IBacklogStore _store;
    private readonly ITaskItems _entryUseCases;
    private readonly GitHubIntegration _gitHub;
    private readonly BacklogIssues _issues;
    private readonly BacklogCopilotCli _copilot;
    private readonly IRoadmapTagSource _roadmapTags;

    /// <summary>The last saved state of each persisted row, as the module
    /// describes it. Held so a badge or a GitHub link can be read without
    /// re-parsing text — never to change an entry, which only ever happens by
    /// saving its text through <see cref="ITaskItems"/>.</summary>
    private readonly Dictionary<Guid, TaskItemDto> _entries = new();

    /// <summary>The debounce each row is waiting on, keyed by row. Written by
    /// whoever is typing and read-modified by every callback that fires, which
    /// are different threads, so it is only ever touched under its own lock.
    /// </summary>
    private readonly Dictionary<Guid, Timer> _debounceTimers = new();

    /// <summary>Cancelled when this state is disposed. Every callback it left in
    /// flight — an elapsed debounce, a save flash — asks this before touching
    /// anything, because by then the store it would write to and the screen it
    /// would re-render belong to a workspace nobody is looking at.</summary>
    private readonly CancellationTokenSource _lifetime = new();

    /// <summary><see cref="_lifetime"/>'s token, taken once. A token read off a
    /// source that has since been disposed throws; a copy taken before that does
    /// not, and still reports the cancellation.</summary>
    private readonly CancellationToken _untilDisposed;

    /// <summary>How many sub-items <see cref="EditingRow"/> had when its editor
    /// opened, or -1 when no entry is being written in. See
    /// <see cref="BeginEdit"/>.</summary>
    private int _editingSubItemCount = -1;

    /// <summary>Whether a save has reported a recurrence successor that is not in
    /// <see cref="Rows"/> yet. See <see cref="ShowSpawnedOccurrenceAsync"/>.</summary>
    private bool _spawnedOccurrencePending;

    /// <summary>Whether <see cref="Dispose"/> has already run.</summary>
    private bool _disposed;

    public BacklogDesktopState(
        IBacklogStore store,
        ITaskItems entryUseCases,
        GitHubIntegration gitHub,
        BacklogCopilotCli? copilot = null,
        IRoadmapTagSource? roadmapTags = null)
    {
        _store = store;
        _entryUseCases = entryUseCases;
        _gitHub = gitHub;
        _issues = new BacklogIssues(gitHub);
        _copilot = copilot ?? BacklogCopilotCli.Unavailable;
        _roadmapTags = roadmapTags ?? EmptyRoadmapTagSource.Instance;
        _untilDisposed = _lifetime.Token;
        _store.RootChanged += OnRootChanged;
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

    /// <summary>
    /// The roadmap item tags the plan carries, offered in the tag picker beside the
    /// tags the backlog itself uses.
    /// <para>
    /// Read from Roadmap Planning through the module's own port, not from the entries:
    /// a tag planned but not yet filed against anything is exactly the one worth
    /// offering, so a person can point an entry at planned work before any entry does.
    /// Held here rather than fetched per keystroke because the picker asks for it every
    /// time it draws, and the plan changes far less often than the list redraws.
    /// </para>
    /// </summary>
    public IReadOnlyList<string> RoadmapTags { get; private set; } = [];

    /// <summary>The rows the repository scope leaves in view, before status, area
    /// and My Day narrow them. What the filter chips count: a count that shrank
    /// with the selection would answer "how many are left" rather than "how much
    /// is over there", which is the only question a chip is asked.</summary>
    public List<EntryRow> ScopedRows { get; private set; } = [];

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

    /// <summary>
    /// The date the My Day scope is narrowing to, or null while the scope is off.
    /// <para>
    /// A date rather than a flag, and one this class is told rather than one it
    /// works out. <c>.domain/backlog/features.md#feature-my-day</c> makes My Day
    /// membership arithmetic against "the reader's current local date", and the
    /// clock that answers that belongs to the pane — one place reads it, so there
    /// is one answer. Holding the date the reader turned the scope on with keeps
    /// this class free of a clock and still says exactly which day is being asked
    /// about.
    /// </para>
    /// </summary>
    public DateOnly? MyDayOn { get; private set; }

    /// <summary>The areas actually in use, in alphabetical order. There is no
    /// fixed taxonomy: an area exists because somebody typed it.</summary>
    public List<AreaFilterOption> AreaFilters { get; private set; } = [];

    public IReadOnlyList<GitHubRepositoryRef> Repositories => _gitHub.Repositories;

    /// <summary>Which identity hue each configured repository wears, keyed by alias,
    /// and empty while the visualization is off. Read from the settings store rather
    /// than worked out here, so the filter, the list and the roadmap are all reading one
    /// answer — see <c>.design/color-scheme.md#band-identity-tokens</c>. The gated
    /// answer rather than the raw one, because a surface is exactly what the gate is
    /// for: this is the hue somebody may be shown, not the hue they chose.</summary>
    public IReadOnlyDictionary<string, int> RepositoryColours => _gitHub.Settings.Current.VisibleColours();

    /// <summary>The hue for a repository named any way the settings store recognises —
    /// its alias, or the <c>owner/name</c> a session records. Null when nothing
    /// configured answers to it, and null for everything while the visualization is
    /// off.</summary>
    public int? RepositoryColourFor(string? repository) => _gitHub.Settings.Current.VisibleColourFor(repository);

    /// <summary>Whether the repository identity hues are being drawn. The shell's header
    /// carries the control, so the shell has to be able to read the state it is
    /// showing.</summary>
    public bool RepositoryColoursVisible => _gitHub.Settings.Current.ShowRepositoryColours;

    /// <summary>
    /// Shows or hides the repository identity hues everywhere at once. Returns the
    /// store's message when the choice could not be persisted; it applies to this
    /// session regardless.
    /// <para>
    /// Through here rather than the shell reaching for the settings store itself, for
    /// the same reason the hues are read through here: the shell knows about this class
    /// and this class knows where the answer lives.
    /// </para>
    /// </summary>
    public string? SetRepositoryColoursVisible(bool visible)
    {
        if (visible == RepositoryColoursVisible) return null;

        var error = _gitHub.Settings.SetShowRepositoryColours(visible);
        Changed?.Invoke();
        return error;
    }

    /// <summary>The identity mark for a row, as classes, or null when the row's area
    /// names no configured repository. The classes are the shared
    /// <c>repo-mark</c> utility; nothing here knows what colour that turns out to
    /// be.</summary>
    public string? RepositoryMarkClass(EntryRow row) =>
        RepositoryFor(row) is { } repository && RepositoryColours.TryGetValue(repository.Alias, out var colour)
            ? $"repo-mark repo-mark--{colour}"
            : null;

    public AppSaveState SaveState { get; private set; } = AppSaveState.Saved;

    /// <summary>The one row currently showing its raw markdown. Everything else
    /// shows the rendered document.</summary>
    public EntryRow? EditingRow { get; private set; }

    /// <summary>
    /// The entry the detail pane is open on, or null when nothing is selected.
    /// <para>
    /// A separate fact from <see cref="EditingRow"/>, and the reason the raw
    /// editor stopped being the way in. Which row is open is about the pane beside
    /// the list rather than about the row, and it survives the reader closing the
    /// raw escape hatch — an entry stays selected while it is read.
    /// </para>
    /// </summary>
    public EntryRow? SelectedRow { get; private set; }

    /// <summary>
    /// Opens an entry in the detail pane, or closes the pane when handed null.
    /// <para>
    /// Flushes whatever the previous selection had open on the way out, because
    /// the raw hatch below is that row's editor: leaving it behind would keep a
    /// live caret pointed at an entry that is no longer on screen, and the
    /// debounce would then save it 750ms after the reader moved on.
    /// </para>
    /// <para>
    /// And drops the outgoing row when it was a draft nobody wrote in. That
    /// sentence used to belong to <see cref="EndEditAsync"/> alone, because a
    /// brand-new entry always had an editor open on it; it no longer does — see
    /// <see cref="NewRow"/> — so leaving the pane is now the moment that says
    /// somebody opened an entry and wrote nothing in it.
    /// </para>
    /// </summary>
    public async Task SelectAsync(EntryRow? row)
    {
        if (ReferenceEquals(SelectedRow, row)) return;

        var leaving = SelectedRow;

        // Whatever caret was owed was owed to the row being left. An intent that
        // outlives the surface that asked for it lands in whichever one renders
        // next, which is a caret jumping into an entry nobody opened.
        PendingCaret = PendingCaret.None;

        // Moved before the flush, so EndEditAsync sees a row the pane is no longer
        // open on and can drop it itself rather than saving an empty draft first.
        SelectedRow = row;

        if (EditingRow is { } editing && !ReferenceEquals(editing, row)) await EndEditAsync(editing);

        if (leaving is { IsPersisted: false, IsUntouched: true } draft && Rows.Remove(draft)) ApplyFilter();

        Changed?.Invoke();
    }

    /// <summary>
    /// Whether the selected entry is showing its canonical markdown rather than
    /// its fields — the escape hatch `.design/content-editing.md#raw-markdown-escape-hatch`
    /// requires be always available.
    /// <para>
    /// Derived rather than held: the hatch <em>is</em> the selected row being the
    /// row with an editor open. A second flag saying so would be the same fact
    /// arriving twice, with two chances to disagree about whose source is on
    /// screen.
    /// </para>
    /// </summary>
    public bool RawHatchOpen => SelectedRow is { } row && ReferenceEquals(EditingRow, row);

    /// <summary>
    /// Shows or hides the selected entry's canonical markdown.
    /// <para>
    /// The whole document, sub-items included — <c>#raw-markdown-escape-hatch</c>
    /// asks for "the exact canonical Markdown that will be stored", and an entry's
    /// steps are stored in it. That is the one difference from
    /// <see cref="BeginEdit"/>, which scopes its editor to the parent chapter
    /// because the cards beside it were editing the rest.
    /// </para>
    /// <para>
    /// Closing flushes through <see cref="EndEditAsync"/>, so the hatch saves on
    /// the same terms as every other editor here and there is still no save
    /// button.
    /// </para>
    /// </summary>
    public async Task ToggleRawHatchAsync()
    {
        if (SelectedRow is not { } row) return;

        if (ReferenceEquals(EditingRow, row))
        {
            await EndEditAsync(row);
            Changed?.Invoke();
            return;
        }

        if (row.IsReadOnly) return;

        EditingRow = row;

        // Zero, not CountSubItems: the hatch hands over the entry entire, so there
        // are no chapters to hold back. See ChildStartLine — a count of zero is
        // what says "the boundary is the end of the document".
        _editingSubItemCount = 0;
        PendingCaret = PendingCaret.RawMarkdown;
        Changed?.Invoke();
    }

    /// <summary>
    /// Which control is owed the caret once the render that creates it has
    /// happened, and <see cref="PendingCaret.None"/> when nothing is.
    /// <para>
    /// Set here because only this knows what just changed; cleared by the component
    /// because only it knows whether the element the intent names is on screen yet.
    /// One value rather than a flag per destination: the caret goes to exactly one
    /// place, and two booleans could both be true — a hatch that has just opened
    /// <em>and</em> a title field waiting — leaving the render to pick between two
    /// facts that contradict each other.
    /// </para>
    /// </summary>
    public PendingCaret PendingCaret { get; set; }

    public string SaveStateLabel => SaveState switch
    {
        AppSaveState.Saving => "Saving…",
        AppSaveState.Error => "Couldn't save",
        _ => "Saved"
    };

    /// <summary>Placeholder text shown (via the native textarea placeholder,
    /// never as literal boilerplate to delete) teaching the plain-text format in
    /// the raw hatch while there is nothing in it. Which is no longer what a new
    /// entry opens on — see <see cref="NewRow"/> — but is still exactly the entry
    /// somebody asking for the source most needs the format spelled out for.</summary>
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

    /// <summary>Turns the My Day scope on for a date, or off when handed null. The
    /// caller supplies the date because the caller is what has the clock; see
    /// <see cref="MyDayOn"/>.</summary>
    public void SetMyDayFilter(DateOnly? date)
    {
        MyDayOn = date;
        ApplyFilter();
    }

    /// <summary>Appends a new, unsaved draft row at the end of the list and opens
    /// it in the detail pane, on its title. It is only persisted once a title line
    /// exists (the domain requires a title), so what is typed before that is held
    /// locally. When an area is being filtered, or a repository is scoped, the new
    /// entry starts already filed there — otherwise it would vanish the moment it
    /// saved, since a repository's rows are exactly the rows whose area names it
    /// (<see cref="RowBelongsToSelectedRepository"/>).</summary>
    public void NewRow()
    {
        var row = new EntryRow();

        var seedArea = SelectedArea.Length > 0 && SelectedArea != UnfiledArea
            ? SelectedArea
            : SelectedRepositoryAlias.Length > 0 ? SelectedRepositoryAlias : null;

        if (seedArea is not null)
        {
            row.RawText = $"# \n`task` `*medium` `!draft` `@{seedArea}`\n";
            row.SeedText = row.RawText;
        }

        Rows.Add(row);

        // Selected, and no editor opened on it. The canonical markdown stays behind
        // Ctrl+Shift+M, because that is what makes it the escape hatch
        // `.design/content-editing.md#raw-markdown-escape-hatch` describes rather
        // than "the primary surface #editing-model rules out" — and opening it here
        // put two writing surfaces on one entry: a mono textarea holding the
        // placeholder template, under the entry's own empty body editor.
        //
        // The caret goes in the title instead. It is the one thing a new entry
        // cannot do without — nothing saves until there is a title line — so it is
        // also the field a reader was going to type in next either way.
        SelectedRow = row;
        PendingCaret = PendingCaret.EntryTitle;
        ApplyFilter();
    }

    /// <summary>
    /// Brings in a plan — a block of entry text naming one or more prompts — and
    /// turns it into backlog entries in one step. Per ADR 0004 this is a use case
    /// over the same grammar every entry already goes through, so the only thing
    /// this class adds on top of an ordinary save is showing what the import
    /// produced.
    /// <para>
    /// Reloads the row list afterwards, the same way <see cref="ShowSpawnedOccurrenceAsync"/>
    /// does for a completed repeat: created and updated entries are not the rows
    /// this list already holds, so a refresh is the only way they show up without
    /// waiting for whatever the next unrelated reload happens to be.
    /// </para>
    /// <para>
    /// Returns the message the dialog reports, on either outcome. There is no
    /// separate error channel: a validation failure ("nothing parsed to a title")
    /// and a success ("3 created, 1 updated") are both a sentence for the reader,
    /// and the dialog shows whichever one it was handed.
    /// </para>
    /// </summary>
    public async Task<string> ImportPlanAsync(string rawText, string? defaultRepo = null)
    {
        SetSaveState(AppSaveState.Saving);

        Result<ImportPlanResultDto> result;
        try
        {
            result = await _entryUseCases.ImportPlanAsync(rawText, defaultRepo);
        }
        catch
        {
            SetSaveState(AppSaveState.Error);
            return "Import failed.";
        }

        SetSaveState(AppSaveState.Saved);

        if (result.IsFailure) return result.Error.Message;

        await ReloadRowsAsync();
        Changed?.Invoke();

        var value = result.Value;
        return $"Imported: {value.Created} created, {value.Updated} updated, {value.Skipped} skipped.";
    }

    // --- Editing ---------------------------------------------------------

    /// <summary>Swaps a row from its rendered form to raw markdown.</summary>
    public void BeginEdit(EntryRow row)
    {
        // Reserved for future truly read-only sources; local and repository
        // backlog rows are both editable.
        if (row.IsReadOnly) return;
        if (ReferenceEquals(EditingRow, row)) return;
        EditingRow = row;

        // The editor shows the entry without its sub-items, so it has to be
        // agreed up front which chapters those are. Working it out afresh from
        // the text on every keystroke means a `##` heading somebody has just
        // typed counts as one, and the chapter below it is handed back a second
        // time — once per keystroke.
        _editingSubItemCount = EntryTextParser.CountSubItems(row.RawText);
        PendingCaret = PendingCaret.RawMarkdown;
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

        // The editor is closing, and with it any caret it had not been given yet —
        // for the reason SelectAsync gives. A flush is also what turns a brand-new
        // draft into an entry with text in it, so the caret its creation asked for
        // is no longer owed either.
        PendingCaret = PendingCaret.None;

        if (ReferenceEquals(EditingRow, row))
        {
            EditingRow = null;
            _editingSubItemCount = -1;
        }

        // An entry someone opened and left untouched was never an entry. Drop
        // it rather than leaving an "Untitled" husk in the list.
        //
        // Once they have actually left it, though: with the detail pane still open
        // on this row, closing the raw hatch is a move between two surfaces on the
        // same entry, and deleting the entry out from under it would answer a
        // keystroke about one control by discarding the document. SelectAsync says
        // the same sentence for the row the pane moves off.
        if (!row.IsPersisted && row.IsUntouched && !ReferenceEquals(SelectedRow, row))
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

    // The sub-item raw editor is gone, and its capability is not: a step's notes
    // are edited in the shared task row's own body editor, which reports every
    // keystroke to ChangeSubItemNote below. That is the same rewrite through the
    // same ReplaceSubItemText, arriving from a library control instead of a
    // hand-rolled textarea — so what went is an implementation, not a way in.

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

        // A detail pane open on a deleted entry is a pane about nothing. Closed
        // here rather than left to the view to notice, so there is one answer to
        // "what is selected" and the list is it.
        if (ReferenceEquals(SelectedRow, row)) SelectedRow = null;

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

        Rows.Remove(row);

        await NormalizeOrderAsync();
        ApplyFilter();
    }

    // --- Reordering ------------------------------------------------------
    //
    // No drag state here any more. The shared task list owns the whole gesture —
    // which row is moving, which one it is over, the previewed order, the keyboard
    // equivalent and the announcement — and tells the host one thing: this row
    // landed on that one. Holding a DraggedRow beside that would be the same fact
    // twice, with the pane's copy able to disagree with the list's.

    /// <summary>
    /// Moves <paramref name="row"/> into <paramref name="target"/>'s place.
    /// <para>
    /// A destination rather than a direction, because that is what a drop is: the
    /// moved row takes the target's slot. "Before" and "after" stop meaning what
    /// the reader saw at the ends of a list, which is why the shared list reports
    /// a target and nothing else.
    /// </para>
    /// </summary>
    public async Task MoveEntryAsync(EntryRow row, EntryRow target)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(target);

        if (row.IsReadOnly || ReferenceEquals(row, target)) return;

        var from = Rows.IndexOf(row);
        var to = Rows.IndexOf(target);
        if (from < 0 || to < 0 || from == to) return;

        // Remove first, then insert at the index the target held *before* the
        // removal. That is exactly what TaskMove.ApplyTo does, and it has to be:
        // the list previews a drop by applying that method to the rows it was
        // handed, so any other arithmetic here would put the row somewhere the
        // reader had not been shown.
        Rows.RemoveAt(from);
        Rows.Insert(to, row);

        await NormalizeOrderAsync();
        ApplyFilter();
        Changed?.Invoke();
    }

    // --- Sub-items -------------------------------------------------------

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

        await SaveRowAsync(row, isFlush: true);

        ApplyFilter();
        Changed?.Invoke();
    }

    public async Task ToggleSubItemAsync(EntryRow row, int subItemIndex)
    {
        if (row.IsReadOnly) return;

        // Two ways a sub-item can be finished, and which one applies is decided by
        // what the author wrote rather than by this method. A heading carrying a
        // literal `[ ]` has its marker flipped, because the checkbox glyph is
        // reserved for literal task-list syntax
        // (.design/content-editing.md#backlog-entry-structure). A plain heading has
        // no marker to flip, and inventing one would put checkbox syntax into
        // somebody's document because they pressed a control — so its completion
        // goes on its own metadata line as `!done`, which is the same form the
        // cascading parent status change already writes and the same form the read
        // view already reads back.
        var rewritten = EntryTextParser.ToggleSubItem(row.RawText, subItemIndex);

        if (string.Equals(rewritten, row.RawText, StringComparison.Ordinal))
        {
            var done = subItemIndex >= 0
                && subItemIndex < row.PreviewSubItems.Count
                && row.PreviewSubItems[subItemIndex].Done;

            rewritten = EntryTextParser.WithSubItemStatus(
                row.RawText,
                subItemIndex,
                done ? EntryStatus.Ready : EntryStatus.Done);
        }

        if (string.Equals(rewritten, row.RawText, StringComparison.Ordinal)) return;

        CancelDebounce(row);
        row.RawText = rewritten;

        await SaveRowAsync(row, isFlush: true);

        ApplyFilter();
        Changed?.Invoke();
    }

    public async Task ChangeTypeAsync(EntryRow row, EntryType type) =>
        await RewriteMetadataAsync(row, EntryTextParser.WithType(row.RawText, type));

    public async Task ChangePriorityAsync(EntryRow row, Priority priority) =>
        await RewriteMetadataAsync(row, EntryTextParser.WithPriority(row.RawText, priority));

    /// <summary>Estimates the entry at a number of story points, or clears the
    /// estimate. Null clears for the same reason the scheduling fields do: absent
    /// means absent, so an unset effort carries no token rather than an
    /// <c>effort:</c> with nothing after it. Written through the same rewrite path
    /// as priority — the number ends up on the canonical metadata line, which is
    /// the entry.</summary>
    public async Task ChangeEffortAsync(EntryRow row, int? effort) =>
        await RewriteMetadataAsync(row, EntryTextParser.WithEffort(row.RawText, effort));

    public async Task ChangeStatusAsync(EntryRow row, EntryStatus status) =>
        await RewriteMetadataAsync(row, EntryTextParser.WithStatus(row.RawText, status, cascadeSubItems: true), forceWhenEqual: row.Status != status);

    /// <summary>
    /// Completes the entry, or puts a completed one back to work.
    /// <para>
    /// Done and back to InProgress, which are the two moves
    /// <c>.domain/backlog/flow.md#backlog-entry-lifecycle</c> allows either side of
    /// the finish line. Nothing else is invented: an entry still in Draft cannot
    /// legally reach Done, the module refuses that transition exactly as it refuses
    /// it from the status selector, and the refusal comes back through the same
    /// "reads as" line — the circle does not tick, and the entry says why.
    /// </para>
    /// </summary>
    public async Task ToggleDoneAsync(EntryRow row)
    {
        ArgumentNullException.ThrowIfNull(row);

        await ChangeStatusAsync(
            row,
            row.PreviewStatus is EntryStatus.Done ? EntryStatus.InProgress : EntryStatus.Done);
    }

    /// <summary>Renames the entry — its first line, which is its title. A discrete
    /// change rather than a keystroke, so it saves immediately.</summary>
    public async Task RenameEntryAsync(EntryRow row, string title)
    {
        ArgumentNullException.ThrowIfNull(row);

        await RewriteMetadataAsync(row, EntryTextParser.WithTitle(row.RawText, title));
    }

    // ChangeNote was here: the entry's prose, scoped to the region in front of the
    // first chapter. It is gone because the pane's markdown block is a view of the
    // whole body rather than of that region — see ChangeBody below — which left this
    // method with no caller and no test. An orphan that still compiles reads as a
    // supported way in, so it went with its caller rather than after it.

    /// <summary>Renames one step. Same shape as renaming the entry, one level
    /// down, and indexed the way every other sub-item write here is indexed — the
    /// nth chapter.</summary>
    public async Task RenameSubItemAsync(EntryRow row, int subItemIndex, string title)
    {
        ArgumentNullException.ThrowIfNull(row);

        await RewriteMetadataAsync(row, EntryTextParser.WithSubItemTitle(row.RawText, subItemIndex, title));
    }

    /// <summary>One step's notes, per keystroke, debounced for the same reason the
    /// entry's note is.</summary>
    public void ChangeSubItemNote(EntryRow row, int subItemIndex, string note)
    {
        ArgumentNullException.ThrowIfNull(row);

        if (row.IsReadOnly) return;

        var rewritten = EntryTextParser.WithSubItemNote(row.RawText, subItemIndex, note);
        if (string.Equals(rewritten, row.RawText, StringComparison.Ordinal)) return;

        row.RawText = rewritten;
        ScheduleDebouncedSave(row);
        Changed?.Invoke();
    }

    /// <summary>Adds a step to the end of the entry. Nothing happens on an empty
    /// name: there is no such thing as a step with no name, and adding one would
    /// leave a chapter the reader cannot see or remove.</summary>
    public async Task AddSubItemAsync(EntryRow row, string title)
    {
        ArgumentNullException.ThrowIfNull(row);

        await RewriteMetadataAsync(row, EntryTextParser.AppendSubItem(row.RawText, title));
    }

    /// <summary>
    /// Deletes one step: the chapter, its own metadata line and its notes.
    /// <para>
    /// No confirmation and no undo, which is the entry-level bin's bargain one level
    /// down — <see cref="DeleteRowAsync"/> asks nothing and offers nothing back, and
    /// a step that asked while the entry holding it did not would be inconsistent in
    /// the wrong direction: the step is the cheaper of the two to lose and the
    /// cheaper of the two to type again.
    /// </para>
    /// <para>
    /// Indexed the way every other sub-item write here is — the nth chapter — and
    /// guarded the way the move is, against an index that names no step: the id came
    /// off a row, and a row the reader pressed may already have gone.
    /// </para>
    /// <para>
    /// <c>_editingSubItemCount</c> is deliberately left alone, exactly as
    /// <see cref="AddSubItemAsync"/> leaves it. It is the count the raw editor was
    /// opened against, and a count higher than the chapters that remain falls back
    /// to the first chapter in <c>ChildStartLine</c> — which is the honest boundary
    /// for an entry nobody has typed a new <c>##</c> into. Rewriting it here would
    /// be this method deciding what an open editor is showing, which is
    /// <see cref="BeginEdit"/>'s answer rather than a step delete's.
    /// </para>
    /// </summary>
    public async Task RemoveSubItemAsync(EntryRow row, int subItemIndex)
    {
        ArgumentNullException.ThrowIfNull(row);

        if (subItemIndex < 0 || subItemIndex >= row.PreviewSubItems.Count) return;

        await RewriteMetadataAsync(row, EntryTextParser.RemoveSubItem(row.RawText, subItemIndex));
    }

    // No per-sub-item type, priority, status or tag edits. A sub-item carries a
    // title, a status, notes and an order and nothing else, and the four
    // rewrites that used to live here wrote tokens that TaskTextSync then
    // discarded on the next save — the UI was editing values the domain had no
    // room for. Sub-item completion is still expressible, through the checkbox
    // and ToggleSubItemAsync above.

    public async Task ChangeAreaAsync(EntryRow row, string? area) =>
        await RewriteMetadataAsync(row, EntryTextParser.WithArea(row.RawText, area));

    public async Task ChangeTagsAsync(EntryRow row, IEnumerable<string> tags) =>
        await RewriteMetadataAsync(row, EntryTextParser.WithTags(row.RawText, tags));

    public async Task ChangeTagsAsync(EntryRow row, string tags) =>
        await RewriteMetadataAsync(row, EntryTextParser.WithTags(row.RawText, tags));

    // The five scheduling and dependency fields, each written the same way every
    // other control on this pane writes: rewrite the metadata line, save the
    // text. Null clears, because absent means absent and an unset field carries
    // no token rather than an empty one.
    //
    // Deliberately no "today" anywhere below. Which day it is belongs to whatever
    // is reading a clock, and a state object that took DateTime.Now would be one
    // no test could pin down.

    public async Task ChangeDueAsync(EntryRow row, DateOnly? dueOn) =>
        await RewriteMetadataAsync(row, EntryTextParser.WithDue(row.RawText, dueOn));

    public async Task ChangeReminderAsync(EntryRow row, DateTime? remindAt) =>
        await RewriteMetadataAsync(row, EntryTextParser.WithReminder(row.RawText, remindAt));

    public async Task ChangeRepeatAsync(EntryRow row, Recurrence? recurrence) =>
        await RewriteMetadataAsync(row, EntryTextParser.WithRepeat(row.RawText, recurrence));

    /// <summary>Stamps the entry for a particular day, or clears the stamp.
    /// Taking an entry out of My Day is clearing the date rather than writing a
    /// different one: the entry is in My Day exactly while the stamp is the
    /// reader's current local date, so there is no "not today" to write.</summary>
    public async Task ChangeMyDayAsync(EntryRow row, DateOnly? inMyDayOn) =>
        await RewriteMetadataAsync(row, EntryTextParser.WithMyDay(row.RawText, inMyDayOn));

    public async Task ChangeDependsOnAsync(EntryRow row, IEnumerable<string>? dependsOn) =>
        await RewriteMetadataAsync(row, EntryTextParser.WithDependsOn(row.RawText, dependsOn));

    /// <summary>Attaches a place, or detaches what was attached. A path and never a
    /// copy, and one place and never a list — both decisions live on
    /// <see cref="Attachment"/>, which also turns a blank path into "nothing
    /// attached" so this method does not have to.</summary>
    public async Task ChangeAttachmentAsync(EntryRow row, string? path) =>
        await RewriteMetadataAsync(row, EntryTextParser.WithAttachment(row.RawText, Attachment.From(path)));

    /// <summary>
    /// Remembers which reading of the body the person asked for.
    /// <para>
    /// A metadata rewrite like every other one on this list, and flushed rather than
    /// debounced for the same reason a status change is: it is one discrete decision,
    /// not typing. The entry is where it is kept because the markdown is canonical —
    /// a preference held anywhere else would not survive the file being shared, and
    /// the next person to open it from a clone would get somebody else's default.
    /// </para>
    /// </summary>
    public async Task ChangeViewAsync(EntryRow row, EntryView? view)
    {
        ArgumentNullException.ThrowIfNull(row);

        await RewriteMetadataAsync(row, EntryTextParser.WithView(row.RawText, view));
    }

    /// <summary>
    /// The whole body, per keystroke, debounced for the same reason a note is
    /// (see <c>interaction-guidelines.md#auto-save-no-save-buttons</c>) — prose is prose, and a save per character would be a save per character.
    /// <para>
    /// The body rather than the note, because the markdown block in the pane is a
    /// view of the same text the steps list is a view of. A writer scoped to the
    /// prose half would silently discard a <c>##</c> chapter somebody typed into the
    /// block, which is the one thing a raw-ish surface must never do.
    /// </para>
    /// </summary>
    public void ChangeBody(EntryRow row, string body)
    {
        ArgumentNullException.ThrowIfNull(row);

        if (row.IsReadOnly) return;

        var rewritten = EntryTextParser.WithBody(row.RawText, body);
        if (string.Equals(rewritten, row.RawText, StringComparison.Ordinal)) return;

        row.RawText = rewritten;
        ScheduleDebouncedSave(row);
        Changed?.Invoke();
    }

    private async Task RewriteMetadataAsync(EntryRow row, string rewritten, bool forceWhenEqual = false)
    {
        if (row.IsReadOnly) return;
        if (string.Equals(rewritten, row.RawText, StringComparison.Ordinal) && !forceWhenEqual) return;

        CancelDebounce(row);
        row.RawText = rewritten;

        await SaveRowAsync(row, isFlush: true);

        ApplyFilter();
        Changed?.Invoke();
    }

    /// <summary>
    /// Moves one step into another step's place, within the same entry.
    /// <para>
    /// Two indices rather than a direction, and no drag state to consult: the
    /// shared task list holds the whole gesture and reports where the step landed,
    /// exactly as it does for the entries above. Steps only ever re-rank inside
    /// their own entry — a step belongs to what it is written under, so dropping
    /// one on another entry would be moving text between two documents, which is a
    /// rewrite rather than a re-rank, and no list here offers it.
    /// </para>
    /// <para>
    /// The re-focus the keyboard move needs is the list's now too. It puts the
    /// caret back on the row it moved, by an id only it knows, which is why there
    /// is nothing left here for a pane to consume.
    /// </para>
    /// </summary>
    public async Task MoveSubItemAsync(EntryRow row, int from, int to)
    {
        ArgumentNullException.ThrowIfNull(row);

        if (row.IsReadOnly) return;
        if (from == to || from < 0 || to < 0 || to >= row.PreviewSubItems.Count) return;

        var rewritten = EntryTextParser.MoveSubItem(row.RawText, from, to);
        if (string.Equals(rewritten, row.RawText, StringComparison.Ordinal)) return;

        CancelDebounce(row);
        row.RawText = rewritten;

        // A re-rank is a finished gesture, not a keystroke: save it now rather
        // than on a debounce that a drag never generates.
        await SaveRowAsync(row, isFlush: true);

        ApplyFilter();
        Changed?.Invoke();
    }

    /// <summary>
    /// Hands back everything that outlives the gesture that started it: the
    /// store subscription, every armed debounce, and the timed callbacks already
    /// in flight.
    /// <para>
    /// The cancellation matters as much as the timer disposal, and is why the
    /// order here is cancel-then-dispose. Disposing a <see cref="Timer"/> does
    /// not stop a callback that has already begun, and the save flash is a bare
    /// delay with no timer to dispose at all — so both read the token instead,
    /// and see a cancellation that was raised before this method took the lock.
    /// </para>
    /// </summary>
    public void Dispose()
    {
        // A host may register this state with a container and dispose it by hand,
        // and which of the two gets there first is not something either can see.
        if (_disposed) return;
        _disposed = true;

        _store.RootChanged -= OnRootChanged;

        _lifetime.Cancel();

        lock (_debounceTimers)
        {
            foreach (var timer in _debounceTimers.Values)
            {
                timer.Dispose();
            }

            _debounceTimers.Clear();
        }

        _lifetime.Dispose();
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

            if (linked.TryGetValue(out var updated))
            {
                _entries[id] = updated;

                // `repo:` round-trips through the canonical text like every other
                // named token now, so the row's raw text has to catch up here —
                // LinkToIssueAsync set RepoIds on the aggregate directly, off to
                // the side of the ordinary parse-and-save path, and a row left
                // holding the text from before the push would silently lose that
                // on its next edit: SaveTaskFromText parses what is on screen,
                // and a `repo:` token that was never written is a `repo:` token
                // that edit clears.
                RefreshRowFromEntry(row, updated, rewriteText: true);
            }

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

    // PushSubItemToGitHubAsync used to sit here, filing one step as an issue of its
    // own. It is gone with the two buttons that reached it, and deliberately this
    // time: `.domain/backlog/domain.md` gives ProjectionRef to TaskItem and says
    // a Sub-Item "may project to GitHub issue task-list checkboxes" — checkboxes
    // inside the entry's issue. A step that was its own issue had nowhere to record
    // the link, so nothing could tell that it had already been pushed. The method
    // and its test went together rather than leaving an orphan on one side or a test
    // guarding a path no reader can take.

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
        _entries.Clear();

        await ReloadRowsAsync();
        Changed?.Invoke();
    }

    // --- Internals ------------------------------------------------------

    private void ScheduleDebouncedSave(EntryRow row)
    {
        CancelDebounce(row);

        lock (_debounceTimers)
        {
            // Disposed while this keystroke was being handled. Arming now would
            // put a timer in a map nothing will ever empty again.
            if (_untilDisposed.IsCancellationRequested) return;

            // The timer is its own callback's state, so the callback can tell
            // whether it is still the arm this row is waiting on. See
            // OnDebounceElapsed.
            var timer = new Timer(state => OnDebounceElapsed(row, (Timer)state!));
            _debounceTimers[row.Key] = timer;
            timer.Change(DebounceMilliseconds, Timeout.Infinite);
        }
    }

    private async void OnDebounceElapsed(EntryRow row, Timer timer)
    {
        lock (_debounceTimers)
        {
            // Not this row's arm any more. Disposing a timer does not stop a
            // callback that has already been scheduled, so the typing thread may
            // have re-armed the row in the meantime — and taking that newer
            // timer's entry out here would leave it with nothing able to cancel
            // it, saving text the person has since moved past.
            if (!_debounceTimers.TryGetValue(row.Key, out var armed) || !ReferenceEquals(armed, timer)) return;

            _debounceTimers.Remove(row.Key);
        }

        timer.Dispose();

        if (_untilDisposed.IsCancellationRequested) return;

        await SaveRowAsync(row, isFlush: false);

        Changed?.Invoke();
    }

    private void CancelDebounce(EntryRow row)
    {
        Timer? timer;

        lock (_debounceTimers)
        {
            _debounceTimers.Remove(row.Key, out timer);
        }

        timer?.Dispose();
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

        if (!isFlush || overflow.Count == 0)
        {
            await ShowSpawnedOccurrenceAsync();
            return;
        }

        var insertAt = Rows.IndexOf(row) + 1;
        foreach (var segment in overflow)
        {
            var spawned = new EntryRow { RawText = segment };
            Rows.Insert(insertAt++, spawned);
            await ApplySegmentAsync(spawned, segment, rewriteText: true);
        }

        await NormalizeOrderAsync();
        ApplyFilter();
        await ShowSpawnedOccurrenceAsync();
    }

    /// <summary>Hands one entry's text to the module and takes back whatever it
    /// made of it. Everything about what a token means, which fields change, and
    /// what a new entry starts as lives behind that call — this only has to know
    /// how to show the answer and how to say "saving".</summary>
    private async Task ApplySegmentAsync(EntryRow row, string text, bool rewriteText)
    {
        SetSaveState(AppSaveState.Saving);

        Result<SavedTaskDto> saved;
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

        var entry = saved.Value.Entry;
        _entries[entry.Id] = entry;
        row.Id = entry.Id;
        RefreshRowFromEntry(row, entry, rewriteText);
        SetSaveState(AppSaveState.Saved);
        FlashSaved(row);

        // Completing a repeating entry left a second entry in the store that this
        // list has never seen. Refreshing the saved row cannot show it, because the
        // successor is not that row — so the save has to say a spawn happened, and
        // it does.
        if (saved.Value.SpawnedOccurrenceId is not null) _spawnedOccurrencePending = true;
    }

    /// <summary>
    /// Reloads the list when a save spawned the next occurrence of a repeating
    /// entry, so the successor appears rather than waiting for the next reload
    /// somebody else happens to trigger.
    /// <para>
    /// Deferred while an editor is open. A reload replaces every row object, and
    /// doing that under a live caret would take the editor out from under whoever
    /// is typing. The flag survives instead, and blurring the editor flushes
    /// through here on its way out — a debounced save that completed the entry
    /// therefore shows its successor the moment focus leaves, which is also the
    /// first moment the reader could have looked at the list.
    /// </para>
    /// </summary>
    private async Task ShowSpawnedOccurrenceAsync()
    {
        if (!_spawnedOccurrencePending) return;
        if (EditingRow is not null) return;

        _spawnedOccurrencePending = false;
        await ReloadRowsAsync();
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

    /// <summary>
    /// The tick beside a row that has just been written, and the wait after which
    /// it goes away again.
    /// <para>
    /// Cancellable, because the state can be disposed inside that wait — the pane
    /// closed, the workspace moved — and a flash that came back regardless would
    /// re-render a screen that is gone. Same shape as the shared library's own
    /// timed feedback; see <c>Toast</c> and <c>CopyButton</c>.
    /// </para>
    /// </summary>
    private async void FlashSaved(EntryRow row)
    {
        row.JustSaved = true;
        Changed?.Invoke();

        try
        {
            await Task.Delay(FlashMilliseconds, _untilDisposed);
        }
        catch (TaskCanceledException)
        {
            return;
        }

        row.JustSaved = false;
        Changed?.Invoke();
    }

    private void SetSaveState(AppSaveState state)
    {
        SaveState = state;
    }

    private static void RefreshRowFromEntry(EntryRow row, TaskItemDto entry, bool rewriteText)
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
        // Whatever a save was waiting to show is about to be on screen, whoever
        // asked for the reload and for whatever reason.
        _spawnedOccurrencePending = false;

        // The plan's tags travel with the reload, so the picker offers planned work
        // the moment the list it sits in refreshes rather than a beat behind it.
        RoadmapTags = await _roadmapTags.TagsInUseAsync();

        var rows = new List<EntryRow>();

        foreach (var entry in await _entryUseCases.ListAsync())
        {
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
        IEnumerable<EntryRow> rows = Rows;

        if (SelectedRepositoryAlias.Length > 0)
        {
            rows = rows.Where(RowBelongsToSelectedRepository);
        }

        var repositoryScopedRows = rows.ToList();
        RebuildAreaFilters(repositoryScopedRows);
        ScopedRows = repositoryScopedRows;
        rows = repositoryScopedRows;

        // My Day narrows what area and status have already left in view rather
        // than replacing either: it is a decision about today taken on top of
        // wherever the reader is looking, so all three compose.
        if (MyDayOn is { } myDay)
        {
            rows = rows.Where(x => x.PreviewInMyDayOn == myDay);
        }

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
        //
        // And so does an unsaved draft the pane is open on, which used to be the
        // same row: creating an entry opened an editor on it, and that is what kept
        // it here. It no longer does (see NewRow), and this one cannot be left to
        // the filter either — it is not in the store, so a filter that dropped it
        // would not be hiding an entry, it would be losing one. Only while it is
        // unsaved: once it exists, it is an entry like any other and the rule below
        // applies to it.
        FilteredRows =
        [
            .. rows
                .Union(Rows.Where(r =>
                    ReferenceEquals(r, EditingRow)
                    || (ReferenceEquals(r, SelectedRow) && !r.IsPersisted)))
                .OrderBy(Rows.IndexOf)
        ];

        // Selection follows the list, which is the only way the pane beside it can
        // be about something. Deleting an entry already closed the pane; changing
        // the repository, the area, the status or My Day did not, so a reader who
        // narrowed the list to nothing was left reading a panel for an entry the
        // list no longer contained — a detail pane open beside "Nothing here yet."
        // Filtered out is the same fact as gone as far as this half of the split is
        // concerned, so it is answered in the same place.
        if (SelectedRow is { } selected && !FilteredRows.Any(row => ReferenceEquals(row, selected)))
        {
            SelectedRow = null;
        }
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

/// <summary>
/// Where the caret is owed, for a control that does not exist until the next
/// render draws it.
/// <para>
/// Two intents rather than one flag, because they are two facts: a hatch that has
/// just been asked for wants the caret in its textarea, and a brand-new entry
/// wants it in its title. A single "focus pending" would leave whichever surface
/// consumed it guessing which of those it was, and the guess would be wrong in
/// exactly the case the surfaces overlap.
/// </para>
/// </summary>
public enum PendingCaret
{
    /// <summary>Nothing is owed the caret; leave it where the reader put it.</summary>
    None,

    /// <summary>The raw-markdown hatch has just opened and wants the caret in its
    /// textarea — otherwise Ctrl+Shift+M reveals the source and leaves the focus on
    /// the pane, so reaching it costs a click anyway.</summary>
    RawMarkdown,

    /// <summary>A brand-new entry has just been added and wants the caret in its
    /// title field, which is the one thing it cannot be saved without.</summary>
    EntryTitle
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

    /// <summary>True for rows the app only reads. Every task is editable today;
    /// this property is reserved for future sources that truly cannot be written
    /// back.</summary>
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

    public bool HasExpandableContent
    {
        get
        {
            Render();
            return _bodyBlocks.Count > 0 || _subItems.Count > 0;
        }
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

    // The scheduling and dependency fields, read from the text and nowhere else.
    // Unlike type, priority and status there is no saved value to fall back to:
    // these five are absent by default, so "no token" is the answer rather than a
    // gap to fill from the last save. Whether a My Day stamp is *today* needs a
    // clock and is therefore the caller's question, not this row's.

    public DateOnly? PreviewDueOn
    {
        get { Render(); return _parsed!.DueOn; }
    }

    public DateTime? PreviewRemindAt
    {
        get { Render(); return _parsed!.RemindAt; }
    }

    public Recurrence? PreviewRecurrence
    {
        get { Render(); return _parsed!.Recurrence; }
    }

    public DateOnly? PreviewInMyDayOn
    {
        get { Render(); return _parsed!.InMyDayOn; }
    }

    public IReadOnlyList<string> PreviewDependsOn
    {
        get { Render(); return _parsed!.DependsOn ?? []; }
    }

    /// <summary>The story-point estimate, read from the text and nowhere else.
    /// Null when the entry carries no <c>effort:</c> token — "not estimated", which
    /// is a different answer from a zero-point estimate. Like the scheduling fields
    /// there is no saved value to fall back to: the token is the whole of what the
    /// entry knows about its size.</summary>
    public int? PreviewEffort
    {
        get { Render(); return _parsed!.Effort; }
    }

    /// <summary>What is attached, as written down. One place or nothing — see
    /// <see cref="Attachment"/> for why it is never a list.</summary>
    public Attachment? PreviewAttachment
    {
        get { Render(); return _parsed!.Attachment; }
    }

    /// <summary>
    /// Which reading of the body this entry was last looked at in, as written down.
    /// Null when nobody has said, which is a different answer from either view —
    /// see <see cref="EffectiveView"/> for what the pane does with that.
    /// </summary>
    public EntryView? PreviewView
    {
        get { Render(); return _parsed!.View; }
    }

    /// <summary>
    /// The view to actually draw: the one that was asked for, or the one that has
    /// something to show.
    /// <para>
    /// An entry with no <c>##</c> chapters has no steps for the steps view to list,
    /// so defaulting it there would open every prose entry on an empty list with a
    /// line underneath explaining that its text is somewhere else. Derived rather
    /// than written down, because a default written into the text is a preference
    /// the reader never expressed — and it would then have to be unwritten from
    /// every entry before this default could ever change.
    /// </para>
    /// </summary>
    public EntryView EffectiveView
    {
        get
        {
            Render();

            // Off the cached parse rather than re-locating the chapters. The pane
            // asks this several times per render, and the text has not changed
            // while it did.
            return _parsed!.View ?? (_subItems.Count > 0 ? EntryView.Steps : EntryView.Notes);
        }
    }

    /// <summary>
    /// Whether the steps view is leaving body text off the screen.
    /// <para>
    /// The steps view lists chapters, and the prose an entry opens with is not one.
    /// A view that quietly hid it would make the markdown look like it had lost
    /// text, so the pane says so instead and puts the block one press away —
    /// <c>.design/content-editing.md#round-trip-fidelity</c> is about the text
    /// surviving, and a reader who cannot see it has no way to know that it did.
    /// </para>
    /// </summary>
    public bool StepsViewHidesProse
    {
        get
        {
            Render();

            // The blocks in front of the first chapter, which is exactly the prose
            // the steps have no row for. Read off the same cached parse the steps
            // themselves come from, so the two cannot disagree about where the first
            // chapter starts.
            return _bodyBlocks.Count > 0;
        }
    }

    /// <summary>What the app actually understood from the meta line, in plain
    /// words. Shown live under the editor so nobody has to guess which token
    /// became the status.
    /// <para>
    /// Values are the canonical token forms rather than anything localized,
    /// because the hint restates what will be <em>saved</em> — and what gets
    /// saved is the metadata line. A reader comparing the hint to the text they
    /// just typed is comparing like with like.
    /// </para></summary>
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

            // Absent means absent: an unset scheduling field contributes nothing
            // rather than a reading saying "none". These are the only readings
            // that are always explicit — there is no default due date for one of
            // them to be mistaken for.
            if (PreviewDueOn is { } due)
            {
                readings.Add(new MetaReading("due", EntryTextParser.DateToken(due), true));
            }

            if (PreviewRemindAt is { } remindAt)
            {
                readings.Add(new MetaReading("reminder", EntryTextParser.ReminderToken(remindAt), true));
            }

            if (PreviewRecurrence is { } recurrence)
            {
                readings.Add(new MetaReading("repeat", EntryTextParser.RepeatToken(recurrence), true));
            }

            if (PreviewInMyDayOn is { } myDay)
            {
                readings.Add(new MetaReading("my day", EntryTextParser.DateToken(myDay), true));
            }

            foreach (var id in PreviewDependsOn)
            {
                readings.Add(new MetaReading("after", id, true));
            }

            // The size, shown the same way and in the same slot the canonical line
            // writes it: after what the entry waits on and before the two tokens
            // that are not about the work. Only when it was written down — an entry
            // nobody has estimated has no reading here, the same as an unset due
            // date has none.
            if (PreviewEffort is { } effort)
            {
                readings.Add(new MetaReading("effort", effort.ToString(CultureInfo.InvariantCulture), true));
            }

            if (PreviewAttachment is { } attachment)
            {
                readings.Add(new MetaReading("files", attachment.Path, true));
            }

            // Last, and only when it was written down. The reading line restates
            // what will be saved, and the view the pane happens to be showing
            // because nobody has chosen one is not on the line to be saved.
            if (PreviewView is { } view)
            {
                readings.Add(new MetaReading("view", EntryTextParser.ViewToken(view), true));
            }

            // A token whose kind was understood and whose value was not reads as
            // refused rather than disappearing. Silence here would be the worst
            // of the options: the field is not saved either way, and a hint that
            // simply omitted `due:friday` would look exactly like a hint for an
            // entry that never had a due date typed on it.
            foreach (var token in _parsed.Unreadable ?? [])
            {
                readings.Add(new MetaReading(
                    RefusedKind(token.Name),
                    token.Value.Length == 0 ? "(empty)" : token.Value,
                    Explicit: true,
                    Note: "not understood — the field is left unset"));
            }

            return readings;
        }
    }

    /// <summary>The words a refused token is filed under, matching the kind the
    /// same field reads as when it parses — so "due" appears once in the hint
    /// whether the value was accepted or refused.</summary>
    private static string RefusedKind(string tokenName) => tokenName switch
    {
        "remind" => "reminder",
        "myday" => "my day",
        _ => tokenName
    };

    private void Render()
    {
        if (_renderedFrom is not null && string.Equals(_renderedFrom, RawText, StringComparison.Ordinal)) return;

        _renderedFrom = RawText;
        _parsed = EntryTextParser.Parse(RawText);
        _blocks = MarkdownPreview.Parse(_parsed.Body, PreviewArea, EntryMarkdownMetadataReader.Instance);
        _bodyBlocks = [.. _blocks.TakeWhile(b => b is not MdSubItem)];
        _subItems = [.. _blocks.OfType<MdSubItem>()];
    }
}
