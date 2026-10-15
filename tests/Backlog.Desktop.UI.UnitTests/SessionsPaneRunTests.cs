using Backlog.Modules.Sessions.UI;
using Backlog.Infrastructure.Sessions;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The delivery runs in the sessions pane: one list, where a run sits under the
/// session that drove it and is a row of its own when the list holds no session for
/// it — whichever of the two readings arrived first. What is asserted is the pane's
/// part: where a run lands and what the line says. The reading is
/// <see cref="DeliveryRunReaderTests"/>' and the matching <see cref="SessionRowsTests"/>'.
/// </summary>
public sealed class SessionsPaneRunTests
{
    private static readonly DateTimeOffset Noon = new(2026, 8, 19, 12, 0, 0, TimeSpan.Zero);

    private const string Folder = @"D:\Repos\Backlog\.claude\worktrees\keen-bose-667825";

    private static readonly string Worktree = DeliveryRunWorktrees.KeyOf(Folder)!;

    [Fact]
    public void A_run_the_session_drove_is_a_line_under_that_sessions_name()
    {
        var run = SessionRowsTests.Run("run-1", Worktree, Noon.AddMinutes(-90), Noon.AddMinutes(-10), title: "Tasks bulk-selection mode") with
        {
            Stages =
            [
                new DeliveryRunStage("Scope Discovery", "done", 60_000, 1),
                new DeliveryRunStage("Implementation", "done", 3_600_000, 3),
                new DeliveryRunStage("QA Validation", "in_progress", null, 0)
            ],
            TokenUsage = new DeliveryRunTokenUsage(
                new DeliveryRunTokens(4789, 9578, 1_662_126, 0, 795_660_916, 21_577_798),
                new DeliveryRunTokens(4411, 8822, 1_089_085, 0, 725_728_911, 18_035_322),
                [new DeliveryRunStageTokens("Implementation", new DeliveryRunTokens(517, 1034, 318_111, 0, 149_099_696, 4_782_409), new DeliveryRunTokens(0, 0, 0, 0, 0, 0))],
                ["claude-opus-5"]),
            Context = new DeliveryRunContext(333_962, 1_000_000, 412_500),
            InsightsByCategory = [new("Shell", 31_639, 7_200_000, 12), new("QA (Playwright/Aspire)", 13_560, 3_000_000, 0)],
            InsightsByServer = [new("plugin_qa_playwright", 9332, 2_000_000, 0)]
        };

        using var context = Context([Live], [run]);

        var pane = context.Render<SessionsPane>();

        pane.WaitForAssertion(() =>
        {
            // One row for the session — not a second row in the list for its run —
            // and on it, the run's stage strip and where the run stands.
            var row = Assert.Single(pane.FindAll("[data-testid='sessions-row']"));

            Assert.Contains("keen-bose-667825", row.TextContent);
            Assert.Empty(row.QuerySelectorAll("[data-testid='sessions-run']"));

            var strip = row.QuerySelector("[data-testid='sessions-stage-strip']")!;
            Assert.Equal(3, strip.QuerySelectorAll(".stage-strip__mark").Length);
            Assert.Equal("2 of 3 stages done", strip.QuerySelector(".stage-strip__marks")!.GetAttribute("aria-label"));
            Assert.Equal("orch-feature · Done, 2 of 3 stages done", strip.QuerySelector(".stage-strip__summary")!.TextContent.Trim());

            // The run itself is in the panel's run card, as a card that titles itself.
            var detail = Assert.Single(pane.FindAll("[data-testid='sessions-detail']"));
            Assert.Empty(detail.QuerySelectorAll("[data-testid='sessions-no-run']"));

            var line = detail.QuerySelector("[data-testid='sessions-run']");
            Assert.NotNull(line);

            // Top to bottom: the header, then the body — insight, tiles, stages,
            // footnote, details — drawn whole, because it is the row's latest run.
            Assert.Equal(["sessions-run__header", "sessions-run__body"], line!.Children.Select(child => child.ClassName));
            var body = line.QuerySelector("[data-testid='sessions-run-panel']")!;
            Assert.False(body.HasAttribute("hidden"));
            Assert.Null(line.QuerySelector("[data-testid='sessions-run-fold']"));

            // The header: "Delivery run", the flow in mono, and the outcome last.
            var header = line.QuerySelector(".sessions-run__header")!;
            Assert.Equal("Delivery run", header.QuerySelector("h4.sessions-run__title")!.TextContent.Trim());
            var flow = header.QuerySelector("[data-testid='sessions-run-flow']")!;
            Assert.Equal("orch-feature", flow.TextContent.Trim());
            Assert.Contains("data-table__mono", flow.ClassList);

            // The library's chip over the dashboard's word, and the word made readable;
            // a span, because this run named no Backlog entry, so there is nowhere to lead.
            var status = header.QuerySelector("[data-testid='sessions-run-status']")!;
            Assert.Equal("Done", status.TextContent.Trim());
            Assert.Equal("SPAN", status.TagName);
            Assert.Same(header.LastElementChild, status.ParentElement);

            // No skill line: the flow in the header is the same id.
            Assert.Null(line.QuerySelector("[data-testid='sessions-run-skill']"));

            // The run's own title is not in the header: beside a reference to the work
            // it names, it is the same sentence twice. It is in the run details.
            Assert.Null(header.QuerySelector("[data-testid='sessions-run-title']"));

            // Nothing linked, no references.
            Assert.Empty(header.QuerySelectorAll(".integration-link"));

            // The figures, as tiles: stages done, context peak with its share of the
            // limit, and tool calls — a run nobody priced has no cost tile, never "$0.00".
            var tiles = body.QuerySelectorAll("[data-testid='sessions-run-figures'] .metric-tile");
            Assert.Equal(
                ["sessions-run-figure-stages", "sessions-run-figure-context", "sessions-run-figure-calls"],
                tiles.Select(tile => tile.GetAttribute("data-testid")));
            Assert.Equal(["Stages done", "Context peak", "Tool calls"], tiles.Select(tile => tile.QuerySelector(".metric-tile__label")!.TextContent.Trim()));
            Assert.Equal(["2/3", "412.5K", "45,199"], tiles.Select(tile => tile.QuerySelector(".metric-tile__value")!.TextContent.Trim()));
            Assert.Equal("41% of the limit", tiles[1].QuerySelector(".metric-tile__footnote")!.TextContent.Trim());

            // No picture of the stages anywhere: the stage list is the run's flow.
            Assert.Empty(line.QuerySelectorAll("[data-testid='diagram-view']"));
            Assert.Empty(line.QuerySelectorAll("[data-testid='flow-step']"));
            Assert.Equal(3, line.QuerySelectorAll("[data-testid='sessions-stage']").Length);

            // The footnote names the run's own flow and the baselines' window.
            Assert.EndsWith(
                "\"vs typical\" compares with that stage's median over your last 30 orch-feature runs.",
                body.QuerySelector("[data-testid='sessions-run-footnote']")!.TextContent.Trim(),
                StringComparison.Ordinal);

            // The rest of the run file, under the footnote, behind a closed fold.
            var details = body.QuerySelector("[data-testid='sessions-run-details-toggle']")!;
            Assert.Equal("Run details", details.TextContent.Trim().TrimStart('▸').Trim());
            Assert.Equal("false", details.GetAttribute("aria-expanded"));
            Assert.Same(body.LastElementChild, details.Closest(".sessions-run__more"));

            // The session row stays a session row: no run-only line on it.
            Assert.Null(row.QuerySelector("[data-testid='sessions-run-only']"));
        });
    }

