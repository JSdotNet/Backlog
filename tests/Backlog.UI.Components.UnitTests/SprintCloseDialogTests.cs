using Backlog.UI.Components.Progress;

namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// What a host is promised about the Close dialog
/// (<c>.devbook/domain/progress/features.md#closing-a-sprint</c>): the review figures in
/// words, every unfinished item on Carry until the person picks otherwise, "Set all to the
/// next sprint" putting them back, the review table carrying a Result column that follows
/// the choices, and Close handing back every choice with the trimmed next goal.
/// </summary>
public sealed class SprintCloseDialogTests : IDisposable
{
    private readonly BunitContext _context = new();

    public SprintCloseDialogTests() => _context.JSInterop.Mode = JSRuntimeMode.Loose;

    [Fact]
    public void The_dialog_reviews_the_sprint_and_lists_the_unfinished_items_on_carry()
    {
        var dialog = Render(Model());

        Assert.Equal("Close Sprint 42", dialog.Find(".modal__title").TextContent.Trim());
        Assert.Equal("Goal not met", dialog.Find("[data-testid=sprint-close-goal-verdict]").TextContent.Trim());
        Assert.Equal("1 of 2 goal tasks done", dialog.Find("[data-testid=sprint-close-goal-line]").TextContent.Trim());
        Assert.Contains("1 of 3", dialog.Find("[data-testid=sprint-close-all]").TextContent, StringComparison.Ordinal);
        Assert.Contains("1", dialog.Find("[data-testid=sprint-close-added]").TextContent, StringComparison.Ordinal);
        Assert.Equal("2 unfinished", dialog.Find("[data-testid=sprint-close-unfinished-heading]").TextContent.Trim());

        var items = dialog.FindAll("[data-testid=sprint-close-item]");
        Assert.Equal(["2", "3"], items.Select(item => item.GetAttribute("data-task-id")));
        Assert.All(dialog.FindAll("[data-testid=sprint-close-item-choice] select"), select => Assert.Equal("Carry", select.GetAttribute("value")));
        Assert.Equal(
            ["Carry to Sprint 43", "Back to the backlog", "Drop"],
            dialog.FindAll("[data-testid=sprint-close-item-choice] select")[0].QuerySelectorAll("option").Select(option => option.TextContent.Trim()));
        Assert.Equal("Close and start Sprint 43", dialog.Find("[data-testid=sprint-close-confirm]").TextContent.Trim());
    }

    [Fact]
    public async Task Close_hands_back_every_choice_and_the_trimmed_next_goal()
    {
        SprintCloseDecision? decision = null;
        var dialog = Render(Model(), chosen => decision = chosen);

        await dialog.FindAll("[data-testid=sprint-close-item-choice] select")[0].ChangeAsync(new ChangeEventArgs { Value = "Drop" });
        await dialog.FindAll("[data-testid=sprint-close-item-choice] select")[1].ChangeAsync(new ChangeEventArgs { Value = "BackToBacklog" });
        await dialog.Find("[data-testid=sprint-close-next-goal] input").InputAsync(new ChangeEventArgs { Value = "  Ship the review  " });
        await dialog.Find("[data-testid=sprint-close-confirm]").ClickAsync(new MouseEventArgs());

        Assert.NotNull(decision);
        Assert.Equal(SprintCloseChoice.Drop, decision!.Choices["2"]);
        Assert.Equal(SprintCloseChoice.BackToBacklog, decision.Choices["3"]);
        Assert.Equal(2, decision.Choices.Count);
        Assert.Equal("Ship the review", decision.NextGoal);
    }

    [Fact]
    public async Task Set_all_puts_every_item_back_on_carry()
    {
        SprintCloseDecision? decision = null;
        var dialog = Render(Model(), chosen => decision = chosen);

        await dialog.FindAll("[data-testid=sprint-close-item-choice] select")[0].ChangeAsync(new ChangeEventArgs { Value = "Drop" });
        Assert.Equal("Set all to Sprint 43", dialog.Find("[data-testid=sprint-close-carry-all]").TextContent.Trim());
        await dialog.Find("[data-testid=sprint-close-carry-all]").ClickAsync(new MouseEventArgs());
        await dialog.Find("[data-testid=sprint-close-confirm]").ClickAsync(new MouseEventArgs());

        Assert.All(decision!.Choices.Values, choice => Assert.Equal(SprintCloseChoice.Carry, choice));
    }

    [Fact]
    public void The_review_table_adds_a_result_column_that_follows_the_choices()
    {
        var model = Model();
        var table = SprintCloseRules.ReviewTable(model, new Dictionary<string, SprintCloseChoice> { ["2"] = SprintCloseChoice.Drop });

        Assert.StartsWith("Plan\tItem\tStatus\tPull requests\tOwner\tNote\tResult", table.Text, StringComparison.Ordinal);
        Assert.Contains(">Result</th>", table.Html, StringComparison.Ordinal);

        var lines = table.Text.Split("\r\n");
        Assert.EndsWith("\tDone", lines.Single(line => line.Contains("Opdrachtbrief", StringComparison.Ordinal)), StringComparison.Ordinal);
        Assert.EndsWith("\tDropped", lines.Single(line => line.Contains("Categorieën", StringComparison.Ordinal)), StringComparison.Ordinal);
        Assert.EndsWith("\tCarried to Sprint 43", lines.Single(line => line.Contains("Grootboek", StringComparison.Ordinal)), StringComparison.Ordinal);
    }

