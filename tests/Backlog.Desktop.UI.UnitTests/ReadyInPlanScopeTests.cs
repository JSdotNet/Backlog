using Backlog.Modules.Tasks.DomainModels;
using Bunit;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The "Ready in a plan" scope in the filter bar.
/// <para>
/// It keeps the entries a person marked <c>!ready</c> that are filed under a
/// <c>+plan</c> — the work a plan has lined up to be picked up. Status is the
/// recorded fact here, not readiness derived from <c>after:</c>: a draft waiting
/// on nothing is not something the person said was ready, and an entry outside
/// every plan is not plan work.
/// </para>
/// <para>
/// A scope rather than one of a set, like My Day and No repo beside it: it
/// narrows whatever the repository, the status and the tags have already left in
/// view instead of replacing any of them.
/// </para>
/// </summary>
[Collection(WorkspaceSettingsCollection.Name)]
public sealed class ReadyInPlanScopeTests
{
    private const string Chip = "[data-testid='readyinplan-filter-option']";

    private static string RowTestId(EntryRow row) => $"entry-list-{row.TaskId}";

    /// <summary>Five entries: a ready one in a plan, a ready one in no plan, a
    /// draft in a plan, an in-progress one in a plan, and a ready one in a plan
    /// that is ticked off. Only the first is ready plan work.</summary>
    private static async Task<(TasksPaneHost Host, EntryRow Planned, EntryRow Unplanned, EntryRow Draft, EntryRow Started, EntryRow Ticked)> FiveAsync()
    {
        var host = await TasksPaneHost.CreateAsync();

        var planned = await host.WriteEntryAsync("# Deploy it\n`task` `!ready` `+release`\n");
        var unplanned = await host.WriteEntryAsync("# Renew the certificate\n`task` `!ready`\n");
        var draft = await host.WriteEntryAsync("# Write the runbook\n`task` `!draft` `+release`\n");
        var started = await host.WriteEntryAsync("# Tag the build\n`task` `!in-progress` `+release`\n");
        var ticked = await host.WriteEntryAsync("# Provision the box\n`task` `!ready` `+release` `completed:2026-09-22`\n");

        // Writing leaves the last entry open, and an open entry is pinned into view
        // whatever the filters say. Nothing here is about that row's stickiness.
        await host.State.SelectAsync(null);

        return (host, planned, unplanned, draft, started, ticked);
    }

    [Fact]
    public async Task The_scope_starts_off_and_is_a_toggle_rather_than_one_of_a_set()
    {
        var (host, _, _, _, _, _) = await FiveAsync();
        using var _host = host;

        var pane = host.Render();
        var chip = pane.Find(Chip);

        Assert.Equal("false", chip.GetAttribute("aria-pressed"));
        Assert.False(host.State.ReadyInPlanOnly);

        // A state of its own, so aria-pressed — not the aria-checked the status
        // chips carry, which pick one of a set.
        Assert.Null(chip.GetAttribute("aria-checked"));
        Assert.Null(chip.GetAttribute("role"));
    }

    [Fact]
    public async Task The_scope_keeps_only_ready_entries_filed_under_a_plan()
    {
        var (host, planned, unplanned, draft, started, ticked) = await FiveAsync();
        using var _host = host;

        var pane = host.Render();
        await pane.Find(Chip).ClickAsync(new());

        Assert.Equal("true", pane.Find(Chip).GetAttribute("aria-pressed"));
        Assert.True(host.State.ReadyInPlanOnly);

        Assert.Single(pane.FindAll($"[data-testid='{RowTestId(planned)}']"));

        // Ready, but plan work it is not.
        Assert.Empty(pane.FindAll($"[data-testid='{RowTestId(unplanned)}']"));

        // In the plan, but not marked ready.
        Assert.Empty(pane.FindAll($"[data-testid='{RowTestId(draft)}']"));
        Assert.Empty(pane.FindAll($"[data-testid='{RowTestId(started)}']"));

        // Ticked off is finished, whatever its status still says.
        Assert.Empty(pane.FindAll($"[data-testid='{RowTestId(ticked)}']"));

        Assert.Equal([planned], host.State.FilteredRows);
    }

