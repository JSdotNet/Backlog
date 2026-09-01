namespace Backlog.UI.Components.UnitTests;

public sealed class TagMultiSelectTests
{
    private static readonly SelectorOption[] Options =
    [
        new("desktop", "desktop"),
        new("spacing", "spacing"),
        new("sync", "sync")
    ];

    [Fact]
    public void The_input_is_wired_as_a_combobox_over_the_option_list()
    {
        using var context = new BunitContext();

        var select = context.Render<TagMultiSelect>(parameters => parameters.Add(s => s.Options, Options));
        var input = select.Find("input");

        Assert.Equal("combobox", input.GetAttribute("role"));
        Assert.Equal("false", input.GetAttribute("aria-expanded"));

        input.Focus();
        input.KeyDown(new KeyboardEventArgs { Key = "ArrowDown" });

        var reopened = select.Find("input");
        Assert.Equal("true", reopened.GetAttribute("aria-expanded"));
        Assert.Equal(select.Find("[role='option']").Id, reopened.GetAttribute("aria-activedescendant"));
        Assert.Equal(reopened.GetAttribute("aria-controls"), select.Find("[role='listbox']").Id);
    }

    [Fact]
    public void Choosing_an_option_turns_it_into_a_chip()
    {
        using var context = new BunitContext();
        IReadOnlyList<string>? reported = null;

        var select = context.Render<TagMultiSelect>(parameters => parameters
            .Add(s => s.Options, Options)
            .Add(s => s.SelectedValuesChanged, (IReadOnlyList<string> values) => reported = values));

        select.Find("input").Focus();
        select.FindAll("[role='option']")[1].Click();

        Assert.Equal(["spacing"], reported);
        Assert.Equal("spacing", select.Find(".tag-select__chip .tag-chip__label").TextContent);
    }

    [Fact]
    public void Backspace_on_an_empty_input_drops_the_last_chip()
    {
        using var context = new BunitContext();
        IReadOnlyList<string>? reported = null;

        var select = context.Render<TagMultiSelect>(parameters => parameters
            .Add(s => s.Options, Options)
            .Add(s => s.SelectedValues, new[] { "desktop", "sync" })
            .Add(s => s.SelectedValuesChanged, (IReadOnlyList<string> values) => reported = values));

        select.Find("input").KeyDown(new KeyboardEventArgs { Key = "Backspace" });

        Assert.Equal(["desktop"], reported);
    }

    [Fact]
    public void Backspace_leaves_the_chips_alone_while_there_is_still_a_query_to_delete()
    {
        using var context = new BunitContext();
        var changes = 0;

        var select = context.Render<TagMultiSelect>(parameters => parameters
            .Add(s => s.Options, Options)
            .Add(s => s.SelectedValues, new[] { "desktop" })
            .Add(s => s.SelectedValuesChanged, (IReadOnlyList<string> _) => changes++));

        select.Find("input").Input("sy");
        select.Find("input").KeyDown(new KeyboardEventArgs { Key = "Backspace" });

        Assert.Equal(0, changes);
    }

    [Fact]
    public void Allow_create_commits_whatever_was_typed()
    {
        using var context = new BunitContext();
        IReadOnlyList<string>? reported = null;

        var select = context.Render<TagMultiSelect>(parameters => parameters
            .Add(s => s.Options, Options)
            .Add(s => s.AllowCreate, true)
            .Add(s => s.SelectedValuesChanged, (IReadOnlyList<string> values) => reported = values));

        select.Find("input").Input("qa-new-tag");
        select.FindAll("[role='option']").Last().Click();

        Assert.Equal(["qa-new-tag"], reported);
    }

    [Fact]
    public void Chips_past_the_visible_limit_collapse_into_a_count()
    {
        using var context = new BunitContext();

        var select = context.Render<TagMultiSelect>(parameters => parameters
            .Add(s => s.Options, Options)
            .Add(s => s.MaxVisible, 1)
            .Add(s => s.SelectedValues, new[] { "desktop", "spacing", "sync" }));

        Assert.Single(select.FindAll(".tag-select__chip"));
        Assert.Equal("+2", select.Find(".tag-select__overflow").TextContent);
    }

    /// <summary>Three candidates from two repositories, in the order a host that
    /// owns the section order would hand them over: one run per section.</summary>
    private static readonly SelectorOption[] Grouped =
    [
        new("1", "Provision the box", null, "backlog"),
        new("2", "Ship the release", null, "backlog"),
        new("3", "Write the changelog", null, "docs")
    ];

