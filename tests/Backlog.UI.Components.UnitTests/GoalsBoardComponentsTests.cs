using Backlog.UI.Components.Goals;
using Backlog.UI.Components.Progress;

namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// The Goals view's parts as drawn (<c>.devbook/domain/progress/features.md#the-goals-view</c>
/// and <c>#the-stand-up</c>): the board's header, goals, No goal and stand-up aside; a goal
/// card folded and open with its attach search; the stacked bar; the impediment row and its
/// state picker; and the New goal dialog. Every act is a callback the host answers.
/// </summary>
public sealed class GoalsBoardComponentsTests : IDisposable
{
    private readonly BunitContext _context = new();

    public GoalsBoardComponentsTests() => _context.JSInterop.Mode = JSRuntimeMode.Loose;

    public void Dispose() => _context.Dispose();

    private static DateOnly Oct(int day) => new(2026, 10, day);

    private static readonly GoalSubject Vat = GoalSubject.ForTask(new Guid("5b0e6c11-0000-4000-8000-000000000001"));
    private static readonly GoalSubject Terms = GoalSubject.ForTask(new Guid("5b0e6c11-0000-4000-8000-000000000002"));
    private static readonly GoalSubject Widget = GoalSubject.ForTask(new Guid("5b0e6c11-0000-4000-8000-000000000003"));
    private static readonly GoalSubject Webhook = GoalSubject.ForPullRequest("JSdotNet/Backlog", 1357);

    private static readonly GoalCardModel Paid = new(
        "paid", "Customers get paid faster", Oct(30), new DateOnly(2026, 11, 6), true,
        new GoalCounts(9, 15, 3, 1, 2, 3),
        [new GoalPlanModel("Bank integration", 5, 9)],
        [
            new GoalTaskModel("t1", "Invoice PDF export", GoalTaskStatus.Done),
            new GoalTaskModel("t3", "Recurring invoices", GoalTaskStatus.Doing)
        ],
        [
            new PullRequestStatusModel(1341, "open", new PullRequestChecks(14, 0, 0), new PullRequestApprovals(1, 2)),
            new PullRequestStatusModel(1352, "merged"),
            new PullRequestStatusModel(1355, "merged")
        ]);

    private static readonly GoalCardModel Search = new(
        "search", "Devbook search ships", Oct(23), null, false, new GoalCounts(5, 6, 0, 0, 1, 0), [], [], []);

    private static readonly StandUpImpediment Bank = new(
        "bank", "Bank sample files missing", StandUpImpedimentState.Escalated,
        [StandUpImpedimentState.BeingHandled, StandUpImpedimentState.Resolved], "Account manager", 3, null, "Customers get paid faster");

    private static readonly StandUpModel StandUp = new(
        Finished:
        [
            new StandUpGroup(null, [new StandUpEntry("VAT rate per line item")]),
            new StandUpGroup("Customers get paid faster", [new StandUpEntry("Invoice PDF export")])
        ],
        Planned:
        [
            new StandUpGroup("Customers get paid faster",
            [
                new StandUpEntry("Customer search by IBAN", Effort: 8),
                new StandUpEntry("Payment status webhook", new PullRequestStatusModel(1357, "open", new PullRequestChecks(9, 0, 5)))
            ])
        ],
        PlannedEffort: 21,
        Capacity: 18,
        Impediments: [Bank],
        ResolvedInLast: 1);

    private static readonly GoalsBoardModel Board = new(
        GoalsIteration.Week,
        new GoalsWindow(Oct(12), Oct(16)),
        new GoalsWindow(Oct(19), Oct(23)),
        [Paid, Search],
        [new GoalsItem(Vat, "VAT rate per line item"), new GoalsItem(Webhook, "Payment status webhook")],
        StandUp,
        [
            new GoalsItem(GoalSubject.ForPlan("pay"), "Payment provider switch"),
            new GoalsItem(Terms, "Payment terms on quotes"),
            new GoalsItem(Webhook, "Payment status webhook"),
            new GoalsItem(Widget, "Quick-capture widget")
        ]);

    private IRenderedComponent<GoalsBoard> RenderBoard(
        GoalsBoardModel? model = null,
        Action<ComponentParameterCollectionBuilder<GoalsBoard>>? more = null) =>
        _context.Render<GoalsBoard>(p =>
        {
            p.Add(c => c.Model, model ?? Board);
            more?.Invoke(p);
        });

    [Fact]
    public void The_header_reads_Goals_with_the_switch_the_window_and_its_acts()
    {
        var board = RenderBoard();

        Assert.Equal("Goals", board.Find("h1").TextContent);
        var options = board.FindAll("[role='radiogroup'][aria-label='Iteration'] [role='radio']");
        Assert.Equal(["Day", "Week", "2 weeks", "Month"], options.Select(o => o.TextContent.Trim()));
        Assert.Equal("true", options[1].GetAttribute("aria-checked"));
        Assert.Equal("12–16 Oct → 19–23 Oct", board.Find("[data-testid='goals-board-window']").TextContent);
        Assert.Equal("New goal", board.Find("[data-testid='goals-board-new-goal']").TextContent.Trim());

        var copy = board.Find("[data-testid='goals-board-copy-button']");
        Assert.Equal("Copy", copy.GetAttribute("aria-label"));
        Assert.Equal(string.Empty, copy.TextContent.Trim());

        var refresh = board.Find("[data-testid='goals-board-refresh']");
        Assert.Equal("Refresh", refresh.GetAttribute("aria-label"));
        Assert.NotNull(refresh.QuerySelector("svg.refresh-cw-icon"));
    }

