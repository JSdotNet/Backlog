namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The grouping is a pure function over the sessions it is given, which is the
/// whole reason it can be tested without a filesystem under it. What is asserted
/// here is that grouping rearranges and never edits: the same rows come back,
/// every time, in an order a reader can rely on.
/// </summary>
public sealed class AgentSessionGroupTests
{
    private static readonly DateTimeOffset Noon = new(2026, 8, 19, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Ungrouped_is_one_nameless_section_of_everything()
    {
        var group = Assert.Single(AgentSessionGroups.Of(Sample, AgentSessionGrouping.None));

        // Null rather than "All": a caller renders sections unconditionally and a
        // name of null is what tells it there is no heading to draw.
        Assert.Null(group.Name);
        Assert.Equal(Sample.Count, group.Sessions.Count);
    }

    [Fact]
    public void Nothing_grouped_is_no_sections_at_all()
    {
        // Not one empty section: the surface decides what "nothing" looks like, and
        // an empty section would make it draw a heading over it.
        Assert.Empty(AgentSessionGroups.Of([], AgentSessionGrouping.None));
        Assert.Empty(AgentSessionGroups.Of([], AgentSessionGrouping.Environment));
        Assert.Empty(AgentSessionGroups.Of([], AgentSessionGrouping.Kind));
    }

    [Fact]
    public void By_environment_is_a_section_per_machine_named_after_it()
    {
        var groups = AgentSessionGroups.Of(Sample, AgentSessionGrouping.Environment);

        // Sorted by name, deliberately not by size: a list that reordered its own
        // sections as sessions came and went would make the reader re-find the one
        // they were reading.
        Assert.Equal(["DEV-LAPTOP", "DEV-TOWER"], groups.Select(group => group.Name));
        Assert.Single(groups[0].Sessions);
        Assert.Equal(3, groups[1].Sessions.Count);
    }

    [Fact]
    public void By_type_is_a_section_per_assistant_in_the_order_the_enum_declares_them()
    {
        var groups = AgentSessionGroups.Of(Sample, AgentSessionGrouping.Kind);

        Assert.Equal(["Claude", "Copilot"], groups.Select(group => group.Name));
        Assert.All(groups[0].Sessions, session => Assert.Equal(AgentSessionKind.Claude, session.Kind));
        Assert.All(groups[1].Sessions, session => Assert.Equal(AgentSessionKind.Copilot, session.Kind));
    }

    /// <summary>
    /// The count grouping was given is the count it hands back. Grouping is not a
    /// filter, and this is the assertion that says so rather than the comment that
    /// claims it.
    /// <para>
    /// What the badge beside the pane's title says is the pane's business, not this
    /// one's: a view now sits in front of the grouping and does remove rows. See
    /// <see cref="AgentSessionViewTests"/>.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData(AgentSessionGrouping.None)]
    [InlineData(AgentSessionGrouping.Environment)]
    [InlineData(AgentSessionGrouping.Kind)]
    public void Every_grouping_carries_every_session(AgentSessionGrouping grouping)
    {
        var grouped = AgentSessionGroups.Of(Sample, grouping)
            .SelectMany(group => group.Sessions)
            .ToList();

        Assert.Equal(Sample.Count, grouped.Count);
        Assert.Equal(
            Sample.Select(session => session.Id).OrderBy(id => id),
            grouped.Select(session => session.Id).OrderBy(id => id));
    }

    [Fact]
    public void Inside_a_section_the_most_recently_active_session_is_first()
    {
        var groups = AgentSessionGroups.Of(Sample, AgentSessionGrouping.Environment);

        foreach (var group in groups)
        {
            var activity = group.Sessions.Select(session => session.LastActivityAt).ToList();

            Assert.Equal(activity.OrderByDescending(moment => moment), activity);
        }
    }

    /// <summary>
    /// One environment is one section however its name happened to be written down.
    /// The id is what makes them one, so the case of the name never comes into it —
    /// two sections for <c>DEV-TOWER</c> and <c>dev-tower</c> would be the same PC
    /// claiming to be two.
    /// </summary>
    [Fact]
    public void One_environment_groups_as_one_whatever_case_its_name_was_written_in()
    {
        IReadOnlyList<AgentSession> sessions =
        [
            Session("a", AgentSessionKind.Claude, "tower", "DEV-TOWER", Noon),
            Session("b", AgentSessionKind.Claude, "tower", "dev-tower", Noon.AddMinutes(-1))
        ];

        var group = Assert.Single(AgentSessionGroups.Of(sessions, AgentSessionGrouping.Environment));

        Assert.Equal(2, group.Sessions.Count);
    }

    /// <summary>
    /// The other half of the same rule, and the one a name-keyed grouping got wrong:
    /// two machines can be called the same thing, and merging them would report one
    /// machine's work as another's.
    /// </summary>
    [Fact]
    public void Two_environments_sharing_a_name_are_two_sections()
    {
        IReadOnlyList<AgentSession> sessions =
        [
            Session("a", AgentSessionKind.Claude, "tower", "DEV-TOWER", Noon),
            Session("b", AgentSessionKind.Claude, "laptop", "DEV-TOWER", Noon.AddMinutes(-1))
        ];

        var groups = AgentSessionGroups.Of(sessions, AgentSessionGrouping.Environment);

        Assert.Equal(2, groups.Count);
        Assert.All(groups, group => Assert.Equal("DEV-TOWER", group.Name));
    }

    /// <summary>
    /// A renamed machine shows the name it has now. The heading comes from the most
    /// recent session in the section rather than from whichever one the grouping
    /// happened to reach first, so the sessions recorded before a rename do not keep
    /// the old name on screen.
    /// </summary>
    [Fact]
    public void A_section_is_named_after_its_most_recent_record()
    {
        IReadOnlyList<AgentSession> sessions =
        [
            Session("older", AgentSessionKind.Claude, "tower", "OLD-NAME", Noon.AddDays(-2)),
            Session("newer", AgentSessionKind.Claude, "tower", "NEW-NAME", Noon)
        ];

        var group = Assert.Single(AgentSessionGroups.Of(sessions, AgentSessionGrouping.Environment));

        Assert.Equal("NEW-NAME", group.Name);
    }

    [Theory]
    [InlineData(AgentSessionKind.Claude, "Claude")]
    [InlineData(AgentSessionKind.Copilot, "Copilot")]
    public void An_assistant_has_one_name_on_screen(AgentSessionKind kind, string label) =>
        // One label function, so a section heading and a row's own cell cannot
        // disagree about what an assistant is called.
        Assert.Equal(label, AgentSessionGroups.Label(kind));

    private static readonly IReadOnlyList<AgentSession> Sample =
    [
        Session("claude-live", AgentSessionKind.Claude, "tower", "DEV-TOWER", Noon.AddMinutes(-2)),
        Session("copilot-old", AgentSessionKind.Copilot, "tower", "DEV-TOWER", Noon.AddHours(-9)),
        Session("claude-old", AgentSessionKind.Claude, "tower", "DEV-TOWER", Noon.AddHours(-30)),
        Session("copilot-laptop", AgentSessionKind.Copilot, "laptop", "DEV-LAPTOP", Noon.AddMinutes(-40))
    ];

    private static AgentSession Session(
        string id,
        AgentSessionKind kind,
        string environmentId,
        string environment,
        DateTimeOffset lastActivity) =>
        new(
            Id: id,
            Kind: kind,
            EnvironmentId: environmentId,
            Environment: environment,
            Title: id,
            WorkingFolder: @"D:\Repos\Backlog",
            Repository: null,
            Branch: null,
            StartedAt: lastActivity.AddHours(-1),
            LastActivityAt: lastActivity,
            State: AgentSessionState.Finished);
}
