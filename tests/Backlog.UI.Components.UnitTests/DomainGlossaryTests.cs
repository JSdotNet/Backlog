using Backlog.SharedKernel.Devbook;

namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// The Glossary unit of the Domain page (local ADR 0044): every term under
/// <c>## Ubiquitous Language</c> and every model chapter of the context's domain
/// files, A–Z, with its kind, its status, its aliases as code chips and its first
/// sentence; a labelled find box that matches aliases too, the Show pills with
/// their counts, deprecated terms struck through, a long list behind "n more",
/// "Easily confused" for each group of terms that name each other in
/// <c>related</c>, and a term borrowed from another context linked to it.
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
    public void The_glossary_opens_on_its_lead_and_a_labelled_find_box_hinting_at_aliases()
    {
        using var context = new BunitContext();
        var page = Render(context, File);

        Assert.Equal(
            "Every word the context uses, whether it is a model chapter or a term of its own. Any alias finds the one chapter it names.",
            page.Find("[data-testid='domain-glossary-lead']").TextContent);
        var input = page.Find("[data-testid='domain-glossary-filter'] input");
        Assert.Equal("Find a word or an alias", page.Find($"label[for='{input.Id}']").TextContent);
        Assert.Equal("e.g. Buyer Line, Purchase, OrderLine", input.GetAttribute("placeholder"));
    }

    [Fact]
    public void The_show_pills_count_and_narrow_to_model_terms_or_borrowed()
    {
        using var context = new BunitContext();
        var page = Render(context, File);

        var pills = page.FindAll("[data-testid='domain-glossary-show'] button");
        Assert.Equal(["all", "model", "terms"], pills.Select(pill => pill.GetAttribute("data-show")));
        Assert.Equal(["7", "3", "4"], pills.Select(pill => pill.QuerySelector(".domain-glossary__count")!.TextContent));
        Assert.Equal("true", page.Find("[data-testid='domain-glossary-show-all']").GetAttribute("aria-checked"));

        page.Find("[data-testid='domain-glossary-show-model']").Click();
        Assert.Equal(["Order", "Order Line", "OrderPlaced"], Shown(page));

        page.Find("[data-testid='domain-glossary-filter'] input").Input("line");
        Assert.Equal(["Order Line"], Shown(page));

        page.Find("[data-testid='domain-glossary-show-terms']").Click();
        Assert.Equal(["Customer"], Shown(page));

        page.Find("[data-testid='domain-glossary-show-all']").Click();
        Assert.Equal(["Customer", "Order Line"], Shown(page));
    }

    [Fact]
    public void Terms_that_name_each_other_are_easily_confused_as_one_card_and_a_one_way_pointer_is_not()
    {
        var file = DomainUnits.Parse(
            Domain.Replace("related: [domain.md#basket]", "related: [domain.md#basket, domain.md#pending-order]", StringComparison.Ordinal) + """


                ### Pending Order

                ```meta
                type: term
                aliases: [awaiting]
                related: [domain.md#draft-order]
                ```

                An order waiting on its payment.
                """,
            DomainPath);

        var group = Assert.Single(DomainGlossaryIndex.Confused([file]));
        Assert.Equal(["Basket", "Draft Order", "Pending Order"], group.Terms.Select(term => term.Title));

        using var context = new BunitContext();
        var page = Render(context, file);
        var shown = Assert.Single(page.FindAll("[data-testid='domain-glossary-confused'] [data-testid='domain-glossary-pair']"));
        Assert.Equal("Basket | Draft Order | Pending Order", shown.GetAttribute("data-pair"));
        Assert.Equal(
            ["Basket", "Draft Order", "Pending Order"],
            shown.QuerySelectorAll("[data-testid='domain-glossary-pair-term']").Select(side => side.GetAttribute("data-term")));
        Assert.Contains("What a draft order used to be called.", shown.TextContent);
        Assert.Contains("An order still being filled.", shown.TextContent);
        Assert.Equal("Basket", shown.QuerySelector("s")!.TextContent);
        Assert.Equal("awaiting", shown.QuerySelector("[data-term='Pending Order'] code")!.TextContent);
    }

    [Fact]
    public void A_term_naming_its_own_heading_in_another_context_is_borrowed()
    {
        var file = DomainUnits.Parse(
            Domain + """


                ### Tag

                ```meta
                type: term
                related: [.devbook/domain/roadmap/domain.md#tag, .devbook/domain/roadmap/domain.md#release]
                ```

                A plan's label, worn by a task.
                """,
            DomainPath);

        var entries = DomainGlossaryIndex.Entries([file]);
        Assert.Equal(new DomainBorrowedTerm("roadmap", ".devbook/domain/roadmap/domain.md#tag"), entries.Single(entry => entry.Title == "Tag").Borrowed);
        Assert.Null(entries.Single(entry => entry.Title == "Price List").Borrowed);

        DevbookReference? followed = null;
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var page = context.Render<DomainGlossary>(parameters => parameters
            .Add(p => p.Chapter, Language(file))
            .Add(p => p.Files, [file])
            .Add(p => p.DocumentPath, DomainPath)
            .Add(p => p.DebounceMilliseconds, 0)
            .Add(p => p.OnNavigate, reference => followed = reference));

        var chip = page.Find("[data-testid='domain-glossary-term'][data-term='Tag'] [data-testid='domain-glossary-borrowed']");
        Assert.Equal("borrowed from roadmap", chip.TextContent.Trim());
        Assert.Equal("1", page.Find("[data-testid='domain-glossary-show-borrowed'] .domain-glossary__count").TextContent);

        var across = page.Find("[data-testid='domain-glossary-across']");
        Assert.Contains("Across contexts", across.TextContent);
        Assert.Equal(["Tag"], across.QuerySelectorAll("[data-testid='domain-glossary-across-term']").Select(row => row.GetAttribute("data-term")));
        Assert.Equal("roadmap", across.QuerySelector("[data-testid='domain-glossary-across-link']")!.TextContent.Trim());

        chip.Click();
        Assert.Equal(".devbook/domain/roadmap/domain.md#tag", followed?.Raw);

        page.Find("[data-testid='domain-glossary-show-borrowed']").Click();
        Assert.Equal(["Tag"], Shown(page));
    }

    [Fact]
    public void A_proposed_term_carries_a_proposed_badge()
    {
        var file = DomainUnits.Parse(Domain.Replace("aliases: [Buyer Line, Client]", "status: proposed\naliases: [Buyer Line, Client]", StringComparison.Ordinal), DomainPath);

        Assert.True(DomainGlossaryIndex.Entries([file]).Single(entry => entry.Title == "Customer").Proposed);

        using var context = new BunitContext();
        var page = Render(context, file);

        Assert.Equal("proposed", page.Find("[data-testid='domain-glossary-term'][data-term='Customer'] [data-testid='domain-glossary-status']").TextContent.Trim());
        Assert.Equal("deprecated", page.Find("[data-testid='domain-glossary-term'][data-term='Basket'] [data-testid='domain-glossary-status']").TextContent.Trim());
        Assert.Null(page.Find("[data-testid='domain-glossary-term'][data-term='Order']").QuerySelector("[data-testid='domain-glossary-status']"));
    }

    [Fact]
    public void A_long_list_shows_fifteen_and_the_rest_behind_more()
    {
        var terms = string.Concat(Enumerable.Range(1, 22).Select(n => $"### Word {n:00}\n\nThe {n:00}th word.\n\n"));
        var file = DomainUnits.Parse(
            $"# Long\n\n```meta\ntype: domain\n```\n\n## Ubiquitous Language\n\n```meta\ntype: ubiquitous-language\n```\n\n{terms}",
            ".devbook/domain/long/domain.md");

        using var context = new BunitContext();
        var page = Render(context, file);

        Assert.Equal(15, Shown(page).Count);
        Assert.Equal("7 more", page.Find("[data-testid='domain-glossary-more']").TextContent.Trim());

        page.Find("[data-testid='domain-glossary-filter'] input").Input("word");
        Assert.Equal(22, Shown(page).Count);
        Assert.Empty(page.FindAll("[data-testid='domain-glossary-more']"));

        page.Find("[data-testid='domain-glossary-filter'] input").Input(string.Empty);
        Assert.Equal(15, Shown(page).Count);

        page.Find("[data-testid='domain-glossary-more']").Click();
        Assert.Equal(22, Shown(page).Count);
        Assert.Empty(page.FindAll("[data-testid='domain-glossary-more']"));
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
