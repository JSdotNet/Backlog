namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// The two things a row can carry beyond its title: a body, and the ids it waits
/// on. Both are additions to a shape that already shipped, so half of what is
/// proved here is that a row using neither is untouched.
/// </summary>
public sealed class TaskPromptTests
{
    private const string Prompt = """
        Review the change against the conventions the library already keeps.

        ### What to look at

        The question is whether this would look out of place beside the code
        that was already there.

        - Does every glyph have an sr-only name beside it?
        - Does any control that would do nothing render as a button?
        """;

    private static readonly IReadOnlyList<TaskRow> Chain =
    [
        new("outline", "Draft the outline", Done: true),
        new("draft", "Write the first draft", DependsOn: ["outline"]),
        new("review", "Review the draft", DependsOn: ["draft"]),
        new("publish", "Publish it", DependsOn: ["review", "draft"])
    ];

    private static readonly IReadOnlyList<TaskRow> Loop =
    [
        new("a", "Collect", DependsOn: ["c"]),
        new("b", "Rank", DependsOn: ["a"]),
        new("c", "Summarise", DependsOn: ["b"])
    ];

    /// <summary>One step three rows are waiting on: the shape where "what should
    /// I start first" and "what can I start now" stop being the same answer.</summary>
    private static readonly IReadOnlyList<TaskRow> FanOut =
    [
        new("gather", "Gather the source material"),
        new("summarise", "Summarise it", DependsOn: ["gather"]),
        new("critique", "Critique it", DependsOn: ["gather"]),
        new("translate", "Translate it", DependsOn: ["gather"])
    ];

    /// <summary>The same fan-out with its one prerequisite finished.</summary>
    private static IReadOnlyList<TaskRow> Freed =>
        [.. FanOut.Select(task => task.Id == "gather" ? task with { Done = true } : task)];

    // --- A body ----------------------------------------------------------

    [Fact]
    public void A_folded_row_still_says_there_is_more_here_than_the_title()
    {
        // The note glyph already meant exactly that, so a body earns it without
        // being asked. A folded row that gave no sign of one would be a row
        // whose disclosure is the only thing saying it is worth opening.
        var row = new TaskRow("a", "Review the change", Body: Prompt);

        Assert.True(row.HasBody);
        var note = Assert.Single(row.Details, detail => detail.Kind is TaskDetailKind.Note);
        Assert.Equal("📝", note.Glyph);
        Assert.Equal("Note", note.Name);
    }

    [Fact]
    public void The_body_is_folded_away_to_start_with_and_kept_in_the_dom_while_it_is()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var view = context.Render<TaskItem>(p => p
            .Add(t => t.Task, new TaskRow("a", "Review the change", Body: Prompt))
            .Add(t => t.TestId, "row"));

        var trigger = view.Find("[data-testid='row-body-toggle']");
        var region = view.Find(".fold__region");

        Assert.Equal("false", trigger.GetAttribute("aria-expanded"));
        Assert.True(region.HasAttribute("hidden"));

