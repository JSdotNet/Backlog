using Backlog.UI.Components.Goals;
using Backlog.UI.Components.Progress;
using Backlog.UI.Components.UnitTests.Harnesses;

namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// The open goal's own acts on the Goals view
/// (<c>.devbook/domain/progress/features.md#setting-a-goal</c> and
/// <c>#attaching-work-to-a-goal</c>): its title, outcome and due date edited in place with
/// no Save button, <b>Delete goal…</b> behind a destructive confirm, and a Detach on each row
/// the goal holds itself. Each act is a callback; without a listener the act is not drawn.
/// </summary>
public sealed class GoalCardEditingTests : IDisposable
{
    private readonly BunitContext _context = new();

    public GoalCardEditingTests() => _context.JSInterop.Mode = JSRuntimeMode.Loose;

    public void Dispose() => _context.Dispose();

    private static DateOnly Oct(int day) => new(2026, 10, day);

    private static readonly GoalSubject Bank = GoalSubject.ForPlan("bank-integration");
    private static readonly GoalSubject T1 = GoalSubject.ForTask(new Guid("3c9e7d20-0000-4000-8000-000000000001"));
    private static readonly GoalSubject T3 = GoalSubject.ForTask(new Guid("3c9e7d20-0000-4000-8000-000000000003"));
    private static readonly GoalSubject Pr1341 = GoalSubject.ForPullRequest("JSdotNet/Backlog", 1341);
    private static readonly GoalSubject Pr1352 = GoalSubject.ForPullRequest("JSdotNet/Backlog", 1352);

    private static readonly GoalCardModel Paid = new(
        "paid", "Customers get paid faster", Oct(30), null, true,
        new GoalCounts(2, 4, 1, 0, 1, 0),
        [new GoalPlanModel("Bank integration", 5, 9, Bank)],
        [
            new GoalTaskModel("t1", "Invoice PDF export", GoalTaskStatus.Done, T1),
            new GoalTaskModel("t3", "Recurring invoices", GoalTaskStatus.Doing, T3)
        ],
        [
            new PullRequestStatusModel(1341, "open"),
            new PullRequestStatusModel(1358, "draft"),
            new PullRequestStatusModel(1352, "merged")
        ],
        "Invoices are paid within 14 days",
        [Pr1341, null, Pr1352]);

    private static readonly GoalCardModel Search = new(
        "search", "Devbook search ships", Oct(23), null, false, new GoalCounts(0, 0, 0, 0, 0, 0), [], [], []);

    private static GoalsBoardModel Board(params GoalCardModel[] goals) => new(
        GoalsIteration.Week,
        new GoalsWindow(Oct(12), Oct(16)),
        new GoalsWindow(Oct(19), Oct(23)),
        goals.Length == 0 ? [Paid, Search] : goals,
        [],
        StandUpModel.Empty,
        []);

    private sealed class Calls
    {
        public List<GoalEditRequest> Edits { get; } = [];
        public List<string> Deletes { get; } = [];
        public List<GoalDetachRequest> Detaches { get; } = [];
        public List<string?> Selected { get; } = [];
    }

    private IRenderedComponent<GoalsBoard> RenderBoard(Calls calls, GoalsBoardModel? model = null, string? editError = null) =>
        _context.Render<GoalsBoard>(p => p
            .Add(c => c.Model, model ?? Board())
            .Add(c => c.EditError, editError)
            .Add(c => c.SelectedGoalIdChanged, (string? id) => calls.Selected.Add(id))
            .Add(c => c.OnEditGoal, (GoalEditRequest request) => calls.Edits.Add(request))
            .Add(c => c.OnDeleteGoal, (string id) => calls.Deletes.Add(id))
            .Add(c => c.OnDetach, (GoalDetachRequest request) => calls.Detaches.Add(request)));

    private const string Open = "[data-testid='goals-board-goal-paid']";

    private static string Field(string part) => $"{Open} [data-testid='goals-board-goal-paid-{part}'] input";

    [Fact]
    public void Without_listeners_the_open_goal_reads_its_outcome_as_text_and_offers_no_edit_delete_or_detach()
    {
        var board = _context.Render<GoalsBoard>(p => p.Add(c => c.Model, Board()));

        Assert.Equal("Invoices are paid within 14 days", board.Find($"{Open} [data-testid='goals-board-goal-paid-outcome']").TextContent);
        Assert.Empty(board.FindAll($"{Open} input:not([type='search'])"));
        Assert.Empty(board.FindAll("[data-testid='goals-board-goal-paid-delete']"));
        Assert.Empty(board.FindAll($"{Open} .goal-card__detach"));
    }