    /// <summary>
    /// Every run a row holds is in its run card, latest first, and the row's strip
    /// is the latest one's, with the rest counted beside its summary.
    /// </summary>
    [Fact]
    public void A_rows_runs_are_all_in_its_run_card()
    {
        var first = SessionRowsTests.Run("run-1", Worktree, Noon.AddMinutes(-90), Noon.AddMinutes(-40));
        var second = SessionRowsTests.Run("run-2", Worktree, Noon.AddMinutes(-30), Noon.AddMinutes(-10), status: "in_progress") with
        {
            Stages = [new DeliveryRunStage("Scope", "done", 1_000, 1), new DeliveryRunStage("Implement", "in_progress", null, 0), new DeliveryRunStage("Review", "pending", null, 0)]
        };

        using var context = Context([Live], [first, second]);

        var pane = context.Render<SessionsPane>();

        pane.WaitForAssertion(() =>
        {
            var card = pane.Find("[data-testid='sessions-run-card']");

            Assert.Equal("Delivery runs · 2", card.GetAttribute("aria-label"));
            var lines = card.QuerySelectorAll("[data-testid='sessions-run']");
            Assert.Equal(["run-2", "run-1"], lines.Select(line => line.GetAttribute("data-run-id")));

            // The latest is drawn whole; the earlier one folds to its header, its
            // title the fold's trigger, until asked.
            Assert.False(lines[0].QuerySelector("[data-testid='sessions-run-panel']")!.HasAttribute("hidden"));
            Assert.Null(lines[0].QuerySelector("[data-testid='sessions-run-fold']"));

            var fold = lines[1].QuerySelector("[data-testid='sessions-run-fold']")!;
            var earlier = lines[1].QuerySelector("[data-testid='sessions-run-panel']")!;
            Assert.Equal("Delivery run", fold.TextContent.Trim().TrimStart('▸').Trim());
            Assert.NotNull(fold.Closest("h4.sessions-run__title"));
            Assert.Equal(earlier.Id, fold.GetAttribute("aria-controls"));
            Assert.True(earlier.HasAttribute("hidden"));

            Assert.Equal(
                "orch-feature · Implement (2 of 3) · 2 runs",
                pane.Find("[data-testid='sessions-row'] .stage-strip__summary").TextContent.Trim());
        });

        pane.Find("[data-testid='sessions-run'][data-run-id='run-1'] [data-testid='sessions-run-fold']").Click();

        pane.WaitForAssertion(() =>
            Assert.False(pane.Find("[data-testid='sessions-run'][data-run-id='run-1'] [data-testid='sessions-run-panel']").HasAttribute("hidden")));
    }

