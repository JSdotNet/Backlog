using Backlog.Modules.Devbook.Abstractions;
using Backlog.Modules.Tasks.Abstractions.Services;

namespace Backlog.Infrastructure.Mcp.UnitTests;

/// <summary>
/// A chapter read lists the page's click demos by path — the ones beside it whose
/// name pairs them with the page, and every place a <c>demo</c> field names — and
/// never carries their HTML.
/// </summary>
public sealed class ChapterDemoTests : IDisposable
{
    private const string Html = "<!doctype html><title>Ordering</title><p>SCREEN-MARKUP</p>";

    private const string Features = """
        # Ordering Features

        ```meta
        status: active
        demo: [.domain/ordering/flows.demo.html#walkthrough/checkout, .domain/ordering/gone.demo.html]
        ```

        Orders are placed here.

        ## Checkout

        ```meta
        demo: ".domain/ordering/features.demo.html#cart/total?role=buyer&flags=promo,gift"
        ```

        The cart becomes an order.
        """;

    private static readonly TasksRepositoryRef Backlog = new("backlog", "JSdotNet", "Backlog");

    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("backlog-mcp-demos");

    public ChapterDemoTests()
    {
        var context = Directory.CreateDirectory(Path.Combine(_root.FullName, ".domain", "ordering"));

        File.WriteAllText(Path.Combine(context.FullName, "context.md"), "# Ordering\n");
        File.WriteAllText(Path.Combine(context.FullName, "features.md"), Features);
        File.WriteAllText(Path.Combine(context.FullName, "demo.html"), Html);
        File.WriteAllText(Path.Combine(context.FullName, "features.demo.html"), Html);
        File.WriteAllText(Path.Combine(context.FullName, "features.checkout.demo.html"), Html);
        File.WriteAllText(Path.Combine(context.FullName, "flows.demo.html"), Html);
    }

    public void Dispose() => _root.Delete(recursive: true);

    [Fact]
    public async Task A_page_lists_its_demos_by_name_then_by_field_in_document_order()
    {
        var answer = await Read(".domain/ordering/features.md");

        Assert.Equal(
            [
                new ChapterDemoPayload(".domain/ordering/features.demo.html", null, "name", null, true),
                new ChapterDemoPayload(".domain/ordering/flows.demo.html", "walkthrough/checkout", "field", 1, true),
                new ChapterDemoPayload(".domain/ordering/gone.demo.html", null, "field", 1, false),
                new ChapterDemoPayload(".domain/ordering/features.demo.html", "cart/total?role=buyer&flags=promo,gift", "field", 4, true)
            ],
            answer.Demos);
    }

    /// <summary>A field's block is the parse's index — the one a review read
    /// shows the <c>meta</c> fence at, and the one notes anchor to.</summary>
    [Fact]
    public async Task A_field_names_the_meta_block_it_sits_in()
    {
        var review = await Tools().ReadKnowledgeChapterAsync(
            "JSdotNet/Backlog", ".domain/ordering/features.md", review: true, TestContext.Current.CancellationToken);

        Assert.All(
            review.Demos.Where(demo => demo.Block is not null),
            demo => Assert.Equal("meta", review.Blocks.Single(block => block.Index == demo.Block).Language));
    }

    [Fact]
    public async Task The_context_owns_demo_html_and_a_split_page_its_own()
    {
        var context = await Read(".domain/ordering/context.md");

        var demo = Assert.Single(context.Demos);
        Assert.Equal(".domain/ordering/demo.html", demo.Path);
        Assert.Equal("name", demo.PairedBy);
    }

    [Fact]
    public async Task No_demo_html_travels_in_the_answer()
    {
        var answer = await Read(".domain/ordering/features.md");

        Assert.DoesNotContain("SCREEN-MARKUP", answer.Markdown, StringComparison.Ordinal);
        Assert.NotEmpty(answer.Demos);
    }

    [Fact]
    public async Task A_demo_is_not_a_chapter_a_session_can_read()
    {
        var tools = Tools();

        await Assert.ThrowsAnyAsync<Exception>(() => tools.ReadKnowledgeChapterAsync(
            "JSdotNet/Backlog", ".domain/ordering/features.demo.html", cancellationToken: TestContext.Current.CancellationToken));
    }

    private Task<ChapterPayload> Read(string chapterPath) =>
        Tools().ReadKnowledgeChapterAsync("JSdotNet/Backlog", chapterPath, review: false, TestContext.Current.CancellationToken);

    private DevbookTools Tools() =>
        new(
            new FakeDevbookFolderSource(_root.FullName, new DevbookFolderSetting(".domain", "Domain", ".domain")),
            new FakeDevbookAnnotationStore(),
            new FakeRepositoryDirectory([Backlog]));
}
