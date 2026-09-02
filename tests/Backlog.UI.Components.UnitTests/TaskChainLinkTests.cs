namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// The second drag a task list has: press a row's link handle, move, and let go
/// on another row to make the first wait for the second.
/// <para>
/// A file of its own rather than more of <c>TaskListTests</c>, because the two
/// drags have to be provably separate: those tests are about the order, and the
/// whole risk here is one gesture doing the other one's job.
/// <c>TaskPromptTests</c> stays about what <c>TaskChain</c> derives from ids that
/// are already written down; this is about writing one down.
/// </para>
/// <para>
/// The thing under test, above all else, is that the mode is decided by the press
/// and never re-derived from where the pointer is. The version before this one
/// derived it from a drop zone under the pointer and oscillated — the retracting
/// reorder preview reflowed the aimed-at row out from under the pointer, the next
/// move reported no zone, and the drop meant whatever the release frame said. Two
/// tests below exist for exactly that failure: nothing moves for the whole of a
/// link drag, and a link drag that wanders still commits a link.
/// </para>
/// <para>
/// Every drag here is driven through the <c>[JSInvokable]</c> methods, as the
/// reorder tests are. The gesture itself lives on <c>document</c> in
/// <c>components.js</c> and there is no pointer in bUnit to move — what these can
/// prove is the contract that script calls, which is also the contract a stale
/// copy of it would break.
/// </para>
/// </summary>
public sealed class TaskChainLinkTests
{
    private static readonly IReadOnlyList<TaskRow> Three =
    [
        new("a", "First", Group: "Tasks"),
        new("b", "Second", Group: "Tasks"),
        new("c", "Third", Group: "Tasks")
    ];

    private static IRenderedComponent<TaskListView> Render(
        BunitContext context,
        IReadOnlyList<TaskRow> tasks,
        Action<TaskLink>? onLink = null,
        Action<TaskMove>? onReorder = null,
        bool reorderable = true,
        bool groupCompleted = true,
        bool directRename = false)
    {
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        return context.Render<TaskListView>((ComponentParameterCollectionBuilder<TaskListView> p) =>
        {
            p.Add(l => l.Tasks, tasks)
                .Add(l => l.Reorderable, reorderable)
                .Add(l => l.GroupCompleted, groupCompleted)
                .Add(l => l.DirectRename, directRename)
                .Add(l => l.TestId, "list");

            // The always-open field needs somebody listening for a rename before
            // it is a field at all, which is the row's rule rather than this
            // test's — see TaskItem.RenameOpen.
            if (directRename) p.Add(l => l.OnRename, (TaskRename _) => { });

            if (onLink is not null) p.Add(l => l.OnLink, onLink);
            if (onReorder is not null) p.Add(l => l.OnReorder, onReorder);
        });
    }

    private static string Announcement(IRenderedComponent<TaskListView> view) =>
        view.Find("[data-testid='list-announcement']").TextContent;

    private static IEnumerable<string> Titles(IRenderedComponent<TaskListView> view) =>
        view.FindAll(".task-item__title").Select(title => title.TextContent);

    [Fact]
    public async Task Dropping_a_row_picked_up_by_its_link_handle_makes_it_wait_for_the_row_it_lands_on()
    {
        using var context = new BunitContext();
        TaskLink? link = null;
        var reordered = 0;

        var view = Render(context, Three, onLink: l => link = l, onReorder: _ => reordered++);

        await view.InvokeAsync(() => view.Instance.PointerLinkStart("a"));
        await view.InvokeAsync(() => view.Instance.PointerDragOver("c"));
        await view.InvokeAsync(view.Instance.PointerDragEnd);

        Assert.Equal("a", link?.Id);
        Assert.Equal("c", link?.DependsOnId);

        // Linking never reorders. Both facts are about the same two rows, and a
        // gesture that wrote both would be a gesture the reader cannot aim.
        Assert.Equal(0, reordered);

        // And nothing was applied here either: the ids the host handed over are
        // the ids the host still has.
        Assert.All(Three, task => Assert.Empty(task.DependsOnList));
    }

