using Bunit;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The search panel's second answer: the words are drawn on the keystroke, the
/// meaning is merged in once typing pauses, and only the latest question's answer
/// is ever drawn.
/// <para>
/// Time is a <see cref="FakeTimeProvider"/> the view finds in its services and hands
/// its search box: the box's debounce and the view's meaning pause both elapse only
/// when a test moves the clock. On the real clock these tests slept a fixed 800 ms
/// through about 550 ms of debounce, pause and pool hop, and under a loaded suite
/// either the embed had not happened yet or the two keystrokes had landed far enough
/// apart for the first to pass the debounce on its own.
/// </para>
/// </summary>
public sealed class DevbookSearchViewMeaningTests
{
    /// <summary>The search box's debounce, as the view composes it.</summary>
    private static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(200);

    /// <summary>How long typing pauses before the view asks for the meaning.</summary>
    private static readonly TimeSpan MeaningPause = TimeSpan.FromMilliseconds(350);

    /// <summary>Longer than the real debounce and the real pause together, with
    /// room for the pool hop after them.</summary>
    private static readonly TimeSpan OutlastsTheRealClock = TimeSpan.FromMilliseconds(800);

    private const string Input = "[data-testid='devbook-search-input'] input";

    private readonly FakeTimeProvider _clock = new();

    [Fact]
    public async Task The_meaning_answer_joins_the_words_after_the_pause()
    {
        await using var context = Create(new FixedEmbedder());

        var view = context.Render<DevbookSearchView>();
        Type(view, "standup");

        // Each step only the view's clock can take. The view is given longer than
        // the real debounce, and then longer than the real pause, and draws nothing
        // until the clock moves: a box or a pause on the real clock fails here.
        await Task.Delay(OutlastsTheRealClock, TestContext.Current.CancellationToken);
        Assert.Empty(Addresses(view));

        _clock.Advance(Debounce);

        // The words first, and the meaning not before the pause is over.
        view.WaitForAssertion(() => Assert.Contains("words.md", Addresses(view)));
        await Task.Delay(OutlastsTheRealClock, TestContext.Current.CancellationToken);
        Assert.DoesNotContain("meaning.md", Addresses(view));

        _clock.Advance(MeaningPause);
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
        Type(view, "standup");
        Type(view, "nothing");

        // The second keystroke cancelled the first one's debounce; this one's runs
        // the words for "nothing", which match nothing, and starts its pause.
        _clock.Advance(Debounce);
        view.WaitForElement("[data-testid='devbook-search-none']");

        // The pause over, the meaning is asked and merged, and the view draws it.
        var drawn = view.RenderCount;
        _clock.Advance(MeaningPause);
        await embedder.FirstAsked.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        view.WaitForState(() => view.RenderCount > drawn);

        // And then nothing stale: any embed is released by the same Advance and runs
        // on the pool, so a stale ask or draw could land just after this one. The
        // window it has to land in is real time, as long as the sleep this test used
        // to take.
        var askedAgain = await Task.WhenAny(embedder.SecondAsked, Task.Delay(800, TestContext.Current.CancellationToken));
        await view.InvokeAsync(() => { });

        Assert.NotSame(embedder.SecondAsked, askedAgain);
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
        Type(view, "standup");
        _clock.Advance(Debounce);

        // In the pause: the words are drawn and the meaning is on its way.
        view.WaitForAssertion(() => Assert.Contains("words.md", Addresses(view)));
        await context.DisposeComponentsAsync();

        // Well past the pause. An embed that was not dropped runs on the pool once
        // the pause ends, so the window it has to show up in is still real time,
        // and as long as the sleep these tests used to take.
        _clock.Advance(MeaningPause * 10);
        var asked = await Task.WhenAny(embedder.FirstAsked, Task.Delay(800, TestContext.Current.CancellationToken));

        Assert.NotSame(embedder.FirstAsked, asked);
        Assert.Empty(embedder.Asked);
    }

    /// <summary>A keystroke into the box. Not awaited: the box's handler waits out
    /// the debounce, which elapses only when the test moves the clock. Waits instead
    /// for the box to show the text, which it does before it starts the debounce —
    /// so the clock is never moved past a delay that has not started yet.</summary>
    private static void Type(IRenderedComponent<DevbookSearchView> view, string text)
    {
        view.Find(Input).Input(text);
        view.WaitForAssertion(() => Assert.Equal(text, view.Find(Input).GetAttribute("value")));
    }

    private BunitContext Create(IDevbookQueryEmbedder embedder)
    {
        var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Services.AddSingleton<TimeProvider>(_clock);
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
        private readonly TaskCompletionSource _firstAsked = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _secondAsked = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public IReadOnlyList<string> Asked
        {
            get { lock (_asked) return [.. _asked]; }
        }

        /// <summary>Completes when the embedder is first asked anything.</summary>
        public Task FirstAsked => _firstAsked.Task;

        /// <summary>Completes when the embedder is asked a second time.</summary>
        public Task SecondAsked => _secondAsked.Task;

        public Task<IReadOnlyList<float>?> EmbedAsync(string text, CancellationToken cancellationToken = default)
        {
            int count;
            lock (_asked)
            {
                _asked.Add(text);
                count = _asked.Count;
            }

            _firstAsked.TrySetResult();
            if (count >= 2) _secondAsked.TrySetResult();
            return Task.FromResult<IReadOnlyList<float>?>(text == "standup" ? [1f] : [-1f]);
        }
    }
}
