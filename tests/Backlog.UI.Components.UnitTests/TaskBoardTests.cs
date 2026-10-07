namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// The board: columns laid side by side, each one adding up its cards, and a drag
/// that writes only where the host's rule says a card may go.
/// <para>
/// The drag is driven through the same three calls <c>taskListDrag</c> makes from
/// components.js — start, over, end — because the pointer events themselves are
/// the browser's and bUnit has none to raise.
/// </para>
/// </summary>
public sealed class TaskBoardTests
{
    private static TaskBoardCard Card(string id, int? effort = null) =>
        new(new TaskRow(id, $"Task {id}"), Effort: effort);

    private static IReadOnlyList<TaskBoardColumn> StatusColumns() =>
    [
        new("Draft", "Draft", [Card("d1", 3), Card("d2")], CanAdd: true),
        new("Ready", "Ready", [Card("r1", 5), Card("r2", 8), Card("r3")], CanAdd: true),
        new("InProgress", "In progress", [Card("p1", 1)], CanAdd: true),
        new("Done", "Done", [], CanAdd: true)
    ];

    /// <summary>A rule shaped like a lifecycle: Ready may go to In progress or back
    /// to Draft, and nowhere else.</summary>
    private static TaskBoardDropRule? ReadyRule(string taskId, string columnKey) => columnKey switch
    {
        "InProgress" => new TaskBoardDropRule(true, "Drop to start"),
        "Draft" => new TaskBoardDropRule(true, "Drop to move back to Draft"),
        "Done" => new TaskBoardDropRule(false, "Ready can't move straight to Done — start it first"),
        _ => null
    };

    private static BunitContext NewContext()
    {
        var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        return context;
    }

    private static AngleSharp.Dom.IElement Column(IRenderedComponent<TaskBoard> board, string key) =>
        board.Find($"[data-board-column='{key}']");

    [Fact]
    public void Each_column_heads_its_cards_with_their_count_and_summed_points()
    {
        using var context = NewContext();

        var board = context.Render<TaskBoard>(p => p.Add(b => b.Columns, StatusColumns()));

        Assert.Equal(
            ["Draft", "Ready", "In progress", "Done"],
            board.FindAll("[data-testid='board-column-title']").Select(title => title.TextContent));
        Assert.Equal(
            ["2", "3", "1", "0"],
            board.FindAll("[data-testid='board-column-count']").Select(count => count.TextContent));
        Assert.Equal(
            ["3 pts · 1 not estimated", "13 pts · 1 not estimated", "1 pt", "0 pts"],
            board.FindAll("[data-testid='board-column-points']").Select(points => points.TextContent));

        Assert.Equal(3, Column(board, "Ready").QuerySelectorAll("[data-testid='board-card']").Length);
        Assert.Equal("Ready, 3 tasks, 13 pts · 1 not estimated", Column(board, "Ready").GetAttribute("aria-label"));
    }

    [Fact]
    public void A_board_that_is_not_draggable_offers_no_card_to_pick_up_and_ignores_a_start()
    {
        using var context = NewContext();
        var drops = new List<TaskBoardDrop>();

        var board = context.Render<TaskBoard>(p => p
            .Add(b => b.Columns, StatusColumns())
            .Add(b => b.DropRuleFor, ReadyRule)
            .Add(b => b.OnDrop, drop => drops.Add(drop)));

        Assert.All(board.FindAll("[data-testid='board-card']"), card => Assert.Null(card.GetAttribute("data-draggable")));

        board.InvokeAsync(() => board.Instance.PointerDragStart("r1"));
        board.InvokeAsync(() => board.Instance.PointerDragOver("InProgress"));
        board.InvokeAsync(() => board.Instance.PointerDragEnd());

        Assert.Empty(board.FindAll(".task-board__column--target"));
        Assert.Empty(drops);
    }

    [Fact]
    public void Holding_a_card_marks_the_columns_it_may_go_to_and_says_why_not_on_the_others()
    {
        using var context = NewContext();

        var board = context.Render<TaskBoard>(p => p
            .Add(b => b.Columns, StatusColumns())
            .Add(b => b.Draggable, true)
            .Add(b => b.DropRuleFor, ReadyRule));

        Assert.All(board.FindAll("[data-testid='board-card']"), card => Assert.Equal("true", card.GetAttribute("data-draggable")));

        board.InvokeAsync(() => board.Instance.PointerDragStart("r1"));

        // The column it came from is no question at all.
        var ready = Column(board, "Ready");
        Assert.DoesNotContain("task-board__column--target", ready.ClassName);
        Assert.DoesNotContain("task-board__column--refused", ready.ClassName);
        Assert.Contains("task-card--dragging", ready.QuerySelector("[data-task-id='r1']")!.ClassName);

        var progress = Column(board, "InProgress");
        Assert.Contains("task-board__column--target", progress.ClassName);
        Assert.Equal("Drop to start", progress.QuerySelector("[data-testid='board-column-slot']")!.TextContent.Trim());

        Assert.Contains("task-board__column--target", Column(board, "Draft").ClassName);

        var done = Column(board, "Done");
        Assert.Contains("task-board__column--refused", done.ClassName);
        Assert.Equal(
            "Ready can't move straight to Done — start it first",
            done.QuerySelector("[data-testid='board-column-refusal']")!.TextContent);
        Assert.Null(done.QuerySelector("[data-testid='board-column-slot']"));

        // Over an allowed column, that column is the one lit.
        board.InvokeAsync(() => board.Instance.PointerDragOver("InProgress"));
        Assert.Contains("task-board__column--over", Column(board, "InProgress").ClassName);
        Assert.Contains("task-board__slot--over", Column(board, "InProgress").QuerySelector("[data-testid='board-column-slot']")!.ClassName);
    }