    [Fact]
    public async Task A_drag_that_started_on_the_row_itself_can_only_ever_reorder()
    {
        // The other direction of the same exclusivity, and the reason there are
        // two entry points rather than one with a mode on it: there is no way for
        // a reorder drag to arrive at a link, whatever the pointer does afterwards.
        using var context = new BunitContext();
        TaskLink? link = null;
        TaskMove? move = null;

        var view = Render(context, Three, onLink: l => link = l, onReorder: m => move = m);

        await view.InvokeAsync(() => view.Instance.PointerDragStart("a"));
        await view.InvokeAsync(() => view.Instance.PointerDragOver("c"));
        await view.InvokeAsync(view.Instance.PointerDragEnd);

        Assert.Equal("a", move?.Id);
        Assert.Equal("c", move?.TargetId);
        Assert.Null(link);
    }

    [Fact]
    public async Task Nothing_moves_for_the_whole_of_a_link_drag()
    {
        // The invariant the design exists for. A link drag has no reorder to
        // preview, so the list must stay in the order it is going to keep for the
        // entire gesture: the row the reader is aiming at cannot be allowed to
        // move out from under the pointer, because that is what made the previous
        // version of this feature bistable.
        using var context = new BunitContext();

        var view = Render(context, Three, onLink: _ => { }, onReorder: _ => { });

        await view.InvokeAsync(() => view.Instance.PointerLinkStart("a"));

        Assert.Equal(["First", "Second", "Third"], Titles(view));

        // Every row the pointer could reach, including the row in flight and the
        // row it would have been previewed against.
        foreach (var id in new[] { "b", "c", "a", "c", "b" })
        {
            await view.InvokeAsync(() => view.Instance.PointerDragOver(id));

            Assert.Equal(["First", "Second", "Third"], Titles(view));
        }

        await view.InvokeAsync(view.Instance.PointerDragEnd);

        Assert.Equal(["First", "Second", "Third"], Titles(view));

        // And the same rows, in the same list, do move for a reorder drag — so
        // what is asserted above is a list holding still rather than a list this
        // test cannot see moving.
        await view.InvokeAsync(() => view.Instance.PointerDragStart("a"));
        await view.InvokeAsync(() => view.Instance.PointerDragOver("c"));

        Assert.Equal(["Second", "Third", "First"], Titles(view));
    }

    [Fact]
    public async Task A_link_drag_that_wanders_still_commits_a_link_to_the_row_it_ends_on()
    {
        // The other half of the same bug. What the drop means is settled by the
        // press, so no amount of travelling — over rows that refuse, back over
        // the row in flight, out of the list and in again — can turn it into a
        // reorder or lose it altogether.
        //
        // The wandering ends on a real row on purpose, and this test would be
        // dishonest if it did not: a gesture that ends over nothing is a gesture
        // that writes nothing, which is the two tests below. What is asserted here
        // is that the journey is forgotten, not that the destination is.
        using var context = new BunitContext();
        TaskLink? link = null;
        var reordered = 0;

        IReadOnlyList<TaskRow> tasks =
        [
            new("a", "First", Group: "Tasks"),
            new("b", "Second", Group: "Tasks"),
            new("c", "Third", Done: true, Group: "Tasks"),
            new("d", "Fourth", Group: "Tasks")
        ];

        var view = Render(context, tasks, onLink: l => link = l, onReorder: _ => reordered++, groupCompleted: false);

        await view.InvokeAsync(() => view.Instance.PointerLinkStart("a"));

        foreach (var id in new string?[] { "b", "c", "a", null, "d", "b" })
        {
            await view.InvokeAsync(() => view.Instance.PointerDragOver(id));
        }

        // Ended on the second row, so that is the row the release is aimed at —
        // asserted before the release, because the drag's state is gone after it.
        Assert.Contains("task-item--link-armed", view.Find("[data-testid='list-b']").ClassList);

        await view.InvokeAsync(view.Instance.PointerDragEnd);

        Assert.Equal("a", link?.Id);
        Assert.Equal("b", link?.DependsOnId);
        Assert.Equal(0, reordered);
    }

