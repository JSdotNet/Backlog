namespace Backlog.Modules.Sessions.UnitTests;

/// <summary>
/// The title reader: the prefixes a person writes by hand and the titles the
/// backlog-tools skills write, read into a purpose, a plan tag, a plan state and
/// the title the row shows. Every row of the grammar in
/// <c>.devbook/domain/sessions/domain.md#session-title-reading</c> is a case here.
/// </summary>
public sealed class SessionTitlesTests
{
    private static readonly DateTimeOffset Noon = new(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("ARTIFACT: Shell window management", SessionPurpose.Artifact, "Shell window management")]
    [InlineData("DESIGN: Sessions purpose and plans", SessionPurpose.Design, "Sessions purpose and plans")]
    [InlineData("RESEARCH: Tailcat for sync service", SessionPurpose.Research, "Tailcat for sync service")]
    [InlineData("HOLD: Fix scenarios specFile()", SessionPurpose.Hold, "Fix scenarios specFile()")]
    public void A_standing_prefix_is_the_purpose_and_leaves_the_title(string title, SessionPurpose purpose, string shown)
    {
        var reading = SessionTitles.Read(title);

        Assert.Equal((purpose, shown, (string?)null, (PlanState?)null), (reading.Purpose, reading.Shown, reading.PlanTag, reading.State));
        Assert.Equal(title, reading.Raw);
    }

    [Theory]
    [InlineData("PLAN: +management-bots — app management bots")]
    [InlineData("PLAN: +management-bots – app management bots")]
    [InlineData("PLAN: +management-bots - app management bots")]
    public void A_plan_with_a_tag_is_a_draft_under_any_of_the_three_dashes(string title)
    {
        var reading = SessionTitles.Read(title);

        Assert.Equal(
            (SessionPurpose.Plan, "management-bots", (PlanState?)PlanState.Draft, "app management bots"),
            (reading.Purpose, reading.PlanTag, reading.State, reading.Shown));
    }

    [Fact]
    public void A_plus_plan_is_imported_and_may_carry_no_tag()
    {
        var reading = SessionTitles.Read("+PLAN: Spec-manager question notifications");

        Assert.Equal(
            (SessionPurpose.Plan, (string?)null, (PlanState?)PlanState.Imported, "Spec-manager question notifications"),
            (reading.Purpose, reading.PlanTag, reading.State, reading.Shown));
    }

    [Fact]
    public void A_plan_whose_tag_is_malformed_is_a_plan_without_one()
    {
        var reading = SessionTitles.Read("PLAN: +9bad — a tag starts with a letter");

        Assert.Equal(
            (SessionPurpose.Plan, (string?)null, "+9bad — a tag starts with a letter"),
            (reading.Purpose, reading.PlanTag, reading.Shown));
    }

    [Fact]
    public void A_plus_plan_with_a_tag_carries_it()
    {
        var reading = SessionTitles.Read("+PLAN: +notifications — Notifications");

        Assert.Equal(("notifications", (PlanState?)PlanState.Imported, "Notifications"), (reading.PlanTag, reading.State, reading.Shown));
    }

    [Theory]
    [InlineData("+spec-manager-questions:execute-plan @spec-manager", "execute-plan @spec-manager")]
    [InlineData("+spec-manager-questions:execute-plan", "execute-plan")]
    public void An_execute_plan_title_is_a_plan_run_of_its_tag(string title, string shown)
    {
        var reading = SessionTitles.Read(title);

        Assert.Equal(
            (SessionPurpose.PlanRun, "spec-manager-questions", shown),
            (reading.Purpose, reading.PlanTag, reading.Shown));
    }

    [Fact]
    public void A_numbered_entry_title_is_an_entry_of_its_tag_with_its_step()
    {
        var reading = SessionTitles.Read("technology-view:3 - Allow plugin chapter types");

        Assert.Equal(
            (SessionPurpose.Entry, "technology-view", (int?)3, "3 - Allow plugin chapter types"),
            (reading.Purpose, reading.PlanTag, reading.Step, reading.Shown));
    }

