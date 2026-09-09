namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// The sections a native select draws when its options name a group.
///
/// <para>An <c>optgroup</c> is the one thing here that cannot be checked by looking:
/// the list a select opens is an OS window rather than part of the page, so its
/// heading is absent from a screenshot and unreachable by a browser driver. What is
/// checkable is the markup handed to the platform, which is what these tests hold —
/// together with <c>SelectOptionStylingTests</c>, which holds the colours it is
/// painted in.</para>
///
/// <para>Pinned against the flat shape throughout, because the point of sections is
/// that they cost a list nobody grouped nothing at all: two golden markup strings in
/// <c>MetadataViewMarkupTests</c> spell out that list option by option, and they are
/// only allowed to keep passing.</para>
/// </summary>
public sealed class SelectSectionTests
{
    /// <summary>The shape the real caller hands over — <c>KnowledgeSourceSelection</c>
    /// offers a default, then the branches, then the local clone and the entry that
    /// fetches more. Two of the three unsectioned entries are <em>after</em> the
    /// section, which is the case a rule that hoisted them would get wrong.</summary>
    private static readonly SelectorOption[] Source =
    [
        new(string.Empty, "Default branch"),
        new("main", "main", null, "Branches"),
        new("release/2.0", "release/2.0", "Configured", "Branches"),
        new("__local__", "Local clone"),
        new("__load__", "Load branches…")
    ];

    private static readonly SelectorOption[] Flat =
    [
        new("Plugin", "Plugin"),
        new("McpServer", "MCP server")
    ];

    [Fact]
    public void A_run_of_options_sharing_a_group_becomes_an_optgroup()
    {
        using var context = new BunitContext();

        var field = context.Render<SelectField>(parameters => parameters.Add(f => f.Options, Source));

        var section = Assert.Single(field.FindAll("optgroup"));
        Assert.Equal("Branches", section.GetAttribute("label"));
        Assert.Equal(
            ["main", "release/2.0"],
            section.QuerySelectorAll("option").Select(option => option.GetAttribute("value")));

        // Every option is still in the list and still in the host's order; the
        // section changed how two of them are wrapped, not where any of them is.
        Assert.Equal(
            ["", "main", "release/2.0", "__local__", "__load__"],
            field.FindAll("option").Select(option => option.GetAttribute("value")));
    }

    /// <summary>
    /// The unsectioned entries stay where the host put them — including the two
    /// that come after the section.
    ///
    /// <para>Hoisting them to the top would be the control overruling an order the
    /// host owns: "Load branches…" is last because it is the least likely thing to
    /// want, and reading it before the branches it would fetch inverts that.</para>
    /// </summary>
    [Fact]
    public void An_option_naming_no_section_stays_exactly_where_the_host_put_it()
    {
        using var context = new BunitContext();

        var field = context.Render<SelectField>(parameters => parameters.Add(f => f.Options, Source));

        Assert.Equal(
            ["Default branch", "Local clone", "Load branches…"],
            field.Find("select").Children
                .Where(child => child.TagName.Equals("OPTION", StringComparison.OrdinalIgnoreCase))
                .Select(child => child.TextContent));
    }

    /// <summary>
    /// Two runs of one name stay two sections.
    ///
    /// <para>A <c>GroupBy</c> would pull the second "Branches" up into the first and
    /// silently reorder the list around it. The host decided that "Local clone" sits
    /// between them; a control that coalesced the runs would be a second, disagreeing
    /// definition of the order.</para>
    /// </summary>
    [Fact]
    public void Two_runs_of_the_same_name_stay_two_sections()
    {
        using var context = new BunitContext();

        var field = context.Render<SelectField>(parameters => parameters.Add(f => f.Options,
        [
            new SelectorOption("main", "main", null, "Branches"),
            new SelectorOption("__local__", "Local clone"),
            new SelectorOption("next", "next", null, "Branches")
        ]));

        var sections = field.FindAll("optgroup");

        Assert.Equal(2, sections.Count);
        Assert.All(sections, section => Assert.Equal("Branches", section.GetAttribute("label")));
        Assert.Equal(["main"], sections[0].QuerySelectorAll("option").Select(option => option.GetAttribute("value")));
        Assert.Equal(["next"], sections[1].QuerySelectorAll("option").Select(option => option.GetAttribute("value")));
    }

    /// <summary>A list nobody grouped renders what it rendered before sections
    /// existed: no wrapper, every option a direct child of the select.</summary>
    [Fact]
    public void A_list_that_names_no_section_renders_no_optgroup_at_all()
    {
        using var context = new BunitContext();

        var field = context.Render<SelectField>(parameters => parameters.Add(f => f.Options, Flat));

        Assert.Empty(field.FindAll("optgroup"));
        Assert.Equal(
            "<option value=\"Plugin\">Plugin</option><option value=\"McpServer\">MCP server</option>",
            field.Find("select").InnerHtml);
    }

    /// <summary>The empty entry holds no value, so there is no section it could
    /// honestly belong to — it stays a direct child, first, ahead of them all.</summary>
    [Fact]
    public void The_empty_entry_belongs_to_no_section()
    {
        using var context = new BunitContext();

        var field = context.Render<SelectField>(parameters => parameters
            .Add(f => f.Options, Source)
            .Add(f => f.IncludeEmptyOption, true)
            .Add(f => f.EmptyLabel, "None"));

        var first = field.Find("select").FirstElementChild;

        Assert.Equal("OPTION", first?.TagName, ignoreCase: true);
        Assert.Equal("None", first?.TextContent);
    }

    /// <summary>
    /// A badge's list draws the same sections as a field's.
    ///
    /// <para>No caller groups a badge's values today. The two components take the
    /// same <c>SelectorOption</c>, though, and a type that means one thing in one
    /// select and nothing in the other is the harder rule to remember than the one
    /// where both honour it.</para>
    /// </summary>
    [Fact]
    public void A_badge_select_draws_the_same_sections()
    {
        using var context = new BunitContext();

        var badge = context.Render<BadgeSelect>(parameters => parameters
            .Add(s => s.CurrentValue, "main")
            .Add(s => s.Options, Source));

        var section = Assert.Single(badge.FindAll("optgroup"));

        Assert.Equal("Branches", section.GetAttribute("label"));
        Assert.Equal(
            ["main", "release/2.0"],
            section.QuerySelectorAll("option").Select(option => option.GetAttribute("value")));
    }

    /// <summary>And costs the badge selects that group nothing, the same way. The
    /// <c>selected</c> on the current option is the DOM reflecting the select's
    /// value, not markup anything here emits — the golden strings in
    /// <c>MetadataViewMarkupTests</c> carry it for the same reason.</summary>
    [Fact]
    public void A_badge_select_over_a_flat_list_renders_no_optgroup_either()
    {
        using var context = new BunitContext();

        var badge = context.Render<BadgeSelect>(parameters => parameters
            .Add(s => s.CurrentValue, "Plugin")
            .Add(s => s.Options, Flat));

        Assert.Empty(badge.FindAll("optgroup"));
        Assert.Equal(
            "<option value=\"Plugin\" selected=\"\">Plugin</option><option value=\"McpServer\">MCP server</option>",
            badge.Find("select").InnerHtml);
    }
}
