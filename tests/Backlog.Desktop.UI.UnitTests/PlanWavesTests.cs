using Backlog.Desktop.UI.Tasks;
using Backlog.Modules.Tasks.Abstractions.Services;

using Bunit;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// One plan's waves view (local ADR 0062 §7;
/// <c>.devbook/domain/tasks/requirements.md#requirement-a-plans-waves-follow-its-dependencies</c>):
/// wave n holds the entries whose longest <c>after:</c> chain inside the plan is n − 1
/// long, the current wave is the first not all done, and the view opened from the
/// expanded plan's heading carries the Waves / List toggle and the run line.
/// </summary>
[Collection(WorkspaceSettingsCollection.Name)]
public sealed class PlanWavesTests
{
    private const string Backlog = "JSdotNet/Backlog";

    // --- The wave assignment, pure ----------------------------------------------

    private static PlanSectionEntry Entry(string title, PlanSection section = PlanSection.Waiting, PlanEntryQueueState state = PlanEntryQueueState.Waiting, string? meta = null) =>
        new(
            new EntryRow { Id = Guid.NewGuid(), RawText = $"# {title}\n{meta ?? "`prompt` `+p`"}\n" },
            new PlanEntryState(state),
            section,
            PlanSections.NumberOf(title));

    private static Func<EntryRow, IReadOnlyList<string>> Deps(params (PlanSectionEntry Entry, PlanSectionEntry[] After)[] links)
    {
        var map = links.ToDictionary(link => link.Entry.Row, link => (IReadOnlyList<string>)[.. link.After.Select(after => after.Row.TaskId)]);

        return row => map.GetValueOrDefault(row) ?? [];
    }

    [Fact]
    public void A_chain_puts_each_entry_one_wave_after_the_one_it_waits_on()
    {
        // The requirement's scenario: #9 waits on #1, #14 on #9, #1 on nothing.
        var one = Entry("1 - Token model");
        var nine = Entry("9 - Device claim");
        var fourteen = Entry("14 - Device-bound tokens");

        var waves = PlanWaves.WaveOf([fourteen, nine, one], Deps((nine, [one]), (fourteen, [nine])));

        Assert.Equal(1, waves[one.Row.TaskId]);
        Assert.Equal(2, waves[nine.Row.TaskId]);
        Assert.Equal(3, waves[fourteen.Row.TaskId]);
    }

    [Fact]
    public void An_entry_depending_across_waves_sits_after_its_latest_dependency()
    {
        // #20 waits on #1 (wave 1) and on #14 (wave 3): its longest chain wins, wave 4,
        // not wave 2 beside #9.
        var one = Entry("1 - A");
        var nine = Entry("9 - B");
        var fourteen = Entry("14 - C");
        var twenty = Entry("20 - D");

        var waves = PlanWaves.WaveOf(
            [one, nine, fourteen, twenty],
            Deps((nine, [one]), (fourteen, [nine]), (twenty, [one, fourteen])));

        Assert.Equal(4, waves[twenty.Row.TaskId]);
    }

    [Fact]
    public void Entries_waiting_on_nothing_in_the_plan_are_all_wave_one()
    {
        var a = Entry("1 - A");
        var b = Entry("2 - B");
        var outside = Guid.NewGuid().ToString("D");

        var waves = PlanWaves.WaveOf([a, b], row => row == b.Row ? [outside] : []);

        Assert.Equal(1, waves[a.Row.TaskId]);
        Assert.Equal(1, waves[b.Row.TaskId]);
    }

    [Fact]
    public void A_cycle_still_places_every_entry_once()
    {
        var a = Entry("1 - A");
        var b = Entry("2 - B");
        var c = Entry("3 - C");

        var waves = PlanWaves.WaveOf([a, b, c], Deps((a, [c]), (b, [a]), (c, [b])));

        Assert.Equal(3, waves.Count);
        Assert.All(waves.Values, wave => Assert.InRange(wave, 1, 3));
    }

