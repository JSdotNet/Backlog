using Backlog.Infrastructure.Devbook;
using Backlog.Infrastructure.GitHub;
using Backlog.Modules.Devbook.Abstractions;
using Backlog.SharedKernel;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.Devbook;

/// <summary>
/// One question asked of the Brain and every configured repository's devbook at
/// once (<c>.devbook/domain/devbook/features.md#unified-search</c>).
/// <para>
/// Each source is searched in its own database through the ports one devbook
/// already answers through — <see cref="IDevbookSearch"/> by words and
/// <see cref="IDevbookVectorSearch"/> by meaning — so nothing here opens SQLite or
/// learns where a database lives. What this adds is the asking of several and the
/// putting together, which <see cref="DevbookSearchMerge"/> does.
/// </para>
/// <para>
/// Words answer synchronously and first; meaning only when it is switched on and
/// the question could be embedded, and then merged in. The panel draws the words
/// while the meaning is on its way, so a slow or absent model never holds up an
/// answer.
/// </para>
/// </summary>
public sealed class DevbookUnifiedSearch
{
    private readonly Func<IReadOnlyList<DevbookSearchSource>> _sources;
    private readonly IDevbookSearch _repositoryWords;
    private readonly IDevbookSearch? _brainWords;
    private readonly IDevbookVectorSearch? _repositoryMeaning;
    private readonly IDevbookVectorSearch? _brainMeaning;
    private readonly IDevbookQueryEmbedder? _embedder;
    private readonly Func<bool> _meaningOn;
    private readonly Func<string?>? _brainFolderProblem;

    /// <param name="sources">The devbooks there are to search, in the order their
    /// chips are drawn: the Brain first, then the repositories as configured.</param>
    /// <param name="repositoryWords">Search by words, for a repository alias.</param>
    /// <param name="brainWords">Search by words over the Brain, or null in a host
    /// that composes no Brain.</param>
    /// <param name="repositoryMeaning">Search by meaning for a repository, if any.</param>
    /// <param name="brainMeaning">Search by meaning over the Brain, if any.</param>
    /// <param name="embedder">What turns a question into a vector, if anything.</param>
    /// <param name="meaningOn">Whether search by meaning is switched on.</param>
    /// <param name="brainFolderProblem">Why the Brain folder cannot be read, when it
    /// cannot — said instead of "no index" for a Brain that has no folder.</param>
    public DevbookUnifiedSearch(
        Func<IReadOnlyList<DevbookSearchSource>> sources,
        IDevbookSearch repositoryWords,
        IDevbookSearch? brainWords,
        IDevbookVectorSearch? repositoryMeaning,
        IDevbookVectorSearch? brainMeaning,
        IDevbookQueryEmbedder? embedder,
        Func<bool> meaningOn,
        Func<string?>? brainFolderProblem = null)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(repositoryWords);
        ArgumentNullException.ThrowIfNull(meaningOn);

