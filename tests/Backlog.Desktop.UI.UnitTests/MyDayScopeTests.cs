using System.Globalization;
using Backlog.Modules.Tasks.DomainModels;
using Bunit;
using Microsoft.AspNetCore.Components.Web;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The My Day scope in the filter bar.
/// <para>
/// <c>.devbook/domain/tasks/features.md#feature-my-day</c> governs, and everything here
/// follows from its one rule: an entry is in My Day exactly while its stamp is the
/// reader's current local date. Nothing is stored as a flag and nothing expires it,
/// so an entry stamped yesterday is not "in yesterday's My Day" — it is simply not
/// in My Day, which is how the list clears itself overnight with no sweep.
/// </para>
/// <para>
/// It is a scope rather than one of a set: orthogonal to tag and status, so it
/// narrows what those have already left in view instead of replacing either. That is
/// why it is a pressed toggle beside one radiogroup — the statuses', the only group on
/// the bar that is one of a set.
/// </para>
/// </summary>
[Collection(WorkspaceSettingsCollection.Name)]
public sealed class MyDayScopeTests
{
    private const string Chip = "[data-testid='myday-filter-option']";

    private static string Token(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.Now);

    private static DateOnly Yesterday => Today.AddDays(-1);

    private static string RowTestId(EntryRow row) => $"entry-list-{(row.Id ?? row.Key)}";

    /// <summary>Three entries: one picked for today, one picked yesterday, one never
    /// picked at all. The middle one is the whole feature — it was in My Day once and
    /// is not now, without anything having run.</summary>
    private static async Task<(TasksPaneHost Host, EntryRow Todays, EntryRow Yesterdays, EntryRow Never)> ThreeAsync()
    {
        var host = await TasksPaneHost.CreateAsync();

        var todays = await host.WriteEntryAsync($"# Provision the box\n`task` `!ready` `@backlog` `myday:{Token(Today)}`\n");
        var yesterdays = await host.WriteEntryAsync($"# Deploy it\n`task` `!ready` `@backlog` `myday:{Token(Yesterday)}`\n");
        var never = await host.WriteEntryAsync("# Write the runbook\n`task` `!ready` `@backlog`\n");

        // Writing leaves the last entry open, and an open entry is pinned into view
        // whatever the filters say. Nothing here is about that row's stickiness.
        await host.State.SelectAsync(null);

        return (host, todays, yesterdays, never);
    }

    [Fact]
    public async Task The_scope_starts_off_and_is_a_toggle_rather_than_one_of_a_set()
    {
        var (host, _, _, _) = await ThreeAsync();
        using var _host = host;

        var pane = host.Render();
        var chip = pane.Find(Chip);

        Assert.Equal("false", chip.GetAttribute("aria-pressed"));

        // A state of its own, so aria-pressed — not the aria-checked the status
        // chips carry, which pick one of a set. The tag chips beside them are
        // aria-pressed toggles like this one.
        Assert.Null(chip.GetAttribute("aria-checked"));
        Assert.Null(chip.GetAttribute("role"));
    }

    /// <summary>
    /// The bar's scopes sit together and are shaped alike, and no group of areas
    /// sits between them and the tags any more.
    /// <para>
    /// Areas are still typed, still shown on rows and still the reader's own pile —
    /// what went is the chip group, on the grounds that the repository scope in the
    /// header was already answering the question people were asking it. "Ready in a
    /// plan" is the second: the work a plan has lined up. The "No repo" chip that sat
    /// between them is gone: the header's Brain chip answers that part of the question
    /// now (local ADR 0027), so no chip on the bar reads "No repo".
    /// </para>
    /// </summary>
    [Fact]
    public async Task The_scope_group_holds_every_scope_and_the_bar_holds_no_areas()
    {
        var (host, _, _, _) = await ThreeAsync();
        using var _host = host;

        var pane = host.Render();

        var scopes = pane.FindAll(".filter-group--scope .chip");

        Assert.Equal(2, scopes.Count);
        Assert.Contains("My Day", scopes[0].TextContent, StringComparison.Ordinal);
        Assert.Contains("Ready in a plan", scopes[1].TextContent, StringComparison.Ordinal);
        Assert.DoesNotContain(pane.FindAll(".filter-bar .chip"), chip => chip.TextContent.Contains("No repo", StringComparison.Ordinal));
        Assert.Empty(pane.FindAll("[data-testid='norepo-filter-option']"));

        // Same shape as My Day, for the same reason: a state of its own rather than
        // one of a set.
        foreach (var scope in scopes.Skip(1))
        {
            Assert.Equal("false", scope.GetAttribute("aria-pressed"));
            Assert.Null(scope.GetAttribute("role"));
        }

        Assert.Empty(pane.FindAll(".filter-group--areas"));
        Assert.Empty(pane.FindAll("[data-testid='area-filter-option']"));
    }