    [Theory]
    [InlineData("Install backlog-tools permission allowlist (Notes)")]
    [InlineData("Plan: the merge permission")]
    [InlineData("plan: lower-case prefix")]
    [InlineData("technology-view - an unnumbered entry")]
    [InlineData("")]
    public void Anything_else_is_other_and_shown_whole(string title)
    {
        var reading = SessionTitles.Read(title);

        Assert.Equal((SessionPurpose.Other, title, (string?)null), (reading.Purpose, reading.Shown, reading.PlanTag));
    }

    [Fact]
    public void No_title_is_other_and_shown_empty()
    {
        var reading = SessionTitles.Read(null);

        Assert.Equal((SessionPurpose.Other, string.Empty), (reading.Purpose, reading.Shown));
    }

    [Fact]
    public void Tags_compare_case_blind_with_the_plus_lifted()
    {
        Assert.True(SessionTitles.SameTag("+Technology-View", "technology-view"));
        Assert.True(SessionTitles.SameTag("technology-view", "TECHNOLOGY-VIEW"));
        Assert.False(SessionTitles.SameTag("technology-view", "technology"));
    }

    [Fact]
    public void A_scheduled_task_run_is_scheduled_whatever_its_title_says()
    {
        var row = SessionRow.Of(Session("s", "ARTIFACT: a nightly sweep") with { ScheduledTask = "nightly-sweep" });

        Assert.Equal(SessionPurpose.Scheduled, SessionTitles.Of(row).Purpose);
    }

    [Fact]
    public void A_schedule_row_is_scheduled()
    {
        var run = SessionRowsTests.Run("r1", "wt", Noon, Noon) with
        {
            Trigger = DeliveryRunTriggers.Scheduled,
            Schedule = "PLAN: +foo — weekly"
        };

        Assert.Equal(SessionPurpose.Scheduled, SessionTitles.Of(new SessionRow(null, [run])).Purpose);
    }

    [Fact]
    public void A_row_reads_the_title_the_desktop_app_gives_it()
    {
        var row = SessionRow.Of(Session("s", "the transcript's title") with { DesktopTitle = "HOLD: waiting on review" });

        Assert.Equal((SessionPurpose.Hold, "waiting on review"), (SessionTitles.Of(row).Purpose, SessionTitles.Of(row).Shown));
    }

    // ---- Narrowings (seam 3) ----

    private static readonly IReadOnlyList<SessionRow> PlanSample =
    [
        Row("draft-plan", "PLAN: +bots — app management bots", Noon.AddHours(-1)),
        Row("bots-entry", "bots:2 - second step", Noon.AddHours(-2)),
        Row("imported-plan", "+PLAN: +notifications — notifications", Noon.AddHours(-3)),
        Row("notif-entry", "notifications:1 - first step", Noon.AddHours(-4)),
        Row("view-run", "+Technology-View:execute-plan", Noon.AddHours(-5)),
        Row("view-entry", "technology-view:1 - only step", Noon.AddHours(-6)),
        Row("orphan-entry", "lonely:1 - no plan session", Noon.AddHours(-7)),
        Row("artifact", "ARTIFACT: proposal", Noon.AddHours(-8)),
        Row("other", "Plan merge permission", Noon.AddHours(-9))
    ];

    [Fact]
    public void A_purpose_narrows_to_its_rows_and_none_is_the_same_list()
    {
        Assert.Same(PlanSample, SessionPurposes.On(PlanSample, null));
        Assert.Equal(["bots-entry", "notif-entry", "view-entry", "orphan-entry"], SessionPurposes.On(PlanSample, SessionPurpose.Entry).Select(row => row.Id));
    }