    [Fact]
    public void Picking_an_iteration_or_pressing_new_goal_or_refresh_calls_the_host()
    {
        GoalsIteration? picked = null;
        var newGoal = 0;
        var refreshed = 0;
        var board = RenderBoard(more: p => p
            .Add(c => c.IterationChanged, (GoalsIteration length) => picked = length)
            .Add(c => c.OnNewGoal, () => newGoal++)
            .Add(c => c.OnRefresh, () => refreshed++));

        board.Find("[data-testid='goals-board-iteration-twoweeks']").Click();
        board.Find("[data-testid='goals-board-new-goal']").Click();
        board.Find("[data-testid='goals-board-refresh']").Click();

        Assert.Equal(GoalsIteration.TwoWeeks, picked);
        Assert.Equal(1, newGoal);
        Assert.Equal(1, refreshed);
    }

    [Fact]
    public void The_board_holds_the_goals_column_and_the_stand_up_aside()
    {
        var board = RenderBoard();

        Assert.NotNull(board.Find("[data-testid='goals-board-goals']"));
        Assert.NotNull(board.Find("aside[aria-label='Stand-up']"));
        Assert.Empty(board.FindAll("[data-testid='goals-view-empty']"));
    }

    [Fact]
    public void The_first_goal_opens_when_the_board_first_draws_and_another_opens_on_its_title()
    {
        var board = RenderBoard();

        Assert.Equal("true", board.Find("[data-testid='goals-board-goal-paid']").GetAttribute("data-expanded"));
        Assert.Equal("false", board.Find("[data-testid='goals-board-goal-search']").GetAttribute("data-expanded"));

        board.Find("[data-testid='goals-board-goal-search-toggle']").Click();

        board.WaitForAssertion(() =>
        {
            Assert.Equal("false", board.Find("[data-testid='goals-board-goal-paid']").GetAttribute("data-expanded"));
            Assert.Equal("true", board.Find("[data-testid='goals-board-goal-search']").GetAttribute("data-expanded"));
            Assert.Equal("true", board.Find("[data-testid='goals-board-goal-search-toggle']").GetAttribute("aria-expanded"));
        });
    }

    /// <summary>A host bound to the selection hears the goal the board opened on its own.</summary>
    [Fact]
    public void Opening_the_first_goal_on_its_own_tells_the_host()
    {
        var heard = new List<string?>();
        RenderBoard(more: p => p.Add(c => c.SelectedGoalIdChanged, (string? id) => heard.Add(id)));

        Assert.Equal(["paid"], heard);
    }

    [Fact]
    public void A_goal_card_reads_its_line_and_forecast_and_its_bar_in_words()
    {
        var card = _context.Render<GoalCard>(p => p.Add(c => c.Goal, Paid).Add(c => c.TestId, "goal"));

        Assert.Equal("Customers get paid faster", card.Find("h3").TextContent.Trim());
        Assert.Equal("9/15 · +3 · due 30 Oct · lands ~6 Nov", card.Find("[data-testid='goal-meta']").TextContent);

        var badge = card.Find("[data-testid='goal-forecast']");
        Assert.Equal("At risk", badge.TextContent);
        Assert.Contains("badge--forecast-at-risk", badge.ClassList);

        var bar = card.Find("[data-testid='goal-bar']");
        Assert.Equal("img", bar.GetAttribute("role"));
        Assert.Equal("9 done, 3 in progress, 1 blocked, 2 not started", bar.GetAttribute("aria-label"));

        // Folded: no columns, no search.
        Assert.Empty(card.FindAll("[data-testid='goal-plans']"));
    }

    [Fact]
    public void An_on_track_goal_wears_the_settled_badge()
    {
        var card = _context.Render<GoalCard>(p => p.Add(c => c.Goal, Search).Add(c => c.TestId, "goal"));

        var badge = card.Find("[data-testid='goal-forecast']");
        Assert.Equal("On track", badge.TextContent);
        Assert.Contains("badge--forecast-on-track", badge.ClassList);
        Assert.Equal("5/6 · due 23 Oct", card.Find("[data-testid='goal-meta']").TextContent);
    }

