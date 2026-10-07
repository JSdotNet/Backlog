using Backlog.UI.Components.Integrations;
using Backlog.UI.Components.Tasks;

namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// The In progress view's section rules and the board that draws them: what needs
/// the person, what is moving, what the two header toggles change, and that a task
/// or loose piece of work hands the host what was pressed.
/// </summary>
public sealed class InProgressBoardTests
{
    private static readonly WorkSession Quiet = new("quiet", "Quiet one", IntegrationSessionState.Stalled, "Last activity 42 min ago");
    private static readonly WorkSession Running = new("running", "Running one", IntegrationSessionState.Running);
    private static readonly WorkSession Finished = new("finished", "Finished one", IntegrationSessionState.Finished);

    private static WorkPullRequest Pull(
        int number,
        IntegrationArtifactState state = IntegrationArtifactState.Open,
        IntegrationReviewState review = IntegrationReviewState.None,
        IntegrationCheckState checks = IntegrationCheckState.Passing) =>
        new($"o/r#{number}", number, $"Pull {number}", state, review, checks);

    private static InProgressTask Item(string id, IReadOnlyList<WorkSession>? sessions = null, IReadOnlyList<WorkPullRequest>? pulls = null, bool finished = false) =>
        new(id, $"Task {id}", sessions ?? [], pulls ?? [], Finished: finished);

    // --- The rules ------------------------------------------------------------

    [Fact]
    public void A_quiet_session_needs_you()
    {
        Assert.True(Item("a", [Quiet]).NeedsYou);
        Assert.True(Item("a", [new WorkSession("w", "Waiting", IntegrationSessionState.Waiting)]).NeedsYou);
        Assert.False(Item("a", [Running, Finished]).NeedsYou);
    }

    [Fact]
    public void A_failing_check_or_requested_changes_on_an_open_pull_request_needs_you()
    {
        Assert.True(Item("a", pulls: [Pull(1, checks: IntegrationCheckState.Failing)]).NeedsYou);
        Assert.True(Item("a", pulls: [Pull(1, IntegrationArtifactState.Draft, IntegrationReviewState.ChangesRequested)]).NeedsYou);
        Assert.False(Item("a", pulls: [Pull(1, review: IntegrationReviewState.Approved), Pull(2, checks: IntegrationCheckState.Pending)]).NeedsYou);

        // A merged or closed one has nothing left for anybody to do.
        Assert.False(Item("a", pulls: [Pull(1, IntegrationArtifactState.Merged, checks: IntegrationCheckState.Failing)]).NeedsYou);
        Assert.False(Item("a", pulls: [Pull(1, IntegrationArtifactState.Closed, IntegrationReviewState.ChangesRequested)]).NeedsYou);
    }

    [Fact]
    public void Needs_you_comes_first_then_moving_each_in_the_hosts_order()
    {
        var tasks = new[]
        {
            Item("moving-1", [Running]),
            Item("needs-1", [Quiet]),
            Item("moving-2"),
            Item("needs-2", pulls: [Pull(1, checks: IntegrationCheckState.Failing)]),
            Item("done", finished: true)
        };

        var sections = InProgressSections.Arrange(tasks, needsYouFirst: true, includeFinishedToday: false);

        Assert.Equal([InProgressSectionKind.NeedsYou, InProgressSectionKind.Moving], sections.Select(section => section.Kind));
        Assert.Equal(["needs-1", "needs-2"], sections[0].Tasks.Select(task => task.Id));
        Assert.Equal(["moving-1", "moving-2"], sections[1].Tasks.Select(task => task.Id));
    }

    [Fact]
    public void Without_needs_you_first_the_tasks_are_one_section_in_the_hosts_order()
    {
        var tasks = new[] { Item("moving", [Running]), Item("needs", [Quiet]) };

        var section = Assert.Single(InProgressSections.Arrange(tasks, needsYouFirst: false, includeFinishedToday: false));

        Assert.Equal(InProgressSectionKind.All, section.Kind);
        Assert.Equal(["moving", "needs"], section.Tasks.Select(task => task.Id));
    }