    [Fact]
    public void The_sprint_table_without_a_result_keeps_its_six_columns()
    {
        var table = SprintTableClipboard.Build(Model().Rows, []);

        Assert.DoesNotContain("Result", table.Text, StringComparison.Ordinal);
        Assert.DoesNotContain(">Result</th>", table.Html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_sprint_with_everything_done_lists_nothing_to_settle()
    {
        var done = Model() with { Unfinished = [] };
        var dialog = Render(done);

        Assert.Equal("Every item is done", dialog.Find("[data-testid=sprint-close-unfinished-heading]").TextContent.Trim());
        Assert.Empty(dialog.FindAll("[data-testid=sprint-close-item]"));
        Assert.Empty(dialog.FindAll("[data-testid=sprint-close-carry-all]"));
    }

    [Fact]
    public async Task Reopening_starts_the_choices_and_the_goal_over()
    {
        var dialog = Render(Model());
        await dialog.FindAll("[data-testid=sprint-close-item-choice] select")[0].ChangeAsync(new ChangeEventArgs { Value = "Drop" });
        await dialog.Find("[data-testid=sprint-close-next-goal] input").InputAsync(new ChangeEventArgs { Value = "Half typed" });

        dialog.Render(parameters => parameters.Add(p => p.Open, false));
        dialog.Render(parameters => parameters.Add(p => p.Open, true));

        Assert.All(dialog.FindAll("[data-testid=sprint-close-item-choice] select"), select => Assert.Equal("Carry", select.GetAttribute("value")));
        Assert.True(string.IsNullOrEmpty(dialog.Find("[data-testid=sprint-close-next-goal] input").GetAttribute("value")));
    }

    [Fact]
    public async Task Set_all_draws_every_select_back_on_carry()
    {
        var dialog = Render(Model());
        await dialog.FindAll("[data-testid=sprint-close-item-choice] select")[1].ChangeAsync(new ChangeEventArgs { Value = "BackToBacklog" });

        await dialog.Find("[data-testid=sprint-close-carry-all]").ClickAsync(new MouseEventArgs());

        Assert.All(dialog.FindAll("[data-testid=sprint-close-item-choice] select"), select => Assert.Equal("Carry", select.GetAttribute("value")));
    }

    [Fact]
    public void A_met_goal_reads_as_met()
    {
        var dialog = Render(Model() with { GoalMet = true, GoalDone = 2 });

        var verdict = dialog.Find("[data-testid=sprint-close-goal-verdict]");
        Assert.Equal("Goal met", verdict.TextContent.Trim());
        Assert.Contains("sprint-close-dialog__verdict--met", verdict.ClassList);
    }

    [Fact]
    public async Task Copy_the_review_puts_the_table_with_its_result_column_on_the_clipboard()
    {
        var copy = _context.JSInterop.Setup<bool>("backlogClipboard.copyRich", _ => true);
        copy.SetResult(true);
        var dialog = Render(Model());
        await dialog.FindAll("[data-testid=sprint-close-item-choice] select")[1].ChangeAsync(new ChangeEventArgs { Value = "BackToBacklog" });

        await dialog.Find("[data-testid=sprint-close-copy-button]").ClickAsync(new MouseEventArgs());

        var invocation = Assert.Single(copy.Invocations);
        Assert.Contains(">Result</th>", Assert.IsType<string>(invocation.Arguments[0]), StringComparison.Ordinal);
        var text = Assert.IsType<string>(invocation.Arguments[1]);
        Assert.EndsWith("\tBack to the backlog", text.Split("\r\n").Single(line => line.Contains("Grootboek", StringComparison.Ordinal)), StringComparison.Ordinal);
    }

    [Fact]
    public void Cancel_waits_while_the_close_is_written()
    {
        var dialog = _context.Render<SprintCloseDialog>(parameters => parameters
            .Add(p => p.Open, true)
            .Add(p => p.Model, Model())
            .Add(p => p.Busy, true));

        Assert.True(dialog.Find("[data-testid=sprint-close-cancel]").HasAttribute("disabled"));
    }

    [Fact]
    public void A_refusal_shows_in_the_dialog()
    {
        var dialog = _context.Render<SprintCloseDialog>(parameters => parameters
            .Add(p => p.Open, true)
            .Add(p => p.Model, Model())
            .Add(p => p.Error, "Every unfinished item needs an outcome."));

        Assert.Contains("Every unfinished item needs an outcome.", dialog.Find("[data-testid=sprint-close-error]").TextContent, StringComparison.Ordinal);
    }

    private IRenderedComponent<SprintCloseDialog> Render(SprintCloseModel model, Action<SprintCloseDecision>? onClose = null) =>
        _context.Render<SprintCloseDialog>(parameters => parameters
            .Add(p => p.Open, true)
            .Add(p => p.Model, model)
            .Add(p => p.OnClose, (SprintCloseDecision decision) => onClose?.Invoke(decision)));

    private static SprintCloseModel Model()
    {
        SprintBoardRow[] rows =
        [
            new("1", "Opdrachtbrief", SprintRowState.Done, "rekeningen", "fincent", true, false, [], [], null, false),
            new("2", "Categorieën", SprintRowState.Blocked, "rekeningen", "fincent", true, false, [], [], "Import?", true),
            new("3", "Grootboek", SprintRowState.NotStarted, null, "fincent", false, true, [], [], null, false)
        ];

        return new SprintCloseModel(
            "Sprint 42",
            "Sprint 43",
            "Fincent · Mon 5 Oct – Sun 18 Oct · goal: Ship it",
            GoalMet: false,
            GoalDone: 1,
            GoalTotal: 2,
            AllDone: 1,
            AllTotal: 3,
            AddedAfterStart: 1,
            [new("2", "Categorieën", "Goal · +rekeningen · blocked 3 days"), new("3", "Grootboek", "not started")],
            rows,
            ["rekeningen"]);
    }

    public void Dispose() => _context.Dispose();
}