    [Fact]
    public void An_open_goal_lists_its_plan_tasks_and_pull_requests_with_merged_ones_gathered()
    {
        var card = _context.Render<GoalCard>(p => p.Add(c => c.Goal, Paid).Add(c => c.Expanded, true).Add(c => c.TestId, "goal"));

        var plans = card.Find("[data-testid='goal-plans']");
        Assert.Contains("Bank integration", plans.TextContent);
        Assert.Contains("5/9", plans.TextContent);

        var tasks = card.FindAll("[data-testid='goal-tasks'] .goal-card__task");
        Assert.Equal(2, tasks.Count);
        Assert.Contains("status-dot--done", tasks[0].QuerySelector(".status-dot")!.ClassList);
        Assert.Contains("status-dot--active", tasks[1].QuerySelector(".status-dot")!.ClassList);

        Assert.NotNull(card.Find("[data-testid='goal-pull-request-1341'].pull-request-status"));
        var merged = card.Find("[data-testid='goal-merged']");
        Assert.Equal(["#1352", "#1355"], merged.QuerySelectorAll(".badge--gh-merged").Select(b => b.TextContent));
        Assert.EndsWith("merged", merged.TextContent.Trim(), StringComparison.Ordinal);
    }

    [Fact]
    public void The_attach_search_lists_matches_under_their_kind_and_hands_back_the_pick()
    {
        GoalSubject? attached = null;
        var card = _context.Render<GoalCard>(p => p
            .Add(c => c.Goal, Paid)
            .Add(c => c.Expanded, true)
            .Add(c => c.Candidates, Board.Candidates)
            .Add(c => c.OnAttach, (GoalSubject subject) => attached = subject)
            .Add(c => c.TestId, "goal"));

        var input = card.Find("[data-testid='goal-attach'] input");
        Assert.Equal("Attach a plan, task or pull request", input.GetAttribute("aria-label"));
        Assert.Empty(card.FindAll("[data-testid='goal-matches']"));

        input.Input("pay");

        card.WaitForAssertion(() =>
        {
            var matches = card.FindAll("[data-testid='goal-matches'] .goal-card__match");
            Assert.Equal(["Plan", "Task", "PR"], matches.Select(m => m.QuerySelector(".goal-card__match-kind")!.TextContent));
            Assert.Equal("#1357 Payment status webhook", Normalise(matches[2].QuerySelector(".goal-card__match-title")!.TextContent));
        });

        card.Find($"[data-testid='goal-match-{Terms.Key}']").Click();

        card.WaitForAssertion(() =>
        {
            Assert.Equal(Terms, attached);
            Assert.Empty(card.FindAll("[data-testid='goal-matches']"));
            // The picked button went with the list, so the focus goes back to the search
            // box, the one element the card holds a reference to.
            Assert.Single(_context.JSInterop.Invocations["Blazor._internal.domWrapper.focus"]);
        });
    }

    [Fact]
    public void A_pull_request_is_matched_by_its_number()
    {
        Assert.Equal([Webhook], GoalCard.Match(Board.Candidates, "#135").Select(m => m.Subject));
        Assert.Empty(GoalCard.Match(Board.Candidates, "  "));
    }

    [Fact]
    public void No_goal_lists_what_no_goal_holds_and_attaches_to_the_open_goal()
    {
        GoalAttachRequest? request = null;
        var board = RenderBoard(more: p => p.Add(c => c.OnAttach, (GoalAttachRequest r) => request = r));

        var noGoal = board.Find("[data-testid='goals-board-no-goal']");
        Assert.Equal("No goal", noGoal.QuerySelector("h3")!.TextContent);
        Assert.Contains("VAT rate per line item", noGoal.TextContent);

        // A pull request reads its number, then its title, apart.
        var pull = board.Find("[data-kind='PR'] .goals-board__no-goal-item");
        Assert.Equal("#1357 Payment status webhook", Normalise(pull.TextContent));
        Assert.StartsWith("#1357 ", pull.TextContent.TrimStart(), StringComparison.Ordinal);

        var attach = board.Find($"[data-testid='goals-board-attach-{Vat.Key}']");
        Assert.Equal("Attach…", attach.TextContent.Trim());
        Assert.Equal("Attach to Customers get paid faster", attach.GetAttribute("title"));

        attach.Click();

        board.WaitForAssertion(() => Assert.Equal(new GoalAttachRequest("paid", Vat), request));
    }

    [Fact]
    public void With_no_goal_open_attach_hands_the_work_to_the_goal_picker()
    {
        GoalAttachRequest? request = null;
        GoalSubject? picking = null;
        var board = RenderBoard(more: p => p
            .Add(c => c.SelectedGoalId, (string?)null)
            .Add(c => c.OnAttach, (GoalAttachRequest r) => request = r)
            .Add(c => c.OnPickGoal, (GoalSubject subject) => picking = subject));

        // Fold the open goal.
        board.Find("[data-testid='goals-board-goal-paid-toggle']").Click();
        board.WaitForAssertion(() => Assert.Equal("Attach to a goal", board.Find($"[data-testid='goals-board-attach-{Webhook.Key}']").GetAttribute("title")));

        board.Find($"[data-testid='goals-board-attach-{Webhook.Key}']").Click();

        // The host opens the shared GoalPicker; the board keeps no menu of its own.
        board.WaitForAssertion(() => Assert.Equal(Webhook, picking));
        Assert.Null(request);
        Assert.Empty(board.FindAll("[role='menu']"));
    }

