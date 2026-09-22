using Backlog.Modules.Tasks.DomainModels;
using Bunit;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The "Not waiting" scope in the filter bar.
/// <para>
/// <c>.domain/tasks/naming.md#readiness</c> governs: a task is done, ready when
/// everything it waits on is finished, or blocked when something is not. This
/// scope keeps the ready ones — the rows a reader could pick up now — and it
/// reads that off the same derivation the list draws its "Waiting for" lines
/// from, so the chip and the rows cannot disagree about who is waiting.
/// </para>
/// <para>
/// A scope rather than one of a set, like My Day and No repo beside it: it
/// narrows whatever the repository, the status and the tags have already left in
/// view instead of replacing any of them.
/// </para>
/// </summary>
[Collection(WorkspaceSettingsCollection.Name)]
public sealed class NotWaitingScopeTests
{
    private const string Chip = "[data-testid='notwaiting-filter-option']";

    private static string RowTestId(EntryRow row) => $"entry-list-{row.TaskId}";

    /// <summary>Five entries: a finished step, a row waiting on it (so ready), an
    /// open step and a row waiting on that (so blocked), and a row that never named
    /// anything. The open step is ready too — it waits on nothing — and the
    /// finished one is done, which is its own answer.</summary>
    private static async Task<(TasksPaneHost Host, EntryRow Finished, EntryRow Unblocked, EntryRow Open, EntryRow Blocked, EntryRow Free)> FiveAsync()
    {
        var host = await TasksPaneHost.CreateAsync();

        var finished = await host.WriteEntryAsync("# Provision the box\n`task` `!done` `completed:2026-09-22`\n");
        var unblocked = await host.WriteEntryAsync($"# Deploy it\n`task` `!ready` `after:{finished.TaskId}`\n");
        var open = await host.WriteEntryAsync("# Write the runbook\n`task` `!draft`\n");
        var blocked = await host.WriteEntryAsync($"# Publish it\n`task` `!ready` `after:{open.TaskId}`\n");
        var free = await host.WriteEntryAsync("# Renew the certificate\n`task` `!ready`\n");

        // Writing leaves the last entry open, and an open entry is pinned into view
        // whatever the filters say. Nothing here is about that row's stickiness.
        await host.State.SelectAsync(null);

        return (host, finished, unblocked, open, blocked, free);
    }

    [Fact]
    public async Task The_scope_starts_off_and_is_a_toggle_rather_than_one_of_a_set()
    {
        var (host, _, _, _, _, _) = await FiveAsync();
        using var _host = host;

        var pane = host.Render();
        var chip = pane.Find(Chip);

        Assert.Equal("false", chip.GetAttribute("aria-pressed"));
        Assert.False(host.State.NotWaitingOnly);

        // A state of its own, so aria-pressed — not the aria-checked the status
        // chips carry, which pick one of a set.
        Assert.Null(chip.GetAttribute("aria-checked"));
        Assert.Null(chip.GetAttribute("role"));
    }

    [Fact]
    public async Task The_scope_keeps_the_rows_that_are_open_and_waiting_on_nothing()
    {
        var (host, finished, unblocked, open, blocked, free) = await FiveAsync();
        using var _host = host;

        var pane = host.Render();
        await pane.Find(Chip).ClickAsync(new());

        Assert.Equal("true", pane.Find(Chip).GetAttribute("aria-pressed"));
        Assert.True(host.State.NotWaitingOnly);

        // Waiting on a finished step is not waiting: the wait is over.
        Assert.Single(pane.FindAll($"[data-testid='{RowTestId(unblocked)}']"));
        Assert.Single(pane.FindAll($"[data-testid='{RowTestId(open)}']"));
        Assert.Single(pane.FindAll($"[data-testid='{RowTestId(free)}']"));

        // Waiting on an open step is exactly what the scope takes out.
        Assert.Empty(pane.FindAll($"[data-testid='{RowTestId(blocked)}']"));

        // And a finished row is out too: it is not waiting, but it is not something
        // to start either, and the chip asks what can be picked up now.
        Assert.Empty(pane.FindAll($"[data-testid='{RowTestId(finished)}']"));

        Assert.Equal([unblocked, open, free], host.State.FilteredRows);
    }