    /// <summary>
    /// Each scope wears a glyph in place of its word, and the word is still its
    /// name: the glyph is hidden from assistive technology and the word sits in an
    /// sr-only span, so what is on screen is the glyph and the count alone while a
    /// screen reader still hears "My Day 1".
    /// </summary>
    [Fact]
    public async Task Each_scope_shows_a_glyph_and_keeps_its_word_as_the_name()
    {
        var (host, _, _, _) = await ThreeAsync();
        using var _host = host;

        var pane = host.Render();

        var scopes = pane.FindAll(".filter-group--scope .chip");
        string[] glyphs = ["sun-icon", "play-icon"];
        string[] names = ["My Day", "Ready in a plan"];

        Assert.Equal(2, scopes.Count);

        for (var i = 0; i < scopes.Count; i++)
        {
            var glyph = scopes[i].QuerySelector($"svg.{glyphs[i]}.chip__icon");
            Assert.NotNull(glyph);
            Assert.Equal("true", glyph.GetAttribute("aria-hidden"));

            Assert.Equal(names[i], scopes[i].QuerySelector(".sr-only")?.TextContent);

            // Nothing visible beside the glyph but the count.
            var visible = scopes[i].ChildNodes
                .Where(node => node is not AngleSharp.Dom.IElement element
                    || !element.ClassList.Contains("sr-only") && !element.ClassList.Contains("chip__count"))
                .Select(node => node.TextContent.Trim());
            Assert.All(visible, text => Assert.Empty(text));
        }
    }

    [Fact]
    public async Task The_scope_shows_only_the_entries_picked_for_today()
    {
        var (host, todays, yesterdays, never) = await ThreeAsync();
        using var _host = host;

        var pane = host.Render();
        await pane.Find(Chip).ClickAsync(new());

        Assert.Equal("true", pane.Find(Chip).GetAttribute("aria-pressed"));
        Assert.Equal(Today, host.State.MyDayOn);

        Assert.Single(pane.FindAll($"[data-testid='{RowTestId(todays)}']"));

        // Yesterday's pick is out for the same reason the never-picked one is: it is
        // not in My Day today, and there is no third state in between.
        Assert.Empty(pane.FindAll($"[data-testid='{RowTestId(yesterdays)}']"));
        Assert.Empty(pane.FindAll($"[data-testid='{RowTestId(never)}']"));

        Assert.Equal([todays], host.State.FilteredRows);
    }

    [Fact]
    public async Task Pressing_it_again_shows_everything_again()
    {
        var (host, _, _, _) = await ThreeAsync();
        using var _host = host;

        var pane = host.Render();
        await pane.Find(Chip).ClickAsync(new());
        await pane.Find(Chip).ClickAsync(new());

        Assert.Null(host.State.MyDayOn);
        Assert.Equal(3, host.State.FilteredRows.Count);
    }

    /// <summary>The count is how many entries are in today's My Day, off the same
    /// pool the area chips count — so it says how much is over there rather than how
    /// much is left after the current selection.</summary>
    [Fact]
    public async Task The_count_says_how_many_are_in_todays_my_day()
    {
        var (host, _, _, _) = await ThreeAsync();
        using var _host = host;

        var pane = host.Render();

        Assert.Equal("1", pane.Find($"{Chip} .chip__count").TextContent);

        await host.WriteEntryAsync($"# Book the room\n`task` `!ready` `@backlog` `myday:{Token(Today)}`\n");
        await host.State.SelectAsync(null);

        pane.Render();

        Assert.Equal("2", pane.Find($"{Chip} .chip__count").TextContent);
    }

    /// <summary>The count is what is still to do today, the way a tag chip's face is
    /// its open count: an entry ticked off stays in My Day, and stays listed while no
    /// status is pressed, but it is no longer counted.</summary>
    [Fact]
    public async Task The_count_leaves_out_what_is_ticked_off()
    {
        using var host = await TasksPaneHost.CreateAsync();

        await host.WriteEntryAsync($"# Provision the box\n`task` `!ready` `@backlog` `myday:{Token(Today)}`\n");
        await host.WriteEntryAsync($"# Deploy it\n`task` `!done` `completed:{Token(Today)}` `@backlog` `myday:{Token(Today)}`\n");
        await host.State.SelectAsync(null);

        var pane = host.Render();

        Assert.Equal("1", pane.Find($"{Chip} .chip__count").TextContent);
    }