    /// <summary>The status is what the person recorded, so a ready entry whose
    /// step is still open is ready plan work all the same — its "Waiting for"
    /// line says what it is waiting on.</summary>
    [Fact]
    public async Task A_ready_planned_entry_waiting_on_a_step_is_still_in_scope()
    {
        using var host = await TasksPaneHost.CreateAsync();

        var step = await host.WriteEntryAsync("# Write the runbook\n`task` `!draft` `+release`\n");
        var waiting = await host.WriteEntryAsync($"# Publish it\n`task` `!ready` `+release` `after:{step.TaskId}`\n");
        await host.State.SelectAsync(null);

        host.State.SetReadyInPlanFilter(true);

        Assert.Equal([waiting], host.State.FilteredRows);
    }

    [Fact]
    public async Task The_chip_counts_what_pressing_it_will_show()
    {
        var (host, _, _, _, _, _) = await FiveAsync();
        using var _host = host;

        var pane = host.Render();

        Assert.Equal("1", pane.Find($"{Chip} .chip__count").TextContent);

        await pane.Find(Chip).ClickAsync(new());

        Assert.Single(host.State.FilteredRows);
        Assert.Equal("1", pane.Find($"{Chip} .chip__count").TextContent);
    }

    [Fact]
    public async Task Pressing_it_again_shows_everything_again()
    {
        var (host, _, _, _, _, _) = await FiveAsync();
        using var _host = host;

        var pane = host.Render();
        await pane.Find(Chip).ClickAsync(new());
        await pane.Find(Chip).ClickAsync(new());

        Assert.False(host.State.ReadyInPlanOnly);
        Assert.Equal(5, host.State.FilteredRows.Count);
    }

    /// <summary>Marking a draft ready moves it into the scope with nothing pressed
    /// again — the status is read on every pass.</summary>
    [Fact]
    public async Task Marking_a_planned_draft_ready_brings_it_into_the_scope()
    {
        var (host, planned, _, draft, _, _) = await FiveAsync();
        using var _host = host;

        host.State.SetReadyInPlanFilter(true);
        Assert.DoesNotContain(draft, host.State.FilteredRows);

        await host.State.ChangeStatusAsync(draft, EntryStatus.Ready);
        await host.State.SelectAsync(null);

        Assert.Equal([planned, draft], host.State.FilteredRows);
    }

    [Fact]
    public async Task The_scope_narrows_what_the_tag_filter_left_in_view()
    {
        var (host, planned, _, _, _, _) = await FiveAsync();
        using var _host = host;

        var docs = await host.WriteEntryAsync("# Document the release\n`task` `!ready` `+docs`\n");
        await host.State.SelectAsync(null);

        host.State.SetReadyInPlanFilter(true);
        Assert.Equal([planned, docs], host.State.FilteredRows);

        host.State.ToggleTagFilter("+docs");
        Assert.Equal([docs], host.State.FilteredRows);

        host.State.ToggleTagFilter("+docs");
        Assert.Equal([planned, docs], host.State.FilteredRows);
    }

    /// <summary>Following a "waiting for" name to a step that is not ready plan
    /// work has to be able to show it, so the scope widens the way every other
    /// one does — and only when it is the one hiding the row.</summary>
    [Fact]
    public async Task Revealing_a_row_outside_the_scope_widens_it()
    {
        var (host, planned, _, draft, _, _) = await FiveAsync();
        using var _host = host;

        host.State.SetReadyInPlanFilter(true);

        Assert.False(host.State.Reveal(planned));
        Assert.True(host.State.ReadyInPlanOnly);

        Assert.True(host.State.Reveal(draft));
        Assert.False(host.State.ReadyInPlanOnly);
        Assert.Contains(draft, host.State.FilteredRows);
    }
}
