namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// Editing the body of a row, which is a different bargain from renaming its
/// title. The rename holds a draft and reports once; this holds nothing and
/// reports every keystroke, so most of what is proved here is an absence: no
/// draft, no save button, no Escape, and no editor at all when nobody is
/// listening.
/// </summary>
public sealed class TaskBodyEditTests
{
    private const string Body = """
        Draft the release note for this change.

        Lead with what somebody can now do that they could not do before.
        """;

    private static IRenderedComponent<TaskItem> Open(
        BunitContext context,
        TaskRow task,
        Action<TaskBodyChange>? onBodyChanged)
    {
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        return context.Render<TaskItem>(p =>
        {
            p.Add(t => t.Task, task)
             .Add(t => t.BodyLabel, "Prompt")
             .Add(t => t.BodyExpanded, true)
             .Add(t => t.TestId, "row");

            if (onBodyChanged is not null) p.Add(t => t.OnBodyChanged, onBodyChanged);
        });
    }

    [Fact]
    public void Editing_starts_off_and_the_toggle_swaps_the_rendered_body_for_the_editor()
    {
        // Reading is the common case, and a list of ten prompts that opened ten
        // textareas is not a list anybody skims.
        using var context = new BunitContext();

        var view = Open(context, new TaskRow("a", "Draft the release note", Body: Body), _ => { });

        var toggle = view.Find("[data-testid='row-body-edit']");

        Assert.Equal("false", toggle.GetAttribute("aria-pressed"));
        Assert.Equal("Edit Prompt", toggle.TextContent.Replace("✎", string.Empty, StringComparison.Ordinal).Trim());
        Assert.NotEmpty(view.FindAll("[data-testid='row-body'] .md-p"));
        Assert.Empty(view.FindAll("[data-testid='row-body-editor']"));

        toggle.Click();

        // Pressed rather than renamed: the button does the same thing to the same
        // body either way, so its name is the same on the way back.
        Assert.Equal("true", view.Find("[data-testid='row-body-edit']").GetAttribute("aria-pressed"));
        Assert.Equal("Edit Prompt", view.Find("[data-testid='row-body-edit']").TextContent.Replace("✎", string.Empty, StringComparison.Ordinal).Trim());

        Assert.Empty(view.FindAll("[data-testid='row-body']"));

        var editor = view.Find("[data-testid='row-body-editor'] textarea");

        Assert.Equal(Body, editor.TextContent);
        Assert.Equal("Prompt for Draft the release note", editor.GetAttribute("aria-label"));
    }

    [Fact]
    public void A_keystroke_is_reported_and_the_row_keeps_none_of_it()
    {
        using var context = new BunitContext();

        var row = new TaskRow("a", "Draft the release note", Body: "Before.");
        var reported = new List<TaskBodyChange>();

        var view = Open(context, row, reported.Add);
        view.Find("[data-testid='row-body-edit']").Click();
        view.Find("[data-testid='row-body-editor'] textarea").Input("After.");

        var change = Assert.Single(reported);
        Assert.Equal("a", change.Id);
        Assert.Equal("After.", change.Body);

        // The row it was handed is untouched. Typing is something that happened to
        // the textarea and was reported; it is not something the row recorded.
        Assert.Equal("Before.", row.Body);

        // And there is no draft here that could outrank the host. Change the body
        // from the host's side and the editor shows the host's text, because Task
        // is the only place a body lives.
        view.Render(p => p
            .Add(t => t.Task, row with { Body = "From the host." })
            .Add(t => t.BodyLabel, "Prompt")
            .Add(t => t.OnBodyChanged, reported.Add)
            .Add(t => t.TestId, "row"));

        Assert.Equal("From the host.", view.Find("[data-testid='row-body-editor'] textarea").TextContent);
    }

    [Fact]
    public void Every_keystroke_is_reported_without_any_gesture_that_commits()
    {
        using var context = new BunitContext();

        var reported = new List<string>();

        var view = Open(
            context,
            new TaskRow("a", "Draft the release note", Body: "A"),
            change => reported.Add(change.Body));

        view.Find("[data-testid='row-body-edit']").Click();

        view.Find("[data-testid='row-body-editor'] textarea").Input("Ab");
        view.Find("[data-testid='row-body-editor'] textarea").Input("Abc");
        view.Find("[data-testid='row-body-editor'] textarea").Input("Abcd");

        // Three changes, no Enter, no blur, no button. A body is multi-line
        // markdown where Enter is a newline, so there is no keystroke left over
        // that could mean "done" — and the product has no save button anywhere.
        Assert.Equal(["Ab", "Abc", "Abcd"], reported);
        Assert.Empty(view.FindAll("button[type='submit']"));

        // Escape is deliberately not a gesture here, and the proof is that the
        // body has no key handler at all to reach. The rename holds a draft, so it
        // has something to throw away; this row holds nothing, so an Escape that
        // put the old text back would have to be an undo the host implements.
        Assert.Throws<MissingEventHandlerException>(() =>
            view.Find("[data-testid='row-body-editor'] textarea").KeyDown(new KeyboardEventArgs { Key = "Escape" }));

        Assert.Equal(3, reported.Count);
        Assert.NotEmpty(view.FindAll("[data-testid='row-body-editor']"));
    }