    /// <summary>Orthogonal, which is the reason it is a toggle: turning it on narrows
    /// whatever tag and status have already left in view instead of replacing
    /// either.</summary>
    [Fact]
    public async Task The_scope_narrows_the_tag_and_status_filters_rather_than_replacing_them()
    {
        using var host = await TasksPaneHost.CreateAsync();

        var wanted = await host.WriteEntryAsync($"# Provision the box\n`task` `!ready` `#platform` `myday:{Token(Today)}`\n");

        // In My Day, but tagged something else.
        await host.WriteEntryAsync($"# Draft the invite\n`task` `!ready` `#marketing` `myday:{Token(Today)}`\n");

        // In My Day and under the tag, but not at the status being looked at.
        await host.WriteEntryAsync($"# Sketch the schema\n`task` `!draft` `#platform` `myday:{Token(Today)}`\n");

        // Under the tag and at the status, but not picked for today.
        await host.WriteEntryAsync("# Write the runbook\n`task` `!ready` `#platform`\n");

        await host.State.SelectAsync(null);

        host.State.ToggleTagFilter("platform");
        host.State.SetStatusFilter("ready");

        var pane = host.Render();
        await pane.Find(Chip).ClickAsync(new());

        Assert.Equal([wanted], host.State.FilteredRows);

        // Both of the other two are still what they were: My Day narrowed the view,
        // it did not take over the bar.
        Assert.Equal(["platform"], host.State.SelectedTags);
        Assert.Equal("ready", host.State.SelectedStatusFilterWire);
    }

    /// <summary>A scope that matches nothing says so. Most entries are not in today's
    /// My Day, so the first-use copy would tell somebody with a full backlog to write
    /// their first entry.</summary>
    [Fact]
    public async Task A_scope_that_matches_nothing_says_the_filters_did_it()
    {
        using var host = await TasksPaneHost.CreateAsync();
        await host.WriteEntryAsync("# Write the runbook\n`task` `!ready` `@backlog`\n");
        await host.State.SelectAsync(null);

        var pane = host.Render();
        await pane.Find(Chip).ClickAsync(new());

        Assert.Empty(host.State.FilteredRows);

        var empty = pane.Find("[data-testid='empty-state']").TextContent;

        Assert.Contains("Nothing matches these filters", empty, StringComparison.Ordinal);
        Assert.DoesNotContain("Write your first entry", empty, StringComparison.Ordinal);
    }

    /// <summary>An entry written while the scope is on is written into it. Otherwise
    /// it would leave the list the moment it saved, since the scope keeps exactly the
    /// entries stamped for its date — the reason a scoped repository is seeded too.</summary>
    [Fact]
    public async Task A_new_entry_written_under_the_scope_lands_in_my_day()
    {
        using var host = await TasksPaneHost.CreateAsync();

        host.State.SetMyDayFilter(Today);
        host.State.NewRow();
        var row = host.State.SelectedRow!;

        Assert.Contains($"`myday:{Token(Today)}`", row.RawText, StringComparison.Ordinal);

        await host.State.RenameEntryAsync(row, "Book the venue");
        await host.State.SelectAsync(null);

        Assert.NotNull(row.Id);
        Assert.Equal(Today, row.PreviewInMyDayOn);
        Assert.Contains(row, host.State.FilteredRows);
    }

    [Fact]
    public async Task A_new_entry_written_with_the_scope_off_is_not_put_in_my_day()
    {
        using var host = await TasksPaneHost.CreateAsync();

        host.State.NewRow();

        Assert.DoesNotContain("myday:", host.State.SelectedRow?.RawText ?? string.Empty, StringComparison.Ordinal);
    }

    /// <summary>Nothing sweeps and nothing expires: the same entry, read on two
    /// dates, is in My Day on one of them and not on the other because the scope is
    /// pinned to a date rather than to a flag on the row.</summary>
    [Fact]
    public async Task Yesterdays_pick_is_still_findable_as_yesterdays()
    {
        var (host, _, yesterdays, _) = await ThreeAsync();
        using var _host = host;

        Assert.Equal(Yesterday, yesterdays.PreviewInMyDayOn);

        host.State.SetMyDayFilter(Yesterday);

        Assert.Equal([yesterdays], host.State.FilteredRows);
    }

    // --- Clear My Day overnight off (local ADR 0032) ------------------------------
    //
    // The switch changes how an earlier stamp reads and nothing else: a task carried
    // over is in My Day while unfinished, and on the day it is finished, and every
    // surface that asks "is this in My Day" asks the state's one rule.

