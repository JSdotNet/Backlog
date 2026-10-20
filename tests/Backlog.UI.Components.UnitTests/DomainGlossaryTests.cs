using Backlog.SharedKernel.Devbook;

namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// The Glossary unit of the Domain page (local ADR 0036): every term under
/// <c>## Ubiquitous Language</c> and every model chapter of the context's domain
/// files, A–Z, with its kind, its aliases as code chips and its first sentence; a
/// filter that matches aliases too, kind filters, deprecated terms struck through,
/// and "Easily confused" for two terms that name each other in <c>related</c>.
/// </summary>
public sealed class DomainGlossaryTests
{
    private const string DomainPath = ".devbook/domain/ordering/domain.md";

    private const string Domain = """
        # Ordering

        ```meta
        type: domain
        ```

        ## Order

        ```meta
        type: aggregate
        aliases: [Purchase]
        ```

        A customer's request for goods. It is placed once.

        ### Order Line

        ```meta
        type: entity
        aliases: [OrderLine]
        ```

        One product and its quantity on the order.

        ## OrderPlaced

        ```meta
        type: domain-event
        ```

        Published when an order is placed.

        ## Ubiquitous Language

        ```meta
        type: ubiquitous-language
        ```

        The words the ordering team uses.

        ### Customer

        ```meta
        type: term
        aliases: [Buyer Line, Client]
        ```

        Whoever places the order.

        ### Basket

        ```meta
        type: term
        status: deprecated
        related: [.devbook/domain/ordering/domain.md#draft-order]
        ```

        What a draft order used to be called.

        ### Draft Order

        ```meta
        type: term
        related: [domain.md#basket]
        ```

        An order still being filled.

        ### Price List

        ```meta
        type: term
        related: [.devbook/domain/ordering/domain.md#basket]
        ```

        The prices in force.
        """;

    private const string Split = """
        # Invoice

        ```meta
        type: domain
        ```

        ## Invoice

        ```meta
        type: aggregate
        ```

        The bill for an order.
        """;

    private static DomainChapterFile File => DomainUnits.Parse(Domain, DomainPath);

    private static DomainChapterFile SplitFile => DomainUnits.Parse(Split, ".devbook/domain/ordering/domain.invoice.md");

    private static DomainChapter Language(DomainChapterFile file) => file.Chapters.Single(chapter => chapter.Type == "ubiquitous-language");

    private static IRenderedComponent<DomainGlossary> Render(BunitContext context, params DomainChapterFile[] files)
    {
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        return context.Render<DomainGlossary>(parameters => parameters
            .Add(p => p.Chapter, Language(files[0]))
            .Add(p => p.Files, files)
            .Add(p => p.DocumentPath, DomainPath)
            .Add(p => p.DebounceMilliseconds, 0));
    }

    private static IReadOnlyList<string?> Shown(IRenderedComponent<DomainGlossary> page) =>
        [.. page.FindAll("[data-testid='domain-glossary-term']").Select(row => row.GetAttribute("data-term"))];

    [Fact]
    public void Lists_every_term_and_model_chapter_of_the_context_a_to_z()
    {
        var entries = DomainGlossaryIndex.Entries([File, SplitFile]);

        Assert.Equal(
            ["Basket", "Customer", "Draft Order", "Invoice", "Order", "Order Line", "OrderPlaced", "Price List"],
            entries.Select(entry => entry.Title));
        Assert.Equal(["term", "term", "term", "aggregate", "aggregate", "entity", "domain-event", "term"], entries.Select(entry => entry.Kind));
        Assert.Equal("A customer's request for goods.", entries.Single(entry => entry.Title == "Order").Summary);
    }

    [Fact]
    public void A_term_and_a_model_chapter_of_one_heading_are_two_rows()
    {
        var file = DomainUnits.Parse(Domain.Replace("### Customer", "### Order", StringComparison.Ordinal), DomainPath);

        Assert.Equal(["aggregate", "term"], DomainGlossaryIndex.Entries([file]).Where(entry => entry.Title == "Order").Select(entry => entry.Kind));
    }

