using Backlog.Infrastructure.GitHub;
using Backlog.Modules.Inbox.Abstractions;
using Backlog.Modules.Inbox.Abstractions.DataTransferObjects;
using Backlog.Modules.Inbox.Abstractions.Services;
using Backlog.SharedKernel.Results;
using Backlog.UI.Components.Badges;
using Backlog.UI.Components.Feedback;
using Backlog.UI.Components.Selects;

namespace Backlog.Desktop.UI.Inbox;

/// <summary>
/// Drives the Inbox pane: the snapshot the module handed over, which slice of it
/// is open, which row is chosen, which kinds are filtered in, which groups are
/// folded and which side-menu row is being renamed. The same shape as
/// <c>TasksDesktopState</c> — one object per window (or per circuit), a
/// <see cref="Changed"/> event the pane re-renders on — and for the same reason:
/// the shell draws the pane in one of two slots depending on which other panes
/// are open, and moving slots re-mounts it. A selection held in the component
/// would drop the moment triage opened the backlog beside it.
/// <para>
/// Everything that reads as a decision goes through <see cref="IInboxItems"/>
/// and comes back as a reload: the module's snapshot is the truth, and this
/// class never edits a DTO in place. What it owns is view state and the
/// derivations a render needs — the rows of a slice, the counts on the side
/// menu, the kind chips — computed here rather than in Razor so they can be
/// asserted without a DOM.
/// </para>
/// <para>
/// <see cref="Routed"/> is the one thing it says to anyone but the pane. Routing
/// creates entries in another context, and the shell — which is allowed to see
/// both — refreshes the Tasks pane on it. The Inbox itself never learns what a
/// backlog row is.
/// </para>
/// </summary>
public sealed class InboxDesktopState
{
    /// <summary>The side menu's fixed row: the unfiled inbox. Every other row's id
    /// is a prefixed guid, so this word can never collide with one.</summary>
    public const string InboxSliceId = "inbox";

    /// <summary>The side menu's second fixed row: every deferred item, whatever
    /// list it is filed in. Not a list — deferral is a lifecycle state, and a
    /// deferred item keeps its list for when it comes back.</summary>
    public const string DeferredSliceId = "deferred";

    private const string ListPrefix = "list:";
    private const string GroupPrefix = "group:";

    /// <summary>The name a list or group is born with. It opens in a rename
    /// field, so the word rarely survives; it is here so a second one can be
    /// numbered rather than refused as a duplicate.</summary>
    private const string NewListName = "New list";
    private const string NewGroupName = "New group";

    private readonly IInboxItems _inbox;
    private readonly GitHubSettingsStore _gitHubSettings;
    private readonly IBacklogTagSource _backlogTags;
    private readonly IToastChannel? _toasts;
    private readonly TimeProvider _clock;

    private readonly HashSet<string> _kindFilter = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Which groups are folded shut. Open is the default, so the set
    /// holds the exceptions — the same rule the old drawers kept.</summary>
    private readonly HashSet<Guid> _collapsedGroups = [];

    /// <summary>The number of the latest reload asked for. A read that comes
    /// back carrying an older number is stale and is dropped — see
    /// <see cref="ReloadAsync"/>. Touched only on the renderer's thread, so a
    /// plain increment is enough.</summary>
    private int _reloadVersion;

    public InboxDesktopState(
        IInboxItems inbox,
        GitHubSettingsStore gitHubSettings,
        IToastChannel? toasts = null,
        TimeProvider? clock = null,
        IBacklogTagSource? backlogTags = null)
    {
        _inbox = inbox;
        _gitHubSettings = gitHubSettings;
        _backlogTags = backlogTags ?? EmptyBacklogTagSource.Instance;
        _toasts = toasts;
        _clock = clock ?? TimeProvider.System;
    }

    /// <summary>Raised whenever anything the pane draws has changed.</summary>
    public event Action? Changed;

    /// <summary>Raised after an item became backlog entries — by "Move to
    /// backlog" or by "Create plan". The shell listens and refreshes the Tasks
    /// pane; nothing in this project does.</summary>
    public event Action<InboxRoutedDto>? Routed;

    // --- The snapshot -------------------------------------------------------

    /// <summary>Every item the module knows, archived included. Which of them a
    /// screen shows is decided below.</summary>
    public IReadOnlyList<InboxItemDto> Items { get; private set; } = [];

    public IReadOnlyList<InboxListDto> Lists { get; private set; } = [];

    public IReadOnlyList<InboxGroupDto> Groups { get; private set; } = [];

    /// <summary>The tags the backlog already uses, read through the port with
    /// every snapshot so the picker offers what an entry was tagged with after
    /// the pane opened. Bare words, general tags only — what the port promises.</summary>
    public IReadOnlyList<string> BacklogTags { get; private set; } = [];

    /// <summary>Whether the first snapshot has arrived. Before it the pane has
    /// nothing to say and says nothing, rather than "Nothing captured yet" for
    /// the half-second the store takes.</summary>
    public bool Loaded { get; private set; }

    // --- View state ---------------------------------------------------------

    /// <summary>The list whose rows are on screen, or null for the unfiled
    /// inbox. Null too while <see cref="DeferredSelected"/>, which spans lists.</summary>
    public Guid? SelectedListId { get; private set; }

    /// <summary>Whether the Deferred slice is open.</summary>
    public bool DeferredSelected { get; private set; }

    /// <summary>The selected slice as the side menu names it.</summary>
    public string SelectedSliceId =>
        DeferredSelected ? DeferredSliceId
        : SelectedListId is { } id ? ListNavId(id)
        : InboxSliceId;

    /// <summary>What the open slice is called, for the list's accessible name
    /// and its empty state.</summary>
    public string SliceName =>
        DeferredSelected ? "Deferred"
        : SelectedListId is { } id && FindList(id) is { } list ? list.Name
        : "Inbox";