    [Fact]
    public void A_run_of_options_sharing_a_group_becomes_a_labelled_section()
    {
        using var context = new BunitContext();

        var select = context.Render<TagMultiSelect>(parameters => parameters.Add(s => s.Options, Grouped));
        select.Find("input").Focus();

        var sections = select.FindAll(".tag-select__group");

        Assert.Equal(2, sections.Count);
        Assert.All(sections, section => Assert.Equal("group", section.GetAttribute("role")));

        // aria-labelledby has to land on an element that is actually there and
        // actually says the name — a section labelled by a missing id announces
        // nothing at all.
        string[] names = ["backlog", "docs"];
        for (var i = 0; i < names.Length; i++)
        {
            var labelId = sections[i].GetAttribute("aria-labelledby");
            Assert.False(string.IsNullOrWhiteSpace(labelId));
            Assert.Equal(names[i], select.Find("#" + labelId).TextContent);
            Assert.Equal(names[i], sections[i].QuerySelector(".tag-select__group-label")?.TextContent);
        }

        Assert.Equal(
            ["Provision the box", "Ship the release"],
            sections[0].QuerySelectorAll("[role='option']").Select(option => option.TextContent));
        Assert.Equal(
            ["Write the changelog"],
            sections[1].QuerySelectorAll("[role='option']").Select(option => option.TextContent));
    }

    /// <summary>Sections nest the options one level deeper in the DOM, and the ids
    /// the reading cursor names them by are flat and unchanged. Nothing moves the
    /// focus in this control, so an <c>aria-activedescendant</c> that stopped
    /// resolving would leave the keyboard silently pointing at nothing.</summary>
    [Fact]
    public void Sections_leave_the_flat_option_ids_the_reading_cursor_resolves_alone()
    {
        using var context = new BunitContext();

        var select = context.Render<TagMultiSelect>(parameters => parameters
            .Add(s => s.Options, Grouped)
            .Add(s => s.Id, "deps"));

        var input = select.Find("input");
        input.Focus();

        Assert.Equal(
            ["deps-option-0", "deps-option-1", "deps-option-2"],
            select.FindAll("[role='option']").Select(option => option.Id));

        input.KeyDown(new KeyboardEventArgs { Key = "ArrowDown" });
        input.KeyDown(new KeyboardEventArgs { Key = "ArrowDown" });
        input.KeyDown(new KeyboardEventArgs { Key = "ArrowDown" });

        // The third option is in the second section; the cursor crosses the
        // boundary without noticing it.
        var active = select.Find("input").GetAttribute("aria-activedescendant");
        Assert.Equal("deps-option-2", active);
        Assert.Equal("Write the changelog", select.Find("#" + active).TextContent);
        Assert.Equal("true", select.Find("#" + active).GetAttribute("aria-selected"));
    }

    /// <summary>A section is a run of what survived the query, so a query that
    /// leaves a section with nothing in it takes the heading with it — there is no
    /// separate list of sections that could go on claiming one exists.</summary>
    [Fact]
    public void A_query_that_empties_a_section_takes_its_heading_with_it()
    {
        using var context = new BunitContext();

        var select = context.Render<TagMultiSelect>(parameters => parameters.Add(s => s.Options, Grouped));

        select.Find("input").Input("changelog");

        Assert.Equal(
            ["docs"],
            select.FindAll(".tag-select__group-label").Select(label => label.TextContent));
        Assert.Single(select.FindAll("[role='option']"));
    }

    /// <summary>An option with no group is not given an invented one. Every
    /// existing host passes no group at all, and their lists have to stay exactly
    /// the markup they were.</summary>
    [Fact]
    public void Options_with_no_group_stay_direct_children_of_the_listbox()
    {
        using var context = new BunitContext();

        var select = context.Render<TagMultiSelect>(parameters => parameters.Add(s => s.Options, Options));
        select.Find("input").Focus();

        Assert.Empty(select.FindAll(".tag-select__group"));

        var listbox = select.Find("[role='listbox']");
        Assert.Equal(
            ["desktop", "spacing", "sync"],
            listbox.Children.Select(child => child.TextContent));
        Assert.All(listbox.Children, child => Assert.Equal("option", child.GetAttribute("role")));
    }

    /// <summary>Sections are runs, not a grouping the control derives: two
    /// non-adjacent options naming the same section stay two sections, because
    /// coalescing them would be this control deciding an order the host owns. The
    /// ungrouped option between them stays bare, and the create row stays outside
    /// every section.</summary>
    [Fact]
    public void Sections_are_runs_so_a_repeated_name_is_a_second_section()
    {
        using var context = new BunitContext();

        var select = context.Render<TagMultiSelect>(parameters => parameters
            .Add(s => s.AllowCreate, true)
            .Add(s => s.Options, new SelectorOption[]
            {
                new("1", "alpha", null, "backlog"),
                new("2", "beta"),
                new("3", "gamma", null, "backlog")
            }));

        select.Find("input").Input("a");

        Assert.Equal(
            ["backlog", "backlog"],
            select.FindAll(".tag-select__group-label").Select(label => label.TextContent));

        var listbox = select.Find("[role='listbox']");
        Assert.Equal(
            ["group", "option", "group", "option"],
            listbox.Children.Select(child => child.GetAttribute("role")));

        // The create row is the last child of the listbox itself, and it still
        // carries the index after the last match.
        var create = listbox.Children[^1];
        Assert.Equal("Add \"a\"", create.TextContent);
        Assert.EndsWith("-option-3", create.Id, StringComparison.Ordinal);
    }
}
