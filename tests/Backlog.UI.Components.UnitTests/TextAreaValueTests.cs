namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// The text reaches the textarea as its <c>value</c>, never as its content.
/// <para>
/// A textarea's content is its <em>default</em> value, and a browser stops
/// showing the default the moment somebody types in the box. Blazor updates
/// content by rewriting a text node, so a host that changed the text after the
/// first keystroke — the Tasks pane reloading an entry an MCP <c>comment</c> had
/// just written to — changed nothing on screen, and the reader's next keystroke
/// reported the old text back and saved it over the comment. The renderer writes
/// only a <c>value</c> attribute to the element's live value.
/// </para>
/// <para>
/// bUnit's DOM has no dirty flag, so what these can pin is the form the text
/// arrives in; the typed-then-changed box itself is a browser's to show.
/// </para>
/// </summary>
public sealed class TextAreaValueTests
{
    [Fact]
    public void The_text_is_the_textareas_value()
    {
        using var context = new BunitContext();

        var field = context.Render<TextArea>(parameters => parameters.Add(f => f.Value, "Typed words."));

        var textarea = field.Find("textarea");
        Assert.Equal("Typed words.", textarea.GetAttribute("value"));
        Assert.Equal(string.Empty, textarea.TextContent);
    }

    [Fact]
    public void A_host_that_changes_the_text_changes_the_value_rather_than_the_default()
    {
        using var context = new BunitContext();

        var field = context.Render<TextArea>(parameters => parameters.Add(f => f.Value, "Typed words."));

        field.Render(parameters => parameters.Add(f => f.Value, "Typed words.\n\n2026-09-26: A comment."));

        field.WaitForAssertion(() =>
        {
            var textarea = field.Find("textarea");
            Assert.Equal("Typed words.\n\n2026-09-26: A comment.", textarea.GetAttribute("value"));
            Assert.Equal(string.Empty, textarea.TextContent);
        });
    }
}