    [Fact]
    public void The_waves_keep_the_store_order_and_the_first_not_all_done_is_current()
    {
        var one = Entry("1 - A", PlanSection.Done, PlanEntryQueueState.Done);
        var two = Entry("2 - B", PlanSection.Done, PlanEntryQueueState.Done);
        var three = Entry("3 - C", PlanSection.Now, PlanEntryQueueState.InPlanSession);
        var four = Entry("4 - D", PlanSection.Next, PlanEntryQueueState.QueuedWithPlan);
        var five = Entry("5 - E");

        var waves = PlanWaves.Build(
            [one, two, three, four, five],
            Deps((three, [one]), (four, [two]), (five, [three, four])),
            "plan/p");

        Assert.Equal([1, 2, 3], waves.Select(wave => wave.Number));
        Assert.Equal(["#1", "#2"], waves[0].Entries.Select(card => card.Number));
        Assert.Equal(["#3", "#4"], waves[1].Entries.Select(card => card.Number));
        Assert.Equal([false, true, false], waves.Select(wave => wave.Current));
        Assert.Equal(["landed", "now — running and ready", "1 waiting"], waves.Select(wave => wave.Label));
    }

    [Fact]
    public void A_card_says_its_number_type_and_detail_and_its_state_as_text()
    {
        var done = Entry("1 - Token model", PlanSection.Done, PlanEntryQueueState.Done, "`task` `+p`");
        var running = Entry("9 - Device claim", PlanSection.Now, PlanEntryQueueState.InPlanSession);
        var ready = Entry("17 - Revoke a device", PlanSection.Next, PlanEntryQueueState.QueuedWithPlan, "`prompt` `+p` `effort:5`");
        var waiting = Entry("20 - Reach every device");

        var cards = PlanWaves.Build(
                [done, running, ready, waiting],
                Deps((ready, [done]), (waiting, [running, ready])),
                "plan/p")
            .SelectMany(wave => wave.Entries)
            .ToDictionary(card => card.Number);

        Assert.Equal("Token model", cards["#1"].Title);
        Assert.Equal("#1 · task · by hand", cards["#1"].MetaLine);
        Assert.Equal(("Done", "done"), (cards["#1"].StateText, cards["#1"].StateSlug));
        Assert.Equal("#9 · prompt · in a session", cards["#9"].MetaLine);
        Assert.Equal("In session", cards["#9"].StateText);
        Assert.Equal("#17 · prompt · 5 pt · after #1", cards["#17"].MetaLine);
        Assert.Equal(("Queued with plan", "ready"), (cards["#17"].StateText, cards["#17"].StateSlug));
        Assert.Equal("#20 · prompt · after #9, #17", cards["#20"].MetaLine);
        Assert.Equal(string.Empty, cards["#20"].StateText);
        Assert.True(cards["#20"].Waiting);
        Assert.False(cards["#17"].Waiting);
    }

    [Fact]
    public void A_done_card_names_the_pull_request_merged_into_the_plan_branch()
    {
        var done = Entry("13 - Key rotation", PlanSection.Done, PlanEntryQueueState.Landed);
        var link = new EntryPullRequestLink(Backlog, 1841);
        done.Row.PullRequestLinks = [link];
        done.Row.PullRequestStatuses = new Dictionary<EntryPullRequestLink, LinkedPullRequest>
        {
            [link] = new(LinkedPullRequestState.Merged) { BaseRefName = "plan/p" }
        };

        var card = PlanWaves.Build([done], _ => [], "plan/p")[0].Entries[0];

        Assert.Equal("#13 · prompt · PR #1841", card.MetaLine);
        Assert.Equal("Landed", card.StateText);
    }

    [Fact]
    public void An_unnumbered_entry_is_named_by_its_place_in_the_plan()
    {
        var cards = PlanWaves.Build([Entry("First"), Entry("Second")], _ => [], "plan/p")[0].Entries;

        Assert.Equal(["#1", "#2"], cards.Select(card => card.Number));
        Assert.Equal(["First", "Second"], cards.Select(card => card.Title));
    }

    // --- The view, rendered -------------------------------------------------------

    private static async Task<TasksPaneHost> HostAsync()
    {
        var host = await TasksPaneHost.CreateAsync(Backlog);

        var one = await host.WriteEntryAsync("# 1 - Token model +zero-trust-auth\n`prompt` `!done` `completed:2026-09-22` `effort:3`\n");
        var nine = await host.WriteEntryAsync($"# 9 - Device claim +zero-trust-auth\n`prompt` `!ready` `after:{one.Id}` `effort:5`\n");
        await host.WriteEntryAsync($"# 14 - Device-bound tokens +zero-trust-auth\n`prompt` `!ready` `after:{nine.Id}` `effort:8`\n");
        await host.WriteEntryAsync("# Other plan step +beta\n`prompt` `!ready`\n");
        await host.State.SelectAsync(null);

        host.Context.Services.AddSingleton<IPlanTagSessionRequests>(new TasksPlanSessionRequestTests.FakePlanTagSessionRequests());
        host.Context.Services.AddSingleton<TimeProvider>(new FakeTimeProvider(DateTimeOffset.UtcNow));

        return host;
    }