    public Guid? SelectedItemId { get; private set; }

    /// <summary>The chosen item, looked up in every item rather than the visible
    /// rows on purpose: an item that was just archived or routed has left the
    /// rows, and the detail still owes the reader the line that says so.</summary>
    public InboxItemDto? SelectedItem =>
        SelectedItemId is { } id ? Items.FirstOrDefault(item => item.Id == id) : null;

    /// <summary>The kind slugs filtered in. Empty means every kind.</summary>
    public IReadOnlySet<string> KindFilter => _kindFilter;

    /// <summary>The side-menu row whose name is a field right now, in the menu's
    /// own id vocabulary, or null.</summary>
    public string? EditingId { get; private set; }

    /// <summary>"Create plan" is in flight. The button shows a spinner and the
    /// other acts wait, because a second click would draft a second plan.</summary>
    public bool PlanRunning { get; private set; }

    /// <summary>"Move to backlog" is in flight.</summary>
    public bool RouteRunning { get; private set; }

    public (bool Available, string? Reason) PlanDrafterAvailability => _inbox.PlanDrafterAvailability;

    // --- Derived ------------------------------------------------------------

    /// <summary>The rows of the open slice before the kind filter.
    /// <para>
    /// A list or the inbox: everything filed there that is neither archived nor
    /// deferred, newest capture first. Routed items stay — the reader sees where
    /// a thing went from the row it was on — and archived ones go, because
    /// archived is the terminal state and a slice full of what was dismissed
    /// would not be a queue. Deferred ones go to their own slice for the same
    /// reason: put aside is the opposite of waiting.
    /// </para>
    /// <para>
    /// The Deferred slice: every deferred item in every list, the soonest review
    /// date first and the undated ones last — the order they will come back in.
    /// </para></summary>
    public IReadOnlyList<InboxItemDto> SliceItems =>
        DeferredSelected
            ? [.. Items
                .Where(item => item.Status == InboxStatus.Deferred)
                .OrderBy(item => item.DeferredUntil is null)
                .ThenBy(item => item.DeferredUntil)
                .ThenByDescending(item => item.CapturedAt)]
            : [.. Items
                .Where(item => item.Status is not (InboxStatus.Archived or InboxStatus.Deferred))
                .Where(item => item.ListId == SelectedListId)
                .OrderByDescending(item => item.CapturedAt)];

    /// <summary>The rows on screen: the slice, then the kind filter.</summary>
    public IReadOnlyList<InboxItemDto> VisibleItems =>
        _kindFilter.Count == 0
            ? SliceItems
            : [.. SliceItems.Where(item => _kindFilter.Contains(item.KindSlug))];

    /// <summary>One chip per kind present in the slice, in the library's kind
    /// order with any kind this build does not know after them, each with how
    /// many rows it would keep. Counted before the filter, so pressing a chip
    /// does not change the number on the one beside it.</summary>
    public IReadOnlyList<InboxKindCount> KindCounts
    {
        get
        {
            var counts = SliceItems
                .GroupBy(item => item.KindSlug, StringComparer.OrdinalIgnoreCase)
                .Select(group => new InboxKindCount(group.Key, CaptureKinds.Label(group.Key), group.Count()))
                .ToList();

            return
            [
                .. counts
                    .OrderBy(count => KindOrder(count.Slug))
                    .ThenBy(count => count.Slug, StringComparer.OrdinalIgnoreCase)
            ];
        }
    }

    /// <summary>Whether the filter has emptied a slice that has rows.</summary>
    public bool FilteredOut => _kindFilter.Count > 0 && VisibleItems.Count == 0 && SliceItems.Count > 0;

    /// <summary>How many unfiled items are still waiting on a decision.</summary>
    public int InboxCount => Items.Count(item => IsWaiting(item) && item.ListId is null);

    /// <summary>How many items in a list are still waiting on a decision.
    /// Waiting rather than non-archived: the number on a To Do list is what is
    /// left to do, and a routed item is done as far as the inbox is concerned.
    /// A deferred one is counted on the Deferred row instead, never twice.</summary>
    public int ListCount(Guid listId) => Items.Count(item => IsWaiting(item) && item.ListId == listId);

    /// <summary>How many items are deferred, dated or not.</summary>
    public int DeferredCount => Items.Count(item => item.Status == InboxStatus.Deferred);

    public int GroupCount(Guid groupId) =>
        Lists.Where(list => list.GroupId == groupId).Sum(list => ListCount(list.Id));

    /// <summary>Whether the group's lists are showing.</summary>
    public bool IsGroupExpanded(Guid groupId) => !_collapsedGroups.Contains(groupId);

    /// <summary>The repositories an item can be routed to, as the settings
    /// screen has them: the <c>owner/name</c> id is the value the module stores
    /// and the alias is what a reader recognises. Read on every access rather
    /// than cached, so a repository added in Settings is offered at once.</summary>
    public IReadOnlyList<SelectorOption> RepositoryOptions =>
        [.. _gitHubSettings.Current.Repositories.Select(repository =>
            new SelectorOption(repository.FullName, repository.Alias))];

    /// <summary>Every tag in use across the inbox and the backlog, bare, so the
    /// picker offers what has been typed before on either side. Items of every
    /// status contribute: a tag on an archived item is still a word the reader
    /// uses. The inbox's own come first in the union, so where the two sides
    /// spell a word differently the spelling the inbox already carries is the
    /// one offered.</summary>
    public IReadOnlyList<SelectorOption> TagOptions =>
        [.. Items
            .SelectMany(item => item.Tags)
            .Select(tag => tag.Name)
            .Concat(BacklogTags)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .Select(name => new SelectorOption(name, name))];