    [Fact]
    public async Task A_link_released_on_the_row_it_is_carrying_writes_nothing_and_says_nothing()
    {
        // The defect. Press the first row's handle, cross the second row in
        // transit, come back and let go on the row in your hand. The second row
        // stayed armed here, because the report of the source row was thrown away
        // instead of clearing it — so the drop wrote "First waits for Second"
        // about a pair the reader had never aimed at, and announced it, which is
        // how they found out.
        //
        // A link target is the row under the pointer at the release and nothing
        // else. Every other reading of it is a reading of where the pointer used
        // to be, and this is the second time that has cost this feature a bug —
        // the first is written out on _linking.
        using var context = new BunitContext();
        var links = 0;
        var reorders = 0;

        var view = Render(context, Three, onLink: _ => links++, onReorder: _ => reorders++);

        await view.InvokeAsync(() => view.Instance.PointerLinkStart("a"));
        await view.InvokeAsync(() => view.Instance.PointerDragOver("b"));

        Assert.Contains("task-item--link-armed", view.Find("[data-testid='list-b']").ClassList);

        await view.InvokeAsync(() => view.Instance.PointerDragOver("a"));

        // Nothing is armed, anywhere. That is the state the drop reads and the
        // state the line reads — the row's tint and the line the script draws are
        // this one answer rendered twice, so a reader cannot be shown a row lit up
        // while the line has already gone quiet.
        Assert.Empty(view.FindAll(".task-item--link-armed"));

        // The row it picked up says what it has said all along, and never that it
        // is taking the drop.
        Assert.Contains("task-item--link-source", view.Find("[data-testid='list-a']").ClassList);

        await view.InvokeAsync(view.Instance.PointerDragEnd);

        Assert.Equal(0, links);
        Assert.Equal(0, reorders);

        // And nothing is said. An abandoned gesture is not a refusal: the reader
        // let go over nothing and already knows they did, so a sentence here would
        // be the app explaining a decision they had made themselves.
        Assert.Equal(string.Empty, Announcement(view));
    }

    [Fact]
    public async Task A_link_released_over_no_row_at_all_writes_nothing_and_says_nothing()
    {
        // The same defect by the other route, and the one that needed the script
        // to learn a new word. It reported entering a row and never leaving one,
        // so a reader who crossed the second row and then let go somewhere off the
        // list entirely got the same dependency they never aimed at.
        using var context = new BunitContext();
        var links = 0;
        var reorders = 0;

        var view = Render(context, Three, onLink: _ => links++, onReorder: _ => reorders++);

        await view.InvokeAsync(() => view.Instance.PointerLinkStart("a"));
        await view.InvokeAsync(() => view.Instance.PointerDragOver("b"));

        Assert.Contains("task-item--link-armed", view.Find("[data-testid='list-b']").ClassList);

        await view.InvokeAsync(() => view.Instance.PointerDragOver(null));

        Assert.Empty(view.FindAll(".task-item--link-armed"));

        // Still a link drag over rows that can still take one — the gesture has
        // not been cancelled, it simply has nowhere to land right now. Which is
        // also why the reader can carry on and land it: see the wander test above.
        Assert.Contains("task-list--linking", view.Find("ul.task-list").ClassList);
        Assert.Contains("task-item--link-target", view.Find("[data-testid='list-b']").ClassList);

        await view.InvokeAsync(view.Instance.PointerDragEnd);

        Assert.Equal(0, links);
        Assert.Equal(0, reorders);
        Assert.Equal(string.Empty, Announcement(view));
    }

    [Fact]
    public async Task A_cancelled_link_drag_writes_nothing_either()
    {
        using var context = new BunitContext();
        var links = 0;
        var reorders = 0;

        var view = Render(context, Three, onLink: _ => links++, onReorder: _ => reorders++);

        await view.InvokeAsync(() => view.Instance.PointerLinkStart("a"));
        await view.InvokeAsync(() => view.Instance.PointerDragOver("c"));
        await view.InvokeAsync(view.Instance.PointerDragCancel);

        Assert.Equal(0, links);
        Assert.Equal(0, reorders);
        Assert.Equal(string.Empty, Announcement(view));
    }

