namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// The one destructive button, and the two hooks that let a row 2rem tall wear it.
///
/// <para>The library had grown a second answer to "what does deleting look like
/// here": <c>TaskItem</c> hand-rolled a <c>button</c> around a bare emoji, inside
/// the very library <see cref="DeleteButton"/> was added to stop that in. The
/// emoji is the half that renders differently on every platform, so the glyph is
/// what the convergence is for — the row's quiet-until-hover dressing is the
/// row's own and stays where it was.</para>
///
/// <para>Which is what <c>Bare</c> is: the glyph alone, with no word beside it and
/// no variant modifier to be overridden, on whatever stem <c>BaseClass</c> hands
/// over. <c>SharedControlAdoptionTests</c> cannot see any of this — its raw-control
/// rule reads <c>src/App</c> and <c>src/Modules</c>, and both components here live
/// in <c>src/Core</c> — so it is pinned by hand.</para>
/// </summary>
public sealed class DeleteButtonTests
{
    [Fact]
    public void The_default_delete_is_a_danger_button_with_a_drawn_bin_and_a_word()
    {
        // The four shipping call sites — the two roadmap editors, the entry footer,
        // the tools pane — render this, and the new hooks must leave them alone.
        using var context = new BunitContext();

        var view = context.Render<DeleteButton>(p => p.Add(b => b.TestId, "delete"));

        var button = view.Find("[data-testid='delete']");

        Assert.Equal("btn btn--danger", button.GetAttribute("class"));
        Assert.Equal("Delete", button.TextContent.Trim());
        Assert.NotNull(button.QuerySelector("span.btn__icon > svg"));
    }

    [Fact]
    public void Bare_leaves_the_glyph_alone_on_the_stem_the_host_handed_over()
    {
        // Exactly the class the row's stylesheet already names, and nothing else:
        // a modifier of ours left on the element would be a second dressing for
        // `.task-item__delete` to fight, and `btn--danger` is a filled red slab
        // where the row wants a quiet 2rem square.
        using var context = new BunitContext();

        var view = context.Render<DeleteButton>(p => p
            .Add(b => b.Bare, true)
            .Add(b => b.BaseClass, "task-item__delete")
            .Add(b => b.AriaLabel, "Delete Write it down")
            .Add(b => b.TestId, "delete"));

        var button = view.Find("[data-testid='delete']");

        Assert.Equal("task-item__delete", button.GetAttribute("class"));
        Assert.Equal("Delete Write it down", button.GetAttribute("aria-label"));
        Assert.Equal(string.Empty, button.TextContent.Trim());

        // The glyph is the whole button, so it is the only element in it — which is
        // what the stylesheet's `:only-child` rule leans on to take back the gap
        // `.btn__icon` keeps for a label that is not there.
        Assert.Single(button.Children);

        var glyph = button.QuerySelector("svg");

        Assert.NotNull(glyph);
        Assert.Equal("true", glyph!.GetAttribute("aria-hidden"));
        Assert.Equal("false", glyph.GetAttribute("focusable"));
    }

    [Fact]
    public void A_stem_handed_over_without_Bare_still_carries_the_variant()
    {
        // BaseClass is AppButton's passthrough and means what it means there: the
        // modifiers are derived from the name given, so nothing of `btn` is left.
        using var context = new BunitContext();

        var view = context.Render<DeleteButton>(p => p
            .Add(b => b.BaseClass, "chip")
            .Add(b => b.TestId, "delete"));

        Assert.Equal("chip chip--danger", view.Find("[data-testid='delete']").GetAttribute("class"));
    }

    [Fact]
    public void A_bare_delete_inside_a_clickable_row_reports_and_stops_there()
    {
        // StopPropagation was already a parameter, and it has to be: writing
        // `@onclick:stopPropagation` on a component is a compile error, so a delete
        // that also opened the row it sits in could not be one of ours.
        using var context = new BunitContext();
        var deleted = 0;
        var opened = 0;

        var view = context.Render<ClickableRowHarness>(p => p
            .Add(h => h.OnDelete, () => deleted++)
            .Add(h => h.OnOpen, () => opened++));

        view.Find("[data-testid='row-delete']").Click();

        Assert.Equal(1, deleted);
        Assert.Equal(0, opened);
    }

    // --- Adopted by the task row --------------------------------------------

