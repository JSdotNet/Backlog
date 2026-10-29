using System.Globalization;

using Backlog.Modules.Devbook.Abstractions;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Time.Testing;

namespace Backlog.Infrastructure.Devbook.UnitTests;

/// <summary>
/// The Brain's database (local ADR 0026 §3–§4): built under the Brain's own
/// convention, into the app's storage keyed by the folder's absolute path, and
/// kept current by the refresher watching the folder.
/// </summary>
[Collection(SqlitePoolClearingCollection.Name)]
public class BrainDatabaseTests : IDisposable
{
    private readonly string _scratch = Path.Combine(Path.GetTempPath(), "backlog-brain-database", Guid.NewGuid().ToString("N"));

    private string Brain => Path.Combine(_scratch, "OneDrive", "Brain");

    private string Cache => Path.Combine(_scratch, "devbook-cache");

    private string Target => DevbookDatabaseLocation.PathFor(Path.Combine(Cache, DevbookDatabaseLocation.DatabasesFolderName), Brain)!;

    public BrainDatabaseTests()
    {
        Write("README.md", "# My Brain\n\n```meta\nindex: root\n```\n");
        Write("CONVENTIONS.md", "# Conventions\n\n## Areas\n\n| Area | Folder |\n|---|---|\n| Work | work |\n| Design | design |\n| Projects | projects |\n");
        Write("work/standup.md", "# Standup\n\nNotes.\n");
        Write("design/palette.md", "# Palette\n\n```meta\ntype: guideline\n```\n");
        Write("projects/plan.md", """
            # Plan

            ```meta
            repos: [backlog]
            tasks: [t-1, t-2]
            related: [repo:backlog/.devbook/arc42/05-building-block-view.md#devbook, work/standup.md, work/missing.md]
            ```

            See the [building blocks](repo:backlog/.devbook/arc42/05-building-block-view.md).

            ## Step

            ```meta
            para: work
            ```

            Twice: [one](repo:other/.devbook/domain/x.md) and [two](repo:other/.devbook/domain/x.md).

            ```markdown
            [fenced](repo:fenced/nothing.md)
            ```
            """);
        Write("archive/old.md", "# Old\n");
    }

    [Fact]
    public void The_areas_are_the_scopes_declared_first_then_discovered()
    {
        Assert.True(DevbookDatabaseBuilder.Build(Brain, Target, DevbookCorpusKind.Brain, TestContext.Current.CancellationToken));

        using var database = DevbookDatabase.TryOpen(Target);
        Assert.NotNull(database);
        Assert.Equal(".,work,design,projects,archive", database.Meta[DevbookDatabaseSchema.ScopesKey]);

        using var connection = Open();
        Assert.Equal(["work", "design", "projects", "archive"], Strings(connection, "SELECT name FROM outline_entry WHERE scope = '.' AND parent_id IS NULL ORDER BY ordinal"));
        Assert.Equal("archive", Strings(connection, "SELECT folder FROM node WHERE id = 'archive/old.md'").Single());
        Assert.Empty(Strings(connection, "SELECT path FROM chapter WHERE path = 'README.md' OR path = 'CONVENTIONS.md'"));
    }

    /// <summary>An ignored folder's notes are not in the database, so search and
    /// the atlas leave them out — and neither is a dot-folder's.</summary>
    [Fact]
    public void An_ignored_folder_and_a_dot_folder_are_not_indexed()
    {
        File.AppendAllText(Path.Combine(Brain, BrainConventions.FileName), "\n## Ignore\n\n- `_templates`\n- `projects`\n");
        Write("_templates/meeting.md", "# Meeting template\n");
        Write(".obsidian/workspace.md", "# Workspace\n");

        Assert.True(DevbookDatabaseBuilder.Build(Brain, Target, DevbookCorpusKind.Brain, TestContext.Current.CancellationToken));

        using var connection = Open();
        Assert.Equal(["work", "design", "archive"], Strings(connection, "SELECT name FROM outline_entry WHERE scope = '.' AND parent_id IS NULL ORDER BY ordinal"));
        Assert.Empty(Strings(connection, "SELECT id FROM node WHERE id LIKE '_templates/%' OR id LIKE '.obsidian/%' OR id LIKE 'projects/%'"));
    }

