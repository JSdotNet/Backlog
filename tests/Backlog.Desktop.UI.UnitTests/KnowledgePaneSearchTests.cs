using System.Text.RegularExpressions;

using Backlog.Infrastructure.Copilot;
using Backlog.Infrastructure.GitHub;
using Backlog.Infrastructure.Knowledge;

using Bunit;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The search surface in the knowledge pane, and the one state local ADR 0004's
/// degradation ladder shows a user.
///
/// <para>Every other knowledge consumer degrades quietly: no index means it reads
/// the Markdown, and nobody is told anything, because browsing touches the
/// handful of files on screen. Search cannot do that — reading 639 chapters per
/// query is not a slower answer but a hang — so a repository with no generated
/// database has no search, and the surface says so <em>and names the command that
/// fixes it</em>. An empty result list is the specific wrong answer here: it
/// claims nothing matched, which is a different statement and an untrue one. The
/// atlas already refuses to fall back for the same reason and its message is the
/// model this one follows.</para>
///
/// <para>The adapter under test is the real one, over a real database file, so
/// what these assert is the pane wired to retrieval rather than the pane wired to
/// a fake that always says what the test wants.</para>
/// </summary>
public sealed class KnowledgePaneSearchTests : IDisposable
{
    private readonly List<string> _roots = [];

    [Fact]
    public async Task Search_is_not_offered_while_the_feature_is_off()
    {
        await using var harness = CreateHarness(searchEnabled: false);

        var component = harness.Render();
        component.WaitForAssertion(() => Assert.NotNull(component.Find("[data-testid='knowledge-atlas-toggle']")));

        Assert.Empty(component.FindAll("[data-testid='knowledge-search-toggle']"));
    }

    [Fact]
    public async Task With_no_database_search_says_so_and_names_the_command()
    {
        await using var harness = CreateHarness(withDatabase: false);

        var component = harness.Render();
        component.WaitForAssertion(() => Assert.NotNull(component.Find("[data-testid='knowledge-search-toggle']")));

        component.Find("[data-testid='knowledge-search-toggle']").Click();

        component.WaitForAssertion(() => Assert.NotNull(component.Find("[data-testid='knowledge-search-unavailable']")));

        var message = component.Find("[data-testid='knowledge-search-unavailable']").TextContent;

        Assert.Contains("has not been written yet", message, StringComparison.Ordinal);
        Assert.Contains(KnowledgeRetrieval.BuildCommand, message, StringComparison.Ordinal);

        // The specific wrong answer: a list with nothing in it.
        Assert.Empty(component.FindAll("[data-testid='knowledge-search-results']"));
    }

    /// <summary>
    /// The reader has not typed anything yet and is already told there is nothing
    /// to search. Finding out after asking a question would be the empty list this
    /// state exists to replace, one keystroke later.
    /// </summary>
    [Fact]
    public async Task The_unavailable_state_arrives_before_anything_is_typed()
    {
        await using var harness = CreateHarness(withDatabase: false);

        var component = harness.Render();
        component.WaitForAssertion(() => Assert.NotNull(component.Find("[data-testid='knowledge-search-toggle']")));
        component.Find("[data-testid='knowledge-search-toggle']").Click();

        component.WaitForAssertion(() => Assert.NotNull(component.Find("[data-testid='knowledge-search-unavailable']")));

        Assert.Empty(component.Find("[data-testid='knowledge-search-input'] input").GetAttribute("value") ?? string.Empty);
    }

    /// <summary>With an index, the same surface opens on an invitation rather than
    /// on a report about a missing file.</summary>
    [Fact]
    public async Task With_a_database_search_opens_ready_to_be_asked()
    {
        await using var harness = CreateHarness();

        var component = harness.Render();
        component.WaitForAssertion(() => Assert.NotNull(component.Find("[data-testid='knowledge-search-toggle']")));
        component.Find("[data-testid='knowledge-search-toggle']").Click();

        component.WaitForAssertion(() => Assert.NotNull(component.Find("[data-testid='knowledge-search-prompt']")));
        Assert.Empty(component.FindAll("[data-testid='knowledge-search-unavailable']"));
    }

