namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// A select has two shapes to be in, and the difference between them is not
/// decoration. Inline in a row of metadata the value is the only thing that
/// should occupy the row, so the control is bare and its name is there for a
/// screen reader. In a form the same control has to look like the fields around
/// it, or the two selects that decide which of those fields exist read as
/// captions nobody is invited to change.
/// <para>
/// So both shapes are pinned here, and pinned against each other: the point of
/// the second one is that it did not cost the first one anything.
/// </para>
/// </summary>
public sealed class SelectFieldShapeTests
{
    private static readonly IReadOnlyList<SelectorOption> Kinds =
    [
        new("Plugin", "Plugin"),
        new("McpServer", "MCP server")
    ];

    [Fact]
    public void The_inline_shape_is_a_wrapping_label_whose_name_is_only_read_aloud()
    {
        using var context = new BunitContext();

        var field = context.Render<SelectField>(parameters => parameters
            .Add(f => f.Label, "Kind")
            .Add(f => f.Options, Kinds));

        // The whole control is the label, so a click anywhere on it opens the
        // list - which is what makes a bare select usable at this size.
        var wrapper = field.Find("label");
        Assert.Equal("metadata-editor", wrapper.GetAttribute("class"));
        Assert.Equal("Kind", field.Find("label > span.sr-only").TextContent);

        var select = field.Find("select");
        Assert.Equal("metadata-editor__select", select.GetAttribute("class"));

        // Nothing visible names it, so the attributes have to.
        Assert.Equal("Kind", select.GetAttribute("aria-label"));
        Assert.Equal("Kind", select.GetAttribute("title"));

        // And no id, because there is no visible label to point one at.
        Assert.False(select.HasAttribute("id"));
        Assert.Empty(field.FindAll(".field"));
    }

    [Fact]
    public void The_form_shape_pairs_a_visible_label_with_a_bordered_control()
    {
        using var context = new BunitContext();

        var field = context.Render<SelectField>(parameters => parameters
            .Add(f => f.Form, true)
            .Add(f => f.Label, "Kind")
            .Add(f => f.Options, Kinds));

        var wrapper = field.Find("div");
        Assert.Equal("field field--select", wrapper.GetAttribute("class"));

        var label = field.Find("label.field__label");
        Assert.Equal("Kind", label.TextContent);

        var select = field.Find("select.field__input");

        // The pairing is the id, so the label has to actually point at this
        // control and not merely sit above it.
        Assert.Equal(select.GetAttribute("id"), label.GetAttribute("for"));
        Assert.False(string.IsNullOrWhiteSpace(select.GetAttribute("id")));

        // A name that can be read is the name. An aria-label beside it would
        // replace the words on the screen with a second copy of them.
        Assert.False(select.HasAttribute("aria-label"));
        Assert.False(select.HasAttribute("title"));
        Assert.Empty(field.FindAll(".sr-only"));
        Assert.Empty(field.FindAll(".metadata-editor"));
        Assert.Empty(field.FindAll(".metadata-editor__select"));
    }

    [Fact]
    public void The_form_shapes_help_text_is_wired_into_the_control_rather_than_left_beside_it()
    {
        using var context = new BunitContext();

        var field = context.Render<SelectField>(parameters => parameters
            .Add(f => f.Form, true)
            .Add(f => f.Label, "Hosts")
            .Add(f => f.HelpText, "Claude adds fields of its own.")
            .Add(f => f.Options, Kinds));

        var help = field.Find("p.field__help");
        Assert.Equal("Claude adds fields of its own.", help.TextContent);
        Assert.Equal(help.GetAttribute("id"), field.Find("select").GetAttribute("aria-describedby"));
    }

    /// <summary>
    /// The inline shape has nowhere to put a paragraph: it is one row high and
    /// the row belongs to the value. A host that sets help text on it has asked
    /// for the wrong shape, and silently growing the row would be a worse answer
    /// than ignoring it.
    /// </summary>
    [Fact]
    public void Help_text_belongs_to_the_form_shape_only()
    {
        using var context = new BunitContext();

        var field = context.Render<SelectField>(parameters => parameters
            .Add(f => f.Label, "Kind")
            .Add(f => f.HelpText, "Never shown.")
            .Add(f => f.Options, Kinds));

        Assert.Empty(field.FindAll(".field__help"));
        Assert.False(field.Find("select").HasAttribute("aria-describedby"));
    }

    [Fact]
    public void Every_part_of_the_form_shape_can_wear_the_hosts_own_names()
    {
        using var context = new BunitContext();

        var field = context.Render<SelectField>(parameters => parameters
            .Add(f => f.Form, true)
            .Add(f => f.BaseClass, "tools-form__field")
            .Add(f => f.LabelCssClass, "tools-form__label")
            .Add(f => f.SelectCssClass, "tools-form__select")
            .Add(f => f.Label, "Kind")
            .Add(f => f.Options, Kinds));

        Assert.Equal("tools-form__field", field.Find("div").GetAttribute("class"));
        Assert.NotNull(field.Find("label.tools-form__label"));
        Assert.NotNull(field.Find("select.tools-form__select"));

        // Replaced, not appended - the same contract EmptyState and Badge keep.
        Assert.Empty(field.FindAll(".field"));
        Assert.Empty(field.FindAll(".field__label"));
        Assert.Empty(field.FindAll(".field__input"));
    }