    [Fact]
    public void Every_row_that_can_be_made_to_wait_carries_the_handle_at_rest()
    {
        // At rest, not on hover and not once a drag is under way: an affordance
        // that only appears under the pointer cannot be found by anybody looking
        // for it. A finished row has none, because there is nothing left for it to
        // be waiting on.
        using var context = new BunitContext();

        IReadOnlyList<TaskRow> tasks =
        [
            new("a", "First", Group: "Tasks"),
            new("b", "Second", Done: true, Group: "Tasks"),
            new("c", "Third", Group: "Tasks")
        ];

        var view = Render(context, tasks, onLink: _ => { }, groupCompleted: false);

        Assert.Equal(["list-a-link", "list-c-link"],
            view.FindAll(".task-item__link").Select(handle => handle.GetAttribute("data-testid")));

        // The pointer's affordance and nothing else, exactly as the grip is: the
        // keyboard has a route of its own, so a tab stop here would be a second
        // way to reach something already reachable.
        Assert.Equal("true", view.Find(".task-item__link").GetAttribute("aria-hidden"));
    }

    [Fact]
    public async Task A_list_nobody_asked_for_links_draws_no_handle_and_still_reorders()
    {
        using var context = new BunitContext();
        TaskMove? move = null;
        var view = Render(context, Three, onReorder: m => move = m);

        Assert.Empty(view.FindAll(".task-item__link"));

        // And the entry point cannot be reached behind the missing handle either:
        // a script that outlived its parameters must not turn a press the reader
        // aimed at a handle into a move.
        await view.InvokeAsync(() => view.Instance.PointerLinkStart("a"));
        await view.InvokeAsync(() => view.Instance.PointerDragOver("c"));
        await view.InvokeAsync(view.Instance.PointerDragEnd);

        Assert.Null(move);

        await view.InvokeAsync(() => view.Instance.PointerDragStart("a"));
        await view.InvokeAsync(() => view.Instance.PointerDragOver("c"));
        await view.InvokeAsync(view.Instance.PointerDragEnd);

        Assert.Equal("c", move?.TargetId);
    }

    [Fact]
    public async Task A_list_that_does_not_reorder_can_still_have_its_chain_drawn()
    {
        // Which is the whole reason the link has a handle of its own rather than
        // being a region of a row that has to be draggable first.
        using var context = new BunitContext();
        TaskLink? link = null;

        var view = Render(context, Three, onLink: l => link = l, reorderable: false);

        Assert.Empty(view.FindAll(".task-item__grip"));
        Assert.Equal(3, view.FindAll(".task-item__link").Count);

        await view.InvokeAsync(() => view.Instance.PointerLinkStart("a"));
        await view.InvokeAsync(() => view.Instance.PointerDragOver("c"));
        await view.InvokeAsync(view.Instance.PointerDragEnd);

        Assert.Equal("c", link?.DependsOnId);
    }

    [Fact]
    public async Task While_a_link_drag_is_in_flight_every_row_says_what_it_is_to_it()
    {
        // The whole row is the drop target, so the row is what has to say whether
        // releasing on it will do anything.
        using var context = new BunitContext();

        IReadOnlyList<TaskRow> tasks =
        [
            new("a", "First", Group: "Tasks", DependsOn: ["b"]),
            new("b", "Second", Group: "Tasks"),
            new("c", "Third", Group: "Tasks")
        ];

        var view = Render(context, tasks, onLink: _ => { }, onReorder: _ => { });

        await view.InvokeAsync(() => view.Instance.PointerLinkStart("a"));

        Assert.Contains("task-list--linking", view.Find("ul.task-list").ClassList);
        Assert.Contains("task-item--link-source", view.Find("[data-testid='list-a']").ClassList);

        // Already waited on, so it will not take the drop; the third row will.
        Assert.Contains("task-item--link-refused", view.Find("[data-testid='list-b']").ClassList);
        Assert.Contains("task-item--link-target", view.Find("[data-testid='list-c']").ClassList);

        await view.InvokeAsync(view.Instance.PointerDragCancel);

        // A reorder drag decorates none of it: the two gestures do not describe
        // each other's rows.
        await view.InvokeAsync(() => view.Instance.PointerDragStart("a"));

        Assert.DoesNotContain("task-list--linking", view.Find("ul.task-list").ClassList);
        Assert.Contains("task-list--dragging", view.Find("ul.task-list").ClassList);
        Assert.Empty(view.FindAll(".task-item--link-target, .task-item--link-refused, .task-item--link-source"));
    }

