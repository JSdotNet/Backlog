using Backlog.SharedKernel.Devbook;

namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// A file the Domain view has no page of its own for — actors.md, skills.md, a
/// page a context adds — drawn as written (domain/devbook requirement "Any other
/// file opens as written"): its title in the
/// unit pages' hero, the words before its first chapter as the lead, its
/// <c>related</c> as links to follow, and each chapter under its heading.
/// </summary>
public sealed class DomainFilePageTests
{
    private const string Path = ".devbook/domain/financieel/actors.md";

    // Shaped as Fincent's financieel/actors.md.
    private const string Actors = """
        # Actoren

        ```meta
        type: actors
        status: review
        related: [.devbook/domain/financieel/context.md, .devbook/domain/financieel/domain.md#rekening]
        ```

        > Wie er met Financieel werkt. Een actor is een **rol** of een **partij**.

        ```annotation
        kind: question
        body: Is de beoordelaar een eigen actor?
        ```

        ## Klant

        ```meta
        type: user
        related: [.devbook/domain/cross-cutting/actors.md#klant]
        ```

        De klant van het dossier waarvoor budgetbeheer wordt uitgevoerd.

        ### Wat hij ziet

        Zijn overzicht en zijn afschriften.

        ## Rechten

        | Recht | Wie |
        |---|---|
        | Te beoordelen | Medewerker |

        ### Beheerder

        ```meta
        type: user
        ```

        Wie de rechten toekent.
        """;

    private static DomainChapterFile File => DomainUnits.Parse(Actors, Path);

    [Fact]
    public void The_lead_is_what_the_file_says_before_its_first_chapter_without_its_annotations()
    {
        var file = File;

        Assert.StartsWith("> Wie er met Financieel werkt.", file.Lead, StringComparison.Ordinal);
        Assert.DoesNotContain("beoordelaar", file.Lead, StringComparison.Ordinal);
        Assert.DoesNotContain("```", file.Lead, StringComparison.Ordinal);
        Assert.Equal("actors", file.Meta["type"]);
        Assert.Equal([".devbook/domain/financieel/context.md", ".devbook/domain/financieel/domain.md#rekening"], file.Related);
    }

    [Fact]
    public void A_file_with_no_words_before_its_first_chapter_has_no_lead()
    {
        var file = DomainUnits.Parse("# Skills\n\n## Triage\n\nSorting the inbox.\n", ".devbook/domain/tasks/skills.md");

        Assert.Equal(string.Empty, file.Lead);
        Assert.Empty(file.Related);
    }

    [Fact]
    public void The_lead_ends_at_a_second_title()
    {
        var file = DomainUnits.Parse("# One\n\nThe first title's words.\n\n# Two\n\nThe second title's words.\n\n## Chapter\n\nIts prose.\n", ".devbook/domain/tasks/notes.md");

        Assert.Equal("The first title's words.", file.Lead);
        Assert.DoesNotContain("second title", file.Lead, StringComparison.Ordinal);
    }

    [Fact]
    public void A_deeper_heading_before_the_first_chapter_stays_in_the_lead_with_its_heading()
    {
        var file = DomainUnits.Parse("# Notes\n\nOpening words.\n\n### An aside\n\nThe aside's words.\n\n## Chapter\n\nIts prose.\n", ".devbook/domain/tasks/notes.md");

        Assert.Contains("### An aside", file.Lead, StringComparison.Ordinal);
        Assert.Contains("The aside's words.", file.Lead, StringComparison.Ordinal);
        Assert.DoesNotContain("Its prose.", file.Lead, StringComparison.Ordinal);
        Assert.Equal(["Chapter"], file.Chapters.Select(chapter => chapter.Title));

        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var page = context.Render<DomainFilePage>(parameters => parameters.Add(p => p.File, file));

        var lead = page.Find("[data-testid='domain-file-lead']");
        Assert.Equal("An aside", lead.QuerySelector("[role='heading'][aria-level='3']")?.TextContent.Trim());
        Assert.Contains("The aside's words.", lead.TextContent);
    }