        // The trigger names the region it folds, and the region is there to be
        // named rather than removed and rebuilt on every fold.
        Assert.Equal(region.Id, trigger.GetAttribute("aria-controls"));
    }

    [Fact]
    public void Opening_a_row_renders_its_body_as_markdown_where_the_row_is()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var view = context.Render<TaskItem>(p => p
            .Add(t => t.Task, new TaskRow("a", "Review the change", Body: Prompt))
            .Add(t => t.TestId, "row"));

        view.Find("[data-testid='row-body-toggle']").Click();

        Assert.Equal("true", view.Find("[data-testid='row-body-toggle']").GetAttribute("aria-expanded"));
        Assert.False(view.Find(".fold__region").HasAttribute("hidden"));

        var body = view.Find("[data-testid='row-body']");

        // A sub-header, a paragraph and a list — the three things a prompt is
        // made of, and none of them survive being crammed into a title.
        var heading = Assert.Single(body.QuerySelectorAll(".md-heading"));
        Assert.Equal("3", heading.GetAttribute("aria-level"));
        Assert.Equal("What to look at", heading.TextContent);

        Assert.NotEmpty(body.QuerySelectorAll(".md-p"));
        Assert.Equal(2, body.QuerySelectorAll(".md-list li").Length);
    }

    [Fact]
    public void The_disclosure_is_called_whatever_the_host_calls_a_body()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var view = context.Render<TaskItem>(p => p
            .Add(t => t.Task, new TaskRow("a", "Review the change", Body: Prompt))
            .Add(t => t.BodyLabel, "Prompt")
            .Add(t => t.TestId, "row"));

        Assert.Equal("Prompt", view.Find(".fold__label").TextContent);
        Assert.Equal("Prompt for Review the change", view.Find("[data-testid='row-body-toggle']").GetAttribute("aria-label"));
    }

    [Fact]
    public void A_row_can_start_open_and_the_fold_is_the_readers_from_then_on()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var view = context.Render<TaskItem>(p => p
            .Add(t => t.Task, new TaskRow("a", "Review the change", Body: Prompt))
            .Add(t => t.BodyExpanded, true)
            .Add(t => t.TestId, "row"));

        Assert.False(view.Find(".fold__region").HasAttribute("hidden"));

        view.Find("[data-testid='row-body-toggle']").Click();

        // A parameter that kept re-asserting itself would snap the body shut
        // under the reader every time anything else in the list moved.
        Assert.True(view.Find(".fold__region").HasAttribute("hidden"));
    }

    [Fact]
    public void A_title_only_row_emits_none_of_it()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var view = context.Render<TaskItem>(p => p
            .Add(t => t.Task, new TaskRow("a", "Ring the dentist"))
            .Add(t => t.OnToggle, _ => { })
            .Add(t => t.TestId, "row"));

        Assert.Empty(view.FindAll("[data-testid='row-body-toggle']"));
        Assert.Empty(view.FindAll("[data-testid='row-body']"));
        Assert.Empty(view.FindAll(".fold__region"));
        Assert.Empty(view.FindAll(".task-item__meta"));
        Assert.DoesNotContain("task-item--has-body", view.Find("li.task-item").ClassList);
    }

    [Fact]
    public void A_prompt_is_copied_with_the_title_that_says_which_step_it_was()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.JSInterop.Setup<bool>("backlogClipboard.copy", _ => true).SetResult(true);

        var view = context.Render<TaskItem>(p => p
            .Add(t => t.Task, new TaskRow("a", "Review the change", Body: "Read the diff."))
            .Add(t => t.TestId, "row"));

        view.Find("[data-testid='row-copy']").Click();

        Assert.Equal(
            "Review the change\n\nRead the diff.",
            Assert.Single(context.JSInterop.Invocations["backlogClipboard.copy"]).Arguments[0]);
    }

    [Fact]
    public void An_explicit_copy_value_still_wins_over_a_body()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.JSInterop.Setup<bool>("backlogClipboard.copy", _ => true).SetResult(true);

        var view = context.Render<TaskItem>(p => p
            .Add(t => t.Task, new TaskRow("a", "Review the change", Body: "Read the diff."))
            .Add(t => t.CopyValue, "just this")
            .Add(t => t.TestId, "row"));

        view.Find("[data-testid='row-copy']").Click();

        Assert.Equal("just this", Assert.Single(context.JSInterop.Invocations["backlogClipboard.copy"]).Arguments[0]);
    }

    // --- Deriving the chain -----------------------------------------------

    [Fact]
    public void A_task_is_ready_when_everything_it_named_is_finished()
    {
        var statuses = TaskChain.Resolve(Chain);

        Assert.Equal(TaskReadiness.Done, statuses[0].Readiness);
        Assert.Equal(TaskReadiness.Ready, statuses[1].Readiness);
        Assert.Equal(TaskReadiness.Blocked, statuses[2].Readiness);
        Assert.Equal(TaskReadiness.Blocked, statuses[3].Readiness);

        Assert.Empty(statuses[1].Waiting);
        Assert.Equal(["Write the first draft"], statuses[2].Waiting.Select(dependency => dependency.Label));

        // Both of them, named. "Waiting on 2" says the reader is stuck without
        // saying what to go and do about it.
        Assert.Equal(["Review the draft", "Write the first draft"], statuses[3].Waiting.Select(dependency => dependency.Label));
    }

    [Fact]
    public void Done_wins_over_an_outstanding_dependency()
    {
        // Done is a fact somebody recorded; blocked is only a conclusion. A
        // component that argued with the recorded one would be telling the
        // reader they are wrong about their own list.
        IReadOnlyList<TaskRow> tasks =
        [
            new("first", "Not finished"),
            new("second", "Finished anyway", Done: true, DependsOn: ["first"])
        ];

        var status = TaskChain.Resolve(tasks)[1];

        Assert.Equal(TaskReadiness.Done, status.Readiness);
        Assert.Empty(status.Waiting);
    }

    [Fact]
    public void The_next_one_to_pick_up_is_the_first_ready_row_in_list_order()
    {
        Assert.Equal("draft", TaskChain.NextReady(Chain)?.Id);

        // Finish it and the chain moves on by one, in the order the host wrote.
        var moved = Chain.Select(task => task.Id == "draft" ? task with { Done = true } : task).ToList();
        Assert.Equal("review", TaskChain.NextReady(moved)?.Id);
    }

    [Fact]
    public void Ready_is_every_startable_row_in_list_order_and_next_is_the_first_of_them()
    {
        // Two questions with one implementation between them: NextReady is the
        // first of Ready, so the row wearing the marker and the row the host is
        // told to run cannot come apart.
        Assert.Equal(["draft"], TaskChain.Ready(Chain).Select(task => task.Id));
        Assert.Equal(TaskChain.Ready(Chain)[0].Id, TaskChain.NextReady(Chain)?.Id);

        Assert.Equal(["gather"], TaskChain.Ready(FanOut).Select(task => task.Id));

        // And here they differ, which is the whole reason both exist.
        var freed = Freed;

        Assert.Equal(["summarise", "critique", "translate"], TaskChain.Ready(freed).Select(task => task.Id));
        Assert.Equal("summarise", TaskChain.NextReady(freed)?.Id);

        // A cycle leaves nothing startable, and empty is the answer rather than a
        // member picked to stand in for one.
        Assert.Empty(TaskChain.Ready(Loop));
        Assert.Null(TaskChain.NextReady(Loop));
    }

    [Fact]
    public void Finishing_one_step_frees_every_row_that_named_it()
    {
        var before = TaskChain.Resolve(FanOut);

        Assert.Equal(TaskReadiness.Ready, before[0].Readiness);
        Assert.All(before.Skip(1), status => Assert.Equal(TaskReadiness.Blocked, status.Readiness));

        var after = TaskChain.Resolve(Freed);

        // Three rows move from blocked to ready in one step. Nothing about the
        // list changed except the fact that was recorded on one row.
        Assert.Equal(TaskReadiness.Done, after[0].Readiness);
        Assert.All(after.Skip(1), status => Assert.Equal(TaskReadiness.Ready, status.Readiness));
        Assert.All(after.Skip(1), status => Assert.Empty(status.Waiting));
    }

    [Fact]
    public void A_list_nobody_chained_derives_nothing()
    {
        IReadOnlyList<TaskRow> plain = [new("a", "One"), new("b", "Two")];

        Assert.False(TaskChain.IsChain(plain));
        Assert.True(TaskChain.IsChain(Chain));
    }

    // --- What the row does with it ----------------------------------------

    [Fact]
    public void A_blocked_row_offers_no_way_to_complete_itself_even_when_someone_is_listening()
    {
        // The same rule as a row with no OnToggle, for the same reason: a
        // control that would record something untrue is worse than no control.
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var view = context.Render<TaskListView>(p => p
            .Add(l => l.Tasks, Chain)
            .Add(l => l.OnToggle, _ => { })
            .Add(l => l.GroupCompleted, false)
            .Add(l => l.TestId, "list"));

        var blocked = view.Find("[data-testid='list-review-check']");

        Assert.Equal("img", blocked.GetAttribute("role"));
        Assert.Equal("Blocked", blocked.GetAttribute("aria-label"));

        // The ready row beside it is still a control.
        Assert.Equal("checkbox", view.Find("[data-testid='list-draft-check']").GetAttribute("role"));
    }

    [Fact]
    public void A_blocked_row_says_what_it_is_waiting_for_by_name()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var view = context.Render<TaskListView>(p => p
            .Add(l => l.Tasks, Chain)
            .Add(l => l.GroupCompleted, false)
            .Add(l => l.TestId, "list"));

        var waiting = view.Find("[data-testid='list-publish'] .task-item__detail--blocked");

        Assert.Contains("Review the draft", waiting.TextContent, StringComparison.Ordinal);
        Assert.Contains("Write the first draft", waiting.TextContent, StringComparison.Ordinal);

        // The glyph is decoration; the name is the part that is read out.
        Assert.Equal("true", waiting.QuerySelector(".task-item__glyph")!.GetAttribute("aria-hidden"));
        Assert.Equal("Waiting for", waiting.QuerySelector(".sr-only")!.TextContent);
    }

    [Fact]
    public void No_row_in_a_linear_chain_wears_a_next_or_ready_marker()
    {
        // The badges that used to name the row to pick up next, and the rows
        // merely startable, are gone: a reader is left the "waiting for"
        // detail on what is not ready and nothing at all on what is.
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var view = context.Render<TaskListView>(p => p
            .Add(l => l.Tasks, Chain)
            .Add(l => l.GroupCompleted, false)
            .Add(l => l.TestId, "list"));

        Assert.Empty(view.FindAll(".task-item__next"));
        Assert.Empty(view.FindAll(".task-item__ready"));
    }

    [Fact]
    public void A_fan_out_wears_no_marker_on_the_row_to_start_with_or_the_rows_it_freed()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var view = context.Render<TaskListView>(p => p
            .Add(l => l.Tasks, Freed)
            .Add(l => l.GroupCompleted, false)
            .Add(l => l.TestId, "list"));

        Assert.Empty(view.FindAll(".task-item__next"));
        Assert.Empty(view.FindAll(".task-item__ready"));

        // Nothing is waiting any more, so no row says it is.
        Assert.Empty(view.FindAll(".task-item__detail--blocked"));
    }

    [Fact]
    public void A_list_nobody_chained_renders_exactly_what_it_rendered_before()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var view = context.Render<TaskListView>(p => p
            .Add(l => l.Tasks, new TaskRow[] { new("a", "One"), new("b", "Two") })
            .Add(l => l.OnToggle, _ => { })
            .Add(l => l.TestId, "list"));

        Assert.Empty(view.FindAll(".task-item__next"));
        Assert.Empty(view.FindAll(".task-item__detail--blocked"));
        Assert.Empty(view.FindAll("[role='img']"));
        Assert.All(view.FindAll("li.task-item"), row => Assert.DoesNotContain("task-item--blocked", row.ClassList));
    }

    // --- Dangling ids ------------------------------------------------------

    [Fact]
    public void An_id_that_names_nothing_here_leaves_the_task_blocked()
    {
        // Dropping it would let the chain claim to be ready when the step it
        // waits on is merely missing from this view.
        IReadOnlyList<TaskRow> tasks = [new("ship", "Ship it", DependsOn: ["sign-off"])];

        var status = Assert.Single(TaskChain.Resolve(tasks));

        Assert.Equal(TaskReadiness.Blocked, status.Readiness);
        Assert.Null(TaskChain.NextReady(tasks));

        var dependency = Assert.Single(status.Waiting);
        Assert.False(dependency.Known);
        Assert.Equal("sign-off", dependency.Label);
    }

    [Fact]
    public void The_row_says_the_unknown_id_verbatim()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var view = context.Render<TaskListView>(p => p
            .Add(l => l.Tasks, new TaskRow[] { new("ship", "Ship it", DependsOn: ["sign-off"]) })
            .Add(l => l.TestId, "list"));

        Assert.Contains(
            "sign-off",
            view.Find("[data-testid='list-ship'] .task-item__detail--blocked").TextContent,
            StringComparison.Ordinal);
    }

    // --- A wider universe to resolve against --------------------------------

    [Fact]
    public void A_dependency_missing_from_tasks_still_resolves_against_the_universe()
    {
        // The list a host draws and the list a dependency is looked up against
        // are not always the same list — a repo-scoped view is a real example,
        // not a hypothetical one. Handed a universe, an id absent from tasks is
        // not unknown; it is simply somewhere else, and the row it names still
        // has a title and a Done bit worth reading.
        IReadOnlyList<TaskRow> tasks = [new("here", "In view", DependsOn: ["elsewhere"])];

        IReadOnlyList<TaskRow> notDoneUniverse =
            [.. tasks, new("elsewhere", "Out of view, not finished")];

        var stillWaiting = Assert.Single(TaskChain.Resolve(tasks, notDoneUniverse));

        Assert.Equal(TaskReadiness.Blocked, stillWaiting.Readiness);
        var dependency = Assert.Single(stillWaiting.Waiting);
        Assert.True(dependency.Known);
        Assert.Equal("Out of view, not finished", dependency.Label);

        IReadOnlyList<TaskRow> doneUniverse =
            [.. tasks, new("elsewhere", "Out of view, finished", Done: true)];

        var freed = Assert.Single(TaskChain.Resolve(tasks, doneUniverse));

        Assert.Equal(TaskReadiness.Ready, freed.Readiness);
        Assert.Empty(freed.Waiting);
    }

    [Fact]
    public void With_no_universe_supplied_resolution_falls_back_to_tasks_itself()
    {
        // Every caller before the universe existed passed one list and meant it
        // as both — the rows to derive statuses for and the rows to look ids up
        // against. Nothing about that behaviour may change just because a second
        // parameter now exists to change it with.
        var withUniverse = TaskChain.Resolve(Chain, Chain);
        var withoutUniverse = TaskChain.Resolve(Chain);

        Assert.Equal(
            withoutUniverse.Select(status => (status.Id, status.Readiness, status.InCycle)),
            withUniverse.Select(status => (status.Id, status.Readiness, status.InCycle)));
    }

    // --- Cycles ------------------------------------------------------------

    [Fact]
    public void Every_task_in_a_cycle_is_blocked_and_flagged_and_nothing_is_next()
    {
        var statuses = TaskChain.Resolve(Loop);

        Assert.All(statuses, status => Assert.Equal(TaskReadiness.Blocked, status.Readiness));
        Assert.All(statuses, status => Assert.True(status.InCycle));

        // Calling any of them ready would be a lie, and picking one would be
        // inventing a beginning for a chain that has none.
        Assert.Null(TaskChain.NextReady(Loop));
        Assert.Equal(["Collect", "Rank", "Summarise"], TaskChain.Cycles(Loop).Select(task => task.Title));
    }

    [Fact]
    public void A_task_that_names_itself_is_a_cycle_of_one()
    {
        IReadOnlyList<TaskRow> tasks = [new("a", "Waits for itself", DependsOn: ["a"])];

        var status = Assert.Single(TaskChain.Resolve(tasks));

        Assert.True(status.InCycle);
        Assert.Equal(TaskReadiness.Blocked, status.Readiness);
    }

    [Fact]
    public void A_cycle_is_walked_without_recursing_so_it_terminates()
    {
        // The one input this must not fall over on is the one it exists to
        // detect: a thousand tasks in a single loop, resolved from an explicit
        // stack rather than the call stack.
        var long_ = Enumerable.Range(0, 1000)
            .Select(i => new TaskRow($"t{i}", $"Step {i}", DependsOn: [$"t{(i + 1) % 1000}"]))
            .ToList();

        var statuses = TaskChain.Resolve(long_);

        Assert.Equal(1000, statuses.Count);
        Assert.All(statuses, status => Assert.True(status.InCycle));
        Assert.Null(TaskChain.NextReady(long_));
    }

    [Fact]
    public void A_row_in_a_cycle_says_so_rather_than_leaving_it_to_be_worked_out()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var view = context.Render<TaskListView>(p => p
            .Add(l => l.Tasks, Loop)
            .Add(l => l.OnToggle, _ => { })
            .Add(l => l.TestId, "list"));

        Assert.Equal(3, view.FindAll(".task-item__detail--cycle").Count);
        Assert.Empty(view.FindAll(".task-item__next"));
        Assert.Empty(view.FindAll("button[role='checkbox']"));

        var cycle = view.Find("[data-testid='list-a'] .task-item__detail--cycle");
        Assert.Equal("true", cycle.QuerySelector(".task-item__glyph")!.GetAttribute("aria-hidden"));
        Assert.Equal("Cycle", cycle.QuerySelector(".sr-only")!.TextContent);
    }

    [Fact]
    public void Nothing_derived_is_written_back_to_the_list_it_was_given()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        context.Render<TaskListView>(p => p
            .Add(l => l.Tasks, Chain)
            .Add(l => l.GroupCompleted, false));

        Assert.Equal(["outline", "draft", "review", "publish"], Chain.Select(task => task.Id));
        Assert.Equal([true, false, false, false], Chain.Select(task => task.Done));
    }
}