    [Fact]
    public void The_open_goal_offers_its_title_outcome_and_due_date_as_fields_with_no_save_button()
    {
        var board = RenderBoard(new Calls());

        Assert.Equal("Customers get paid faster", board.Find(Field("title")).GetAttribute("value"));
        Assert.Equal("Invoices are paid within 14 days", board.Find(Field("outcome")).GetAttribute("value"));
        var due = board.Find(Field("due"));
        Assert.Equal("date", due.GetAttribute("type"));
        Assert.Equal("2026-10-30", due.GetAttribute("value"));
        Assert.Equal(["Title", "Outcome", "Due date"], board.FindAll($"{Open} .goal-card__details label").Select(l => l.TextContent.Trim()));
        Assert.DoesNotContain(board.FindAll($"{Open} button"), b => b.TextContent.Trim() == "Save");

        // A folded goal has no fields.
        Assert.Empty(board.FindAll("[data-testid='goals-board-goal-search'] .goal-card__details"));
    }

    private IRenderedComponent<GoalsBoardEditHarness> RenderHarness(TimeSpan delay = default, string? deleteRefusal = null) =>
        _context.Render<GoalsBoardEditHarness>(p => p
            .Add(c => c.Model, Board())
            .Add(c => c.Delay, delay)
            .Add(c => c.DeleteRefusal, deleteRefusal));

    /// <summary>Types into a field and leaves it, as a person does: the input event, then
    /// the change.</summary>
    private static void Type<T>(IRenderedComponent<T> host, string part, string text)
        where T : IComponent
    {
        var input = host.Find(Field(part));
        input.Input(text);
        input.Change(text);
    }

    [Fact]
    public void A_changed_field_hands_back_all_three_trimmed_with_the_other_two_as_the_goal_says_them()
    {
        var host = RenderHarness();

        Type(host, "title", "  Customers paid sooner ");
        Type(host, "outcome", "   ");
        Type(host, "due", "2026-11-06");
        host.Find(Field("due")).FocusOut();

        Assert.Equal(
            [
                new GoalEditRequest("paid", "Customers paid sooner", "Invoices are paid within 14 days", Oct(30)),
                new GoalEditRequest("paid", "Customers paid sooner", null, Oct(30)),
                new GoalEditRequest("paid", "Customers paid sooner", null, new DateOnly(2026, 11, 6))
            ],
            host.Instance.Edits);
        Assert.Equal("Customers paid sooner", host.Find(Field("title")).GetAttribute("value"));
    }

    [Fact]
    public void A_second_edit_sent_before_the_first_ones_reading_lands_carries_both()
    {
        var calls = new Calls();
        var board = RenderBoard(calls);

        // The host here never reads again, which is the slow reading at its slowest.
        board.Find(Field("title")).Change("Customers paid sooner");
        board.Find(Field("outcome")).Change("Paid within a week");

        Assert.Equal(
            [
                new GoalEditRequest("paid", "Customers paid sooner", "Invoices are paid within 14 days", Oct(30)),
                new GoalEditRequest("paid", "Customers paid sooner", "Paid within a week", Oct(30))
            ],
            calls.Edits);
        Assert.Equal("Customers paid sooner", board.Find(Field("title")).GetAttribute("value"));
    }

    [Fact]
    public void A_value_the_goal_already_says_is_not_handed_back()
    {
        var calls = new Calls();
        var board = RenderBoard(calls);

        board.Find(Field("title")).Change(" Customers get paid faster ");
        board.Find(Field("outcome")).Change("Invoices are paid within 14 days");
        board.Find(Field("due")).Change("2026-10-30");
        board.Find(Field("due")).FocusOut();

        Assert.Empty(calls.Edits);
    }

    [Fact]
    public void A_date_the_field_cannot_read_is_not_handed_back_and_leaving_puts_the_goals_date_back()
    {
        var host = RenderHarness();

        Type(host, "due", string.Empty);
        Assert.Empty(host.Instance.Edits);

        host.Find(Field("due")).FocusOut();

        Assert.Equal("2026-10-30", host.Find(Field("due")).GetAttribute("value"));
        Assert.Empty(host.Instance.Edits);
    }

    [Fact]
    public void A_date_is_held_while_it_is_typed_and_handed_back_on_leaving_the_field()
    {
        var host = RenderHarness();

        // A date field changes on every keystroke that makes a whole date: typing 2027
        // passes through 0002, 0020 and 0202.
        foreach (var partway in new[] { "0002-11-06", "0020-11-06", "0202-11-06", "2027-11-06" })
        {
            Type(host, "due", partway);
        }

        Assert.Empty(host.Instance.Edits);

        host.Find(Field("due")).FocusOut();

        Assert.Equal(new GoalEditRequest("paid", "Customers get paid faster", "Invoices are paid within 14 days", new DateOnly(2027, 11, 6)), Assert.Single(host.Instance.Edits));
        Assert.Equal("2027-11-06", host.Find(Field("due")).GetAttribute("value"));
    }