    [Fact]
    public void No_goals_and_nothing_outside_one_is_a_real_empty_state()
    {
        var newGoal = 0;
        var board = RenderBoard(Board with { Goals = [], NoGoal = [] }, p => p.Add(c => c.OnNewGoal, () => newGoal++));

        var empty = board.Find("[data-testid='goals-view-empty']");
        Assert.Equal("No goals yet", empty.QuerySelector(".empty-state__title")!.TextContent);
        Assert.NotNull(board.Find("aside[aria-label='Stand-up']"));

        board.Find("[data-testid='goals-board-empty-new-goal']").Click();
        Assert.Equal(1, newGoal);
    }

    [Fact]
    public void A_refusal_shows_in_an_alert()
    {
        var board = RenderBoard(more: p => p.Add(c => c.Error, "Moves go forward only."));

        Assert.Equal("Moves go forward only.", board.Find("[data-testid='goals-board-error']").TextContent.Trim());
    }

    [Fact]
    public void The_stand_up_heads_each_block_with_its_count_and_tags_planned_with_points()
    {
        var panel = _context.Render<StandUpPanel>(p => p.Add(c => c.StandUp, StandUp).Add(c => c.TestId, "su"));

        Assert.Equal(["Finished", "Planned", "Impediments"], panel.FindAll("h3").Select(h => h.TextContent));
        Assert.Equal("2", panel.Find("[data-testid='su-finished-count']").TextContent);
        Assert.Equal("2", panel.Find("[data-testid='su-planned-count']").TextContent);
        Assert.Equal("1", panel.Find("[data-testid='su-impediments-count']").TextContent);

        var points = panel.Find("[data-testid='su-points']");
        Assert.Equal("21 / 18 pts", points.TextContent);
        Assert.Equal("21 points planned, you usually finish 18 a week", points.GetAttribute("title"));

        Assert.Equal("1 resolved", panel.Find("[data-testid='su-resolved']").TextContent);
    }

    [Fact]
    public void The_goals_heading_carries_the_count_of_goals()
    {
        var board = RenderBoard();

        var heading = board.Find("[data-testid='goals-board-goals'] h2");
        Assert.Equal("Goals", heading.TextContent.Trim());
        Assert.Equal("2", board.Find("[data-testid='goals-board-goals-count']").TextContent.Trim());

        var empty = RenderBoard(Board with { Goals = [], NoGoal = [] });
        Assert.Equal("0", empty.Find("[data-testid='goals-board-goals-count']").TextContent.Trim());
    }

    [Fact]
    public void A_planned_item_shows_its_effort_in_points_and_one_without_shows_none()
    {
        var panel = _context.Render<StandUpPanel>(p => p.Add(c => c.StandUp, StandUp).Add(c => c.TestId, "su"));

        var items = panel.FindAll("[data-testid='su-planned'] .stand-up-panel__item");
        Assert.Equal("8 pts", items[0].QuerySelector("[data-testid='su-effort']")!.TextContent);
        Assert.Contains("Customer search by IBAN", items[0].TextContent);
        Assert.Null(items[1].QuerySelector("[data-testid='su-effort']"));
        Assert.Empty(panel.FindAll("[data-testid='su-finished'] [data-testid='su-effort']"));
    }

    [Fact]
    public void A_finished_item_no_goal_holds_is_last_and_reads_no_goal()
    {
        var panel = _context.Render<StandUpPanel>(p => p.Add(c => c.StandUp, StandUp).Add(c => c.TestId, "su"));

        var items = panel.FindAll("[data-testid='su-finished'] .stand-up-panel__item");
        Assert.Equal("Invoice PDF export", items[0].TextContent.Trim());
        Assert.Equal("VAT rate per line item · no goal", items[1].TextContent.Trim());
    }

    [Fact]
    public void A_planned_pull_request_carries_its_status_line()
    {
        var panel = _context.Render<StandUpPanel>(p => p.Add(c => c.StandUp, StandUp).Add(c => c.TestId, "su"));

        var pull = panel.Find("[data-testid='su-planned'] .stand-up-panel__pull");
        Assert.NotNull(pull.QuerySelector(".pull-request-status [data-mark='checks']"));
        Assert.Contains("Payment status webhook", pull.TextContent);
    }

    [Fact]
    public void Without_a_pace_the_points_tag_has_no_title()
    {
        var panel = _context.Render<StandUpPanel>(p => p.Add(c => c.StandUp, StandUp with { Capacity = null }).Add(c => c.TestId, "su"));

        var points = panel.Find("[data-testid='su-points']");
        Assert.Equal("21 pts", points.TextContent);
        Assert.False(points.HasAttribute("title"));
    }

    [Fact]
    public void An_impediment_row_names_who_it_is_with_its_age_and_its_goal_in_the_error_ink()
    {
        var row = _context.Render<ImpedimentRow>(p => p.Add(c => c.Impediment, Bank).Add(c => c.TestId, "imp"));

        Assert.Equal("Bank sample files missing", row.Find(".impediment-row__title").TextContent);
        Assert.Equal("Account manager · 3 days · Customers get paid faster", Normalise(row.Find("[data-testid='imp-meta']").TextContent));
        Assert.Equal("Customers get paid faster", row.Find(".impediment-row__goal").TextContent);
    }

