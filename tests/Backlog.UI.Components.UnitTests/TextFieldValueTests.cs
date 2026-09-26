using Backlog.UI.Components.Inputs;
using Bunit;
using Microsoft.AspNetCore.Components;

namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// What the field's render tree holds for its value, against what is on screen.
/// <para>
/// The field used to render <c>value="@Value"</c> beside a bare <c>@oninput</c>.
/// Nothing told the renderer that handler updates <c>value</c>, so every keystroke's
/// text was re-rendered and written into the live input a round trip late — over
/// whatever had been typed since, which on a slow circuit lost letters. The echo
/// itself is invisible here, because the markup comes out the same either way; the
/// browser trace is its evidence. What bUnit can see is the other half: the render
/// tree holding the text the input shows, and a host's value reaching it only when
/// the host changed it.
/// </para>
/// </summary>
public class TextFieldValueTests
{
    [Fact]
    public async Task Typed_text_is_the_value_the_field_holds()
    {
        using var context = new BunitContext();

        var field = context.Render<TextField>(parameters => parameters
            .Add(f => f.AriaLabel, "Title")
            .Add(f => f.Value, string.Empty));

        await field.Find("input").InputAsync(new ChangeEventArgs { Value = "Typed" });

        Assert.Equal("Typed", field.Find("input").GetAttribute("value"));
    }

    /// <summary>
    /// A host that commits on <c>onchange</c> passes a value it does not update while
    /// somebody types — the bulk date pickers pass <c>""</c> for good. A re-render of
    /// that host in the meantime, from its own <c>onkeydown</c> or anything else,
    /// brings the same value back, and writing it would wipe the typing.
    /// </summary>
    [Fact]
    public async Task A_host_rerender_with_its_unchanged_value_keeps_the_typed_text()
    {
        using var context = new BunitContext();

        var field = context.Render<TextField>(parameters => parameters
            .Add(f => f.AriaLabel, "Due")
            .Add(f => f.Value, string.Empty));

        await field.Find("input").InputAsync(new ChangeEventArgs { Value = "Half typ" });

        field.Render(parameters => parameters.Add(f => f.Value, string.Empty));

        Assert.Equal("Half typ", field.Find("input").GetAttribute("value"));
    }

    [Fact]
    public async Task A_value_the_host_changes_after_typing_replaces_the_text()
    {
        using var context = new BunitContext();

        var field = context.Render<TextField>(parameters => parameters
            .Add(f => f.AriaLabel, "Title")
            .Add(f => f.Value, "From the host"));

        await field.Find("input").InputAsync(new ChangeEventArgs { Value = "Typed over" });

        field.Render(parameters => parameters.Add(f => f.Value, string.Empty));

        Assert.Equal(string.Empty, field.Find("input").GetAttribute("value"));
    }

    [Fact]
    public async Task Each_keystroke_is_reported_through_value_changed()
    {
        using var context = new BunitContext();
        var reported = new List<string>();

        var field = context.Render<TextField>(parameters => parameters
            .Add(f => f.AriaLabel, "Title")
            .Add(f => f.Value, string.Empty)
            .Add(f => f.ValueChanged, (string value) => reported.Add(value)));

        await field.Find("input").InputAsync(new ChangeEventArgs { Value = "a" });
        await field.Find("input").InputAsync(new ChangeEventArgs { Value = "ab" });

        Assert.Equal(["a", "ab"], reported);
    }
}
