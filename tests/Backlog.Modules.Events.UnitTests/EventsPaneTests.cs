using Backlog.Modules.Events.Abstractions;

namespace Backlog.Modules.Events.UnitTests;

/// <summary>
/// The Events pane's port, over in-memory stores and the task and Brain targets at the boundary
/// (<c>.devbook/domain/events/requirements.md#events-pane</c>, <c>#prep-tasks</c>). Each test
/// names the scenario it proves.
/// </summary>
public sealed class EventsPaneTests : EventsPaneFixture
{
    // --- AC-1 A quick entry ---------------------------------------------------------------

    [Fact]
    public async Task A_title_alone_adds_one_spotted_event()
    {
        var added = await Pane.AddAsync("dotNET Friday, 27 November", cancellationToken: Token);

        Assert.True(added.IsSuccess);
        var groups = await Pane.ListAsync(Token);
        var group = Assert.Single(groups);
        Assert.Equal(KnowledgeEventState.Spotted, group.State);
        var listed = Assert.Single(group.Events);
        Assert.Equal("dotNET Friday, 27 November", listed.Title);
        Assert.Equal(added.Value.Id, listed.Id);
    }

    [Fact]
    public async Task A_blank_title_is_refused_and_nothing_is_saved()
    {
        var added = await Pane.AddAsync("  ", cancellationToken: Token);

        Assert.Equal("knowledge_event.title_missing", added.Error.Code);
        Assert.Empty(await Pane.ListAsync(Token));
        Assert.Equal(0, Store.Saves);
    }

    [Fact]
    public async Task An_event_the_store_does_not_hold_is_not_found()
    {
        var read = await Pane.GetAsync(Guid.NewGuid(), Token);

        Assert.Equal("knowledge_event.not_found", read.Error.Code);
    }

    // --- AC-11 Grouped by state, upcoming first -----------------------------------------

    [Fact]
    public async Task The_list_groups_by_state_in_decided_order_and_leaves_empty_groups_out()
    {
        var spotted = await Add("Spotted one");
        var going = await Add("Going one");
        await Pane.MoveToAsync(going, KnowledgeEventState.Going, Token);
        var skipped = await Add("Skipped one");
        await Pane.SkipAsync(skipped, "Clashes with the release", Token);
        var considering = await Add("Considering one");
        await Pane.MoveToAsync(considering, KnowledgeEventState.Considering, Token);

        var groups = await Pane.ListAsync(Token);

        Assert.Equal(
            [KnowledgeEventState.Going, KnowledgeEventState.Considering, KnowledgeEventState.Spotted, KnowledgeEventState.Skipped],
            groups.Select(group => group.State));
    }

    [Fact]
    public async Task Within_a_group_upcoming_come_soonest_first_then_undated_then_past_most_recent_first()
    {
        await Dated("Last year", new DateOnly(2025, 11, 1));
        await Add("Undated");
        await Dated("Next month", new DateOnly(2026, 12, 4));
        await Dated("Today", new DateOnly(2026, 11, 10));
        await Dated("Last week", new DateOnly(2026, 11, 3));

        var group = Assert.Single(await Pane.ListAsync(Token));

        Assert.Equal(["Today", "Next month", "Undated", "Last week", "Last year"], group.Events.Select(e => e.Title));
    }

    [Fact]
    public async Task An_event_still_running_today_counts_as_upcoming_and_one_that_ended_yesterday_as_past()
    {
        var running = await Add("Running since yesterday");
        Assert.True((await Pane.SetDatesAsync(running, new EventDayDto(new DateOnly(2026, 11, 9)), new EventDayDto(new DateOnly(2026, 11, 10)), Token)).IsSuccess);
        var ended = await Add("Ended yesterday");
        Assert.True((await Pane.SetDatesAsync(ended, new EventDayDto(new DateOnly(2026, 11, 8)), new EventDayDto(new DateOnly(2026, 11, 9)), Token)).IsSuccess);
        await Dated("Next month", new DateOnly(2026, 12, 4));
        await Add("Undated");

        var group = Assert.Single(await Pane.ListAsync(Token));

        Assert.Equal(["Running since yesterday", "Next month", "Undated", "Ended yesterday"], group.Events.Select(e => e.Title));
    }
}
