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

    private const InboxStatus Open = InboxStatus.Unprocessed;

    private const InboxStatus Routed = InboxStatus.Triaged;

    /// <summary>Seeds the rows newest first, so they show in the order given.</summary>
    private async Task<(InboxDesktopState State, IReadOnlyList<InboxItemDto> Items)> Loaded(params (string Title, InboxStatus Status)[] rows)
    {
        var inbox = new FakeInboxItems();
        var items = rows
            .Select((row, index) => inbox.Seed(row.Title, status: row.Status, capturedAt: inbox.Now.AddMinutes(rows.Length - index)))
            .ToList();

        var settings = new GitHubSettingsStore(Path.Combine(_root, "github.json"));
        var state = new InboxDesktopState(inbox, settings);
        await state.ReloadAsync();

        Assert.Equal(rows.Select(row => row.Title), state.VisibleItems.Select(item => item.Title));
        return (state, items);
    }
}
