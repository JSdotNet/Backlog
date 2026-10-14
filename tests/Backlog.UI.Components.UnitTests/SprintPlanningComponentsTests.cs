using Backlog.UI.Components.Progress;

namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// What a host is promised about planning a sprint's parts
/// (<c>.devbook/domain/progress/features.md#filling-a-sprint</c>): the board's row menu
/// only when the host offers acts, the "Add to sprint" picker's narrowing and what it
/// will not offer again, the note dialog handing back one trimmed line, and a task's
/// Sprint section offering each act only where it applies.
/// </summary>
public sealed class SprintPlanningComponentsTests : IDisposable
{
    private readonly BunitContext _context = new();

    public SprintPlanningComponentsTests() => _context.JSInterop.Mode = JSRuntimeMode.Loose;

    // ---- The board's row menu --------------------------------------------------------

    [Fact]
    public void A_board_without_row_acts_draws_no_menu()
    {
        var board = _context.Render<SprintBoard>(parameters => parameters.Add(p => p.Model, Board()));

        Assert.Empty(board.FindAll("[data-testid=sprint-row-menu]"));
        Assert.DoesNotContain("sprint-board--editable", board.Find("[data-testid=sprint-board]").ClassList);
    }

    [Fact]
    public async Task A_row_menu_offers_the_hosts_acts_for_that_row_and_hands_back_the_choice()
    {
        SprintRowMenuChoice? chosen = null;
        var board = _context.Render<SprintBoard>(parameters => parameters
            .Add(p => p.Model, Board())
            .Add(p => p.RowMenu, row => [new MenuItem("goal", $"Mark {row.Title} as goal"), new MenuItem("remove", "Remove from sprint", Destructive: true)])
            .Add(p => p.OnRowMenu, (SprintRowMenuChoice choice) => chosen = choice));

        var buttons = board.FindAll("[data-testid=sprint-row-menu]");
        Assert.Equal(2, buttons.Count);
        Assert.Equal("Sprint actions for Grootboek", buttons[1].GetAttribute("aria-label"));

        await buttons[1].ClickAsync(new MouseEventArgs());
        var items = board.FindAll("[data-testid=sprint-row-menu-list] [role=menuitem]");
        Assert.Equal(["Mark Grootboek as goal", "Remove from sprint"], items.Select(item => item.TextContent.Trim()));

        await items[0].ClickAsync(new MouseEventArgs());

        Assert.NotNull(chosen);
        Assert.Equal("2", chosen!.Row.TaskId);
        Assert.Equal("goal", chosen.Item.Id);
        Assert.Empty(board.FindAll("[data-testid=sprint-row-menu-list]"));
    }

    // ---- Add to sprint ---------------------------------------------------------------

    [Fact]
    public void The_filter_narrows_by_kind_repository_and_every_search_word()
    {
        var found = SprintAddRules.Filter(Choices(), SprintAddKind.Task, "Fincent", "brief POL");

        Assert.Equal(["Dashboard brief polish"], found.Select(choice => choice.Label));
        Assert.Equal(2, SprintAddRules.Filter(Choices(), SprintAddKind.Plan, null, null).Count);
        Assert.Empty(SprintAddRules.Filter(Choices(), SprintAddKind.Task, "devbook-marketplace", "brief"));
    }

    [Fact]
    public async Task The_picker_lists_plans_before_tasks_and_does_not_offer_what_the_sprint_holds()
    {
        SprintAddChoice? chosen = null;
        var picker = _context.Render<SprintAddPicker>(parameters => parameters
            .Add(p => p.Open, true)
            .Add(p => p.SprintName, "Sprint 42")
            .Add(p => p.Repositories, ["Fincent", "devbook-marketplace"])
            .Add(p => p.Choices, Choices())
            .Add(p => p.OnChosen, (SprintAddChoice choice) => chosen = choice));

        var options = picker.FindAll("[data-testid=sprint-add-option]");
        Assert.Equal(["plan", "plan", "task", "task", "task"], options.Select(option => option.GetAttribute("data-kind")));
        Assert.True(options[0].HasAttribute("disabled"));
        Assert.Contains("In the sprint", options[0].TextContent);
        Assert.Single(picker.FindAll("[data-testid=sprint-add-repository]"));

        await options[1].ClickAsync(new MouseEventArgs());

        Assert.Equal("verplaatsen-specs", chosen!.Id);
        Assert.Equal(SprintAddKind.Plan, chosen.Kind);
        Assert.NotEmpty(picker.FindAll("[data-testid=sprint-add-picker]"));
    }

