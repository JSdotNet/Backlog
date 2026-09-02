using System.Globalization;
using Backlog.Modules.Tasks.DomainModels;
using Bunit;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The My Day scope in the filter bar.
/// <para>
/// <c>.domain/tasks/features.md#feature-my-day</c> governs, and everything here
/// follows from its one rule: an entry is in My Day exactly while its stamp is the
/// reader's current local date. Nothing is stored as a flag and nothing expires it,
/// so an entry stamped yesterday is not "in yesterday's My Day" — it is simply not
/// in My Day, which is how the list clears itself overnight with no sweep.
/// </para>
/// <para>
/// It is a scope rather than one of a set: orthogonal to tag and status, so it
/// narrows what those have already left in view instead of replacing either. That is
/// why it is a pressed toggle beside two radiogroups.
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

        // A state of its own, so aria-pressed — not the aria-checked the tag and
        // status chips carry, which pick one of a set.
        Assert.Null(chip.GetAttribute("aria-checked"));
        Assert.Null(chip.GetAttribute("role"));
    }

    /// <summary>
    /// The bar's two scopes sit together and are shaped alike, and no group of areas
    /// sits between them and the tags any more.
    /// <para>
    /// Areas are still typed, still shown on rows and still the reader's own pile —
    /// what went is the chip group, on the grounds that the repository scope in the
    /// header was already answering the question people were asking it. "No repo" is
    /// the one part of that question the header could not answer, so it arrived here
    /// as the areas left.
    /// </para>
    /// </summary>
    [Fact]
    public async Task The_scope_group_holds_both_scopes_and_the_bar_holds_no_areas()
    {
        var (host, _, _, _) = await ThreeAsync();
        using var _host = host;

        var pane = host.Render();

        var scopes = pane.FindAll(".filter-group--scope .chip");

        Assert.Equal(2, scopes.Count);
        Assert.Contains("My Day", scopes[0].TextContent, StringComparison.Ordinal);
        Assert.Contains("No repo", scopes[1].TextContent, StringComparison.Ordinal);

        // Same shape as My Day, for the same reason: a state of its own rather than
        // one of a set.
        Assert.Equal("false", scopes[1].GetAttribute("aria-pressed"));
        Assert.Null(scopes[1].GetAttribute("role"));

        Assert.Empty(pane.FindAll(".filter-group--areas"));
        Assert.Empty(pane.FindAll("[data-testid='area-filter-option']"));
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

        host.State.SetTagFilter("platform");
        host.State.SetStatusFilter("ready");

        var pane = host.Render();
        await pane.Find(Chip).ClickAsync(new());

        Assert.Equal([wanted], host.State.FilteredRows);

        // Both of the other two are still what they were: My Day narrowed the view,
        // it did not take over the bar.
        Assert.Equal("platform", host.State.SelectedTag);
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

        Assert.Contains("Nothing matches these filters.", empty, StringComparison.Ordinal);
        Assert.DoesNotContain("Write your first entry", empty, StringComparison.Ordinal);
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
}
