namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// What the Domain view's Domain page reads for one domain file beside the file
/// itself (local ADR 0047): the Model file its "How it is built" and "Contracts on
/// Model" links open — a split's own <c>model.&lt;name&gt;.md</c> when it has one —
/// and the domain's name its events' heading says, the context map's own heading.
/// </summary>
public sealed class DomainViewDomainPageTests : IDisposable
{
    private readonly TemporaryDirectory _temp = new("backlog-domain-view-domain-page");

    private const string Domain = """
        # Ordering

        ## Order

        ```meta
        type: aggregate
        ```

        A customer's request for goods.
        """;

    [Fact]
    public void A_split_aggregate_links_to_its_own_model_split()
    {
        var split = Read("ordering/domain.order.md", "domain.md", "domain.order.md", "domain.billing.md", "model.md", "model.order.md");

        Assert.Equal(".domain/ordering/model.order.md", split.ModelPath);

        // A split with no model split of its own, and the unsplit file, are built in model.md.
        Assert.Equal(".domain/ordering/model.md", Read("ordering/domain.billing.md", "domain.md", "domain.billing.md", "model.md", "model.order.md").ModelPath);
        Assert.Equal(".domain/ordering/model.md", Read("ordering/domain.md", "domain.md", "model.order.md", "model.md").ModelPath);

        // Without a model.md, the first Model file; without any, none.
        Assert.Equal(".domain/ordering/model.order.md", Read("ordering/domain.md", "domain.md", "model.order.md").ModelPath);
        Assert.Null(Read("ordering/domain.md", "domain.md").ModelPath);
    }

    [Theory]
    [InlineData("Backlog", "Backlog")]
    [InlineData("Fincent", "Fincent")]
    [InlineData("Context map", null)]
    [InlineData("Context Map: Backlog", null)]
    [InlineData("context-map.md", null)]
    [InlineData("", null)]
    public void The_events_heading_names_the_domain_the_context_map_heads(string title, string? name)
    {
        var read = ReadUnder(title, "ordering/domain.md", "domain.md");

        Assert.Null(read.Problem);
        Assert.Equal(name, read.DomainName);
        Assert.Equal(name, DomainViewDomainRead.DomainNameOf(title));
    }

    private const string Service = """
        # Boekingsservice

        ## BoekingsService

        ```meta
        type: domain-service
        related: [requirements.md#boeken, model.domain-services.md, domain.md#order]
        ```

        Books a payment.
        """;

    [Fact]
    public void When_no_name_matches_the_chapters_related_decides_before_model_md()
    {
        var read = ReadWith("ordering/domain.boekingsservice.md",
            ("domain.md", Domain),
            ("domain.boekingsservice.md", Service),
            ("model.md", Domain),
            ("model.domain-services.md", Domain));

        Assert.Equal(".domain/ordering/model.domain-services.md", read.ModelPath);
        Assert.Equal(
            [".domain/ordering/model.md", ".domain/ordering/model.domain-services.md"],
            read.ModelPaths);
    }

    private const string NestedService = """
        # Boeken

        ## Diensten

        ```meta
        type: domain-services
        ```

        ### BoekingsService

        ```meta
        type: domain-service
        related: [model.domain-services.md]
        ```

        Books a payment.
        """;

    [Fact]
    public void A_related_entry_on_a_chapter_at_any_level_decides()
    {
        var read = ReadWith("ordering/domain.boeken.md",
            ("domain.boeken.md", NestedService),
            ("model.md", Domain),
            ("model.domain-services.md", Domain));

        Assert.Equal(".domain/ordering/model.domain-services.md", read.ModelPath);
    }

    [Fact]
    public void The_splits_own_related_is_read_before_its_chapters()
    {
        var titled = Service.ReplaceLineEndings("\n").Replace(
            "# Boekingsservice\n",
            "# Boekingsservice\n\n```meta\ntype: domain\nrelated: [.devbook/domain/ordering/model.boeken.md]\n```\n",
            StringComparison.Ordinal);
        Assert.NotEqual(Service, titled);

        var read = ReadWith("ordering/domain.boekingsservice.md",
            ("domain.boekingsservice.md", titled),
            ("model.md", Domain),
            ("model.boeken.md", Domain),
            ("model.domain-services.md", Domain));

        Assert.Equal(".domain/ordering/model.boeken.md", read.ModelPath);
    }

    [Fact]
    public void A_name_match_wins_over_related_and_related_naming_no_model_file_falls_back()
    {
        Assert.Equal(".domain/ordering/model.boekingsservice.md", ReadWith("ordering/domain.boekingsservice.md",
            ("domain.boekingsservice.md", Service),
            ("model.boekingsservice.md", Domain),
            ("model.domain-services.md", Domain)).ModelPath);

        // Related names a requirements file and a domain chapter, but no Model file
        // the context has: the old fallback, model.md.
        Assert.Equal(".domain/ordering/model.md", ReadWith("ordering/domain.boekingsservice.md",
            ("domain.boekingsservice.md", Service),
            ("model.md", Domain),
            ("model.other.md", Domain)).ModelPath);
    }

    private DomainViewDomainRead Read(string selected, params string[] files) => ReadUnder("Backlog", selected, files);

    private DomainViewDomainRead ReadUnder(string mapTitle, string selected, params string[] files) =>
        ReadWith(mapTitle, selected, [.. files.Select(file => (file, Domain))]);

    private DomainViewDomainRead ReadWith(string selected, params (string Name, string Text)[] files) => ReadWith("Backlog", selected, files);

    private DomainViewDomainRead ReadWith(string mapTitle, string selected, (string Name, string Text)[] contents)
    {
        var folder = _temp.Combine(".domain", "ordering");
        Directory.CreateDirectory(folder);
        foreach (var (name, text) in contents) File.WriteAllText(Path.Combine(folder, name), text);
        var files = contents.Select(content => content.Name).ToArray();

        var documents = files.Select(file => Document($".domain/ordering/{file}", DomainDevbookStore.KindFromFile(file))).ToList();
        var view = new DomainDevbookView("JSdotNet/Backlog", _temp.Path, _temp.Combine(".domain"), null,
            Document(".domain/context-map.md", DomainDevbookDocumentKind.ContextMap) with { Title = mapTitle },
            [new DomainDevbookContext("ordering", "Ordering", "none", documents)]);

        return DomainViewDomainPage.Read(view, selected);
    }

    private static DomainDevbookDocument Document(string path, DomainDevbookDocumentKind kind) =>
        new(path, Path.GetFileName(path), kind, "none", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), string.Empty, [], [], []);

    public void Dispose() => _temp.Dispose();
}
