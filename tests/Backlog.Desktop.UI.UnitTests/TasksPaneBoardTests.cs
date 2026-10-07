using Backlog.Modules.Tasks.Abstractions;
using Backlog.UI.Components.Tasks;
using Bunit;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The Tasks pane in its Board layout: the same filtered rows in columns under the
/// same filter bar and beside the same detail panel, grouped four ways, adding up
/// each column, and moving a card between status columns only where the lifecycle
/// lets it go.
/// </summary>
public sealed class TasksPaneBoardTests
{
    private static IRenderedComponent<TasksPane> RenderBoard(
        TasksPaneHost host,
        TaskBoardGrouping grouping = TaskBoardGrouping.Status,
        Action<TaskBoardGrouping>? changed = null) =>
        host.Context.Render<TasksPane>(parameters =>
        {
            parameters.Add(pane => pane.Layout, TasksLayout.Board).Add(pane => pane.BoardGrouping, grouping);
            if (changed is not null) parameters.Add(pane => pane.BoardGroupingChanged, changed);
        });

    private static string[] ColumnTitles(IRenderedComponent<TasksPane> pane) =>
        [.. pane.FindAll("[data-testid='board-column-title']").Select(title => title.TextContent)];

    private static string[] CardIdsIn(IRenderedComponent<TasksPane> pane, string columnKey) =>
        [.. pane.Find($"[data-board-column='{columnKey}']")
            .QuerySelectorAll("[data-testid='board-card']")
            .Select(card => card.GetAttribute("data-task-id")!)];

    private static string Text(IRenderedComponent<TasksPane> pane, string columnKey, string testId) =>
        pane.Find($"[data-board-column='{columnKey}'] [data-testid='{testId}']").TextContent.Trim();

    [Fact]
    public async Task The_board_replaces_the_list_under_the_same_filter_bar_with_the_four_status_columns()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var draft = await host.WriteEntryAsync("# Sketch the board\n`task`\n");
        var ready = await host.WriteEntryAsync("# Build the board\n`task` `!ready`\n");
        var started = await host.WriteEntryAsync("# Extract the card\n`task` `!in-progress`\n");
        var done = await host.WriteEntryAsync("# Ship the switch\n`task` `!done`\n");

        var pane = RenderBoard(host);

        Assert.NotEmpty(pane.FindAll(".filter-bar"));
        Assert.Empty(pane.FindAll("[data-testid='entry-list']"));
        Assert.Equal(["Draft", "Ready", "In progress", "Done"], ColumnTitles(pane));
        Assert.Equal([draft.TaskId], CardIdsIn(pane, "Draft"));
        Assert.Equal([ready.TaskId], CardIdsIn(pane, "Ready"));
        Assert.Equal([started.TaskId], CardIdsIn(pane, "InProgress"));
        Assert.Equal([done.TaskId], CardIdsIn(pane, "Done"));