    [Fact]
    public void A_refused_choice_is_said_in_the_hosts_words()
    {
        var picker = _context.Render<SprintAddPicker>(parameters => parameters
            .Add(p => p.Open, true)
            .Add(p => p.Choices, Choices())
            .Add(p => p.Error, "Sprint 41 is closed."));

        Assert.Contains("Sprint 41 is closed.", picker.Find("[data-testid=sprint-add-error]").TextContent);
    }

    // ---- The note dialog -------------------------------------------------------------

    [Fact]
    public async Task The_note_dialog_starts_from_what_is_recorded_and_hands_back_one_trimmed_line()
    {
        SprintItemNote? saved = null;
        var dialog = _context.Render<SprintItemNoteDialog>(parameters => parameters
            .Add(p => p.Open, true)
            .Add(p => p.TaskTitle, "Categorieën")
            .Add(p => p.Current, new SprintItemNote("Import?", true))
            .Add(p => p.OnSave, (SprintItemNote note) => saved = note));

        var input = dialog.Find("[data-testid=sprint-note-text] input");
        Assert.Equal("Import?", input.GetAttribute("value"));

        await input.InputAsync(new ChangeEventArgs { Value = "  Ask Anne  " });
        await dialog.Find("[data-testid=sprint-note-save]").ClickAsync(new MouseEventArgs());

        Assert.Equal(new SprintItemNote("Ask Anne", true), saved);
    }

    [Fact]
    public async Task A_blank_note_clears_it()
    {
        SprintItemNote? saved = null;
        var dialog = _context.Render<SprintItemNoteDialog>(parameters => parameters
            .Add(p => p.Open, true)
            .Add(p => p.Current, new SprintItemNote("Import?", false))
            .Add(p => p.OnSave, (SprintItemNote note) => saved = note));

        var input = dialog.Find("[data-testid=sprint-note-text] input");
        await input.InputAsync(new ChangeEventArgs { Value = "   " });
        await dialog.Find("[data-testid=sprint-note-save]").ClickAsync(new MouseEventArgs());

        Assert.Equal(new SprintItemNote(null, false), saved);
    }

    // ---- A task's Sprint section -----------------------------------------------------

    [Fact]
    public void A_task_outside_the_sprint_offers_adding_it_and_its_plan_and_no_marks()
    {
        var panel = Panel(SprintMembership.Outside, plan: "specs", linked: false);

        Assert.Equal("Not in Sprint 42.", panel.Find("[data-testid=sprint-task-line]").TextContent.Trim());
        Assert.Equal("Add to Sprint 42", panel.Find("[data-testid=sprint-task-add]").TextContent.Trim());
        Assert.Equal("Add +specs to Sprint 42", panel.Find("[data-testid=sprint-task-link-plan]").TextContent.Trim());
        Assert.Empty(panel.FindAll("[data-testid=sprint-task-goal-toggle]"));
        Assert.Empty(panel.FindAll("[data-testid=sprint-task-edit-note]"));
    }

    [Fact]
    public void A_task_a_plan_brought_in_offers_adding_it_unlinking_the_plan_and_its_marks()
    {
        var panel = Panel(SprintMembership.ThroughPlan, plan: "rekeningen", linked: true);

        Assert.Equal("In Sprint 42 through +rekeningen.", panel.Find("[data-testid=sprint-task-line]").TextContent.Trim());
        Assert.Single(panel.FindAll("[data-testid=sprint-task-add]"));
        Assert.Empty(panel.FindAll("[data-testid=sprint-task-remove]"));
        Assert.Equal("Unlink +rekeningen", panel.Find("[data-testid=sprint-task-unlink-plan]").TextContent.Trim());
        Assert.Equal("Mark as goal", panel.Find("[data-testid=sprint-task-goal-toggle]").TextContent.Trim());
    }