    [Fact]
    public void A_note_in_a_new_top_folder_makes_the_database_stale()
    {
        Directory.CreateDirectory(Path.Combine(Brain, "Meetings"));
        DevbookDatabaseBuilder.Build(Brain, Target, DevbookCorpusKind.Brain, TestContext.Current.CancellationToken);
        Assert.True(DevbookDatabaseBuilder.IsCurrent(Brain, Target, DevbookCorpusKind.Brain));

        Write("Meetings/standup.md", "# Standup\n");

        Assert.False(DevbookDatabaseBuilder.IsCurrent(Brain, Target, DevbookCorpusKind.Brain));
        DevbookDatabaseBuilder.Build(Brain, Target, DevbookCorpusKind.Brain, TestContext.Current.CancellationToken);

        using var connection = Open();
        Assert.Equal("Meetings", Strings(connection, "SELECT folder FROM node WHERE id = 'Meetings/standup.md'").Single());
    }

    [Fact]
    public void A_note_s_folder_is_its_area_and_no_devbook_convention_applies()
    {
        DevbookDatabaseBuilder.Build(Brain, Target, DevbookCorpusKind.Brain, TestContext.Current.CancellationToken);

        using var connection = Open();
        Assert.Equal("projects", Strings(connection, "SELECT folder FROM node WHERE id = 'projects/plan.md'").Single());
        Assert.Equal("projects", Strings(connection, "SELECT folder FROM chapter WHERE path = 'projects/plan.md' AND slug = 'step'").Single());

        // An area called design is not the devbook's design folder: no resting
        // status for its chapters, no README.md root by convention.
        Assert.Equal(["NULL"], Strings(connection, "SELECT coalesce(status, 'NULL') FROM node WHERE id = 'design/palette.md'"));
        Assert.Equal("Palette (guideline)", Strings(connection, "SELECT label FROM node WHERE id = 'design/palette.md'").Single());
    }

    [Fact]
    public void Para_repos_and_tasks_are_node_attributes()
    {
        DevbookDatabaseBuilder.Build(Brain, Target, DevbookCorpusKind.Brain, TestContext.Current.CancellationToken);

        using var connection = Open();
        Assert.Equal(
            [
                "projects/plan.md para projects",
                "projects/plan.md repos backlog",
                "projects/plan.md tasks t-1",
                "projects/plan.md tasks t-2",
                "projects/plan.md#step para work",
                "work/standup.md para work"
            ],
            Strings(connection, "SELECT node_id || ' ' || name || ' ' || value FROM node_attribute WHERE node_id IN ('projects/plan.md', 'projects/plan.md#step', 'work/standup.md') ORDER BY node_id, name, value"));
    }

    [Fact]
    public void Repo_links_are_edges_whose_target_is_no_node()
    {
        DevbookDatabaseBuilder.Build(Brain, Target, DevbookCorpusKind.Brain, TestContext.Current.CancellationToken);

        using var connection = Open();
        Assert.Equal(
            [
                "related projects/plan.md -> repo:backlog/.devbook/arc42/05-building-block-view.md#devbook",
                "related projects/plan.md -> work/standup.md",
                "repo-link projects/plan.md -> repo:backlog/.devbook/arc42/05-building-block-view.md",
                "repo-link projects/plan.md#step -> repo:other/.devbook/domain/x.md"
            ],
            Strings(connection, "SELECT type || ' ' || source || ' -> ' || target FROM edge WHERE type <> 'contains' ORDER BY type, source, target"));

        Assert.Empty(Strings(connection, "SELECT id FROM node WHERE id LIKE 'repo:%' OR id = 'work/missing.md'"));
    }

    [Fact]
    public void Nothing_derived_lands_in_the_brain_folder()
    {
        var before = Directory.GetFiles(Brain, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal).ToArray();

        Assert.True(DevbookDatabaseBuilder.Build(Brain, Target, DevbookCorpusKind.Brain, TestContext.Current.CancellationToken));

        Assert.Equal(before, Directory.GetFiles(Brain, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal));
        Assert.StartsWith(Cache, Target, StringComparison.Ordinal);
    }

