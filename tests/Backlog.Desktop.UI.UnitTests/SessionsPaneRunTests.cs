using Backlog.Modules.Sessions.UI;
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
            // One row for the session, and its runs under it across every column —
            // not a second row in the list, and not a third line of the name cell.
            var row = Assert.Single(pane.FindAll(".data-table__row"));

            Assert.Contains("keen-bose-667825", row.TextContent);
            Assert.Empty(row.QuerySelectorAll("[data-testid='sessions-run']"));

            var detail = Assert.Single(pane.FindAll(".data-table__row-detail"));
            var cell = Assert.Single(detail.QuerySelectorAll("td"));

            // Every column, which is what makes the width the table's.
            Assert.Equal(pane.FindAll(".data-table__table thead th").Count.ToString(), cell.GetAttribute("colspan"));

            var line = detail.QuerySelector("[data-testid='sessions-run']");
            Assert.NotNull(line);

            // The library's chip over the dashboard's word, and the word made readable.
            var status = line!.QuerySelector("[data-testid='sessions-run-status']")!;
            Assert.Equal("Done", status.TextContent.Trim());

            // Under the summary's references — here there are none — and a span:
            // this run named no Backlog entry, so there is nowhere to lead.
            Assert.Same(status, line.QuerySelector(".sessions-run__summary")!.LastElementChild);
            Assert.Equal("SPAN", status.TagName);

            // The skill is the last of the figures, under the fold, wearing the
            // sparkles glyph as decoration beside the word.
            var facts = line.QuerySelector(".sessions-run__facts")!;
            var skill = facts.QuerySelector("[data-testid='sessions-run-skill']")!;

            Assert.Equal("orch-feature", skill.TextContent.Trim());
            Assert.NotNull(skill.QuerySelector("svg.sparkles-icon[aria-hidden='true']"));
            Assert.Same(skill, facts.LastElementChild);

            // Two blocks, and the summary is the first of them in the markup: the
            // work and the outcome before the figures, whatever side each takes.
            Assert.Equal(
                ["sessions-run__summary", "sessions-run__facts"],
                line.Children.Select(child => child.ClassName));

            // The run's own title is not on the line: beside a reference to the work
            // it names, it is the same sentence twice. It is in the fold.
            Assert.Null(line.QuerySelector(".sessions-run__summary [data-testid='sessions-run-title']"));

            // Nothing linked, no references.
            Assert.Empty(line.QuerySelectorAll(".integration-link"));

            // The four figures, on the fold's own trigger.
            var trigger = line.QuerySelector(".fold__trigger")!;
            Assert.Equal("2 of 3 stages done · 1.7M output tokens · context peak 41% · 45.2K tool calls", trigger.TextContent.Trim().TrimStart('▸').Trim());

            // Folded until asked.
            Assert.Equal("false", trigger.GetAttribute("aria-expanded"));

            // The session row stays a session row: no run-only line on it.
            Assert.Null(row.QuerySelector("[data-testid='sessions-run-only']"));
        });
    }

    [Fact]
    public void The_fold_opens_on_the_stages_tokens_context_and_tool_calls()
    {
        var run = SessionRowsTests.Run("run-1", Worktree, Noon.AddMinutes(-90), Noon.AddMinutes(-10)) with
        {
            Stages = [new DeliveryRunStage("Build & Test", "done", 125_000, 3)],
            TokenUsage = new DeliveryRunTokenUsage(
                new DeliveryRunTokens(66, 132, 32_875, 0, 5_586_145, 457_801),
                new DeliveryRunTokens(49, 98, 13_252, 0, 2_211_729, 416_625),
                [],
                ["claude-opus-5", "claude-sonnet-5"]),
            Context = new DeliveryRunContext(120_000, 200_000, 180_000),
            InsightsByCategory = [new("Shell", 3, 3500, 1), new("Agents", 1, 22_069, 0)],
            InsightsByServer = [new("plugin_qa_aspire", 2, 400, 0)]
        };

        using var context = Context([Live], [run]);

        var pane = context.Render<SessionsPane>();

        pane.WaitForAssertion(() => Assert.NotEmpty(pane.FindAll("[data-testid='sessions-run'] .fold__trigger")));
        pane.Find("[data-testid='sessions-run'] .fold__trigger").Click();

        pane.WaitForAssertion(() =>
        {
            var line = pane.Find("[data-testid='sessions-run']");

            Assert.Equal("true", line.QuerySelector(".fold__trigger")!.GetAttribute("aria-expanded"));

            // The run's own words, kept where they do not repeat a reference.
            Assert.Equal("A run", line.QuerySelector("[data-testid='sessions-run-title']")!.TextContent.Trim());

            var stages = line.QuerySelector("[data-testid='sessions-run-stages']")!;
            Assert.Contains("Build & Test", stages.TextContent);

            // A re-entered stage says so; a stage done once is not decorated with ×1.
            Assert.Contains("Done · 2m 5s · done ×3", stages.TextContent);

            var tokens = line.QuerySelector("[data-testid='sessions-run-tokens']")!;
            Assert.Contains("66 calls · 32.9K out", tokens.TextContent);
            Assert.Contains("Sub-agents: 49 calls", tokens.TextContent);
            Assert.Contains("claude-opus-5, claude-sonnet-5", tokens.TextContent);

            Assert.Contains("Peak 180.0K of 200.0K (90%)", line.QuerySelector("[data-testid='sessions-run-context']")!.TextContent);

            var insights = line.QuerySelector("[data-testid='sessions-run-insights']")!;
            Assert.Contains("Shell", insights.TextContent);
            Assert.Contains("3 calls · 3s · 1 failed", insights.TextContent);
            Assert.Contains("Agents", insights.TextContent);

            Assert.Contains("plugin_qa_aspire", line.QuerySelector("[data-testid='sessions-run-servers']")!.TextContent);

        });
    }

    /// <summary>
    /// The GitHub work a run links to, drawn as the Integrations family's references
    /// — the way the storybook's references page draws them — one per line, with the
    /// status under them. The Backlog entry is not among them: the status badge is
    /// how this line reaches one.
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

            // Stacked, in the order a reader asks: what the run was for, what it
            // produced, then how it ended.
            var summary = line.QuerySelector(".sessions-run__summary")!;

            Assert.Equal(
                ["sessions-run-issue", "sessions-run-pull-request", "sessions-run-status"],
                summary.Children.Select(child => child.GetAttribute("data-testid")));

            // The status never leads to the pull request above it: with no way to
            // open the entry, it is the span every badge here is.
            var status = summary.QuerySelector("[data-testid='sessions-run-status']")!;

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
            Assert.Equal(2, pane.FindAll(".data-table__row").Count);
            Assert.Equal("false", pane.Find("[data-testid='sessions-runs-only']").GetAttribute("aria-pressed"));
        });

        pane.Find("[data-testid='sessions-runs-only']").Click();

        pane.WaitForAssertion(() =>
        {
            var row = Assert.Single(pane.FindAll(".data-table__row"));

            Assert.Contains("keen-bose-667825", row.TextContent);

            // The run is under the row, across the columns.
            Assert.NotNull(Assert.Single(pane.FindAll(".data-table__row-detail")).QuerySelector("[data-testid='sessions-run']"));
            Assert.Equal("1 of 2 sessions", pane.Find("[data-testid='sessions-count']").TextContent.Trim());
        });

        pane.Find("[data-testid='sessions-runs-only']").Click();

        pane.WaitForAssertion(() => Assert.Equal(2, pane.FindAll(".data-table__row").Count));
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
            Assert.Empty(pane.FindAll(".data-table__row"));
            Assert.Contains("No delivery runs here.", pane.Markup);
            Assert.Contains("Turn off With runs to see them all.", pane.Markup);
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
            Assert.Single(pane.FindAll(".data-table__row"));
            Assert.Empty(pane.FindAll("[data-testid='sessions-run-only']"));

            // And the count says two exist, one shown — and does not call the run a
            // session.
            Assert.Equal("1 of 2 sessions and runs", pane.Find("[data-testid='sessions-count']").TextContent.Trim());
            Assert.Contains("And 1 delivery run no listed session drove, as a row of its own.", pane.Find(".sessions-panel__subtitle").TextContent);
        });

        ShowAll(pane);

        pane.WaitForAssertion(() =>
        {
            var rows = pane.FindAll(".data-table__row");

            Assert.Equal(2, rows.Count);

            // Most recently active first: the session today, the run five days ago.
            var row = rows[1];

            Assert.Contains("old-worktree", row.QuerySelector(".sessions-table__title")!.TextContent);

            var line = row.QuerySelector("[data-testid='sessions-run-only']")!;
            Assert.Equal(
                "No session record on this PC. Recorded by orch-dashboard under old-worktree-1a2b3c4d.",
                line.TextContent.Trim());

            // The run itself, under the row and across the columns; the dashboard's
            // own status on its chip, and the run's own words in the fold.
            var detail = pane.FindAll(".data-table__row-detail")[1];

            Assert.Equal("In progress", detail.QuerySelector("[data-testid='sessions-run-status']")!.TextContent.Trim());
            Assert.Equal("orch-feature", detail.QuerySelector("[data-testid='sessions-run-skill']")!.TextContent.Trim());

            // A Claude row on this machine, dated by the run, and Finished on the
            // row's own chip: the run's word is the run's, the row's state is the
            // list's, and the two say different true things.
            Assert.Contains("Claude", row.TextContent);
            Assert.Contains("DEV-TOWER", row.TextContent);
            Assert.NotEmpty(row.QuerySelectorAll(".badge--integration-finished"));

            // One table, still.
            Assert.Single(pane.FindAll("[data-testid='sessions-table-table']"));
        });
    }

    /// <summary>The list is one list under every control: a run-only row groups by
    /// the agent whose dashboard wrote it, beside the sessions.</summary>
    [Fact]
    public void A_run_only_row_groups_with_the_sessions()
    {
        var stray = SessionRowsTests.Run("run-stray", "old-worktree-1a2b3c4d", Noon.AddDays(-5), Noon.AddDays(-5).AddHours(2));

        using var context = Context([Live, Live with { Id = "copilot", Kind = AgentSessionKind.Copilot, Title = "JSdotNet/Backlog" }], [stray]);

        var pane = context.Render<SessionsPane>();

        ShowAll(pane);
        pane.Find("[data-testid='sessions-group-type']").Click();

        pane.WaitForAssertion(() =>
        {
            Assert.Equal(
                ["Claude", "Copilot"],
                pane.FindAll(".data-table__group-name").Select(name => name.TextContent.Trim()));

            // Two Claude rows — the session and the run — and the Copilot one.
            Assert.Equal(3, pane.FindAll(".data-table__row").Count);
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

        pane.WaitForAssertion(() => Assert.Single(pane.FindAll("[data-testid='sessions-run-only']")));

        pane.Find("[data-testid='sessions-machine-filter'] select").Change("laptop");

        pane.WaitForAssertion(() => Assert.Empty(pane.FindAll("[data-testid='sessions-run-only']")));

        pane.Find("[data-testid='sessions-machine-filter'] select").Change("tower");

        pane.WaitForAssertion(() => Assert.Single(pane.FindAll("[data-testid='sessions-run-only']")));
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
            var line = pane.Find("[data-testid='sessions-run']");
            var trigger = line.QuerySelector(".fold__trigger")!;
            var summary = trigger.TextContent.Trim().TrimStart('▸').Trim();

            // The stage count is measured and is shown. The three figures this run
            // does not carry are left out rather than shown as zero — the rule the
            // area already states for a dashboard's own gaps, which a run recorded
            // here is simply another case of.
            Assert.Equal("2 of 2 stages done", summary);

            Assert.DoesNotContain("0 output tokens", summary, StringComparison.Ordinal);
            Assert.DoesNotContain("context peak", summary, StringComparison.Ordinal);
            Assert.DoesNotContain("tool calls", summary, StringComparison.Ordinal);
        });

        // And the same inside the fold, which is where this first went wrong: the
        // rule held on the trigger and not under it, so opening the fold on a run
        // recorded here answered "0 calls · 0 out · 0 in · 0 cache read · 0 cache
        // write" — a measurement nobody made, contradicting the line that opened it.
        pane.Find(".fold__trigger").Click();

        pane.WaitForAssertion(() =>
        {
            var tokens = pane.Find("[data-testid='sessions-run-tokens']").TextContent;

            // The model is kept: the run states it, so it is observed rather than
            // measured, and it is the one thing in here this product does know.
            Assert.Contains("claude-opus-5", tokens, StringComparison.Ordinal);

            Assert.DoesNotContain("0 calls", tokens, StringComparison.Ordinal);
            Assert.DoesNotContain("0 out", tokens, StringComparison.Ordinal);
            Assert.DoesNotContain("cache read", tokens, StringComparison.Ordinal);
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

        pane.WaitForAssertion(() => Assert.Contains("1.7M output tokens", pane.Find(".fold__trigger").TextContent, StringComparison.Ordinal));

        pane.Find(".fold__trigger").Click();

        pane.WaitForAssertion(() =>
        {
            var tokens = pane.Find("[data-testid='sessions-run-tokens']").TextContent;

            Assert.Contains("4,789 calls", tokens, StringComparison.Ordinal);
            Assert.Contains("cache read", tokens, StringComparison.Ordinal);
            Assert.Contains("claude-opus-5", tokens, StringComparison.Ordinal);
        });
    }

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
