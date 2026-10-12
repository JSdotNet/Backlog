using Backlog.Desktop.UI.Inbox;
using Backlog.Infrastructure.GitHub;
using Backlog.Modules.Inbox.Abstractions;
using Backlog.Modules.Inbox.Abstractions.DataTransferObjects;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// Triage reads only the items still waiting for a decision. A routed item
/// stays in the slice's rows — the columns show it on purpose — but triage
/// never lands on it: not on start, not after a decision, not on j or k, and
/// Up next does not list it.
/// </summary>
public sealed class InboxTriageNavigationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "backlog-inbox-triage-navigation-tests", Guid.NewGuid().ToString("n"));

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

    [Fact]
    public async Task Up_next_leaves_out_the_rows_already_routed()
    {
        var (state, items) = await Loaded(("A", Open), ("B", Routed), ("C", Open), ("D", Routed), ("E", Open));
        state.SelectItem(items[0].Id);
        state.SetTriageMode(true);

        Assert.Equal(["C", "E"], state.TriageUpNext.Select(item => item.Title));
    }

    [Fact]
    public async Task On_the_last_open_row_up_next_lists_only_the_open_rows_above_it()
    {
        var (state, items) = await Loaded(("A", Open), ("B", Routed), ("C", Open), ("D", Routed));
        state.SelectItem(items[0].Id);
        state.SetTriageMode(true);

        state.Step(1);

        Assert.Equal(items[2].Id, state.SelectedItemId);
        Assert.Equal(["A"], state.TriageUpNext.Select(item => item.Title));
    }

    [Fact]
    public async Task A_decision_moves_on_past_a_routed_row()
    {
        var (state, items) = await Loaded(("A", Open), ("B", Routed), ("C", Open));
        state.SelectItem(items[0].Id);
        state.SetTriageMode(true);

        await state.ArchiveAsync();

        Assert.Equal(items[2].Id, state.SelectedItemId);
    }

    [Fact]
    public async Task Skip_and_previous_step_over_routed_rows()
    {
        var (state, items) = await Loaded(("A", Open), ("B", Routed), ("C", Open), ("D", Routed));
        state.SelectItem(items[0].Id);
        state.SetTriageMode(true);

        state.Step(1);
        Assert.Equal(items[2].Id, state.SelectedItemId);

        // Past the last open row there is nothing more to read: it stays put
        // rather than landing on the routed row below.
        state.Step(1);
        Assert.Equal(items[2].Id, state.SelectedItemId);

        state.Step(-1);
        Assert.Equal(items[0].Id, state.SelectedItemId);
    }

    [Fact]
    public async Task Starting_triage_on_a_routed_row_opens_the_next_open_one_and_counts_only_open_rows()
    {
        var (state, items) = await Loaded(("A", Open), ("B", Routed), ("C", Open));
        state.SelectItem(items[1].Id);

        state.SetTriageMode(true);

        Assert.Equal(items[2].Id, state.SelectedItemId);
        Assert.Equal(2, state.TriageTotal);
        Assert.Equal(2, state.TriageNumber);
    }

    [Fact]
    public async Task Starting_triage_with_nothing_chosen_skips_a_routed_first_row()
    {
        var (state, items) = await Loaded(("A", Routed), ("B", Open));

        state.SetTriageMode(true);

        Assert.Equal(items[1].Id, state.SelectedItemId);
        Assert.Equal(1, state.TriageNumber);
        Assert.Equal(1, state.TriageTotal);
    }

    [Fact]
    public async Task The_columns_still_step_onto_a_routed_row()
    {
        var (state, items) = await Loaded(("A", Open), ("B", Routed), ("C", Open));
        state.SelectItem(items[0].Id);

        state.Step(1);

        Assert.Equal(items[1].Id, state.SelectedItemId);
    }

    // --- A capture made while triaging ----------------------------------------

    /// <summary>The capture field stays in the header during triage, and a
    /// thought filed from it while a list is being read lands in the unfiled
    /// Inbox — not in the slice being triaged. The session is not the reader's
    /// to lose for it: the slice, the kind filter, the item and its "n of total"
    /// all stay as they were, and the new item waits unfiled.</summary>
    [Fact]
    public async Task A_capture_while_triaging_a_list_leaves_the_slice_the_item_and_the_count_alone()
    {
        var inbox = new FakeInboxItems();
        var list = inbox.SeedList("Reading");
        inbox.Seed("Unfiled", capturedAt: inbox.Now.AddMinutes(9));
        var rows = new[] { "A", "B", "C" }
            .Select((title, index) => inbox.Seed(title, listId: list.Id, capturedAt: inbox.Now.AddMinutes(3 - index)))
            .ToList();
        var state = await LoadedOn(inbox);
        state.SelectSlice(InboxDesktopState.ListNavId(list.Id));
        state.ToggleKind(rows[0].KindSlug);
        state.SetTriageMode(true);
        state.Step(1);
        Assert.Equal(rows[1].Id, state.SelectedItemId);

        var kept = await state.CaptureAsync(new InboxCapture("A thought on the way", string.Empty));

        Assert.True(kept);
        Assert.True(state.TriageMode);
        Assert.Equal(list.Id, state.SelectedListId);
        Assert.Equal("Reading", state.SliceName);
        Assert.Equal(new[] { rows[0].KindSlug }, state.KindFilter);
        Assert.Equal(rows[1].Id, state.SelectedItemId);
        Assert.Equal(2, state.TriageNumber);
        Assert.Equal(3, state.TriageTotal);
        Assert.Equal(0, state.TriageProgressed);
        var captured = Assert.Single(state.Items, item => item.Title == "A thought on the way");
        Assert.Null(captured.ListId);
        Assert.Equal(InboxStatus.Unprocessed, captured.Status);
    }

    /// <summary>The Deferred slice spans lists and holds only what was put
    /// aside, so a capture is never an arrival in it either.</summary>
    [Fact]
    public async Task A_capture_while_triaging_the_deferred_slice_stays_in_it()
    {
        var inbox = new FakeInboxItems();
        var first = inbox.Seed("Later one", status: InboxStatus.Deferred, deferredUntil: new DateOnly(2026, 10, 1));
        inbox.Seed("Later two", status: InboxStatus.Deferred, deferredUntil: new DateOnly(2026, 10, 2));
        var state = await LoadedOn(inbox);
        state.SelectSlice(InboxDesktopState.DeferredSliceId);
        state.SetTriageMode(true);
        Assert.Equal(first.Id, state.SelectedItemId);

        await state.CaptureAsync(new InboxCapture("Captured while deferring", string.Empty));

        Assert.True(state.DeferredSelected);
        Assert.Equal("Deferred", state.SliceName);
        Assert.Equal(first.Id, state.SelectedItemId);
        Assert.Equal(1, state.TriageNumber);
        Assert.Equal(2, state.TriageTotal);
    }

    /// <summary>Triaging the unfiled Inbox itself, the capture is an arrival in
    /// the slice being read: it is counted after the rows triage began with and
    /// grows the total (requirements.md, "Triage counts the session"), while the
    /// reader stays on the item they were reading.</summary>
    [Fact]
    public async Task A_capture_while_triaging_the_inbox_is_counted_and_the_reader_stays_on_their_item()
    {
        var (state, items) = await Loaded(("A", Open), ("B", Open), ("C", Open));
        state.SelectItem(items[0].Id);
        state.SetTriageMode(true);
        state.Step(1);

        await state.CaptureAsync(new InboxCapture("Arrived", string.Empty));

        Assert.Null(state.SelectedListId);
        Assert.Equal(items[1].Id, state.SelectedItemId);
        Assert.Equal(2, state.TriageNumber);
        Assert.Equal(4, state.TriageTotal);

        // The arrival is the last row and the last number, reached by stepping.
        state.Step(1);
        state.Step(1);
        Assert.Equal("Arrived", state.SelectedItem!.Title);
        Assert.Equal(4, state.TriageNumber);
    }

    // --- Stepping from an item that has left the rows --------------------------

    /// <summary>An item archived elsewhere — a sync, the phone — while it is
    /// being read leaves the rows but stays shown. Next is the row that slid
    /// into its place and previous the one above it; the count holds the item
    /// that left, decided.</summary>
    [Fact]
    public async Task Stepping_from_an_item_that_left_the_rows_goes_to_its_neighbours()
    {
        var (state, items, inbox) = await LoadedWithInbox(("A", Open), ("B", Open), ("C", Open), ("D", Open));
        state.SelectItem(items[0].Id);
        state.SetTriageMode(true);
        state.Step(1);

        await inbox.ArchiveAsync(items[1].Id, TestContext.Current.CancellationToken);
        await state.ReloadAsync();
        Assert.Equal(items[1].Id, state.SelectedItemId);
        Assert.Equal(-1, state.TriagePosition);

        state.Step(1);

        Assert.Equal(items[2].Id, state.SelectedItemId);
        Assert.Equal(3, state.TriageNumber);
        Assert.Equal(4, state.TriageTotal);
        Assert.Equal(1, state.TriageProgressed);

        await inbox.ArchiveAsync(items[2].Id, TestContext.Current.CancellationToken);
        await state.ReloadAsync();

        state.Step(-1);

        Assert.Equal(items[0].Id, state.SelectedItemId);
        Assert.Equal(1, state.TriageNumber);
        Assert.Equal(4, state.TriageTotal);
        Assert.Equal(2, state.TriageProgressed);
    }

    /// <summary>The last row leaving with nothing after it: next has nowhere to
    /// go that way, so it takes the nearest open row above.</summary>
    [Fact]
    public async Task Stepping_on_from_the_last_row_after_it_left_takes_the_row_above()
    {
        var (state, items, inbox) = await LoadedWithInbox(("A", Open), ("B", Open), ("C", Open));
        state.SelectItem(items[2].Id);
        state.SetTriageMode(true);

        await inbox.ArchiveAsync(items[2].Id, TestContext.Current.CancellationToken);
        await state.ReloadAsync();

        state.Step(1);

        Assert.Equal(items[1].Id, state.SelectedItemId);
        Assert.Equal(2, state.TriageNumber);
        Assert.Equal(3, state.TriageTotal);
    }

    /// <summary>An item routed elsewhere while it is being read stays in the
    /// rows, routed; j and k step on from it to the open rows either side.</summary>
    [Fact]
    public async Task Stepping_from_an_item_routed_elsewhere_steps_to_the_open_rows_either_side()
    {
        var (state, items, inbox) = await LoadedWithInbox(("A", Open), ("B", Open), ("C", Open));
        state.SelectItem(items[0].Id);
        state.SetTriageMode(true);
        state.Step(1);

        await inbox.RouteToBacklogAsync(items[1].Id, TestContext.Current.CancellationToken);
        await state.ReloadAsync();
        Assert.Equal(1, state.TriagePosition);

        state.Step(1);
        Assert.Equal(items[2].Id, state.SelectedItemId);

        state.Step(-1);
        Assert.Equal(items[0].Id, state.SelectedItemId);
        Assert.Equal(1, state.TriageNumber);
        Assert.Equal(3, state.TriageTotal);
        Assert.Equal(1, state.TriageProgressed);
    }

    // --- Decisions and undo while triage runs ---------------------------------

    /// <summary>A route moves on and counts as progress without changing the
    /// total; Undo puts the row back as it was, selects it, takes the progress
    /// back, and stepping carries on from it.</summary>
    [Fact]
    public async Task A_route_and_its_undo_keep_the_count_and_stepping_consistent()
    {
        var (state, items) = await Loaded(("A", Open), ("B", Open), ("C", Open));
        state.SelectItem(items[0].Id);
        state.SetTriageMode(true);

        await state.RouteToBacklogAsync();

        Assert.Equal(items[1].Id, state.SelectedItemId);
        Assert.Equal(2, state.TriageNumber);
        Assert.Equal(3, state.TriageTotal);
        Assert.Equal(1, state.TriageProgressed);

        Assert.True(await state.UndoLatestAsync());

        Assert.Equal(items[0].Id, state.SelectedItemId);
        Assert.Equal(InboxStatus.Unprocessed, state.SelectedItem!.Status);
        Assert.Null(state.SelectedItem.Routing);
        Assert.Equal(0, state.TriagePosition);
        Assert.Equal(1, state.TriageNumber);
        Assert.Equal(3, state.TriageTotal);
        Assert.Equal(0, state.TriageProgressed);

        state.Step(1);
        Assert.Equal(items[1].Id, state.SelectedItemId);
        Assert.Equal(2, state.TriageNumber);
    }

    /// <summary>The same with a decision that takes the row out of the rows:
    /// the undo brings it back into its place, and both directions step from it.</summary>
    [Fact]
    public async Task An_archive_undone_in_the_middle_puts_the_row_back_in_its_place()
    {
        var (state, items) = await Loaded(("A", Open), ("B", Open), ("C", Open));
        state.SelectItem(items[0].Id);
        state.SetTriageMode(true);
        state.Step(1);

        await state.ArchiveAsync();

        Assert.Equal(items[2].Id, state.SelectedItemId);
        Assert.Equal(["A", "C"], state.VisibleItems.Select(item => item.Title));
        Assert.Equal(3, state.TriageNumber);
        Assert.Equal(1, state.TriageProgressed);

        Assert.True(await state.UndoLatestAsync());

        Assert.Equal(items[1].Id, state.SelectedItemId);
        Assert.Equal(["A", "B", "C"], state.VisibleItems.Select(item => item.Title));
        Assert.Equal(1, state.TriagePosition);
        Assert.Equal(2, state.TriageNumber);
        Assert.Equal(3, state.TriageTotal);
        Assert.Equal(0, state.TriageProgressed);

        state.Step(1);
        Assert.Equal(items[2].Id, state.SelectedItemId);
        state.Step(-1);
        state.Step(-1);
        Assert.Equal(items[0].Id, state.SelectedItemId);
    }

    private const InboxStatus Open = InboxStatus.Unprocessed;

    private const InboxStatus Routed = InboxStatus.Triaged;

    /// <summary>Seeds the rows newest first, so they show in the order given.</summary>
    private async Task<(InboxDesktopState State, IReadOnlyList<InboxItemDto> Items)> Loaded(params (string Title, InboxStatus Status)[] rows)
    {
        var (state, items, _) = await LoadedWithInbox(rows);
        return (state, items);
    }

    /// <summary><see cref="Loaded"/>, with the fake behind it, for a test that
    /// changes an item the way a sync would — out from under the pane.</summary>
    private async Task<(InboxDesktopState State, IReadOnlyList<InboxItemDto> Items, FakeInboxItems Inbox)> LoadedWithInbox(params (string Title, InboxStatus Status)[] rows)
    {
        var inbox = new FakeInboxItems();
        var items = rows
            .Select((row, index) => inbox.Seed(row.Title, status: row.Status, capturedAt: inbox.Now.AddMinutes(rows.Length - index)))
            .ToList();

        var state = await LoadedOn(inbox);

        Assert.Equal(rows.Select(row => row.Title), state.VisibleItems.Select(item => item.Title));
        return (state, items, inbox);
    }

    private async Task<InboxDesktopState> LoadedOn(FakeInboxItems inbox)
    {
        var settings = new GitHubSettingsStore(Path.Combine(_root, "github.json"));
        var state = new InboxDesktopState(inbox, settings);
        await state.ReloadAsync();
        return state;
    }
}