    [Fact]
    public void Run_details_open_on_the_tokens_context_and_tool_calls()
    {
        var run = SessionRowsTests.Run("run-1", Worktree, Noon.AddMinutes(-90), Noon.AddMinutes(-10)) with
        {
            Stages =
            [
                new DeliveryRunStage("Build & Test", "done", 125_000, 3)
                {
                    Agents = [new("general-purpose", "claude-sonnet-5", 2, 1), new("delivery:builder", null, 0, 0)]
                },
                new DeliveryRunStage("Validation", "in_progress", null, 0),
                new DeliveryRunStage("Summary", "pending", null, 0)
            ],
            TokenUsage = new DeliveryRunTokenUsage(
                new DeliveryRunTokens(66, 132, 32_875, 0, 5_586_145, 457_801),
                new DeliveryRunTokens(49, 98, 13_252, 0, 2_211_729, 416_625),
                [
                    new("Build & Test", new DeliveryRunTokens(10, 20, 500, 0, 1_000, 100), new DeliveryRunTokens(0, 0, 0, 0, 0, 0)),
                    new("Validation", new DeliveryRunTokens(5, 10, 2_000, 0, 500, 50), new DeliveryRunTokens(0, 0, 0, 0, 0, 0))
                ],
                ["claude-opus-5", "claude-sonnet-5"]),
            Context = new DeliveryRunContext(120_000, 200_000, 180_000),
            InsightsByCategory = [new("Shell", 3, 3500, 1), new("Agents", 1, 22_069, 0)],
            InsightsByServer = [new("plugin_qa_aspire", 2, 400, 0)]
        };

        using var context = Context([Live], [run]);

        var pane = context.Render<SessionsPane>();

        pane.WaitForAssertion(() => Assert.NotEmpty(pane.FindAll("[data-testid='sessions-run-details-toggle']")));

        // The stages need no opening: each a row in run order, its dot in the tone of
        // where it stands, a re-entered stage's rounds on its name and a finished
        // stage's time where an unfinished one says where it stands.
        var stages = pane.FindAll("[data-testid='sessions-stage']");
        Assert.Equal(["done", "active", "pending"], stages.Select(stage => stage.QuerySelector("[data-testid='sessions-stage-dot']")!.GetAttribute("data-tone")));
        Assert.Equal("· 3 rounds", stages[0].QuerySelector("[data-testid='sessions-stage-rounds']")!.TextContent.Trim());
        Assert.Equal(["2m 5s", "In progress", "Not yet"], stages.Select(stage => stage.QuerySelector("[data-testid='sessions-stage-took']")!.TextContent.Trim()));

        pane.Find("[data-testid='sessions-run-details-toggle']").Click();

        pane.WaitForAssertion(() =>
        {
            var line = pane.Find("[data-testid='sessions-run']");

            Assert.Equal("true", line.QuerySelector("[data-testid='sessions-run-details-toggle']")!.GetAttribute("aria-expanded"));

            // The run's own words, kept where they do not repeat a reference.
            Assert.Equal("A run", line.QuerySelector("[data-testid='sessions-run-title']")!.TextContent.Trim());

            var tokens = line.QuerySelector("[data-testid='sessions-run-tokens']")!;
            Assert.Contains("66 calls", tokens.TextContent);
            Assert.Contains("claude-opus-5, claude-sonnet-5", tokens.TextContent);

            // The four token kinds as one part-to-whole bar, cheapest first, each
            // part also a legend row carrying its figure.
            var mix = tokens.QuerySelector("[data-testid='sessions-run-token-mix']")!;
            Assert.Equal(
                ["Cache read", "Input", "Cache write", "Output"],
                mix.QuerySelectorAll(".metric-breakdown-bar__legend-label").Select(label => label.TextContent));
            var output = mix.QuerySelectorAll(".metric-breakdown-bar__legend-item")
                .Single(item => item.QuerySelector(".metric-breakdown-bar__legend-label")!.TextContent == "Output");
            Assert.Equal("32.9K", output.QuerySelector(".metric-breakdown-bar__legend-value")!.TextContent);

            Assert.Contains("Sub-agents · 49 calls", tokens.QuerySelector("[data-testid='sessions-run-subagent-mix']")!.TextContent);

            // Stages ranked by the output they produced, the calls beside it.
            var stageTokens = tokens.QuerySelector("[data-testid='sessions-run-stage-tokens']")!;
            Assert.Equal(
                ["Validation", "Build & Test"],
                stageTokens.QuerySelectorAll(".metric-ranking__name").Select(name => name.TextContent));
            Assert.Equal("5 calls", stageTokens.QuerySelector(".metric-ranking__detail")!.TextContent);
            Assert.Equal("2,000 out", stageTokens.QuerySelector(".metric-ranking__value")!.TextContent);

            // Context against its limit is a meter, with the reading and the last
            // sample in words beside it.
            var meter = line.QuerySelector("[data-testid='sessions-run-context'] [role='meter']")!;
            Assert.Equal("180.0K of 200.0K", meter.GetAttribute("aria-valuetext"));
            Assert.Contains("90% at peak", line.QuerySelector("[data-testid='sessions-run-context']")!.TextContent);

            // Ranked by time spent, each group's calls and failures printed beside
            // the time it took.
            var insights = line.QuerySelector("[data-testid='sessions-run-insights']")!;
            var shell = insights.QuerySelectorAll(".metric-ranking__item")
                .Single(item => item.QuerySelector(".metric-ranking__name")!.TextContent == "Shell");
            Assert.Equal("3 calls · 1 failed", shell.QuerySelector(".metric-ranking__detail")!.TextContent);
            Assert.Equal("3s", shell.QuerySelector(".metric-ranking__value")!.TextContent);
            // Agents took 22s against Shell's 3s, so it leads although the run
            // file listed Shell first.
            Assert.Equal(
                ["Agents", "Shell"],
                insights.QuerySelectorAll(".metric-ranking__name").Select(name => name.TextContent));

            Assert.Contains("plugin_qa_aspire", line.QuerySelector("[data-testid='sessions-run-servers']")!.TextContent);

        });
    }

    /// <summary>
    /// The GitHub work a run links to, drawn as the Integrations family's references
    /// — the way the storybook's references page draws them — in the header between
    /// the flow and the outcome. The Backlog entry is not among them: the status badge
    /// is how this card reaches one.
    /// </summary>
    [Fact]
    public void The_work_a_run_links_to_is_an_integration_reference_per_kind()
    {
        var run = SessionRowsTests.Run("run-1", Worktree, Noon.AddMinutes(-90), Noon.AddMinutes(-10)) with
        {
            References =
            [
                new(DeliveryRunReferenceKind.Task, "delivery-run-reader", "Plan backlog-mcp-server", null, null),
                new(DeliveryRunReferenceKind.Issue, "#211", "Fix the leak", "https://github.com/JSdotNet/Backlog/issues/211", "JSdotNet/Backlog"),
                new(DeliveryRunReferenceKind.PullRequest, "PR #482", "Devbook rename", "https://github.com/JSdotNet/Backlog/pull/482", "JSdotNet/Backlog")
            ]
        };

        using var context = Context([Live], [run]);

        var pane = context.Render<SessionsPane>();

        pane.WaitForAssertion(() =>
        {
            var line = pane.Find("[data-testid='sessions-run']");

            // The Backlog entry is not a chip on the line: the status badge leads
            // there, and a second control to one place is one too many.
            Assert.Null(line.QuerySelector("[data-testid='sessions-run-task']"));

            var issue = line.QuerySelector("a[data-testid='sessions-run-issue']")!;
            Assert.Equal("https://github.com/JSdotNet/Backlog/issues/211", issue.GetAttribute("href"));
            Assert.Equal("_blank", issue.GetAttribute("target"));
            Assert.Equal("#211", issue.QuerySelector(".integration-link__label")!.TextContent.Trim());
            Assert.NotNull(issue.QuerySelector(".provider-mark"));

            // The item's title rides in the tooltip, not beside the number: the run's
            // own title already says what the work is about.
            Assert.Null(issue.QuerySelector(".integration-link__title"));
            Assert.Equal("Fix the leak", issue.GetAttribute("title"));

            // No state chip on any of them: nothing here has asked GitHub.
            Assert.Empty(line.QuerySelectorAll(".integration-link .badge--integration"));

            var pull = line.QuerySelector("a[data-testid='sessions-run-pull-request']")!;
            Assert.Equal("https://github.com/JSdotNet/Backlog/pull/482", pull.GetAttribute("href"));
            Assert.Equal("PR #482", pull.QuerySelector(".integration-link__label")!.TextContent.Trim());

            // In the order a reader asks: the flow, what the run was for, what it
            // produced, then how it ended.
            var header = line.QuerySelector(".sessions-run__header")!;

            Assert.Equal(
                ["sessions-run-flow", "sessions-run-issue", "sessions-run-pull-request", "sessions-run-status"],
                header.QuerySelectorAll("[data-testid]").Select(child => child.GetAttribute("data-testid")));

            // The status never leads to the pull request beside it: with no way to
            // open the entry, it is the span every badge here is.
            var status = header.QuerySelector("[data-testid='sessions-run-status']")!;

            Assert.Equal("SPAN", status.TagName);
            Assert.Null(status.GetAttribute("href"));
            Assert.Equal("Done", status.TextContent.Trim());
        });
    }

