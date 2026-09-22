using Backlog.Desktop.UI.Devbook;
using Backlog.Infrastructure.GitHub;
using Backlog.Modules.Devbook.Abstractions;
using Backlog.SharedKernel.Ai;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// Devbook's Ask AI content: the chapters the index says the question is about,
/// loaded from the folder, with the open chapter pinned — and what is said when
/// there is no index to ask.
/// </summary>
public sealed class DevbookAiContentSourceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "backlog-devbook-ai-content-tests", Guid.NewGuid().ToString("n"));

    private const string ContextChapter = "# Context and scope\n\nThe system in its surroundings, and the sync service beside it.\n";
    private const string DecisionChapter = "# Decisions\n\nWhy the index is a generated database.\n";

    [Fact]
    public async Task A_record_is_the_chapter_path_and_its_text_chosen_by_the_index()
    {
        var (folders, alias) = Folder();
        var search = new StubSearch { Hits = [Hit(".arc42/03-context-and-scope.md")] };

        // The pane has rendered for the repository but shows no single chapter —
        // the Technology graph, say — so there is a scope and nothing to pin.
        var open = new DevbookOpenChapter();
        open.Set(alias, "tech", null);

        var content = await new DevbookAiContentSource(search, folders, open)
            .ComposeAsync(new AiContentRequest("what surrounds the sync service?", 6000), TestContext.Current.CancellationToken);

        Assert.Equal("devbook", content.AreaKey);
        Assert.Equal(1, content.Shown);
        Assert.Equal($"Devbook: 1 entry.\n### .arc42/03-context-and-scope.md\n{ContextChapter.Trim()}", content.Body);

        // The index was asked one content word at a time, never the sentence:
        // its expression ANDs the terms, and a sentence would match nothing.
        Assert.Equal(["surrounds", "sync", "service"], search.Queries);
        Assert.All(search.Queries, query => Assert.Equal(alias, search.AliasFor(query)));
    }

    [Fact]
    public async Task The_open_chapter_is_pinned_and_not_repeated_when_the_index_finds_it_too()
    {
        var (folders, _) = Folder();
        var open = new DevbookOpenChapter();
        open.Set("backlog", "arc42", "09-decisions.md");
        var search = new StubSearch { Hits = [Hit(".arc42/03-context-and-scope.md"), Hit(".arc42/09-decisions.md")] };

        // Room for one chapter: the pinned one goes, the better match does not.
        var budget = AiContentBudget.HeaderReserve("Devbook", 2) + $"### .arc42/09-decisions.md\n{DecisionChapter.Trim()}".Length;
        var content = await new DevbookAiContentSource(search, folders, open)
            .ComposeAsync(new AiContentRequest("sync service", budget), TestContext.Current.CancellationToken);

        Assert.Equal(2, content.Total);
        Assert.True(content.Trimmed);
        Assert.Contains("### .arc42/09-decisions.md", content.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("03-context-and-scope", content.Body, StringComparison.Ordinal);
    }

    /// <summary>The index answers per heading, so one chapter can come back as
    /// several rows: they collapse to the chapter, scored by the best of them,
    /// before the cap on chapters is applied.</summary>
    [Fact]
    public async Task Rows_of_one_chapter_collapse_to_the_chapter_before_the_cap()
    {
        var (folders, alias) = Folder();
        var open = new DevbookOpenChapter();
        open.Set(alias, "tech", null);

        // Nine rows of the context chapter and one of the decisions chapter, the
        // context rows first: a cap of eight applied to rows would never reach it.
        var rows = Enumerable.Range(0, 9).Select(_ => Hit(".arc42/03-context-and-scope.md", 0.5)).Append(Hit(".arc42/09-decisions.md", 9.0)).ToList();
        var search = new StubSearch { Hits = rows };

        var content = await new DevbookAiContentSource(search, folders, open)
            .ComposeAsync(new AiContentRequest("index", 6000), TestContext.Current.CancellationToken);

        Assert.Equal(2, content.Total);
        // Best chapter first: one strong row outranks nine weak ones.
        Assert.StartsWith("Devbook: 2 entries.\n### .arc42/09-decisions.md", content.Body, StringComparison.Ordinal);
        Assert.Contains("### .arc42/03-context-and-scope.md", content.Body, StringComparison.Ordinal);
        Assert.All(search.Limits, limit => Assert.True(limit > DevbookAiContentSource.MaximumHits));
    }

    [Fact]
    public async Task Without_an_index_the_body_says_so_and_carries_only_the_open_chapter()
    {
        var (folders, _) = Folder();
        var open = new DevbookOpenChapter();
        open.Set("backlog", "arc42", "03-context-and-scope.md");
        var search = new StubSearch { Unavailable = "No devbook database has been generated for this repository." };

        var content = await new DevbookAiContentSource(search, folders, open)
            .ComposeAsync(new AiContentRequest("sync", 6000), TestContext.Current.CancellationToken);

        Assert.StartsWith(
            "The devbook search index is unavailable: No devbook database has been generated for this repository. Only the open chapter is included.\n",
            content.Body,
            StringComparison.Ordinal);
        Assert.Contains("### .arc42/03-context-and-scope.md", content.Body, StringComparison.Ordinal);
        Assert.Equal(1, content.Total);
    }

    [Fact]
    public async Task Without_an_index_and_without_an_open_chapter_the_body_says_both()
    {
        var (folders, alias) = Folder();
        var open = new DevbookOpenChapter();
        open.Set(alias, "tech", null);
        var search = new StubSearch { Unavailable = "No devbook database has been generated for this repository." };

        var content = await new DevbookAiContentSource(search, folders, open)
            .ComposeAsync(new AiContentRequest("sync", 6000), TestContext.Current.CancellationToken);

        Assert.Equal(
            "The devbook search index is unavailable and no chapter is open. No devbook database has been generated for this repository.\nDevbook: 0 entries.",
            content.Body);
    }

    /// <summary>The selection skips a record that does not fit. A chapter is the
    /// one record that is instead cut to fit, because the open chapter is
    /// usually the subject of the question and a big one would otherwise be the
    /// one chapter left out.</summary>
    [Fact]
    public async Task A_chapter_longer_than_the_budget_is_cut_to_fit_rather_than_left_out()
    {
        var (folders, _) = Folder();
        var open = new DevbookOpenChapter();
        open.Set("backlog", "arc42", "03-context-and-scope.md");

        const int budget = 120;
        var content = await new DevbookAiContentSource(new StubSearch(), folders, open)
            .ComposeAsync(new AiContentRequest("sync", budget), TestContext.Current.CancellationToken);

        // Shown whole as far as the count goes, and trimmed as far as the reader
        // of the answer is concerned: the assistant did not see the chapter whole.
        Assert.Equal(1, content.Shown);
        Assert.True(content.Trimmed);
        Assert.True(content.Body.Length <= budget);
        var record = content.Body["Devbook: 1 entry.\n".Length..];
        Assert.EndsWith("…", record, StringComparison.Ordinal);
        Assert.Equal(budget - AiContentBudget.HeaderReserve("Devbook", 1), record.Length);
    }

    private static DevbookSearchHit Hit(string path, double score = 1.0) =>
        new(new DevbookChapterAddress(path, string.Empty, null, path.Split('/')[0].TrimStart('.')), "…", score);

    private (IDevbookFolderSource Folders, string Alias) Folder()
    {
        Directory.CreateDirectory(Path.Combine(_root, ".arc42"));
        File.WriteAllText(Path.Combine(_root, ".arc42", "03-context-and-scope.md"), ContextChapter);
        File.WriteAllText(Path.Combine(_root, ".arc42", "09-decisions.md"), DecisionChapter);

        var settings = new WorkspaceSettingsStore(Path.Combine(_root, "store"));
        var gitHub = new GitHubSettingsStore(Path.Combine(_root, "github", "github.json"));
        var (repositories, errors) = GitHubSettings.ParseText("backlog = JSdotNet/Backlog");
        Assert.Empty(errors);

        var repository = Assert.Single(repositories) with
        {
            CloneDirectory = _root,
            DevbookFolders = DevbookFolderSetting.Defaults()
        };
        Assert.Null(gitHub.SetRepositories([repository]));

        return (new DevbookFolderSource(gitHub, settings), repository.Alias);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private sealed class StubSearch : IDevbookSearch
    {
        private readonly Dictionary<string, string?> _aliases = new(StringComparer.Ordinal);

        public IReadOnlyList<DevbookSearchHit> Hits { get; init; } = [];

        public string? Unavailable { get; init; }

        public List<string> Queries { get; } = [];

        public List<int> Limits { get; } = [];

        public string? AliasFor(string query) => _aliases[query];

        public DevbookSearchAnswer Search(string? query, string? repositoryAlias = null, string? scope = null, int limit = 30)
        {
            Queries.Add(query ?? string.Empty);
            Limits.Add(limit);
            _aliases[query ?? string.Empty] = repositoryAlias;

            if (Unavailable is not null) return DevbookSearchAnswer.Unavailable(Unavailable);

            return DevbookSearchAnswer.For([.. Hits.Take(limit)]);
        }
    }
}
