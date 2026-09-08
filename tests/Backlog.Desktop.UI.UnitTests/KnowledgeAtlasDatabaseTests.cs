using System.Text.RegularExpressions;

using Backlog.Infrastructure.Knowledge;

using Microsoft.Data.Sqlite;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The atlas out of the generated database.
///
/// <para>The graph is stored once, unprojected, so the scope that used to be a
/// file is applied while reading. Two things are worth holding here and neither is
/// visible from the map itself: that a folder scope keeps the boundary nodes its
/// outbound references reach, exactly as the scoped <c>graph.json</c> did, and that
/// a repository with no database still says the index has not been generated rather
/// than drawing an empty map. That message is the one rung of ADR 0004's ladder a
/// reader is meant to see, and slice 4's search-unavailable state is modelled on
/// it.</para>
/// </summary>
public sealed class KnowledgeAtlasDatabaseTests : IDisposable
{
    private static readonly KnowledgeAtlasScope DomainScope = new("domain", "Domain", ".domain");

    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "backlog-atlas-database-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task A_folder_scope_draws_from_the_database_and_keeps_its_boundary_nodes()
    {
        var source = Arrange(withDatabase: true);
        var service = new KnowledgeAtlasService(source);

        var graph = await service.ReadAsync(DomainScope, null, TestContext.Current.CancellationToken);

        Assert.True(graph.Available);
        Assert.Null(graph.Message);

        // The file and its two chapters, plus the .tech chapter one of them depends
        // on, which comes in as a stub rather than as a member of the scope.
        Assert.Equal(4, graph.Nodes.Count);

        var boundary = Assert.Single(graph.Nodes, node => node.OutOfScope);
        Assert.Equal(".tech/shared.md#net", boundary.Id);
        Assert.Equal("Technology", boundary.Folder);

        var edge = Assert.Single(graph.Edges);
        Assert.Equal(".domain/inbox/domain.md#inbox", edge.Source);
        Assert.Equal(".tech/shared.md#net", edge.Target);
    }

    [Fact]
    public async Task The_whole_repository_scope_keeps_every_folder()
    {
        var source = Arrange(withDatabase: true);
        var service = new KnowledgeAtlasService(source);

        var graph = await service.ReadAsync(KnowledgeAtlasScope.All, null, TestContext.Current.CancellationToken);

        Assert.True(graph.Available);
        Assert.Equal(4, graph.Nodes.Count);
        Assert.DoesNotContain(graph.Nodes, node => node.OutOfScope);
        Assert.Equal(["Domain", "Technology"], graph.Nodes.Select(node => node.Folder).Distinct().Order());
    }

    /// <summary>
    /// The atlas is the one knowledge consumer that does not fall back to Markdown,
    /// and that is correct: a map of the references between chapters cannot be
    /// drawn from the handful of files on screen. So it says what is missing.
    /// </summary>
    [Fact]
    public async Task With_no_database_and_no_committed_graph_the_atlas_says_the_index_is_not_generated()
    {
        var source = Arrange(withDatabase: false);
        var service = new KnowledgeAtlasService(source);

        var graph = await service.ReadAsync(DomainScope, null, TestContext.Current.CancellationToken);

        Assert.False(graph.Available);
        Assert.Empty(graph.Nodes);
        Assert.Contains("has not been written yet", graph.Message);
    }

    private IKnowledgeFolderSource Arrange(bool withDatabase)
    {
        Directory.CreateDirectory(Path.Combine(_root, ".domain"));
        Directory.CreateDirectory(Path.Combine(_root, ".tech"));

        if (withDatabase) WriteDatabase();

        return new FixedKnowledgeFolderSource(_root);
    }

    private void WriteDatabase()
    {
        var databasePath = Path.Combine(_root, "_meta", "knowledge.db");
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);

        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate
        }.ToString());

        connection.Open();
        Execute(connection, "PRAGMA journal_mode = WAL");

        // Read from tools/knowledge/knowledge-schema.mjs rather than restated here,
        // for the reason that file's own header gives.
        var source = File.ReadAllText(RepositoryRoot.File("tools", "knowledge", "knowledge-schema.mjs"));
        var ddl = Regex.Match(source, @"export const KNOWLEDGE_SCHEMA = `(?<value>[^`]*)`", RegexOptions.Singleline);
        Assert.True(ddl.Success, "tools/knowledge/knowledge-schema.mjs no longer exports KNOWLEDGE_SCHEMA.");

        Execute(connection, ddl.Groups["value"].Value);
        Execute(
            connection,
            $"INSERT INTO meta (key, value) VALUES ('schemaVersion', '{KnowledgeDatabaseSchema.Version}')");

        Execute(connection, """
            INSERT INTO node (id, type, label, folder, path, slug, level, line, status, out_of_scope, effort, kind, version, issue)
            VALUES ('.domain/inbox/domain.md', 'file', 'domain.md', 'domain', '.domain/inbox/domain.md',
                    NULL, NULL, NULL, NULL, 0, NULL, NULL, NULL, NULL),
                   ('.domain/inbox/domain.md#inbox', 'chapter', 'Inbox', 'domain', '.domain/inbox/domain.md',
                    'inbox', 2, 7, 'active', 0, NULL, NULL, NULL, NULL),
                   ('.domain/inbox/domain.md#capture', 'chapter', 'Capture', 'domain', '.domain/inbox/domain.md',
                    'capture', 2, 21, 'draft', 0, NULL, NULL, NULL, NULL),
                   ('.tech/shared.md#net', 'chapter', '.NET', 'tech', '.tech/shared.md',
                    'net', 2, 3, 'adopted', 0, NULL, NULL, NULL, NULL)
            """);

        Execute(connection, """
            INSERT INTO edge (id, type, source, target)
            VALUES ('contains-1', 'contains', '.domain/inbox/domain.md', '.domain/inbox/domain.md#inbox'),
                   ('contains-2', 'contains', '.domain/inbox/domain.md', '.domain/inbox/domain.md#capture'),
                   ('depends-1', 'depends-on', '.domain/inbox/domain.md#inbox', '.tech/shared.md#net')
            """);

        SqliteConnection.ClearPool(connection);
        connection.Close();
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    public void Dispose()
    {
        try
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A temp folder that outlives the run is not a test failure.
        }
    }

    /// <summary>The knowledge folders of one repository on disk, with nothing else
    /// the port can do — no settings file, no clone, no branch snapshot.</summary>
    private sealed class FixedKnowledgeFolderSource(string root) : IKnowledgeFolderSource
    {
        public event Action? Changed
        {
            add { }
            remove { }
        }

        public string StorageDirectory => root;

        public IReadOnlyList<KnowledgeFolderSetting> Folders(string? repositoryAlias) =>
            [new(".domain", "Domain", ".domain"), new(".tech", "Technology", ".tech")];

        public KnowledgeFolderLocation Resolve(string key, string? repositoryAlias = null)
        {
            var setting = Folders(repositoryAlias).FirstOrDefault(folder => folder.Key == key);

            return setting is null
                ? KnowledgeFolderLocation.Unavailable(key, "Not configured.")
                : new KnowledgeFolderLocation(
                    key,
                    true,
                    null,
                    "JSdotNet/Backlog",
                    setting,
                    Path.Combine(root, setting.DefaultRelativePath),
                    root);
        }

        public void NotifyContentChanged()
        {
        }
    }
}
