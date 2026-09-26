using System.Text.RegularExpressions;

using Backlog.Infrastructure.Copilot;
using Backlog.Infrastructure.GitHub;
using Backlog.Infrastructure.Devbook;

using Bunit;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The search surface in the Devbook pane, and the one state local ADR 0004's
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
public sealed class DevbookPaneSearchTests : IDisposable
{
    private readonly List<string> _roots = [];

    [Fact]
    public async Task Search_is_not_offered_while_the_feature_is_off()
    {
        await using var harness = CreateHarness(searchEnabled: false);

        var component = harness.Render();
        component.WaitForAssertion(() => Assert.NotNull(component.Find("[data-testid='devbook-atlas-toggle']")));

        Assert.Empty(component.FindAll("[data-testid='devbook-search-toggle']"));
    }

    [Fact]
    public async Task With_no_database_search_says_so_and_names_the_command()
    {
        await using var harness = CreateHarness(withDatabase: false);

        var component = harness.Render();
        component.WaitForAssertion(() => Assert.NotNull(component.Find("[data-testid='devbook-search-toggle']")));

        component.Find("[data-testid='devbook-search-toggle']").Click();

        component.WaitForAssertion(() => Assert.NotNull(component.Find("[data-testid='devbook-search-unavailable']")));

        var message = component.Find("[data-testid='devbook-search-unavailable']").TextContent;

        Assert.Contains("has not been built yet", message, StringComparison.Ordinal);

        // The specific wrong answer: a list with nothing in it.
        Assert.Empty(component.FindAll("[data-testid='devbook-search-results']"));
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
        component.WaitForAssertion(() => Assert.NotNull(component.Find("[data-testid='devbook-search-toggle']")));
        component.Find("[data-testid='devbook-search-toggle']").Click();

        component.WaitForAssertion(() => Assert.NotNull(component.Find("[data-testid='devbook-search-unavailable']")));

        Assert.Empty(component.Find("[data-testid='devbook-search-input'] input").GetAttribute("value") ?? string.Empty);
    }

    /// <summary>With an index, the same surface opens on an invitation rather than
    /// on a report about a missing file.</summary>
    [Fact]
    public async Task With_a_database_search_opens_ready_to_be_asked()
    {
        await using var harness = CreateHarness();

        var component = harness.Render();
        component.WaitForAssertion(() => Assert.NotNull(component.Find("[data-testid='devbook-search-toggle']")));
        component.Find("[data-testid='devbook-search-toggle']").Click();

        component.WaitForAssertion(() => Assert.NotNull(component.Find("[data-testid='devbook-search-prompt']")));
        Assert.Empty(component.FindAll("[data-testid='devbook-search-unavailable']"));
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
        component.WaitForAssertion(() => Assert.NotNull(component.Find("[data-testid='devbook-search-toggle']")));
        component.Find("[data-testid='devbook-search-toggle']").Click();
        component.WaitForAssertion(() => Assert.NotNull(component.Find("[data-testid='devbook-search-input'] input")));

        component.Find("[data-testid='devbook-search-input'] input").Input("capture");

        component.WaitForAssertion(() => Assert.NotNull(component.Find("[data-testid='devbook-search-results']")));

        var results = component.Find("[data-testid='devbook-search-results']");

        Assert.Contains("Quick capture", results.TextContent, StringComparison.Ordinal);
        Assert.Equal(
            ".domain/inbox/domain.md#quick-capture",
            component.Find(".devbook-search__address").TextContent.Trim());
    }

    /// <summary>Following a result closes the search, for the reason the atlas
    /// closes on the same move: the reader asked to go and read that chapter.</summary>
    [Fact]
    public async Task Opening_a_result_closes_the_search()
    {
        await using var harness = CreateHarness();

        var component = harness.Render();
        component.WaitForAssertion(() => Assert.NotNull(component.Find("[data-testid='devbook-search-toggle']")));
        component.Find("[data-testid='devbook-search-toggle']").Click();
        component.WaitForAssertion(() => Assert.NotNull(component.Find("[data-testid='devbook-search-input'] input")));
        component.Find("[data-testid='devbook-search-input'] input").Input("capture");
        component.WaitForAssertion(() => Assert.NotNull(component.Find(".devbook-search__open")));

        component.Find(".devbook-search__open").Click();

        component.WaitForAssertion(() => Assert.Empty(component.FindAll("[data-testid='devbook-search']")));
        Assert.Equal("Search", component.Find("[data-testid='devbook-search-toggle']").TextContent.Trim());
    }

