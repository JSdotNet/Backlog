namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// One question asked of the Brain and every configured repository's devbook
/// (<c>.devbook/domain/devbook/requirements.md#unified-search</c>): which sources
/// are asked under which chip, and how the words and the meaning tiers meet.
/// </summary>
public sealed class DevbookUnifiedSearchTests
{
    private static readonly DevbookSearchSource Backlog = DevbookSearchSource.Repository("backlog");

    /// <summary>Scenario: Everywhere — the Brain group first and the
    /// <c>backlog</c> group after it.</summary>
    [Fact]
    public async Task Everywhere_asks_every_source_and_puts_the_brain_first()
    {
        var search = Create(
            brainWords: Answers(("projects/standup.md", "standup")),
            repositoryWords: Answers(("backlog", ".devbook/domain/inbox/domain.md", "standup")));

        var groups = await search.SearchAsync("standup", only: null, limit: 30, TestContext.Current.CancellationToken);

        Assert.Equal(["Brain", "backlog"], groups.Select(group => group.Source.Label));
        Assert.Equal("projects/standup.md", Assert.Single(groups[0].Hits).Chapter.Reference);
        Assert.Equal(".devbook/domain/inbox/domain.md", Assert.Single(groups[1].Hits).Chapter.Reference);
    }

    /// <summary>Scenario: Brain only — under the Brain chip only the Brain note is
    /// listed, and the repository is not even asked.</summary>
    [Fact]
    public async Task The_brain_chip_asks_only_the_brain()
    {
        var repository = Answers(("backlog", ".devbook/domain/inbox/domain.md", "standup"));
        var search = Create(brainWords: Answers(("projects/standup.md", "standup")), repositoryWords: repository);

        var groups = await search.SearchAsync("standup", DevbookSearchSource.Brain, limit: 30, TestContext.Current.CancellationToken);

        Assert.Equal("Brain", Assert.Single(groups).Source.Label);
        Assert.Empty(repository.Asked);
    }

    [Fact]
    public async Task A_repository_chip_asks_only_that_repository()
    {
        var brain = Answers(("projects/standup.md", "standup"));
        var search = Create(brainWords: brain, repositoryWords: Answers(("backlog", ".devbook/domain/inbox/domain.md", "standup")));

        var groups = await search.SearchAsync("standup", Backlog, limit: 30, TestContext.Current.CancellationToken);

        Assert.Equal("backlog", Assert.Single(groups).Source.Label);
        Assert.Empty(brain.Asked);
    }

    /// <summary>The sources are the Brain, then the repositories in the order the
    /// host lists them.</summary>
    [Fact]
    public void The_sources_are_the_brain_then_the_repositories()
    {
        var search = Create(repositories: ["backlog", "client-app"]);

        Assert.Equal(["Brain", "backlog", "client-app"], search.Sources().Select(source => source.Label));
    }

    /// <summary>A host that composes no Brain searches its repositories alone.</summary>
    [Fact]
    public void Without_a_brain_there_is_no_brain_source()
    {
        var search = Create(noBrain: true);

        Assert.Equal(["backlog"], search.Sources().Select(source => source.Label));
    }

    /// <summary>The meaning tier joins the words when it is switched on and the
    /// question could be embedded: a chapter only the meaning found is listed.</summary>
    [Fact]
    public async Task With_meaning_on_a_chapter_only_the_meaning_found_is_listed()
    {
        var search = Create(
            repositoryWords: Answers(("backlog", "words.md", "standup")),
            repositoryMeaning: Nearest(("backlog", "meaning.md")),
            embedder: new FixedEmbedder([1f, 0f]),
            meaningOn: true);

        var groups = await search.SearchAsync("standup", only: null, limit: 30, TestContext.Current.CancellationToken);

        Assert.Equal(["words.md", "meaning.md"], Assert.Single(groups).Hits.Select(hit => hit.Chapter.Reference));
    }

