using Bunit;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The tag group in the backlog filter bar.
/// <para>
/// Modelled on the area group beside it and different from it in one way that
/// governs everything here: an entry has one area and any number of tags. So a row
/// is counted under every tag it wears, the counts sum past the row count on
/// purpose, and "narrow to this tag" asks whether the row <em>carries</em> the tag
/// rather than whether it <em>is</em> it.
/// </para>
/// <para>
/// Values are bare and lower-cased because that is how <c>EntryTextParser</c> stores
/// a tag; the leading <c>#</c> is on the chip's label only, which is how a tag reads
/// everywhere else on this screen.
/// </para>
/// </summary>
[Collection(WorkspaceSettingsCollection.Name)]
public sealed class TagFilterTests
{
    private const string Chip = "[data-testid='tag-filter-option']";

    private static string RowTestId(EntryRow row) => $"entry-list-{(row.Id ?? row.Key)}";

    /// <summary>Four entries: one tagged <c>#sync</c>, one tagged <c>#desktop</c>,
    /// one wearing both, and one wearing neither. Every case in this file is some
    /// question about that shape.</summary>
    private static async Task<(TasksPaneHost Host, EntryRow Sync, EntryRow Desktop, EntryRow Both, EntryRow None)> FourAsync()
    {
        var host = await TasksPaneHost.CreateAsync();

        var sync = await host.WriteEntryAsync("# Provision the box\n`task` `!ready` `@platform` `#sync`\n");
        var desktop = await host.WriteEntryAsync("# Draft the invite\n`task` `!ready` `@platform` `#desktop`\n");
        var both = await host.WriteEntryAsync("# Deploy it\n`task` `!draft` `@platform` `#sync` `#desktop`\n");
        var none = await host.WriteEntryAsync("# Write the runbook\n`task` `!ready` `@platform`\n");

        // Writing leaves the last entry open, and an open entry is pinned into view
        // whatever the filters say. Nothing here is about that row's stickiness.
        await host.State.SelectAsync(null);

        return (host, sync, desktop, both, none);
    }

    [Fact]
    public async Task The_options_are_the_tags_people_actually_typed()
    {
        var (host, _, _, _, _) = await FourAsync();
        using var _host = host;

        Assert.Equal(
            ["All", "#desktop", "#sync", "Untagged"],
            host.State.TagFilters.Select(option => option.Label));

        // Bare and lower-cased on the wire, hash on the label only.
        Assert.Equal(
            [string.Empty, "desktop", "sync", TasksDesktopState.UntaggedTag],
            host.State.TagFilters.Select(option => option.Value));
    }

    /// <summary>"All" counts rows; a tag counts occurrences. Two entries wear
    /// <c>#sync</c> and two wear <c>#desktop</c> across four rows, so the tag counts
    /// sum to more than the pool — which is right, because each one answers "how
    /// much is over there" rather than "what is my share".</summary>
    [Fact]
    public async Task The_counts_are_per_tag_and_all_still_counts_the_rows()
    {
        var (host, _, _, _, _) = await FourAsync();
        using var _host = host;

        Assert.Equal(4, Option(host, string.Empty).Count);
        Assert.Equal(2, Option(host, "sync").Count);
        Assert.Equal(2, Option(host, "desktop").Count);
        Assert.Equal(1, Option(host, TasksDesktopState.UntaggedTag).Count);
    }

    [Fact]
    public async Task The_chips_pick_one_of_a_set()
    {
        var (host, _, _, _, _) = await FourAsync();
        using var _host = host;

        var pane = host.Render();
        var chips = pane.FindAll(Chip);

        Assert.Equal(4, chips.Count);

        // A radiogroup, the same as the areas beside it — so aria-checked rather
        // than the aria-pressed the My Day scope carries.
        Assert.All(chips, chip => Assert.Equal("radio", chip.GetAttribute("role")));
        Assert.Equal("true", chips[0].GetAttribute("aria-checked"));
        Assert.Equal("All4", chips[0].TextContent);

        Assert.Single(pane.FindAll("[aria-label='Filter by tag']"));
    }

    [Fact]
    public async Task Selecting_a_tag_narrows_the_list_to_the_entries_wearing_it()
    {
        var (host, sync, desktop, both, none) = await FourAsync();
        using var _host = host;

        var pane = host.Render();
        await pane.FindAll(Chip)[2].ClickAsync(new());

        Assert.Equal("sync", host.State.SelectedTag);
        Assert.Equal([sync, both], host.State.FilteredRows);

        Assert.Single(pane.FindAll($"[data-testid='{RowTestId(sync)}']"));
        Assert.Empty(pane.FindAll($"[data-testid='{RowTestId(desktop)}']"));
        Assert.Empty(pane.FindAll($"[data-testid='{RowTestId(none)}']"));

        Assert.Equal("true", pane.FindAll(Chip)[2].GetAttribute("aria-checked"));
        Assert.Equal("false", pane.FindAll(Chip)[0].GetAttribute("aria-checked"));
    }