    [Fact]
    public async Task Only_the_row_the_pointer_is_on_says_it_is_taking_the_drop()
    {
        // Armed is computed from the position that already decides where the drop
        // lands rather than from :hover — one answer to where the pointer is, and
        // the only form of it a test can see at all.
        using var context = new BunitContext();

        var view = Render(context, Three, onLink: _ => { });

        await view.InvokeAsync(() => view.Instance.PointerLinkStart("a"));
        await view.InvokeAsync(() => view.Instance.PointerDragOver("c"));

        Assert.Contains("task-item--link-armed", view.Find("[data-testid='list-c']").ClassList);
        Assert.DoesNotContain("task-item--link-armed", view.Find("[data-testid='list-b']").ClassList);

        await view.InvokeAsync(() => view.Instance.PointerDragOver("b"));

        Assert.Contains("task-item--link-armed", view.Find("[data-testid='list-b']").ClassList);
        Assert.DoesNotContain("task-item--link-armed", view.Find("[data-testid='list-c']").ClassList);

        // And no row at all once the pointer is over no row, or over the row the
        // gesture is carrying. Both are "nothing is about to happen", and a row
        // still lit under a pointer that has left it is the drop the reader did
        // not aim — see the two tests on releasing over each of them.
        await view.InvokeAsync(() => view.Instance.PointerDragOver(null));

        Assert.Empty(view.FindAll(".task-item--link-armed"));

        await view.InvokeAsync(() => view.Instance.PointerDragOver("c"));
        await view.InvokeAsync(() => view.Instance.PointerDragOver("a"));

        Assert.Empty(view.FindAll(".task-item--link-armed"));
    }

    /// <summary>
    /// The line the drag draws is drawn by <c>components.js</c>, per animation
    /// frame, from the pointer stream — there is no pointer in bUnit and no script
    /// running here, so what these two tests cover is the surface it is drawn on
    /// and not one pixel of the line. Which is the half that can go wrong
    /// silently: the overlay is looked up by the list id, the marks by class, and
    /// a rename on either side of that contract would leave the gesture working
    /// and the line simply absent.
    /// <para>
    /// That the curve tracks the pointer, snaps to a row's leading edge, and takes
    /// its colour from what that row is wearing is verifiable only by driving a
    /// browser.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_link_drag_hands_the_script_a_line_to_draw_and_takes_it_away_again()
    {
        using var context = new BunitContext();

        var view = Render(context, Three, onLink: _ => { }, onReorder: _ => { });

        // Nothing at rest. A viewport-sized overlay standing there between drags
        // would be a thing to keep pointer-transparent for the whole life of the
        // page rather than for the length of a gesture.
        Assert.Empty(view.FindAll("[data-testid='list-link-line']"));

        await view.InvokeAsync(() => view.Instance.PointerLinkStart("a"));

        var line = view.Find("[data-testid='list-link-line']");

        // The lookup key. The script holds the list id from the press that started
        // the drag and finds the overlay by it, so these two attributes are one
        // contract: two lists on a page can be showing the same tasks, and the
        // line belongs to the one whose handle was pressed.
        Assert.Equal(
            view.Find("ul.task-list").GetAttribute("data-list-owner"),
            line.GetAttribute("data-link-line"));

        // The two marks it writes into, and the fact that both start empty: a
        // frame with no script behind it draws nothing at all rather than a line
        // from a corner.
        Assert.Equal(string.Empty, line.QuerySelector(".task-list__link-line-path")!.GetAttribute("d"));
        Assert.Equal("0", line.QuerySelector(".task-list__link-line-dot")!.GetAttribute("r"));

        // The pointer's own feedback. What is about to happen is said in the live
        // region for anybody who cannot see it, so a screen reader announcing a
        // graphic as well would be the same fact twice.
        Assert.Equal("true", line.GetAttribute("aria-hidden"));

        // One of it, and still one after the row under the pointer changes. Every
        // reported row is a re-render, and a second overlay — or a replaced one —
        // would be a line drawn into an element the script is no longer writing to.
        await view.InvokeAsync(() => view.Instance.PointerDragOver("c"));

        Assert.Single(view.FindAll("[data-testid='list-link-line']"));

        // And gone with the gesture. Dropped here; cancelled below — Escape, a
        // cancelled pointer and the window losing focus all arrive as that one.
        await view.InvokeAsync(view.Instance.PointerDragEnd);

        Assert.Empty(view.FindAll("[data-testid='list-link-line']"));

        await view.InvokeAsync(() => view.Instance.PointerLinkStart("a"));
        await view.InvokeAsync(view.Instance.PointerDragCancel);

        Assert.Empty(view.FindAll("[data-testid='list-link-line']"));

        // A reorder drag draws no line, on the same terms as it decorates no rows:
        // the two gestures do not describe each other.
        await view.InvokeAsync(() => view.Instance.PointerDragStart("a"));

        Assert.Empty(view.FindAll("[data-testid='list-link-line']"));
    }