    [Fact]
    public async Task A_task_added_directly_offers_removal_and_its_goal_mark_hands_back_the_other_state()
    {
        bool? goal = null;
        var panel = _context.Render<SprintTaskPanel>(parameters => parameters
            .Add(p => p.Model, Model(SprintMembership.Added, isGoal: true, note: "Import?", question: true))
            .Add(p => p.OnGoalChanged, (bool value) => goal = value));

        Assert.Equal("Remove from Sprint 42", panel.Find("[data-testid=sprint-task-remove]").TextContent.Trim());
        Assert.Single(panel.FindAll("[data-testid=sprint-task-goal]"));
        Assert.Equal("Import?", panel.Find("[data-testid=sprint-task-question]").TextContent.Trim());

        await panel.Find("[data-testid=sprint-task-goal-toggle]").ClickAsync(new MouseEventArgs());

        Assert.False(goal);
    }

    [Fact]
    public void A_series_with_no_open_sprint_says_so_and_offers_nothing()
    {
        var panel = _context.Render<SprintTaskPanel>(parameters => parameters
            .Add(p => p.Model, new SprintTaskPanelModel("Fincent team", null, null, SprintMembership.Outside, false, null, false, null, false)));

        Assert.Equal("Fincent team has no open sprint.", panel.Find("[data-testid=sprint-task-line]").TextContent.Trim());
        Assert.Empty(panel.FindAll("button"));
    }

    private IRenderedComponent<SprintTaskPanel> Panel(SprintMembership membership, string? plan, bool linked) =>
        _context.Render<SprintTaskPanel>(parameters => parameters.Add(p => p.Model, Model(membership, plan: plan, linked: linked)));

    private static SprintTaskPanelModel Model(
        SprintMembership membership,
        bool isGoal = false,
        string? note = null,
        bool question = false,
        string? plan = null,
        bool linked = false) =>
        new("Fincent team", "Sprint 42", "Mon 5 Oct – Sun 18 Oct", membership, isGoal, note, question, plan, linked);

    private static IReadOnlyList<SprintAddChoice> Choices() =>
    [
        new(SprintAddKind.Plan, "rekeningen", "+rekeningen", "6 tasks", ["Fincent"], InSprint: true),
        new(SprintAddKind.Plan, "verplaatsen-specs", "+verplaatsen-specs", "4 tasks", ["devbook-marketplace"]),
        new(SprintAddKind.Task, "1", "Opdrachtbrief", "Fincent · +rekeningen", ["Fincent"], InSprint: true),
        new(SprintAddKind.Task, "20", "Dashboard brief polish", "Fincent", ["Fincent"]),
        new(SprintAddKind.Task, "21", "Openspec", "devbook-marketplace", ["devbook-marketplace"])
    ];

    private static SprintBoardModel Board()
    {
        SprintBoardRow[] rows =
        [
            new("1", "Opdrachtbrief", SprintRowState.Done, "rekeningen", "fincent", true, false, [], [], null, false),
            new("2", "Grootboek", SprintRowState.NotStarted, null, "fincent", false, false, [], [], null, false)
        ];

        return new SprintBoardModel(
            "Fincent", "Sprint 42", "Mon 5 Oct – Sun 18 Oct", ["fincent"], false, false, "Ship it",
            new SprintBoardCounts(1, 0, 0, 0), "+rekeningen", false, SprintBoardCounts.Of(rows),
            "Day 7 of 10 · 3 working days left", [0, 1], 10, "Goal met.", ["rekeningen"], rows);
    }

    public void Dispose() => _context.Dispose();
}