    [Fact]
    public void The_inline_shapes_own_names_are_replaceable_too()
    {
        using var context = new BunitContext();

        var field = context.Render<SelectField>(parameters => parameters
            .Add(f => f.BaseClass, "status-editor")
            .Add(f => f.SelectCssClass, "status-editor__select")
            .Add(f => f.Options, Kinds));

        Assert.Equal("status-editor", field.Find("label").GetAttribute("class"));
        Assert.NotNull(field.Find("select.status-editor__select"));
        Assert.Empty(field.FindAll(".metadata-editor"));
    }

    [Fact]
    public void Extra_classes_still_append_to_the_wrapper_in_both_shapes()
    {
        using var context = new BunitContext();

        var inline = context.Render<SelectField>(parameters => parameters
            .Add(f => f.CssClass, "metadata-editor--repo badge")
            .Add(f => f.Options, Kinds));

        // What RepositorySelector depends on: its badge styling on top of the
        // component's own class, in that order.
        Assert.Equal("metadata-editor metadata-editor--repo badge", inline.Find("label").GetAttribute("class"));

        var form = context.Render<SelectField>(parameters => parameters
            .Add(f => f.Form, true)
            .Add(f => f.CssClass, "tools-form__wide")
            .Add(f => f.Options, Kinds));

        Assert.Equal("field field--select tools-form__wide", form.Find("div").GetAttribute("class"));
    }

    [Fact]
    public void An_explicit_id_wins_over_the_one_the_form_shape_generates()
    {
        using var context = new BunitContext();

        var field = context.Render<SelectField>(parameters => parameters
            .Add(f => f.Form, true)
            .Add(f => f.Id, "tools-add-kind-select")
            .Add(f => f.Label, "Kind")
            .Add(f => f.HelpText, "Changes which fields the form asks for.")
            .Add(f => f.Options, Kinds));

        Assert.Equal("tools-add-kind-select", field.Find("select").GetAttribute("id"));
        Assert.Equal("tools-add-kind-select", field.Find("label.field__label").GetAttribute("for"));

        // The help text hangs off the same id, so a host that names one names both.
        Assert.Equal("tools-add-kind-select-help", field.Find("p.field__help").GetAttribute("id"));
    }

    [Fact]
    public void The_test_id_stays_on_the_wrapper_whichever_shape_is_rendered()
    {
        using var context = new BunitContext();

        var inline = context.Render<SelectField>(parameters => parameters
            .Add(f => f.TestId, "entry-priority-select")
            .Add(f => f.Options, Kinds));

        Assert.Equal("entry-priority-select", inline.Find("label").GetAttribute("data-testid"));

        var form = context.Render<SelectField>(parameters => parameters
            .Add(f => f.Form, true)
            .Add(f => f.TestId, "tools-add-kind")
            .Add(f => f.Options, Kinds));

        Assert.Equal("tools-add-kind", form.Find("div").GetAttribute("data-testid"));
    }

    [Fact]
    public void The_empty_option_and_the_change_it_reports_survive_the_new_shape()
    {
        using var context = new BunitContext();

        string? chosen = "Plugin";

        var field = context.Render<SelectField>(parameters => parameters
            .Add(f => f.Form, true)
            .Add(f => f.Label, "Kind of plugin")
            .Add(f => f.Options, Kinds)
            .Add(f => f.IncludeEmptyOption, true)
            .Add(f => f.EmptyLabel, "Plugin (default)")
            .Add(f => f.OnChanged, value => chosen = value));

        var options = field.FindAll("option");
        Assert.Equal(3, options.Count);
        Assert.Equal("Plugin (default)", options[0].TextContent);
        Assert.Equal(string.Empty, options[0].GetAttribute("value"));

        field.Find("select").Change("McpServer");
        Assert.Equal("McpServer", chosen);
    }

    /// <summary>
    /// A disabled select is disabled in either shape, and the form shape's own
    /// chrome is what shows it - which is the point of putting it on
    /// <c>field__input</c> rather than inventing a second disabled style.
    /// </summary>
    [Fact]
    public void A_disabled_select_is_disabled_in_the_form_shape_too()
    {
        using var context = new BunitContext();

        var field = context.Render<SelectField>(parameters => parameters
            .Add(f => f.Form, true)
            .Add(f => f.Label, "Kind")
            .Add(f => f.Disabled, true)
            .Add(f => f.Options, Kinds));

        Assert.True(field.Find("select.field__input").HasAttribute("disabled"));
    }

    /// <summary>
    /// A field with nothing to write in the label renders no caption above the
    /// control - and no empty aria-label either, which would be a name that says
    /// nothing while claiming the control has one. The same rule TextField
    /// follows.
    /// </summary>
    [Fact]
    public void A_field_with_no_label_renders_neither_a_caption_nor_an_empty_name()
    {
        using var context = new BunitContext();

        var field = context.Render<SelectField>(parameters => parameters
            .Add(f => f.Form, true)
            .Add(f => f.Label, "Pick one")
            .Add(f => f.Options, Kinds));

        Assert.NotNull(field.Find("label.field__label"));

        var unlabelled = context.Render<SelectField>(parameters => parameters
            .Add(f => f.Form, true)
            .Add(f => f.Label, string.Empty)
            .Add(f => f.Options, Kinds));

        Assert.Empty(unlabelled.FindAll("label"));
        Assert.False(unlabelled.Find("select").HasAttribute("aria-label"));
        Assert.False(unlabelled.Find("select").HasAttribute("title"));

        // The inline shape answers the same way, for the same reason: its name is
        // an attribute, and an attribute with nothing in it is not a name.
        var bare = context.Render<SelectField>(parameters => parameters
            .Add(f => f.Label, string.Empty)
            .Add(f => f.Options, Kinds));

        Assert.False(bare.Find("select").HasAttribute("aria-label"));
    }
}