    /// <summary>The multi-tag row is the case an area filter never has: it is under
    /// <c>#sync</c> and under <c>#desktop</c>, and picking either one keeps it.</summary>
    [Fact]
    public async Task An_entry_wearing_two_tags_is_under_both_of_them()
    {
        var (host, _, _, both, _) = await FourAsync();
        using var _host = host;

        host.State.SetTagFilter("sync");
        Assert.Contains(both, host.State.FilteredRows);

        host.State.SetTagFilter("desktop");
        Assert.Contains(both, host.State.FilteredRows);
    }

    [Fact]
    public async Task Untagged_is_the_entries_carrying_no_tag_at_all()
    {
        var (host, _, _, _, none) = await FourAsync();
        using var _host = host;

        host.State.SetTagFilter(TasksDesktopState.UntaggedTag);

        Assert.Equal([none], host.State.FilteredRows);
    }

    [Fact]
    public async Task All_puts_everything_back()
    {
        var (host, _, _, _, _) = await FourAsync();
        using var _host = host;

        var pane = host.Render();
        await pane.FindAll(Chip)[2].ClickAsync(new());
        await pane.FindAll(Chip)[0].ClickAsync(new());

        Assert.Equal(string.Empty, host.State.SelectedTag);
        Assert.Equal(4, host.State.FilteredRows.Count);
    }

    /// <summary>Orthogonal to the rest of the bar: a tag narrows what area and
    /// status have already left in view instead of replacing either.</summary>
    [Fact]
    public async Task A_tag_composes_with_the_status_filter_rather_than_replacing_it()
    {
        var (host, sync, _, _, _) = await FourAsync();
        using var _host = host;

        host.State.SetAreaFilter("platform");
        host.State.SetStatusFilter("ready");
        host.State.SetTagFilter("sync");

        // The other #sync entry is a draft, so status takes it; the other ready
        // entries are not tagged #sync, so the tag takes those.
        Assert.Equal([sync], host.State.FilteredRows);

        Assert.Equal("platform", host.State.SelectedArea);
        Assert.Equal("ready", host.State.SelectedStatusFilterWire);
    }

    /// <summary>A tag stops existing when the last entry wearing it drops it, and a
    /// selection pointing at nothing would filter the list to nothing with no chip
    /// on screen saying why. Same fallback the area group makes.</summary>
    [Fact]
    public async Task A_tag_that_stopped_existing_falls_back_to_all()
    {
        var (host, sync, _, both, _) = await FourAsync();
        using var _host = host;

        host.State.SetTagFilter("sync");
        Assert.Equal(2, host.State.FilteredRows.Count);

        await Retag(host, sync, "# Provision the box\n`task` `!ready` `@platform`\n");
        await Retag(host, both, "# Deploy it\n`task` `!draft` `@platform` `#desktop`\n");

        Assert.Equal(string.Empty, host.State.SelectedTag);
        Assert.DoesNotContain(host.State.TagFilters, option => option.Value == "sync");
        Assert.Equal(4, host.State.FilteredRows.Count);
    }

    /// <summary>Nothing on the bar for a backlog nobody tagged. The group is built
    /// out of what people typed, so with nothing typed there is nothing to build —
    /// and a lone "All" chip filtering nothing would be a fourth group charging every
    /// reader for a feature only the taggers use.</summary>
    [Fact]
    public async Task The_group_is_absent_while_nothing_carries_a_tag()
    {
        using var host = await TasksPaneHost.CreateAsync();

        await host.WriteEntryAsync("# Write the runbook\n`task` `!ready` `@platform`\n");
        await host.State.SelectAsync(null);

        Assert.Empty(host.State.TagFilters);

        var pane = host.Render();

        Assert.Empty(pane.FindAll(Chip));
        Assert.Empty(pane.FindAll("[aria-label='Filter by tag']"));

        // The three groups that were always there are untouched.
        Assert.Single(pane.FindAll("[aria-label='Filter by area']"));
        Assert.Single(pane.FindAll("[aria-label='Filter by status']"));
        Assert.Single(pane.FindAll("[aria-label='Scope']"));
    }

    // ---- The tags on the rows -------------------------------------------------
    //
    // The other half of the feature, and the half that carries it on a narrow
    // column: the tag group leaves the bar below 38rem, so a row's own tag is both
    // the way into the filter and — pressed again — the only way back out.

    /// <summary>The row's tags, by the class the library has always drawn them
    /// with — the same hook <c>TaskListTests</c> reads them through.</summary>
    private static IReadOnlyList<AngleSharp.Dom.IElement> RowTags(
        IRenderedComponent<TasksPane> pane,
        EntryRow row) =>
        pane.FindAll($"[data-testid='{RowTestId(row)}'] .task-item__tag .tag-chip__label");