    [Fact]
    public void Finished_today_is_left_out_unless_asked_for_and_then_follows_in_its_own_section()
    {
        var tasks = new[] { Item("open"), Item("done", [Quiet], finished: true) };

        Assert.DoesNotContain(
            InProgressSections.Arrange(tasks, needsYouFirst: true, includeFinishedToday: false).SelectMany(section => section.Tasks),
            task => task.Id == "done");

        var sections = InProgressSections.Arrange(tasks, needsYouFirst: true, includeFinishedToday: true);

        Assert.Equal([InProgressSectionKind.NeedsYou, InProgressSectionKind.Moving, InProgressSectionKind.FinishedToday], sections.Select(section => section.Kind));
        Assert.Equal(["done"], sections[2].Tasks.Select(task => task.Id));
        Assert.Empty(sections[0].Tasks);
    }

    [Fact]
    public void Nothing_in_progress_has_no_task_sections()
    {
        Assert.Empty(InProgressSections.Arrange([], needsYouFirst: true, includeFinishedToday: true));
    }

    // --- The board --------------------------------------------------------------

    [Fact]
    public void The_board_draws_its_sections_in_order_with_the_loose_work_last()
    {
        using var context = new BunitContext();

        var board = context.Render<InProgressBoard>(parameters => parameters
            .Add(p => p.Tasks, [Item("moving", [Running]), Item("needs", [Quiet], [Pull(5)])])
            .Add(p => p.Loose, [new InProgressLoose(Session: new WorkSession("loose", "Loose one", IntegrationSessionState.Running))]));

        Assert.Equal(
            ["needs-you", "moving", "loose"],
            board.FindAll("[data-testid='in-progress-section']").Select(section => section.GetAttribute("data-section")));
        Assert.Equal("2 tasks · 3 sessions · 1 pull request", board.Find("[data-testid='in-progress-summary']").TextContent.Trim());

        // A task with nothing of a kind says so, rather than drawing an empty column.
        var moving = board.Find("[data-section='moving'] [data-testid='in-progress-task']");
        Assert.NotNull(moving.QuerySelector("[data-testid='work-links-no-pull-requests']"));
        Assert.Equal("None yet", moving.QuerySelector("[data-testid='work-links-no-pull-requests']")!.TextContent.Trim());

        // No listener, no button: the loose card is inert without a link handler.
        Assert.Empty(board.FindAll("[data-testid='in-progress-link']"));
    }

    [Fact]
    public void The_toggles_report_their_press_and_the_host_decides()
    {
        using var context = new BunitContext();
        bool? needsYouFirst = null;
        bool? includeFinished = null;

        var board = context.Render<InProgressBoard>(parameters => parameters
            .Add(p => p.Tasks, [Item("a")])
            .Add(p => p.NeedsYouFirstChanged, value => needsYouFirst = value)
            .Add(p => p.IncludeFinishedTodayChanged, value => includeFinished = value));

        Assert.Equal("true", board.Find("[data-testid='in-progress-needs-you-first']").GetAttribute("aria-pressed"));
        Assert.Equal("false", board.Find("[data-testid='in-progress-include-finished']").GetAttribute("aria-pressed"));

        board.Find("[data-testid='in-progress-needs-you-first']").Click();
        board.Find("[data-testid='in-progress-include-finished']").Click();

        Assert.False(needsYouFirst);
        Assert.True(includeFinished);
    }

    [Fact]
    public void A_title_a_card_and_link_to_a_task_hand_the_host_what_was_pressed()
    {
        using var context = new BunitContext();
        var pressed = new List<string>();
        var loose = new InProgressLoose(PullRequest: Pull(9));

        var board = context.Render<InProgressBoard>(parameters => parameters
            .Add(p => p.Tasks, [Item("a", [Running], [Pull(5)])])
            .Add(p => p.Loose, [loose])
            .Add(p => p.OnOpenTask, task => pressed.Add($"task {task.Id}"))
            .Add(p => p.OnOpenSession, session => pressed.Add($"session {session.Id}"))
            .Add(p => p.OnOpenPullRequest, pull => pressed.Add($"pull {pull.Key}"))
            .Add(p => p.OnLink, item => pressed.Add($"link {item.Key}")));

        board.Find("[data-testid='in-progress-task-title']").Click();
        board.Find("[data-testid='in-progress-task'] [data-testid='work-session']").Click();
        board.Find("[data-testid='in-progress-task'] [data-testid='work-pull-request']").Click();
        board.Find("[data-testid='in-progress-link']").Click();

        Assert.Equal(["task a", "session running", "pull o/r#5", "link o/r#9"], pressed);
    }