    [Fact]
    public async Task The_line_hangs_outside_the_list_so_no_row_can_be_laid_out_against_it()
    {
        // The other half of Nothing_moves_for_the_whole_of_a_link_drag, at the
        // level this can actually see. That test proves the order holds; a line
        // that took part in the list's layout could still move a row by a pixel at
        // the moment the reader is aiming at it, which is the same bug measured
        // more finely. The overlay is fixed to the viewport — see components.css —
        // and it is rendered outside the <ul>, so no row is its sibling and
        // nothing structural in the list's own styling can see it at all.
        using var context = new BunitContext();

        var view = Render(context, Three, onLink: _ => { });

        await view.InvokeAsync(() => view.Instance.PointerLinkStart("a"));
        await view.InvokeAsync(() => view.Instance.PointerDragOver("c"));

        var list = view.Find("ul.task-list");

        // Whether it reflows is CSS's answer and not one bUnit can compute. What is
        // asserted here is that the question cannot be asked of it: the overlay is
        // not in the list's subtree, and the list's children are rows and nothing
        // else — so no `:nth-child`, no `:last-child` and no sibling combinator in
        // the list's own styling can see it, and no row's box is beside it.
        Assert.Null(list.QuerySelector("[data-testid='list-link-line']"));
        Assert.Equal(["li", "li", "li"], list.Children.Select(child => child.LocalName));
        Assert.Single(view.FindAll("[data-testid='list-link-line']"));
    }

    [Fact]
    public void The_handle_reads_first_of_the_rows_controls_ahead_of_the_pencil()
    {
        // A handle you drag belongs beside what you are dragging rather than filed
        // among the buttons that act on it — it is the other half of the grip at
        // the row's other end, and everything after it is a click. So: after the
        // title, ahead of the pencil, and therefore ahead of the copy button and
        // the bin as well. Not inside the host's action slot, which declines a
        // press outright.
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var view = context.Render<TaskItem>(p => p
            .Add(i => i.Task, new TaskRow("a", "First"))
            .Add(i => i.LinkHandle, true)
            .Add(i => i.AllowCopy, true)
            .Add(i => i.OnRename, (TaskRename _) => { })
            .Add(i => i.OnDelete, (string _) => { })
            .Add(i => i.TestId, "row"));

        var row = view.Find("li.task-item");

        var order = row.Children
            .Select(child => child.ClassList.FirstOrDefault(name => name.StartsWith("task-item__", StringComparison.Ordinal)))
            .ToList();

        // Every control the row can carry is on this row, so these are three real
        // comparisons rather than three IndexOf(-1)s agreeing with each other.
        Assert.Contains("task-item__edit", order);
        Assert.Contains("task-item__copy", order);
        Assert.Contains("task-item__delete", order);

        Assert.True(order.IndexOf("task-item__link") < order.IndexOf("task-item__edit"));
        Assert.True(order.IndexOf("task-item__link") < order.IndexOf("task-item__copy"));
        Assert.True(order.IndexOf("task-item__link") < order.IndexOf("task-item__delete"));

        Assert.Null(row.QuerySelector(".task-item__actions .task-item__link"));
    }