    /// <summary>The alias a repository id is known by in Settings, or the id
    /// itself when Settings no longer lists it.</summary>
    public string RepositoryAlias(string repoId) =>
        _gitHubSettings.Current.Repositories
            .FirstOrDefault(repository => string.Equals(repository.FullName, repoId, StringComparison.OrdinalIgnoreCase))
            ?.Alias ?? repoId;

    /// <summary>The list an item is filed in, or null for the unfiled inbox.</summary>
    public InboxListDto? FindList(Guid listId) => Lists.FirstOrDefault(list => list.Id == listId);

    public InboxGroupDto? FindGroup(Guid groupId) => Groups.FirstOrDefault(group => group.Id == groupId);

    /// <summary>How long ago, in the short form a row has room for.</summary>
    public string Age(DateTimeOffset at) => Age(at, _clock.GetUtcNow());

    // --- Loading ------------------------------------------------------------

    /// <summary>Seeds the starter lists and groups if the organiser is empty,
    /// brings back whatever deferred item is due, then loads. Called by the shell
    /// when the pane first shows; idempotent.</summary>
    public async Task InitializeAsync()
    {
        await FollowRepositoryRenamesAsync();
        await _inbox.EnsureDefaultOrganizerAsync();
        await ResurfaceDueItemsAsync();
        await ReloadAsync();
    }

    /// <summary>Called by the shell each time the pane is shown again after
    /// being closed: the resurface sweep, then a reload. The sweep runs on open
    /// rather than on a timer because opening the pane is the moment a single
    /// reader could notice the difference.</summary>
    public async Task OpenedAsync()
    {
        await ResurfaceDueItemsAsync();
        await ReloadAsync();
    }

    /// <summary>Returns every deferred item whose review date has been reached
    /// to the queue, and says so on a toast when any did — an item appearing in
    /// the inbox with an old capture date would otherwise read as a glitch.</summary>
    private async Task ResurfaceDueItemsAsync()
    {
        var resurfaced = await _inbox.ResurfaceDueAsync();
        if (Report(resurfaced) || resurfaced.Value == 0) return;

        _toasts?.Publish(ToastMessage.Info(
            $"{Counted(resurfaced.Value, "deferred item is", "deferred items are")} back in the inbox.",
            ResurfacedTestId));
    }

    /// <summary>
    /// Re-points every item still filed against a coordinate the registry
    /// remembers renaming away — the Inbox's half of what the Tasks reconcile
    /// pass does for entries on every start.
    /// <para>
    /// Here because a rename applied on another install reaches this one as a
    /// record in the shared registry, not as a call into this module, and inbox
    /// items do not travel by the sync service at all: nothing but this pass
    /// would ever move them. Idempotent, like the rename it repeats — once no
    /// item names the old id, every run is a pure read — and a store that will
    /// not answer must not be the reason the inbox will not open, so a refusal
    /// is left for the next start rather than surfaced.
    /// </para>
    /// </summary>
    private async Task FollowRepositoryRenamesAsync()
    {
        foreach (var rename in _gitHubSettings.Current.Renames)
        {
            try
            {
                _ = await _inbox.RenameRepositoryAsync(rename.OldId, rename.NewId);
            }
            catch (Exception)
            {
                // Left for the next start, as the Tasks pass leaves its own.
            }
        }
    }

    /// <summary>Re-reads the snapshot and keeps whatever view state still makes
    /// sense against it: a selected item that has gone is deselected, a selected
    /// list that was deleted falls back to the inbox.
    /// <para>
    /// Reloads are not serialised — the pane, the shell after a sync, and
    /// every act on an item all ask, and nothing queues them — so the read is
    /// numbered on the way out and its answer is dropped on the way back if a
    /// later read has been asked for since. Without that, a slow read started
    /// first and finished last would put its older snapshot over the newer one
    /// already on screen, and a capture that had just arrived would vanish
    /// until the next reload.
    /// </para></summary>
    public async Task ReloadAsync()
    {
        var version = ++_reloadVersion;

        var snapshot = await _inbox.GetSnapshotAsync();
        // The backlog's tags travel with the snapshot, and are dropped with it
        // when a later reload has overtaken this one.
        var backlogTags = await _backlogTags.TagsInUseAsync();

        if (version != _reloadVersion) return;

        Items = snapshot.Items;
        Lists = snapshot.Lists;
        Groups = snapshot.Groups;
        BacklogTags = backlogTags;
        Loaded = true;

        if (SelectedListId is { } listId && FindList(listId) is null)
        {
            SelectedListId = null;
            ForgetSelection();
        }

        if (SelectedItemId is { } itemId && Items.All(item => item.Id != itemId))
        {
            SelectedItemId = null;
        }

        if (EditingId is { } editing && !RowExists(editing))
        {
            EditingId = null;
        }

        PruneSelection();

        Changed?.Invoke();
    }

    // --- Slices, rows and chips -----------------------------------------------

    /// <summary>Opens a side-menu row by its id. The kind filter is cleared on
    /// the way: a filter is a question about the slice it was asked in, and a
    /// list that happens to hold no video would otherwise open empty with no
    /// chip on screen to say why.</summary>
    public void SelectSlice(string navId)
    {
        Guid? listId = null;

        if (string.Equals(navId, DeferredSliceId, StringComparison.Ordinal))
        {
            if (DeferredSelected) return;

            SelectedListId = null;
            DeferredSelected = true;
            SelectedItemId = null;
            _kindFilter.Clear();
            ForgetSelection();
            Changed?.Invoke();
            return;
        }

        if (TryParseListId(navId, out var parsed))
        {
            listId = parsed;
        }
        else if (!string.Equals(navId, InboxSliceId, StringComparison.Ordinal))
        {
            return;
        }

        if (listId == SelectedListId && !DeferredSelected) return;

        SelectedListId = listId;
        DeferredSelected = false;
        SelectedItemId = null;
        _kindFilter.Clear();
        ForgetSelection();
        Changed?.Invoke();
    }