        // The Columns picker leads the bar; the status radiogroup is off it, since
        // every status is a column.
        Assert.Equal("Status", pane.Find("#board-columns").GetAttribute("value"));
        Assert.Empty(pane.FindAll("[role='radiogroup'][aria-label='Filter by status']"));
        var bar = pane.Find(".filter-bar");
        Assert.Contains("filter-group--columns", bar.FirstElementChild!.ClassName);
    }

    [Fact]
    public async Task The_board_reads_the_filtered_rows_and_sets_the_status_filter_aside_only_while_columns_are_statuses()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var ready = await host.WriteEntryAsync("# Build the board\n`task` `!ready` `+task-views`\n");
        var draft = await host.WriteEntryAsync("# Sketch the board\n`task` `+task-views`\n");
        var other = await host.WriteEntryAsync("# Rotate the key\n`task` `!ready` `+sync-hardening`\n");

        // The list was narrowed to Ready before the reader switched to the Board.
        host.State.SetStatusFilter("ready");

        var pane = RenderBoard(host);

        // By status, every status is a column: the filter is set aside, not lost.
        Assert.True(host.State.StatusFilterSuspended);
        Assert.Equal("ready", host.State.SelectedStatusFilterWire);
        Assert.Equal([draft.TaskId], CardIdsIn(pane, "Draft"));

        // The rest of the filter bar applies unchanged.
        host.State.ToggleTagFilter("+task-views");
        pane.WaitForAssertion(() =>
        {
            Assert.Equal([ready.TaskId], CardIdsIn(pane, "Ready"));
            Assert.DoesNotContain(other.TaskId, CardIdsIn(pane, "Ready"));
        });

        // By priority the statuses are back on the bar and narrow again.
        pane.Render(parameters => parameters.Add(p => p.BoardGrouping, TaskBoardGrouping.Priority));
        pane.WaitForAssertion(() =>
        {
            Assert.False(host.State.StatusFilterSuspended);
            Assert.NotEmpty(pane.FindAll("[role='radiogroup'][aria-label='Filter by status']"));
            Assert.Equal([ready.TaskId], CardIdsIn(pane, "Medium"));
        });
    }

    [Fact]
    public async Task Columns_by_plan_are_one_per_plan_tag_then_no_plan_and_add_up_their_points()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var board = await host.WriteEntryAsync("# Build the board\n`task` `!ready` `+task-views` `effort:8`\n");
        var shared = await host.WriteEntryAsync("# Plan facet\n`task` `+task-views` `+sync-hardening` `effort:3`\n");
        var guess = await host.WriteEntryAsync("# Swimlanes\n`idea` `+task-views`\n");
        var loose = await host.WriteEntryAsync("# Banner copy\n`task` `#ux` `effort:1`\n");

        var pane = RenderBoard(host, TaskBoardGrouping.Plan);

        Assert.Equal(["+sync-hardening", "+task-views", "No plan"], ColumnTitles(pane));
        Assert.Equal([board.TaskId, shared.TaskId, guess.TaskId], CardIdsIn(pane, "plan:+task-views"));
        Assert.Equal([shared.TaskId], CardIdsIn(pane, "plan:+sync-hardening"));
        Assert.Equal([loose.TaskId], CardIdsIn(pane, "plan:"));

        Assert.Equal("3", Text(pane, "plan:+task-views", "board-column-count"));
        Assert.Equal("11 pts · 1 not estimated", Text(pane, "plan:+task-views", "board-column-points"));
        Assert.Equal("1 pt", Text(pane, "plan:", "board-column-points"));

        // Status is no column here, so the statuses stay on the bar, and nothing
        // is draggable and nothing is added from a plan column.
        Assert.NotEmpty(pane.FindAll("[role='radiogroup'][aria-label='Filter by status']"));
        Assert.All(pane.FindAll("[data-testid='board-card']"), card => Assert.Null(card.GetAttribute("data-draggable")));
        Assert.Empty(pane.FindAll("[data-testid='board-column-add']"));
    }

    [Fact]
    public async Task Columns_by_priority_run_highest_first_and_by_repository_follow_the_configured_ones()
    {
        using var host = await TasksPaneHost.CreateAsync("backlog = JSdotNet/Backlog", "sync = JSdotNet/Backlog-Sync");
        host.Features.SetEnabled(TasksFeatures.AdditionalRepositories, enabled: true);
        var critical = await host.WriteEntryAsync("# Rotate the key\n`task` `*critical` `repo:sync`\n");
        var low = await host.WriteEntryAsync("# Banner copy\n`task` `*low` `repo:backlog`\n");
        var plain = await host.WriteEntryAsync("# Hours report\n`task`\n");

        var pane = RenderBoard(host, TaskBoardGrouping.Priority);

        Assert.Equal(["Critical", "High", "Medium", "Low"], ColumnTitles(pane));
        Assert.Equal([critical.TaskId], CardIdsIn(pane, "Critical"));
        Assert.Equal([plain.TaskId], CardIdsIn(pane, "Medium"));
        Assert.Equal([low.TaskId], CardIdsIn(pane, "Low"));
        Assert.Empty(CardIdsIn(pane, "High"));

        pane.Render(parameters => parameters.Add(p => p.BoardGrouping, TaskBoardGrouping.Repository));

        pane.WaitForAssertion(() =>
        {
            Assert.Equal(["backlog", "sync", "No repository"], ColumnTitles(pane));
            Assert.Equal([low.TaskId], CardIdsIn(pane, "repo:backlog"));
            Assert.Equal([critical.TaskId], CardIdsIn(pane, "repo:sync"));
            Assert.Equal([plain.TaskId], CardIdsIn(pane, "repo:"));
        });
    }

    [Fact]
    public async Task Dropping_a_ready_card_on_in_progress_runs_the_status_change_the_row_selector_does()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var ready = await host.WriteEntryAsync("# Build the board\n`task` `!ready`\n");

        var pane = RenderBoard(host);
        var board = pane.FindComponent<TaskBoard>();

        await board.InvokeAsync(() => board.Instance.PointerDragStart(ready.TaskId));

        var target = pane.Find("[data-board-column='InProgress']");
        Assert.Contains("task-board__column--target", target.ClassName);
        Assert.Equal("Drop to start", Text(pane, "InProgress", "board-column-slot-text"));
        Assert.Equal("Moves to In progress and stamps Started", Text(pane, "InProgress", "board-column-slot-detail"));

        await board.InvokeAsync(() => board.Instance.PointerDragOver("InProgress"));
        await board.InvokeAsync(() => board.Instance.PointerDragEnd());

        Assert.Equal(EntryStatus.InProgress, ready.PreviewStatus);
        pane.WaitForAssertion(() =>
        {
            Assert.Equal([ready.TaskId], CardIdsIn(pane, "InProgress"));
            Assert.Equal("Moved Build the board to In progress.", pane.Find("[data-testid='board-announcement']").TextContent);
        });

        // What was written is what the store holds, not only what the row previews.
        await host.State.ReloadFromStoreAsync();
        var reloaded = Assert.Single(host.State.Rows, row => row.Id == ready.Id);
        Assert.Equal(EntryStatus.InProgress, reloaded.PreviewStatus);
    }

    /// <summary>An entry moved back out of progress keeps the day it first
    /// started, so the slot over In progress says only that it starts and does
    /// not promise a stamp the drop will not write.</summary>
    [Fact]
    public async Task An_entry_already_started_is_offered_the_start_without_a_new_stamp()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var ready = await host.WriteEntryAsync("# Build the board\n`task` `!ready` `started:2026-10-01`\n");

        var pane = RenderBoard(host);
        var board = pane.FindComponent<TaskBoard>();

        await board.InvokeAsync(() => board.Instance.PointerDragStart(ready.TaskId));

        Assert.Equal("Drop to start", Text(pane, "InProgress", "board-column-slot-text"));
        Assert.Empty(pane.FindAll("[data-board-column='InProgress'] [data-testid='board-column-slot-detail']"));
    }

    [Fact]
    public async Task Dropping_a_ready_card_on_done_is_refused_with_the_step_it_needs_first()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var ready = await host.WriteEntryAsync("# Build the board\n`task` `!ready`\n");

        var pane = RenderBoard(host);
        var board = pane.FindComponent<TaskBoard>();

        await board.InvokeAsync(() => board.Instance.PointerDragStart(ready.TaskId));

        Assert.Contains("task-board__column--refused", pane.Find("[data-board-column='Done']").ClassName);
        Assert.Equal("Ready can't move straight to Done — start it first", Text(pane, "Done", "board-column-refusal"));

        await board.InvokeAsync(() => board.Instance.PointerDragOver("Done"));
        await board.InvokeAsync(() => board.Instance.PointerDragEnd());

        Assert.Equal(EntryStatus.Ready, ready.PreviewStatus);
        Assert.Equal([ready.TaskId], CardIdsIn(pane, "Ready"));
    }

    /// <summary>Every pair of board statuses, against the graph: an allowed move
    /// says what it does, a refused one names the step it needs or where it can go.</summary>
    [Fact]
    public void The_drop_rule_is_the_lifecycle_graph_edge_for_edge()
    {
        EntryStatus[] statuses = [EntryStatus.Draft, EntryStatus.Ready, EntryStatus.InProgress, EntryStatus.Done];

        foreach (var from in statuses)
        {
            foreach (var to in statuses.Where(to => to != from))
            {
                var rule = TasksPane.DropRule(from, to);
                Assert.Equal(EntryStatusFlow.IsAllowed(from, to), rule.Allowed);
                if (!rule.Allowed) Assert.Contains(" can't move straight to ", rule.Text, StringComparison.Ordinal);
            }
        }

        Assert.Equal("Drop to start", TasksPane.DropRule(EntryStatus.Ready, EntryStatus.InProgress).Text);
        Assert.Equal("Moves to In progress and stamps Started", TasksPane.DropRule(EntryStatus.Ready, EntryStatus.InProgress).Detail);
        Assert.Null(TasksPane.DropRule(EntryStatus.Ready, EntryStatus.InProgress, started: true).Detail);
        Assert.Null(TasksPane.DropRule(EntryStatus.Done, EntryStatus.InProgress, started: true).Detail);
        // A Done entry with no stamp (typed `!done`, an import) gets one on reopen.
        Assert.Equal("Moves to In progress and stamps Started", TasksPane.DropRule(EntryStatus.Done, EntryStatus.InProgress).Detail);
        Assert.Null(TasksPane.DropRule(EntryStatus.InProgress, EntryStatus.Done).Detail);
        Assert.Equal("Drop to finish", TasksPane.DropRule(EntryStatus.InProgress, EntryStatus.Done).Text);
        Assert.Equal("Drop to reopen", TasksPane.DropRule(EntryStatus.Done, EntryStatus.InProgress).Text);
        Assert.Equal("Draft can't move straight to In progress — mark it ready first", TasksPane.DropRule(EntryStatus.Draft, EntryStatus.InProgress).Text);
        Assert.Equal("Draft can't move straight to Done — it can move to Ready from here", TasksPane.DropRule(EntryStatus.Draft, EntryStatus.Done).Text);
        Assert.Equal("Done can't move straight to Ready — reopen it first", TasksPane.DropRule(EntryStatus.Done, EntryStatus.Ready).Text);
    }

    [Fact]
    public async Task New_entry_at_the_foot_of_a_status_column_creates_the_entry_in_that_status()
    {
        using var host = await TasksPaneHost.CreateAsync();
        await host.WriteEntryAsync("# Build the board\n`task` `!ready`\n");

        var pane = RenderBoard(host);

        var adds = pane.FindAll("[data-testid='board-column-add']");
        Assert.Equal(4, adds.Count);

        pane.Find("[data-board-column='InProgress'] [data-testid='board-column-add']").Click();

        pane.WaitForAssertion(() =>
        {
            var created = host.State.SelectedRow;
            Assert.NotNull(created);
            Assert.False(created.IsPersisted);
            Assert.Equal(EntryStatus.InProgress, created.PreviewStatus);
            Assert.Contains(created.TaskId, CardIdsIn(pane, "InProgress"));
            Assert.Single(pane.FindAll("[data-testid='entry-detail']"));
        });
    }

    /// <summary>A ticked-off card sits in Done whatever its status says; moving it
    /// out is judged from the status it records and takes the tick away with it,
    /// so it does not land straight back in Done.</summary>
    [Fact]
    public async Task A_ticked_card_moved_out_of_done_is_judged_by_its_status_and_loses_its_tick()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var ticked = await host.WriteEntryAsync("# Banner copy\n`task` `!in-progress` `completed:2026-10-01`\n");
        Assert.True(ticked.IsPreviewCompleted);

        var pane = RenderBoard(host);
        Assert.Equal([ticked.TaskId], CardIdsIn(pane, "Done"));
        var board = pane.FindComponent<TaskBoard>();

        await board.InvokeAsync(() => board.Instance.PointerDragStart(ticked.TaskId));

        // From In progress, Ready is one move back and Draft is not.
        Assert.Equal("Drop to move back to Ready", Text(pane, "Ready", "board-column-slot-text"));
        Assert.Contains("task-board__column--refused", pane.Find("[data-board-column='Draft']").ClassName);

        await board.InvokeAsync(() => board.Instance.PointerDragOver("Ready"));
        await board.InvokeAsync(() => board.Instance.PointerDragEnd());

        Assert.Equal(EntryStatus.Ready, ticked.PreviewStatus);
        Assert.False(ticked.IsPreviewCompleted);
        pane.WaitForAssertion(() => Assert.Equal([ticked.TaskId], CardIdsIn(pane, "Ready")));
    }

    [Fact]
    public async Task Archived_entries_have_no_status_column_but_count_under_the_other_groupings()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var archived = await host.WriteEntryAsync("# Old idea\n`task` `!archived` `*high`\n");

        var pane = RenderBoard(host);
        Assert.Empty(pane.FindAll($"[data-task-id='{archived.TaskId}']"));

        pane.Render(parameters => parameters.Add(p => p.BoardGrouping, TaskBoardGrouping.Priority));
        pane.WaitForAssertion(() => Assert.Equal([archived.TaskId], CardIdsIn(pane, "High")));
    }

    [Fact]
    public async Task Pressing_a_card_opens_the_existing_detail_panel()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var ready = await host.WriteEntryAsync("# Build the board\n`task` `!ready`\n");
        await host.State.SelectAsync(null);

        var pane = RenderBoard(host);
        Assert.Empty(pane.FindAll("[data-testid='entry-detail']"));

        pane.Find($"[data-board-column='Ready'] [data-task-id='{ready.TaskId}']").Click();

        pane.WaitForAssertion(() =>
        {
            Assert.Single(pane.FindAll("[data-testid='entry-detail']"));
            Assert.Same(ready, host.State.SelectedRow);
            Assert.Equal("true", pane.Find($"[data-task-id='{ready.TaskId}']").GetAttribute("aria-current"));
        });
    }

    [Fact]
    public async Task Picking_columns_raises_the_choice_for_the_shell_to_remember()
    {
        using var host = await TasksPaneHost.CreateAsync();
        await host.WriteEntryAsync("# Build the board\n`task` `!ready` `+task-views`\n");
        var picked = new List<TaskBoardGrouping>();

        var pane = RenderBoard(host, changed: picked.Add);

        pane.Find("#board-columns").Change("Plan");

        pane.WaitForAssertion(() =>
        {
            Assert.Equal([TaskBoardGrouping.Plan], picked);
            Assert.Equal(["+task-views", "No plan"], ColumnTitles(pane));
        });
    }

    [Fact]
    public async Task Leaving_the_board_applies_the_status_filter_again()
    {
        using var host = await TasksPaneHost.CreateAsync();
        await host.WriteEntryAsync("# Build the board\n`task` `!ready`\n");
        host.State.SetStatusFilter("ready");

        var pane = RenderBoard(host);
        Assert.True(host.State.StatusFilterSuspended);

        pane.Render(parameters => parameters.Add(p => p.Layout, TasksLayout.List));
        Assert.False(host.State.StatusFilterSuspended);
        Assert.NotEmpty(pane.FindAll("[data-testid='entry-list']"));
    }
}
