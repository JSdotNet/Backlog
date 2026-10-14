using Backlog.Modules.Dashboard.Abstractions;
using Backlog.Modules.Dashboard.Abstractions.Services;
using Backlog.Modules.Dashboard.UI.Parts;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The weekly brief's Sprint block, as figures and sentences: a sprint read for one
/// week, "done" as of that week's end, against an even pace over its working days.
/// </summary>
public class SprintWeekBriefTests
{
    /// <summary>Wednesday of ISO week 42, 2026: the brief reports week 41, Mon 5 – Sun 11 Oct.</summary>
    private static readonly DateTimeOffset InWeek42 = new(2026, 10, 14, 9, 0, 0, TimeSpan.Zero);

    /// <summary>Wednesday of week 43: the brief reports week 42, the sprint's last.</summary>
    private static readonly DateTimeOffset InWeek43 = new(2026, 10, 21, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void The_first_week_reads_done_as_of_its_end_against_the_even_pace()
    {
        var brief = SprintWeekBrief.Of(SprintFixtures.Sprint42(), BriefWeek.Of(InWeek42, 0));

        Assert.Equal((1, 2), (brief.Week, brief.Weeks));
        Assert.Equal((2, 4), (brief.GoalDone, brief.GoalTotal));
        Assert.Equal((3, 6), (brief.Done, brief.Total));
        Assert.Equal(3, brief.EvenPace);
        Assert.Equal((1, 0), (brief.Added, brief.Removed));
        Assert.Equal(["Openspec"], brief.Blocked);
        Assert.Equal(3, brief.DoneThisWeek);
        Assert.Equal(3, brief.Merged);
        Assert.Equal(3, brief.Open);

        Assert.Equal(
            "Fincent team · Fincent, devbook · week 1 of 2 · 05 Oct – 18 Oct",
            WeeklyBriefSentences.SprintMeta(brief));
        Assert.Equal(
            "2 of 4 goal tasks and 3 of 6 items done after the first week, against an even pace of 3. "
            + "1 item was added after the start and none were removed. Openspec is the one item blocked.",
            WeeklyBriefSentences.SprintSummary(brief));
        Assert.Equal("Into week 2", WeeklyBriefSentences.SprintIntoHeading(brief));
        Assert.Equal(
            "3 open going into the second week, Openspec blocked among them. Local dev platform #4572 waits on review.",
            WeeklyBriefSentences.SprintInto(brief));
    }

    [Fact]
    public void Shipped_groups_the_week_s_done_members_by_plan_with_their_merged_pull_requests()
    {
        var brief = SprintWeekBrief.Of(SprintFixtures.Sprint42(), BriefWeek.Of(InWeek42, 0));

        Assert.Equal(["feature-flags", "rekeningen", "specs"], brief.Shipped.Select(plan => plan.PlanTag));

        var flags = Assert.Single(brief.Shipped[0].Items);
        Assert.Equal("Feature flags", flags.Title);
        Assert.Equal(["4560", "4558"], flags.Merged.Select(pull => pull.Number));

        Assert.Equal(["Opdrachtbrief"], brief.Shipped[1].Items.Select(item => item.Title));
        Assert.Empty(Assert.Single(brief.Shipped[2].Items).Merged);
    }

    [Fact]
    public void The_last_week_carries_into_next_week_as_the_sprint_ends()
    {
        var brief = SprintWeekBrief.Of(SprintFixtures.Sprint42(), BriefWeek.Of(InWeek43, 0));

        Assert.True(brief.IsLastWeek);
        Assert.Equal((2, 2), (brief.Week, brief.Weeks));
        Assert.Equal((3, 4), (brief.GoalDone, brief.GoalTotal));
        Assert.Equal(6, brief.EvenPace);
        Assert.Equal(1, brief.DoneThisWeek);
        Assert.Equal(0, brief.Merged);
        Assert.Equal("Into next week", WeeklyBriefSentences.SprintIntoHeading(brief));
        Assert.Equal(
            "2 open as the sprint ends, Openspec blocked among them. Local dev platform #4572 waits on review.",
            WeeklyBriefSentences.SprintInto(brief));
        Assert.StartsWith("3 of 4 goal tasks and 4 of 6 items done after the second week, against an even pace of 6.",
            WeeklyBriefSentences.SprintSummary(brief), StringComparison.Ordinal);
    }

    [Fact]
    public void A_sprint_with_no_goal_task_reports_its_items_alone()
    {
        var sprint = SprintFixtures.Sprint42() with
        {
            Members = [.. SprintFixtures.Sprint42().Members.Select(member => member with { IsGoal = false, IsBlocked = false })]
        };

        var brief = SprintWeekBrief.Of(sprint, BriefWeek.Of(InWeek42, 0));

        Assert.StartsWith("3 of 6 items done after the first week", WeeklyBriefSentences.SprintSummary(brief), StringComparison.Ordinal);
        Assert.EndsWith("Nothing is blocked.", WeeklyBriefSentences.SprintSummary(brief), StringComparison.Ordinal);
    }

    [Fact]
    public void Removed_members_count_as_removed_and_leave_the_totals()
    {
        var sprint = SprintFixtures.Sprint42() with
        {
            Members = [.. SprintFixtures.Sprint42().Members, SprintFixtures.Member("Dropped", removed: true)]
        };

        var brief = SprintWeekBrief.Of(sprint, BriefWeek.Of(InWeek42, 0));

        Assert.Equal((6, 1), (brief.Total, brief.Removed));
        Assert.Contains("and 1 was removed.", WeeklyBriefSentences.SprintSummary(brief), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0, "Nothing is blocked.")]
    [InlineData(1, "A is the one item blocked.")]
    [InlineData(2, "A and B are blocked.")]
    [InlineData(3, "A, B and C are blocked.")]
    [InlineData(4, "4 items are blocked.")]
    public void Blocked_names_up_to_three(int count, string expected) =>
        Assert.Equal(expected, WeeklyBriefSentences.SprintBlocked([.. new[] { "A", "B", "C", "D" }.Take(count)]));

    [Fact]
    public void For_picks_each_series_latest_overlapping_sprint_in_the_repositories_in_focus()
    {
        var week = BriefWeek.Of(InWeek42, 0);
        var current = SprintFixtures.Sprint42();
        var earlier = current with { Number = 41, Name = "Sprint 41", Start = new DateOnly(2026, 9, 21), End = new DateOnly(2026, 10, 4) };
        var other = current with { SeriesId = Guid.NewGuid(), SeriesName = "Another team", Repositories = ["tools"] };

        Assert.Equal(
            ["Another team", "Fincent team"],
            SprintWeekBrief.For([earlier, current, other], week, RepositoryFocus.All).Select(brief => brief.Sprint.SeriesName));

        var focused = Assert.Single(SprintWeekBrief.For([earlier, current, other], week, RepositoryFocus.Of("devbook")));
        Assert.Equal(42, focused.Sprint.Number);

        Assert.Empty(SprintWeekBrief.For([earlier], week, RepositoryFocus.All));
    }

    [Theory]
    [InlineData("innovadis-dev/Fincent", "4530", "https://github.com/innovadis-dev/Fincent/pull/4530")]
    [InlineData("repo-id", "4530", null)]
    [InlineData("innovadis-dev/Fincent", "abc", null)]
    public void A_pull_request_links_to_github_only_from_owner_and_name(string repository, string number, string? expected) =>
        Assert.Equal(expected, SprintWeekBrief.Url(new SprintBriefPullRequest(repository, number, SprintBriefPullRequestState.Merged, null)));
}

/// <summary>Sprint 42 of the Fincent team, Mon 5 – Sun 18 Oct 2026, after the Weekly
/// artboard.</summary>
internal static class SprintFixtures
{
    public static readonly Guid SeriesId = new("6b7f4a9e-0d8e-4c4e-9a52-6c0f3a1d2e42");