    [Fact]
    public void Each_row_shows_its_kind_its_aliases_as_code_chips_and_its_first_sentence()
    {
        using var context = new BunitContext();
        var page = Render(context, File);

        var customer = page.Find("[data-testid='domain-glossary-term'][data-term='Customer']");
        Assert.Equal("term", customer.QuerySelector("[data-testid='domain-glossary-kind']")!.TextContent.Trim());
        Assert.Equal(["Buyer Line", "Client"], customer.QuerySelectorAll("code[data-testid='domain-glossary-alias']").Select(chip => chip.TextContent));
        Assert.Contains("Whoever places the order.", customer.TextContent);

        var line = page.Find("[data-testid='domain-glossary-term'][data-term='Order Line']");
        Assert.Equal("entity", line.QuerySelector("[data-testid='domain-glossary-kind']")!.TextContent.Trim());
        Assert.NotNull(line.QuerySelector("svg"));
    }

    [Fact]
    public void A_deprecated_term_is_struck_through_and_says_so_in_words()
    {
        using var context = new BunitContext();
        var page = Render(context, File);

        var basket = page.Find("[data-testid='domain-glossary-term'][data-term='Basket']");
        Assert.Equal("true", basket.GetAttribute("data-deprecated"));
        Assert.Equal("Basket", basket.QuerySelector("s")!.TextContent);
        Assert.Contains("(deprecated)", basket.TextContent);
        Assert.Null(page.Find("[data-testid='domain-glossary-term'][data-term='Customer']").QuerySelector("s"));
    }

    [Fact]
    public void The_filter_matches_a_heading_or_an_alias()
    {
        using var context = new BunitContext();
        var page = Render(context, File);

        page.Find("[data-testid='domain-glossary-filter'] input").Input("line");
        Assert.Equal(["Customer", "Order Line"], Shown(page));

        page.Find("[data-testid='domain-glossary-filter'] input").Input("purchase");
        Assert.Equal(["Order"], Shown(page));

        page.Find("[data-testid='domain-glossary-filter'] input").Input("nothing like it");
        Assert.Empty(Shown(page));
        Assert.NotNull(page.Find("[data-testid='domain-glossary-no-match']"));
    }

    [Fact]
    public void The_kind_filters_narrow_to_the_kinds_pressed_beside_the_filter()
    {
        using var context = new BunitContext();
        var page = Render(context, File);

        Assert.Equal(
            ["term", "aggregate", "entity", "domain-event"],
            page.FindAll("[data-testid='domain-glossary-kinds'] button").Select(button => button.GetAttribute("data-kind")));

        page.Find("[data-testid='domain-glossary-kind-aggregate']").Click();
        Assert.Equal(["Order"], Shown(page));

        page.Find("[data-testid='domain-glossary-kind-entity']").Click();
        Assert.Equal(["Order", "Order Line"], Shown(page));

        page.Find("[data-testid='domain-glossary-filter'] input").Input("line");
        Assert.Equal(["Order Line"], Shown(page));

        page.Find("[data-testid='domain-glossary-kind-aggregate']").Click();
        page.Find("[data-testid='domain-glossary-kind-entity']").Click();
        Assert.Equal(["Customer", "Order Line"], Shown(page));
    }

    [Fact]
    public void Two_terms_that_name_each_other_are_easily_confused_and_a_one_way_pointer_is_not()
    {
        var pairs = DomainGlossaryIndex.Confused([File]);

        var pair = Assert.Single(pairs);
        Assert.Equal(("Basket", "Draft Order"), (pair.First.Title, pair.Second.Title));

        using var context = new BunitContext();
        var page = Render(context, File);
        var shown = page.Find("[data-testid='domain-glossary-confused'] [data-testid='domain-glossary-pair']");
        Assert.Equal(
            ["Basket", "Draft Order"],
            shown.QuerySelectorAll("[data-testid='domain-glossary-pair-term']").Select(side => side.GetAttribute("data-term")));
        Assert.Contains("What a draft order used to be called.", shown.TextContent);
        Assert.Contains("An order still being filled.", shown.TextContent);
        Assert.Equal("Basket", shown.QuerySelector("s")!.TextContent);
    }

    [Fact]
    public void Reached_from_the_domain_chapter_bar_it_lists_the_split_files_too()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var page = context.Render<DomainUnitPage>(parameters => parameters
            .Add(p => p.File, File)
            .Add(p => p.DomainFiles, [File, SplitFile])
            .Add(p => p.ActiveUnit, "ubiquitous-language"));

        Assert.Equal("Glossary", page.Find("[data-testid='domain-units-bar'] [role='tab'][aria-selected='true']").TextContent.Trim());
        Assert.Contains("Invoice", page.FindAll("[data-testid='domain-glossary-term']").Select(row => row.GetAttribute("data-term")));
    }
}
