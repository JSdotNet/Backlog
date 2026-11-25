using Backlog.Desktop.UI.Tasks;
using Backlog.Modules.Tasks.Abstractions.Services;

using Bunit;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// A whole plan on My Day (local ADR 0062 §8; <c>.devbook/domain/tasks/features.md#my-day</c>):
/// the plan's My Day date is read by exactly the task rule under the Clear My Day overnight
/// switch, the sun on the plan's heading and its waves view puts it on today's My Day and
/// takes it off, the rail's My Day item counts plans and tasks, and the My Day list shows
/// <b>Plans today</b> above the tasks with only the entries the person holds.
/// </summary>
[Collection(WorkspaceSettingsCollection.Name)]
public sealed class PlanMyDayTests
{
    private const string Backlog = "JSdotNet/Backlog";

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.Now);

    /// <summary>A plan whose tags and My Day dates the test holds, storing a put the way
    /// the roadmap does: a plan tag it did not carry gets its item.</summary>
    private sealed class PlanMyDays(params string[] tags) : IRoadmapTagSource
    {
        public List<string> Tags { get; } = [.. tags];

        public Dictionary<string, DateOnly> Stamps { get; } = new(StringComparer.OrdinalIgnoreCase);

        public List<(string Tag, DateOnly? On)> Puts { get; } = [];

        public event Action? Changed;

        public Task<IReadOnlyList<string>> TagsInUseAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<string>>([.. Tags]);

        public Task<IReadOnlyDictionary<string, DateOnly>> PlansOnMyDayAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<string, DateOnly>>(new Dictionary<string, DateOnly>(Stamps, StringComparer.OrdinalIgnoreCase));

        public Task<bool> SetPlanMyDayAsync(string planTag, DateOnly? on, CancellationToken cancellationToken = default)
        {
            Puts.Add((planTag, on));
            if (on is { } day) Stamps[planTag] = day;
            else Stamps.Remove(planTag);
            if (on is not null && !Tags.Contains("+" + planTag, StringComparer.OrdinalIgnoreCase)) Tags.Add("+" + planTag);
            return Task.FromResult(true);
        }

        public void Raise() => Changed?.Invoke();
    }

    private static async Task<(TasksPaneHost Host, PlanMyDays Plan)> HostAsync(params string[] entries)
    {
        var plan = new PlanMyDays("+alpha", "+beta");
        var host = await TasksPaneHost.CreateAsync(plan, [Backlog]);
        foreach (var entry in entries) await host.WriteEntryAsync(entry);
        await host.State.SelectAsync(null);
        return (host, plan);
    }

    private static IRenderedComponent<TasksPane> RenderPlans(TasksPaneHost host) =>
        host.Context.Render<TasksPane>(parameters => parameters.Add(pane => pane.ListGrouping, TaskListGrouping.Plans));

    // ---- The stamp rule -------------------------------------------------------------

    [Fact]
    public async Task A_plan_stamped_today_is_in_My_Day_and_one_on_no_day_or_a_later_day_is_not()
    {
        var (host, plan) = await HostAsync("# Wire it +alpha\n`prompt` `!ready`\n");
        using var _host = host;

        Assert.False(host.State.IsPlanInMyDay("+alpha", Today));

        plan.Stamps["alpha"] = Today;
        plan.Stamps["beta"] = Today.AddDays(1);
        plan.Raise();
        await TasksTestHost.UntilAsync(host.State, () => host.State.PlanMyDayOn("alpha") is not null);

        Assert.True(host.State.IsPlanInMyDay("+alpha", Today));
        Assert.True(host.State.IsPlanInMyDay("ALPHA", Today));
        Assert.False(host.State.IsPlanInMyDay("+beta", Today));
        Assert.Equal(["+alpha"], host.State.PlansInMyDay(Today));
    }

    [Fact]
    public async Task With_Clear_My_Day_overnight_on_a_plan_picked_yesterday_has_left_My_Day()
    {
        var (host, plan) = await HostAsync("# Wire it +alpha\n`prompt` `!ready`\n");
        using var _host = host;
        plan.Stamps["alpha"] = Today.AddDays(-1);
        plan.Raise();
        await TasksTestHost.UntilAsync(host.State, () => host.State.PlanMyDayOn("alpha") is not null);

        Assert.True(host.State.ClearsMyDayOvernight);
        Assert.False(host.State.IsPlanInMyDay("+alpha", Today));
        Assert.True(host.State.IsPlanInMyDay("+alpha", Today.AddDays(-1)));
    }

    [Fact]
    public async Task With_Clear_My_Day_overnight_off_an_unfinished_plan_carries_over_and_a_finished_one_does_not()
    {
        var (host, plan) = await HostAsync(
            "# Wire it +alpha\n`prompt` `!ready`\n",
            $"# Ship it +beta\n`task` `!done` `completed:{Today.AddDays(-1):yyyy-MM-dd}`\n");
        using var _host = host;
        Assert.Null(host.MyDay.SetClearsOvernight(false));
        plan.Stamps["alpha"] = Today.AddDays(-3);
        plan.Stamps["beta"] = Today.AddDays(-3);
        plan.Raise();
        await TasksTestHost.UntilAsync(host.State, () => host.State.PlanMyDayOn("beta") is not null);

        Assert.False(host.State.ClearsMyDayOvernight);
        Assert.True(host.State.IsPlanInMyDay("+alpha", Today));
        Assert.False(host.State.IsPlanInMyDay("+beta", Today));
    }

    [Fact]
    public async Task With_Clear_My_Day_overnight_off_a_plan_finished_today_stays_until_the_day_ends()
    {
        var (host, plan) = await HostAsync($"# Ship it +beta\n`task` `!done` `completed:{Today:yyyy-MM-dd}`\n");
        using var _host = host;
        Assert.Null(host.MyDay.SetClearsOvernight(false));
        plan.Stamps["beta"] = Today.AddDays(-2);
        plan.Raise();
        await TasksTestHost.UntilAsync(host.State, () => host.State.PlanMyDayOn("beta") is not null);

        Assert.True(host.State.IsPlanInMyDay("+beta", Today));
    }

    // ---- Putting a plan on My Day ---------------------------------------------------

    [Fact]
    public async Task The_sun_puts_the_plan_on_today_and_pressing_it_again_takes_it_off()
    {
        var (host, plan) = await HostAsync("# Wire it +alpha\n`prompt` `!ready`\n");
        using var _host = host;

        Assert.True(await host.State.TogglePlanMyDayAsync("+alpha", Today));
        Assert.Equal(("alpha", (DateOnly?)Today), plan.Puts[^1]);
        Assert.True(host.State.IsPlanInMyDay("+alpha", Today));

        Assert.True(await host.State.TogglePlanMyDayAsync("+alpha", Today));
        Assert.Equal(("alpha", (DateOnly?)null), plan.Puts[^1]);
        Assert.False(host.State.IsPlanInMyDay("+alpha", Today));
    }

    [Fact]
    public async Task Putting_a_plan_tag_the_roadmap_does_not_carry_on_My_Day_makes_it_a_planned_tag()
    {
        var (host, plan) = await HostAsync("# Book the venue +lets-bbq\n`task` `!ready`\n");
        using var _host = host;
        var row = host.State.Rows.Single(candidate => candidate.PreviewTitle.StartsWith("Book the venue", StringComparison.Ordinal));
        Assert.Null(host.State.QueuedOnlyWithPlan(row));

        Assert.True(await host.State.SetPlanMyDayAsync("+lets-bbq", Today));

        Assert.Contains("+lets-bbq", plan.Tags);
        Assert.Contains("+lets-bbq", host.State.RoadmapTags);
        Assert.Equal("+lets-bbq", host.State.QueuedOnlyWithPlan(row));
        Assert.True(host.State.IsPlanInMyDay("+lets-bbq", Today));
    }

    [Fact]
    public async Task A_plan_tag_written_another_way_is_the_plan_the_roadmap_slug_names()
    {
        var (host, plan) = await HostAsync("# Book the venue\n`task` `!ready` `+lets_bbq`\n");
        using var _host = host;

        Assert.True(await host.State.SetPlanMyDayAsync("+Lets_BBQ", Today));

        Assert.Equal(("lets-bbq", (DateOnly?)Today), plan.Puts[^1]);
        Assert.True(host.State.IsPlanInMyDay("+lets_bbq", Today));
        Assert.True(host.State.IsPlanInMyDay("+lets-bbq", Today));

        plan.Raise();
        await TasksTestHost.UntilAsync(host.State, () => host.State.RoadmapTags.Contains("+lets-bbq"));
        Assert.True(host.State.IsPlanInMyDay("+lets_bbq", Today));
        Assert.Equal(["+lets-bbq"], host.State.PlansInMyDay(Today));
    }

    [Fact]
    public async Task A_plan_finished_today_stays_on_My_Day_but_leaves_the_count()
    {
        var (host, _) = await HostAsync($"# Ship it\n`task` `!done` `completed:{Today:yyyy-MM-dd}` `+beta`\n");
        using var _host = host;
        await host.State.SetPlanMyDayAsync("+beta", Today);
        var pane = RenderPlans(host);

        pane.WaitForAssertion(() => Assert.Equal("0", pane.Find("[data-testid='plan-rail-myday'] [data-testid='plan-rail-count']").TextContent.Trim()));
        Assert.True(host.State.IsPlanInMyDay("+beta", Today));
        Assert.False(host.State.IsPlanOpen("+beta"));
    }

        // ---- The pane -------------------------------------------------------------------

    [Fact]
    public async Task The_expanded_plan_heading_and_its_waves_view_carry_the_sun()
    {
        var (host, plan) = await HostAsync("# Wire it +alpha\n`prompt` `!ready`\n");
        using var _host = host;
        var pane = RenderPlans(host);
        pane.WaitForAssertion(() => Assert.NotEmpty(pane.FindAll("[data-testid='plan-rail-plan'][data-plan='alpha']")));
        pane.Find("[data-testid='plan-rail-plan'][data-plan='alpha']").Click();

        var sun = pane.WaitForElement("[data-testid='plan-expanded-myday']");
        Assert.Equal("false", sun.GetAttribute("aria-pressed"));
        Assert.Equal("+alpha in My Day", sun.GetAttribute("aria-label"));
        Assert.Equal("Put +alpha on My Day", sun.GetAttribute("title"));

        sun.Click();

        pane.WaitForAssertion(() =>
        {
            Assert.Equal(("alpha", (DateOnly?)Today), plan.Puts.Single());
            Assert.Equal("true", pane.Find("[data-testid='plan-expanded-myday']").GetAttribute("aria-pressed"));
        });

        pane.Find("[data-testid='plan-expanded-open']").Click();
        var focusSun = pane.WaitForElement("[data-testid='plan-focus-myday']");
        Assert.Equal("true", focusSun.GetAttribute("aria-pressed"));
        focusSun.Click();
        pane.WaitForAssertion(() => Assert.Equal(("alpha", (DateOnly?)null), plan.Puts[^1]));
    }

    [Fact]
    public async Task The_rails_My_Day_item_counts_plans_and_tasks()
    {
        var (host, plan) = await HostAsync(
            $"# Book the room\n`task` `!ready` `myday:{Today:yyyy-MM-dd}`\n",
            "# Wire it +alpha\n`prompt` `!ready`\n");
        using var _host = host;
        var pane = RenderPlans(host);
        pane.WaitForAssertion(() => Assert.Equal("1", pane.Find("[data-testid='plan-rail-myday'] [data-testid='plan-rail-count']").TextContent.Trim()));

        await host.State.SetPlanMyDayAsync("+alpha", Today);

        pane.WaitForAssertion(() => Assert.Equal("2", pane.Find("[data-testid='plan-rail-myday'] [data-testid='plan-rail-count']").TextContent.Trim()));
    }

    [Fact]
    public async Task My_Day_lists_Plans_today_above_the_tasks_with_only_the_entries_the_person_holds()
    {
        var (host, _) = await HostAsync(
            $"# Book the room\n`task` `!ready` `myday:{Today:yyyy-MM-dd}`\n",
            "# Wire it +alpha\n`prompt` `!ready`\n",
            "# Sign in on a revoked device\n`test` `!ready` `+alpha`\n",
            "# Rotate the keys\n`prompt` `!ready` `blocked:2026-11-20` `+alpha`\n",
            "# Open the account\n`task` `!ready` `+alpha`\n",
            "# Done already +alpha\n`task` `!done` `completed:2026-11-20`\n");
        using var _host = host;
        await host.State.SetPlanMyDayAsync("+alpha", Today);
        var pane = RenderPlans(host);

        pane.Find("[data-testid='plan-rail-myday']").Click();

        pane.WaitForAssertion(() =>
        {
            var section = pane.Find("[data-testid='plans-today']");
            var plan = section.QuerySelector("[data-testid='plans-today-plan'][data-plan='alpha']")!;
            Assert.NotNull(plan);
            Assert.Equal("1 of 5", plan.QuerySelector("[data-testid='plans-today-progress']")!.TextContent.Trim());
            Assert.Equal("3 ready", plan.QuerySelector("[data-testid='plans-today-state']")!.TextContent.Trim());
            Assert.Equal("true", plan.QuerySelector("[data-testid='plans-today-myday']")!.GetAttribute("aria-pressed"));

            var entries = plan.QuerySelectorAll("[data-testid='plans-today-entry']");
            Assert.Equal(
                ["Sign in on a revoked device", "Rotate the keys", "Open the account"],
                entries.Select(entry => entry.QuerySelector(".plan-sections__row-title")!.TextContent.Trim()));
            Assert.Equal(
                ["Test, left for you", "Held for you", "Task, yours to do"],
                entries.Select(entry => entry.QuerySelector("[data-testid='plans-today-entry-reason']")!.TextContent.Trim()));

            // Above the tasks, and no suggestions.
            Assert.Null(section.QuerySelector("[data-testid='tasks-today-heading']"));
            Assert.Equal("plans-today", pane.Find("[data-testid='tasks-today-heading']").PreviousElementSibling?.GetAttribute("data-testid"));
            Assert.Contains("Book the room", pane.Markup, StringComparison.Ordinal);
            Assert.DoesNotContain("Suggested", pane.Markup, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task My_Day_shows_no_Plans_today_while_no_plan_is_on_it()
    {
        var (host, _) = await HostAsync($"# Book the room\n`task` `!ready` `myday:{Today:yyyy-MM-dd}`\n", "# Wire it +alpha\n`prompt` `!ready`\n");
        using var _host = host;
        var pane = RenderPlans(host);

        pane.Find("[data-testid='plan-rail-myday']").Click();

        pane.WaitForAssertion(() =>
        {
            Assert.Contains("Book the room", pane.Markup, StringComparison.Ordinal);
            Assert.Empty(pane.FindAll("[data-testid='plans-today']"));
        });
    }

    [Theory]
    [InlineData(PlanRunState.Running, 0, "Running · unattended")]
    [InlineData(PlanRunState.Queued, 2, "Queued 2nd")]
    [InlineData(PlanRunState.Ready, 0, "3 ready")]
    [InlineData(PlanRunState.None, 0, "Not queued")]
    public void A_plan_today_words_its_run_state(PlanRunState state, int place, string expected) =>
        Assert.Equal(expected, new PlanToday(new PlanRailPlan("+alpha", "alpha", 1, 4, state, place, 3), []).StateText);

    [Fact]
    public void An_entry_awaiting_validation_on_an_open_pull_request_is_the_persons_and_one_in_a_session_is_not()
    {
        var row = new EntryRow { Id = Guid.NewGuid(), RawText = "# Wire it\n`prompt` `!in-progress` `+alpha`\n" };
        var inSession = new PlanEntryState(PlanEntryQueueState.InPlanSession);

        Assert.False(TasksDesktopState.IsForYou(row, inSession));
        Assert.True(TasksDesktopState.IsForYou(row, new PlanEntryState(PlanEntryQueueState.HeldForYou)));
        Assert.True(TasksDesktopState.IsForYou(row, new PlanEntryState(PlanEntryQueueState.LeftForYou)));
        Assert.False(TasksDesktopState.IsForYou(row, new PlanEntryState(PlanEntryQueueState.Done)));

        var pr = new EntryPullRequestLink(Backlog, 2014);
        row.PullRequestLinks = [pr];
        row.PullRequestStatuses = new Dictionary<EntryPullRequestLink, LinkedPullRequest>
        {
            [pr] = new(LinkedPullRequestState.Draft) { BaseRefName = "plan/alpha" }
        };

        Assert.True(TasksDesktopState.IsForYou(row, inSession));
        Assert.Equal("Awaiting validation", PlanToday.ReasonFor(row, inSession));
    }
}