    [Fact]
    public void The_state_picker_wears_the_states_badge_and_offers_only_its_moves()
    {
        var row = _context.Render<ImpedimentRow>(p => p.Add(c => c.Impediment, Bank).Add(c => c.TestId, "imp"));

        var picker = row.Find("[data-testid='imp-state']");
        Assert.Contains("badge--impediment-escalated", picker.ClassList);
        var select = picker.QuerySelector("select")!;
        Assert.Equal("State", select.GetAttribute("aria-label"));
        Assert.Equal(["Escalated", "Being handled", "Resolved"], select.QuerySelectorAll("option").Select(o => o.TextContent));
    }

    [Theory]
    [InlineData(StandUpImpedimentState.New, "badge--impediment-new")]
    [InlineData(StandUpImpedimentState.Reported, "badge--impediment-reported")]
    [InlineData(StandUpImpedimentState.BeingHandled, "badge--impediment-being-handled")]
    [InlineData(StandUpImpedimentState.Resolved, "badge--impediment-resolved")]
    public void Each_state_wears_its_own_badge(StandUpImpedimentState state, string badge)
    {
        var row = _context.Render<ImpedimentRow>(p => p.Add(c => c.Impediment, Bank with { State = state }).Add(c => c.TestId, "imp"));

        Assert.Contains(badge, row.Find("[data-testid='imp-state']").ClassList);
    }

    [Fact]
    public void Resolved_offers_no_move_and_the_picker_waits()
    {
        var row = _context.Render<ImpedimentRow>(p => p
            .Add(c => c.Impediment, Bank with { State = StandUpImpedimentState.Resolved, Moves = [] })
            .Add(c => c.TestId, "imp"));

        Assert.True(row.Find("[data-testid='imp-state'] select").HasAttribute("disabled"));
    }

    [Fact]
    public void Picking_a_state_hands_the_move_back_and_the_picker_shows_the_stored_state_after()
    {
        var moves = new List<StandUpImpedimentState>();
        var row = _context.Render<ImpedimentRow>(p => p
            .Add(c => c.Impediment, Bank)
            .Add(c => c.OnMove, (StandUpImpedimentState state) => moves.Add(state))
            .Add(c => c.TestId, "imp"));

        var picker = row.FindComponent<BadgeSelect>().Instance;
        row.Find("[data-testid='imp-state'] select").Change(nameof(StandUpImpedimentState.Resolved));

        row.WaitForAssertion(() =>
        {
            Assert.Equal([StandUpImpedimentState.Resolved], moves);
            // Refused by a host that kept the stored state: the picker shows it again.
            var after = row.Find("[data-testid='imp-state'] select");
            Assert.Equal(nameof(StandUpImpedimentState.Escalated), after.GetAttribute("value"));
            Assert.Contains("badge--impediment-escalated", row.Find("[data-testid='imp-state']").ClassList);
        });

        // The same select, not a new one: a keyboard user's focus stays on it.
        Assert.Same(picker, row.FindComponent<BadgeSelect>().Instance);
    }

    /// <summary>The note shows under the meta line as written; without a host listening
    /// it is text alone, with no button that would do nothing.</summary>
    [Fact]
    public void A_note_shows_under_the_meta_line_and_without_a_host_it_cannot_be_edited()
    {
        var row = _context.Render<ImpedimentRow>(p => p
            .Add(c => c.Impediment, Bank with { Note = "QA12 phone note\nAsk for CAMT.053 too" })
            .Add(c => c.TestId, "imp"));

        Assert.Equal("QA12 phone note\nAsk for CAMT.053 too", row.Find("[data-testid='imp-note']").TextContent);
        Assert.Empty(row.FindAll("[data-testid='imp-note-edit']"));

        var bare = _context.Render<ImpedimentRow>(p => p.Add(c => c.Impediment, Bank).Add(c => c.TestId, "bare"));
        Assert.Empty(bare.FindAll(".impediment-row__note-line"));
    }

    [Fact]
    public void Without_a_note_the_row_offers_to_add_one_named_for_the_impediment()
    {
        var row = RenderEditable(Bank, _ => { });

        Assert.Empty(row.FindAll("[data-testid='imp-note']"));
        var button = row.Find("[data-testid='imp-note-edit']");
        Assert.Equal("Add note", button.TextContent.Trim());
        Assert.Equal("Add note to Bank sample files missing", button.GetAttribute("aria-label"));
    }