    private static string Card(EntryRow row) => $"[data-task-id='{row.TaskId}']";

    private static Task OpenMenuAsync(IRenderedComponent<TasksPane> pane, EntryRow row) =>
        pane.Find($"[data-testid='{RowTestId(row)}']")
            .ContextMenuAsync(new MouseEventArgs { ClientX = 40, ClientY = 60 });

    private static string MenuLabel(IRenderedComponent<TasksPane> pane, string item) =>
        pane.Find($"[data-testid='entry-menu-item-{item}'] .menu-list__label").TextContent;

    [Fact]
    public async Task With_the_switch_off_a_carried_task_counts_and_the_scope_shows_it()
    {
        var (host, todays, yesterdays, never) = await ThreeAsync();
        using var _host = host;
        Assert.Null(host.MyDay.SetClearsOvernight(false));

        var pane = host.Render();

        Assert.Equal("2", pane.Find($"{Chip} .chip__count").TextContent);

        await pane.Find(Chip).ClickAsync(new());

        Assert.Equal([todays, yesterdays], host.State.FilteredRows);
        Assert.Single(pane.FindAll($"[data-testid='{RowTestId(yesterdays)}']"));
        Assert.Empty(pane.FindAll($"[data-testid='{RowTestId(never)}']"));
    }

    /// <summary>A carried task finished today stays, ticked, for the rest of the day
    /// and is no longer counted; one finished on an earlier day has left.</summary>
    [Fact]
    public async Task With_the_switch_off_a_carried_task_stays_on_the_day_it_is_finished_and_only_then()
    {
        using var host = await TasksPaneHost.CreateAsync();
        Assert.Null(host.MyDay.SetClearsOvernight(false));

        var finishedToday = await host.WriteEntryAsync($"# Deploy it\n`task` `!done` `completed:{Token(Today)}` `@backlog` `myday:{Token(Yesterday)}`\n");
        await host.WriteEntryAsync($"# Book the room\n`task` `!done` `completed:{Token(Yesterday)}` `@backlog` `myday:{Token(Yesterday)}`\n");
        await host.State.SelectAsync(null);

        var pane = host.Render();

        Assert.Equal("0", pane.Find($"{Chip} .chip__count").TextContent);

        await pane.Find(Chip).ClickAsync(new());

        Assert.Equal([finishedToday], host.State.FilteredRows);
    }

    /// <summary>A stamp for a later day is in no earlier My Day, whichever way the
    /// switch is set.</summary>
    [Fact]
    public async Task With_the_switch_off_a_task_stamped_for_tomorrow_is_still_not_in_today()
    {
        using var host = await TasksPaneHost.CreateAsync();
        Assert.Null(host.MyDay.SetClearsOvernight(false));

        await host.WriteEntryAsync($"# Plan the week\n`task` `!ready` `@backlog` `myday:{Token(Today.AddDays(1))}`\n");
        await host.State.SelectAsync(null);

        host.State.SetMyDayFilter(Today);

        Assert.Empty(host.State.FilteredRows);
    }

    /// <summary>Reveal widens only the scopes that hide the row. A carried task is in
    /// My Day with the switch off, so the scope stays pressed while the status that
    /// hid it is let go.</summary>
    [Fact]
    public async Task With_the_switch_off_reveal_keeps_my_day_on_for_a_carried_task()
    {
        var (host, _, yesterdays, _) = await ThreeAsync();
        using var _host = host;
        Assert.Null(host.MyDay.SetClearsOvernight(false));

        host.State.SetMyDayFilter(Today);
        host.State.SetStatusFilter("draft");
        Assert.DoesNotContain(yesterdays, host.State.FilteredRows);

        Assert.True(host.State.Reveal(yesterdays));

        Assert.Equal(Today, host.State.MyDayOn);
        Assert.Contains(yesterdays, host.State.FilteredRows);
    }

    /// <summary>The same reveal with the switch on lets My Day go: yesterday's stamp
    /// is not in today's list, so the scope is what hid the row.</summary>
    [Fact]
    public async Task With_the_switch_on_reveal_lets_my_day_go_for_yesterdays_pick()
    {
        var (host, _, yesterdays, _) = await ThreeAsync();
        using var _host = host;

        host.State.SetMyDayFilter(Today);

        Assert.True(host.State.Reveal(yesterdays));

        Assert.Null(host.State.MyDayOn);
        Assert.Contains(yesterdays, host.State.FilteredRows);
    }