    private static string Text(AngleSharp.Dom.IElement element) => element.TextContent.Trim();

    private static IRenderedComponent<TasksPane> OpenPlan(TasksPaneHost host)
    {
        var pane = host.Context.Render<TasksPane>(parameters => parameters.Add(p => p.ListGrouping, TaskListGrouping.Plans));
        pane.Find("[data-testid='plan-rail-plan'][data-plan='zero-trust-auth']").Click();
        pane.WaitForAssertion(() => pane.Find("[data-testid='plan-expanded-open']"));
        pane.Find("[data-testid='plan-expanded-open']").Click();
        pane.WaitForAssertion(() => pane.Find("[data-testid='plan-focus']"));

        return pane;
    }

    [Fact]
    public async Task Opening_the_heading_shows_the_plan_alone_in_waves()
    {
        using var host = await HostAsync();
        var pane = OpenPlan(host);

        Assert.Empty(pane.FindAll("[data-testid='plan-sections']"));
        Assert.Equal("zero-trust-auth", Text(pane.Find("[data-testid='plan-focus-name']")));
        Assert.Equal("3 entries · 1 done · 13 pts left", Text(pane.Find("[data-testid='plan-focus-counts']")));
        Assert.Equal("true", pane.Find("[data-testid='plan-focus-layout-option'][data-layout='Waves']").GetAttribute("aria-checked"));

        var waves = pane.FindAll("[data-testid='plan-wave']");
        Assert.Equal(["1", "2", "3"], waves.Select(wave => wave.GetAttribute("data-wave")));
        Assert.Equal(["false", "true", "false"], waves.Select(wave => wave.GetAttribute("data-current")));
        Assert.Equal(
            ["Token model +zero-trust-auth", "Device claim +zero-trust-auth", "Device-bound tokens +zero-trust-auth"],
            waves.Select(wave => Text(wave.QuerySelector("[data-testid='plan-wave-card-title']")!)));
        Assert.Contains("plan-waves__card--waiting", waves[2].QuerySelector("[data-testid='plan-wave-card']")!.ClassName);
        Assert.Equal("Lands on plan/zero-trust-auth · Unattended, asks nothing", Text(pane.Find("[data-testid='plan-focus-run-line']")));
        Assert.NotNull(pane.Find("[data-testid='plan-focus-copy-button']"));
    }

    [Fact]
    public async Task List_shows_the_plan_as_rows_and_Plans_goes_back()
    {
        using var host = await HostAsync();
        var pane = OpenPlan(host);

        pane.Find("[data-testid='plan-focus-layout-option'][data-layout='List']").Click();

        pane.WaitForAssertion(() =>
        {
            Assert.Empty(pane.FindAll("[data-testid='plan-waves']"));
            Assert.NotNull(pane.Find("[data-testid='plan-focus-list'] [data-testid='plan-section-next']"));
        });

        pane.Find("[data-testid='plan-focus-back']").Click();

        pane.WaitForAssertion(() =>
        {
            Assert.Empty(pane.FindAll("[data-testid='plan-focus']"));
            Assert.NotNull(pane.Find("[data-testid='plan-expanded']"));
        });
    }

    [Fact]
    public async Task Selecting_another_plan_leaves_the_one_plan_view()
    {
        using var host = await HostAsync();
        var pane = OpenPlan(host);

        pane.Find("[data-testid='plan-rail-plan'][data-plan='beta']").Click();

        pane.WaitForAssertion(() =>
        {
            Assert.Empty(pane.FindAll("[data-testid='plan-focus']"));
            Assert.Equal("beta", pane.Find("[data-testid='plan-expanded']").GetAttribute("data-plan"));
        });

        // Coming back to the first plan shows its sections, not the view left behind.
        pane.Find("[data-testid='plan-rail-plan'][data-plan='zero-trust-auth']").Click();

        pane.WaitForAssertion(() =>
        {
            Assert.Empty(pane.FindAll("[data-testid='plan-focus']"));
            Assert.Equal("zero-trust-auth", pane.Find("[data-testid='plan-expanded']").GetAttribute("data-plan"));
        });
    }
}