    [Fact]
    public void Enter_hands_the_held_date_back()
    {
        var host = RenderHarness();

        Type(host, "due", "2026-11-13");
        host.Find(Field("due")).KeyDown(new KeyboardEventArgs { Key = "Enter" });

        Assert.Equal(new DateOnly(2026, 11, 13), Assert.Single(host.Instance.Edits).DueDate);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(40)]
    public void A_refused_edit_names_the_field_in_the_modules_words_and_puts_the_stored_value_back_on_the_same_input(int delay)
    {
        var host = RenderHarness(TimeSpan.FromMilliseconds(delay));
        var id = host.Find(Field("title")).Id;

        Type(host, "title", "  ");

        host.WaitForAssertion(() =>
        {
            Assert.Equal(new GoalEditRequest("paid", string.Empty, "Invoices are paid within 14 days", Oct(30)), Assert.Single(host.Instance.Edits));
            var field = host.Find("[data-testid='goals-board-goal-paid-title']");
            Assert.Contains("A goal needs a title.", field.TextContent);
            Assert.Equal("true", host.Find(Field("title")).GetAttribute("aria-invalid"));
            Assert.Equal("Customers get paid faster", host.Find(Field("title")).GetAttribute("value"));
        });

        // The same field, not a new one, so the focus a keyboard user had is still on it.
        Assert.Equal(id, host.Find(Field("title")).Id);
        Assert.Empty(host.FindAll("[data-testid='goals-board-goal-paid-edit-error']"));
    }

    [Fact]
    public void A_kept_edit_clears_the_refusal()
    {
        var host = RenderHarness();
        Type(host, "title", " ");
        host.WaitForAssertion(() => Assert.Contains("A goal needs a title.", host.Find("[data-testid='goals-board-goal-paid-title']").TextContent));

        Type(host, "title", "Customers paid sooner");

        host.WaitForAssertion(() =>
        {
            Assert.DoesNotContain("A goal needs a title.", host.Find("[data-testid='goals-board-goal-paid-title']").TextContent);
            Assert.Null(host.Find(Field("title")).GetAttribute("aria-invalid"));
        });
    }

    [Fact]
    public void Escape_abandons_what_was_typed_and_stops_at_the_field()
    {
        var calls = new Calls();
        var board = RenderBoard(calls);
        var input = board.Find(Field("outcome"));

        input.Input("Paid within a week");
        input.KeyDown(new KeyboardEventArgs { Key = "Escape" });

        Assert.Equal("Invoices are paid within 14 days", board.Find(Field("outcome")).GetAttribute("value"));
        Assert.Empty(calls.Edits);
    }

    [Fact]
    public void Delete_goal_asks_first_and_names_what_stays()
    {
        var calls = new Calls();
        var board = RenderBoard(calls);

        var delete = board.Find("[data-testid='goals-board-goal-paid-delete']");
        Assert.Equal("Delete goal…", delete.TextContent.Trim());
        Assert.Contains("btn--danger", delete.ClassName);
        Assert.Empty(board.FindAll("[data-testid='goals-board-delete-confirm']"));

        delete.Click();

        var confirm = board.Find("[data-testid='goals-board-delete-confirm']");
        Assert.Contains("Delete this goal?", confirm.TextContent);
        Assert.Contains("Customers get paid faster is deleted. The plans, tasks and pull requests it holds stay as they are and belong to no goal.", confirm.TextContent);
        Assert.Equal("Delete goal", board.Find("[data-testid='goals-board-delete-confirm-yes']").TextContent.Trim());
        Assert.Empty(calls.Deletes);
    }

    [Fact]
    public void Confirming_hands_the_goals_id_back_and_folds_it_once_the_host_has_deleted_it()
    {
        var calls = new Calls();
        var board = RenderBoard(calls);
        calls.Selected.Clear();

        board.Find("[data-testid='goals-board-goal-paid-delete']").Click();
        board.Find("[data-testid='goals-board-delete-confirm-yes']").Click();

        board.WaitForAssertion(() =>
        {
            Assert.Equal(["paid"], calls.Deletes);
            Assert.Equal([null], calls.Selected);
            Assert.Empty(board.FindAll("[data-testid='goals-board-delete-confirm']"));
            Assert.Equal("false", board.Find(Open).GetAttribute("data-expanded"));
        });
    }

