using Bunit;

using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The search panel's second answer: the words are drawn on the keystroke, the
/// meaning is merged in once typing pauses, and only the latest question's answer
/// is ever drawn.
/// </summary>
public sealed class DevbookSearchViewMeaningTests
{
    [Fact]
    public async Task The_meaning_answer_joins_the_words_after_the_pause()
    {
        await using var context = Create(new FixedEmbedder());

        var view = context.Render<DevbookSearchView>();
        view.Find("[data-testid='devbook-search-input'] input").Input("standup");

        // The words first.
        view.WaitForAssertion(() => Assert.Contains("words.md", Addresses(view)));

        view.WaitForAssertion(() => Assert.Equal(["words.md", "meaning.md"], Addresses(view)), TimeSpan.FromSeconds(5));
    }

    /// <summary>A question replaced before its meaning arrived never draws: the
    /// panel shows the answer to what is in the box.</summary>
    [Fact]
    public async Task Only_the_latest_question_is_drawn()
    {
        var embedder = new FixedEmbedder();
        await using var context = Create(embedder);

        var view = context.Render<DevbookSearchView>();
        view.Find("[data-testid='devbook-search-input'] input").Input("standup");
        view.Find("[data-testid='devbook-search-input'] input").Input("nothing");

        await Task.Delay(800, TestContext.Current.CancellationToken);

        Assert.Empty(Addresses(view));
        Assert.Equal(["nothing"], embedder.Asked);
    }

    /// <summary>Closing the panel while a meaning answer is on its way drops it
    /// without a render into a disposed component.</summary>
    [Fact]
    public async Task Closing_the_panel_during_the_pause_drops_the_meaning()
    {
        var embedder = new FixedEmbedder();
        await using var context = Create(embedder);

        var view = context.Render<DevbookSearchView>();
        view.Find("[data-testid='devbook-search-input'] input").Input("standup");
        await context.DisposeComponentsAsync();

        await Task.Delay(800, TestContext.Current.CancellationToken);

        Assert.Empty(embedder.Asked);
    }

    private static BunitContext Create(IDevbookQueryEmbedder embedder)
    {
        var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Services.AddSingleton(new DevbookUnifiedSearch(
            () => [DevbookSearchSource.Repository("backlog")],
            new WordsSearch(),
            brainWords: null,
            repositoryMeaning: new MeaningSearch(),
            brainMeaning: null,
            embedder,
            meaningOn: () => true));
        return context;
    }

    private static IReadOnlyList<string> Addresses(IRenderedComponent<DevbookSearchView> view) =>
        [.. view.FindAll(".devbook-search__address").Select(address => address.TextContent.Trim())];

    private static DevbookSearchHit Hit(string path) =>
        new(new DevbookChapterAddress(path, string.Empty, null, null), "standup", 1);

    private sealed class WordsSearch : IDevbookSearch
    {
        public DevbookSearchAnswer Search(string? query, string? repositoryAlias = null, string? scope = null, int limit = 30) =>
            query == "standup" ? DevbookSearchAnswer.For([Hit("words.md")]) : DevbookSearchAnswer.None;
    }

    private sealed class MeaningSearch : IDevbookVectorSearch
    {
        public DevbookSearchAnswer Nearest(IReadOnlyList<float> queryVector, string? repositoryAlias = null, string? scope = null, int limit = 30) =>
            queryVector[0] > 0 ? DevbookSearchAnswer.For([Hit("meaning.md")]) : DevbookSearchAnswer.None;
    }

    /// <summary>Embeds "standup" as a vector the meaning search answers and
    /// anything else as one it does not, recording what it was asked.</summary>
    private sealed class FixedEmbedder : IDevbookQueryEmbedder
    {
        private readonly List<string> _asked = [];

        public IReadOnlyList<string> Asked
        {
            get { lock (_asked) return [.. _asked]; }
        }

        public Task<IReadOnlyList<float>?> EmbedAsync(string text, CancellationToken cancellationToken = default)
        {
            lock (_asked) _asked.Add(text);
            return Task.FromResult<IReadOnlyList<float>?>(text == "standup" ? [1f] : [-1f]);
        }
    }
}