    /// <summary>
    /// The toggle keeps only the rows with a run on them. A filter like Show: the
    /// badge says what it hid, and it is off again on every open.
    /// </summary>
    [Fact]
    public void With_runs_keeps_only_the_rows_that_carry_a_run()
    {
        var run = SessionRowsTests.Run("run-1", Worktree, Noon.AddMinutes(-90), Noon.AddMinutes(-10));
        var without = Live with { Id = "bare", Title = "bare-session", WorkingFolder = @"D:\Repos\Other" };

        using var context = Context([Live, without], [run]);

        var pane = context.Render<SessionsPane>();

        pane.WaitForAssertion(() =>
        {
            Assert.Equal(2, pane.FindAll("[data-testid='sessions-row']").Count);
            Assert.Equal("false", pane.Find("[data-testid='sessions-runs-only']").GetAttribute("aria-pressed"));
        });

        pane.Find("[data-testid='sessions-runs-only']").Click();

        pane.WaitForAssertion(() =>
        {
            var row = Assert.Single(pane.FindAll("[data-testid='sessions-row']"));

            Assert.Contains("keen-bose-667825", row.TextContent);

            // The run is under the row, across the columns.
            Assert.NotNull(Assert.Single(pane.FindAll("[data-testid='sessions-detail']")).QuerySelector("[data-testid='sessions-run']"));
            Assert.Equal("1 of 2 sessions", pane.Find("[data-testid='sessions-count']").TextContent.Trim());
        });

        pane.Find("[data-testid='sessions-runs-only']").Click();

        pane.WaitForAssertion(() => Assert.Equal(2, pane.FindAll("[data-testid='sessions-row']").Count));
    }

    [Fact]
    public void With_runs_over_nothing_says_so_rather_than_showing_an_empty_table()
    {
        using var context = Context([Live], []);

        var pane = context.Render<SessionsPane>();

        pane.WaitForAssertion(() => Assert.NotEmpty(pane.FindAll("[data-testid='sessions-runs-only']")));
        pane.Find("[data-testid='sessions-runs-only']").Click();

        pane.WaitForAssertion(() =>
        {
            Assert.Empty(pane.FindAll("[data-testid='sessions-row']"));
            Assert.Contains("No delivery runs here.", pane.Markup);
            Assert.Contains("Turn off With delivery runs to see them all.", pane.Markup);
        });
    }

    /// <summary>
    /// A run the list holds no session for is a row of the same list, named after its
    /// worktree, with a line saying what it rests on. Under All, because with no
    /// liveness evidence it is Finished — a session still running would be in the
    /// reading and the run would have joined it.
    /// </summary>
    [Fact]
    public void A_run_with_no_session_in_the_list_is_a_row_of_the_same_list()
    {
        var stray = SessionRowsTests.Run("run-stray", "old-worktree-1a2b3c4d", Noon.AddDays(-5), Noon.AddDays(-5).AddHours(2), title: "An old run", status: "in_progress");

        using var context = Context([Live], [stray]);

        var pane = context.Render<SessionsPane>();

        // Live first: one row, the live session. A run alone is not live.
        pane.WaitForAssertion(() =>
        {
            Assert.Single(pane.FindAll("[data-testid='sessions-row']"));
            Assert.Empty(pane.FindAll("[data-testid='sessions-run-only']"));

            // And the count says two exist, one shown — and does not call the run a
            // session.
            Assert.Equal("1 of 2 sessions and runs", pane.Find("[data-testid='sessions-count']").TextContent.Trim());
            Assert.Contains("And 1 delivery run no listed session drove, as a row of its own.", pane.Find(".sessions-panel__subtitle").TextContent);
        });

        ShowAll(pane);

        pane.WaitForAssertion(() =>
        {
            var rows = pane.FindAll("[data-testid='sessions-row']");

            Assert.Equal(2, rows.Count);

            // Most recently active first: the session today, the run five days ago.
            var row = rows[1];

            Assert.Contains("old-worktree", row.QuerySelector(".sessions-row__title")!.TextContent);
            Assert.Equal("run/orch-dashboard/old-worktree-1a2b3c4d/run-stray", row.GetAttribute("data-row-key"));
        });

        var detail = SessionsPaneTests.Pick(pane, "run/orch-dashboard/old-worktree-1a2b3c4d/run-stray");

        pane.WaitForAssertion(() =>
        {
            var row = pane.FindAll("[data-testid='sessions-row']")[1];
            var line = detail.QuerySelector("[data-testid='sessions-run-only']")!;
            Assert.Equal(
                "No session record on this PC. Recorded by orch-dashboard under old-worktree-1a2b3c4d.",
                line.TextContent.Trim());

            // The run itself, in the panel's run card; the dashboard's own status on
            // its chip, and its flow beside the title.

            Assert.Equal("In progress", detail.QuerySelector("[data-testid='sessions-run-status']")!.TextContent.Trim());
            Assert.Equal("orch-feature", detail.QuerySelector("[data-testid='sessions-run-flow']")!.TextContent.Trim());

            // A Claude row on this machine, dated by the run, and Finished on the
            // row's own chip: the run's word is the run's, the row's state is the
            // list's, and the two say different true things.
            Assert.Contains("Claude", row.TextContent);
            Assert.Contains("DEV-TOWER", row.TextContent);
            Assert.NotEmpty(row.QuerySelectorAll(".badge--integration-finished"));

            // One list, still.
            Assert.Single(pane.FindAll("[data-testid='sessions-list']"));
        });
    }