    public void SelectItem(Guid? id)
    {
        if (id == SelectedItemId) return;

        SelectedItemId = id;
        Changed?.Invoke();
    }

    /// <summary>Presses or releases a kind chip. Several may be pressed at once;
    /// the rows are the union.</summary>
    public void ToggleKind(string slug)
    {
        if (!_kindFilter.Add(slug)) _kindFilter.Remove(slug);
        PruneSelection();
        Changed?.Invoke();
    }

    public void ClearKindFilter()
    {
        if (_kindFilter.Count == 0) return;

        _kindFilter.Clear();
        PruneSelection();
        Changed?.Invoke();
    }

    public void ToggleGroup(Guid groupId)
    {
        if (!_collapsedGroups.Add(groupId)) _collapsedGroups.Remove(groupId);
        Changed?.Invoke();
    }

    // --- Capture and the item's own acts ------------------------------------

    /// <summary>Captures what was typed into the pane's Add dialog: a title and,
    /// optionally, notes that become the item's body. Returns whether it was
    /// kept; a refusal is a toast under <c>inbox-add-error</c>, because the
    /// dialog has closed by the time the answer arrives and there is nowhere in
    /// the pane for a one-off failure to sit. The new item is selected so the
    /// detail opens on it.</summary>
    public async Task<bool> CaptureAsync(InboxCapture capture)
    {
        ArgumentNullException.ThrowIfNull(capture);

        if (string.IsNullOrWhiteSpace(capture.Title)) return false;

        var captured = await _inbox.CaptureAsync(capture.Title.Trim(), capture.Notes);
        if (Report(captured, AddErrorTestId)) return false;

        // A capture lands in the unfiled inbox. Opening it there rather than in
        // whatever list was showing, so the row the reader just made is on
        // screen — a capture that vanished into another slice would read as lost.
        if (SelectedListId is not null || DeferredSelected) ForgetSelection();
        SelectedListId = null;
        DeferredSelected = false;
        _kindFilter.Clear();
        SelectedItemId = captured.Value.Id;
        await ReloadAsync();
        return true;
    }

    public async Task SetTagsAsync(IReadOnlyList<string> tags)
    {
        if (SelectedItem is not { } item) return;

        if (Report(await _inbox.SetTagsAsync(item.Id, tags))) return;

        await ReloadAsync();
    }

    public async Task AssignRepositoriesAsync(IReadOnlyList<string> repoIds)
    {
        if (SelectedItem is not { } item) return;

        if (Report(await _inbox.AssignRepositoriesAsync(item.Id, repoIds))) return;

        await ReloadAsync();
    }

    /// <summary>Files the selected item in a list, or back in the inbox with
    /// null. The item stays selected: it moved, the reader did not.</summary>
    public async Task MoveToListAsync(Guid? listId)
    {
        if (SelectedItem is not { } item) return;

        if (Report(await _inbox.MoveToListAsync(item.Id, listId))) return;

        await ReloadAsync();
    }

    public async Task ArchiveAsync()
    {
        if (SelectedItem is not { } item) return;

        if (Report(await _inbox.ArchiveAsync(item.Id))) return;

        await ReloadAsync();
    }

    // --- Attachments --------------------------------------------------------

    /// <summary>The largest picture drawn as a thumbnail. A bigger one is shown
    /// as a file row instead: a thumbnail travels to the page as a data URI, and
    /// in the web harness that is a render batch over the circuit.</summary>
    internal const long ThumbnailMaxBytes = 8L * 1024 * 1024;

    /// <summary>Thumbnails already read, by attachment id, as data URIs. Kept for
    /// the window's life: a file on this machine does not change under its id.</summary>
    private readonly Dictionary<Guid, string> _thumbnails = [];

    /// <summary>The file a Retry is running for, so its row can show it busy.</summary>
    public Guid? RetryingAttachmentId { get; private set; }

    /// <summary>Whether a picture on the item is drawn as a thumbnail rather than
    /// listed as a file: it is an image, it is on this machine, and it is small
    /// enough to draw.</summary>
    public static bool ShowsAsThumbnail(InboxAttachmentDto attachment) =>
        attachment.IsImage && attachment.Downloaded && attachment.SizeBytes <= ThumbnailMaxBytes;

    /// <summary>The thumbnail for one attachment, once <see cref="LoadThumbnailsAsync"/>
    /// has read it; null before, or when it could not be read.</summary>
    public string? Thumbnail(Guid attachmentId) => _thumbnails.GetValueOrDefault(attachmentId);

    /// <summary>Reads every thumbnail the item shows that is not read yet, and
    /// re-renders once when any arrived. A file that cannot be read is left out
    /// silently: its row still names it, and a toast per picture on every render
    /// would be noise about something the row already says.</summary>
    public async Task LoadThumbnailsAsync(InboxItemDto item)
    {
        var loaded = false;

        foreach (var attachment in item.Attachments.Where(ShowsAsThumbnail))
        {
            if (_thumbnails.ContainsKey(attachment.Id)) continue;

            var read = await _inbox.ReadAttachmentAsync(item.Id, attachment.Id);
            if (read.IsFailure) continue;

            _thumbnails[attachment.Id] = $"data:{attachment.ContentType};base64,{Convert.ToBase64String(read.Value)}";
            loaded = true;
        }

        if (loaded) Changed?.Invoke();
    }

    /// <summary>Opens one of the selected item's files with the machine's own
    /// application for it.</summary>
    public async Task OpenAttachmentAsync(Guid attachmentId)
    {
        if (SelectedItem is not { } item) return;

        Report(await _inbox.OpenAttachmentAsync(item.Id, attachmentId));
    }