    /// <summary>With the meaning tier off nothing is embedded: the question never
    /// leaves the machine for a model.</summary>
    [Fact]
    public async Task With_meaning_off_the_question_is_not_embedded()
    {
        var embedder = new FixedEmbedder([1f, 0f]);
        var search = Create(
            repositoryWords: Answers(("backlog", "words.md", "standup")),
            repositoryMeaning: Nearest(("backlog", "meaning.md")),
            embedder: embedder,
            meaningOn: false);

        var groups = await search.SearchAsync("standup", only: null, limit: 30, TestContext.Current.CancellationToken);

        Assert.Equal(["words.md"], Assert.Single(groups).Hits.Select(hit => hit.Chapter.Reference));
        Assert.Equal(0, embedder.Calls);
        Assert.False(search.SearchesByMeaning);
    }

    /// <summary>With meaning on but no deployment to answer, the question is not
    /// sent: a typing pause costs no call that could only fail.</summary>
    [Fact]
    public async Task With_no_deployment_configured_the_question_is_not_embedded()
    {
        var embedder = new FixedEmbedder([1f, 0f]) { Available = false };
        var search = Create(
            repositoryWords: Answers(("backlog", "words.md", "standup")),
            repositoryMeaning: Nearest(("backlog", "meaning.md")),
            embedder: embedder,
            meaningOn: true);

        var groups = await search.SearchAsync("standup", only: null, limit: 30, TestContext.Current.CancellationToken);

        Assert.Equal(["words.md"], Assert.Single(groups).Hits.Select(hit => hit.Chapter.Reference));
        Assert.Equal(0, embedder.Calls);
        Assert.False(search.SearchesByMeaning);
    }

    /// <summary>A model that cannot be reached leaves the words answering: the
    /// meaning tier is additive, never a precondition.</summary>
    [Fact]
    public async Task An_unreachable_model_leaves_the_words_answering()
    {
        var search = Create(
            repositoryWords: Answers(("backlog", "words.md", "standup")),
            repositoryMeaning: Nearest(("backlog", "meaning.md")),
            embedder: new FixedEmbedder(null),
            meaningOn: true);

        var groups = await search.SearchAsync("standup", only: null, limit: 30, TestContext.Current.CancellationToken);

        Assert.Equal(["words.md"], Assert.Single(groups).Hits.Select(hit => hit.Chapter.Reference));
    }

    /// <summary>The words answer on their own, synchronously, so the panel draws
    /// them while the question is still being embedded.</summary>
    [Fact]
    public void The_words_answer_without_the_meaning()
    {
        var embedder = new FixedEmbedder([1f, 0f]);
        var search = Create(
            repositoryWords: Answers(("backlog", "words.md", "standup")),
            repositoryMeaning: Nearest(("backlog", "meaning.md")),
            embedder: embedder,
            meaningOn: true);

        var groups = search.Search("standup", only: null, limit: 30);

        Assert.Equal(["words.md"], Assert.Single(groups).Hits.Select(hit => hit.Chapter.Reference));
        Assert.Equal(0, embedder.Calls);
    }

    /// <summary>A repository with no database is named, with the sentence its
    /// search gave, rather than dropped.</summary>
    [Fact]
    public void A_repository_with_no_database_is_named_as_not_searchable()
    {
        var search = Create(brainWords: Answers(("projects/standup.md", "standup")), repositoryWords: new UnavailableSearch("No index for backlog."));

        var groups = search.Search("standup", only: null, limit: 30);

        Assert.Equal(["Brain", "backlog"], groups.Select(group => group.Source.Label));
        Assert.Equal("No index for backlog.", groups[1].UnavailableMessage);
    }

    /// <summary>The Brain's own words for its missing index: the repository
    /// sentence talks about "this repository", which the Brain is not.</summary>
    [Fact]
    public void A_brain_with_no_database_is_named_in_the_brains_words()
    {
        var search = Create(brainWords: new UnavailableSearch("this repository's devbook index"));

        var groups = search.Search(null, DevbookSearchSource.Brain, limit: 30);

        var message = Assert.Single(groups).UnavailableMessage;
        Assert.NotNull(message);
        Assert.Contains("Brain", message, StringComparison.Ordinal);
        Assert.DoesNotContain("repository", message, StringComparison.Ordinal);
    }