    /// <summary>Leaving the box hands the note back trimmed, and the focus stays wherever the
    /// person took it: it is not pulled back to the button.</summary>
    [Fact]
    public void Leaving_the_box_hands_the_note_back_trimmed_and_leaves_the_focus_where_it_went()
    {
        var notes = new List<string?>();
        var row = RenderEditable(Bank with { Note = "QA12 phone note" }, note => notes.Add(note));

        var button = row.Find("[data-testid='imp-note-edit']");
        Assert.Equal("Edit note", button.TextContent.Trim());
        button.Click();

        var box = row.Find("[data-testid='imp-note-editor'] textarea");
        Assert.Equal("QA12 phone note", box.GetAttribute("value"));
        row.WaitForAssertion(() => Assert.Contains(box.Id!, Focused));
        box.Input("  Samples promised for Tuesday  ");
        row.Find(".impediment-row__note-editor").FocusOut();

        row.WaitForAssertion(() => Assert.Equal(["Samples promised for Tuesday"], notes));
        Assert.Empty(row.FindAll("[data-testid='imp-note-editor']"));
        Assert.DoesNotContain(row.Find("[data-testid='imp-note-edit']").Id, Focused);
    }

    /// <summary>Closed by a key, the box gives the focus back to the button; a commit by
    /// Ctrl+Enter is handed back once, even when the closing box raises a focusout after.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_key_that_closes_the_box_puts_the_focus_back_on_the_button(bool commit)
    {
        var notes = new List<string?>();
        var row = RenderEditable(Bank with { Note = "QA12 phone note" }, note => notes.Add(note));

        row.Find("[data-testid='imp-note-edit']").Click();
        row.Find("[data-testid='imp-note-editor'] textarea").Input("Call the bank");
        var editor = row.Find(".impediment-row__note-editor");
        editor.KeyDown(commit ? new KeyboardEventArgs { Key = "Enter", CtrlKey = true } : new KeyboardEventArgs { Key = "Escape" });

        row.WaitForAssertion(() => Assert.Equal(row.Find("[data-testid='imp-note-edit']").Id, Focused[^1]));
        Assert.Equal(commit ? ["Call the bank"] : [], notes);
    }

    /// <summary>The Escape that abandons the draft stops at the box, so a surface around the
    /// row that closes on Escape does not close with it.</summary>
    [Fact]
    public void Escape_in_the_box_does_not_reach_the_surface_around_the_row()
    {
        var host = _context.Render<Harnesses.ImpedimentNoteEscapeHarness>(p => p.Add(c => c.Impediment, Bank));

        host.Find("[data-testid='imp-note-edit']").Click();
        host.Find(".impediment-row__note-editor").KeyDown(new KeyboardEventArgs { Key = "Escape" });

        Assert.Empty(host.FindAll("[data-testid='imp-note-editor']"));
        Assert.Empty(host.Instance.HostKeys);

        // With the box closed the next Escape is the surface's.
        host.Find("[data-testid='host']").KeyDown(new KeyboardEventArgs { Key = "Escape" });
        Assert.Equal(["Escape"], host.Instance.HostKeys);
    }

    /// <summary>While the host saves the note the row shows the note handed back, and its
    /// button is busy rather than disabled, so the focus on it stays.</summary>
    [Fact]
    public void While_the_note_is_saved_the_row_shows_it_and_the_button_stays_focusable()
    {
        var saving = new TaskCompletionSource();
        var row = _context.Render<ImpedimentRow>(p => p
            .Add(c => c.Impediment, Bank)
            .Add(c => c.OnEditNote, (string? _) => saving.Task)
            .Add(c => c.TestId, "imp"));

        row.Find("[data-testid='imp-note-edit']").Click();
        row.Find("[data-testid='imp-note-editor'] textarea").Input("Call the bank");
        row.Find(".impediment-row__note-editor").KeyDown(new KeyboardEventArgs { Key = "Enter", CtrlKey = true });

        row.WaitForAssertion(() => Assert.Equal("Call the bank", row.Find("[data-testid='imp-note']").TextContent));
        var button = row.Find("[data-testid='imp-note-edit']");
        Assert.False(button.HasAttribute("disabled"));
        Assert.Equal("true", button.GetAttribute("aria-busy"));

        saving.SetResult();
        row.WaitForAssertion(() => Assert.Empty(row.FindAll("[data-testid='imp-note']")));
    }

    /// <summary>A note being saved in the stand-up keeps its own row enabled while the
    /// others wait, as a move does.</summary>
    [Fact]
    public void While_a_note_is_saved_the_other_rows_wait_and_its_own_stays_enabled()
    {
        var saving = new TaskCompletionSource();
        var standUp = StandUp with { Impediments = [Bank, Runner] };
        var panel = _context.Render<StandUpPanel>(p => p
            .Add(c => c.StandUp, standUp)
            .Add(c => c.OnEditImpedimentNote, (ImpedimentNoteRequest _) => saving.Task)
            .Add(c => c.TestId, "su"));

        panel.Find("[data-testid='su-impediment-bank-note-edit']").Click();
        panel.Find("[data-testid='su-impediment-bank-note-editor'] textarea").Input("Call the bank");
        panel.Find(".impediment-row__note-editor").KeyDown(new KeyboardEventArgs { Key = "Enter", CtrlKey = true });
        panel.Render(p => p.Add(c => c.Busy, true));

        Assert.False(panel.Find("[data-testid='su-impediment-bank-note-edit']").HasAttribute("disabled"));
        Assert.True(panel.Find("[data-testid='su-impediment-runner-note-edit']").HasAttribute("disabled"));

        saving.SetResult();
    }