    /// <summary>The two readings of the same panel body are mutually exclusive:
    /// opening one puts the other away rather than stacking over it.</summary>
    [Fact]
    public async Task Opening_search_closes_the_atlas()
    {
        await using var harness = CreateHarness();

        var component = harness.Render();
        component.WaitForAssertion(() => Assert.NotNull(component.Find("[data-testid='devbook-atlas-toggle']")));
        component.Find("[data-testid='devbook-atlas-toggle']").Click();
        component.WaitForAssertion(() => Assert.NotNull(component.Find("[data-testid='devbook-atlas']")));

        component.Find("[data-testid='devbook-search-toggle']").Click();

        component.WaitForAssertion(() => Assert.NotNull(component.Find("[data-testid='devbook-search']")));
        Assert.Empty(component.FindAll("[data-testid='devbook-atlas']"));
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
        var root = Path.Combine(Path.GetTempPath(), "backlog-devbook-pane-search", Guid.NewGuid().ToString("N"));
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
        _ = features.SetEnabled(DevbookFeatures.DevbookSections, true);
        _ = features.SetEnabled(DevbookFeatures.RepositoryDevbook, true);
        _ = features.SetEnabled(DevbookFeatures.Search, searchEnabled);

        var (repositories, errors) = GitHubSettings.ParseText("JSdotNet/Backlog");
        Assert.Empty(errors);

        var repository = Assert.Single(repositories) with
        {
            CloneDirectory = root,
            DevbookFolders = [.. DevbookFolderSetting.Defaults().Select(folder => folder with { Enabled = folder.Key is ".domain" or ".arc42" })]
        };
        Assert.Null(gitHub.SetRepositories([repository]));

        var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        context.Services.AddSingleton(settings);
        context.Services.AddSingleton(gitHub);
        context.Services.AddSingleton<IAppFeatureSettings>(features);
        context.Services.AddSingleton<IDevbookFolderSource>(new DevbookFolderSource(gitHub, settings));
        context.Services.AddSingleton(sp => new DomainDevbookStore(sp.GetRequiredService<IDevbookFolderSource>()));
        context.Services.AddSingleton<Arc42DevbookStore>();
        context.Services.AddSingleton(new DevbookCopilotCli(new UnavailableCopilotCliLauncher()));
        context.Services.AddSingleton<DevbookChapterWriter>();
        context.Services.AddSingleton<DevbookMenu>();
        context.Services.AddSingleton<DevbookScope>();
        // The pane publishes its open chapter here for the Ask AI source; a pane
        // rendered without it would fail on inject, as the application hosts would.
        context.Services.AddScoped<DevbookOpenChapter>();
        context.Services.AddSingleton<ILocalGitRepositoryService, LocalGitRepositoryService>();
        context.Services.AddSingleton<DevbookUpdateService>();
        context.Services.AddSingleton<IGitHubBranchCatalog>(new StubBranchCatalog());
        context.Services.AddSingleton<DevbookSourceSelection>();
        context.Services.AddSingleton<IFolderEditorLauncher, UnsupportedFolderEditorLauncher>();
        context.Services.AddSingleton<DevbookFolderOpenService>();
        context.Services.AddSingleton<DevbookAtlasService>();
        context.Services.AddSingleton<IGitFileHistoryService>(new StubGitFileHistory());
        // The real adapter over the real file. A stub here would prove the pane
        // renders what a stub returns, which is not the thing that can break.
        context.Services.AddSingleton<IDevbookSearch>(sp =>
            new DevbookFullTextSearch(sp.GetRequiredService<IDevbookFolderSource>()));

        return new Harness(context, repository.Alias);
    }

    /// <summary>
    /// A database with one searchable chapter in it, created from the writer's own
    /// DDL rather than from a copy of it — the same rule
    /// <c>DevbookIndexDatabaseTests</c> follows, and for the same reason: nothing
    /// on the C# side restates this schema, including a fixture.
    /// </summary>
    private static void SeedDatabase(string root)
    {
        var databasePath = DevbookDatabaseLocation.ForRepositoryRoot(root)!;
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
            $"INSERT INTO meta (key, value) VALUES ('schemaVersion', '{DevbookDatabaseSchema.Version}')");
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

    private static string WriterSchema() => DevbookDatabaseSchema.Ddl;

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
        public IRenderedComponent<DevbookPane> Render() =>
            Context.Render<DevbookPane>(parameters => parameters.Add(pane => pane.RepositoryAlias, RepositoryAlias));

        public async ValueTask DisposeAsync() => await Context.DisposeAsync();
    }
}