    /// <summary>
    /// A result is a chapter address. The pane renders the path and slug the
    /// domain asks it to — "results name the chapter they came from rather than
    /// returning loose text" — because a chapter address is what everything else
    /// in this product links by, and it is what makes the row openable.
    /// </summary>
    [Fact]
    public async Task A_result_shows_the_chapter_address_it_came_from()
    {
        await using var harness = CreateHarness();

        var component = harness.Render();
        component.WaitForAssertion(() => Assert.NotNull(component.Find("[data-testid='knowledge-search-toggle']")));
        component.Find("[data-testid='knowledge-search-toggle']").Click();
        component.WaitForAssertion(() => Assert.NotNull(component.Find("[data-testid='knowledge-search-input'] input")));

        component.Find("[data-testid='knowledge-search-input'] input").Input("capture");

        component.WaitForAssertion(() => Assert.NotNull(component.Find("[data-testid='knowledge-search-results']")));

        var results = component.Find("[data-testid='knowledge-search-results']");

        Assert.Contains("Quick capture", results.TextContent, StringComparison.Ordinal);
        Assert.Equal(
            ".domain/inbox/domain.md#quick-capture",
            component.Find(".knowledge-search__address").TextContent.Trim());
    }

    /// <summary>Following a result closes the search, for the reason the atlas
    /// closes on the same move: the reader asked to go and read that chapter.</summary>
    [Fact]
    public async Task Opening_a_result_closes_the_search()
    {
        await using var harness = CreateHarness();

        var component = harness.Render();
        component.WaitForAssertion(() => Assert.NotNull(component.Find("[data-testid='knowledge-search-toggle']")));
        component.Find("[data-testid='knowledge-search-toggle']").Click();
        component.WaitForAssertion(() => Assert.NotNull(component.Find("[data-testid='knowledge-search-input'] input")));
        component.Find("[data-testid='knowledge-search-input'] input").Input("capture");
        component.WaitForAssertion(() => Assert.NotNull(component.Find(".knowledge-search__open")));

        component.Find(".knowledge-search__open").Click();

        component.WaitForAssertion(() => Assert.Empty(component.FindAll("[data-testid='knowledge-search']")));
        Assert.Equal("Search", component.Find("[data-testid='knowledge-search-toggle']").TextContent.Trim());
    }

    /// <summary>The two readings of the same panel body are mutually exclusive:
    /// opening one puts the other away rather than stacking over it.</summary>
    [Fact]
    public async Task Opening_search_closes_the_atlas()
    {
        await using var harness = CreateHarness();

        var component = harness.Render();
        component.WaitForAssertion(() => Assert.NotNull(component.Find("[data-testid='knowledge-atlas-toggle']")));
        component.Find("[data-testid='knowledge-atlas-toggle']").Click();
        component.WaitForAssertion(() => Assert.NotNull(component.Find("[data-testid='knowledge-atlas']")));

        component.Find("[data-testid='knowledge-search-toggle']").Click();

        component.WaitForAssertion(() => Assert.NotNull(component.Find("[data-testid='knowledge-search']")));
        Assert.Empty(component.FindAll("[data-testid='knowledge-atlas']"));
    }

    private const string ContextMap = """
        # Context Map

        ```meta
        status: draft
        ```

        The context map.
        """;

    private const string InboxChapter = """
        # Inbox

        ## Quick capture

        Quick capture never blocks on a decision.
        """;

    private Harness CreateHarness(bool withDatabase = true, bool searchEnabled = true)
    {
        var root = Path.Combine(Path.GetTempPath(), "backlog-knowledge-pane-search", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, ".domain", "inbox"));
        Directory.CreateDirectory(Path.Combine(root, ".arc42"));
        _roots.Add(root);

        File.WriteAllText(Path.Combine(root, ".domain", "context-map.md"), ContextMap);
        File.WriteAllText(Path.Combine(root, ".domain", "inbox", "domain.md"), InboxChapter);
        File.WriteAllText(Path.Combine(root, ".arc42", "03-context-and-scope.md"), "# Context and scope\n\nThe system in its surroundings.\n");

        if (withDatabase) SeedDatabase(root);

        var settings = new WorkspaceSettingsStore(Path.Combine(root, "store"));
        var gitHub = new GitHubSettingsStore(Path.Combine(root, "github", "github.json"));
        var features = new AppFeatureSettingsStore(AppFeatures.All, Path.Combine(root, "features", "features.json"));
        _ = features.SetEnabled(KnowledgeFeatures.KnowledgeSections, true);
        _ = features.SetEnabled(KnowledgeFeatures.RepositoryKnowledge, true);
        _ = features.SetEnabled(KnowledgeFeatures.Search, searchEnabled);

        var (repositories, errors) = GitHubSettings.ParseText("JSdotNet/Backlog");
        Assert.Empty(errors);