    /// <summary>Fetches one of the selected item's files again, then reloads so
    /// the row shows the file or the new reason it failed.</summary>
    public async Task RetryAttachmentAsync(Guid attachmentId)
    {
        if (SelectedItem is not { } item || RetryingAttachmentId is not null) return;

        RetryingAttachmentId = attachmentId;
        Changed?.Invoke();

        try
        {
            Report(await _inbox.RetryAttachmentAsync(item.Id, attachmentId));
        }
        finally
        {
            RetryingAttachmentId = null;
        }

        await ReloadAsync();
    }

    /// <summary>Puts the selected item aside until <paramref name="until"/>, or
    /// with no date until the reader returns it; on a deferred item it changes
    /// the date. The item stays selected: it leaves the queue's rows, and the
    /// detail still owes the reader the line that says until when.</summary>
    public async Task DeferAsync(DateOnly? until)
    {
        if (SelectedItem is not { } item) return;

        if (Report(await _inbox.DeferAsync(item.Id, until))) return;

        await ReloadAsync();
    }

    /// <summary>Returns the selected deferred item to the queue now.</summary>
    public async Task ResurfaceAsync()
    {
        if (SelectedItem is not { } item) return;

        if (Report(await _inbox.ResurfaceAsync(item.Id))) return;

        await ReloadAsync();
    }

    /// <summary>Turns the selected item into backlog entries and tells the shell
    /// so. The item stays selected: its detail now says where it went.</summary>
    public async Task RouteToBacklogAsync()
    {
        if (SelectedItem is not { } item || RouteRunning || PlanRunning) return;

        RouteRunning = true;
        Changed?.Invoke();

        try
        {
            var routed = await _inbox.RouteToBacklogAsync(item.Id);
            if (Report(routed)) return;

            await ReloadAsync();
            Routed?.Invoke(routed.Value);
        }
        finally
        {
            RouteRunning = false;
            Changed?.Invoke();
        }
    }

    /// <summary>Asks the drafter for a plan about the selected item and imports
    /// it. Refused up front when the drafter is unavailable — the button is
    /// disabled then, but a keyboard can still reach a disabled act's handler
    /// through a stale render, and the module answers with the same code.</summary>
    public async Task CreatePlanAsync()
    {
        if (SelectedItem is not { } item || PlanRunning || RouteRunning) return;

        PlanRunning = true;
        Changed?.Invoke();

        try
        {
            var routed = await _inbox.CreatePlanAsync(item.Id);
            if (Report(routed)) return;

            await ReloadAsync();
            _toasts?.Publish(ToastMessage.Info(
                $"Planned {Counted(routed.Value.TaskIds.Count, "entry", "entries")} from \"{item.Title}\".",
                "inbox-plan-created"));
            Routed?.Invoke(routed.Value);
        }
        finally
        {
            PlanRunning = false;
            Changed?.Invoke();
        }
    }

    // --- Picking several items -------------------------------------------------
    //
    // A set beside SelectedItemId rather than an extension of it, on the terms
    // TasksDesktopState sets for its own: SelectedItemId is "which item am I
    // reading" and drives the detail, this is "which items am I about to act on"
    // and drives the bar. Held here rather than in the list component because
    // the shell re-mounts the pane when it moves slots, and a selection held in
    // the component would drop with it.
    //
    // Mode and selection are one state, enforced both ways as Tasks enforces it:
    // leaving the mode empties the selection, and a pick turns the mode on.

    private readonly HashSet<Guid> _selection = [];

    /// <summary>The last box pressed, and so where a Shift press measures its
    /// range from.</summary>
    private Guid? _selectionAnchor;

    /// <summary>Whether the rows carry a box, because the reader asked for them.</summary>
    public bool SelectionMode { get; private set; }

    public IReadOnlyCollection<Guid> SelectedIds => _selection;

    public int SelectionCount => _selection.Count;

    public bool IsPicked(Guid id) => _selection.Contains(id);

    /// <summary>The picked items in the order the list draws them, and a
    /// snapshot: a bulk act reloads, and the reload prunes the live set.</summary>
    public IReadOnlyList<InboxItemDto> SelectedItems =>
        [.. VisibleItems.Where(item => _selection.Contains(item.Id))];

    /// <summary>A bulk act is in flight; the bar's acts wait, because a second
    /// press would run the batch again over whatever the first left.</summary>
    public bool BulkRunning { get; private set; }

    /// <summary>Turns the boxes on, or off and empty.</summary>
    public void SetSelectionMode(bool on)
    {
        if (SelectionMode == on) return;

        SelectionMode = on;
        if (!on) ForgetSelection();

        Changed?.Invoke();
    }

    /// <summary>
    /// A row's box pressed. Plain, it is that row; with Shift and an anchor still
    /// in view, every row from the anchor to this one takes the state this box
    /// now reads as — a state rather than a flip, so the run comes out uniform.
    /// The anchor moves either way: extending twice means "from where I just got
    /// to". The same gesture <c>TaskListView</c> gives the Tasks list.
    /// </summary>
    public void TogglePicked(Guid id, bool picked, bool range)
    {
        var order = VisibleItems;
        var pressed = IndexOf(order, id);
        if (pressed < 0) return;

        var anchor = range && _selectionAnchor is { } previous ? IndexOf(order, previous) : -1;

        if (anchor >= 0)
        {
            for (var index = Math.Min(anchor, pressed); index <= Math.Max(anchor, pressed); index++)
            {
                if (picked) _selection.Add(order[index].Id);
                else _selection.Remove(order[index].Id);
            }
        }
        else if (picked)
        {
            _selection.Add(id);
        }
        else
        {
            _selection.Remove(id);
        }

        _selectionAnchor = id;
        if (_selection.Count > 0) SelectionMode = true;

        Changed?.Invoke();
    }

    /// <summary>Takes every row in view, or gives them all back. In view rather
    /// than in the slice: "select all" under a kind filter means what is shown,
    /// which is also what the bar's count is counting.</summary>
    public void SetSelectAllVisible(bool picked)
    {
        _selection.Clear();

        if (picked)
        {
            foreach (var item in VisibleItems) _selection.Add(item.Id);
            SelectionMode = true;
        }

        Changed?.Invoke();
    }

