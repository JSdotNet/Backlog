using Backlog.Desktop.UI.Tasks;
using Backlog.Modules.Tasks.Abstractions.Services;

using Bunit;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The Plans view's main column with a plan selected (local ADR 0062 §3;
/// <c>.devbook/domain/tasks/requirements.md#requirement-the-selected-plan-reads-as-now-next-and-later</c>):
/// the selected plan's entries grouped into Now, Next and Later, Later's unlock and done
/// lines, the other plans folded to a line each, and Unplanned closing the column.
/// </summary>
[Collection(WorkspaceSettingsCollection.Name)]
public sealed class PlanSectionsTests
{
    private const string Backlog = "JSdotNet/Backlog";

    private static async Task<TasksPaneHost> HostAsync()
    {
        var host = await TasksPaneHost.CreateAsync(Backlog);

        // +zta: one done, two in a session, two ready, three waiting — #20 on two
        // entries not themselves waiting, #21 on #20, #22 a draft.
        await host.WriteEntryAsync("# 13 - Seed the keys +zta\n`prompt` `!done` `completed:2026-09-22` `effort:3`\n");
        var tokens = await host.WriteEntryAsync("# 14 - Issue device-bound tokens +zta\n`prompt` `!in-progress` `effort:8`\n");
        var rotate = await host.WriteEntryAsync("# 15 - Rotate sync keys +zta\n`prompt` `!in-progress` `effort:5`\n");
        var revoke = await host.WriteEntryAsync("# 17 - Revoke a device +zta\n`prompt` `!ready` `effort:5`\n");
        await host.WriteEntryAsync("# 18 - Audit log for token use +zta\n`prompt` `!ready` `effort:3`\n");
        var signOut = await host.WriteEntryAsync($"# 20 - Sign out a revoked device +zta\n`prompt` `!ready` `after:{tokens.Id}` `after:{revoke.Id}` `effort:2`\n");
        await host.WriteEntryAsync($"# 21 - Report revoked devices +zta\n`prompt` `!ready` `after:{signOut.Id}` `effort:1`\n");
        await host.WriteEntryAsync("# 22 - Sketch the recovery flow +zta\n`prompt` `!draft`\n");
        // +beta: queued; +gamma: two ready, one waiting.
        await host.WriteEntryAsync("# Plan it +beta\n`task` `!ready`\n");
        var first = await host.WriteEntryAsync("# First +gamma\n`prompt` `!ready`\n");
        await host.WriteEntryAsync("# Second +gamma\n`prompt` `!ready`\n");
        await host.WriteEntryAsync($"# Third +gamma\n`prompt` `!ready` `after:{first.Id}`\n");
        // Unplanned: five open, one done.
        foreach (var title in new[] { "Write the runbook", "Renew the certificate", "Tidy the inbox", "Book the venue", "Call the bank" })
        {
            await host.WriteEntryAsync($"# {title}\n`task` `!ready`\n");
        }

        await host.WriteEntryAsync("# Filed it\n`task` `!done` `completed:2026-09-20`\n");

        tokens.SessionLinks = [new EntrySessionLink(Backlog, "zta-14-session")];
        rotate.SessionLinks = [new EntrySessionLink(Backlog, "zta-15-session")];
        await host.State.SelectAsync(null);

        var queue = new TasksPlanSessionRequestTests.FakePlanTagSessionRequests();
        queue.Open["beta"] = new EntrySessionRequest(Guid.NewGuid(), EntrySessionRequestState.Requested, Backlog, DateTimeOffset.UnixEpoch, QueuePosition: 0);
        host.Context.Services.AddSingleton<IPlanTagSessionRequests>(queue);
        host.Context.Services.AddSingleton<TimeProvider>(new FakeTimeProvider(DateTimeOffset.UtcNow));

        return host;
    }

    private static IRenderedComponent<TasksPane> RenderPlans(TasksPaneHost host, Action<string>? openedSession = null) =>
        host.Context.Render<TasksPane>(parameters => parameters
            .Add(pane => pane.ListGrouping, TaskListGrouping.Plans)
            .Add(pane => pane.OnOpenSession, id => openedSession?.Invoke(id)));

    private static string Text(AngleSharp.Dom.IElement element) => element.TextContent.Trim();

    private static IRenderedComponent<TasksPane> SelectZta(IRenderedComponent<TasksPane> pane)
    {
        pane.Find("[data-testid='plan-rail-plan'][data-plan='zta']").Click();
        pane.WaitForAssertion(() => pane.Find("[data-testid='plan-expanded']"));

        return pane;
    }

