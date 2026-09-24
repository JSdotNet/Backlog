using System.Text.RegularExpressions;

using Backlog.Desktop.UI.Devbook;
using Backlog.Infrastructure.Devbook;
using Backlog.Modules.Devbook.Abstractions;
using Backlog.UI.Components.Devbook;
using Backlog.UI.Components.Diagrams.C4;

using Microsoft.Data.Sqlite;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// A repository on the devbook layout reads the way one on the root layout does:
/// its generated database is found at <c>.devbook/_meta/devbook.db</c>, the rows
/// the devbook generator writes in <c>.devbook/…</c> spelling are matched against
/// the folders asking for them, and a reference written as
/// <c>.devbook/domain/…</c> goes where <c>.domain/…</c> goes.
/// </summary>
public sealed class DevbookLayoutReadingTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "backlog-devbook-layout-reading-tests",
        Guid.NewGuid().ToString("N"));

    private const string Scope = ".devbook/domain";

    private const string ChapterPath = ".devbook/domain/inbox/domain.md";

    private const string SecondPath = ".devbook/domain/inbox/features.md";

    [Theory]
    [InlineData(".devbook/arc42/05.md", ".arc42/05.md")]
    [InlineData("./.devbook/domain/tasks/features.md#task", ".domain/tasks/features.md#task")]
    [InlineData(".devbook\\tech\\runtime.md", ".tech/runtime.md")]
    [InlineData(".arc42/05.md", ".arc42/05.md")]
    [InlineData(".devbook/config.json", ".devbook/config.json")]
    [InlineData(".devbook/_meta/devbook.db", ".devbook/_meta/devbook.db")]
    public void A_devbook_layout_path_folds_to_the_root_layout_spelling(string path, string expected)
    {
        Assert.Equal(expected, DevbookLayout.ConventionalPath(path));
    }

    [Theory]
    [InlineData(".devbook/domain/inbox/domain.md", "inbox/domain.md")]
    [InlineData(".domain/inbox/domain.md", "inbox/domain.md")]
    [InlineData(".devbook/arc42/05.md", "05.md")]
    public void A_path_within_its_folder_drops_either_layouts_prefix(string path, string expected)
    {
        Assert.Equal(expected, DevbookLayout.WithinFolder(path));
    }

    [Theory]
    [InlineData(".devbook/tech/runtime.md", ".tech", true)]
    [InlineData(".tech/runtime.md", ".devbook/tech", true)]
    [InlineData(".devbook/tech", ".tech", true)]
    [InlineData(".devbook/tech-old/runtime.md", ".tech", false)]
    [InlineData(".devbook/arc42/05.md", ".tech", false)]
    public void Folder_membership_holds_across_layouts(string path, string folder, bool expected)
    {
        Assert.Equal(expected, DevbookLayout.IsInFolder(path, folder));
    }

    [Fact]
    public void A_devbook_only_holding_configuration_is_not_the_devbook_layout()
    {
        Directory.CreateDirectory(Path.Combine(_root, ".devbook"));
        File.WriteAllText(Path.Combine(_root, ".devbook", "config.json"), "{}");

        Assert.False(DevbookLayout.UsesDevbookLayout(_root));

        Directory.CreateDirectory(Path.Combine(_root, ".devbook", "tech"));

        Assert.True(DevbookLayout.UsesDevbookLayout(_root));
    }

    [Fact]
    public void The_database_of_a_devbook_layout_repository_is_under_devbook_meta()
    {
        var domain = Path.Combine(_root, ".devbook", "domain");
        Directory.CreateDirectory(domain);
        var database = CreateEmptyFile(".devbook", "_meta", DevbookDatabaseLocation.FileName);

        Assert.Equal(database, DevbookDatabaseLocation.ForRepositoryRoot(_root));
        Assert.Equal(database, DevbookDatabaseLocation.ForDevbookFolder(domain));
    }

    [Fact]
    public void The_layouts_own_database_wins_over_the_other_layouts()
    {
        Directory.CreateDirectory(Path.Combine(_root, ".devbook", "domain"));
        var rootDatabase = CreateEmptyFile("_meta", DevbookDatabaseLocation.FileName);
        var devbookDatabase = CreateEmptyFile(".devbook", "_meta", DevbookDatabaseLocation.FileName);

        Assert.Equal(devbookDatabase, DevbookDatabaseLocation.ForRepositoryRoot(_root));
        Assert.NotEqual(rootDatabase, DevbookDatabaseLocation.ForRepositoryRoot(_root));
    }

    [Fact]
    public void A_root_layout_repository_keeps_its_database_at_the_root()
    {
        Directory.CreateDirectory(Path.Combine(_root, ".domain"));
        Directory.CreateDirectory(Path.Combine(_root, ".devbook"));
        var rootDatabase = CreateEmptyFile("_meta", DevbookDatabaseLocation.FileName);
        CreateEmptyFile(".devbook", "_meta", DevbookDatabaseLocation.FileName);

        Assert.Equal(rootDatabase, DevbookDatabaseLocation.ForRepositoryRoot(_root));
        Assert.Equal(rootDatabase, DevbookDatabaseLocation.ForDevbookFolder(Path.Combine(_root, ".domain")));
    }

    [Fact]
    public void An_absent_database_names_the_layouts_own_location()
    {
        Directory.CreateDirectory(Path.Combine(_root, ".devbook", "arc42"));

        Assert.Equal(
            Path.Combine(_root, ".devbook", "_meta", DevbookDatabaseLocation.FileName),
            DevbookDatabaseLocation.ForRepositoryRoot(_root));
    }

    [Fact]
    public void The_outline_of_a_devbook_layout_folder_comes_out_of_its_database()
    {
        var folder = Arrange();

        var index = DevbookIndexDocument.TryRead(folder);

        Assert.NotNull(index);
        Assert.Equal([ChapterPath, SecondPath], index.Files.Select(entry => entry.Path));
        Assert.All(index.Files, entry => Assert.True(index.Exists(entry)));
        Assert.All(index.Files, entry => Assert.False(index.IsStale(entry)));
        Assert.Equal(Path.Combine(folder, "inbox", "domain.md"), index.FullPath(index.Files.First()));
    }

    [Fact]
    public void An_absent_database_still_leaves_the_folder_readable_by_scan()
    {
        var folder = Arrange(withDatabase: false);

        // ADR 0004's last rung: nothing indexed, so the panels scan the folder.
        Assert.Null(DevbookIndexDocument.TryRead(folder));
        Assert.True(File.Exists(Path.Combine(folder, "inbox", "domain.md")));
    }

    [Fact]
    public void A_scope_keeps_the_nodes_the_devbook_generator_wrote()
    {
        DevbookNodeRow Node(string path) =>
            new(path, "file", path, "tech", path, null, null, null, "adopted", false, null, null, null, null);

        var projection = DevbookScopeProjection.Project(
            ".tech",
            [Node(".devbook/tech/runtime.md"), Node(".devbook/arc42/05.md")],
            []);

        var kept = Assert.Single(projection.Nodes);
        Assert.Equal(".devbook/tech/runtime.md", kept.Id);
    }

    [Theory]
    [InlineData(".devbook/domain/tasks/features.md#task", "domain", "tasks/features.md")]
    [InlineData(".devbook/arc42/adr/0004.md", "arc42", "adr/0004.md")]
    [InlineData(".domain/tasks/features.md", "domain", "tasks/features.md")]
    public void A_reference_in_either_layout_is_a_link_into_its_section(string reference, string area, string relative)
    {
        var link = DevbookChapterLink.From(reference);

        Assert.NotNull(link);
        Assert.Equal(area, link.AreaKey);
        Assert.Equal(relative, link.RelativePath);
    }

    [Fact]
    public void A_relative_link_between_devbook_folders_resolves_from_a_root_spelled_document()
    {
        // The domain store names its documents .domain/…; on disk the file is
        // .devbook/domain/tasks/domain.md, where a neighbouring folder is ../../arc42.
        var reference = DevbookReference.ParseDevbookPath("../../arc42/05-building-block-view.md#tasks", ".domain/tasks/domain.md");

        Assert.NotNull(reference);
        Assert.Equal(".devbook/arc42/05-building-block-view.md", reference.Path);
        Assert.Equal(DevbookFolder.Arc42, DevbookFolders.FromPath(reference.Path));
    }

    [Fact]
    public void A_relative_link_in_the_root_layout_resolves_as_before()
    {
        var reference = DevbookReference.ParseDevbookPath("../../.arc42/05-building-block-view.md", ".domain/tasks/domain.md");

        Assert.NotNull(reference);
        Assert.Equal(".arc42/05-building-block-view.md", reference.Path);
    }

    [Theory]
    [InlineData(".arc42/05-building-block-view.md#tasks", ".devbook/arc42/05-building-block-view.md")]
    [InlineData(".devbook/arc42/05-building-block-view.md", ".arc42/05-building-block-view.md")]
    public void A_c4_view_documents_a_chapter_whichever_layout_names_either(string documented, string chapter)
    {
        var view = new C4ViewEntry(".arc42/_c4/workspace.dsl#containers", "workspace.dsl", "Backlog", "containers", default(C4ViewKind), "Containers", string.Empty, [documented]);

        Assert.True(view.Documented(chapter));
    }

    [Fact]
    public void A_devbook_layout_atlas_groups_by_the_directory_under_its_folder()
    {
        static object Node(string id, string label, string type, string path) =>
            new { data = new { id, label, folder = "arc42", type, status = "active", path, outOfScope = false } };

        var json = System.Text.Json.JsonSerializer.Serialize(new
        {
            elements = new
            {
                nodes = new object[]
                {
                    Node(".devbook/arc42/adr/0001-x.md", "ADR 1", "file", ".devbook/arc42/adr/0001-x.md"),
                    Node(".devbook/arc42/tdr/0001-y.md", "TDR 1", "file", ".devbook/arc42/tdr/0001-y.md"),
                    Node(".devbook/arc42/05-building-block-view.md", "Building Block View", "file", ".devbook/arc42/05-building-block-view.md")
                },
                edges = Array.Empty<object>()
            }
        });

        var graph = DevbookAtlasReader.Read(new DevbookAtlasScope("arc42", "Architecture", ".arc42"), json);

        Assert.Equal("Adr", graph.Nodes.Single(node => node.Id == ".devbook/arc42/adr/0001-x.md").Group);
        Assert.Equal("Tdr", graph.Nodes.Single(node => node.Id == ".devbook/arc42/tdr/0001-y.md").Group);
        Assert.Equal("Building Block View", graph.Nodes.Single(node => node.Id == ".devbook/arc42/05-building-block-view.md").Group);
    }

    [Fact]
    public void A_status_change_in_the_ai_folder_lands_in_the_devbook_layout_file()
    {
        // The AI and Design views name their documents .ai/… whichever layout the
        // folder is in; the write resolves that against the folder it was read from.
        var folder = Path.Combine(_root, ".devbook", "ai");
        Directory.CreateDirectory(folder);
        var file = Path.Combine(folder, "01-author.md");
        File.WriteAllText(file, "# Author\n\n```meta\nstatus: trial\n```\n\nWriting with AI.\n");

        DevbookMarkdownStatusWriter.UpdateStatus(folder, DocumentDevbookFolder.Ai.DocumentPath("01-author.md"), DocumentDevbookFolder.Ai.PathPrefix, "adopted");

        Assert.Contains("status: adopted", File.ReadAllText(file));
        Assert.False(Directory.Exists(Path.Combine(_root, ".ai")));
    }

    private string Arrange(bool withDatabase = true)
    {
        var folder = Path.Combine(_root, ".devbook", "domain");
        Directory.CreateDirectory(Path.Combine(folder, "inbox"));

        Write(ChapterPath, "# Inbox\n\nQuick capture never blocks on a decision.\n");
        Write(SecondPath, "# Features\n\nWhat the context does.\n");

        if (!withDatabase) return folder;

        var databasePath = Path.Combine(_root, ".devbook", "_meta", DevbookDatabaseLocation.FileName);
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);

        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate
        }.ToString());

        connection.Open();
        Execute(connection, WriterSchema());
        Execute(connection, $"INSERT INTO meta (key, value) VALUES ('schemaVersion', '{DevbookDatabaseSchema.Version}')");
        Execute(connection, "INSERT INTO meta (key, value) VALUES ('generatedAt', '2026-09-25T01:23:45.000Z')");

        Execute(connection, $"""
            INSERT INTO outline_entry (scope, parent_id, ordinal, type, name, path, title, status, kind, is_root)
            VALUES ('{Scope}', NULL, 0, 'directory', 'inbox', '.devbook/domain/inbox', 'Inbox', NULL, 'domain', 0)
            """);

        Execute(connection, $"""
            INSERT INTO outline_entry (scope, parent_id, ordinal, type, name, path, title, status, kind, is_root)
            VALUES ('{Scope}', 1, 0, 'file', 'domain.md', '{ChapterPath}', 'Inbox', 'active', 'domain', 1),
                   ('{Scope}', 1, 1, 'file', 'features.md', '{SecondPath}', 'Features', 'draft', 'domain', 0)
            """);

        foreach (var path in new[] { ChapterPath, SecondPath })
        {
            var file = new FileInfo(Resolve(path));
            var hash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(file.FullName)));
            var mtime = (long)Math.Round(
                (double)(new DateTimeOffset(file.LastWriteTimeUtc).UtcTicks - DateTimeOffset.UnixEpoch.UtcTicks) / TimeSpan.TicksPerMillisecond,
                MidpointRounding.AwayFromZero);

            Execute(connection, $"""
                INSERT INTO chapter (path, folder, slug, level, title, status, line, text, search_text, content_hash, source_hash, size, mtime)
                VALUES ('{path}', 'domain', 'slug', 1, 'Title', 'active', 1, 'text', 'text', 'aa', '{hash}', {file.Length}, {mtime})
                """);
        }

        SqliteConnection.ClearPool(connection);
        connection.Close();

        return folder;
    }

    private string CreateEmptyFile(params string[] segments)
    {
        var path = Path.Combine([_root, .. segments]);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, string.Empty);
        return path;
    }

    private void Write(string relativePath, string markdown) => File.WriteAllText(Resolve(relativePath), markdown);

    private string Resolve(string relativePath) =>
        Path.Combine(_root, relativePath.Replace('/', Path.DirectorySeparatorChar));

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    /// <summary>The writer's DDL, read out of <c>tools/devbook/devbook-schema.mjs</c>
    /// rather than restated here.</summary>
    private static string WriterSchema()
    {
        var source = File.ReadAllText(RepositoryRoot.File("tools", "devbook", "devbook-schema.mjs"));
        var match = Regex.Match(source, @"export const DEVBOOK_SCHEMA = `(?<value>[^`]*)`", RegexOptions.Singleline);

        Assert.True(match.Success, "tools/devbook/devbook-schema.mjs no longer exports DEVBOOK_SCHEMA.");
        return match.Groups["value"].Value;
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
}