    /// <summary>Puts the whole thing down: the picked items and the boxes.</summary>
    public void ClearSelection()
    {
        SelectionMode = false;
        ForgetSelection();
        Changed?.Invoke();
    }

    /// <summary>Archives every picked item. A routed item is refused by the
    /// command and named, rather than quietly skipped.</summary>
    public Task<InboxBulkOutcome> BulkArchiveAsync() =>
        RunBulkAsync(
            "archived",
            refuse: _ => null,
            alreadyThere: item => item.Status == InboxStatus.Archived,
            apply: items => _inbox.ArchiveAsync(Ids(items)));

    /// <summary>Files every picked item in one list, or back in the unfiled
    /// inbox with null. They leave the slice, and the selection with it.</summary>
    public Task<InboxBulkOutcome> BulkMoveToListAsync(Guid? listId)
    {
        var name = listId is { } id ? FindList(id)?.Name ?? "the list" : "Inbox";

        return RunBulkAsync(
            $"moved to {name}",
            refuse: _ => null,
            alreadyThere: item => item.ListId == listId,
            apply: items => _inbox.MoveToListAsync(Ids(items), listId));
    }

    /// <summary>
    /// Adds tags to every picked item, keeping each item's own. Union rather
    /// than replace, for the reason Tasks gives: a bulk tag change that wrote the
    /// picked set would take every other tag off every item. So each item's
    /// target set is worked out here, from its own tags, and the batch hands the
    /// command one set per item.
    /// </summary>
    public Task<InboxBulkOutcome> BulkAddTagsAsync(IEnumerable<string> tags)
    {
        ArgumentNullException.ThrowIfNull(tags);

        var added = tags
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Select(tag => tag.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (added.Count == 0) return Task.FromResult(InboxBulkOutcome.Nothing);

        return RunBulkAsync(
            $"tagged {string.Join(", ", added)}",
            refuse: RefuseDecided,
            alreadyThere: item => added.All(tag => HasTag(item, tag)),
            apply: items => _inbox.SetTagsAsync(items.ToDictionary(
                item => item.Id,
                item => (IReadOnlyList<string>)[.. TagNames(item).Concat(added).Distinct(StringComparer.OrdinalIgnoreCase)])));
    }

    /// <summary>Takes one tag off every picked item, leaving the rest. An item
    /// that never had it is unchanged rather than rewritten.</summary>
    public Task<InboxBulkOutcome> BulkRemoveTagAsync(string tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return Task.FromResult(InboxBulkOutcome.Nothing);

        var removed = tag.Trim();

        return RunBulkAsync(
            $"no longer tagged {removed}",
            refuse: RefuseDecided,
            alreadyThere: item => !HasTag(item, removed),
            apply: items => _inbox.SetTagsAsync(items.ToDictionary(
                item => item.Id,
                item => (IReadOnlyList<string>)[.. TagNames(item).Where(name => !string.Equals(name, removed, StringComparison.OrdinalIgnoreCase))])));
    }

    /// <summary>Points every picked item at these repositories, replacing what
    /// each targeted before — "these all belong here now". Set-only, as in
    /// Tasks: an empty pick is a reader who has not chosen yet, not one asking
    /// to untarget the lot.</summary>
    public Task<InboxBulkOutcome> BulkAssignRepositoriesAsync(IReadOnlyList<string> repoIds)
    {
        ArgumentNullException.ThrowIfNull(repoIds);

        var chosen = repoIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (chosen.Count == 0) return Task.FromResult(InboxBulkOutcome.Nothing);

        return RunBulkAsync(
            $"pointed at {string.Join(", ", chosen.Select(RepositoryAlias))}",
            refuse: RefuseDecided,
            alreadyThere: item => item.RepoIds.Count == chosen.Count
                && chosen.All(id => item.RepoIds.Contains(id, StringComparer.OrdinalIgnoreCase)),
            apply: items => _inbox.AssignRepositoriesAsync(Ids(items), chosen));
    }

    /// <summary>The tags every picked item's union offers to take off: each tag
    /// carried by at least one picked item, once.</summary>
    public IReadOnlyList<string> SelectedTags =>
        [.. SelectedItems
            .SelectMany(TagNames)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)];

    /// <summary>
    /// One bulk act, end to end: sort the picked items into refused up front,
    /// already there, and to be written; hand the last to the module's batch;
    /// fold its refusals in; reload; and say what happened.
    /// <para>
    /// An item already at the value is not written, the skip-if-unchanged
    /// Tasks' bulk edit does: a save per item where nothing moved overstates the
    /// count. An item refused — here or by its command — is named in the
    /// sentence, so a batch that only partly landed never reads as a success.
    /// </para>
    /// </summary>
    private async Task<InboxBulkOutcome> RunBulkAsync(
        string did,
        Func<InboxItemDto, Error?> refuse,
        Func<InboxItemDto, bool> alreadyThere,
        Func<IReadOnlyList<InboxItemDto>, Task<InboxBatchResultDto>> apply)
    {
        var picked = SelectedItems;
        if (picked.Count == 0 || BulkRunning) return InboxBulkOutcome.Nothing;

        var refused = new Dictionary<Guid, Error>();
        var unchanged = 0;
        var pending = new List<InboxItemDto>();

        foreach (var item in picked)
        {
            if (refuse(item) is { } error) refused[item.Id] = error;
            else if (alreadyThere(item)) unchanged++;
            else pending.Add(item);
        }

        BulkRunning = true;
        Changed?.Invoke();

        try
        {
            var result = pending.Count == 0 ? InboxBatchResultDto.Nothing : await apply(pending);

            foreach (var failure in result.Failed) refused[failure.Id] = failure.Error;

            var failures = picked
                .Where(item => refused.ContainsKey(item.Id))
                .Select(item => new InboxBulkFailure(item.Id, ItemTitle(item), refused[item.Id]))
                .ToList();

            var outcome = new InboxBulkOutcome(result.Changed.Count, unchanged, failures);

            await ReloadAsync();

            var message = BulkMessage(outcome, did);
            _toasts?.Publish(failures.Count > 0
                ? ToastMessage.Warning(message, BulkResultTestId)
                : ToastMessage.Info(message, BulkResultTestId));

            return outcome;
        }
        finally
        {
            BulkRunning = false;
            Changed?.Invoke();
        }
    }