    public static SprintBriefSprint Sprint42() =>
        new(SeriesId, "Fincent team", ["Fincent", "devbook"], 42, "Sprint 42",
            new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 18),
        [
            Member("Opdrachtbrief", goal: true, plan: "rekeningen", doneOn: new DateOnly(2026, 10, 6),
                pulls: [Merged("4530", new DateOnly(2026, 10, 6))]),
            Member("Feature flags", goal: true, plan: "feature-flags", doneOn: new DateOnly(2026, 10, 8),
                pulls: [Merged("4560", new DateOnly(2026, 10, 8)), Merged("4558", new DateOnly(2026, 10, 8))]),
            Member("Rekeningen", plan: "specs", doneOn: new DateOnly(2026, 10, 9)),
            Member("Openspec", goal: true, blocked: true, added: true),
            Member("Local dev platform", pulls: [new SprintBriefPullRequest("innovadis-dev/Fincent", "4572", SprintBriefPullRequestState.Open, null)]),
            Member("Later", goal: true, plan: "rekeningen", doneOn: new DateOnly(2026, 10, 13))
        ]);

    public static SprintBriefMember Member(
        string title,
        bool goal = false,
        string? plan = null,
        DateOnly? doneOn = null,
        bool blocked = false,
        bool added = false,
        bool removed = false,
        IReadOnlyList<SprintBriefPullRequest>? pulls = null) =>
        new(Guid.NewGuid(), title, plan, goal, blocked, doneOn is not null, doneOn, added, removed, pulls ?? []);

    private static SprintBriefPullRequest Merged(string number, DateOnly on) =>
        new("innovadis-dev/Fincent", number, SprintBriefPullRequestState.Merged, on);
}