    [Fact]
    public void The_identity_edge_is_the_hosts_colour_and_absent_without_one()
    {
        using var context = new BunitContext();

        var board = context.Render<InProgressBoard>(parameters => parameters
            .Add(p => p.Tasks, [Item("a") with { Colour = 2 }, Item("b")]));

        var cards = board.FindAll("[data-testid='in-progress-task']");
        Assert.Contains("repo-mark--2", cards[0].ClassName, StringComparison.Ordinal);
        Assert.DoesNotContain("repo-mark", cards[1].ClassName, StringComparison.Ordinal);
    }

    [Fact]
    public void Nothing_in_progress_says_so()
    {
        using var context = new BunitContext();

        var board = context.Render<InProgressBoard>();

        Assert.NotEmpty(board.FindAll("[data-testid='in-progress-empty']"));
        Assert.Empty(board.FindAll("[data-testid='in-progress-section']"));
    }

    // --- The cards --------------------------------------------------------------

    [Fact]
    public void A_quiet_session_card_says_quiet_30_min_and_wears_the_needs_you_edge()
    {
        using var context = new BunitContext();

        var card = context.Render<WorkSessionCard>(parameters => parameters.Add(p => p.Session, Quiet));

        Assert.Equal("Quiet 30 min", card.Find("[data-testid='work-session-state']").TextContent.Trim());
        Assert.Contains("work-card--needs-you", card.Find("[data-testid='work-session']").ClassName, StringComparison.Ordinal);
        Assert.Equal("div", card.Find("[data-testid='work-session']").TagName, ignoreCase: true);
    }

    [Fact]
    public void A_pull_request_card_says_its_state_review_checks_and_size()
    {
        using var context = new BunitContext();
        var pull = new WorkPullRequest("o/r#1018", 1018, "Filter Tasks by plan", IntegrationArtifactState.Open,
            IntegrationReviewState.ChangesRequested, IntegrationCheckState.Failing, "1 check failing", 156, 22);

        var card = context.Render<WorkPullRequestCard>(parameters => parameters
            .Add(p => p.PullRequest, pull)
            .Add(p => p.OnOpen, _ => { }));

        Assert.Equal("button", card.Find("[data-testid='work-pull-request']").TagName, ignoreCase: true);
        Assert.Equal("#1018", card.Find("[data-testid='work-pull-request-number']").TextContent.Trim());
        Assert.Equal("Open", card.Find("[data-testid='work-pull-request-state']").TextContent.Trim());
        Assert.Equal("Changes requested", card.Find("[data-testid='work-pull-request-review']").TextContent.Trim());
        Assert.Equal("1 check failing", card.Find("[data-testid='work-pull-request-checks']").TextContent.Trim());
        Assert.Equal("+156 −22", card.Find("[data-testid='work-pull-request-diff']").TextContent.Trim());
        Assert.Contains("work-card--fault", card.Find("[data-testid='work-pull-request']").ClassName, StringComparison.Ordinal);
    }

    [Fact]
    public void A_draft_with_no_size_read_draws_no_size()
    {
        using var context = new BunitContext();

        var card = context.Render<WorkPullRequestCard>(parameters => parameters
            .Add(p => p.PullRequest, Pull(3, IntegrationArtifactState.Draft, checks: IntegrationCheckState.Pending)));

        Assert.Equal("Draft", card.Find("[data-testid='work-pull-request-state']").TextContent.Trim());
        Assert.Equal("Checks running", card.Find("[data-testid='work-pull-request-checks']").TextContent.Trim());
        Assert.Empty(card.FindAll("[data-testid='work-pull-request-diff']"));
        Assert.DoesNotContain("work-card--fault", card.Find("[data-testid='work-pull-request']").ClassName, StringComparison.Ordinal);
    }
}
