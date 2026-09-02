using Backlog.UI.Components.Tasks;
using Bunit;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// Dragging one entry by its link handle onto another entry, which is the pane's
/// half of the gesture.
/// <para>
/// The shared list reports the two ids and applies nothing; what a dependency
/// means here is an <c>after:</c> token in the entry's own text, and that is the
/// only thing these tests are about. The handle itself, the refusals and what is
/// said out loud all belong to the library and are proved in
/// <c>TaskChainLinkTests</c>.
/// </para>
/// <para>
/// Driven through the list's <c>[JSInvokable]</c> methods, because the gesture
/// lives on <c>document</c> in <c>components.js</c> and there is no pointer here
/// to move.
/// </para>
/// </summary>
[Collection(WorkspaceSettingsCollection.Name)]
public sealed class TasksPaneChainDragTests
{
    private const string WithSteps =
        "# Ship the sync spike\n" +
        "`task`\n\n" +
        "Notes on the parent.\n\n" +
        "## Wire up the store\n" +
        "How the store gets wired.\n\n" +
        "## Write the rows\n" +
        "And then the rows.\n";

    /// <summary>One of the pane's two task lists, by the test id the pane gives
    /// it. Both are the same component, so the id is the only thing that says
    /// which of them a drag is happening in.</summary>
    private static IRenderedComponent<TaskListView> List(
        IRenderedComponent<TasksPane> pane,
        string testId) =>
        pane.FindComponents<TaskListView>()
            .FirstOrDefault(list => string.Equals(list.Instance.TestId, testId, StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"The pane is not rendering a '{testId}'.");

    /// <summary>The steps as they are drawn, in order. Read off the rendered
    /// fields rather than off the row: the steps list is DirectRename, so a
    /// step's title on screen <em>is</em> a field, and a step's own title is
    /// parsed inline markdown rather than a string to compare.</summary>
    private static IEnumerable<string> StepTitles(IRenderedComponent<TasksPane> pane) =>
        pane.FindAll("[data-testid^='subitem-list-'][data-testid$='-rename']")
            .Select(field => field.GetAttribute("value") ?? string.Empty);

    [Fact]
    public async Task Dragging_one_entry_onto_another_writes_an_after_token()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var predecessor = await host.WriteEntryAsync("# Provision the box\n`task`\n\nGet a machine.\n");
        var waiter = await host.WriteEntryAsync("# Deploy it\n`task` `after:a1b2c3`\n\nShip it.\n");

        var pane = host.Render();
        var list = List(pane, "entry-list");

        var waiterId = waiter.Id!.Value.ToString();
        var predecessorId = predecessor.Id!.Value.ToString();

        await list.InvokeAsync(() => list.Instance.PointerLinkStart(waiterId));
        await list.InvokeAsync(() => list.Instance.PointerDragOver(predecessorId));
        await list.InvokeAsync(list.Instance.PointerDragEnd);

        Assert.Contains($"`after:{predecessorId}`", waiter.RawText, StringComparison.Ordinal);

        // The token it already had is still there. The parser rewrites the whole
        // set, so a handler that passed only the new id would have deleted the
        // dependency the reader wrote first — silently, and only visible once
        // they looked for it.
        Assert.Contains("`after:a1b2c3`", waiter.RawText, StringComparison.Ordinal);
        Assert.Equal(["a1b2c3", predecessorId], waiter.PreviewDependsOn);
    }

    [Fact]
    public async Task The_steps_list_has_no_link_handle_and_its_drag_still_reorders()
    {
        // A step's id *is* its index, so a dependency written against one would
        // point at a different step the moment a step is added or deleted. The
        // list draws no handle because the pane passes no OnLink — there is no flag
        // to remember and nothing to turn off.
        using var host = await TasksPaneHost.CreateAsync();
        await host.WriteEntryAsync(WithSteps);

        var pane = host.Render();
        var steps = List(pane, "subitem-list");

        Assert.Equal(["Wire up the store", "Write the rows"], StepTitles(pane));
        Assert.Empty(steps.FindAll(".task-item__link"));

        // Nothing to press, and nothing behind it either: the link entry point is
        // shut on a list that is not listening, so a stale script cannot reach
        // past the missing handle and write a step-to-step dependency.
        await steps.InvokeAsync(() => steps.Instance.PointerLinkStart("0"));
        await steps.InvokeAsync(() => steps.Instance.PointerDragOver("1"));
        await steps.InvokeAsync(steps.Instance.PointerDragEnd);

        Assert.Equal(["Wire up the store", "Write the rows"], StepTitles(pane));

        // And the drag the list does have is untouched by any of it.
        await steps.InvokeAsync(() => steps.Instance.PointerDragStart("0"));
        await steps.InvokeAsync(() => steps.Instance.PointerDragOver("1"));
        await steps.InvokeAsync(steps.Instance.PointerDragEnd);

        Assert.Equal(["Write the rows", "Wire up the store"], StepTitles(pane));
    }

    [Fact]
    public async Task The_entry_list_draws_a_link_handle_on_every_open_row()
    {
        // The pane's half of "visible at rest": the handle is there before anybody
        // drags anything, because an affordance that appears only under the pointer
        // is one nobody finds.
        using var host = await TasksPaneHost.CreateAsync();
        await host.WriteEntryAsync("# Provision the box\n`task`\n\nGet a machine.\n");
        await host.WriteEntryAsync("# Deploy it\n`task`\n\nShip it.\n");

        var pane = host.Render();

        Assert.Equal(2, pane.FindAll("[data-testid='entry-list'] .task-item__link").Count);
    }

    [Fact]
    public async Task The_drag_and_the_dependency_picker_write_the_same_id()
    {
        // Two writers, one list of ids. The drag reads EntryTaskId and the picker
        // reads PreviewDependsOn, and if those two ever named a row differently
        // the chip for a dragged dependency would simply not appear.
        using var host = await TasksPaneHost.CreateAsync();
        var predecessor = await host.WriteEntryAsync("# Provision the box\n`task`\n\nGet a machine.\n");
        var waiter = await host.WriteEntryAsync("# Deploy it\n`task`\n\nShip it.\n");

        var pane = host.Render();
        var list = List(pane, "entry-list");

        await list.InvokeAsync(() => list.Instance.PointerLinkStart(waiter.Id!.Value.ToString()));
        await list.InvokeAsync(() => list.Instance.PointerDragOver(predecessor.Id!.Value.ToString()));
        await list.InvokeAsync(list.Instance.PointerDragEnd);

        await pane.Find("[data-testid='entry-action-depends-set']").ClickAsync(new());

        // The chip's label rather than the chip: a removable chip also carries the
        // × that takes it off again.
        var chips = pane.FindAll("[data-testid='entry-depends-select'] .tag-select__chip .tag-chip__label");

        Assert.Equal(["Provision the box"], chips.Select(chip => chip.TextContent.Trim()));
    }
}