        var repository = Assert.Single(repositories) with
        {
            CloneDirectory = root,
            KnowledgeFolders = [.. KnowledgeFolderSetting.Defaults().Select(folder => folder with { Enabled = folder.Key is ".domain" or ".arc42" })]
        };
        Assert.Null(gitHub.SetRepositories([repository]));

        var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        context.Services.AddSingleton(settings);
        context.Services.AddSingleton(gitHub);
        context.Services.AddSingleton<IAppFeatureSettings>(features);
        context.Services.AddSingleton<IKnowledgeFolderSource>(new KnowledgeFolderSource(gitHub, settings));
        context.Services.AddSingleton(sp => new DomainKnowledgeStore(sp.GetRequiredService<IKnowledgeFolderSource>()));
        context.Services.AddSingleton<Arc42KnowledgeStore>();
        context.Services.AddSingleton(new KnowledgeCopilotCli(new UnavailableCopilotCliLauncher()));
        context.Services.AddSingleton<KnowledgeChapterWriter>();
        context.Services.AddSingleton<KnowledgeMenu>();
        context.Services.AddSingleton<KnowledgeScope>();
        context.Services.AddSingleton<ILocalGitRepositoryService, LocalGitRepositoryService>();
        context.Services.AddSingleton<KnowledgeUpdateService>();
        context.Services.AddSingleton<IGitHubBranchCatalog>(new StubBranchCatalog());
        context.Services.AddSingleton<KnowledgeSourceSelection>();
        context.Services.AddSingleton<IFolderEditorLauncher, UnsupportedFolderEditorLauncher>();
        context.Services.AddSingleton<KnowledgeFolderOpenService>();
        context.Services.AddSingleton<KnowledgeAtlasService>();
        context.Services.AddSingleton<IGitFileHistoryService>(new StubGitFileHistory());
        // The real adapter over the real file. A stub here would prove the pane
        // renders what a stub returns, which is not the thing that can break.
        context.Services.AddSingleton<IKnowledgeSearch>(sp =>
            new KnowledgeFullTextSearch(sp.GetRequiredService<IKnowledgeFolderSource>()));

        return new Harness(context, repository.Alias);
    }

    /// <summary>
    /// A database with one searchable chapter in it, created from the writer's own
    /// DDL rather than from a copy of it — the same rule
    /// <c>KnowledgeIndexDatabaseTests</c> follows, and for the same reason: nothing
    /// on the C# side restates this schema, including a fixture.
    /// </summary>
    private static void SeedDatabase(string root)
    {
        var databasePath = Path.Combine(root, "_meta", "knowledge.db");
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);

        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate
        }.ToString());

        connection.Open();
        Execute(connection, "PRAGMA journal_mode = WAL");
        Execute(connection, WriterSchema());
        Execute(
            connection,
            $"INSERT INTO meta (key, value) VALUES ('schemaVersion', '{KnowledgeDatabaseSchema.Version}')");
        Execute(connection, """
            INSERT INTO chapter (path, folder, slug, level, title, status, line, text, search_text, content_hash, source_hash, size, mtime)
            VALUES ('.domain/inbox/domain.md', 'domain', 'quick-capture', 2, 'Quick capture', 'active', 3,
                    'Quick capture never blocks on a decision.',
                    'Quick capture never blocks on a decision.', 'aa', 'bb', 41, 0)
            """);
        Execute(connection, "INSERT INTO chapter_fts (rowid, title, search_text) SELECT id, title, search_text FROM chapter");

        SqliteConnection.ClearPool(connection);
        connection.Close();
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static string WriterSchema()
    {
        var source = File.ReadAllText(RepositoryRoot.File("tools", "knowledge", "knowledge-schema.mjs"));
        var match = Regex.Match(source, @"export const KNOWLEDGE_SCHEMA = `(?<value>[^`]*)`", RegexOptions.Singleline);

        Assert.True(match.Success, "tools/knowledge/knowledge-schema.mjs no longer exports KNOWLEDGE_SCHEMA.");
        return match.Groups["value"].Value;
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();

        foreach (var root in _roots.Where(Directory.Exists))
        {
            try { Directory.Delete(root, recursive: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    private sealed record Harness(BunitContext Context, string RepositoryAlias) : IAsyncDisposable
    {
        public IRenderedComponent<KnowledgePane> Render() =>
            Context.Render<KnowledgePane>(parameters => parameters.Add(pane => pane.RepositoryAlias, RepositoryAlias));

        public async ValueTask DisposeAsync() => await Context.DisposeAsync();
    }
}
