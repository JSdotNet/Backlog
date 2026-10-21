namespace Backlog.Modules.Sessions.UnitTests;

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
            AgentSessionViews.Of(Rows(Sample), AgentSessionView.Live)
                .Select(session => session.Id)
                .OrderBy(id => id, StringComparer.Ordinal));

    [Fact]
    public void The_live_view_never_keeps_a_finished_session() =>
        // The invariant this filter is the code home for: with no liveness evidence
        // a session is Finished, and Finished is the one thing Live is not.
        Assert.All(
            AgentSessionViews.Of(Rows(Sample), AgentSessionView.Live),
            session => Assert.NotEqual(AgentSessionState.Finished, session.State));

    [Fact]
    public void All_is_every_session_unchanged()
    {
        var rows = Rows(Sample);

        // The same list, not a copy of it. All is the absence of a filter, and a
        // rebuilt list would be work done to arrive back where it started.
        Assert.Same(rows, AgentSessionViews.Of(rows, AgentSessionView.All));
    }

    [Fact]
    public void Nothing_filtered_is_an_empty_list_rather_than_null()
    {
        // A machine where every agent has gone home. The surface decides what
        // "nothing live" looks like, and it can only do that if it is handed a list.
        var live = AgentSessionViews.Of(
            Rows([Session("done", AgentSessionState.Finished, Noon.AddHours(-6))]),
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
            AgentSessionViews.Of(Rows(Sample), AgentSessionView.Live).Select(session => session.Id));

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
            AgentSessionGroups.Of(Rows(Sample), AgentSessionGrouping.Environment).Select(group => group.Name));

        var live = AgentSessionGroups.Of(
            AgentSessionViews.Of(Rows(Sample), AgentSessionView.Live),
            AgentSessionGrouping.Environment);

        var group = Assert.Single(live);

        Assert.Equal("DEV-TOWER", group.Name);
        Assert.Equal(2, group.Rows.Count);
    }

    [Theory]
    [InlineData(AgentSessionView.Live, "Live")]
    [InlineData(AgentSessionView.Open, "Open")]
    [InlineData(AgentSessionView.All, "All")]
    public void A_view_has_one_name_on_screen(AgentSessionView view, string label) =>
        // One label function, so the strip's button and anything that names the
        // view in a sentence cannot disagree about what it is called.
        Assert.Equal(label, AgentSessionViews.Label(view));

    /// <summary>The strip draws the views in the order the enum declares them, so the
    /// declaration is the screen order: Open sits between Live and All.</summary>
    [Fact]
    public void The_views_are_live_open_and_all_in_that_order() =>
        Assert.Equal(
            [AgentSessionView.Live, AgentSessionView.Open, AgentSessionView.All],
            Enum.GetValues<AgentSessionView>());

    /// <summary>
    /// Open is a question about the desktop app's record, not about liveness: it keeps
    /// every row the desktop app has not archived, Finished ones included, and nothing
    /// else — in the order it was given, like Live.
    /// </summary>
    [Fact]
    public void The_open_view_keeps_exactly_the_unarchived_rows_whatever_their_state() =>
        Assert.Equal(
            ["open-stalled", "open-finished", "open-running"],
            AgentSessionViews.Of(Rows(DesktopSample), AgentSessionView.Open).Select(row => row.Id));

    /// <summary>Unknown is never open. A session with no desktop record behind it — a
    /// CLI-only one, any Copilot or replicated one — has nothing that says it is open,
    /// and a run with no session has no record at all.</summary>
    [Fact]
    public void The_open_view_never_keeps_an_archived_unknown_or_run_only_row()
    {
        var run = SessionRowsTests.Run("run-1", "elsewhere-0a1b2c3d", Noon.AddHours(-3), Noon.AddHours(-2));

        var rows = SessionRows.Of(DesktopSample, [run]);

        Assert.Contains(rows, row => row.RunOnly);
        Assert.DoesNotContain(
            AgentSessionViews.Of(rows, AgentSessionView.Open),
            row => row.RunOnly || row.Session!.Archived is not false);
    }

    /// <summary>The invariant Live rests on, re-proved with the desktop record present:
    /// an open session with no live marker reads Finished, and Finished is never
    /// live.</summary>
    [Fact]
    public void An_open_finished_session_is_not_live() =>
        Assert.DoesNotContain(
            AgentSessionViews.Of(Rows(DesktopSample), AgentSessionView.Live),
            row => row.Id == "open-finished");

    private static readonly IReadOnlyList<AgentSession> DesktopSample =
    [
        Session("open-stalled", AgentSessionState.Stalled, Noon.AddMinutes(-40)) with { Archived = false },
        Session("archived", AgentSessionState.Finished, Noon.AddMinutes(-30)) with { Archived = true },
        Session("open-finished", AgentSessionState.Finished, Noon.AddMinutes(-20)) with { Archived = false },
        Session("unknown", AgentSessionState.Running, Noon.AddMinutes(-5)),
        Session("open-running", AgentSessionState.Running, Noon.AddMinutes(-2)) with { Archived = false }
    ];

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
            // The name lower-cased stands in for the id here: this file is about which
            // sessions a view admits, and the only thing it needs of an environment is
            // that two different names are two different environments.
            EnvironmentId: environment.ToLowerInvariant(),
            Environment: environment,
            Title: id,
            WorkingFolder: @"D:\Repos\Backlog",
            Repository: null,
            Branch: null,
            StartedAt: lastActivity.AddHours(-1),
            LastActivityAt: lastActivity,
            State: state,

            // A view admits a session on its state alone, so neither of these can
            // change an answer here; they are stated because the record requires
            // every construction site to say them.
            TurnCount: null,
            Origin: AgentSessionOrigin.Local);

    /// <summary>The list the pure functions now take: every session as a row of its
    /// own, no runs on any of them. What the pane builds from a reading with no run
    /// files behind it.</summary>
    private static IReadOnlyList<SessionRow> Rows(IReadOnlyList<AgentSession> sessions) => SessionRows.Of(sessions, []);
}