    [Fact]
    public void With_nobody_listening_the_body_is_read_only_rather_than_disabled()
    {
        using var context = new BunitContext();

        var view = Open(context, new TaskRow("a", "Draft the release note", Body: Body), onBodyChanged: null);

        // No toggle and no editor at all. A disabled editor would be a control
        // that takes up room saying it cannot do the thing it names.
        Assert.Empty(view.FindAll("[data-testid='row-body-edit']"));
        Assert.Empty(view.FindAll("[data-testid='row-body-editor']"));
        Assert.Empty(view.FindAll("textarea"));

        // The body is still there, still rendered as markdown.
        Assert.NotEmpty(view.FindAll("[data-testid='row-body'] .md-p"));
    }

    [Fact]
    public void Emptying_the_body_does_not_close_the_editor_it_was_typed_in()
    {
        using var context = new BunitContext();

        var task = new TaskRow("a", "Draft the release note", Body: "Before.");

        var view = Open(context, task, change => task = task with { Body = change.Body });

        view.Find("[data-testid='row-body-edit']").Click();
        view.Find("[data-testid='row-body-editor'] textarea").Input(string.Empty);

        // The host applied it, so the row now has no body at all.
        Assert.False(task.HasBody);

        view.Render(p => p
            .Add(t => t.Task, task)
            .Add(t => t.BodyLabel, "Prompt")
            .Add(t => t.OnBodyChanged, change => task = task with { Body = change.Body })
            .Add(t => t.TestId, "row"));

        // Selecting the whole body and deleting it is the one keystroke that must
        // not close the thing it was typed into.
        Assert.False(view.Find(".fold__region").HasAttribute("hidden"));
        Assert.NotEmpty(view.FindAll("[data-testid='row-body-editor']"));
        Assert.Contains("task-item--has-body", view.Find("li.task-item").ClassList);

        // Stop editing with nothing in it and the region goes: an empty body is no
        // body, and the fold is only ever there because there is one.
        view.Find("[data-testid='row-body-edit']").Click();

        Assert.Empty(view.FindAll(".fold__region"));
        Assert.Empty(view.FindAll("[data-testid='row-body-toggle']"));
    }

    [Fact]
    public void A_title_only_row_offers_no_body_to_edit_even_with_a_host_listening()
    {
        // Whether a task may have a body at all is the host's call. A fold on
        // every row would be this component offering "add a prompt" to "Ring the
        // dentist".
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var view = context.Render<TaskItem>(p => p
            .Add(t => t.Task, new TaskRow("a", "Ring the dentist"))
            .Add(t => t.OnToggle, _ => { })
            .Add(t => t.OnBodyChanged, _ => { })
            .Add(t => t.TestId, "row"));

        Assert.Empty(view.FindAll(".fold__region"));
        Assert.Empty(view.FindAll("[data-testid='row-body-toggle']"));
        Assert.Empty(view.FindAll("[data-testid='row-body-edit']"));
        Assert.Empty(view.FindAll("[data-testid='row-body-editor']"));
        Assert.Empty(view.FindAll(".task-item__next"));
        Assert.Empty(view.FindAll(".task-item__ready"));
        Assert.DoesNotContain("task-item--has-body", view.Find("li.task-item").ClassList);
    }

    [Fact]
    public void The_copy_button_hands_over_the_body_that_is_on_screen()
    {
        // The reason per-keystroke is not merely convenient: CopyText reads
        // Task.Body, so under a blur-commit editor this would copy the text from
        // before the reader started typing.
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.JSInterop.Setup<bool>("backlogClipboard.copy", _ => true).SetResult(true);

        var task = new TaskRow("a", "Draft the release note", Body: "Before.");

        var view = Open(context, task, change => task = task with { Body = change.Body });

        view.Find("[data-testid='row-body-edit']").Click();
        view.Find("[data-testid='row-body-editor'] textarea").Input("After.");

        view.Render(p => p
            .Add(t => t.Task, task)
            .Add(t => t.BodyLabel, "Prompt")
            .Add(t => t.OnBodyChanged, change => task = task with { Body = change.Body })
            .Add(t => t.TestId, "row"));

        view.Find("[data-testid='row-copy']").Click();

        Assert.Equal(
            "Draft the release note\n\nAfter.",
            Assert.Single(context.JSInterop.Invocations["backlogClipboard.copy"]).Arguments[0]);
    }

    [Fact]
    public void The_list_hands_the_listener_to_every_row_or_to_none()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        IReadOnlyList<TaskRow> tasks =
        [
            new("a", "Draft the release note", Body: "One."),
            new("b", "Summarise the findings", Body: "Two.")
        ];

        var listening = context.Render<TaskListView>(p => p
            .Add(l => l.Tasks, tasks)
            .Add(l => l.BodyLabel, "Prompt")
            .Add(l => l.OnBodyChanged, _ => { })
            .Add(l => l.TestId, "list"));

        Assert.Equal(2, listening.FindAll("[data-testid$='-body-edit']").Count);

        var silent = context.Render<TaskListView>(p => p
            .Add(l => l.Tasks, tasks)
            .Add(l => l.BodyLabel, "Prompt")
            .Add(l => l.TestId, "list"));

        Assert.Empty(silent.FindAll("[data-testid$='-body-edit']"));
    }
}