    [Fact]
    public void A_tilde_annotation_fence_is_left_out_of_the_lead()
    {
        var file = DomainUnits.Parse("# Notes\n\nOpening words.\n\n~~~annotation\nkind: question\nbody: A hidden remark.\n~~~\n\nClosing words.\n\n## Chapter\n\nIts prose.\n", ".devbook/domain/tasks/notes.md");

        Assert.Contains("Opening words.", file.Lead, StringComparison.Ordinal);
        Assert.Contains("Closing words.", file.Lead, StringComparison.Ordinal);
        Assert.DoesNotContain("hidden remark", file.Lead, StringComparison.Ordinal);
        Assert.DoesNotContain("~~~", file.Lead, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("type: actors\nrating: adopt\nstatus: review", "actors · adopt")]
    [InlineData("type: actors\nstatus: review", "actors · review")]
    [InlineData("type: actors", "actors")]
    public void The_kicker_reads_the_status_as_a_chapter_does(string meta, string kicker)
    {
        var file = DomainUnits.Parse($"# Actors\n\n```meta\n{meta}\n```\n\nWho.\n", Path);

        Assert.Equal(kicker, DomainFilePage.Kicker(file, "actors"));
        Assert.Equal(DomainUnits.StatusOf(file.Meta), kicker.Contains(" · ", StringComparison.Ordinal) ? kicker.Split(" · ")[1] : null);
    }

    [Fact]
    public void The_page_heads_with_the_kind_title_lead_and_related_links()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var page = context.Render<DomainFilePage>(parameters => parameters.Add(p => p.File, File));

        Assert.Equal("actors · review", page.Find("[data-testid='domain-file-kicker']").TextContent.Trim());
        Assert.Equal("Actoren", page.Find("[data-testid='domain-file-title']").TextContent.Trim());
        Assert.Contains("Wie er met Financieel werkt.", page.Find("[data-testid='domain-file-lead']").TextContent);
        Assert.Equal(
            ["context", "rekening"],
            page.FindAll("[data-testid='domain-file-related'] [data-testid='domain-file-related-link']").Select(link => link.TextContent.Trim()));
    }

    [Fact]
    public void Each_chapter_is_a_section_with_its_markdown_and_a_typed_sub_chapter_nests_under_it()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var page = context.Render<DomainFilePage>(parameters => parameters.Add(p => p.File, File));

        var article = page.Find("[data-testid='domain-file']");
        Assert.Equal(["klant", "rechten"], article.Children.Where(child => child.LocalName == "section" && child.GetAttribute("data-testid") == "domain-file-chapter").Select(section => section.GetAttribute("data-chapter")));

        var klant = page.Find("[data-testid='domain-file-chapter'][data-chapter='klant']");
        Assert.Contains("De klant van het dossier", klant.TextContent);
        // An untyped sub-chapter is the chapter's prose, not a section of its own.
        Assert.Contains("Zijn overzicht en zijn afschriften.", klant.TextContent);
        Assert.Empty(klant.QuerySelectorAll("[data-testid='domain-file-chapter']"));
        Assert.Single(klant.QuerySelectorAll("[data-testid='domain-file-chapter-related'] [data-testid='domain-file-related-link']"));

        var rechten = page.Find("[data-testid='domain-file-chapter'][data-chapter='rechten']");
        Assert.NotNull(rechten.QuerySelector("table"));
        Assert.Empty(rechten.QuerySelectorAll("[data-testid='domain-file-chapter-related']"));
        var beheerder = Assert.Single(rechten.QuerySelectorAll("[data-testid='domain-file-chapter']"));
        Assert.Equal("beheerder", beheerder.GetAttribute("data-chapter"));
        Assert.Contains("Wie de rechten toekent.", beheerder.TextContent);
    }

    [Fact]
    public void Following_a_related_link_hands_its_reference_to_the_host()
    {
        DevbookReference? followed = null;
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var page = context.Render<DomainFilePage>(parameters => parameters
            .Add(p => p.File, File)
            .Add(p => p.OnNavigate, (DevbookReference reference) => followed = reference));

        page.FindAll("[data-testid='domain-file-related'] [data-testid='domain-file-related-link']")[1].Click();

        Assert.NotNull(followed);
        Assert.Equal(".devbook/domain/financieel/domain.md#rekening", followed.Raw);
    }

    [Fact]
    public void A_file_with_no_title_is_named_by_its_file_and_its_kind_by_its_name()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var page = context.Render<DomainFilePage>(parameters => parameters
            .Add(p => p.File, DomainUnits.Parse("Loose notes.\n", ".devbook/domain/index.md")));

        Assert.Equal("index", page.Find("[data-testid='domain-file-title']").TextContent.Trim());
        Assert.Equal("index", page.Find("[data-testid='domain-file-kicker']").TextContent.Trim());
        Assert.Contains("Loose notes.", page.Find("[data-testid='domain-file-lead']").TextContent);
        Assert.Empty(page.FindAll("[data-testid='domain-file-related']"));
    }
}