    /// <summary>
    /// A schedule's runs, each in a worktree of its own and none with a session in the
    /// list, are one row titled by the schedule with every run a line under it, each
    /// line carrying the "scheduled" chip.
    /// </summary>
    [Fact]
    public void A_schedules_runs_are_one_row_with_a_scheduled_chip_on_every_line()
    {
        DeliveryRun Fired(string id, string worktree, int daysAgo) =>
            SessionRowsTests.Run(id, worktree, Noon.AddDays(-daysAgo), Noon.AddDays(-daysAgo).AddHours(1), status: "done") with
            {
                Trigger = DeliveryRunTriggers.Scheduled,
                Schedule = "devbook-pull-sweep",
                Repository = "JSdotNet/Backlog",
                Verdicts =
                [
                    new(".devbook/domain/tasks/domain.md#task", "aggregate", "pull", null, "aligned", "none", null, []),
                    new(".devbook/domain/inbox/domain.md#inbox-item", "aggregate", "pull", null, "code-ahead", "pr", "https://github.com/JSdotNet/Backlog/pull/951", [])
                ]
            };

        using var context = Context([Live], [Fired("pull-2", "sweep-b-00000002", 1), Fired("pull-1", "sweep-a-00000001", 8)]);

        var pane = context.Render<SessionsPane>();

        ShowAll(pane);

        pane.WaitForAssertion(() =>
        {
            var rows = pane.FindAll("[data-testid='sessions-row']");

            Assert.Equal(2, rows.Count);

            var row = rows[1];

            Assert.Contains("devbook-pull-sweep", row.QuerySelector(".sessions-row__title")!.TextContent);
        });

        var detail = SessionsPaneTests.Pick(pane, "schedule/devbook-pull-sweep/JSdotNet/Backlog");

        pane.WaitForAssertion(() =>
        {
            Assert.Equal(
                "Scheduled, unattended. 2 runs recorded on this PC.",
                detail.QuerySelector("[data-testid='sessions-run-only']")!.TextContent.Trim());

            var lines = detail.QuerySelectorAll("[data-testid='sessions-run']");

            Assert.Equal(["pull-2", "pull-1"], lines.Select(line => line.GetAttribute("data-run-id")));

            foreach (var line in lines)
            {
                var chip = line.QuerySelector("[data-testid='sessions-run-scheduled']")!;

                Assert.Equal("scheduled", chip.TextContent.Trim());
                Assert.Contains("badge--trigger", chip.ClassName);
                Assert.Equal("Fired by the devbook-pull-sweep schedule, unattended", chip.GetAttribute("title"));
                Assert.Equal("2 units: 1 aligned · 1 code-ahead", line.QuerySelector("[data-testid='sessions-run-verdicts']")!.TextContent.Trim());
            }

            Assert.Contains("And 1 schedule's unattended runs, as one row.", pane.Find(".sessions-panel__subtitle").TextContent);
        });
    }

    [Fact]
    public void An_attended_run_carries_no_scheduled_chip()
    {
        var stray = SessionRowsTests.Run("run-stray", "old-worktree-1a2b3c4d", Noon.AddDays(-5), Noon.AddDays(-5).AddHours(2));

        using var context = Context([Live], [stray]);

        var pane = context.Render<SessionsPane>();

        ShowAll(pane);

        pane.WaitForAssertion(() =>
        {
            Assert.Equal(2, pane.FindAll("[data-testid='sessions-row']").Count);
            Assert.Empty(pane.FindAll("[data-testid='sessions-run-scheduled']"));
        });
    }

    /// <summary>The list is one list under every control: a run-only row groups by
    /// the day it was last active, beside the sessions.</summary>
    [Fact]
    public void A_run_only_row_groups_with_the_sessions()
    {
        var stray = SessionRowsTests.Run("run-stray", "old-worktree-1a2b3c4d", Noon.AddDays(-5), Noon.AddDays(-5).AddHours(2));

        using var context = Context([Live, Live with { Id = "copilot", Kind = AgentSessionKind.Copilot, Title = "JSdotNet/Backlog" }], [stray]);

        var pane = context.Render<SessionsPane>();

        ShowAll(pane);
        SessionsPaneTests.GroupBy(pane, "when");

        pane.WaitForAssertion(() =>
        {
            Assert.Equal(
                ["Live now", "Older"],
                pane.FindAll("[data-testid='sessions-group-name']").Select(name => name.TextContent.Trim()));

            // The two live sessions, and the run under the day it was last active.
            Assert.Equal(3, pane.FindAll("[data-testid='sessions-row']").Count);
            Assert.Equal(["2", "1"], pane.FindAll("[data-testid='sessions-group-size']").Select(size => size.TextContent.Trim()));
        });
    }

    [Fact]
    public void A_run_only_row_follows_the_machine_filter_by_the_machine_its_file_was_read_on()
    {
        var elsewhere = Live with
        {
            Id = "e7f2b1a0",
            EnvironmentId = "laptop",
            Environment = "DEV-LAPTOP",
            Origin = AgentSessionOrigin.Replicated,
            WorkingFolder = string.Empty
        };
        var stray = SessionRowsTests.Run("run-stray", "old-worktree-1a2b3c4d", Noon.AddDays(-5), Noon.AddDays(-5).AddHours(2));

        using var context = Context([Live, elsewhere], [stray]);

        var pane = context.Render<SessionsPane>();

        ShowAll(pane);

        pane.WaitForAssertion(() => Assert.Single(pane.FindAll("[data-testid='sessions-row'][data-row-key^='run/']")));

        pane.Find("[data-testid='sessions-machine-filter'] select").Change("laptop");

        pane.WaitForAssertion(() => Assert.Empty(pane.FindAll("[data-testid='sessions-row'][data-row-key^='run/']")));

        pane.Find("[data-testid='sessions-machine-filter'] select").Change("tower");

        pane.WaitForAssertion(() => Assert.Single(pane.FindAll("[data-testid='sessions-row'][data-row-key^='run/']")));
    }

    /// <summary>
    /// The status badge is how a run reaches the Backlog entry it was started from:
    /// a button once the host has said how to open one, and the span every badge here
    /// is otherwise — never an anchor to the pull request stacked above it.
    /// </summary>
    [Fact]
    public void The_status_opens_the_backlog_entry_where_the_host_can()
    {
        var run = SessionRowsTests.Run("run-1", Worktree, Noon.AddMinutes(-90), Noon.AddMinutes(-10)) with
        {
            References =
            [
                new(DeliveryRunReferenceKind.Task, "delivery-run-reader", "Plan backlog-mcp-server", null, null, "backlog-mcp-server"),
                new(DeliveryRunReferenceKind.PullRequest, "PR #482", "Devbook rename", "https://github.com/JSdotNet/Backlog/pull/482", "JSdotNet/Backlog")
            ]
        };

        using var inert = Context([Live], [run]);

        var without = inert.Render<SessionsPane>();

        without.WaitForAssertion(() =>
        {
            var status = without.Find("[data-testid='sessions-run-status']");

            Assert.Equal("SPAN", status.TagName);
            Assert.Null(status.GetAttribute("href"));
        });

        DeliveryRunReference? opened = null;

        using var context = Context([Live], [run]);

        var pane = context.Render<SessionsPane>(parameters => parameters
            .Add(component => component.OnOpenTask, EventCallback.Factory.Create<DeliveryRunReference>(this, reference => opened = reference)));

        pane.WaitForAssertion(() =>
        {
            var status = pane.Find("[data-testid='sessions-run-status']");

            Assert.Equal("BUTTON", status.TagName);
            Assert.Equal("Open delivery-run-reader in the task list", status.GetAttribute("title"));

            // And still not a second way to the pull request.
            Assert.Null(status.GetAttribute("href"));
        });

        pane.Find("[data-testid='sessions-run-status']").Click();

        pane.WaitForAssertion(() =>
        {
            Assert.NotNull(opened);

            // The whole reference, so the host has the plan as well as the id — the
            // pair an entry is identified by.
            Assert.Equal("delivery-run-reader", opened!.Label);
            Assert.Equal("backlog-mcp-server", opened.Plan);
        });
    }