    [Fact]
    public async Task A_drop_on_an_allowed_column_is_reported_and_the_board_settles()
    {
        using var context = NewContext();
        var drops = new List<TaskBoardDrop>();

        var board = context.Render<TaskBoard>(p => p
            .Add(b => b.Columns, StatusColumns())
            .Add(b => b.Draggable, true)
            .Add(b => b.DropRuleFor, ReadyRule)
            .Add(b => b.OnDrop, drop => drops.Add(drop)));

        await board.InvokeAsync(() => board.Instance.PointerDragStart("r2"));
        await board.InvokeAsync(() => board.Instance.PointerDragOver("InProgress"));
        await board.InvokeAsync(() => board.Instance.PointerDragEnd());

        Assert.Equal([new TaskBoardDrop("r2", "InProgress")], drops);
        Assert.Empty(board.FindAll(".task-board__column--target, .task-board__column--refused, .task-card--dragging"));
        // This host wrote nothing, so the card never left Ready: no move is announced.
        Assert.Equal(string.Empty, board.Find("[data-testid='board-announcement']").TextContent);
    }

    [Fact]
    public async Task A_drop_on_a_refused_column_writes_nothing_and_says_why()
    {
        using var context = NewContext();
        var drops = new List<TaskBoardDrop>();

        var board = context.Render<TaskBoard>(p => p
            .Add(b => b.Columns, StatusColumns())
            .Add(b => b.Draggable, true)
            .Add(b => b.DropRuleFor, ReadyRule)
            .Add(b => b.OnDrop, drop => drops.Add(drop)));

        await board.InvokeAsync(() => board.Instance.PointerDragStart("r1"));
        await board.InvokeAsync(() => board.Instance.PointerDragOver("Done"));
        await board.InvokeAsync(() => board.Instance.PointerDragEnd());

        Assert.Empty(drops);
        Assert.Equal(
            "Ready can't move straight to Done — start it first",
            board.Find("[data-testid='board-announcement']").TextContent);
    }

    [Fact]
    public async Task A_drop_back_on_its_own_column_off_the_board_or_after_a_cancel_writes_nothing()
    {
        using var context = NewContext();
        var drops = new List<TaskBoardDrop>();

        var board = context.Render<TaskBoard>(p => p
            .Add(b => b.Columns, StatusColumns())
            .Add(b => b.Draggable, true)
            .Add(b => b.DropRuleFor, ReadyRule)
            .Add(b => b.OnDrop, drop => drops.Add(drop)));

        await board.InvokeAsync(() => board.Instance.PointerDragStart("r1"));
        await board.InvokeAsync(() => board.Instance.PointerDragOver("Ready"));
        await board.InvokeAsync(() => board.Instance.PointerDragEnd());

        await board.InvokeAsync(() => board.Instance.PointerDragStart("r1"));
        await board.InvokeAsync(() => board.Instance.PointerDragOver(null));
        await board.InvokeAsync(() => board.Instance.PointerDragEnd());

        await board.InvokeAsync(() => board.Instance.PointerDragStart("r1"));
        await board.InvokeAsync(() => board.Instance.PointerDragOver("InProgress"));
        await board.InvokeAsync(() => board.Instance.PointerDragCancel());
        await board.InvokeAsync(() => board.Instance.PointerDragEnd());

        Assert.Empty(drops);
        Assert.Empty(board.FindAll(".task-card--dragging"));
    }

    [Fact]
    public void A_card_press_opens_the_task_and_an_add_creates_in_its_column()
    {
        using var context = NewContext();
        var opened = new List<string>();
        var added = new List<string>();
        IReadOnlyList<TaskBoardColumn> columns =
        [
            new("Draft", "Draft", [Card("d1")], CanAdd: true),
            new("plan:none", "No plan", [Card("n1")])
        ];

        var board = context.Render<TaskBoard>(p => p
            .Add(b => b.Columns, columns)
            .Add(b => b.SelectedId, "n1")
            .Add(b => b.OnSelected, id => opened.Add(id))
            .Add(b => b.OnAdd, key => added.Add(key)));

        board.Find("[data-task-id='d1']").Click();
        Assert.Equal(["d1"], opened);
        Assert.Equal("true", board.Find("[data-task-id='n1']").GetAttribute("aria-current"));

        // Only a column that takes new entries draws the control.
        var add = Assert.Single(board.FindAll("[data-testid='board-column-add']"));
        Assert.Equal("New entry in Draft", add.GetAttribute("aria-label"));
        add.Click();
        Assert.Equal(["Draft"], added);
    }

    [Fact]
    public void The_board_registers_with_the_task_drag_and_names_its_columns_for_it()
    {
        using var context = NewContext();

        var board = context.Render<TaskBoard>(p => p.Add(b => b.Columns, StatusColumns()));

        var owner = board.Find(".task-board").GetAttribute("data-list-owner");
        Assert.StartsWith("task-board-", owner, StringComparison.Ordinal);
        Assert.Contains(
            context.JSInterop.Invocations["taskListDrag.register"],
            call => string.Equals(call.Arguments[0] as string, owner, StringComparison.Ordinal));
        Assert.Equal(
            ["Draft", "Ready", "InProgress", "Done"],
            board.FindAll("[data-board-column]").Select(column => column.GetAttribute("data-board-column")));
    }
}
