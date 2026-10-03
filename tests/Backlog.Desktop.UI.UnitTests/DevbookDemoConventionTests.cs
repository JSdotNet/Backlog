using Backlog.Modules.Devbook.Abstractions;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// How a <c>*.demo.html</c> pairs with a page — by its name, and by a chapter's
/// <c>demo</c> field — as devbook's <c>devbook-domain.md</c> and
/// <c>devbook-chapter-metadata.md</c> state it. The database builder holds the
/// Node writer to the same rules through <c>DevbookBuilderParityTests</c>.
/// </summary>
public sealed class DevbookDemoConventionTests
{
    [Theory]
    [InlineData("demo.html", "context.md")]
    [InlineData("features.demo.html", "features.md")]
    [InlineData("features.checkout.demo.html", "features.checkout.md")]
    [InlineData(".devbook/domain/ordering/features.demo.html", "features.md")]
    [InlineData(@".devbook\domain\ordering\demo.html", "context.md")]
    public void A_demo_pairs_by_name_with_the_page_beside_it(string demo, string page)
    {
        Assert.Equal(page, DevbookReadingConvention.DemoPage(demo));
        Assert.True(DevbookReadingConvention.IsDemo(demo));
    }

    [Theory]
    [InlineData("features.md")]
    [InlineData(".demo.html")]
    [InlineData("demo-template.html")]
    [InlineData("features.html")]
    [InlineData("Features.Demo.HTML")]
    public void Anything_else_is_not_a_demo(string name)
    {
        Assert.Null(DevbookReadingConvention.DemoPage(name));
        Assert.False(DevbookReadingConvention.IsDemo(name));
    }

    [Fact]
    public void A_page_owns_only_the_demos_its_name_pairs_it_with()
    {
        string[] names = ["features.md", "features.demo.html", "features.checkout.demo.html", "demo.html", "context.md", "notes.txt"];

        Assert.Equal(["features.demo.html"], DevbookReadingConvention.DemosOf("features.md", names));
        Assert.Equal(["demo.html"], DevbookReadingConvention.DemosOf("context.md", names));
        Assert.Empty(DevbookReadingConvention.DemosOf("domain.md", names));
    }

    [Fact]
    public void A_demo_field_names_places_by_path_and_address()
    {
        var references = DevbookReadingConvention.DemoField(
            "[.devbook/domain/ordering/features.demo.html#checkout, ./.devbook/domain/ordering/features.demo.html#walkthrough/checkout-declined/2, .devbook/domain/ordering/demo.html]");

        Assert.Equal(
            [
                new DevbookDemoReference(".devbook/domain/ordering/features.demo.html", "checkout"),
                new DevbookDemoReference(".devbook/domain/ordering/features.demo.html", "walkthrough/checkout-declined/2"),
                new DevbookDemoReference(".devbook/domain/ordering/demo.html", null)
            ],
            references);
    }

    [Fact]
    public void An_address_split_on_the_comma_in_its_flags_is_joined_back()
    {
        // The metadata parse splits a list on every comma; the rule says to quote
        // such an address, and the parse splits the quoted one too.
        var references = DevbookReadingConvention.DemoReferences(
        [
            "\".devbook/domain/ordering/features.demo.html#cart/total?role=buyer&flags=promo",
            "gift\"",
            ".devbook/domain/ordering/features.demo.html#checkout"
        ]);

        Assert.Equal(
            [
                new DevbookDemoReference(".devbook/domain/ordering/features.demo.html", "cart/total?role=buyer&flags=promo,gift"),
                new DevbookDemoReference(".devbook/domain/ordering/features.demo.html", "checkout")
            ],
            references);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("[]")]
    [InlineData("[.devbook/domain/ordering/features.md#checkout]")]
    public void A_field_naming_no_demo_names_nothing(string? value) =>
        Assert.Empty(DevbookReadingConvention.DemoField(value));

    [Fact]
    public void A_bare_value_and_a_panel_flattened_list_read_like_the_bracketed_list()
    {
        Assert.Equal(
            [new DevbookDemoReference(".devbook/domain/ordering/demo.html", "orders")],
            DevbookReadingConvention.DemoField(".devbook/domain/ordering/demo.html#orders"));
        Assert.Equal(2, DevbookReadingConvention.DemoField("a.demo.html#x, b.demo.html").Count);
    }
}