    private static DevbookUnifiedSearch Create(
        IDevbookSearch? repositoryWords = null,
        IDevbookSearch? brainWords = null,
        IDevbookVectorSearch? repositoryMeaning = null,
        IDevbookVectorSearch? brainMeaning = null,
        IDevbookQueryEmbedder? embedder = null,
        bool meaningOn = false,
        IReadOnlyList<string>? repositories = null,
        bool noBrain = false)
    {
        var brain = noBrain ? null : brainWords ?? new ScriptedSearch([]);
        IReadOnlyList<string> aliases = repositories ?? ["backlog"];

        return new DevbookUnifiedSearch(
            () => [.. (brain is null ? [] : new[] { DevbookSearchSource.Brain }), .. aliases.Select(DevbookSearchSource.Repository)],
            repositoryWords ?? new ScriptedSearch([]),
            brain,
            repositoryMeaning,
            brainMeaning,
            embedder,
            () => meaningOn);
    }

    private static ScriptedSearch Answers(params (string Path, string Word)[] brainHits) =>
        new([.. brainHits.Select(hit => (Alias: (string?)null, hit.Path, hit.Word))]);

    private static ScriptedSearch Answers(params (string Alias, string Path, string Word)[] hits) =>
        new([.. hits.Select(hit => (Alias: (string?)hit.Alias, hit.Path, hit.Word))]);

    private static ScriptedVectors Nearest(params (string Alias, string Path)[] hits) => new(hits);

    private static DevbookSearchHit Hit(string path, string excerpt) =>
        new(new DevbookChapterAddress(path, string.Empty, null, null), excerpt, 1);

    /// <summary>Answers each alias with the hits scripted for it whose word is in
    /// the query, and records which aliases it was asked about.</summary>
    private sealed class ScriptedSearch(IReadOnlyList<(string? Alias, string Path, string Word)> hits) : IDevbookSearch
    {
        public List<string?> Asked { get; } = [];

        public DevbookSearchAnswer Search(string? query, string? repositoryAlias = null, string? scope = null, int limit = 30)
        {
            Asked.Add(repositoryAlias);
            if (string.IsNullOrWhiteSpace(query)) return DevbookSearchAnswer.None;

            return DevbookSearchAnswer.For(
            [
                .. hits
                    .Where(hit => hit.Alias is null || string.Equals(hit.Alias, repositoryAlias, StringComparison.Ordinal))
                    .Where(hit => query.Contains(hit.Word, StringComparison.OrdinalIgnoreCase))
                    .Select(hit => Hit(hit.Path, hit.Word))
            ]);
        }
    }

    private sealed class ScriptedVectors(IReadOnlyList<(string Alias, string Path)> hits) : IDevbookVectorSearch
    {
        public DevbookSearchAnswer Nearest(IReadOnlyList<float> queryVector, string? repositoryAlias = null, string? scope = null, int limit = 30) =>
            DevbookSearchAnswer.For(
            [
                .. hits
                    .Where(hit => string.Equals(hit.Alias, repositoryAlias, StringComparison.Ordinal))
                    .Select(hit => Hit(hit.Path, string.Empty))
            ]);
    }

    private sealed class UnavailableSearch(string message) : IDevbookSearch
    {
        public DevbookSearchAnswer Search(string? query, string? repositoryAlias = null, string? scope = null, int limit = 30) =>
            DevbookSearchAnswer.Unavailable(message);
    }

    private sealed class FixedEmbedder(float[]? vector) : IDevbookQueryEmbedder
    {
        public int Calls { get; private set; }

        public bool Available { get; init; } = true;

        public bool IsAvailable => Available;

        public Task<IReadOnlyList<float>?> EmbedAsync(string text, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult<IReadOnlyList<float>?>(vector);
        }
    }
}
