namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// <c>MarkdownEditor.Label</c> is the hook that lets a form host adopt the editor
/// without hand-rolling a <c>field</c> around it, which is the copy the
/// shared-control rule forbids. What these pin is that the label is a real one —
/// wired to the textarea, so it is the box's accessible name rather than a
/// caption floating above it — and that asking for one changes nothing for the
/// hosts that did not.
/// </summary>
public sealed class MarkdownEditorLabelTests
{
    [Fact]
    public void A_label_is_wired_to_the_textarea_and_replaces_the_aria_label()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var view = context.Render<MarkdownEditor>(parameters => parameters
            .Add(e => e.Label, "Notes")
            .Add(e => e.AriaLabel, "Markdown source")
            .Add(e => e.TestId, "add-notes"));

        var label = view.Find("label.field__label");
        Assert.Equal("Notes", label.TextContent);

        var textarea = view.Find("textarea");
        var id = textarea.GetAttribute("id");
        Assert.False(string.IsNullOrWhiteSpace(id));
        Assert.Equal(id, label.GetAttribute("for"));

        // One name, not two: the visible label is the accessible one.
        Assert.Null(textarea.GetAttribute("aria-label"));
    }

    [Fact]
    public void The_host_may_name_the_textarea_itself()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var view = context.Render<MarkdownEditor>(parameters => parameters
            .Add(e => e.Label, "Notes")
            .Add(e => e.Id, "inbox-pane-add-notes-input"));

        Assert.Equal("inbox-pane-add-notes-input", view.Find("textarea").GetAttribute("id"));
        Assert.Equal("inbox-pane-add-notes-input", view.Find("label").GetAttribute("for"));
    }

    [Fact]
    public void A_labelled_editor_stands_inside_the_shared_field_layout()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var view = context.Render<MarkdownEditor>(parameters => parameters
            .Add(e => e.Label, "Notes")
            .Add(e => e.TestId, "add-notes"));

        // The same wrapper TextArea draws, so the editor lines up with the
        // labelled fields beside it. The test id stays on the editor, where every
        // existing locator — `[data-testid=…] textarea` — expects it.
        var field = view.Find("div.field");
        var editor = Assert.Single(view.FindAll("[data-testid=\"add-notes\"]"));
        Assert.Contains("markdown-editor", editor.ClassList);
        Assert.Contains(editor, field.Children);
        Assert.NotNull(view.Find("[data-testid=\"add-notes\"] textarea"));
    }

    [Fact]
    public void Without_a_label_nothing_changes()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var view = context.Render<MarkdownEditor>(parameters => parameters
            .Add(e => e.AriaLabel, "Task body")
            .Add(e => e.TestId, "body-editor"));

        Assert.Empty(view.FindAll("label"));
        Assert.Empty(view.FindAll(".field"));
        Assert.Equal("Task body", view.Find("textarea").GetAttribute("aria-label"));
    }
}