    /// <summary>The row wears the sun and its menu names the way out, as for a task
    /// picked today.</summary>
    [Fact]
    public async Task With_the_switch_off_a_carried_row_shows_the_sun_and_its_menu_says_remove()
    {
        var (host, _, yesterdays, never) = await ThreeAsync();
        using var _host = host;
        Assert.Null(host.MyDay.SetClearsOvernight(false));

        var pane = host.Render();

        Assert.NotNull(pane.Find(Card(yesterdays)).QuerySelector(".task-item__detail--myday"));
        Assert.Null(pane.Find(Card(never)).QuerySelector(".task-item__detail--myday"));

        await OpenMenuAsync(pane, yesterdays);
        Assert.Equal("Remove from My Day", MenuLabel(pane, "myday"));
    }

    /// <summary>The detail pane says the task is in My Day, and offers no agenda
    /// time: the slot it had was the day it was stamped for, so a carried task shows
    /// none on its row either (ADR 0032, §5; V7).</summary>
    [Fact]
    public async Task With_the_switch_off_a_carried_task_reads_added_and_has_no_agenda_slot()
    {
        using var host = await TasksPaneHost.CreateAsync();
        Assert.Null(host.MyDay.SetClearsOvernight(false));

        var carried = await host.WriteEntryAsync($"# Deploy it\n`task` `!ready` `myday:{Token(Yesterday)}` `at:10:45`\n");

        var pane = host.Render();

        Assert.Contains("Added to My Day", pane.Find("[data-testid='entry-action-myday']").TextContent, StringComparison.Ordinal);
        Assert.Empty(pane.FindAll("[data-testid='entry-action-agenda']"));
        Assert.NotNull(pane.Find(Card(carried)).QuerySelector(".task-item__detail--myday"));
        Assert.Null(pane.Find(Card(carried)).QuerySelector(".task-item__detail--agenda"));
        Assert.DoesNotContain("10:45", pane.Find(Card(carried)).TextContent, StringComparison.Ordinal);
    }

    /// <summary>Taking a carried task out clears its stamp and writes nothing
    /// else.</summary>
    [Fact]
    public async Task With_the_switch_off_removing_a_carried_task_clears_only_its_stamp()
    {
        using var host = await TasksPaneHost.CreateAsync();
        Assert.Null(host.MyDay.SetClearsOvernight(false));

        var carried = await host.WriteEntryAsync($"# Deploy it\n`task` `!ready` `@backlog` `myday:{Token(Yesterday)}`\n\nShip it.\n");
        var saved = carried.RawText;
        Assert.Contains($" `myday:{Token(Yesterday)}`", saved, StringComparison.Ordinal);

        var pane = host.Render();
        await pane.Find("[data-testid='entry-action-myday-set']").ClickAsync(new());

        Assert.Null(carried.PreviewInMyDayOn);
        Assert.Equal(saved.Replace($" `myday:{Token(Yesterday)}`", string.Empty, StringComparison.Ordinal), carried.RawText);
        Assert.Contains("Add to My Day", pane.Find("[data-testid='entry-action-myday']").TextContent, StringComparison.Ordinal);
    }

    /// <summary>The switch moved with the list on screen — here, or in a copy a sync
    /// pull took — and the count, the scope and the rows follow it without anything
    /// being written to a task.</summary>
    [Fact]
    public async Task Flipping_the_switch_with_the_pane_open_redraws_my_day()
    {
        var (host, todays, yesterdays, _) = await ThreeAsync();
        using var _host = host;
        var before = yesterdays.RawText;

        var pane = host.Render();
        await pane.Find(Chip).ClickAsync(new());
        Assert.Equal("1", pane.Find($"{Chip} .chip__count").TextContent);
        Assert.Equal([todays], host.State.FilteredRows);

        Assert.Null(host.MyDay.SetClearsOvernight(false));

        pane.WaitForAssertion(() =>
        {
            Assert.Equal("2", pane.Find($"{Chip} .chip__count").TextContent);
            Assert.Single(pane.FindAll($"[data-testid='{RowTestId(yesterdays)}']"));
        });
        Assert.Equal([todays, yesterdays], host.State.FilteredRows);

        Assert.Null(host.MyDay.SetClearsOvernight(true));

        pane.WaitForAssertion(() =>
        {
            Assert.Equal("1", pane.Find($"{Chip} .chip__count").TextContent);
            Assert.Empty(pane.FindAll($"[data-testid='{RowTestId(yesterdays)}']"));
        });
        Assert.Equal(before, yesterdays.RawText);
    }
}