    [Fact]
    public void The_confirm_stays_open_while_the_host_deletes_and_the_goal_leaves_with_the_reading()
    {
        var host = RenderHarness(TimeSpan.FromMilliseconds(80));

        host.Find("[data-testid='goals-board-goal-paid-delete']").Click();
        host.Find("[data-testid='goals-board-delete-confirm-yes']").Click();

        Assert.NotNull(host.Find("[data-testid='goals-board-delete-confirm']"));
        host.WaitForAssertion(() =>
        {
            Assert.Equal(["paid"], host.Instance.Deletes);
            Assert.Empty(host.FindAll("[data-testid='goals-board-delete-confirm']"));
            Assert.Empty(host.FindAll(Open));
        });
    }

    [Fact]
    public void A_refused_delete_leaves_the_goal_open_with_the_refusal_above_the_board()
    {
        var host = RenderHarness(deleteRefusal: "That goal is gone.");

        host.Find("[data-testid='goals-board-goal-paid-delete']").Click();
        host.Find("[data-testid='goals-board-delete-confirm-yes']").Click();

        host.WaitForAssertion(() =>
        {
            Assert.Contains("That goal is gone.", host.Find("[data-testid='goals-board-error']").TextContent);
            Assert.Equal("true", host.Find(Open).GetAttribute("data-expanded"));
            Assert.Empty(host.FindAll("[data-testid='goals-board-delete-confirm']"));
        });
    }

    [Fact]
    public void Cancelling_the_delete_changes_nothing()
    {
        var calls = new Calls();
        var board = RenderBoard(calls);

        board.Find("[data-testid='goals-board-goal-paid-delete']").Click();
        board.Find("[data-testid='goals-board-delete-confirm-no']").Click();

        Assert.Empty(calls.Deletes);
        Assert.Empty(board.FindAll("[data-testid='goals-board-delete-confirm']"));
        Assert.Equal("true", board.Find(Open).GetAttribute("data-expanded"));
    }

    [Fact]
    public void Every_row_the_goal_holds_itself_carries_a_detach_named_for_the_item_and_the_goal()
    {
        var board = RenderBoard(new Calls());

        var names = board.FindAll($"{Open} .goal-card__detach").Select(b => b.GetAttribute("aria-label")).ToList();

        Assert.Equal(
            [
                "Detach plan Bank integration from Customers get paid faster",
                "Detach task Invoice PDF export from Customers get paid faster",
                "Detach task Recurring invoices from Customers get paid faster",
                "Detach pull request #1341 from Customers get paid faster",
                "Detach pull request #1352 from Customers get paid faster"
            ],
            names);

        // #1358 comes only through a task: detaching the goal's own pull request would not
        // remove it, so it has no Detach.
        Assert.Empty(board.FindAll("[data-testid^='goals-board-goal-paid-detach-pr:JSdotNet/Backlog#1358']"));
        Assert.NotNull(board.Find($"[data-testid='goals-board-goal-paid-merged'] [data-testid='goals-board-goal-paid-detach-{Pr1352.Key}']"));
    }

    [Fact]
    public void Detach_hands_the_goal_and_the_items_key_back_without_asking()
    {
        var calls = new Calls();
        var board = RenderBoard(calls);

        board.Find($"[data-testid='goals-board-goal-paid-detach-{T3.Key}']").Click();
        board.Find("[data-testid='goals-board-goal-paid-detach-plan:bank-integration']").Click();
        board.Find($"[data-testid='goals-board-goal-paid-detach-{Pr1352.Key}']").Click();

        Assert.Equal(
            [new GoalDetachRequest("paid", T3), new GoalDetachRequest("paid", Bank), new GoalDetachRequest("paid", Pr1352)],
            calls.Detaches);
        Assert.Empty(board.FindAll("[role='dialog']"));
    }

    [Fact]
    public void While_the_host_writes_delete_and_detach_wait_and_the_fields_stay_live()
    {
        var board = _context.Render<GoalsBoard>(p => p
            .Add(c => c.Model, Board())
            .Add(c => c.Busy, true)
            .Add(c => c.OnEditGoal, (GoalEditRequest _) => { })
            .Add(c => c.OnDeleteGoal, (string _) => { })
            .Add(c => c.OnDetach, (GoalDetachRequest _) => { }));

        Assert.True(board.Find("[data-testid='goals-board-goal-paid-delete']").HasAttribute("disabled"));
        Assert.All(board.FindAll($"{Open} .goal-card__detach"), b => Assert.True(b.HasAttribute("disabled")));
        Assert.False(board.Find(Field("title")).HasAttribute("disabled"));
    }

    [Fact]
    public void A_pull_request_subject_past_the_list_is_none()
    {
        Assert.Equal(Pr1341, Paid.PullRequestSubject(0));
        Assert.Null(Paid.PullRequestSubject(1));
        Assert.Null(Paid.PullRequestSubject(3));
        Assert.Null(Search.PullRequestSubject(0));
    }
}