    [Fact]
    public async Task A_finished_row_refuses_the_drop_and_says_so()
    {
        // Refusing rather than reinterpreting. Falling back to a reorder would be
        // the row doing something the reader did not aim at, on a row where
        // reordering means close to nothing anyway.
        using var context = new BunitContext();
        var links = 0;
        var reorders = 0;

        IReadOnlyList<TaskRow> tasks =
        [
            new("a", "First", Group: "Tasks"),
            new("b", "Second", Group: "Tasks"),
            new("c", "Third", Done: true, Group: "Tasks")
        ];

        var view = Render(context, tasks, onLink: _ => links++, onReorder: _ => reorders++, groupCompleted: false);

        await view.InvokeAsync(() => view.Instance.PointerLinkStart("a"));
        await view.InvokeAsync(() => view.Instance.PointerDragOver("c"));

        Assert.Contains("task-item--link-refused", view.Find("[data-testid='list-c']").ClassList);

        await view.InvokeAsync(view.Instance.PointerDragEnd);

        Assert.Equal(0, links);
        Assert.Equal(0, reorders);
        Assert.Equal("Third is finished, so nothing can wait for it.", Announcement(view));
    }

    [Fact]
    public async Task A_row_already_waited_on_refuses_the_drop_and_says_so()
    {
        using var context = new BunitContext();
        var links = 0;

        IReadOnlyList<TaskRow> tasks =
        [
            new("a", "First", Group: "Tasks", DependsOn: ["c"]),
            new("b", "Second", Group: "Tasks"),
            new("c", "Third", Group: "Tasks")
        ];

        var view = Render(context, tasks, onLink: _ => links++);

        await view.InvokeAsync(() => view.Instance.PointerLinkStart("a"));
        await view.InvokeAsync(() => view.Instance.PointerDragOver("c"));

        Assert.Contains("task-item--link-refused", view.Find("[data-testid='list-c']").ClassList);

        await view.InvokeAsync(view.Instance.PointerDragEnd);

        Assert.Equal(0, links);
        Assert.Equal("First already waits for Third.", Announcement(view));
    }

    [Fact]
    public async Task The_row_in_flight_is_the_payload_and_so_is_no_place_to_drop_it()
    {
        // Self-link needs no refusal of its own: the drag ignores being dragged
        // over the row it picked up, so the row keeps saying it is the source and
        // a release there commits nothing.
        using var context = new BunitContext();
        var links = 0;
        var reorders = 0;

        var view = Render(context, Three, onLink: _ => links++, onReorder: _ => reorders++);

        await view.InvokeAsync(() => view.Instance.PointerLinkStart("a"));
        await view.InvokeAsync(() => view.Instance.PointerDragOver("a"));

        // Still the source and never a target, however long the pointer sits on it.
        Assert.Contains("task-item--link-source", view.Find("[data-testid='list-a']").ClassList);
        Assert.DoesNotContain("task-item--link-target", view.Find("[data-testid='list-a']").ClassList);

        await view.InvokeAsync(view.Instance.PointerDragEnd);

        Assert.Equal(0, links);
        Assert.Equal(0, reorders);
    }

    [Fact]
    public async Task A_link_that_closes_a_loop_is_accepted_and_narrated()
    {
        // Cycles are flagged, never refused. Which edge in a loop is the wrong one
        // is a question only the person who wrote them can answer, so the link
        // lands and the rows grow the badge that says what happened — and the
        // sentence is the screen-reader half of that badge.
        using var context = new BunitContext();
        TaskLink? link = null;

        IReadOnlyList<TaskRow> tasks =
        [
            new("a", "First", Group: "Tasks"),
            new("b", "Second", Group: "Tasks"),
            new("c", "Third", Group: "Tasks", DependsOn: ["a"])
        ];

        var view = Render(context, tasks, onLink: l => link = l);

        await view.InvokeAsync(() => view.Instance.PointerLinkStart("a"));
        await view.InvokeAsync(() => view.Instance.PointerDragOver("c"));

        Assert.Contains("task-item--link-target", view.Find("[data-testid='list-c']").ClassList);

        await view.InvokeAsync(view.Instance.PointerDragEnd);

        Assert.Equal("a", link?.Id);
        Assert.Equal("c", link?.DependsOnId);
        Assert.Equal("First now waits for Third. This closes a loop.", Announcement(view));
    }