    [Fact]
    public void Ctrl_Enter_hands_the_note_back_and_a_blank_one_clears_it()
    {
        var notes = new List<string?>();
        var row = RenderEditable(Bank with { Note = "QA12 phone note" }, note => notes.Add(note));

        row.Find("[data-testid='imp-note-edit']").Click();
        row.Find("[data-testid='imp-note-editor'] textarea").Input("   ");
        row.Find(".impediment-row__note-editor").KeyDown(new KeyboardEventArgs { Key = "Enter", CtrlKey = true });

        row.WaitForAssertion(() => Assert.Equal([(string?)null], notes));
    }

    [Fact]
    public void Escape_abandons_the_draft_and_an_unchanged_note_is_not_handed_back()
    {
        var notes = new List<string?>();
        var row = RenderEditable(Bank with { Note = "QA12 phone note" }, note => notes.Add(note));

        row.Find("[data-testid='imp-note-edit']").Click();
        row.Find("[data-testid='imp-note-editor'] textarea").Input("Something else");
        row.Find(".impediment-row__note-editor").KeyDown(new KeyboardEventArgs { Key = "Escape" });

        Assert.Empty(row.FindAll("[data-testid='imp-note-editor']"));
        Assert.Equal("QA12 phone note", row.Find("[data-testid='imp-note']").TextContent);

        row.Find("[data-testid='imp-note-edit']").Click();
        row.Find("[data-testid='imp-note-editor'] textarea").Input(" QA12 phone note ");
        row.Find(".impediment-row__note-editor").FocusOut();

        Assert.Empty(row.FindAll("[data-testid='imp-note-editor']"));
        Assert.Empty(notes);
    }

    [Fact]
    public void A_note_from_the_board_names_the_impediment()
    {
        ImpedimentNoteRequest? request = null;
        var board = RenderBoard(more: p => p.Add(c => c.OnEditImpedimentNote, (ImpedimentNoteRequest r) => request = r));

        board.Find("[data-testid='goals-board-stand-up-impediment-bank-note-edit']").Click();
        board.Find("[data-testid='goals-board-stand-up-impediment-bank-note-editor'] textarea").Input("Call the bank");
        board.Find(".impediment-row__note-editor").FocusOut();

        board.WaitForAssertion(() => Assert.Equal(new ImpedimentNoteRequest("bank", "Call the bank"), request));
    }

    private IRenderedComponent<ImpedimentRow> RenderEditable(StandUpImpediment impediment, Action<string?> onNote) =>
        _context.Render<ImpedimentRow>(p => p
            .Add(c => c.Impediment, impediment)
            .Add(c => c.OnEditNote, onNote)
            .Add(c => c.TestId, "imp"));

    private static readonly StandUpImpediment Runner = new(
        "runner", "Android build runner flaky", StandUpImpedimentState.BeingHandled,
        [StandUpImpedimentState.Resolved], "Platform team", 4, null, null);

    private IReadOnlyList<string> Focused =>
        [.. _context.JSInterop.Invocations.Where(i => i.Identifier == "backlogFocus").Select(i => (string)i.Arguments[0]!)];

    /// <summary>A resolved impediment leaves the list with the focused picker inside it, so
    /// the focus moves on: to the next impediment's picker, or to the resolved line once the
    /// list is empty. A refused resolve keeps the row, and the focus stays where it was.</summary>
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void After_a_resolve_the_focus_moves_on_and_after_a_refusal_it_stays(bool others, bool refused)
    {
        var standUp = StandUp with { Impediments = others ? [Bank, Runner] : [Bank], ResolvedInLast = 0 };
        var panel = _context.Render<StandUpPanel>(p => p.Add(c => c.StandUp, standUp).Add(c => c.TestId, "su"));

        panel.Find("[data-testid='su-impediment-bank-state'] select").Change(nameof(StandUpImpedimentState.Resolved));

        // The host reads again: the impediment is gone, and one more was resolved.
        var after = refused
            ? standUp
            : standUp with { Impediments = others ? [Runner] : [], ResolvedInLast = 1 };
        panel.Render(p => p.Add(c => c.StandUp, after));

        if (refused)
        {
            panel.WaitForAssertion(() => Assert.Empty(Focused));
            return;
        }

        // The impediment that took its place, or the resolved line once none is left.
        var target = others
            ? panel.Find("[data-testid='su-impediment-runner-state'] select")
            : panel.Find("[data-testid='su-resolved']");
        panel.WaitForAssertion(() => Assert.Equal([target.Id!], Focused));
        if (!others) Assert.Equal("-1", target.GetAttribute("tabindex"));
    }

