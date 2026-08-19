using Backlog.UI.Components.Inputs;
using Bunit;

namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// Which input types the shared field will actually wear.
/// <para>
/// The allowlist is not decoration: an unknown type falls back to text, so a host
/// asking for one the field does not know gets a plain box and no error. That is
/// the right failure mode and a quiet one — the storybook asked for
/// <c>Type="date"</c> and rendered a text box for as long as that story existed,
/// with nothing anywhere to say so. These are what say so.
/// </para>
/// </summary>
public class TextFieldTypeTests
{
    [Theory]
    [InlineData("text")]
    [InlineData("number")]
    [InlineData("url")]
    [InlineData("email")]
    [InlineData("date")]
    [InlineData("datetime-local")]
    public void A_known_type_reaches_the_input(string type)
    {
        using var context = new BunitContext();

        var field = context.Render<TextField>(parameters => parameters
            .Add(f => f.Type, type)
            .Add(f => f.AriaLabel, "A field"));

        Assert.Equal(type, field.Find("input").GetAttribute("type"));
    }

    [Theory]
    [InlineData("color")]
    [InlineData("password")]
    [InlineData("file")]
    public void An_unknown_type_falls_back_to_text(string type)
    {
        using var context = new BunitContext();

        var field = context.Render<TextField>(parameters => parameters
            .Add(f => f.Type, type)
            .Add(f => f.AriaLabel, "A field"));

        Assert.Equal("text", field.Find("input").GetAttribute("type"));
    }

    /// <summary>The value stays the native wire format on the way in and out —
    /// yyyy-MM-dd for a date, and yyyy-MM-ddTHH:mm for a local instant. What the
    /// date is called on screen is the browser's business from the locale; the
    /// value never is.</summary>
    [Fact]
    public void A_date_field_carries_the_wire_format_value()
    {
        using var context = new BunitContext();

        var field = context.Render<TextField>(parameters => parameters
            .Add(f => f.Type, "date")
            .Add(f => f.Value, "2026-08-21")
            .Add(f => f.AriaLabel, "Due date"));

        Assert.Equal("2026-08-21", field.Find("input").GetAttribute("value"));
    }

    /// <summary>A host that needs the committed value rather than every keystroke
    /// splats its own onchange, which is what a date field wants: an incomplete
    /// date raises input events reading as empty.</summary>
    [Fact]
    public void A_splatted_onchange_reaches_the_input()
    {
        using var context = new BunitContext();

        string? committed = null;

        var field = context.Render<TextField>(parameters => parameters
            .Add(f => f.Type, "date")
            .Add(f => f.AriaLabel, "Due date")
            .AddUnmatched("onchange", EventCallback.Factory.Create<ChangeEventArgs>(
                new object(),
                e => committed = e.Value?.ToString())));

        field.Find("input").Change("2026-08-21");

        Assert.Equal("2026-08-21", committed);
    }
}