    /// <summary>
    /// A run whose prompt named no entry borrows the entry its session was linked to,
    /// so its status still leads to the work; the host answers which one, by session id.
    /// </summary>
    [Fact]
    public void A_run_naming_no_entry_opens_the_entry_that_linked_its_session()
    {
        var run = SessionRowsTests.Run("run-1", Worktree, Noon.AddMinutes(-90), Noon.AddMinutes(-10));
        var linked = new DeliveryRunReference(DeliveryRunReferenceKind.Task, "Link the done badge", null, null, null, EntryId: Guid.NewGuid());
        string? askedFor = null;
        DeliveryRunReference? opened = null;

        using var context = Context([Live], [run]);

        var pane = context.Render<SessionsPane>(parameters => parameters
            .Add(component => component.SessionTask, sessionId => { askedFor = sessionId; return linked; })
            .Add(component => component.OnOpenTask, EventCallback.Factory.Create<DeliveryRunReference>(this, reference => opened = reference)));

        pane.WaitForAssertion(() =>
        {
            var status = pane.Find("[data-testid='sessions-run-status']");

            Assert.Equal("BUTTON", status.TagName);
            Assert.Equal("Open Link the done badge in the task list", status.GetAttribute("title"));
        });

        Assert.Equal(Live.Id, askedFor);

        pane.Find("[data-testid='sessions-run-status']").Click();

        pane.WaitForAssertion(() => Assert.Same(linked, opened));
    }

    /// <summary>
    /// The session's entry is a fallback, never a second opinion: a run that named its
    /// own entry opens that one, because the prompt is what the run was started from.
    /// </summary>
    [Fact]
    public void A_run_that_names_its_own_entry_keeps_it_over_its_sessions_link()
    {
        var own = new DeliveryRunReference(DeliveryRunReferenceKind.Task, "delivery-run-reader", "Plan backlog-mcp-server", null, null, "backlog-mcp-server");
        var run = SessionRowsTests.Run("run-1", Worktree, Noon.AddMinutes(-90), Noon.AddMinutes(-10)) with { References = [own] };
        DeliveryRunReference? opened = null;

        using var context = Context([Live], [run]);

        var pane = context.Render<SessionsPane>(parameters => parameters
            .Add(component => component.SessionTask, _ => new DeliveryRunReference(DeliveryRunReferenceKind.Task, "Something else", null, null, null, EntryId: Guid.NewGuid()))
            .Add(component => component.OnOpenTask, EventCallback.Factory.Create<DeliveryRunReference>(this, reference => opened = reference)));

        pane.WaitForAssertion(() => Assert.Equal("BUTTON", pane.Find("[data-testid='sessions-run-status']").TagName));

        pane.Find("[data-testid='sessions-run-status']").Click();

        pane.WaitForAssertion(() => Assert.Same(own, opened));
    }

    /// <summary>
    /// The row's Task column names the entry its runs were started from and opens it,
    /// so the work is one click from the row while the run lines stay folded.
    /// </summary>
    [Fact]
    public void The_task_column_names_the_rows_entry_and_opens_it()
    {
        var own = new DeliveryRunReference(DeliveryRunReferenceKind.Task, "delivery-run-reader", "Plan backlog-mcp-server", null, null, "backlog-mcp-server");
        var run = SessionRowsTests.Run("run-1", Worktree, Noon.AddMinutes(-90), Noon.AddMinutes(-10)) with { References = [own] };
        DeliveryRunReference? opened = null;

        using var context = Context([Live], [run]);

        var pane = context.Render<SessionsPane>(parameters => parameters
            .Add(component => component.OnOpenTask, EventCallback.Factory.Create<DeliveryRunReference>(this, reference => opened = reference)));

        pane.WaitForAssertion(() =>
        {
            var task = pane.Find("[data-testid='sessions-task-card'] [data-testid='sessions-task']");

            Assert.Contains("delivery-run-reader", task.TextContent);
        });

        pane.Find("[data-testid='sessions-task-card'] [data-testid='sessions-task']").Click();

        pane.WaitForAssertion(() => Assert.Same(own, opened));
    }

    /// <summary>A row with no run still names the entry its session was linked to.</summary>
    [Fact]
    public void The_task_column_falls_back_to_the_sessions_linked_entry()
    {
        var linked = new DeliveryRunReference(DeliveryRunReferenceKind.Task, "Link the done badge", null, null, null, EntryId: Guid.NewGuid());

        using var context = Context([Live], []);

        var pane = context.Render<SessionsPane>(parameters => parameters
            .Add(component => component.SessionTask, _ => linked));

        pane.WaitForAssertion(() =>
            Assert.Contains("Link the done badge", pane.Find("[data-testid='sessions-task-card'] [data-testid='sessions-task']").TextContent));
    }

    [Fact]
    public void A_dashboard_that_could_not_be_read_is_named_in_the_same_notice()
    {
        using var context = Context([Live], [], unreadable: ["orch-dashboard"]);

        var pane = context.Render<SessionsPane>();

        pane.WaitForAssertion(() =>
        {
            var notice = pane.Find("[data-testid='sessions-unreadable']");

            Assert.Contains("Could not read orch-dashboard on this PC.", notice.TextContent);
        });
    }