    private static IReadOnlyList<string> Titles(IRenderedComponent<TasksPane> pane, string section) =>
        [.. pane.FindAll($"[data-testid='{section}'] [data-testid='plan-section-row'] .plan-sections__row-title").Select(Text)];

    [Fact]
    public async Task With_no_plan_selected_the_list_stays_one_list()
    {
        using var host = await HostAsync();
        var pane = RenderPlans(host);

        Assert.Empty(pane.FindAll("[data-testid='plan-sections']"));
        Assert.NotNull(pane.Find("[data-testid='entry-list']"));
    }

    [Fact]
    public async Task The_selected_plan_groups_its_entries_into_Now_Next_and_Later()
    {
        using var host = await HostAsync();
        var pane = SelectZta(RenderPlans(host));

        Assert.Empty(pane.FindAll("[data-testid='entry-list']"));
        Assert.Equal(["14 - Issue device-bound tokens +zta", "15 - Rotate sync keys +zta"], Titles(pane, "plan-section-now"));
        Assert.Equal(["17 - Revoke a device +zta", "18 - Audit log for token use +zta"], Titles(pane, "plan-section-next"));
        Assert.Equal(["prompt · 5 pt", "prompt · 3 pt"], pane.FindAll("[data-testid='plan-section-next'] [data-testid='plan-section-row-meta']").Select(Text));
        Assert.Equal(["Ready", "Ready"], pane.FindAll("[data-testid='plan-section-next'] [data-testid='plan-section-row-state']").Select(Text));

        // Later is two lines, its lists shut.
        var later = pane.Find("[data-testid='plan-section-later']");
        Assert.Empty(later.QuerySelectorAll("[data-testid='plan-section-row']"));
        Assert.Equal("3 waiting — next to unlock: #20 when #14 and #17 land", Text(pane.Find("[data-testid='plan-later-waiting']")));
        Assert.Equal("1 done", Text(pane.Find("[data-testid='plan-later-done']")));
    }

    [Fact]
    public async Task The_heading_carries_the_name_progress_points_left_and_branch()
    {
        using var host = await HostAsync();
        var pane = SelectZta(RenderPlans(host));

        Assert.Equal("zta", Text(pane.Find("[data-testid='plan-expanded-name']")));
        Assert.Equal("1 of 8 · 24 pts left", Text(pane.Find("[data-testid='plan-expanded-progress']")));
        Assert.Equal("plan/zta", Text(pane.Find("[data-testid='plan-expanded-branch']")));
        Assert.Empty(pane.FindAll("[data-testid='plan-expanded-run']"));
        Assert.NotNull(pane.Find("[data-testid='plan-expanded-copy-button']"));

        var bar = pane.Find("[data-testid='plan-expanded-bar']");
        Assert.Contains("width: 13%", bar.QuerySelector(".plan-sections__bar-done")!.GetAttribute("style"));
        Assert.Contains("width: 25%", bar.QuerySelector(".plan-sections__bar-now")!.GetAttribute("style"));
        Assert.Contains("width: 25%", bar.QuerySelector(".plan-sections__bar-next")!.GetAttribute("style"));
    }

    [Fact]
    public async Task A_Now_row_opens_its_session()
    {
        using var host = await HostAsync();
        var opened = new List<string>();
        var pane = SelectZta(RenderPlans(host, opened.Add));

        pane.FindAll("[data-testid='plan-section-now'] [data-testid='plan-section-row-session']")[0].Click();

        Assert.Equal(["zta-14-session"], opened);
    }

    [Fact]
    public async Task Each_Later_line_opens_its_list()
    {
        using var host = await HostAsync();
        var pane = SelectZta(RenderPlans(host));

        pane.Find("[data-testid='plan-later-waiting']").Click();

        pane.WaitForAssertion(() => Assert.Equal(
            ["20 - Sign out a revoked device +zta", "21 - Report revoked devices +zta", "22 - Sketch the recovery flow +zta"],
            Titles(pane, "plan-section-later")));
        Assert.Equal("true", pane.Find("[data-testid='plan-later-waiting']").GetAttribute("aria-expanded"));

        pane.Find("[data-testid='plan-later-done']").Click();

        pane.WaitForAssertion(() => Assert.Equal(["13 - Seed the keys +zta"], Titles(pane, "plan-section-later")));
    }

