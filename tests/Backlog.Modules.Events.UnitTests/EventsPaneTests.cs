using Backlog.Modules.Events.Abstractions;
using Backlog.SharedKernel.Results;

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

        Assert.IsType<Ok<KnowledgeEventDto>>(added.Value);
        var groups = await Pane.ListAsync(Token);
        var group = Assert.Single(groups);
        Assert.Equal(KnowledgeEventState.Spotted, group.State);
        var listed = Assert.Single(group.Events);
        Assert.Equal("dotNET Friday, 27 November", listed.Title);
        Assert.Equal(Assert.IsType<Ok<KnowledgeEventDto>>(added.Value).Value.Id, listed.Id);
    }

    [Fact]
    public async Task A_blank_title_is_refused_and_nothing_is_saved()
    {
        var added = await Pane.AddAsync("  ", cancellationToken: Token);

        Assert.Equal("knowledge_event.title_missing", Assert.IsType<Error>(added.Value).Code);
        Assert.Empty(await Pane.ListAsync(Token));
        Assert.Equal(0, Store.Saves);
    }

    [Fact]
    public async Task An_event_the_store_does_not_hold_is_not_found()
    {
        var read = await Pane.GetAsync(Guid.NewGuid(), Token);

        Assert.Equal("knowledge_event.not_found", Assert.IsType<Error>(read.Value).Code);
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
        Assert.IsType<Ok<KnowledgeEventDto>>((await Pane.SetDatesAsync(running, new EventDateDto(new DateOnly(2026, 11, 9)), new EventDateDto(new DateOnly(2026, 11, 10)), Token)).Value);
        var ended = await Add("Ended yesterday");
        Assert.IsType<Ok<KnowledgeEventDto>>((await Pane.SetDatesAsync(ended, new EventDateDto(new DateOnly(2026, 11, 8)), new EventDateDto(new DateOnly(2026, 11, 9)), Token)).Value);
        await Dated("Next month", new DateOnly(2026, 12, 4));
        await Add("Undated");

        var group = Assert.Single(await Pane.ListAsync(Token));

        Assert.Equal(["Running since yesterday", "Next month", "Undated", "Ended yesterday"], group.Events.Select(e => e.Title));
    }

    // --- Saves made elsewhere -----------------------------------------------------------

    /// <summary>Another window, the MCP server, the To Event route: each saves through a pane
    /// or a service of its own, and an open pane hears it on <see cref="IEventsPane.Changed"/>.</summary>
    [Fact]
    public async Task A_save_made_through_another_pane_reaches_this_one_until_it_unsubscribes()
    {
        var heard = 0;
        void Listen() => heard++;
        var elsewhere = Elsewhere();

        Pane.Changed += Listen;
        await elsewhere.AddAsync(new NewKnowledgeEventDto("dotNET Friday", SeriesName: "dotNET Friday"), Token);
        await elsewhere.AddAsync("NDC Oslo", cancellationToken: Token);
        Pane.Changed -= Listen;
        await elsewhere.AddAsync("Techorama", cancellationToken: Token);

        Assert.Equal(2, heard);
    }

    /// <summary>The pane's own saves answer the event as saved, so they are not heard back: a
    /// notice for one would read the open event again while the person types in it.</summary>
    [Fact]
    public async Task The_panes_own_saves_are_not_heard_back()
    {
        var heard = 0;
        Pane.Changed += () => heard++;
        var elsewhere = 0;
        Elsewhere().Changed += () => elsewhere++;

        var id = Assert.IsType<Ok<KnowledgeEventDto>>((await Pane.AddAsync(new NewKnowledgeEventDto("Techorama", SeriesName: "Techorama"), Token)).Value).Value.Id;
        await Pane.RenameAsync(id, "Techorama Belgium", Token);
        await Pane.StartSeriesAsync("NDC", cancellationToken: Token);
        await Pane.RemoveAsync(id, Token);

        Assert.Equal(0, heard);
        Assert.Equal(4, elsewhere);
    }

    /// <summary>Work a save starts and that runs later — the sync loop's settle timer, a
    /// <c>Task.Run</c> — carries the save's execution context, and with it the mark. Once the
    /// save is over the mark no longer counts: a save announced from that later work is one made
    /// elsewhere, and the pane hears it.</summary>
    [Fact]
    public async Task Work_started_from_the_panes_own_save_does_not_carry_the_mark_once_the_save_is_over()
    {
        var heard = 0;
        Pane.Changed += () => heard++;
        ExecutionContext? started = null;
        Announced.Changed += () => started ??= ExecutionContext.Capture();

        await Pane.AddAsync("Techorama", cancellationToken: Token);
        Assert.Equal(0, heard);

        ExecutionContext.Run(started!, _ => Announced.Raise(), null);

        Assert.Equal(1, heard);
    }

    /// <summary>Every open window hears a save on the writer's own call. One window's handler
    /// that throws does not stop the others, and does not fail a save that already landed.</summary>
    [Fact]
    public async Task A_handler_that_throws_neither_stops_the_others_nor_fails_the_save_made_elsewhere()
    {
        var heard = 0;
        Pane.Changed += () => throw new InvalidOperationException("A window's handler failed.");
        Pane.Changed += () => heard++;

        var added = await Elsewhere().AddAsync(new NewKnowledgeEventDto("NDC Oslo"), Token);

        Assert.IsType<Ok<KnowledgeEventDto>>(added.Value);
        Assert.Equal(1, heard);
    }

    /// <summary>Filing a prep task waits on Tasks between loading the event and saving it. A
    /// change saved elsewhere during that wait is kept: the link is applied to the event as it
    /// stands once the task is filed.</summary>
    [Fact]
    public async Task A_change_saved_elsewhere_while_a_prep_task_is_filed_is_kept()
    {
        var id = await Add("Techorama");
        Tasks.FileGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var adding = Pane.AddPrepTaskAsync(id, "Book hotel in Antwerp", Token);
        Assert.IsType<Ok<KnowledgeEventDto>>((await Elsewhere().RenameAsync(id, "Techorama Belgium", Token)).Value);
        Tasks.FileGate.SetResult();
        var added = await adding;

        Assert.IsType<Ok<KnowledgeEventDto>>(added.Value);
        var saved = Assert.IsType<Ok<KnowledgeEventDto>>((await Pane.GetAsync(id, Token)).Value).Value;
        Assert.Equal("Techorama Belgium", saved.Title);
        Assert.Single(saved.Tasks);
    }
}
