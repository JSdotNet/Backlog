using Backlog.Modules.Tasks.Abstractions.Services;

using ModelContextProtocol;

namespace Backlog.Infrastructure.Mcp.UnitTests;

/// <summary>
/// <c>set_devbook_references</c>, <c>list_devbook_references</c> and the
/// references <c>read_item</c> carries: an entry's pointers at Devbook pages and
/// chapters, written as a whole list and read back resolved.
/// </summary>
public class DevbookReferenceToolsTests
{
    private static readonly TasksRepositoryRef Backlog = new("backlog", "JSdotNet", "Backlog");

    private const string Chapter = ".devbook/domain/tasks/domain.md#task-item";

    private const string Page = ".devbook/arc42/05-building-block-view.md";

    private static readonly ResolvedDevbookReference ChapterAnswer = new(
        Chapter, ".devbook/domain/tasks/domain.md", "task-item", DevbookReferenceState.Chapter, "Task item", "draft", "domain");

    private static readonly ResolvedDevbookReference PageAnswer = new(
        Page, Page, null, DevbookReferenceState.Page, "Building block view", null, "arc42");

    // --- set_devbook_references ----------------------------------------------

    [Fact]
    public async Task Setting_replaces_the_whole_list_and_answers_it_resolved()
    {
        var id = Guid.NewGuid();
        var entries = new FakeTaskItems(Entries.Entry("The one", id: id, devbookReferences: [".devbook/design/old.md"]));
        var resolver = new FakeDevbookReferenceResolver(ChapterAnswer, PageAnswer);
        var tools = Tools(entries, resolver);

        var answer = await tools.SetDevbookReferencesAsync(id, "JSdotNet/Backlog", [Chapter, Page], TestContext.Current.CancellationToken);

        var write = Assert.Single(entries.DevbookReferenceWrites);
        Assert.Equal(id, write.Id);
        Assert.Equal([Chapter, Page], write.References);

        Assert.Equal(id, answer.Id);
        Assert.Equal("JSdotNet/Backlog", answer.Repository);
        Assert.Equal(
            [
                new DevbookReferencePayload(Chapter, ".devbook/domain/tasks/domain.md", "task-item", "chapter", "Task item", "draft", "domain"),
                new DevbookReferencePayload(Page, Page, null, "page", "Building block view", null, "arc42")
            ],
            answer.References);

        // Resolved against the repository the caller named, by its alias.
        Assert.Equal("backlog", Assert.Single(resolver.Resolved).Alias);
    }

    /// <summary>What is resolved is what the entry now holds, not what was sent:
    /// the module drops repeats, and the answer has to say so.</summary>
    [Fact]
    public async Task The_answer_is_the_list_the_entry_kept()
    {
        var id = Guid.NewGuid();
        var tools = Tools(new FakeTaskItems(Entries.Entry("The one", id: id)), new FakeDevbookReferenceResolver(ChapterAnswer));

        var answer = await tools.SetDevbookReferencesAsync(id, "JSdotNet/Backlog", [Chapter, Chapter], TestContext.Current.CancellationToken);

        Assert.Equal([Chapter], answer.References.Select(reference => reference.Reference));
    }

    [Fact]
    public async Task An_empty_list_clears_them()
    {
        var id = Guid.NewGuid();
        var entries = new FakeTaskItems(Entries.Entry("The one", id: id, devbookReferences: [Chapter]));
        var tools = Tools(entries, new FakeDevbookReferenceResolver());

        var answer = await tools.SetDevbookReferencesAsync(id, "JSdotNet/Backlog", [], TestContext.Current.CancellationToken);

        Assert.Empty(answer.References);
        Assert.Empty(Assert.Single(entries.DevbookReferenceWrites).References);
    }

    /// <summary>Existence is not checked on write: a page nobody wrote is kept and
    /// comes back marked as broken, so the caller sees it at once.</summary>
    [Fact]
    public async Task A_reference_to_nothing_is_kept_and_answered_as_broken()
    {
        var id = Guid.NewGuid();
        var tools = Tools(new FakeTaskItems(Entries.Entry("The one", id: id)), new FakeDevbookReferenceResolver());

        var answer = await tools.SetDevbookReferencesAsync(id, "JSdotNet/Backlog", [".devbook/domain/gone.md"], TestContext.Current.CancellationToken);

        Assert.Equal("unknown-page", Assert.Single(answer.References).State);
    }