    [Fact]
    public void The_task_rows_bin_is_the_shared_button_wearing_the_rows_own_class()
    {
        // The convergence itself. The rendered element is what it always was — one
        // button, a direct child of the row, carrying `task-item__delete` and
        // nothing else — because `components.js` excludes the bin from the row-drag
        // gesture by that selector and the stylesheet dresses it by that name.
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var view = context.Render<TaskItem>(p => p
            .Add(t => t.Task, new TaskRow("a", "Write it down"))
            .Add(t => t.OnDelete, _ => { })
            .Add(t => t.TestId, "row"));

        var bin = view.Find("[data-testid='row-delete']");

        Assert.Equal("BUTTON", bin.TagName);
        Assert.Equal("task-item__delete", bin.GetAttribute("class"));
        // The row itself, by what it is rather than by instance: `Find` hands back a
        // bUnit wrapper and `ParentElement` the element under it, so the two are never
        // the same object even when they are the same node.
        var row = bin.ParentElement;

        Assert.NotNull(row);
        Assert.Equal("LI", row!.TagName);
        Assert.Contains("task-item", row.ClassList);

        // DeleteButton's drawn bin, not a glyph of the row's own: a lid, a bin and
        // the two lines down its face, in currentColor.
        var glyph = bin.QuerySelector("svg");

        Assert.NotNull(glyph);
        Assert.Equal("currentColor", glyph!.GetAttribute("stroke"));
        Assert.Equal(5, glyph.QuerySelectorAll("path").Length);
    }

    [Fact]
    public void A_glyph_with_no_word_after_it_keeps_no_gap_after_it()
    {
        // `.btn__icon` holds a gap open for the label beside it, and a bare button
        // has no label: the gap becomes padding down one side of a 2rem square and
        // the glyph sits off centre in it. Asserted against the stylesheet for the
        // reason FocusVisibilityTests gives — the markup is not in question, and
        // bUnit brings no layout engine to see what the cascade did with it.
        var css = Css();

        var start = css.IndexOf("\n.btn__icon--alone {", StringComparison.Ordinal);
        Assert.True(start >= 0, "components.css has no rule for .btn__icon--alone.");

        var close = css.IndexOf('}', start);
        Assert.True(close > start, "The rule for .btn__icon--alone in components.css is never closed.");
        Assert.Matches(@"margin-right:\s*0", css[start..close]);

        // And it is keyed on the marker the button supplies, never on the shape of
        // the DOM: `:only-child` counts elements and the label is a text node, so
        // that form of the rule matched every labelled icon button too.
        Assert.DoesNotContain(".btn__icon:only-child", css, StringComparison.Ordinal);
    }

    private static string Css() => File.ReadAllText(RepositoryRoot.File(
        "src", "Core", "Backlog.UI.Components", "wwwroot", "components.css")).Replace("\r\n", "\n");

    // --- The gap the icon keeps for a label ---------------------------------

    /// <summary>
    /// Bare marks the glyph as the whole button, so the stylesheet can close the
    /// gap the icon keeps for a word beside it.
    /// </summary>
    [Fact]
    public void A_bare_button_marks_its_icon_as_having_no_label()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var view = context.Render<DeleteButton>(p => p
            .Add(b => b.Bare, true)
            .Add(b => b.AriaLabel, "Delete it"));

        var icon = view.Find(".btn__icon");

        Assert.Contains("btn__icon--alone", icon.ClassList);
    }

    /// <summary>
    /// The regression the first attempt at that rule caused. A label is a text node
    /// rather than an element, so <c>.btn__icon--alone</c> held on a labelled
    /// icon button too and closed the gap on every one of them -- twelve on the
    /// Buttons storybook page alone. The marker has to come from the button, which
    /// is the only thing that knows whether a word follows the glyph.
    /// </summary>
    [Fact]
    public void A_labelled_button_keeps_the_gap_between_its_icon_and_its_word()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var view = context.Render<DeleteButton>(p => p
            .AddChildContent("Delete"));

        var icon = view.Find(".btn__icon");

        Assert.DoesNotContain("btn__icon--alone", icon.ClassList);

        // The label really is a bare text node beside the icon -- which is why the
        // selector could not tell the two cases apart on its own.
        Assert.Equal("Delete", view.Find("button").TextContent.Trim());
        Assert.Single(view.FindAll("button > *"));
    }
}