    [Fact]
    public async Task The_other_plans_fold_to_a_line_each_with_their_note()
    {
        using var host = await HostAsync();
        var pane = SelectZta(RenderPlans(host));

        var folded = pane.FindAll("[data-testid='plan-folded']");
        Assert.Equal(["beta", "gamma"], folded.Select(row => row.GetAttribute("data-plan")));
        Assert.Equal(["0 of 1", "0 of 3"], pane.FindAll("[data-testid='plan-folded-count']").Select(Text));
        Assert.Equal(["Queued · 1st up next", "2 ready · 1 waiting"], pane.FindAll("[data-testid='plan-folded-note']").Select(Text));

        pane.Find("[data-testid='plan-folded'][data-plan='gamma'] [data-testid='plan-folded-open']").Click();

        pane.WaitForAssertion(() =>
        {
            Assert.Equal("+gamma", host.State.SelectedPlan);
            Assert.Equal("gamma", pane.Find("[data-testid='plan-expanded']").GetAttribute("data-plan"));
        });
    }

    [Fact]
    public async Task Unplanned_closes_the_column_and_Select_new_plan_picks_from_it()
    {
        using var host = await HostAsync();
        var pane = SelectZta(RenderPlans(host));

        Assert.Equal("plan-unplanned", pane.FindAll("[data-testid='plan-sections'] > *")[^1].GetAttribute("data-testid"));
        Assert.Equal("5 open", Text(pane.Find("[data-testid='plan-unplanned-count']")));
        Assert.Equal(4, pane.FindAll("[data-testid='plan-unplanned-row']").Count);
        Assert.Equal("1 more", Text(pane.Find("[data-testid='plan-unplanned-more']")));

        pane.Find("[data-testid='plan-unplanned-select']").Click();

        pane.WaitForAssertion(() =>
        {
            Assert.True(host.State.IsTagSelected(TasksDesktopState.NoPlanTag));
            Assert.True(host.State.SelectionMode);
            Assert.NotNull(pane.Find("[data-testid='entry-list']"));
        });
    }

    // --- The unlock line, pure -------------------------------------------------

    private static PlanSectionEntry Entry(string title, PlanSection section, Guid? id = null) =>
        new(new EntryRow { Id = id ?? Guid.NewGuid(), RawText = $"# {title}\n`prompt` `+p`\n" }, new PlanEntryState(PlanEntryQueueState.Ready), section, PlanSections.NumberOf(title));

    [Fact]
    public void The_unlock_line_names_only_waiting_entries_one_landing_away()
    {
        var a = Entry("14 - A", PlanSection.Now);
        var b = Entry("15 - B", PlanSection.Next);
        var c = Entry("16 - C", PlanSection.Done);
        var one = Entry("20 - One", PlanSection.Waiting);
        var two = Entry("21 - Two", PlanSection.Waiting);
        var deep = Entry("22 - Deep", PlanSection.Waiting);
        var draft = Entry("23 - Draft", PlanSection.Waiting);
        var deps = new Dictionary<EntryRow, IReadOnlyList<string>>
        {
            [one.Row] = [a.Row.TaskId, b.Row.TaskId, c.Row.TaskId],
            [two.Row] = [b.Row.TaskId],
            [deep.Row] = [one.Row.TaskId],
        };

        var line = PlanSections.UnlockLine([a, b, c, one, two, deep, draft], row => deps.GetValueOrDefault(row) ?? []);

        Assert.Equal("4 waiting — next to unlock: #20 when #14 and #15 land, #21 when #15 lands", line);
    }

    [Fact]
    public void With_nothing_one_landing_away_the_unlock_line_is_the_count()
    {
        Assert.Equal("1 waiting", PlanSections.UnlockLine([Entry("Draft", PlanSection.Waiting)], _ => []));
        Assert.Equal("Nothing waiting", PlanSections.UnlockLine([Entry("A", PlanSection.Next)], _ => []));
    }

    [Fact]
    public void The_done_line_names_the_last_pull_request_merged_into_the_plan_branch()
    {
        var first = Entry("12 - First", PlanSection.Done);
        var last = Entry("13 - Last", PlanSection.Done);
        foreach (var (entry, number) in new[] { (first, 1830), (last, 1841) })
        {
            var link = new EntryPullRequestLink(Backlog, number);
            entry.Row.PullRequestLinks = [link];
            entry.Row.PullRequestStatuses = new Dictionary<EntryPullRequestLink, LinkedPullRequest>
            {
                [link] = new(LinkedPullRequestState.Merged) { BaseRefName = "plan/p" }
            };
        }

        var line = PlanSections.DoneLine([Entry("11 - Unmerged", PlanSection.Done), first, last], "plan/p");

        Assert.Equal("3 done — 2 merged into plan/p · last: #13, PR #1841", line);
    }

    [Theory]
    [InlineData("14 - Issue tokens", "#14")]
    [InlineData("Issue tokens", "Issue tokens")]
    [InlineData("  7 - Seven", "#7")]
    public void An_entry_is_named_by_its_step_number(string title, string expected) =>
        Assert.Equal(expected, PlanSections.NumberOf(title));
}