    [Fact]
    public void Alt_shift_arrow_links_where_alt_arrow_moves()
    {
        // A drag has no keyboard equivalent, so a gesture that was only a drag
        // would be a chain some people cannot write. Shift is what separates the
        // two, and one press does exactly one of them.
        using var context = new BunitContext();
        TaskLink? link = null;
        TaskMove? move = null;

        var view = Render(context, Three, onLink: l => link = l, onReorder: m => move = m);

        view.Find("[data-testid='list-a-open']")
            .KeyDown(new KeyboardEventArgs { Key = "ArrowDown", AltKey = true, ShiftKey = true });

        Assert.Equal("a", link?.Id);
        Assert.Equal("b", link?.DependsOnId);
        Assert.Null(move);
        Assert.Equal("First now waits for Second.", Announcement(view));

        view.Find("[data-testid='list-a-open']")
            .KeyDown(new KeyboardEventArgs { Key = "ArrowDown", AltKey = true });

        Assert.Equal("a", move?.Id);
        Assert.Equal("b", move?.TargetId);
    }

    [Fact]
    public void The_keyboard_walks_past_a_finished_row_to_find_something_to_wait_for()
    {
        // A list that leaves its done rows inline — the desktop pane's steps —
        // would otherwise offer a neighbour nothing can wait for, and the reader
        // would get a refusal instead of the link they asked for.
        using var context = new BunitContext();
        TaskLink? link = null;

        IReadOnlyList<TaskRow> tasks =
        [
            new("a", "First", Group: "Tasks"),
            new("b", "Second", Done: true, Group: "Tasks"),
            new("c", "Third", Group: "Tasks")
        ];

        var view = Render(context, tasks, onLink: l => link = l, groupCompleted: false);

        view.Find("[data-testid='list-a-open']")
            .KeyDown(new KeyboardEventArgs { Key = "ArrowDown", AltKey = true, ShiftKey = true });

        Assert.Equal("c", link?.DependsOnId);
    }

    [Fact]
    public void A_finished_row_asked_to_wait_is_told_why_rather_than_ignored()
    {
        // This used to be silence. A finished row declined the chord, the press
        // fell through to the reorder branch, and that declines a finished row too
        // — so the one refusal on this feature with no cursor and no class to wear
        // was also the one that said nothing at all.
        using var context = new BunitContext();
        var links = 0;
        var reorders = 0;

        IReadOnlyList<TaskRow> tasks =
        [
            new("a", "First", Group: "Tasks"),
            new("b", "Second", Done: true, Group: "Tasks"),
            new("c", "Third", Group: "Tasks")
        ];

        var view = Render(context, tasks, onLink: _ => links++, onReorder: _ => reorders++, groupCompleted: false);

        // No handle on the row, so the pointer is never offered this at all — the
        // chord is the only way to ask, and the only place an answer can come from.
        Assert.Null(view.Find("[data-testid='list-b']").QuerySelector(".task-item__link"));

        view.Find("[data-testid='list-b-open']")
            .KeyDown(new KeyboardEventArgs { Key = "ArrowDown", AltKey = true, ShiftKey = true });

        Assert.Equal(0, links);
        Assert.Equal(0, reorders);
        Assert.Equal("Second is finished, so it cannot be made to wait.", Announcement(view));
    }

    [Fact]
    public void Off_the_top_of_the_list_there_is_nothing_to_wait_for()
    {
        using var context = new BunitContext();
        var links = 0;
        var reorders = 0;

        var view = Render(context, Three, onLink: _ => links++, onReorder: _ => reorders++);

        view.Find("[data-testid='list-a-open']")
            .KeyDown(new KeyboardEventArgs { Key = "ArrowUp", AltKey = true, ShiftKey = true });

        Assert.Equal(0, links);
        Assert.Equal(0, reorders);
        Assert.Equal("Nothing above First to wait for.", Announcement(view));
    }

    [Fact]
    public void The_chord_is_heard_inside_an_always_open_rename_field_too()
    {
        // Which is a case the keyboard route exists for: with the title always a
        // field the row has no line left to press anything on.
        using var context = new BunitContext();
        TaskLink? link = null;

        var view = Render(context, Three, onLink: l => link = l, directRename: true);

        view.Find("[data-testid='list-a-rename']")
            .KeyDown(new KeyboardEventArgs { Key = "ArrowDown", AltKey = true, ShiftKey = true });

        Assert.Equal("a", link?.Id);
        Assert.Equal("b", link?.DependsOnId);
    }
}
