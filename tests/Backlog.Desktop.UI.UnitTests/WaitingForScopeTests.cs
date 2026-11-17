using Bunit;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The <b>Waiting</b> slice in the filter bar, and the waiting-for controls in the
/// open entry (local ADR 0054 §4, <c>.devbook/domain/tasks/domain.md#waiting-for</c>).
/// <para>
/// The slice keeps the open tasks waiting on someone, soonest follow-up first and
/// undated last. A scope like My Day beside it: it narrows what the other scopes
/// already left in view.
/// </para>
/// </summary>
[Collection(WorkspaceSettingsCollection.Name)]
public sealed class WaitingForScopeTests
{
    private const string Chip = "[data-testid='waitingfor-filter-option']";

    private static string RowTestId(EntryRow row) => $"entry-list-{row.TaskId}";

    /// <summary>Five entries: three waiting — late, soon and undated, written in an
    /// order that is none of those — one ticked off while it still waits, and one
    /// that waits on nobody.</summary>
    private static async Task<(TasksPaneHost Host, EntryRow Undated, EntryRow Later, EntryRow Sooner, EntryRow Ticked, EntryRow Plain)> FiveAsync()
    {
        var host = await TasksPaneHost.CreateAsync();

        var undated = await host.WriteEntryAsync("# Hear back from Anna\n`task` `!ready` `waiting:Anna`\n");
        var later = await host.WriteEntryAsync("# Get the quote\n`task` `!ready` `waiting:Jansen Roofing` `followup:2026-12-01`\n");
        var sooner = await host.WriteEntryAsync("# Get the contract signed\n`task` `!ready` `waiting:Legal` `followup:2026-11-20`\n");
        var ticked = await host.WriteEntryAsync("# Get the keys\n`task` `!done` `waiting:Landlord` `followup:2026-11-01` `completed:2026-11-02`\n");
        var plain = await host.WriteEntryAsync("# Paint the fence\n`task` `!ready`\n");

        // Writing leaves the last entry open, and an open entry is pinned into view
        // whatever the filters say.
        await host.State.SelectAsync(null);

        return (host, undated, later, sooner, ticked, plain);
    }

    [Fact]
    public async Task The_slice_starts_off_and_counts_the_open_tasks_waiting_on_someone()
    {
        var (host, _, _, _, _, _) = await FiveAsync();
        using var _host = host;

        var pane = host.Render();
        var chip = pane.Find(Chip);

        Assert.Equal("false", chip.GetAttribute("aria-pressed"));
        Assert.False(host.State.WaitingForOnly);
        Assert.Equal("3", chip.QuerySelector(".chip__count")?.TextContent);
    }

    [Fact]
    public async Task The_slice_keeps_the_waiting_tasks_soonest_follow_up_first_and_undated_last()
    {
        var (host, undated, later, sooner, ticked, plain) = await FiveAsync();
        using var _host = host;

        var pane = host.Render();
        await pane.Find(Chip).ClickAsync(new());

        Assert.Equal("true", pane.Find(Chip).GetAttribute("aria-pressed"));
        Assert.True(host.State.WaitingForOnly);

        Assert.Equal([sooner, later, undated], host.State.FilteredRows);
        Assert.Empty(pane.FindAll($"[data-testid='{RowTestId(ticked)}']"));
        Assert.Empty(pane.FindAll($"[data-testid='{RowTestId(plain)}']"));
    }

    [Fact]
    public async Task Pressing_it_again_shows_everything_in_rank_order_again()
    {
        var (host, undated, later, sooner, _, plain) = await FiveAsync();
        using var _host = host;

        var pane = host.Render();
        await pane.Find(Chip).ClickAsync(new());
        await pane.Find(Chip).ClickAsync(new());

        Assert.False(host.State.WaitingForOnly);
        Assert.Contains(plain, host.State.FilteredRows);

        // Out of the slice, the list goes back to its own order.
        Assert.Equal(host.State.Rows.Where(host.State.FilteredRows.Contains), host.State.FilteredRows);
        Assert.Contains(undated, host.State.FilteredRows);
        Assert.Contains(later, host.State.FilteredRows);
        Assert.Contains(sooner, host.State.FilteredRows);
    }

    /// <summary>Clearing the waiting-for takes the task out of the slice.</summary>
    [Fact]
    public async Task Clearing_the_waiting_for_takes_a_task_out_of_the_slice()
    {
        var (host, _, _, sooner, _, _) = await FiveAsync();
        using var _host = host;

        host.State.SetWaitingForFilter(true);
        await host.State.ChangeWaitingForAsync(sooner, null);
        await host.State.SelectAsync(null);
        host.State.SetWaitingForFilter(true);

        Assert.DoesNotContain(sooner, host.State.FilteredRows);
        Assert.DoesNotContain("waiting:", sooner.RawText, StringComparison.Ordinal);
        Assert.DoesNotContain("followup:", sooner.RawText, StringComparison.Ordinal);
    }