    [Fact]
    public async Task A_value_naming_no_page_is_refused_with_the_modules_code()
    {
        var id = Guid.NewGuid();
        var tools = Tools(new FakeTaskItems(Entries.Entry("The one", id: id)), new FakeDevbookReferenceResolver());

        var failure = await Assert.ThrowsAsync<McpException>(() =>
            tools.SetDevbookReferencesAsync(id, "JSdotNet/Backlog", ["readme"], TestContext.Current.CancellationToken));

        Assert.Contains("entry.invalid_devbook_reference", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_unknown_entry_is_refused()
    {
        var tools = Tools(new FakeTaskItems(), new FakeDevbookReferenceResolver());

        var failure = await Assert.ThrowsAsync<McpException>(() =>
            tools.SetDevbookReferencesAsync(Guid.NewGuid(), "JSdotNet/Backlog", [Chapter], TestContext.Current.CancellationToken));

        Assert.Contains("entry.not_found", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>The repository is resolved before anything is written, so an
    /// <c>owner/name</c> nobody registered changes nothing.</summary>
    [Fact]
    public async Task An_unregistered_repository_is_refused_before_anything_is_written()
    {
        var id = Guid.NewGuid();
        var entries = new FakeTaskItems(Entries.Entry("The one", id: id));
        var tools = Tools(entries, new FakeDevbookReferenceResolver());

        var failure = await Assert.ThrowsAsync<McpException>(() =>
            tools.SetDevbookReferencesAsync(id, "Someone/Else", [Chapter], TestContext.Current.CancellationToken));

        Assert.Contains(RepositoryScope.NotFoundCode, failure.Message, StringComparison.Ordinal);
        Assert.Empty(entries.DevbookReferenceWrites);
    }

    // --- list_devbook_references ---------------------------------------------

    [Fact]
    public async Task Listing_answers_the_entrys_references_resolved_and_writes_nothing()
    {
        var id = Guid.NewGuid();
        var entries = new FakeTaskItems(Entries.Entry("The one", id: id, devbookReferences: [Chapter, ".devbook/domain/gone.md"]));
        var resolver = new FakeDevbookReferenceResolver(ChapterAnswer);
        var tools = Tools(entries, resolver);

        var answer = await tools.ListDevbookReferencesAsync(id, "JSdotNet/Backlog", TestContext.Current.CancellationToken);

        Assert.Equal(
            [("chapter", "Task item"), ("unknown-page", ".devbook/domain/gone.md")],
            answer.References.Select(reference => (reference.State, reference.Title)));
        Assert.Equal("backlog", Assert.Single(resolver.Resolved).Alias);
        Assert.Empty(entries.DevbookReferenceWrites);
        Assert.Empty(entries.Saves);
    }

    [Fact]
    public async Task Listing_an_unknown_entry_is_refused()
    {
        var tools = Tools(new FakeTaskItems(), new FakeDevbookReferenceResolver());

        var failure = await Assert.ThrowsAsync<McpException>(() =>
            tools.ListDevbookReferencesAsync(Guid.NewGuid(), "JSdotNet/Backlog", TestContext.Current.CancellationToken));

        Assert.Contains("item.not_found", failure.Message, StringComparison.Ordinal);
    }

    // --- read_item ------------------------------------------------------------

    [Fact]
    public async Task Read_item_carries_the_references_as_stored()
    {
        var id = Guid.NewGuid();
        var tools = Tools(
            new FakeTaskItems(Entries.Entry("The one", id: id, devbookReferences: [Chapter, Page])),
            new FakeDevbookReferenceResolver());

        var answer = await tools.ReadItemAsync(id, TestContext.Current.CancellationToken);

        Assert.Equal([Chapter, Page], answer.DevbookReferences);
        Assert.DoesNotContain("devbook", answer.Markdown, StringComparison.OrdinalIgnoreCase);
    }

    private static TrackerTools Tools(FakeTaskItems entries, FakeDevbookReferenceResolver resolver) =>
        new(entries, new FakeRepositoryDirectory([Backlog]), resolver);
}