    [Fact]
    public void A_purpose_counts_its_rows()
    {
        var counts = SessionPurposes.Counts(PlanSample);

        Assert.Equal(4, counts[SessionPurpose.Entry]);
        Assert.Equal(2, counts[SessionPurpose.Plan]);
        Assert.Equal(0, counts[SessionPurpose.Hold]);
        Assert.Equal(Enum.GetValues<SessionPurpose>().Length, counts.Count);
    }

    [Fact]
    public void A_plan_narrows_to_its_rows_case_blind_and_none_is_the_same_list()
    {
        Assert.Same(PlanSample, SessionPlans.On(PlanSample, null));
        Assert.Equal(["view-run", "view-entry"], SessionPlans.On(PlanSample, "+technology-view").Select(row => row.Id));
    }

    [Fact]
    public void Every_tag_present_is_offered_with_its_plans_state()
    {
        var plans = SessionPlans.Of(PlanSample);

        Assert.Equal(
            [("bots", (PlanState?)PlanState.Draft), ("lonely", null), ("notifications", PlanState.Imported), ("technology-view", PlanState.Imported)],
            plans.Select(plan => (plan.Tag.ToLowerInvariant(), plan.State)));
    }

    [Fact]
    public void A_plan_state_keeps_every_row_of_a_plan_in_that_state()
    {
        Assert.Same(PlanSample, SessionPlans.InState(PlanSample, null));
        Assert.Equal(["draft-plan", "bots-entry"], SessionPlans.InState(PlanSample, PlanState.Draft).Select(row => row.Id));
        Assert.Equal(
            ["imported-plan", "notif-entry", "view-run", "view-entry"],
            SessionPlans.InState(PlanSample, PlanState.Imported).Select(row => row.Id));
    }

    [Fact]
    public void A_plan_state_reads_the_plans_off_the_whole_list_not_the_narrowed_one()
    {
        // Narrowed to entries first, the PLAN row that makes "bots" a draft is gone; the
        // state still comes from the plans the whole reading holds.
        var entries = SessionPurposes.On(PlanSample, SessionPurpose.Entry);

        Assert.Equal(["bots-entry"], SessionPlans.InState(entries, PlanState.Draft, SessionPlans.Of(PlanSample)).Select(row => row.Id));
    }

    // ---- Groupings (seam 4) ----

    [Fact]
    public void By_plan_is_a_section_per_tag_with_the_plan_then_its_runs_then_its_entries_by_step()
    {
        IReadOnlyList<SessionRow> rows =
        [
            Row("entry-2", "foo:2 - second", Noon),
            Row("run", "+foo:execute-plan", Noon.AddMinutes(-1)),
            Row("entry-1", "FOO:1 - first", Noon.AddMinutes(-2)),
            Row("plan", "+PLAN: +foo — the plan", Noon.AddMinutes(-3)),
            Row("other", "something", Noon.AddMinutes(-4)),
            Row("bar-entry", "bar:1 - first", Noon.AddMinutes(-5))
        ];

        var groups = AgentSessionGroups.Of(rows, AgentSessionGrouping.Plan, Noon, TimeZoneInfo.Utc);

        Assert.Equal(["plan/bar", "plan/foo", null], groups.Select(group => group.Key));
        Assert.Equal(["plan", "run", "entry-1", "entry-2"], groups[1].Rows.Select(row => row.Id));
        Assert.Equal(AgentSessionGroups.NotPartOfAPlan, groups[^1].Name);
        Assert.Equal(["other"], groups[^1].Rows.Select(row => row.Id));
    }

