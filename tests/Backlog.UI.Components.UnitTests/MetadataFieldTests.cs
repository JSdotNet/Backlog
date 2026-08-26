namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// One row of a metadata record, and the record that is made of them.
///
/// <para><c>MetadataViewTests</c> asserts the record field by field. What is
/// added here is the row itself: that the pair a screen reader depends on is
/// drawn once rather than five times, and that the record still hands each field
/// the same three elements it used to build for itself.</para>
/// </summary>
public sealed class MetadataFieldTests
{
    [Fact]
    public void A_field_is_a_label_and_a_value_inside_one_row()
    {
        using var context = new BunitContext();

        var field = context.Render<MetadataField>(parameters => parameters
            .Add(row => row.Label, "depends-on")
            .Add(row => row.ChildContent, "<code class=\"knowledge-value\">dotnet</code>"));

        Assert.Equal("depends-on", field.Find("div.knowledge-fields__row > dt.knowledge-fields__label").TextContent);
        Assert.NotNull(field.Find("div.knowledge-fields__row > dd.knowledge-fields__value > code.knowledge-value"));

        // Most rows are found by their label, so the value cell carries no test id
        // unless a field asks for one.
        Assert.Null(field.Find("dd").GetAttribute("data-testid"));
    }

    [Fact]
    public void A_field_that_asks_for_a_test_id_gets_it_on_the_values()
    {
        using var context = new BunitContext();

        var field = context.Render<MetadataField>(parameters => parameters
            .Add(row => row.Label, "feature-flag")
            .Add(row => row.TestId, "knowledge-feature-flag-tags"));

        // On the cell holding the values, not on the row: what a caller wants to
        // reach is the badges.
        Assert.Equal("knowledge-feature-flag-tags", field.Find("dd").GetAttribute("data-testid"));
        Assert.Null(field.Find("div").GetAttribute("data-testid"));
    }

    [Fact]
    public void A_hidden_label_is_still_in_the_list_and_still_has_its_name()
    {
        // The whole of what ShowLabel does. A value that says which field it came
        // from does not need the word on screen; somebody listening still does, so
        // the dt stays and takes sr-only rather than being dropped.
        using var context = new BunitContext();

        var field = context.Render<MetadataField>(parameters => parameters
            .Add(row => row.Label, "version")
            .Add(row => row.ShowLabel, false));

        var label = field.Find("dt");
        Assert.Equal("version", label.TextContent);
        Assert.Equal("knowledge-fields__label sr-only", label.GetAttribute("class"));

        // The modifier is what lets the stylesheet hand the value the column the
        // hidden label is no longer holding.
        Assert.Equal(
            "knowledge-fields__row knowledge-fields__row--bare",
            field.Find("div").GetAttribute("class"));
    }

    [Fact]
    public void The_bare_modifier_is_derived_from_whatever_base_class_the_host_gave()
    {
        // A host laying these out under its own names gets a modifier in its own
        // namespace rather than one of ours — the same rule Badge follows for
        // BaseClass. With no base class there is no modifier to derive.
        using var context = new BunitContext();

        var dressed = context.Render<MetadataField>(parameters => parameters
            .Add(row => row.Label, "version")
            .Add(row => row.ShowLabel, false)
            .Add(row => row.BaseClass, "spec__row")
            .Add(row => row.LabelCssClass, "spec__label")
            .Add(row => row.CssClass, "spec__row--wide"));

        Assert.Equal("spec__row spec__row--bare spec__row--wide", dressed.Find("div").GetAttribute("class"));
        Assert.Equal("spec__label sr-only", dressed.Find("dt").GetAttribute("class"));

        var bare = context.Render<MetadataField>(parameters => parameters
            .Add(row => row.Label, "version")
            .Add(row => row.ShowLabel, false)
            .Add(row => row.BaseClass, null)
            .Add(row => row.LabelCssClass, null));

        Assert.Null(bare.Find("div").GetAttribute("class"));
        Assert.Equal("sr-only", bare.Find("dt").GetAttribute("class"));
    }

    [Fact]
    public void The_label_is_visible_until_a_caller_says_otherwise()
    {
        // The default has to be the row every existing field already draws.
        using var context = new BunitContext();

        var field = context.Render<MetadataField>(parameters => parameters
            .Add(row => row.Label, "aliases"));

        Assert.Equal("knowledge-fields__label", field.Find("dt").GetAttribute("class"));
        Assert.Equal("knowledge-fields__row", field.Find("div").GetAttribute("class"));
    }

    [Fact]
    public void Every_part_can_be_renamed_and_the_row_can_be_dropped_entirely()
    {
        using var context = new BunitContext();

        var dressed = context.Render<MetadataField>(parameters => parameters
            .Add(row => row.Label, "kind")
            .Add(row => row.BaseClass, "spec__row")
            .Add(row => row.CssClass, "spec__row--wide")
            .Add(row => row.LabelCssClass, "spec__label")
            .Add(row => row.ValueCssClass, "spec__value"));

        Assert.Equal("spec__row spec__row--wide", dressed.Find("div").GetAttribute("class"));
        Assert.Equal("spec__label", dressed.Find("dt").GetAttribute("class"));
        Assert.Equal("spec__value", dressed.Find("dd").GetAttribute("class"));
        Assert.Empty(dressed.FindAll(".knowledge-fields__row"));

        var bare = context.Render<MetadataField>(parameters => parameters
            .Add(row => row.Label, "kind")
            .Add(row => row.BaseClass, null));

        // Null drops the attribute rather than emitting an empty one, for a host
        // laying the pair out with a grid of its own.
        Assert.Null(bare.Find("div").GetAttribute("class"));
    }

    [Fact]
    public void The_row_draws_whatever_it_is_given_and_asks_nothing()
    {
        using var context = new BunitContext();

        // Whether there is anything to say is the caller's question, because only
        // the caller knows what empty means for its own field: an absent list, a
        // blank scalar and an estimate of zero are three different answers, and
        // zero is an estimate that still shows.
        var field = context.Render<MetadataField>(parameters => parameters
            .Add(row => row.Label, "effort"));

        Assert.Equal(string.Empty, field.Find("dd").InnerHtml);
        Assert.NotNull(field.Find("div.knowledge-fields__row"));
    }

    [Fact]
    public void A_record_draws_each_of_its_fields_as_one_of_these_rows()
    {
        using var context = new BunitContext();

        var record = context.Render<MetadataView>(parameters => parameters
            .Add(view => view.Metadata, MetadataReader.Parse(
                """
                status: adopted
                related: [".tech/shared.md"]
                kind: framework
                effort: 5
                feature-flag: [inbox-pane]
                owner: platform
                """))
            .Add(view => view.Vocabulary, KnowledgeStatus.Vocabulary(KnowledgeFolder.Tech)));

        // Every row is the same three elements, so the description list stays
        // paired however many fields the schema grows. The status is not among
        // them: it is drawn in the headline and nowhere else — and a hidden label is
        // still one of these, which is why `kind` and `effort` are in this list.
        Assert.Equal(
            ["related", "kind", "effort", "feature-flag", "owner"],
            record.FindAll("dl.knowledge-fields > div.knowledge-fields__row > dt").Select(label => label.TextContent));

        Assert.Equal(
            record.FindAll("div.knowledge-fields__row").Count,
            record.FindComponents<MetadataField>().Count);
    }
}
