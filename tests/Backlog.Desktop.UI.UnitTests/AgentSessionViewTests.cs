namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The view is a pure function over the sessions it is given, the same as the
/// grouping beside it — and the one that does the thing grouping must never do.
/// What is asserted here is that Live leaves sessions out, that the ones it
/// leaves out are exactly the finished ones, and that All leaves the list alone.
/// </summary>
public sealed class AgentSessionViewTests
{
    private static readonly DateTimeOffset Noon = new(2026, 8, 19, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void The_live_view_is_running_and_stalled_together() =>
        // Both, because both are states the machine has evidence for. Stalled is a
        // session that has gone quiet, not one that has ended.
        Assert.Equal(
            ["running", "stalled"],
            AgentSessionViews.Of(Sample, AgentSessionView.Live)
                .Select(session => session.Id)
                .OrderBy(id => id, StringComparer.Ordinal));

    [Fact]
    public void The_live_view_never_keeps_a_finished_session() =>
        // The invariant this filter is the code home for: with no liveness evidence
        // a session is Finished, and Finished is the one thing Live is not.
        Assert.All(
            AgentSessionViews.Of(Sample, AgentSessionView.Live),
            session => Assert.NotEqual(AgentSessionState.Finished, session.State));

    [Fact]
    public void All_is_every_session_unchanged() =>
        // The same list, not a copy of it. All is the absence of a filter, and a
        // rebuilt list would be work done to arrive back where it started.
        Assert.Same(Sample, AgentSessionViews.Of(Sample, AgentSessionView.All));

    [Fact]
    public void Nothing_filtered_is_an_empty_list_rather_than_null()
    {
        // A machine where every agent has gone home. The surface decides what
        // "nothing live" looks like, and it can only do that if it is handed a list.
        var live = AgentSessionViews.Of(
            [Session("done", AgentSessionState.Finished, Noon.AddHours(-6))],
            AgentSessionView.Live);

        Assert.NotNull(live);
        Assert.Empty(live);
    }

    /// <summary>
    /// The filter does not sort. Ordering the list is the grouping's job, and a
    /// filter that also reordered would be two operations wearing one name — as
    /// well as a second answer to "what does most recently active first mean".
    /// </summary>
    [Fact]
    public void Filtering_preserves_the_order_it_was_given() =>
        Assert.Equal(
            ["stalled", "running"],
            AgentSessionViews.Of(Sample, AgentSessionView.Live).Select(session => session.Id));

    /// <summary>
    /// The composition the pane uses: view first, then grouping. A machine with
    /// nothing live on it loses its section rather than keeping an empty one,
    /// because the grouping never sees the sessions the view removed.
    /// </summary>
    [Fact]
    public void Filtering_then_grouping_is_grouping_over_what_survived()
    {
        Assert.Equal(
            ["DEV-LAPTOP", "DEV-TOWER"],
            AgentSessionGroups.Of(Sample, AgentSessionGrouping.Environment).Select(group => group.Name));

        var live = AgentSessionGroups.Of(
            AgentSessionViews.Of(Sample, AgentSessionView.Live),
            AgentSessionGrouping.Environment);

        var group = Assert.Single(live);

        Assert.Equal("DEV-TOWER", group.Name);
        Assert.Equal(2, group.Sessions.Count);
    }

    [Theory]
    [InlineData(AgentSessionView.Live, "Live")]
    [InlineData(AgentSessionView.All, "All")]
    public void A_view_has_one_name_on_screen(AgentSessionView view, string label) =>
        // One label function, so the strip's button and anything that names the
        // view in a sentence cannot disagree about what it is called.
        Assert.Equal(label, AgentSessionViews.Label(view));

    /// <summary>
    /// Deliberately not in activity order, so the order test above is measuring
    /// what it was given rather than what a sort would have produced anyway.
    /// </summary>
    private static readonly IReadOnlyList<AgentSession> Sample =
    [
        Session("stalled", AgentSessionState.Stalled, Noon.AddMinutes(-40)),
        Session("finished-recent", AgentSessionState.Finished, Noon.AddMinutes(-10)),
        Session("running", AgentSessionState.Running, Noon.AddMinutes(-2)),
        Session("finished-laptop", AgentSessionState.Finished, Noon.AddDays(-3), "DEV-LAPTOP")
    ];

    private static AgentSession Session(
        string id,
        AgentSessionState state,
        DateTimeOffset lastActivity,
        string environment = "DEV-TOWER") =>
        new(
            Id: id,
            Kind: AgentSessionKind.Claude,
            Environment: environment,
            Title: id,
            WorkingFolder: @"D:\Repos\Backlog",
            Repository: null,
            Branch: null,
            StartedAt: lastActivity.AddHours(-1),
            LastActivityAt: lastActivity,
            State: state);
}