    /// <summary>A plan may be tagged "scheduled", and its section still never shares a
    /// key with the Scheduled section the Open view closes the list with.</summary>
    [Fact]
    public void A_plan_tagged_scheduled_never_shares_a_key_with_the_scheduled_section()
    {
        IReadOnlyList<SessionRow> rows =
        [
            Row("entry", "scheduled:1 - a step", Noon),
            new SessionRow(Session("routine", "morning brief") with { ScheduledTask = "morning-brief" }, [])
        ];

        var sections = AgentSessionGroups.ScheduledApart(
            AgentSessionGroups.Of(rows, AgentSessionGrouping.Plan, Noon, TimeZoneInfo.Utc));

        Assert.Equal(["plan/scheduled", AgentSessionGroups.ScheduledKey], sections.Select(section => section.Key));
        Assert.Equal("scheduled", AgentSessionGroups.PlanTagOf(sections[0].Key));
        Assert.Null(AgentSessionGroups.PlanTagOf(sections[1].Key));
    }

    [Fact]
    public void By_purpose_is_a_section_per_purpose_in_declared_order()
    {
        var groups = AgentSessionGroups.Of(PlanSample, AgentSessionGrouping.Purpose, Noon, TimeZoneInfo.Utc);

        Assert.Equal(["Artifact", "Plan", "Plan run", "Entry", "Other"], groups.Select(group => group.Name));
        Assert.Equal(["Artifact", "Plan", "PlanRun", "Entry", "Other"], groups.Select(group => group.Key));
    }

    [Fact]
    public void Under_when_the_standing_sessions_open_the_list_above_live_now()
    {
        IReadOnlyList<SessionRow> rows =
        [
            Row("live-other", "something", Noon, AgentSessionState.Running),
            Row("live-hold", "HOLD: running now", Noon, AgentSessionState.Running),
            Row("old-artifact", "ARTIFACT: a proposal", Noon.AddDays(-20)),
            Row("research", "RESEARCH: a question", Noon.AddHours(-1)),
            Row("entry", "foo:1 - a step", Noon.AddHours(-1)),
            new SessionRow(Session("routine", "ARTIFACT: scheduled") with { ScheduledTask = "nightly", LastActivityAt = Noon.AddHours(-1) }, [])
        ];

        var groups = AgentSessionGroups.Of(rows, AgentSessionGrouping.When, Noon, TimeZoneInfo.Utc);

        Assert.Equal(["standing", "live", "today"], groups.Select(group => group.Key));
        Assert.Equal(["research", "old-artifact"], groups[0].Rows.Select(row => row.Id));
        Assert.Equal("Standing", groups[0].Name);

        // A live standing session sits in Live now while it runs, and a scheduled one
        // is Scheduled before it is standing.
        Assert.Contains(groups[1].Rows, row => row.Id == "live-hold");
        Assert.Contains(groups[2].Rows, row => row.Id == "routine");
    }

    [Theory]
    [InlineData(AgentSessionGrouping.Plan)]
    [InlineData(AgentSessionGrouping.Purpose)]
    [InlineData(AgentSessionGrouping.When)]
    public void The_new_groupings_carry_every_row_exactly_once(AgentSessionGrouping grouping)
    {
        var grouped = AgentSessionGroups.Of(PlanSample, grouping, Noon, TimeZoneInfo.Utc)
            .SelectMany(group => group.Rows)
            .Select(row => row.Id)
            .Order(StringComparer.Ordinal);

        Assert.Equal(PlanSample.Select(row => row.Id).Order(StringComparer.Ordinal), grouped);
    }

    private static SessionRow Row(string id, string title, DateTimeOffset at, AgentSessionState state = AgentSessionState.Finished) =>
        SessionRow.Of(Session(id, title) with { LastActivityAt = at, State = state });

    private static AgentSession Session(string id, string title) =>
        new(
            Id: id,
            Kind: AgentSessionKind.Claude,
            EnvironmentId: "tower",
            Environment: "DEV-TOWER",
            Title: title,
            WorkingFolder: @"D:\Repos\Backlog",
            Repository: null,
            Branch: null,
            StartedAt: Noon.AddHours(-1),
            LastActivityAt: Noon,
            State: AgentSessionState.Finished,
            TurnCount: null,
            Origin: AgentSessionOrigin.Local);
}