    /// <summary>The slice is in follow-up order, not rank order, so a move there
    /// would rewrite a rank the reader cannot see: it changes nothing, and the status
    /// sort is not offered.</summary>
    [Fact]
    public async Task Reordering_and_the_status_sort_are_off_in_the_slice()
    {
        var (host, undated, _, sooner, _, _) = await FiveAsync();
        using var _host = host;

        host.State.SetWaitingForFilter(true);
        var ranks = host.State.Rows.ToList();

        await host.State.MoveEntryAsync(undated, sooner);

        Assert.Equal(ranks, host.State.Rows);
        Assert.False(host.State.CanSortVisibleByStatus);
    }

    // --- The open entry's controls ------------------------------------------

    [Fact]
    public async Task The_editor_writes_who_an_entry_waits_on_then_its_follow_up_date()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var row = await host.WriteEntryAsync("# Fix the roof\n`task` `!ready`\n");

        var pane = host.Render();

        // Nobody named yet, so there is no follow-up row: a date alone is no
        // waiting-for.
        Assert.Contains("Wait on someone", pane.Find("[data-testid='entry-action-waiting']").TextContent, StringComparison.Ordinal);
        Assert.Empty(pane.FindAll("[data-testid='entry-action-followup-on']"));

        await pane.Find("[data-testid='entry-action-waiting-set']").ClickAsync(new());
        await pane.Find("[data-testid='entry-waiting-input']").ChangeAsync(new() { Value = "Jansen Roofing" });

        Assert.Equal("Jansen Roofing", row.PreviewWaitingFor?.Who);
        Assert.Contains("`waiting:Jansen Roofing`", row.RawText, StringComparison.Ordinal);
        Assert.Contains("Jansen Roofing", pane.Find("[data-testid='entry-action-waiting']").TextContent, StringComparison.Ordinal);

        await pane.Find("[data-testid='entry-action-followup-on-set']").ClickAsync(new());
        await pane.Find("[data-testid='entry-followup-input']").ChangeAsync(new() { Value = "2099-11-20" });

        Assert.Equal(new DateOnly(2099, 11, 20), row.PreviewWaitingFor?.FollowUpOn);
        Assert.Contains("`waiting:Jansen Roofing` `followup:2099-11-20`", row.RawText, StringComparison.Ordinal);
        Assert.DoesNotContain("overdue", pane.Find("[data-testid='entry-action-followup-on']").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_editor_clears_the_follow_up_date_alone_or_the_whole_waiting_for()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var row = await host.WriteEntryAsync("# Fix the roof\n`task` `!ready` `waiting:Jansen Roofing` `followup:2099-11-20`\n");

        var pane = host.Render();

        await pane.Find("[data-testid='entry-action-followup-on-clear']").ClickAsync(new());

        Assert.Equal("Jansen Roofing", row.PreviewWaitingFor?.Who);
        Assert.Null(row.PreviewWaitingFor?.FollowUpOn);
        Assert.DoesNotContain("followup:", row.RawText, StringComparison.Ordinal);

        await pane.Find("[data-testid='entry-action-waiting-clear']").ClickAsync(new());

        Assert.Null(row.PreviewWaitingFor);
        Assert.DoesNotContain("waiting:", row.RawText, StringComparison.Ordinal);
        Assert.EndsWith("`task` `*medium` `!ready`\n", row.RawText, StringComparison.Ordinal);
    }

    /// <summary>A passed follow-up date reads overdue, as a passed reminder does: in
    /// the open entry and on the row's badge.</summary>
    [Fact]
    public async Task A_passed_follow_up_date_reads_overdue()
    {
        using var host = await TasksPaneHost.CreateAsync();
        await host.WriteEntryAsync("# Fix the roof\n`task` `!ready` `waiting:Jansen Roofing` `followup:2020-01-15`\n");

        var pane = host.Render();

        var action = pane.Find("[data-testid='entry-action-followup-on']");
        Assert.Contains("overdue", action.TextContent, StringComparison.Ordinal);
        Assert.Contains("task-action--overdue", action.ClassList);

        var badge = pane.Find("[data-testid='entry-waiting-badge']");
        Assert.Contains("overdue", badge.TextContent, StringComparison.Ordinal);
        Assert.Contains("badge--waiting-overdue", badge.ClassList);
    }

    /// <summary>A ticked-off entry is never overdue, the way a finished date is not.</summary>
    [Fact]
    public async Task A_ticked_off_entry_does_not_read_overdue()
    {
        using var host = await TasksPaneHost.CreateAsync();
        await host.WriteEntryAsync("# Fix the roof\n`task` `!done` `waiting:Jansen Roofing` `followup:2020-01-15` `completed:2020-01-20`\n");

        var pane = host.Render();

        Assert.DoesNotContain("overdue", pane.Find("[data-testid='entry-action-followup-on']").TextContent, StringComparison.Ordinal);
        Assert.DoesNotContain("overdue", pane.Find("[data-testid='entry-waiting-badge']").TextContent, StringComparison.Ordinal);
    }
}