    /// <summary>A verify stage's scenario evidence (BL2) shows in that stage's own
    /// panel, one row per part with the run's outcome as its dot, and nowhere else
    /// on the run card.</summary>
    [Fact]
    public void A_verify_stage_shows_its_scenario_evidence_per_part()
    {
        var run = SessionRowsTests.Run("run-1", Worktree, Noon.AddMinutes(-90), Noon.AddMinutes(-10)) with
        {
            Stages =
            [
                new DeliveryRunStage("Build & Test", "done", 125_000, 1),
                new DeliveryRunStage("Verify", "done", 60_000, 1)
                {
                    Evidence =
                    [
                        new DeliveryScenarioEvidence("set-up-and-fill-the-backlog", "statuses-are-set-up", "passed", "2026-10-07T08:30:00Z"),
                        new DeliveryScenarioEvidence("set-up-and-fill-the-backlog", "an-item-is-moved", "not-run", null)
                    ]
                }
            ]
        };

        using var context = Context([Live], [run]);

        var pane = context.Render<SessionsPane>();

        // Closed, the Verify row already says how its parts went.
        pane.WaitForAssertion(() =>
        {
            var verify = pane.Find("[data-testid='sessions-stage'][data-stage='Verify']");
            Assert.Equal("1 of 2 passing", verify.QuerySelector("[data-testid='sessions-stage-evidence-summary']")!.TextContent.Trim());
        });

        pane.Find("[data-testid='sessions-stage'][data-stage='Verify'] [data-testid='sessions-stage-toggle']").Click();

        pane.WaitForAssertion(() =>
        {
            var list = Assert.Single(pane.FindAll("[data-testid='sessions-stage-evidence']"));
            Assert.Null(list.Closest("[hidden]"));
            Assert.NotNull(list.Closest("[data-testid='sessions-stage'][data-stage='Verify'] [data-testid='sessions-stage-panel']"));
            Assert.Equal("Scenario evidence", list.GetAttribute("aria-label"));
            Assert.Equal("1 of 2 passing", list.QuerySelector("[data-testid='sessions-stage-evidence-count']")!.TextContent.Trim());

            var parts = list.QuerySelectorAll("[data-testid='sessions-stage-evidence-part']");
            Assert.Equal(["passed", "never-run"], parts.Select(part => part.GetAttribute("data-state")));
            Assert.Equal("statuses-are-set-up", parts[0].QuerySelector(".scenario-checklist__part")!.TextContent.Trim());
            Assert.Equal("set-up-and-fill-the-backlog", parts[0].QuerySelector(".scenario-checklist__page")!.TextContent.Trim());

            // A run's evidence is no item: it never says proved.
            Assert.Empty(list.QuerySelectorAll("[data-testid='sessions-stage-evidence-proved']"));
        });

        // Nothing draws the evidence outside the stage list.
        Assert.All(pane.FindAll(".scenario-checklist"), checklist => Assert.NotNull(checklist.Closest("[data-testid='sessions-stages']")));
    }

    [Fact]
    public void A_run_with_no_evidence_draws_no_evidence_list()
    {
        var run = SessionRowsTests.Run("run-1", Worktree, Noon.AddMinutes(-90), Noon.AddMinutes(-10)) with
        {
            Stages = [new DeliveryRunStage("Verify", "done", 60_000, 1)]
        };

        using var context = Context([Live], [run]);

        var pane = context.Render<SessionsPane>();

        pane.WaitForAssertion(() => Assert.NotEmpty(pane.FindAll("[data-testid='sessions-stage-toggle']")));
        pane.Find("[data-testid='sessions-stage-toggle']").Click();

        pane.WaitForAssertion(() => Assert.NotEmpty(pane.FindAll("[data-testid='sessions-stage-panel']")));
        Assert.Empty(pane.FindAll("[data-testid='sessions-stage-evidence-summary']"));
        Assert.Empty(pane.FindAll(".scenario-checklist"));
    }

    private static void ShowAll(IRenderedComponent<SessionsPane> pane)
    {
        pane.WaitForAssertion(() => Assert.NotEmpty(pane.FindAll("[data-testid='sessions-view-all']")));
        pane.Find("[data-testid='sessions-view-all']").Click();

        pane.WaitForAssertion(() =>
            Assert.Equal("true", pane.Find("[data-testid='sessions-view-all']").GetAttribute("aria-pressed")));
    }

