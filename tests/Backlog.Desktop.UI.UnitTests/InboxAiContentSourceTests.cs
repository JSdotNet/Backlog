using Backlog.Desktop.UI.Inbox;
using Backlog.Infrastructure.GitHub;
using Backlog.Modules.Inbox.Abstractions;
using Backlog.SharedKernel.Ai;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The Inbox's Ask AI content: what was captured, not what the pane is
/// showing of it.
/// </summary>
public sealed class InboxAiContentSourceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "backlog-inbox-ai-content-tests", Guid.NewGuid().ToString("n"));

    [Fact]
    public async Task A_record_is_the_title_kind_tags_source_and_body()
    {
        var inbox = new FakeInboxItems();
        inbox.Seed("Read about FTS5", ContentKind.Link, sourceUrl: "https://sqlite.org/fts5.html", bodyMd: "Ranking functions.", tags: ["search", "sqlite"]);
        var state = await LoadedStateAsync(inbox);

        var content = await new InboxAiContentSource(state).ComposeAsync(new AiContentRequest("fts5", 6000), TestContext.Current.CancellationToken);

        Assert.Equal("inbox", content.AreaKey);
        Assert.Equal(1, content.Shown);
        Assert.False(content.Trimmed);
        Assert.Equal(
            "Inbox: 1 entry.\nRead about FTS5\nKind: link\nTags: search, sqlite\nSource: https://sqlite.org/fts5.html\n\nRanking functions.",
            content.Body);
    }

    /// <summary>The slice and the kind chips arrange the screen; the body is the
    /// whole inbox either way. Archived items are the one thing left out, because
    /// archived is the reader's own decision that the item is over.</summary>
    [Fact]
    public async Task The_body_ignores_the_slice_and_the_kind_filter_and_leaves_out_archived_items()
    {
        var inbox = new FakeInboxItems();
        var list = inbox.SeedList("Reading");
        inbox.Seed("Unfiled note");
        inbox.Seed("Filed article", ContentKind.Article, listId: list.Id);
        inbox.Seed("Dismissed", status: InboxStatus.Archived);
        var state = await LoadedStateAsync(inbox);
        var source = new InboxAiContentSource(state);

        var whole = await source.ComposeAsync(new AiContentRequest("anything", 6000), TestContext.Current.CancellationToken);

        state.SelectSlice(InboxDesktopState.ListNavId(list.Id));
        state.ToggleKind("article");

        var filtered = await source.ComposeAsync(new AiContentRequest("anything", 6000), TestContext.Current.CancellationToken);

        Assert.Equal(whole.Body, filtered.Body);
        Assert.Equal(2, whole.Total);
        Assert.Contains("Unfiled note", whole.Body, StringComparison.Ordinal);
        Assert.Contains("Filed article", whole.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("Dismissed", whole.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_selected_item_is_pinned_when_the_budget_holds_one()
    {
        var inbox = new FakeInboxItems();
        inbox.Seed("Newest capture", capturedAt: new DateTimeOffset(2026, 9, 20, 9, 0, 0, TimeSpan.Zero));
        var older = inbox.Seed("Older capture", capturedAt: new DateTimeOffset(2026, 9, 10, 9, 0, 0, TimeSpan.Zero));
        var state = await LoadedStateAsync(inbox);
        state.SelectItem(older.Id);

        var budget = AiContentBudget.HeaderReserve("Inbox", 2) + InboxAiContentSource.Text(older).Length;
        var content = await new InboxAiContentSource(state).ComposeAsync(new AiContentRequest("capture", budget), TestContext.Current.CancellationToken);

        Assert.True(content.Trimmed);
        Assert.Contains("Older capture", content.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("Newest capture", content.Body, StringComparison.Ordinal);
    }

    private async Task<InboxDesktopState> LoadedStateAsync(FakeInboxItems inbox)
    {
        var state = new InboxDesktopState(inbox, new GitHubSettingsStore(Path.Combine(_root, "github.json")));
        await state.InitializeAsync();
        return state;
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }
}
