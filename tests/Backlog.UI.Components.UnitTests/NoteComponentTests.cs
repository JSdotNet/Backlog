using Backlog.UI.Components.Feedback;
using Backlog.UI.Components.Notes;

namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// The parts of the phone's Notes tab on their own: a row that is one link to
/// its note, and the editor whose only word on saving is the line at its top.
/// </summary>
public sealed class NoteComponentTests
{
    [Fact]
    public void A_row_is_one_link_with_the_title_the_time_and_the_snippet()
    {
        using var context = new BunitContext();

        var row = context.Render<NoteRow>(parameters => parameters
            .Add(p => p.Title, "Conflict cases from the call")
            .Add(p => p.When, "09:50")
            .Add(p => p.Snippet, "Last write wins for the title.")
            .Add(p => p.Href, "notes/42")
            .Add(p => p.TestId, "row"));

        var link = row.Find("a.note-row");
        Assert.Equal("notes/42", link.GetAttribute("href"));
        Assert.Equal("row", link.GetAttribute("data-testid"));
        Assert.Equal("Conflict cases from the call", row.Find("[data-testid='row-title']").TextContent);
        Assert.Equal("09:50", row.Find("[data-testid='row-when']").TextContent);
        Assert.Equal("Last write wins for the title.", row.Find("[data-testid='row-snippet']").TextContent);
        Assert.Empty(row.FindAll("button"));
    }

    [Fact]
    public void A_row_with_no_body_is_a_title_line_alone()
    {
        using var context = new BunitContext();

        var row = context.Render<NoteRow>(parameters => parameters
            .Add(p => p.Title, "Books to read")
            .Add(p => p.Href, "notes/7")
            .Add(p => p.TestId, "row"));

        Assert.Empty(row.FindAll("[data-testid='row-snippet']"));
        Assert.Empty(row.FindAll("[data-testid='row-when']"));
    }

    [Theory]
    [InlineData(SaveState.Saving, false, "Saving...", "false")]
    [InlineData(SaveState.Saved, false, "Saved", "false")]
    [InlineData(SaveState.Saved, true, "Waiting to sync", "true")]
    public void The_save_line_says_saving_saved_or_waiting_to_sync(SaveState state, bool waiting, string words, string flag)
    {
        using var context = new BunitContext();

        var editor = context.Render<NoteEditor>(parameters => parameters
            .Add(p => p.Title, "Standup")
            .Add(p => p.State, state)
            .Add(p => p.Waiting, waiting)
            .Add(p => p.TestId, "editor"));

        var save = editor.Find("[data-testid='editor-save']");
        Assert.Equal(words, save.TextContent.Trim());
        Assert.Equal(flag, save.GetAttribute("data-waiting"));
        Assert.Equal("status", save.GetAttribute("role"));
    }

    [Fact]
    public void A_note_not_yet_saved_has_no_save_line_and_no_time()
    {
        using var context = new BunitContext();

        var editor = context.Render<NoteEditor>(parameters => parameters.Add(p => p.TestId, "editor"));

        Assert.Empty(editor.FindAll("[data-testid='editor-save']"));
        Assert.Empty(editor.FindAll("[data-testid='editor-when']"));
        Assert.Equal("Back to Notes", editor.Find("[data-testid='editor-back']").GetAttribute("aria-label"));
        Assert.Equal("notes", editor.Find("[data-testid='editor-back']").GetAttribute("href"));
    }

    [Fact]
    public void Typing_raises_the_title_and_the_body_and_leaving_a_field_raises_blur()
    {
        using var context = new BunitContext();
        string? title = null;
        string? body = null;
        var blurs = 0;

        var editor = context.Render<NoteEditor>(parameters => parameters
            .Add(p => p.TitleChanged, value => title = value)
            .Add(p => p.BodyChanged, value => body = value)
            .Add(p => p.OnBlur, () => blurs++)
            .Add(p => p.When, "Wed 7 Oct · 09:50")
            .Add(p => p.TestId, "editor"));

        editor.Find("[data-testid='editor-title'] input").Input("Standup");
        editor.Find("[data-testid='editor-body'] textarea").Input("- shipped sync");
        editor.Find("[data-testid='editor-body'] textarea").Blur();

        Assert.Equal("Standup", title);
        Assert.Equal("- shipped sync", body);
        Assert.Equal(1, blurs);
        Assert.Equal("Title", editor.Find("[data-testid='editor-title'] input").GetAttribute("aria-label"));
        Assert.Equal("Note", editor.Find("[data-testid='editor-body'] textarea").GetAttribute("aria-label"));
        Assert.Equal("Wed 7 Oct · 09:50", editor.Find("[data-testid='editor-when']").TextContent);
        Assert.Empty(editor.FindAll("[role='toolbar']"));
    }
}
