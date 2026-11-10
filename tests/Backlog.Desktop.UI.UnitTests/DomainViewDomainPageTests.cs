namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// What the Domain view's Domain page reads for one domain file beside the file
/// itself (local ADR 0047): the Model file its "How it is built" and "Contracts on
/// Model" links open — a split's own <c>model.&lt;name&gt;.md</c> when it has one —
/// and the domain's name its events' heading says, the context map's own heading.
/// </summary>
public sealed class DomainViewDomainPageTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "backlog-domain-view-domain-page", Guid.NewGuid().ToString("N"));

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

    private DomainViewDomainRead Read(string selected, params string[] files) => ReadUnder("Backlog", selected, files);

    private DomainViewDomainRead ReadUnder(string mapTitle, string selected, params string[] files)
    {
        var folder = Path.Combine(_root, ".domain", "ordering");
        Directory.CreateDirectory(folder);
        foreach (var file in files) File.WriteAllText(Path.Combine(folder, file), Domain);

        var documents = files.Select(file => Document($".domain/ordering/{file}", DomainDevbookStore.KindFromFile(file))).ToList();
        var view = new DomainDevbookView("JSdotNet/Backlog", _root, Path.Combine(_root, ".domain"), null,
            Document(".domain/context-map.md", DomainDevbookDocumentKind.ContextMap) with { Title = mapTitle },
            [new DomainDevbookContext("ordering", "Ordering", "none", documents)]);

        return DomainViewDomainPage.Read(view, selected);
    }

    private static DomainDevbookDocument Document(string path, DomainDevbookDocumentKind kind) =>
        new(path, Path.GetFileName(path), kind, "none", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), string.Empty, [], [], []);

    public void Dispose()
    {
        if (!Directory.Exists(_root)) return;
        try { Directory.Delete(_root, recursive: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
