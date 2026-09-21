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
    /// inbox.</summary>
    public Guid? SelectedListId { get; private set; }

    /// <summary>The selected slice as the side menu names it.</summary>
    public string SelectedSliceId => SelectedListId is { } id ? ListNavId(id) : InboxSliceId;

    /// <summary>What the open slice is called, for the list's accessible name
    /// and its empty state.</summary>
    public string SliceName =>
        SelectedListId is { } id && FindList(id) is { } list ? list.Name : "Inbox";

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

    /// <summary>The rows of the open slice before the kind filter: everything
    /// filed there that is not archived, newest capture first. Routed items stay
    /// — the reader sees where a thing went from the row it was on — and
    /// archived ones go, because archived is the terminal state and a slice full
    /// of what was dismissed would not be a queue.</summary>
    public IReadOnlyList<InboxItemDto> SliceItems =>
        [.. Items
            .Where(item => item.Status != InboxStatus.Archived)
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
    public int InboxCount => Items.Count(item => IsOpen(item) && item.ListId is null);

    /// <summary>How many items in a list are still waiting on a decision. Open
    /// rather than non-archived: the number on a To Do list is what is left to
    /// do, and a routed item is done as far as the inbox is concerned.</summary>
    public int ListCount(Guid listId) => Items.Count(item => IsOpen(item) && item.ListId == listId);

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
    /// then loads. Called by the shell when the pane first shows; idempotent.</summary>
    public async Task InitializeAsync()
    {
        await FollowRepositoryRenamesAsync();
        await _inbox.EnsureDefaultOrganizerAsync();
        await ReloadAsync();
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
        }

        if (SelectedItemId is { } itemId && Items.All(item => item.Id != itemId))
        {
            SelectedItemId = null;
        }

        if (EditingId is { } editing && !RowExists(editing))
        {
            EditingId = null;
        }

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

        if (TryParseListId(navId, out var parsed))
        {
            listId = parsed;
        }
        else if (!string.Equals(navId, InboxSliceId, StringComparison.Ordinal))
        {
            return;
        }

        if (listId == SelectedListId) return;

        SelectedListId = listId;
        SelectedItemId = null;
        _kindFilter.Clear();
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
        Changed?.Invoke();
    }

    public void ClearKindFilter()
    {
        if (_kindFilter.Count == 0) return;

        _kindFilter.Clear();
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
        SelectedListId = null;
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

        SelectedListId = created.Value.Id;
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

    private static bool IsOpen(InboxItemDto item) => item.Status is InboxStatus.Unprocessed or InboxStatus.Deferred;

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