    /// <summary>
    /// What a bulk act did, in one sentence and then the names. The count of
    /// what landed leads, so it never goes missing behind a refusal; every item
    /// refused follows by title with its own reason, because "2 could not be
    /// saved" leaves the reader hunting for which two.
    /// </summary>
    internal static string BulkMessage(InboxBulkOutcome outcome, string did)
    {
        var clauses = new List<string>
        {
            outcome.Changed > 0 ? $"{Counted(outcome.Changed, "item", "items")} {did}" : "No items changed"
        };

        if (outcome.Unchanged > 0) clauses.Add($"{outcome.Unchanged} already up to date");

        var sentence = string.Join(", ", clauses) + ".";

        if (outcome.Failures.Count == 0) return sentence;

        var named = outcome.Failures.Select(failure => $"\"{failure.Title}\": {failure.Error.Message}");

        return $"{sentence} Not changed — {string.Join(" ", named)}";
    }

    private static Error? RefuseDecided(InboxItemDto item) => IsOpen(item) ? null : InboxErrors.ItemAlreadyDecided;

    private static IEnumerable<string> TagNames(InboxItemDto item) => item.Tags.Select(tag => tag.Name);

    private static bool HasTag(InboxItemDto item, string tag) =>
        item.Tags.Any(existing => string.Equals(existing.Name, tag, StringComparison.OrdinalIgnoreCase));

    private static IReadOnlyList<Guid> Ids(IReadOnlyList<InboxItemDto> items) => [.. items.Select(item => item.Id)];

    private static string ItemTitle(InboxItemDto item) =>
        string.IsNullOrWhiteSpace(item.Title) ? "Untitled" : item.Title;

    private static int IndexOf(IReadOnlyList<InboxItemDto> items, Guid id)
    {
        for (var i = 0; i < items.Count; i++)
        {
            if (items[i].Id == id) return i;
        }

        return -1;
    }

    /// <summary>Drops every pick that is no longer on screen — archived, moved
    /// away, filtered out — and keeps the rest. What a refresh does to the
    /// selection: it survives, less what left.</summary>
    private void PruneSelection()
    {
        if (_selection.Count == 0 && _selectionAnchor is null) return;

        var visible = VisibleItems.Select(item => item.Id).ToHashSet();

        _selection.IntersectWith(visible);
        if (_selectionAnchor is { } anchor && !visible.Contains(anchor)) _selectionAnchor = null;
    }

    /// <summary>Empties the picks without leaving the mode: the slice changed,
    /// and a pick made in another slice is not one the reader can see.</summary>
    private void ForgetSelection()
    {
        _selection.Clear();
        _selectionAnchor = null;
    }

    // --- Lists and groups ------------------------------------------------------

    /// <summary>Makes a list and opens its name for typing. Born with a
    /// numbered placeholder rather than an empty name, because the module
    /// refuses an empty one and a list that could not be created could not be
    /// renamed either.</summary>
    public async Task CreateListAsync(Guid? groupId)
    {
        var name = UniqueName(NewListName, Lists.Where(list => list.GroupId == groupId).Select(list => list.Name));

        var created = await _inbox.CreateListAsync(name, groupId);
        if (Report(created)) return;

        if (groupId is { } group) _collapsedGroups.Remove(group);

        ForgetSelection();
        SelectedListId = created.Value.Id;
        DeferredSelected = false;
        SelectedItemId = null;
        _kindFilter.Clear();
        EditingId = ListNavId(created.Value.Id);
        await ReloadAsync();
    }

    public async Task CreateGroupAsync()
    {
        var name = UniqueName(NewGroupName, Groups.Select(group => group.Name));

        var created = await _inbox.CreateGroupAsync(name);
        if (Report(created)) return;

        EditingId = GroupNavId(created.Value.Id);
        await ReloadAsync();
    }

    /// <summary>Opens the rename field on a side-menu row. The inbox row has no
    /// name to change, so asking on it does nothing.</summary>
    public void BeginRename(string navId)
    {
        if (!RowExists(navId)) return;

        EditingId = navId;
        Changed?.Invoke();
    }

    public void CancelRename()
    {
        if (EditingId is null) return;

        EditingId = null;
        Changed?.Invoke();
    }

    /// <summary>Applies a settled rename. On a refusal — a duplicate, say — the
    /// field stays open with the reason on a toast, so the reader can try
    /// another name rather than start over.</summary>
    public async Task CommitRenameAsync(string navId, string name)
    {
        Result result;

        if (TryParseListId(navId, out var listId))
        {
            result = await _inbox.RenameListAsync(listId, name);
        }
        else if (TryParseGroupId(navId, out var groupId))
        {
            result = await _inbox.RenameGroupAsync(groupId, name);
        }
        else
        {
            return;
        }

        if (Report(result)) return;

        EditingId = null;
        await ReloadAsync();
    }

    /// <summary>Deletes a list; its items return to the inbox. Confirmation is
    /// the pane's business — this is the act after the question.</summary>
    public async Task DeleteListAsync(Guid listId)
    {
        if (Report(await _inbox.DeleteListAsync(listId))) return;

        await ReloadAsync();
    }

