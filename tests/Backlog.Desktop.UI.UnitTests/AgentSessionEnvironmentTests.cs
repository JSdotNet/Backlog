namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The environment narrowing is a pure function over the sessions it is given, the
/// same as the view and the grouping beside it. What is asserted here is that the
/// environments on offer are exactly the ones with records behind them — named and
/// ordered the way the grouping names and orders its sections — that narrowing
/// keeps exactly one environment's rows in the order it was given, and that no
/// environment is the list left alone.
/// </summary>
public sealed class AgentSessionEnvironmentTests
{
    private static readonly DateTimeOffset Noon = new(2026, 8, 19, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void The_environments_on_offer_are_the_ones_with_records_behind_them() =>
        // One option per environment id, however many sessions each holds, and none
        // for an environment nothing names: an option that could only empty the
        // list is not something this can produce.
        Assert.Equal(
            ["laptop", "tower"],
            AgentSessionEnvironments.Of(Sample).Select(environment => environment.Id));

    /// <summary>
    /// The filter's options and the grouping's sections are one derivation. A
    /// reader who picks "DEV-LAPTOP" in the select and then groups by environment
    /// sees a section with that exact heading, in the position the option had.
    /// </summary>
    [Fact]
    public void The_environments_are_named_and_ordered_as_the_grouping_draws_its_sections() =>
        Assert.Equal(
            AgentSessionGroups.Of(Sample, AgentSessionGrouping.Environment).Select(group => group.Name),
            AgentSessionEnvironments.Of(Sample).Select(environment => environment.Name));

    /// <summary>
    /// Keyed on the id and named by the newest session, so a machine renamed since
    /// its oldest record is one option under its current name rather than two under
    /// both.
    /// </summary>
    [Fact]
    public void A_renamed_machine_is_one_environment_under_its_newest_name()
    {
        IReadOnlyList<AgentSession> renamed =
        [
            Session("old", Noon.AddDays(-5), "tower", "TOWER-OLD"),
            Session("new", Noon.AddHours(-1), "tower", "DEV-TOWER")
        ];

        var environment = Assert.Single(AgentSessionEnvironments.Of(renamed));

        Assert.Equal("tower", environment.Id);
        Assert.Equal("DEV-TOWER", environment.Name);
    }

    [Fact]
    public void An_environment_with_no_name_is_offered_under_its_id()
    {
        // A wire field can be blank. An option with an empty label cannot be told
        // from "All machines" above it, so the id stands in.
        var environment = Assert.Single(AgentSessionEnvironments.Of([Session("s", Noon, "8f3d5c11", "")]));

        Assert.Equal("8f3d5c11", environment.Name);
    }

    [Fact]
    public void Narrowing_keeps_exactly_one_environments_sessions() =>
        Assert.Equal(
            ["tower-stalled", "tower-running"],
            AgentSessionEnvironments.On(Sample, "tower").Select(session => session.Id));

    /// <summary>
    /// The narrowing does not sort. Ordering the list is the grouping's job, and a
    /// filter that also reordered would be a second answer to "what does most
    /// recently active first mean".
    /// </summary>
    [Fact]
    public void Narrowing_preserves_the_order_it_was_given() =>
        Assert.Equal(
            ["laptop-finished", "laptop-running"],
            AgentSessionEnvironments.On(Sample, "laptop").Select(session => session.Id));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void No_environment_is_every_session_unchanged(string? environmentId) =>
        // The same list, not a copy of it: no environment is the absence of a
        // filter, the way All is for the view. Blank and null both mean it, because
        // a select hands back an empty string for its empty option.
        Assert.Same(Sample, AgentSessionEnvironments.On(Sample, environmentId));

    [Fact]
    public void Narrowing_is_on_the_id_and_not_the_name()
    {
        // Two machines called the same thing are two environments. A narrowing on
        // the name would merge them; the id keeps them apart.
        IReadOnlyList<AgentSession> twins =
        [
            Session("first", Noon.AddHours(-1), "a", "DEV-PC"),
            Session("second", Noon.AddHours(-2), "b", "DEV-PC")
        ];

        Assert.Equal(2, AgentSessionEnvironments.Of(twins).Count);
        Assert.Equal(["first"], AgentSessionEnvironments.On(twins, "a").Select(session => session.Id));
        Assert.Empty(AgentSessionEnvironments.On(twins, "DEV-PC"));
    }

    /// <summary>
    /// The composition the pane uses: environment, then view, then grouping. The
    /// grouping never sees what either narrowing removed, so a machine whose live
    /// sessions were all somewhere else loses its section rather than keeping an
    /// empty one.
    /// </summary>
    [Fact]
    public void Narrowing_then_viewing_then_grouping_is_grouping_over_what_survived()
    {
        var live = AgentSessionGroups.Of(
            AgentSessionViews.Of(AgentSessionEnvironments.On(Sample, "laptop"), AgentSessionView.Live),
            AgentSessionGrouping.Environment);

        var group = Assert.Single(live);

        Assert.Equal("DEV-LAPTOP", group.Name);
        Assert.Equal(["laptop-running"], group.Sessions.Select(session => session.Id));
    }

    /// <summary>
    /// Deliberately not in activity order and not grouped by machine, so the order
    /// tests above are measuring what they were given rather than what a sort would
    /// have produced anyway.
    /// </summary>
    private static readonly IReadOnlyList<AgentSession> Sample =
    [
        Session("tower-stalled", Noon.AddMinutes(-40), "tower", "DEV-TOWER", AgentSessionState.Stalled),
        Session("laptop-finished", Noon.AddDays(-3), "laptop", "DEV-LAPTOP", AgentSessionState.Finished),
        Session("tower-running", Noon.AddMinutes(-2), "tower", "DEV-TOWER"),
        Session("laptop-running", Noon.AddMinutes(-5), "laptop", "DEV-LAPTOP")
    ];

    private static AgentSession Session(
        string id,
        DateTimeOffset lastActivity,
        string environmentId,
        string environment,
        AgentSessionState state = AgentSessionState.Running) =>
        new(
            Id: id,
            Kind: AgentSessionKind.Claude,
            EnvironmentId: environmentId,
            Environment: environment,
            Title: id,
            WorkingFolder: @"D:\Repos\Backlog",
            Repository: null,
            Branch: null,
            StartedAt: lastActivity.AddHours(-1),
            LastActivityAt: lastActivity,
            State: state,

            // Neither can change an answer here: the narrowing asks the environment
            // id alone. Stated because the record requires every construction site
            // to say them.
            TurnCount: null,
            Origin: AgentSessionOrigin.Local);
}