    [Fact]
    public void A_run_this_product_recorded_claims_no_figure_it_could_not_measure()
    {
        // What the delivery surface writes: the resolved model, because a run states
        // it, and no consumption, because this product cannot measure it — those
        // figures come from a collector watching a session's own tool calls, and the
        // server recording this run sees a tool call arrive rather than the session
        // that made it. The reader fills an absent bucket with zeros, so the run
        // arrives here carrying a token usage whose every number is nought.
        var run = SessionRowsTests.Run("run-1", Worktree, Noon.AddMinutes(-90), Noon.AddMinutes(-10), title: "Surface lifecycle tools") with
        {
            Dashboard = "backlog",
            SkillId = "flow-code",
            Stages =
            [
                new DeliveryRunStage("Scope Discovery", "done", 60_000, 1),
                new DeliveryRunStage("Implementation", "done", 3_600_000, 1)
            ],
            TokenUsage = new DeliveryRunTokenUsage(
                new DeliveryRunTokens(0, 0, 0, 0, 0, 0),
                new DeliveryRunTokens(0, 0, 0, 0, 0, 0),
                [],
                ["claude-opus-5"]),
            Context = null
        };

        using var context = Context([Live], [run]);

        var pane = context.Render<SessionsPane>();

        pane.WaitForAssertion(() =>
        {
            var tiles = pane.FindAll("[data-testid='sessions-run-figures'] .metric-tile");

            // The stage count is measured and is shown. The three figures this run
            // does not carry are left out rather than shown as zero — the rule the
            // area already states for a dashboard's own gaps, which a run recorded
            // here is simply another case of.
            var tile = Assert.Single(tiles);
            Assert.Equal("sessions-run-figure-stages", tile.GetAttribute("data-testid"));
            Assert.Equal("2/2", tile.QuerySelector(".metric-tile__value")!.TextContent.Trim());
        });

        // And the same inside the run details, which is where this first went wrong:
        // the rule held on the figures and not under them, so opening the details on
        // a run recorded here answered "0 calls · 0 out · 0 in · 0 cache read · 0 cache
        // write" — a measurement nobody made, contradicting the figures above.
        pane.Find("[data-testid='sessions-run-details-toggle']").Click();

        pane.WaitForAssertion(() =>
        {
            var tokens = pane.Find("[data-testid='sessions-run-tokens']").TextContent;

            // The model is kept: the run states it, so it is observed rather than
            // measured, and it is the one thing in here this product does know.
            Assert.Contains("claude-opus-5", tokens, StringComparison.Ordinal);

            Assert.DoesNotContain("0 calls", tokens, StringComparison.Ordinal);
            Assert.DoesNotContain("0 out", tokens, StringComparison.Ordinal);
            Assert.DoesNotContain("cache read", tokens, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public void A_run_that_was_measured_still_shows_every_figure()
    {
        // The other side of the rule, so the fix above cannot be mistaken for
        // "stop showing tokens": a run whose dashboard did measure consumption
        // reports all of it, in the fold as well as on the trigger.
        var run = SessionRowsTests.Run("run-1", Worktree, Noon.AddMinutes(-90), Noon.AddMinutes(-10)) with
        {
            Stages = [new DeliveryRunStage("Implementation", "done", 3_600_000, 1)],
            TokenUsage = new DeliveryRunTokenUsage(
                new DeliveryRunTokens(4789, 9578, 1_662_126, 0, 795_660_916, 21_577_798),
                new DeliveryRunTokens(0, 0, 0, 0, 0, 0),
                [],
                ["claude-opus-5"])
        };

        using var context = Context([Live], [run]);

        var pane = context.Render<SessionsPane>();

        // The tiles are stages done, cost, context and calls; the tokens are the run
        // details', where every one of them is still shown.
        pane.WaitForAssertion(() => Assert.Equal("1/1", pane.Find("[data-testid='sessions-run-figure-stages'] .metric-tile__value").TextContent.Trim()));

        pane.Find("[data-testid='sessions-run-details-toggle']").Click();

        pane.WaitForAssertion(() =>
        {
            var tokens = pane.Find("[data-testid='sessions-run-tokens']").TextContent;

            Assert.Contains("4,789 calls", tokens, StringComparison.Ordinal);
            Assert.Contains("Cache read", tokens, StringComparison.Ordinal);
            Assert.Contains("claude-opus-5", tokens, StringComparison.Ordinal);
        });
    }

    /// <summary>
    /// A flow reports to every surface it is bound to, and each surface files the run
    /// under an id of its own: two files in two dashboards' folders for one piece of
    /// work. Read off the disk the way the app reads them, the pair is one card under
    /// the session that drove it, and its run details say which surfaces reported it.
    /// </summary>
    [Fact]
    public void A_run_two_surfaces_reported_is_one_line_under_its_session()
    {
        var home = Path.Combine(Path.GetTempPath(), "backlog-sessions-pane-tests", Guid.NewGuid().ToString("n"));

        try
        {
            GivenRun(home, "delivery-surface-dashboard", "run-mujuq9gz-xn8tz3", Noon.AddMinutes(-60), "in_progress");
            GivenRun(home, "backlog", "run-mujuq9av-zxl8tg", Noon.AddMinutes(-60).AddMilliseconds(-220), "in_progress");

            using var context = new BunitContext();
            context.Services.AddSingleton<IAgentSessionSource>(new StubSessionSource([Live]));
            context.Services.AddSingleton<IDeliveryRunSource>(new LocalDeliveryRunSource(home, Live.EnvironmentId, Live.Environment));

            var pane = context.Render<SessionsPane>();

            pane.WaitForAssertion(() =>
            {
                var row = Assert.Single(pane.FindAll("[data-testid='sessions-row']"));

                Assert.Contains("keen-bose-667825", row.TextContent);

                // One line, not two: the dashboard's file is the record.
                var line = Assert.Single(pane.FindAll("[data-testid='sessions-run']"));

                Assert.Equal("run-mujuq9gz-xn8tz3", line.GetAttribute("data-run-id"));

                // And the run details say who reported it, after the run's own identity.
                var surfaces = line.QuerySelector("[data-testid='sessions-run-surfaces']");

                Assert.NotNull(surfaces);
                Assert.Equal("Reported to 2 surfaces: delivery-surface-dashboard, backlog", surfaces!.TextContent.Trim());
                Assert.Equal("Surfaces", surfaces.PreviousElementSibling!.TextContent.Trim());
            });
        }
        finally
        {
            try
            {
                if (Directory.Exists(home)) Directory.Delete(home, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    /// <summary>One surface's file of a run the <see cref="Live"/> session drove,
    /// in the folder the dashboards key on that session's worktree.</summary>
    private static void GivenRun(string home, string dashboard, string id, DateTimeOffset started, string status)
    {
        var folder = Path.Combine(home, dashboard, Worktree, "runs");

        Directory.CreateDirectory(folder);
        File.WriteAllText(
            Path.Combine(folder, id + ".json"),
            $$"""
            {
              "id": "{{id}}",
              "skillId": "flow-code",
              "title": "Roadmap: an effort-placed plan keeps up with its work",
              "status": "{{status}}",
              "sessionIds": ["{{Live.Id}}"],
              "startedAt": "{{Stamp(started)}}",
              "updatedAt": "{{Stamp(started.AddMinutes(30))}}",
              "stages": [{ "name": "Scope Discovery", "status": "done", "doneCount": 1 }]
            }
            """);
    }

    private static string Stamp(DateTimeOffset moment) =>
        moment.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", System.Globalization.CultureInfo.InvariantCulture);

    private static BunitContext Context(
        IReadOnlyList<AgentSession> sessions,
        IReadOnlyList<DeliveryRun> runs,
        IReadOnlyList<string>? unreadable = null)
    {
        var context = new BunitContext();
        context.Services.AddSingleton<IAgentSessionSource>(new StubSessionSource(sessions));
        context.Services.AddSingleton<IDeliveryRunSource>(new SessionsPaneTests.StubRunSource(runs, unreadable ?? []));

        return context;
    }

    private static readonly AgentSession Live = new(
        Id: "5905cf2d",
        Kind: AgentSessionKind.Claude,
        EnvironmentId: "tower",
        Environment: "DEV-TOWER",
        Title: "keen-bose-667825",
        WorkingFolder: Folder,
        Repository: null,
        Branch: "claude/desktop-session-area",
        StartedAt: Noon.AddHours(-2),
        LastActivityAt: Noon.AddMinutes(-3),
        State: AgentSessionState.Running,
        TurnCount: 12,
        Origin: AgentSessionOrigin.Local);

    private sealed class StubSessionSource(IReadOnlyList<AgentSession> sessions) : IAgentSessionSource
    {
        public Task<AgentSessionCatalog> GetSessionsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new AgentSessionCatalog(sessions, [], sessions.Count));

        public Task<AgentSessionCatalog> GetSessionsAsync(AgentSessionQuery query, CancellationToken cancellationToken = default) =>
            GetSessionsAsync(cancellationToken);
    }
}