    [Fact]
    public void A_move_from_the_board_names_the_impediment()
    {
        ImpedimentMoveRequest? request = null;
        var board = RenderBoard(more: p => p.Add(c => c.OnMoveImpediment, (ImpedimentMoveRequest r) => request = r));

        board.Find("[data-testid='goals-board-stand-up-impediment-bank-state'] select").Change(nameof(StandUpImpedimentState.BeingHandled));

        board.WaitForAssertion(() => Assert.Equal(new ImpedimentMoveRequest("bank", StandUpImpedimentState.BeingHandled), request));
    }

    [Fact]
    public void The_progress_bar_grows_each_segment_by_its_count()
    {
        var bar = _context.Render<GoalProgressBar>(p => p.Add(c => c.Counts, new GoalCounts(9, 15, 3, 1, 2, 0)));

        Assert.Equal("flex: 9 1 0%", bar.Find(".goal-progress-bar__done").GetAttribute("style"));
        Assert.Equal("flex: 3 1 0%", bar.Find(".goal-progress-bar__doing").GetAttribute("style"));
        Assert.Equal("flex: 1 1 0%", bar.Find(".goal-progress-bar__blocked").GetAttribute("style"));
        Assert.Equal("flex: 2 1 0%", bar.Find(".goal-progress-bar__todo").GetAttribute("style"));
    }

    /// <summary>A segment with nothing in it is not drawn, so it takes no gap: a goal all in
    /// progress is one solid segment.</summary>
    [Fact]
    public void Only_segments_with_items_are_drawn()
    {
        var bar = _context.Render<GoalProgressBar>(p => p.Add(c => c.Counts, new GoalCounts(0, 4, 4, 0, 0, 0)));

        var segment = Assert.Single(bar.Find(".goal-progress-bar").Children);
        Assert.Contains("goal-progress-bar__doing", segment.ClassList);
        Assert.Equal("flex: 4 1 0%", segment.GetAttribute("style"));
    }

    [Fact]
    public void An_empty_goal_draws_the_track_alone()
    {
        var bar = _context.Render<GoalProgressBar>(p => p.Add(c => c.Counts, new GoalCounts(0, 0, 0, 0, 0, 0)));

        Assert.Empty(bar.Find(".goal-progress-bar").Children);
    }

    [Fact]
    public void The_new_goal_dialog_hands_back_the_draft_trimmed()
    {
        NewGoalDraft? draft = null;
        var dialog = _context.Render<GoalCreateDialog>(p => p
            .Add(c => c.Open, true)
            .Add(c => c.OnSave, (NewGoalDraft d) => draft = d));

        dialog.Find("[data-testid='goal-create-dialog-title'] input").Input("  Customers get paid faster ");
        dialog.Find("[data-testid='goal-create-dialog-outcome'] input").Input("   ");
        dialog.Find("[data-testid='goal-create-dialog-due'] input").Input("2026-10-30");
        dialog.Find("[data-testid='goal-create-dialog-save']").Click();

        dialog.WaitForAssertion(() => Assert.Equal(new NewGoalDraft("Customers get paid faster", null, Oct(30)), draft));
    }

    [Fact]
    public void The_new_goal_dialog_shows_the_modules_refusal()
    {
        var dialog = _context.Render<GoalCreateDialog>(p => p
            .Add(c => c.Open, true)
            .Add(c => c.Error, "A goal needs a title."));

        Assert.Equal("A goal needs a title.", dialog.Find("[data-testid='goal-create-dialog-error']").TextContent.Trim());
        Assert.Equal("date", dialog.Find("[data-testid='goal-create-dialog-due'] input").GetAttribute("type"));
    }

    [Fact]
    public void The_two_week_start_dialog_opens_on_the_offered_day_and_hands_back_the_one_given()
    {
        DateOnly? saved = null;
        var dialog = _context.Render<TwoWeekStartDialog>(p => p
            .Add(c => c.Open, true)
            .Add(c => c.Offered, Oct(19))
            .Add(c => c.OnSave, (DateOnly day) => saved = day));

        var field = dialog.Find("[data-testid='two-week-start-dialog-start'] input");
        Assert.Equal("date", field.GetAttribute("type"));
        Assert.Equal("2026-10-19", field.GetAttribute("value"));

        dialog.Find("[data-testid='two-week-start-dialog-start'] input").Input("2026-10-05");
        dialog.Find("[data-testid='two-week-start-dialog-save']").Click();

        dialog.WaitForAssertion(() => Assert.Equal(Oct(5), saved));
    }

    [Fact]
    public void The_two_week_start_dialog_refuses_a_blank_day_and_keeps_nothing()
    {
        var saves = 0;
        var dialog = _context.Render<TwoWeekStartDialog>(p => p
            .Add(c => c.Open, true)
            .Add(c => c.Offered, Oct(19))
            .Add(c => c.OnSave, (DateOnly _) => saves++));

        dialog.Find("[data-testid='two-week-start-dialog-start'] input").Input("");
        dialog.Find("[data-testid='two-week-start-dialog-save']").Click();

        dialog.WaitForAssertion(() => Assert.Equal(
            "Two-week iterations need a day to count from.",
            dialog.Find("[data-testid='two-week-start-dialog-error']").TextContent.Trim()));
        Assert.Equal(0, saves);
    }

    private static string Normalise(string text) => string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