        _sources = sources;
        _repositoryWords = repositoryWords;
        _brainWords = brainWords;
        _repositoryMeaning = repositoryMeaning;
        _brainMeaning = brainMeaning;
        _embedder = embedder;
        _meaningOn = meaningOn;
        _brainFolderProblem = brainFolderProblem;
    }

    /// <summary>
    /// The search the way the hosts compose it: the Brain through its own folder
    /// source, every configured repository with a devbook through the repository
    /// ports. Resolved rather than injected, as the pane resolves the Brain, so a
    /// host — or a test — that composes no Brain or no model gets a search without
    /// them instead of failing on construction.
    /// </summary>
    public static DevbookUnifiedSearch From(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var scope = services.GetRequiredService<DevbookScope>();
        var repositories = services.GetRequiredService<GitHubSettingsStore>();
        var features = services.GetRequiredService<IAppFeatureSettings>();
        var brain = services.GetService<IBrainFolderSource>();

        return new DevbookUnifiedSearch(
            () => Sources(scope, repositories, brain),
            services.GetRequiredService<IDevbookSearch>(),
            brain is null ? null : new DevbookFullTextSearch(brain),
            services.GetService<IDevbookVectorSearch>(),
            brain is null ? null : new DevbookSemanticSearch(brain, DevbookEmbeddingModel.Default),
            services.GetService<IDevbookQueryEmbedder>(),
            () => features.IsEnabled(DevbookFeatures.SemanticSearch),
            brain is null
                ? null
                : () => brain.Resolve(BrainConventions.FolderKey) is { Available: false, Message: { Length: > 0 } message } ? message : null);
    }

    /// <summary>The Brain, when the host composes one and Devbook offers it, then
    /// every configured repository that has a devbook to read, in the registry's
    /// order. A repository with no devbook folders is not a source: it has no
    /// devbook, which is different from a devbook with no index yet.</summary>
    private static IReadOnlyList<DevbookSearchSource> Sources(DevbookScope scope, GitHubSettingsStore repositories, IBrainFolderSource? brain)
    {
        var sources = new List<DevbookSearchSource>();
        if (brain is not null && scope.BrainOffered) sources.Add(DevbookSearchSource.Brain);

        foreach (var repository in repositories.Current.Repositories)
        {
            if (scope.VisibleAreas(repository.Alias).Count > 0) sources.Add(DevbookSearchSource.Repository(repository.Alias));
        }

        return sources;
    }

    /// <summary>What the chips offer besides Everywhere, in their order.</summary>
    public IReadOnlyList<DevbookSearchSource> Sources() => _sources();

    /// <summary>Whether a question is also searched by meaning: the switch is on
    /// and something can embed it.</summary>
    public bool SearchesByMeaning => _embedder is not null && _meaningOn();

    /// <summary>
    /// The words answer, grouped: every source under Everywhere
    /// (<paramref name="only"/> null), or the one source a chip names.
    /// Synchronous, so the panel draws it on the keystroke that asked.
    /// </summary>
    public IReadOnlyList<DevbookSearchGroup> Search(string? query, DevbookSearchSource? only, int limit) =>
        DevbookSearchMerge.Group(Words(query, only, limit));

    /// <summary>The words answer per source, in the order the sources were asked
    /// and not yet grouped — what <see cref="WithMeaningAsync"/> merges into.</summary>
    public IReadOnlyList<DevbookSearchGroup> Words(string? query, DevbookSearchSource? only, int limit) =>
        [.. Asked(only).Select(source => WordsOf(source, query, limit))];

    /// <summary>
    /// The words and the meaning answers merged, grouped. The question is embedded
    /// once, for every source; when it cannot be, or search by meaning is off,
    /// this is the words answer.
    /// </summary>
    public async Task<IReadOnlyList<DevbookSearchGroup>> SearchAsync(
        string? query,
        DevbookSearchSource? only,
        int limit,
        CancellationToken cancellationToken = default)
    {
        var words = Words(query, only, limit);
        return DevbookSearchMerge.Group(await WithMeaningAsync(words, query, limit, cancellationToken).ConfigureAwait(false));
    }

    /// <summary>
    /// <paramref name="words"/> — a <see cref="Words(string?, DevbookSearchSource?, int)"/>
    /// answer — with each source's meaning answer merged in, ungrouped. The words
    /// unchanged when search by meaning is off, the question is blank, or it could
    /// not be embedded; so the words a panel already drew are never asked twice.
    /// </summary>
    public async Task<IReadOnlyList<DevbookSearchGroup>> WithMeaningAsync(
        IReadOnlyList<DevbookSearchGroup> words,
        string? query,
        int limit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(words);

        if (!SearchesByMeaning || string.IsNullOrWhiteSpace(query)) return words;

        var vector = await _embedder!.EmbedAsync(query.Trim(), cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        if (vector is not { Count: > 0 }) return words;

        return [.. words.Select(group => WithMeaning(group, vector, limit))];
    }

    private IEnumerable<DevbookSearchSource> Asked(DevbookSearchSource? only)
    {
        var sources = _sources();
        return only is null
            ? sources
            : sources.Where(source => string.Equals(source.Key, only.Key, StringComparison.OrdinalIgnoreCase));
    }

    private DevbookSearchGroup WordsOf(DevbookSearchSource source, string? query, int limit)
    {
        var search = source.IsBrain ? _brainWords : _repositoryWords;
        if (search is null) return DevbookSearchGroup.NotSearchable(source, BrainIndexMissing);

        var answer = search.Search(query, AliasOf(source), scope: null, limit);

        return answer.IsUnavailable
            ? DevbookSearchGroup.NotSearchable(source, source.IsBrain ? BrainUnavailableMessage() : answer.UnavailableMessage!)
            : DevbookSearchGroup.For(source, answer.Hits);
    }

    private DevbookSearchGroup WithMeaning(DevbookSearchGroup words, IReadOnlyList<float> vector, int limit)
    {
        if (words.IsUnavailable) return words;

        var search = words.Source.IsBrain ? _brainMeaning : _repositoryMeaning;
        if (search is null) return words;

        var meaning = search.Nearest(vector, AliasOf(words.Source), scope: null, limit);
        if (meaning.IsUnavailable || meaning.Hits.Count == 0) return words;

        return words with { Hits = DevbookSearchMerge.Merge(words.Hits, meaning.Hits, limit) };
    }

    /// <summary>The alias a search port takes: null for the Brain, whose source
    /// answers the unscoped question.</summary>
    private static string? AliasOf(DevbookSearchSource source) => source.IsBrain ? null : source.Key;

    /// <summary>The Brain in its own words. The repository sentence speaks of
    /// "this repository's devbook index", which the Brain is not; and a Brain with
    /// no folder at all is told that, not that an index is missing.</summary>
    private string BrainUnavailableMessage() => _brainFolderProblem?.Invoke() ?? BrainIndexMissing;

    private const string BrainIndexMissing =
        "Search needs the Brain's index, which has not been built yet. Backlog builds it in the background when the Brain is opened — try again in a moment.";
}