    [Fact]
    public void A_brain_with_no_area_folder_has_nothing_to_build()
    {
        var empty = Path.Combine(_scratch, "Empty");
        Directory.CreateDirectory(empty);
        File.WriteAllText(Path.Combine(empty, BrainConventions.FileName), "## Areas\n\n- `work`\n");

        Assert.False(DevbookDatabaseBuilder.HasDevbook(empty, DevbookCorpusKind.Brain));
        Assert.False(DevbookDatabaseBuilder.Build(empty, Path.Combine(_scratch, "empty.db"), DevbookCorpusKind.Brain, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void A_brain_without_conventions_is_read_as_para()
    {
        File.Delete(Path.Combine(Brain, BrainConventions.FileName));

        Assert.True(DevbookDatabaseBuilder.Build(Brain, Target, DevbookCorpusKind.Brain, TestContext.Current.CancellationToken));

        using var connection = Open();
        Assert.Equal(["projects", "archive", "design", "work"], Strings(connection, "SELECT name FROM outline_entry WHERE scope = '.' AND parent_id IS NULL ORDER BY ordinal"));
    }

    [Fact]
    public void Declaring_an_area_makes_the_database_stale()
    {
        DevbookDatabaseBuilder.Build(Brain, Target, DevbookCorpusKind.Brain, TestContext.Current.CancellationToken);
        Assert.True(DevbookDatabaseBuilder.IsCurrent(Brain, Target, DevbookCorpusKind.Brain));

        File.AppendAllText(Path.Combine(Brain, BrainConventions.FileName), "| Archive | archive |\n");

        Assert.False(DevbookDatabaseBuilder.IsCurrent(Brain, Target, DevbookCorpusKind.Brain));
    }

    /// <summary>A OneDrive placeholder whose content is still in the cloud is
    /// indexed by its name and not opened, which would download it; once it is
    /// hydrated the database is stale, and the rebuild reads its meta and text.
    /// The offline attribute stands in for the cloud-files API's recall
    /// attributes, which only a sync provider can set.</summary>
    [Fact]
    public void A_cloud_only_note_is_indexed_by_name_until_it_is_hydrated()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("File attributes for cloud placeholders are a Windows notion.");

        var note = Write("work/remote.md", "# Remote Note\n\n```meta\ntasks: [t-9]\n```\n\nFrom the other machine.\n");
        File.SetAttributes(note, File.GetAttributes(note) | FileAttributes.Offline);

        DevbookDatabaseBuilder.Build(Brain, Target, DevbookCorpusKind.Brain, TestContext.Current.CancellationToken);

        using (var connection = Open())
        {
            Assert.Equal("remote", Strings(connection, "SELECT label FROM node WHERE id = 'work/remote.md'").Single());
            Assert.Equal("remote", Strings(connection, "SELECT title FROM outline_entry WHERE path = 'work/remote.md' AND scope = 'work'").Single());
            Assert.Empty(Strings(connection, "SELECT slug FROM chapter WHERE path = 'work/remote.md'"));
            Assert.Empty(Strings(connection, "SELECT value FROM node_attribute WHERE node_id = 'work/remote.md' AND name = 'tasks'"));
        }

        Assert.True(DevbookDatabaseBuilder.IsCurrent(Brain, Target, DevbookCorpusKind.Brain));
        File.SetAttributes(note, File.GetAttributes(note) & ~FileAttributes.Offline);
        Assert.False(DevbookDatabaseBuilder.IsCurrent(Brain, Target, DevbookCorpusKind.Brain), "hydrating the note is a change");

        SqliteConnection.ClearAllPools();
        DevbookDatabaseBuilder.Build(Brain, Target, DevbookCorpusKind.Brain, TestContext.Current.CancellationToken);

        using var rebuilt = Open();
        Assert.Equal("Remote Note", Strings(rebuilt, "SELECT label FROM node WHERE id = 'work/remote.md'").Single());
        Assert.Equal(["t-9"], Strings(rebuilt, "SELECT value FROM node_attribute WHERE node_id = 'work/remote.md' AND name = 'tasks'"));
        Assert.NotEmpty(Strings(rebuilt, "SELECT slug FROM chapter WHERE path = 'work/remote.md'"));
    }

    [Fact]
    public void The_brain_s_database_is_keyed_by_its_folder_and_found_from_the_folder()
    {
        var brainDatabase = DevbookDatabaseLocation.ForBrainFolder(Brain);
        Assert.NotNull(brainDatabase);

        Assert.True(DevbookDatabaseLocation.IsBrainRoot(Brain));
        Assert.Equal(DevbookCorpusKind.Brain, DevbookDatabaseLocation.KindFor(Brain + Path.DirectorySeparatorChar));
        Assert.Equal(brainDatabase, DevbookDatabaseLocation.ForRepositoryRoot(Brain));

        // The Brain's folder is its own root: the database is not its parent's.
        Assert.Equal(brainDatabase, DevbookDatabaseLocation.ForDevbookFolder(Brain));
        Assert.NotEqual(DevbookDatabaseLocation.ForRepositoryRoot(Path.GetDirectoryName(Brain)), brainDatabase);
        Assert.Equal(DevbookCorpusKind.Repository, DevbookDatabaseLocation.KindFor(Path.GetDirectoryName(Brain)));
    }

    [Fact]
    public async Task The_refresher_builds_a_recorded_brain_under_its_convention()
    {
        using var refresher = new DevbookDatabaseRefresher(() => Cache, quietInterval: TimeSpan.FromMinutes(5), watchFolder: (_, _) => null);
        DevbookDatabaseLocation.RegisterBrainRoot(Brain);

        Assert.True(await refresher.RefreshAsync(Brain, TestContext.Current.CancellationToken));

        Assert.True(DevbookDatabaseBuilder.IsCurrent(Brain, Target, DevbookCorpusKind.Brain));
        using var connection = Open();
        Assert.Equal("projects", Strings(connection, "SELECT folder FROM node WHERE id = 'projects/plan.md'").Single());
    }

    /// <summary>A change inside the quiet interval is not dropped: every change
    /// of a burst collapses into one check at the interval's end.</summary>
    [Fact]
    public async Task Changes_inside_the_quiet_interval_are_checked_once_at_its_end()
    {
        var time = new FakeTimeProvider(DateTimeOffset.Parse("2026-10-13T09:00:00Z", CultureInfo.InvariantCulture));
        Action? changed = null;
        using var refresher = new DevbookDatabaseRefresher(
            () => Cache,
            quietInterval: DevbookDatabaseRefresher.DefaultQuietInterval,
            time: time,
            watchFolder: (_, onChange) =>
            {
                changed = onChange;
                return null;
            });

        refresher.WatchBrain(Brain);
        var first = refresher.Pending(Brain);
        await first.WaitAsync(TimeSpan.FromMinutes(1), TestContext.Current.CancellationToken);
        Assert.True(DevbookDatabaseBuilder.IsCurrent(Brain, Target, DevbookCorpusKind.Brain), "watching checks at once");
        Assert.NotNull(changed);

        Write("work/new.md", "# New\n");
        changed();
        changed();
        Assert.Same(first, refresher.Pending(Brain));

        time.Advance(TimeSpan.FromSeconds(9));
        Assert.Same(first, refresher.Pending(Brain));

        time.Advance(TimeSpan.FromSeconds(1));
        var deferred = refresher.Pending(Brain);
        Assert.NotSame(first, deferred);
        await deferred.WaitAsync(TimeSpan.FromMinutes(1), TestContext.Current.CancellationToken);
        Assert.True(DevbookDatabaseBuilder.IsCurrent(Brain, Target, DevbookCorpusKind.Brain), "the last change of the burst was built");
    }

    [Fact]
    public async Task Watching_the_real_folder_rebuilds_on_a_change()
    {
        using var refresher = new DevbookDatabaseRefresher(() => Cache, quietInterval: TimeSpan.FromMilliseconds(200));
        using var watch = refresher.WatchBrain(Brain);
        await refresher.Pending(Brain).WaitAsync(TimeSpan.FromMinutes(1), TestContext.Current.CancellationToken);

        Write("work/added.md", "# Added\n");

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        while (!DevbookDatabaseBuilder.IsCurrent(Brain, Target, DevbookCorpusKind.Brain) && DateTime.UtcNow < deadline)
        {
            await Task.Delay(100, TestContext.Current.CancellationToken);
            await refresher.Pending(Brain).WaitAsync(TimeSpan.FromMinutes(1), TestContext.Current.CancellationToken);
        }

        Assert.True(DevbookDatabaseBuilder.IsCurrent(Brain, Target, DevbookCorpusKind.Brain));
        using var database = DevbookDatabase.TryOpen(Target);
        Assert.NotEmpty(database!.Chapters("work/added.md"));
    }

    private string Write(string relativePath, string text)
    {
        var file = Path.Combine(Brain, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, text);
        return file;
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Target, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        connection.Open();
        return connection;
    }

    private static List<string> Strings(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        using var reader = command.ExecuteReader();
        var values = new List<string>();
        while (reader.Read()) values.Add(reader.IsDBNull(0) ? "NULL" : Convert.ToString(reader.GetValue(0), CultureInfo.InvariantCulture)!);
        return values;
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();

        try
        {
            foreach (var file in Directory.Exists(_scratch) ? Directory.GetFiles(_scratch, "*", SearchOption.AllDirectories) : [])
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }

            if (Directory.Exists(_scratch)) Directory.Delete(_scratch, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A temp folder that outlives the run is not a test failure.
        }
    }
}
