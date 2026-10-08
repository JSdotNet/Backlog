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

    /// <summary>Raised once after a batch became backlog entries — by "Move to
    /// backlog" across a selection or "Move list to backlog" — rather than once
    /// per item, so the shell refreshes the Tasks pane once for the lot. Not
    /// raised when nothing was routed.</summary>
    public event Action<InboxBatchRoutedDto>? BatchRouted;

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

    /// <summary>How long an item may wait unprocessed before the queue health
    /// bar calls it out (features.md#queue-health-bar).</summary>
    public const int StaleAfterDays = 14;

    /// <summary>The first part of the queue health bar: items captured fewer
    /// than this many days ago are fresh.</summary>
    public const int FreshUnderDays = 3;

    /// <summary>
    /// The queue health bar's numbers, over every unprocessed item in every
    /// list: how many, how long the oldest has waited, and how they split by
    /// age — under <see cref="FreshUnderDays"/>, from there up to
    /// <see cref="StaleAfterDays"/>, and longer than that. Deferred items are
    /// left out — put aside is not waiting — and age runs from the capture, the
    /// same instant each row's own age is read from.
    /// </summary>
    public InboxQueueHealth QueueHealth
    {
        get
        {
            var waiting = Items.Where(item => item.Status == InboxStatus.Unprocessed).ToList();
            if (waiting.Count == 0) return InboxQueueHealth.Clear;

            var now = _clock.GetUtcNow();
            var oldest = waiting.Min(item => item.CapturedAt);
            var stale = waiting.Count(item => now - item.CapturedAt > TimeSpan.FromDays(StaleAfterDays));
            var fresh = waiting.Count(item => now - item.CapturedAt < TimeSpan.FromDays(FreshUnderDays));

            return new InboxQueueHealth(waiting.Count, oldest, stale)
            {
                Fresh = fresh,
                Aging = waiting.Count - fresh - stale,
            };
        }
    }

    /// <summary>The header's short form of the queue's health: "3 waiting ·
    /// oldest 12 days", or "Nothing waiting".</summary>
    public string WaitingSummary
    {
        get
        {
            var health = QueueHealth;
            return health.OldestCapturedAt is { } oldest
                ? $"{health.Unprocessed} waiting · oldest {Waited(oldest)}"
                : "Nothing waiting";
        }
    }

    /// <summary>How long something captured at <paramref name="at"/> has
    /// waited, in words: "under an hour", "5 hours", "12 days".</summary>
    public string Waited(DateTimeOffset at) => Waited(_clock.GetUtcNow() - at);

    /// <summary>The span in words, pure so a test can pin the wording.</summary>
    internal static string Waited(TimeSpan span) => span switch
    {
        _ when span < TimeSpan.FromHours(1) => "under an hour",
        _ when span < TimeSpan.FromDays(1) => Counted((int)span.TotalHours, "hour", "hours"),
        _ => Counted((int)span.TotalDays, "day", "days"),
    };

    /// <summary>
    /// The rows on screen in the groups the list draws them under: Today (since
    /// local midnight), This week (the seven days before now) and Older than a
    /// week, each in the rows' own order and none that would be empty. The
    /// Deferred slice is one group: its rows run in the order they come back,
    /// and splitting them by capture would scramble that.
    /// </summary>
    public IReadOnlyList<InboxAgeGroup> VisibleGroups
    {
        get
        {
            var rows = VisibleItems;
            if (rows.Count == 0) return [];
            if (DeferredSelected) return [new InboxAgeGroup(AllGroupKey, "Deferred", rows)];

            // Midnight at the offset in force at midnight, not now: on the day the
            // clocks change the two differ by an hour.
            var local = _clock.GetLocalNow();
            var midnight = new DateTimeOffset(local.Date, _clock.LocalTimeZone.GetUtcOffset(local.Date));
            var weekAgo = local.AddDays(-7);

            var groups = new List<InboxAgeGroup>(3);
            Add(TodayGroupKey, "Today", rows.Where(item => item.CapturedAt >= midnight));
            Add(WeekGroupKey, "This week", rows.Where(item => item.CapturedAt < midnight && item.CapturedAt >= weekAgo));
            Add(OlderGroupKey, "Older than a week", rows.Where(item => item.CapturedAt < weekAgo));
            return groups;

            void Add(string key, string label, IEnumerable<InboxItemDto> items)
            {
                var list = items.ToList();
                if (list.Count > 0) groups.Add(new InboxAgeGroup(key, label, list));
            }
        }
    }

    internal const string TodayGroupKey = "today";
    internal const string WeekGroupKey = "week";
    internal const string OlderGroupKey = "older";
    internal const string AllGroupKey = "all";

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

        // Whatever the item now carries — a tag just accepted, a suggestion just
        // turned down, a rule the backlog's tags now meet — the suggestions are
        // asked for again on the next render rather than trusted from before.
        // So are the relations: any change may be the one that made or broke one.
        _suggestionsFor = null;
        _relationsFor = null;

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
        RememberPosition();
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

    /// <summary>Captures what was typed into the pane's capture field: a title
    /// and, optionally, notes that become the item's body. Returns whether it
    /// was kept; a refusal is a toast under <c>inbox-add-error</c>, because the
    /// field is a line in the header and there is nowhere in it for a one-off
    /// failure to sit. The new item is selected so the detail opens on it.</summary>
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
    /// null. The item stays selected: it moved, the reader did not — except in
    /// triage mode, where every decision moves on to the next item.</summary>
    public Task MoveToListAsync(Guid? listId) =>
        DecideAsync(item => _inbox.MoveToListAsync(item.Id, listId));

    public Task ArchiveAsync() =>
        DecideAsync(item => _inbox.ArchiveAsync(item.Id));

    /// <summary>Deletes the selected item for good. The detail confirms first;
    /// the reload then finds the item gone and clears the selection.</summary>
    public async Task DeleteAsync()
    {
        if (SelectedItem is not { } item) return;

        if (Report(await _inbox.DeleteAsync(item.Id))) return;

        await ReloadAsync();
    }

    // --- Suggestions ----------------------------------------------------------
    //
    // Classification's proposals for the item on screen. Asked of the module
    // rather than worked out here — which tags the backlog uses, which rules the
    // reader wrote and which suggestions they turned down are all the module's —
    // and applied only through the act each one names, when the reader takes it.

    /// <summary>The item the suggestions on screen were asked for, as the DTO
    /// instance it was then; a reload hands over new instances and clears this,
    /// so the next render asks again.</summary>
    private InboxItemDto? _suggestionsFor;

    /// <summary>What Classification proposes for the selected item, in the order
    /// the chips are drawn. Empty for no item, a decided item, or before the
    /// first answer.</summary>
    public IReadOnlyList<InboxSuggestionDto> Suggestions { get; private set; } = [];

    /// <summary>The suggestions a key takes, in order: the ones that can be taken
    /// at all, up to nine, so the digit on each chip is its position here.</summary>
    public IReadOnlyList<InboxSuggestionDto> ShortcutSuggestions =>
        [.. Suggestions.Where(suggestion => suggestion.Available).Take(9)];

    /// <summary>The digit that takes <paramref name="suggestion"/>, or null for
    /// one no key takes.</summary>
    public string? ShortcutFor(InboxSuggestionDto suggestion)
    {
        var index = ShortcutSuggestions.ToList().IndexOf(suggestion);
        return index < 0 ? null : (index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>Asks for the selected item's suggestions when the ones on screen
    /// were asked for another item, or before the last reload. Called on every
    /// render of the detail, and a no-op when nothing changed. A failure leaves
    /// no chips rather than a toast: a suggestion is an offer, and an offer that
    /// could not be made is not an error the reader has to deal with.</summary>
    public async Task LoadSuggestionsAsync()
    {
        var item = SelectedItem;
        if (ReferenceEquals(item, _suggestionsFor)) return;

        _suggestionsFor = item;

        if (item is null || item.Status is not (InboxStatus.Unprocessed or InboxStatus.Deferred))
        {
            if (Suggestions.Count == 0) return;

            Suggestions = [];
            Changed?.Invoke();
            return;
        }

        var answer = await _inbox.SuggestAsync(item.Id);

        // Another item was chosen, or a reload landed, while this was asked.
        if (!ReferenceEquals(item, _suggestionsFor)) return;

        Suggestions = answer.IsSuccess ? answer.Value : [];
        Changed?.Invoke();
    }

    /// <summary>
    /// Takes a suggestion through the act it names: a tag is added to the item's
    /// tags, a repository to its repositories, and a destination decides the item
    /// — the backlog routes it, archive archives it. A suggestion that cannot be
    /// taken is left alone; the chip says why.
    /// </summary>
    public async Task AcceptSuggestionAsync(InboxSuggestionDto suggestion)
    {
        ArgumentNullException.ThrowIfNull(suggestion);

        if (SelectedItem is not { } item || !suggestion.Available || RoutingInFlight) return;

        switch (suggestion.Kind)
        {
            case InboxSuggestionKind.Tag:
                await SetTagsAsync([.. item.Tags.Select(tag => tag.Name), suggestion.Value]);
                break;

            case InboxSuggestionKind.Repository:
                await AssignRepositoriesAsync([.. item.RepoIds, suggestion.Value]);
                break;

            case InboxSuggestionKind.Destination when suggestion.Value == InboxEnumMap.ToWire(RoutingDomain.Tasks):
                await RouteToBacklogAsync();
                break;

            case InboxSuggestionKind.Destination when suggestion.Value == InboxEnumMap.ToWire(RoutingDomain.Archive):
                await ArchiveAsync();
                break;
        }
    }

    /// <summary>Takes the suggestion whose chip shows <paramref name="key"/> — a
    /// digit from 1 to 9 — and answers whether one did. Any other key, or a
    /// digit with no chip, is left to whatever else wants it.</summary>
    public async Task<bool> AcceptShortcutAsync(string? key)
    {
        if (key is not { Length: 1 } || key[0] is < '1' or > '9') return false;

        var index = key[0] - '1';
        var shortcuts = ShortcutSuggestions;
        if (index >= shortcuts.Count) return false;

        await AcceptSuggestionAsync(shortcuts[index]);
        return true;
    }

    /// <summary>Turns a suggestion down for the selected item. The module records
    /// it, so it is not offered for this item again — after a reload, a restart
    /// or another reason to propose it.</summary>
    public async Task DismissSuggestionAsync(InboxSuggestionDto suggestion)
    {
        ArgumentNullException.ThrowIfNull(suggestion);

        if (SelectedItem is not { } item) return;

        if (Report(await _inbox.DismissSuggestionAsync(item.Id, suggestion.Key))) return;

        // Gone from the row at once rather than after the reload's round trip,
        // so a second press of the same digit cannot reach a chip already refused.
        Suggestions = [.. Suggestions.Where(other => other.Key != suggestion.Key)];
        await ReloadAsync();
    }

    // --- The rows' "suggested" marker ------------------------------------------

    /// <summary>Per row on screen: the reload it was asked after, and whether
    /// Classification had anything to propose for it. Any reload makes every
    /// answer stale — a tag just added, a suggestion just turned down, a rule
    /// the backlog's tags now meet — for the reason <see cref="_suggestionsFor"/>
    /// is cleared by one; the old answer stays on screen until the new one is
    /// in, so the marker does not flicker.</summary>
    private readonly Dictionary<Guid, (int Reload, bool Has)> _rowSuggestions = [];

    private bool _rowSuggestionsLoading;

    /// <summary>Whether the row carries the "suggested" marker: the item is
    /// still open and has a rule-based suggestion the reader has not turned
    /// down.</summary>
    public bool HasSuggestions(Guid itemId) =>
        _rowSuggestions.TryGetValue(itemId, out var entry) && entry.Has;

    /// <summary>Asks, in one batch, for the suggestions of every open row on
    /// screen whose answer is missing or older than the last reload, and raises
    /// <see cref="Changed"/> once if any marker moved. Called whenever the list's
    /// parameters are set, and a no-op when nothing is stale. One pass at a
    /// time: a reload that lands mid-pass is caught by the pass going round
    /// again. Only a suggestion that can be taken counts — the marker promises
    /// something the detail lets the reader do. A failure, or a throw from the
    /// store, counts as nothing to suggest, as it does for the detail's chips:
    /// a marker is an offer, never an error.</summary>
    public async Task LoadRowSuggestionsAsync()
    {
        if (_rowSuggestionsLoading) return;

        _rowSuggestionsLoading = true;
        var moved = false;

        try
        {
            while (VisibleItems.Where(IsRowSuggestionStale).Select(item => item.Id).ToList() is { Count: > 0 } stale)
            {
                var version = _reloadVersion;
                IReadOnlyDictionary<Guid, IReadOnlyList<InboxSuggestionDto>> answers;
                try
                {
                    var answer = await _inbox.SuggestManyAsync(stale);
                    answers = answer.IsSuccess ? answer.Value : new Dictionary<Guid, IReadOnlyList<InboxSuggestionDto>>();
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    answers = new Dictionary<Guid, IReadOnlyList<InboxSuggestionDto>>();
                }

                foreach (var id in stale)
                {
                    var has = answers.TryGetValue(id, out var offered) && offered.Any(suggestion => suggestion.Available);
                    moved |= !_rowSuggestions.TryGetValue(id, out var before) || before.Has != has;
                    _rowSuggestions[id] = (version, has);
                }
            }

            // Rows that have left the snapshot — archived away, deleted — are
            // forgotten rather than carried for the session.
            foreach (var gone in _rowSuggestions.Keys.Where(id => Items.All(item => item.Id != id)).ToList())
            {
                _rowSuggestions.Remove(gone);
            }
        }
        finally
        {
            _rowSuggestionsLoading = false;
        }

        if (moved) Changed?.Invoke();
    }

    private bool IsRowSuggestionStale(InboxItemDto item)
    {
        if (item.Status is not (InboxStatus.Unprocessed or InboxStatus.Deferred)) return false;

        return !_rowSuggestions.TryGetValue(item.Id, out var entry) || entry.Reload != _reloadVersion;
    }

    // --- Relations ------------------------------------------------------------
    //
    // What the item on screen already has to do with the rest of the backlog —
    // other items that look like the same capture, tasks that already carry it.
    // Asked of the module, which holds the join through its task port, and read
    // the way the suggestions are: once per item, again after any reload. The
    // Inbox never acts on one; the two acts below are the person's.

    /// <summary>The item the relations on screen were asked for, as the DTO
    /// instance it was then — cleared by a reload, like the suggestions'.</summary>
    private InboxItemDto? _relationsFor;

    /// <summary>What the selected item relates to, or null before the first
    /// answer or when it could not be read. Its <see cref="InboxRelationsDto.ItemId"/>
    /// says which item it is for, so a render between two items can tell.</summary>
    public InboxRelationsDto? Relations { get; private set; }

    /// <summary>Asks for the selected item's relations when the ones on screen
    /// were asked for another item, or before the last reload. Called on every
    /// render of the detail and a no-op when nothing changed. A failure leaves
    /// none rather than a toast, for the reason a suggestion's does: a relation
    /// is information, and one that could not be read is not an error to deal
    /// with.</summary>
    public async Task LoadRelationsAsync()
    {
        var item = SelectedItem;
        if (ReferenceEquals(item, _relationsFor)) return;

        _relationsFor = item;

        if (item is null)
        {
            if (Relations is null) return;

            Relations = null;
            Changed?.Invoke();
            return;
        }

        var answer = await _inbox.RelatedAsync(item.Id);

        // Another item was chosen, or a reload landed, while this was asked.
        if (!ReferenceEquals(item, _relationsFor)) return;

        Relations = answer.IsSuccess ? answer.Value : null;
        Changed?.Invoke();
    }

    /// <summary>The relations of <paramref name="item"/>, when the ones on
    /// screen are its; otherwise null.</summary>
    public InboxRelationsDto? RelationsOf(InboxItemDto item) =>
        Relations is { } relations && item is not null && relations.ItemId == item.Id ? relations : null;

    /// <summary>What "Archive as duplicate of…" offers for the selected item:
    /// the related items first, in the order they relate, then every other item,
    /// newest capture first — narrowed to the titles that hold
    /// <paramref name="query"/>, without regard to case. Never the item itself,
    /// and never an item already archived as a duplicate of it, directly or down
    /// a chain: the module refuses those as circular, so they are not offered.</summary>
    public IReadOnlyList<InboxItemDto> DuplicateCandidates(string? query)
    {
        if (SelectedItem is not { } item) return [];

        var byId = Items.ToDictionary(other => other.Id);
        var related = (RelationsOf(item)?.Items ?? [])
            .Select(relation => byId.GetValueOrDefault(relation.Id))
            .OfType<InboxItemDto>();
        var rest = Items.OrderByDescending(other => other.CapturedAt);

        return [.. related.Concat(rest)
            .Where(other => other.Id != item.Id && !LeadsTo(other, item.Id, byId))
            .DistinctBy(other => other.Id)
            .Where(other => Matches(other.Title, query))];
    }

    /// <summary>Whether <paramref name="other"/>'s duplicate chain reaches
    /// <paramref name="itemId"/>, as far as the items on screen show it.</summary>
    private static bool LeadsTo(InboxItemDto other, Guid itemId, Dictionary<Guid, InboxItemDto> byId)
    {
        var seen = new HashSet<Guid>();

        for (var next = other.DuplicateOf; next is { } id && seen.Add(id); next = byId.GetValueOrDefault(id)?.DuplicateOf)
        {
            if (id == itemId) return true;
        }

        return false;
    }

    /// <summary>The reason an item relates to the selected one, or null for
    /// one that does not — the note a picker row carries.</summary>
    public string? RelationReason(Guid otherId) =>
        SelectedItem is { } item ? RelationsOf(item)?.Items.FirstOrDefault(relation => relation.Id == otherId)?.Reason : null;

    /// <summary>What "Link to task…" offers for the selected item: the related
    /// tasks first, each with its reason, then the backlog's open tasks, each
    /// once — narrowed to the titles that hold <paramref name="query"/>.</summary>
    public IReadOnlyList<InboxTaskChoice> LinkCandidates(string? query)
    {
        if (SelectedItem is not { } item || RelationsOf(item) is not { } relations) return [];

        return [.. relations.Tasks
            .Select(task => new InboxTaskChoice(task.Id, task.Title, task.Reason, task.IsOpen))
            .Concat(relations.OpenTasks.Select(task => new InboxTaskChoice(task.Id, task.Title, null, true)))
            .DistinctBy(task => task.Id)
            .Where(task => Matches(task.Title, query))];
    }

    /// <summary>"Archive as duplicate of…": dismisses the selected item as the
    /// same capture as <paramref name="duplicateOf"/>. A decision like Archive,
    /// so triage moves on from it.</summary>
    public Task ArchiveAsDuplicateAsync(Guid duplicateOf) =>
        DecideAsync(item => _inbox.ArchiveAsDuplicateAsync(item.Id, duplicateOf));

    /// <summary>"Link to task…": records that the selected item is already the
    /// task <paramref name="taskId"/>, so it leaves the queue routed to it and no
    /// new task is made.</summary>
    public Task LinkToTaskAsync(Guid taskId) =>
        DecideAsync(item => _inbox.LinkToTaskAsync(item.Id, taskId));

    /// <summary>"Merge into a task": folds the item <paramref name="itemId"/>
    /// into the backlog task <paramref name="taskId"/> it repeats — its title,
    /// link and notes become a comment on the task, and it is archived as a
    /// duplicate of the task. A decision like Archive, so when the item is the
    /// one selected, triage moves on from it; any other item is merged where it
    /// is and the selection stays. A refusal is toasted and changes nothing.</summary>
    public async Task MergeIntoTaskAsync(Guid itemId, Guid taskId)
    {
        if (SelectedItem?.Id == itemId)
        {
            await DecideAsync(item => _inbox.MergeIntoTaskAsync(item.Id, taskId));
            return;
        }

        if (Report(await _inbox.MergeIntoTaskAsync(itemId, taskId))) return;

        await ReloadAsync();
    }

    private static bool Matches(string title, string? query) =>
        string.IsNullOrWhiteSpace(query) || (title ?? string.Empty).Contains(query.Trim(), StringComparison.OrdinalIgnoreCase);

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
    public Task DeferAsync(DateOnly? until) =>
        DecideAsync(item => _inbox.DeferAsync(item.Id, until));

    /// <summary>The reader's calendar date, which is what a review date is
    /// counted from — the same local "today" the resurface sweep uses.</summary>
    public DateOnly Today => DateOnly.FromDateTime(_clock.GetLocalNow().DateTime);

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
        if (SelectedItem is not { } item || RoutingInFlight) return;

        var before = VisibleItems;
        RememberPosition();
        RouteRunning = true;
        Changed?.Invoke();

        try
        {
            var routed = await _inbox.RouteToBacklogAsync(item.Id);
            if (Report(routed)) return;

            await ReloadAsync();
            if (TriageMode) AdvancePast(before, item.Id);
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
        if (SelectedItem is not { } item || RoutingInFlight) return;

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

    // --- Triage, one item at a time ---------------------------------------------
    //
    // A triage session is a run of decisions over the rows on screen, and the
    // mode exists to make it short: one item at a time, "12 of 40" over it, and
    // each decision — archive, defer, move to a list, route — moving straight on
    // to the next item instead of leaving the reader on the one just decided.
    // Tags and repositories are not decisions; they refine the item and leave it
    // where it is. Held here rather than in the pane for the reason the picks
    // are: the shell re-mounts the pane when it moves slots, and a session that
    // forgot where it was would be a session started over.
    //
    // Leaving the mode changes nothing but the view: the item the reader stopped
    // at stays selected, so the queue opens on it.

    // --- AI triage (local ADR 0023) ----------------------------------------------
    // Asked of the module only while the advisor says it can run. While it
    // cannot — no Foundry, or none registered — nothing is asked and nothing is
    // exposed, so the pane draws no AI surface at all (ADR 0023 §4). A call that
    // fails, or throws, is the same as no answer: the item shows no cards, the
    // pass shows nothing, and triage carries on with the rule-based suggestions.

    /// <summary>The cards already asked for, by item, for the app session — one
    /// model call per item opened in triage (ADR 0023 §2). An item whose call
    /// failed is kept too, as no advice, so moving back to it does not ask again.</summary>
    private readonly Dictionary<Guid, InboxTriageAdviceDto?> _triageAdvice = [];

    /// <summary>The items whose cards are being asked for right now.</summary>
    private readonly HashSet<Guid> _triageAdvicePending = [];

    /// <summary>Whether any AI triage surface may be shown: the advisor is
    /// registered and can run. Read live, so configuring Foundry in Settings
    /// shows the surfaces without a restart, and removing it hides them.</summary>
    public bool TriageAdvisorAvailable => _inbox.TriageAdvisorAvailable;

    /// <summary>The AI cards for <paramref name="itemId"/>, or null — before
    /// they were asked for, when the call failed, when the advisor is not
    /// available, and when the answer has no card left to show. The answer is
    /// kept for the session, but the inbox moves on under it: a duplicate of a
    /// capture since decided, and plan members since decided, are left out, so
    /// a card never names what the reader can no longer find.</summary>
    public InboxTriageAdviceDto? TriageAdviceFor(Guid itemId)
    {
        if (!TriageAdvisorAvailable || !_triageAdvice.TryGetValue(itemId, out var advice) || advice is null) return null;

        var waiting = Items.Where(IsUndecided).Select(item => item.Id).ToHashSet();

        var duplicate = advice.Duplicate is { TargetKind: InboxTriageTargetKind.InboxItem } twin && !waiting.Contains(twin.TargetId)
            ? null
            : advice.Duplicate;

        var plan = advice.Plan;
        if (plan is not null)
        {
            var members = plan.ItemIds.Where(waiting.Contains).ToList();
            plan = members.Count >= 2 ? plan with { ItemIds = members } : null;
        }

        var current = advice with { Duplicate = duplicate, Plan = plan };
        return current.HasCards ? current : null;
    }

    /// <summary>The AI cards for the item being read, as <see cref="TriageAdviceFor"/>.</summary>
    public InboxTriageAdviceDto? TriageAdvice =>
        SelectedItemId is { } id ? TriageAdviceFor(id) : null;

    /// <summary>
    /// Asks for the AI cards of the item being read, when triage mode is on, the
    /// advisor can run, and they were not asked for before in this app session.
    /// Called when an item is opened in triage, and a no-op otherwise — never on
    /// intake, on a timer or while the list is browsed (ADR 0023 §2). The
    /// repositories sent are the ones Settings lists.
    /// </summary>
    public async Task LoadTriageAdviceAsync()
    {
        if (!TriageMode || SelectedItem is not { } item || !IsUndecided(item)) return;
        if (!TriageAdvisorAvailable) return;
        if (_triageAdvice.ContainsKey(item.Id) || !_triageAdvicePending.Add(item.Id)) return;

        InboxTriageAdviceDto? advice = null;
        try
        {
            var answer = await _inbox.AdviseTriageAsync(item.Id, ConfiguredRepositories());
            advice = answer.IsSuccess ? answer.Value : null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A card is a proposal; one that could not be made is no card, not
            // an error the reader has to deal with.
            advice = null;
        }
        finally
        {
            _triageAdvicePending.Remove(item.Id);
        }

        _triageAdvice[item.Id] = advice;
        Changed?.Invoke();
    }

    /// <summary>The AI triage pass on screen, or null — before it was asked
    /// for, after it was put away, when it failed, and while the advisor is not
    /// available.</summary>
    public InboxTriagePassDto? TriagePass => TriageAdvisorAvailable ? _triagePass : null;

    private InboxTriagePassDto? _triagePass;

    /// <summary>The one sentence that names why the last pass failed, or null —
    /// and null while the advisor is not available, like every AI surface.
    /// The reader stays in triage and nothing changed (ADR 0023 §4).</summary>
    public string? TriagePassError => TriageAdvisorAvailable ? _triagePassError : null;

    private string? _triagePassError;

    /// <summary>The number of the latest pass asked for, so an answer that
    /// lands after the pass was put away, or asked again, is dropped.</summary>
    private int _triagePassVersion;

    /// <summary>While the pass is being asked for.</summary>
    public bool TriagePassRunning { get; private set; }

    /// <summary>
    /// "Let AI propose the rest": asks for the pass over every unprocessed item
    /// of the slice being triaged, and answers whether one came back. Asked only
    /// when the advisor can run and a pass is not already running; a failure,
    /// thrown or returned, leaves no pass and one sentence in
    /// <see cref="TriagePassError"/>. Nothing is applied — the pass is a
    /// proposal until the reader presses Apply (ADR 0023 §5).
    /// </summary>
    public async Task<bool> ProposeTriagePassAsync()
    {
        if (TriagePassRunning || !TriageAdvisorAvailable) return false;

        var ids = SliceItems.Where(IsUndecided).Select(item => item.Id).ToList();
        if (ids.Count == 0) return false;

        var version = ++_triagePassVersion;
        TriagePassRunning = true;
        _triagePassError = null;
        _triagePass = null;
        Changed?.Invoke();

        InboxTriagePassDto? pass = null;
        string? error = null;
        try
        {
            var answer = await _inbox.ProposeTriagePassAsync(ids, ConfiguredRepositories());
            if (answer.IsSuccess) pass = answer.Value;
            else error = answer.Error.Message;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            error = $"The AI triage pass failed: {ex.Message}";
        }
        finally
        {
            TriagePassRunning = false;
        }

        // Put away, or asked again, while this one was out: its answer is stale.
        if (version != _triagePassVersion)
        {
            Changed?.Invoke();
            return false;
        }

        _triagePass = pass;
        _triagePassError = error;
        Changed?.Invoke();

        return pass is not null;
    }

    /// <summary>Puts the pass, or its failure, away.</summary>
    public void DismissTriagePass()
    {
        // A pass still out is put away too: its answer will be dropped.
        _triagePassVersion++;

        if (_triagePass is null && _triagePassError is null) return;

        _triagePass = null;
        _triagePassError = null;
        Changed?.Invoke();
    }

    /// <summary>Waiting for its first decision: unprocessed and never routed.</summary>
    private static bool IsUndecided(InboxItemDto item) =>
        item.Status == InboxStatus.Unprocessed && item.Routing is null;

    /// <summary>The repositories Settings lists, <c>owner/name</c>, as the
    /// advisor may name them.</summary>
    private IReadOnlyList<string> ConfiguredRepositories() =>
        [.. _gitHubSettings.Current.Repositories.Select(repository => repository.FullName)];

    /// <summary>Where the item being read sat in the rows, the last time it was
    /// in them. A decision takes an item out of the rows while it stays
    /// selected, and "next" from there means the row that slid into its place.</summary>
    private int _lastPosition;

    /// <summary>Whether the pane shows one item at a time.</summary>
    public bool TriageMode { get; private set; }

    /// <summary>Whether the pane shows its Sources tab rather than the queue.
    /// Held here, not on the pane, for the reason triage mode is: the pane
    /// re-mounts when it changes slot, and a tab that snapped back to the
    /// queue on that would be one the reader keeps choosing again.</summary>
    public bool SourcesShown { get; private set; }

    /// <summary>Shows the Sources tab, or the queue again.</summary>
    public void ShowSources(bool shown)
    {
        if (SourcesShown == shown) return;

        SourcesShown = shown;
        Changed?.Invoke();
    }

    /// <summary>The zero-based place of the item being read among the rows on
    /// screen, or -1 when none is chosen or it has left them.</summary>
    public int TriagePosition => SelectedItemId is { } id ? IndexOf(VisibleItems, id) : -1;

    /// <summary>Opens or leaves triage mode. Opening it on nothing, or on an
    /// item no longer in the rows, starts at the first row.</summary>
    public void SetTriageMode(bool on)
    {
        if (TriageMode == on) return;

        TriageMode = on;

        // The pass belongs to the triage it was asked from.
        if (!on)
        {
            _triagePassVersion++;
            _triagePass = null;
            _triagePassError = null;
        }

        if (on && TriagePosition < 0)
        {
            SelectedItemId = VisibleItems.Count > 0 ? VisibleItems[0].Id : null;
            RememberPosition();
        }

        Changed?.Invoke();
    }

    /// <summary>Reads the next row (<paramref name="delta"/> 1) or the previous
    /// one (-1), stopping at either end rather than wrapping — a reader who
    /// pressed past the last row has read them all, and landing back on the first
    /// would hide that. From an item that has just left the rows, next is the row
    /// now in its place and previous the one above it.</summary>
    public void Step(int delta)
    {
        var order = VisibleItems;
        if (order.Count == 0 || delta == 0) return;

        var at = TriagePosition;
        var next = at >= 0
            ? at + delta
            : delta > 0 ? _lastPosition : _lastPosition - 1;

        SelectItem(order[Math.Clamp(next, 0, order.Count - 1)].Id);
    }

    private void RememberPosition()
    {
        var at = TriagePosition;
        if (at >= 0) _lastPosition = at;
    }

    /// <summary>One decision on the selected item: the module's act, a reload,
    /// and in triage mode the step on to the next item.</summary>
    private async Task DecideAsync(Func<InboxItemDto, Task<Result>> act)
    {
        if (SelectedItem is not { } item) return;

        var before = VisibleItems;
        RememberPosition();

        if (Report(await act(item))) return;

        await ReloadAsync();
        if (TriageMode) AdvancePast(before, item.Id);
    }

    /// <summary>Selects what comes after <paramref name="decided"/> in the rows as
    /// they were before the decision — the first of them still on screen, so an
    /// item that left the rows is not landed on. Past the last row it goes back
    /// to the first item still waiting for a decision, which is where a reader
    /// who skipped some with j left them; with none left, nothing is selected
    /// and the mode says the queue is done.</summary>
    private void AdvancePast(IReadOnlyList<InboxItemDto> before, Guid decided)
    {
        var now = VisibleItems;
        var onScreen = now.Select(item => item.Id).ToHashSet();
        var at = IndexOf(before, decided);

        Guid? next = null;
        for (var index = at + 1; at >= 0 && index < before.Count && next is null; index++)
        {
            if (onScreen.Contains(before[index].Id)) next = before[index].Id;
        }

        next ??= now.FirstOrDefault(item => item.Id != decided && IsOpen(item))?.Id;

        SelectedItemId = next;
        RememberPosition();
        Changed?.Invoke();
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

    /// <summary>A route of any kind is in flight — one item, a drafted plan, or
    /// an act across the selection, a batch route among them. Each waits for
    /// the others: two routes at once could send one item to the backlog twice.</summary>
    public bool RoutingInFlight => RouteRunning || PlanRunning || BulkRunning;

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

    /// <summary>Deletes every picked item for good — the bar asks first. Nothing
    /// is already there: an item on screen has not been deleted.</summary>
    public Task<InboxBulkOutcome> BulkDeleteAsync() =>
        RunBulkAsync(
            "deleted",
            refuse: _ => null,
            alreadyThere: _ => false,
            apply: items => _inbox.DeleteAsync(Ids(items)));

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

    /// <summary>Routes every picked item to the backlog as one batch — one plan,
    /// tagged <c>+inbox-batch-…</c>. An item already decided is refused here and
    /// named, as the tag and repository acts do; the rest go together.</summary>
    public Task<InboxBulkOutcome> BulkRouteToBacklogAsync() => RouteBatchAsync(SelectedItems, listId: null);

    /// <summary>The open list's items still open to a decision — what "Move
    /// list to backlog" would route. Empty for the unfiled inbox and the
    /// Deferred slice, which are not lists.</summary>
    public IReadOnlyList<InboxItemDto> ListOpenItems =>
        SelectedListId is null || DeferredSelected ? [] : [.. SliceItems.Where(IsOpen)];

    /// <summary>How many of the open list's items are deferred. They stay in the
    /// list when it is routed — put aside is not decided — and the confirm says
    /// so. Counted from every item, since the list's slice leaves them out.</summary>
    public int ListDeferredCount =>
        SelectedListId is { } listId && !DeferredSelected
            ? Items.Count(item => item.ListId == listId && item.Status == InboxStatus.Deferred)
            : 0;

    /// <summary>Routes the open list's open items to the backlog as one batch,
    /// tagged after the list. The list stays; what it held leaves it for the
    /// backlog, and a routed row stays on screen marked, as a single route's does.</summary>
    public Task<InboxBulkOutcome> RouteListToBacklogAsync()
    {
        ListRouteConfirmOpen = false;

        return SelectedListId is { } listId && !DeferredSelected
            ? RouteBatchAsync(ListOpenItems, listId)
            : Task.FromResult(InboxBulkOutcome.Nothing);
    }

    /// <summary>Whether the list's plain "Move this list to the backlog?"
    /// confirm is open — the question a list of one is asked, since it has
    /// nothing to order. Held here rather than in the list component because
    /// the panel's path can end in it too.</summary>
    public bool ListRouteConfirmOpen { get; private set; }

    /// <summary>Move list to backlog: the panel for two or more open items,
    /// the plain confirm for one. A list route always asks first.</summary>
    public Task<InboxBulkOutcome> AskListRouteAsync()
    {
        if (ListOpenItems.Count > 1) return BeginListRouteAsync();

        OpenListRouteConfirm();
        return Task.FromResult(InboxBulkOutcome.Nothing);
    }

    /// <summary>Cancel on the list's confirm: nothing is routed.</summary>
    public void CancelListRouteConfirm()
    {
        if (!ListRouteConfirmOpen) return;

        ListRouteConfirmOpen = false;
        Changed?.Invoke();
    }

    private void OpenListRouteConfirm()
    {
        if (SelectedListId is null || DeferredSelected || ListOpenItems.Count == 0) return;

        ListRouteConfirmOpen = true;
        Changed?.Invoke();
    }

    // --- Before you route -------------------------------------------------------
    //
    // A batch of two or more items is decided in a panel before it goes: the
    // order, the dependencies the module can see between the items and on the
    // backlog, a repository per item, the shared tag, the count. One item has
    // nothing to order and keeps the act it always had.

    /// <summary>The batch the "Before you route" panel is showing, or null when
    /// the panel is closed.</summary>
    public InboxRouteDraft? RouteDraft { get; private set; }

    /// <summary>The bulk bar's Move to backlog: the panel for two or more
    /// routable items, the route itself for one.</summary>
    public Task<InboxBulkOutcome> BeginBulkRouteAsync() => BeginRouteAsync(SelectedItems, listId: null);

    /// <summary>Move list to backlog: the panel for the open list's items. The
    /// list's own confirm asks about a list of one, so this is only reached with
    /// two or more.</summary>
    public Task<InboxBulkOutcome> BeginListRouteAsync() =>
        SelectedListId is { } listId && !DeferredSelected
            ? BeginRouteAsync(ListOpenItems, listId)
            : Task.FromResult(InboxBulkOutcome.Nothing);

    /// <summary>
    /// Asks the module what the batch would do and opens the panel on its
    /// answer. Fewer than two routable items — here, or in the module's answer
    /// — is a batch with nothing to decide, so it routes at once as it always
    /// did. The ask counts as a route in flight, so a second press while it is
    /// out opens nothing twice.
    /// </summary>
    private async Task<InboxBulkOutcome> BeginRouteAsync(IReadOnlyList<InboxItemDto> picked, Guid? listId)
    {
        if (picked.Count == 0 || RoutingInFlight || RouteDraft is not null) return InboxBulkOutcome.Nothing;

        var pending = picked.Where(item => RefuseDecided(item) is null).ToList();
        if (pending.Count < 2) return await RouteOneOrAskAsync(picked, listId);

        InboxBatchProposalDto proposal;

        BulkRunning = true;
        Changed?.Invoke();

        try
        {
            var result = await _inbox.ProposeBatchAsync(Ids(pending), listId);
            if (Report(result, BulkResultTestId))
            {
                // The list went while the pane was open; the reload takes the
                // pane off it.
                await ReloadAsync();
                return InboxBulkOutcome.Nothing;
            }

            proposal = result.Value;
        }
        finally
        {
            BulkRunning = false;
            Changed?.Invoke();
        }

        if (proposal.Items.Count < 2)
        {
            // The module sees fewer to route than the pane did: something was
            // decided since the pane last looked. Read again first, so what
            // follows counts what is really there.
            await ReloadAsync();
            return await RouteOneOrAskAsync(picked, listId);
        }

        RouteDraft = new InboxRouteDraft(proposal, listId, picked);
        Changed?.Invoke();
        return InboxBulkOutcome.Nothing;
    }

    /// <summary>A batch with nothing to order. A selection of one routes on
    /// the press, as it always did; a list never routes unasked, so it gets
    /// the list's plain confirm.</summary>
    private async Task<InboxBulkOutcome> RouteOneOrAskAsync(IReadOnlyList<InboxItemDto> picked, Guid? listId)
    {
        if (listId is null) return await RouteBatchAsync(picked, listId);

        OpenListRouteConfirm();
        return InboxBulkOutcome.Nothing;
    }

    /// <summary>Cancel on the panel: it closes, and nothing is routed or changed.</summary>
    public void CancelRoute()
    {
        if (RouteDraft is null) return;

        RouteDraft = null;
        Changed?.Invoke();
    }

    /// <summary>
    /// "Ask the AI to order" on the panel: the drafter's reading of the batch,
    /// added to the panel as dependencies the person keeps or turns off. Asked
    /// only on the press — every ask is a model call — and refused up front
    /// when the drafter is unavailable, as the button is. The repositories sent
    /// are the panel's current choices, the only ones the answer may name. An
    /// answer that lands after the panel was closed, or replaced, is dropped.
    /// </summary>
    public async Task InferRouteOrderAsync()
    {
        if (RouteDraft is not { Inferring: false } draft || RoutingInFlight || !PlanDrafterAvailability.Available) return;

        draft.Inferring = true;
        Changed?.Invoke();

        try
        {
            var result = await _inbox.InferBatchOrderAsync(
                [.. draft.Proposal.Items.Select(item => item.Id)],
                draft.PlanTag,
                draft.Choices().Repositories);

            if (!ReferenceEquals(RouteDraft, draft)) return;

            if (result.IsFailure) draft.InferenceRefused(result.Error.Message);
            else draft.AddInferred(result.Value);
        }
        finally
        {
            draft.Inferring = false;
            Changed?.Invoke();
        }
    }

    /// <summary>Confirm on the panel: routes the batch as one import with the
    /// choices made — the tag the panel showed, the repositories, the
    /// dependencies left on. Refused while a loop is on, as the panel's Confirm
    /// is, so a keyboard reaching this anyway routes nothing.</summary>
    public async Task<InboxBulkOutcome> ConfirmRouteAsync()
    {
        if (RouteDraft is not { CanConfirm: true } draft || RoutingInFlight) return InboxBulkOutcome.Nothing;

        RouteDraft = null;

        return await RouteBatchAsync(draft.Picked, draft.ListId, draft.Choices());
    }

    /// <summary>
    /// One batch route, end to end — <see cref="RunBulkAsync"/>'s shape with the
    /// one difference a batch has: the module answers once, for the lot, and
    /// either routed every item sent or none of them. So there is no "already
    /// there" to skip, the sentence names the plan the batch became, and the shell
    /// hears of it once.
    /// </summary>
    private async Task<InboxBulkOutcome> RouteBatchAsync(
        IReadOnlyList<InboxItemDto> picked,
        Guid? listId,
        InboxBatchRouteChoicesDto? choices = null)
    {
        if (picked.Count == 0 || RoutingInFlight) return InboxBulkOutcome.Nothing;

        var refused = new Dictionary<Guid, Error>();
        var pending = new List<InboxItemDto>();

        foreach (var item in picked)
        {
            if (RefuseDecided(item) is { } error) refused[item.Id] = error;
            else pending.Add(item);
        }

        BulkRunning = true;
        Changed?.Invoke();

        try
        {
            InboxBatchRoutedDto? batch = null;

            if (pending.Count > 0)
            {
                var result = await _inbox.RouteToBacklogAsync(Ids(pending), listId, choices);
                if (Report(result, BulkResultTestId))
                {
                    // The list went while the dialog was open; the reload takes
                    // the pane off it.
                    await ReloadAsync();
                    return InboxBulkOutcome.Nothing;
                }

                batch = result.Value;
                foreach (var failure in batch.Failed) refused[failure.Id] = failure.Error;
            }

            var failures = picked
                .Where(item => refused.ContainsKey(item.Id))
                .Select(item => new InboxBulkFailure(item.Id, ItemTitle(item), refused[item.Id]))
                .ToList();

            var outcome = new InboxBulkOutcome(batch?.Routed.Count ?? 0, 0, failures);

            await ReloadAsync();

            var message = BatchRouteMessage(outcome, batch?.PlanTag);
            _toasts?.Publish(failures.Count > 0
                ? ToastMessage.Warning(message, BulkResultTestId)
                : ToastMessage.Info(message, BulkResultTestId));

            if (batch is { Routed.Count: > 0 }) BatchRouted?.Invoke(batch);

            return outcome;
        }
        finally
        {
            BulkRunning = false;
            Changed?.Invoke();
        }
    }

    /// <summary>
    /// What a batch route did. Landed, it is the bulk sentence with the plan the
    /// batch became. Refused by Tasks, every item sent carries the same refusal,
    /// and naming it once per item would bury the one thing to know — nothing
    /// moved — under repeats; so the refusal is the sentence, said once, and only
    /// the items refused before anything was sent are named after it.
    /// </summary>
    internal static string BatchRouteMessage(InboxBulkOutcome outcome, string? planTag)
    {
        if (!outcome.Failures.Any(failure => failure.Error.Code == InboxErrors.BatchRefusedCode))
        {
            return BulkMessage(outcome, $"moved to the backlog as {planTag}");
        }

        var refusal = outcome.Failures.First(failure => failure.Error.Code == InboxErrors.BatchRefusedCode).Error;
        var named = outcome.Failures
            .Where(failure => failure.Error.Code != InboxErrors.BatchRefusedCode)
            .Select(failure => $"\"{failure.Title}\": {failure.Error.Message}")
            .ToList();

        return named.Count == 0
            ? refusal.Message
            : $"{refusal.Message} Not sent either — {string.Join(" ", named)}";
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

/// <summary>A task "Link to task…" can offer: its reason when it relates to the
/// item, and whether it is still open.</summary>
public sealed record InboxTaskChoice(Guid Id, string Title, string? Reason, bool IsOpen);

/// <summary>What the queue health bar shows: the unprocessed count, when the
/// oldest of them was captured (null with none), how many have waited too long,
/// and how the rest split between fresh and aging.</summary>
public sealed record InboxQueueHealth(int Unprocessed, DateTimeOffset? OldestCapturedAt, int Stale)
{
    public static readonly InboxQueueHealth Clear = new(0, null, 0);

    /// <summary>Captured under <see cref="InboxDesktopState.FreshUnderDays"/> ago.</summary>
    public int Fresh { get; init; }

    /// <summary>From <see cref="InboxDesktopState.FreshUnderDays"/> up to
    /// <see cref="InboxDesktopState.StaleAfterDays"/>.</summary>
    public int Aging { get; init; }
}

/// <summary>One heading of the list and the rows under it: Today, This week or
/// Older than a week.</summary>
public sealed record InboxAgeGroup(string Key, string Label, IReadOnlyList<InboxItemDto> Items);

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