    public async Task MoveListToGroupAsync(Guid listId, Guid? groupId)
    {
        if (Report(await _inbox.MoveListToGroupAsync(listId, groupId))) return;

        if (groupId is { } group) _collapsedGroups.Remove(group);

        await ReloadAsync();
    }

    /// <summary>Dissolves a group; its lists move to the top level.</summary>
    public async Task UngroupAsync(Guid groupId)
    {
        if (Report(await _inbox.UngroupAsync(groupId))) return;

        _collapsedGroups.Remove(groupId);
        await ReloadAsync();
    }

    // --- Side-menu ids -------------------------------------------------------

    /// <summary>The side menu addresses lists and groups by one string id, so
    /// the two are prefixed to keep a list and a group with the same guid — which
    /// cannot happen, and is guarded against anyway — apart.</summary>
    public static string ListNavId(Guid listId) => ListPrefix + listId.ToString("D");

    public static string GroupNavId(Guid groupId) => GroupPrefix + groupId.ToString("D");

    public static bool TryParseListId(string? navId, out Guid listId) => TryParse(navId, ListPrefix, out listId);

    public static bool TryParseGroupId(string? navId, out Guid groupId) => TryParse(navId, GroupPrefix, out groupId);

    /// <summary>The relative age, pure so a test can pin the wording.</summary>
    internal static string Age(DateTimeOffset at, DateTimeOffset now)
    {
        var elapsed = now - at;

        if (elapsed < TimeSpan.FromMinutes(1)) return "just now";
        if (elapsed < TimeSpan.FromHours(1)) return $"{(int)elapsed.TotalMinutes}m ago";
        if (elapsed < TimeSpan.FromDays(1)) return $"{(int)elapsed.TotalHours}h ago";
        if (elapsed < TimeSpan.FromDays(30)) return $"{(int)elapsed.TotalDays}d ago";

        return at.ToLocalTime().ToString("d MMM yyyy", System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>"1 entry", "3 entries".</summary>
    internal static string Counted(int count, string singular, string plural) =>
        count == 1 ? $"1 {singular}" : $"{count} {plural}";

    // --- Internals -------------------------------------------------------------

    /// <summary>Still open to a decision: unprocessed or deferred. A deferred
    /// item can still be tagged, filed, archived or routed.</summary>
    private static bool IsOpen(InboxItemDto item) => item.Status is InboxStatus.Unprocessed or InboxStatus.Deferred;

    /// <summary>Still in the queue: not routed, not archived, not put aside.</summary>
    private static bool IsWaiting(InboxItemDto item) =>
        item.Routing is null && item.Status is InboxStatus.Unprocessed or InboxStatus.Triaged;

    private static int KindOrder(string slug)
    {
        for (var i = 0; i < CaptureKinds.All.Count; i++)
        {
            if (string.Equals(CaptureKinds.All[i], slug, StringComparison.OrdinalIgnoreCase)) return i;
        }

        return CaptureKinds.All.Count;
    }

    private bool RowExists(string navId) =>
        (TryParseListId(navId, out var listId) && FindList(listId) is not null)
        || (TryParseGroupId(navId, out var groupId) && FindGroup(groupId) is not null);

    private static bool TryParse(string? navId, string prefix, out Guid id)
    {
        id = Guid.Empty;

        return navId is not null
            && navId.StartsWith(prefix, StringComparison.Ordinal)
            && Guid.TryParseExact(navId.AsSpan(prefix.Length), "D", out id);
    }

    /// <summary>The base name, or the base name with the first number that is
    /// not already taken beside it — compared the way the module compares.</summary>
    private static string UniqueName(string baseName, IEnumerable<string> taken)
    {
        var names = new HashSet<string>(taken, StringComparer.OrdinalIgnoreCase);
        if (!names.Contains(baseName)) return baseName;

        for (var suffix = 2; ; suffix++)
        {
            var candidate = $"{baseName} {suffix}";
            if (!names.Contains(candidate)) return candidate;
        }
    }

    /// <summary>The toast an item act's refusal lands on, and the one the Add
    /// dialog's refusal lands on. Two ids because a test — and a driver — needs
    /// to tell "the add failed" from "the archive failed" without reading the
    /// sentence.</summary>
    private const string ErrorTestId = "inbox-error";
    private const string AddErrorTestId = "inbox-add-error";

    /// <summary>The toast the resurface sweep leaves when it brought items back.</summary>
    internal const string ResurfacedTestId = "inbox-resurfaced";

    /// <summary>The toast a bulk act's sentence lands on, success or not.</summary>
    public const string BulkResultTestId = "inbox-bulk-result";

    /// <summary>Puts a refused result on a toast and says whether it was one.
    /// Action-level feedback per the interaction guidelines: an error toast, and
    /// the control that failed is left as it was so the reader can try again.</summary>
    private bool Report(Result result, string testId = ErrorTestId)
    {
        if (result.IsSuccess) return false;

        _toasts?.Publish(ToastMessage.Error(result.Error.Message, testId));
        return true;
    }
}

/// <summary>One kind chip: the slug the marker draws, the word beside it, and
/// how many rows of the slice it stands for.</summary>
public sealed record InboxKindCount(string Slug, string Label, int Count);

/// <summary>
/// What a bulk act across the picked items did: how many it changed, how many
/// were already at the value and left alone, and which it could not change.
/// Three answers, as Tasks' <c>BulkEditOutcome</c> gives — the Inbox keeps its
/// own because its pane may not reach into Tasks'.
/// </summary>
public sealed record InboxBulkOutcome(int Changed, int Unchanged, IReadOnlyList<InboxBulkFailure> Failures)
{
    public static readonly InboxBulkOutcome Nothing = new(0, 0, []);

    public int Total => Changed + Unchanged + Failures.Count;
}

/// <summary>One item a bulk act could not change: which, what it is called, and
/// why.</summary>
public readonly record struct InboxBulkFailure(Guid Id, string Title, Error Error);