    [Fact]
    public async Task A_rows_tags_are_controls_that_say_what_pressing_them_does()
    {
        var (host, sync, _, both, none) = await FourAsync();
        using var _host = host;

        var pane = host.Render();

        var chips = RowTags(pane, sync);

        Assert.Single(chips);
        Assert.Equal("button", chips[0].LocalName);
        Assert.Equal("#sync", chips[0].TextContent);

        // The word is the tag; the name is the act.
        Assert.Equal("Filter by #sync", chips[0].GetAttribute("aria-label"));

        // Every tag the row wears, in the order it wears them.
        Assert.Equal(["#sync", "#desktop"], RowTags(pane, both).Select(chip => chip.TextContent));

        // And a row with none has none, rather than an empty strip.
        Assert.Empty(RowTags(pane, none));
    }

    [Fact]
    public async Task Pressing_a_tag_on_a_row_filters_by_it()
    {
        var (host, sync, desktop, both, _) = await FourAsync();
        using var _host = host;

        var pane = host.Render();
        await RowTags(pane, sync)[0].ClickAsync(new());

        Assert.Equal("sync", host.State.SelectedTag);
        Assert.Equal([sync, both], host.State.FilteredRows);

        Assert.Empty(pane.FindAll($"[data-testid='{RowTestId(desktop)}']"));
    }

    /// <summary>The toggle, and the reason it exists: the group is off the bar on a
    /// narrow column, so the tag on the row is the only control left that can
    /// unfilter the list.</summary>
    [Fact]
    public async Task Pressing_the_tag_already_being_filtered_by_clears_it()
    {
        var (host, sync, _, _, _) = await FourAsync();
        using var _host = host;

        var pane = host.Render();
        await RowTags(pane, sync)[0].ClickAsync(new());

        Assert.Equal("sync", host.State.SelectedTag);

        await RowTags(pane, sync)[0].ClickAsync(new());

        Assert.Equal(string.Empty, host.State.SelectedTag);
        Assert.Equal(4, host.State.FilteredRows.Count);
    }

    /// <summary>The chips in the bar are a radiogroup and keep radio semantics:
    /// pressing the chosen one of a set is not a request to choose nothing. Only the
    /// row's tag toggles, which is why the toggle lives in the pane rather than in
    /// <c>SetTagFilter</c>.</summary>
    [Fact]
    public async Task The_chip_in_the_bar_does_not_toggle()
    {
        var (host, _, _, _, _) = await FourAsync();
        using var _host = host;

        var pane = host.Render();

        await pane.FindAll(Chip)[2].ClickAsync(new());
        await pane.FindAll(Chip)[2].ClickAsync(new());

        Assert.Equal("sync", host.State.SelectedTag);
    }

    /// <summary>Filtering by a tag is not opening the entry it was on. The row's own
    /// button is what opens it, and the tag's click stops before it gets there.</summary>
    [Fact]
    public async Task Pressing_a_tag_does_not_open_the_entry()
    {
        var (host, sync, _, _, _) = await FourAsync();
        using var _host = host;

        var pane = host.Render();
        await RowTags(pane, sync)[0].ClickAsync(new());

        Assert.Null(host.State.SelectedRow);
    }

    /// <summary>A row with tags draws its metadata line beside the button that opens
    /// it rather than inside it, because a button cannot hold a button — and into a
    /// box that stands where the button stood, so the row keeps its shape. The
    /// pickers are the proof: they are the row's own children and stay on its one
    /// line. <c>TaskListTests</c> pins the arrangement in full.</summary>
    [Fact]
    public async Task A_tagged_rows_metadata_line_leaves_the_button()
    {
        var (host, sync, _, _, none) = await FourAsync();
        using var _host = host;

        var pane = host.Render();

        var tagged = pane.Find($"[data-testid='{RowTestId(sync)}']");

        Assert.Null(tagged.QuerySelector(".task-item__body .task-item__meta"));
        Assert.NotNull(tagged.QuerySelector(".task-item__line .task-item__meta"));

        // The row's pickers did not follow it. They sit in the host's action slot,
        // which is a child of the row and not of the box the line went into — the
        // difference between the metadata moving and the whole right-hand side of
        // the row moving with it.
        var pickers = tagged.QuerySelector(".entry-row__pickers");

        Assert.NotNull(pickers);
        Assert.Null(pickers!.Closest(".task-item__line"));

        // A row with no tags is untouched — no box at all. That it also keeps its
        // metadata line inside the button is TaskListTests' to say, on a row that
        // has a line to keep: this one has nothing on it but a title.
        var untagged = pane.Find($"[data-testid='{RowTestId(none)}']");

        Assert.Null(untagged.QuerySelector(".task-item__line"));
    }

    private static TagFilterOption Option(TasksPaneHost host, string value) =>
        host.State.TagFilters.Single(option => option.Value == value);

    /// <summary>Rewrites an entry the way the raw hatch does, and saves it.</summary>
    private static async Task Retag(TasksPaneHost host, EntryRow row, string text)
    {
        host.State.OnRawTextInput(row, text);
        await host.State.EndEditAsync(row);
        await host.State.SelectAsync(null);
    }
}
