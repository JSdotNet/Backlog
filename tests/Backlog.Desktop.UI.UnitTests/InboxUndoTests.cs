using Backlog.Desktop.UI.Inbox;
using Backlog.Infrastructure.GitHub;
using Backlog.Modules.Inbox.Abstractions;
using Backlog.Modules.Inbox.Abstractions.DataTransferObjects;
using Backlog.UI.Components.Feedback;
using Bunit;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The Inbox session's undo history: every decision records its way back as it
/// lands, U takes the newest back through the module, and the history keeps the
/// session's tally per decision kind for the inbox-zero screen.
/// </summary>
public sealed class InboxUndoTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "backlog-inbox-undo-tests", Guid.NewGuid().ToString("n"));

    // --- Each decision's way back ------------------------------------------------

    [Fact]
    public async Task An_archive_is_undone_by_restoring_the_item_to_unprocessed()
    {
        var (inbox, state, _) = Build();
        var item = inbox.Seed("Call the dentist");
        await Open(state, item.Id);

        await state.ArchiveAsync();
        Assert.Equal(InboxStatus.Archived, inbox.Find(item.Id)!.Status);

        Assert.True(await state.UndoLatestAsync());

        Assert.Equal([("restore", item.Id)], inbox.Undos);
        Assert.Equal(InboxStatus.Unprocessed, inbox.Find(item.Id)!.Status);
        Assert.Equal(item.Id, state.SelectedItemId);
        Assert.False(state.CanUndo);
    }

    [Fact]
    public async Task Undoing_the_archive_of_a_deferred_item_brings_it_back_deferred_to_the_same_date()
    {
        var (inbox, state, _) = Build();
        var item = inbox.Seed("Read the design doc", status: InboxStatus.Deferred, deferredUntil: new DateOnly(2026, 10, 1));
        await state.ReloadAsync();
        state.SelectSlice(InboxDesktopState.DeferredSliceId);
        state.SelectItem(item.Id);

        await state.ArchiveAsync();
        Assert.Null(inbox.Find(item.Id)!.DeferredUntil);

        await state.UndoLatestAsync();

        var back = inbox.Find(item.Id)!;
        Assert.Equal(InboxStatus.Deferred, back.Status);
        Assert.Equal(new DateOnly(2026, 10, 1), back.DeferredUntil);
    }

    [Fact]
    public async Task A_deferral_is_undone_by_resurfacing_the_item()
    {
        var (inbox, state, _) = Build();
        var item = inbox.Seed("Read the design doc");
        await Open(state, item.Id);

        await state.DeferAsync(new DateOnly(2026, 10, 1));
        await state.UndoLatestAsync();

        var back = inbox.Find(item.Id)!;
        Assert.Equal(InboxStatus.Unprocessed, back.Status);
        Assert.Null(back.DeferredUntil);
    }

    [Fact]
    public async Task A_deferral_whose_date_moved_is_undone_by_putting_the_old_date_back()
    {
        var (inbox, state, _) = Build();
        var item = inbox.Seed("Read the design doc", status: InboxStatus.Deferred, deferredUntil: new DateOnly(2026, 10, 1));
        await Open(state, item.Id);

        await state.DeferAsync(new DateOnly(2026, 11, 1));
        await state.UndoLatestAsync();

        var back = inbox.Find(item.Id)!;
        Assert.Equal(InboxStatus.Deferred, back.Status);
        Assert.Equal(new DateOnly(2026, 10, 1), back.DeferredUntil);
    }

    [Fact]
    public async Task A_move_to_a_list_is_undone_by_moving_back_to_the_list_it_came_from()
    {
        var (inbox, state, _) = Build();
        var from = inbox.SeedList("Someday");
        var to = inbox.SeedList("Errands");
        var item = inbox.Seed("Buy stamps", listId: from.Id);
        await state.ReloadAsync();
        state.SelectSlice(InboxDesktopState.ListNavId(from.Id));
        state.SelectItem(item.Id);

        await state.MoveToListAsync(to.Id);
        Assert.Equal(to.Id, inbox.Find(item.Id)!.ListId);

        await state.UndoLatestAsync();

        Assert.Equal(from.Id, inbox.Find(item.Id)!.ListId);
    }

    [Fact]
    public async Task A_move_to_the_list_the_item_is_already_in_records_nothing()
    {
        var (inbox, state, _) = Build();
        var item = inbox.Seed("Buy stamps");
        await Open(state, item.Id);

        await state.MoveToListAsync(null);

        Assert.False(state.CanUndo);
    }

    [Fact]
    public async Task A_route_is_undone_by_returning_the_item_and_deleting_its_tasks_and_the_shell_hears_of_it()
    {
        var (inbox, state, _) = Build();
        var item = inbox.Seed("Fix the scroll jump");
        await Open(state, item.Id);
        var undone = 0;
        state.RouteUndone += () => undone++;

        await state.RouteToBacklogAsync();
        Assert.NotNull(inbox.Find(item.Id)!.Routing);

        await state.UndoLatestAsync();

        Assert.Equal([("return", item.Id)], inbox.Undos);
        var back = inbox.Find(item.Id)!;
        Assert.Null(back.Routing);
        Assert.Equal(InboxStatus.Unprocessed, back.Status);
        Assert.Equal(1, undone);
    }

    [Fact]
    public async Task A_route_whose_task_has_started_is_refused_in_one_sentence_and_the_next_U_reaches_the_decision_before()
    {
        var (inbox, state, toasts) = Build();
        var earlier = inbox.Seed("Earlier", capturedAt: inbox.Now.AddMinutes(1));
        var routed = inbox.Seed("Fix the scroll jump");
        await Open(state, earlier.Id);
        await state.ArchiveAsync();
        state.SelectItem(routed.Id);
        await state.RouteToBacklogAsync();
        inbox.RefuseReturn = InboxErrors.UndoTaskStarted("Fix the scroll jump");
        var undone = 0;
        state.RouteUndone += () => undone++;

        Assert.False(await state.UndoLatestAsync());

        var toast = toasts.Visible.Last(message => message.TestId == InboxDesktopState.UndoResultTestId);
        Assert.Equal(ToastSeverity.Error, toast.Severity);
        Assert.Equal("Can't undo — Fix the scroll jump has started", toast.Message);
        Assert.NotNull(inbox.Find(routed.Id)!.Routing);
        Assert.Equal(1, state.DecisionCount(InboxDecisionKind.MoveToBacklog));
        Assert.Equal(0, undone);

        // The refused step is gone, so it does not stand in front of the archive.
        Assert.True(await state.UndoLatestAsync());
        Assert.Equal(InboxStatus.Unprocessed, inbox.Find(earlier.Id)!.Status);
    }

    [Fact]
    public async Task A_merge_is_undone_by_restoring_the_capture_and_clearing_DuplicateOf()
    {
        var (inbox, state, _) = Build();
        var item = inbox.Seed("Scroll jumps to top");
        await Open(state, item.Id);
        var task = Guid.CreateVersion7();

        await state.MergeIntoTaskAsync(item.Id, task);
        Assert.Equal(task, inbox.Find(item.Id)!.DuplicateOf);

        await state.UndoLatestAsync();

        var back = inbox.Find(item.Id)!;
        Assert.Equal(InboxStatus.Unprocessed, back.Status);
        Assert.Null(back.DuplicateOf);
        Assert.False(back.DuplicateOfTask);
        // Only the item came back; the merge was not asked to be taken off the task.
        Assert.Single(inbox.Merges);
        Assert.Equal([("restore", item.Id)], inbox.Undos);
    }

    [Fact]
    public async Task A_merge_of_an_item_that_is_not_selected_is_recorded_too()
    {
        var (inbox, state, _) = Build();
        var selected = inbox.Seed("Selected", capturedAt: inbox.Now.AddMinutes(1));
        var other = inbox.Seed("Other");
        await Open(state, selected.Id);

        await state.MergeIntoTaskAsync(other.Id, Guid.CreateVersion7());
        await state.UndoLatestAsync();

        Assert.Equal([("restore", other.Id)], inbox.Undos);
        Assert.Equal(0, state.DecisionCount(InboxDecisionKind.MergeIntoTask));
    }

    [Fact]
    public async Task A_link_is_undone_without_deleting_the_task_it_named()
    {
        var (inbox, state, _) = Build();
        var item = inbox.Seed("Already a task");
        await Open(state, item.Id);
        var undone = 0;
        state.RouteUndone += () => undone++;

        await state.LinkToTaskAsync(Guid.CreateVersion7());
        await state.UndoLatestAsync();

        Assert.Equal([("unlink", item.Id)], inbox.Undos);
        Assert.Null(inbox.Find(item.Id)!.Routing);
        Assert.Equal(0, undone);
    }

    // --- The history -------------------------------------------------------------

    [Fact]
    public async Task U_takes_back_the_newest_decision_first()
    {
        var (inbox, state, _) = Build();
        var first = inbox.Seed("First", capturedAt: inbox.Now.AddMinutes(2));
        var second = inbox.Seed("Second", capturedAt: inbox.Now.AddMinutes(1));
        await Open(state, first.Id);
        await state.ArchiveAsync();
        state.SelectItem(second.Id);
        await state.ArchiveAsync();

        await state.UndoLatestAsync();

        Assert.Equal(InboxStatus.Unprocessed, inbox.Find(second.Id)!.Status);
        Assert.Equal(InboxStatus.Archived, inbox.Find(first.Id)!.Status);
        Assert.True(state.CanUndo);
    }

    [Fact]
    public async Task With_nothing_to_undo_U_says_so_and_changes_nothing()
    {
        var (_, state, toasts) = Build();
        await state.ReloadAsync();

        Assert.False(await state.UndoLatestAsync());

        Assert.Equal("Nothing to undo.", Assert.Single(toasts.Visible).Message);
    }

    [Fact]
    public async Task The_session_counts_each_decision_kind_and_an_undo_takes_one_off()
    {
        var (inbox, state, _) = Build();
        var list = inbox.SeedList("Errands");
        var a = inbox.Seed("A", capturedAt: inbox.Now.AddMinutes(4));
        var b = inbox.Seed("B", capturedAt: inbox.Now.AddMinutes(3));
        var c = inbox.Seed("C", capturedAt: inbox.Now.AddMinutes(2));
        var d = inbox.Seed("D", capturedAt: inbox.Now.AddMinutes(1));
        await Open(state, a.Id);
        await state.ArchiveAsync();
        state.SelectItem(b.Id);
        await state.ArchiveAsync();
        state.SelectItem(c.Id);
        await state.DeferAsync(null);
        state.SelectItem(d.Id);
        await state.MoveToListAsync(list.Id);

        Assert.Equal(2, state.DecisionCount(InboxDecisionKind.Archive));
        Assert.Equal(1, state.DecisionCount(InboxDecisionKind.Defer));
        Assert.Equal(1, state.DecisionCount(InboxDecisionKind.MoveToList));
        Assert.Equal(0, state.DecisionCount(InboxDecisionKind.MoveToBacklog));

        await state.UndoLatestAsync();

        Assert.Equal(0, state.DecisionCount(InboxDecisionKind.MoveToList));
        Assert.False(state.DecisionCounts.ContainsKey(InboxDecisionKind.MoveToList));
        Assert.Equal(2, state.DecisionCount(InboxDecisionKind.Archive));
    }

    [Fact]
    public async Task A_bulk_archive_is_one_step_that_brings_every_item_back()
    {
        var (inbox, state, _) = Build();
        var a = inbox.Seed("A", capturedAt: inbox.Now.AddMinutes(2));
        var b = inbox.Seed("B", capturedAt: inbox.Now.AddMinutes(1));
        await state.ReloadAsync();
        state.TogglePicked(a.Id, true, range: false);
        state.TogglePicked(b.Id, true, range: false);

        await state.BulkArchiveAsync();
        Assert.Equal(2, state.DecisionCount(InboxDecisionKind.Archive));

        await state.UndoLatestAsync();

        Assert.Equal(InboxStatus.Unprocessed, inbox.Find(a.Id)!.Status);
        Assert.Equal(InboxStatus.Unprocessed, inbox.Find(b.Id)!.Status);
        Assert.Equal(0, state.DecisionCount(InboxDecisionKind.Archive));
        Assert.False(state.CanUndo);
    }

    [Fact]
    public async Task A_bulk_move_is_one_step_that_moves_every_item_back_to_its_own_list()
    {
        var (inbox, state, _) = Build();
        var someday = inbox.SeedList("Someday");
        var errands = inbox.SeedList("Errands");
        var a = inbox.Seed("A", capturedAt: inbox.Now.AddMinutes(2));
        var b = inbox.Seed("B", capturedAt: inbox.Now.AddMinutes(1));
        await state.ReloadAsync();
        state.TogglePicked(a.Id, true, range: false);
        await state.BulkMoveToListAsync(someday.Id);
        state.SelectSlice(InboxDesktopState.ListNavId(someday.Id));
        state.TogglePicked(a.Id, true, range: false);
        state.SelectSlice(InboxDesktopState.InboxSliceId);
        state.TogglePicked(b.Id, true, range: false);
        await state.BulkMoveToListAsync(errands.Id);

        await state.UndoLatestAsync();

        Assert.Null(inbox.Find(b.Id)!.ListId);
        Assert.Equal(someday.Id, inbox.Find(a.Id)!.ListId);
    }

    [Fact]
    public async Task A_batch_route_partly_refused_takes_back_the_rest_and_says_so()
    {
        var (inbox, state, toasts) = Build();
        var a = inbox.Seed("Started already", capturedAt: inbox.Now.AddMinutes(2));
        var b = inbox.Seed("Not started", capturedAt: inbox.Now.AddMinutes(1));
        await state.ReloadAsync();
        state.TogglePicked(a.Id, true, range: false);
        state.TogglePicked(b.Id, true, range: false);
        await state.BulkRouteToBacklogAsync();
        Assert.NotNull(inbox.Find(a.Id)!.Routing);
        Assert.NotNull(inbox.Find(b.Id)!.Routing);
        Assert.Equal(2, state.DecisionCount(InboxDecisionKind.MoveToBacklog));
        inbox.RefuseReturn = InboxErrors.UndoTaskStarted("Started already");
        inbox.RefuseReturnOf.Add(a.Id);

        Assert.True(await state.UndoLatestAsync());

        Assert.NotNull(inbox.Find(a.Id)!.Routing);
        Assert.Null(inbox.Find(b.Id)!.Routing);
        Assert.Equal(1, state.DecisionCount(InboxDecisionKind.MoveToBacklog));
        var toast = toasts.Visible.Last(message => message.TestId == InboxDesktopState.UndoResultTestId);
        Assert.Equal("Can't undo — Started already has started. The rest was undone.", toast.Message);
    }

    [Fact]
    public async Task A_deferral_whose_date_moved_is_not_counted_twice()
    {
        var (inbox, state, _) = Build();
        var item = inbox.Seed("Read the design doc");
        await Open(state, item.Id);

        await state.DeferAsync(new DateOnly(2026, 10, 1));
        await state.DeferAsync(new DateOnly(2026, 11, 1));

        Assert.Equal(1, state.DecisionCount(InboxDecisionKind.Defer));
        await state.UndoLatestAsync();
        Assert.Equal(1, state.DecisionCount(InboxDecisionKind.Defer));
        await state.UndoLatestAsync();
        Assert.Equal(0, state.DecisionCount(InboxDecisionKind.Defer));
    }

    [Fact]
    public void A_long_session_forgets_its_oldest_steps_but_still_counts_them()
    {
        var history = new InboxUndoHistory();

        for (var i = 0; i < InboxUndoHistory.Capacity + 5; i++)
        {
            history.Record(new InboxRestoreUndoStep(InboxDecisionKind.Archive, "Archive", Guid.NewGuid()));
        }

        Assert.Equal(InboxUndoHistory.Capacity + 5, history.CountOf(InboxDecisionKind.Archive));

        var taken = 0;
        while (history.TakeLatest() is { } step)
        {
            history.Undone([step]);
            taken++;
        }

        Assert.Equal(InboxUndoHistory.Capacity, taken);
        Assert.Equal(5, history.CountOf(InboxDecisionKind.Archive));
    }

    // --- The key ---------------------------------------------------------------

    [Fact]
    public async Task U_in_the_pane_undoes_and_the_shortcuts_dialog_lists_it()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Services.AddSingleton(new GitHubSettingsStore(Path.Combine(_root, "github.json")));
        TasksTestHost.AddToastChannel(context.Services);
        var inbox = InboxTestHost.AddInboxState(context.Services);
        var item = inbox.Seed("Call the dentist");
        var pane = context.Render<InboxPane>();
        var state = context.Services.GetRequiredService<InboxDesktopState>();
        await pane.InvokeAsync(state.InitializeAsync);
        await pane.Find($"[data-testid='inbox-item-{item.Id:D}']").ClickAsync(new());

        await pane.InvokeAsync(() => pane.Instance.OnShortcutAsync("a"));
        pane.WaitForAssertion(() => Assert.Equal(InboxStatus.Archived, inbox.Find(item.Id)!.Status));

        await pane.InvokeAsync(() => pane.Instance.OnShortcutAsync("u"));
        pane.WaitForAssertion(() => Assert.Equal(InboxStatus.Unprocessed, inbox.Find(item.Id)!.Status));

        Assert.Contains(InboxPane.Shortcuts, shortcut => shortcut.Keys == "u" && shortcut.Action == "Undo the last decision");
        await pane.InvokeAsync(() => pane.Instance.OnShortcutAsync("?"));
        Assert.Contains("Undo the last decision", pane.Find("[data-testid='inbox-shortcuts-list']").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Components_js_hands_u_to_the_pane()
    {
        var script = File.ReadAllText(RepositoryRoot.File("src", "Core", "Backlog.UI.Components", "wwwroot", "components.js"));
        var keys = script[script.IndexOf("const SHORTCUT_PANE_KEYS", StringComparison.Ordinal)..];
        keys = keys[..keys.IndexOf(';', StringComparison.Ordinal)];

        Assert.Contains("'u'", keys, StringComparison.Ordinal);
    }

    // --- Helpers -----------------------------------------------------------------

    private (FakeInboxItems Inbox, InboxDesktopState State, ToastChannel Toasts) Build()
    {
        var inbox = new FakeInboxItems();
        var toasts = new ToastChannel();
        var state = new InboxDesktopState(inbox, new GitHubSettingsStore(Path.Combine(_root, "github.json")), toasts);
        return (inbox, state, toasts);
    }

    private static async Task Open(InboxDesktopState state, Guid id)
    {
        await state.ReloadAsync();
        state.SelectItem(id);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