    /// <summary>The domain's one hard rule about dependencies: an id that names
    /// nothing still blocks, because a chain reporting itself ready when the step
    /// it waits on is merely missing is the failure that looks like success.</summary>
    [Fact]
    public async Task A_dependency_naming_nothing_still_counts_as_waiting()
    {
        using var host = await TasksPaneHost.CreateAsync();

        var orphan = await host.WriteEntryAsync("# Ship the app\n`task` `!ready` `after:no-such-entry`\n");
        var free = await host.WriteEntryAsync("# Renew the certificate\n`task` `!ready`\n");
        await host.State.SelectAsync(null);

        host.State.SetNotWaitingFilter(true);

        Assert.Equal([free], host.State.FilteredRows);
        Assert.DoesNotContain(orphan, host.State.FilteredRows);
    }

    [Fact]
    public async Task The_chip_counts_what_pressing_it_will_show()
    {
        var (host, _, _, _, _, _) = await FiveAsync();
        using var _host = host;

        var pane = host.Render();

        Assert.Equal("3", pane.Find($"{Chip} .chip__count").TextContent);

        await pane.Find(Chip).ClickAsync(new());

        Assert.Equal(3, host.State.FilteredRows.Count);
        Assert.Equal("3", pane.Find($"{Chip} .chip__count").TextContent);
    }

    [Fact]
    public async Task Pressing_it_again_shows_everything_again()
    {
        var (host, _, _, _, _, _) = await FiveAsync();
        using var _host = host;

        var pane = host.Render();
        await pane.Find(Chip).ClickAsync(new());
        await pane.Find(Chip).ClickAsync(new());

        Assert.False(host.State.NotWaitingOnly);
        Assert.Equal(5, host.State.FilteredRows.Count);
    }

    /// <summary>Finishing the step a row waited on moves the row into the scope
    /// with nothing pressed again — readiness is derived on every read.</summary>
    [Fact]
    public async Task Finishing_the_step_a_row_waits_on_brings_the_row_into_the_scope()
    {
        var (host, _, _, open, blocked, _) = await FiveAsync();
        using var _host = host;

        host.State.SetNotWaitingFilter(true);
        Assert.DoesNotContain(blocked, host.State.FilteredRows);

        await host.State.ToggleCompletedAsync(open, new DateOnly(2026, 9, 22));

        Assert.Contains(blocked, host.State.FilteredRows);
        Assert.DoesNotContain(open, host.State.FilteredRows);
    }

    [Fact]
    public async Task The_scope_narrows_what_the_status_filter_left_in_view()
    {
        var (host, _, unblocked, open, _, free) = await FiveAsync();
        using var _host = host;

        var draft = await host.WriteEntryAsync("# Sketch the design\n`task` `!draft`\n");
        await host.State.SelectAsync(null);

        host.State.SetNotWaitingFilter(true);
        Assert.Equal([unblocked, open, free, draft], host.State.FilteredRows);

        host.State.SetStatusFilter("draft");
        Assert.Equal([open, draft], host.State.FilteredRows);

        host.State.SetStatusFilter(null);
        Assert.Equal([unblocked, open, free, draft], host.State.FilteredRows);
    }

    /// <summary>Following a "waiting for" name off a blocked row to a step that is
    /// itself blocked has to be able to show it, so the scope widens the way every
    /// other one does — and only when it is the one hiding the row.</summary>
    [Fact]
    public async Task Revealing_a_waiting_row_widens_the_scope()
    {
        var (host, _, unblocked, _, blocked, _) = await FiveAsync();
        using var _host = host;

        host.State.SetNotWaitingFilter(true);

        Assert.False(host.State.Reveal(unblocked));
        Assert.True(host.State.NotWaitingOnly);

        Assert.True(host.State.Reveal(blocked));
        Assert.False(host.State.NotWaitingOnly);
        Assert.Contains(blocked, host.State.FilteredRows);
    }
}
