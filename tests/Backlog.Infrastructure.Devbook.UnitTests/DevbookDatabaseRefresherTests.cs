using Microsoft.Data.Sqlite;

namespace Backlog.Infrastructure.Devbook.UnitTests;

/// <summary>
/// The app keeping a repository's database current by itself (local ADR 0015),
/// and the degrade ladder of local ADR 0004 holding while it does.
///
/// <para>Each test owns its refresher and its storage folder rather than the
/// process-wide one, so nothing here builds into another test's database.</para>
/// </summary>
public class DevbookDatabaseRefresherTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "backlog-refresher", Guid.NewGuid().ToString("N"), "repo");
    private readonly string _cache = Path.Combine(Path.GetTempPath(), "backlog-refresher", Guid.NewGuid().ToString("N"), "devbook-cache");
    private readonly DevbookDatabaseRefresher _refresher;

    public DevbookDatabaseRefresherTests()
    {
        Write(".devbook/domain/inbox/domain.md", "# Inbox\n\n```meta\nstatus: draft\n```\n\n## Inbox Item\n\n```meta\nrelated: [.devbook/domain/inbox/domain.md]\n```\n\nThe item a capture becomes.\n");
        Write(".devbook/arc42/01-introduction.md", "# 01. Introduction\n\nWhy the system exists.\n");
        _refresher = new DevbookDatabaseRefresher(() => _cache, quietInterval: TimeSpan.FromMinutes(5));
    }

    private string Target => DevbookDatabaseLocation.PathFor(Path.Combine(_cache, DevbookDatabaseLocation.DatabasesFolderName), _root)!;

    [Fact]
    public async Task An_absent_database_is_built_outside_the_repository()
    {
        Assert.False(File.Exists(Target));

        Assert.True(await _refresher.RefreshAsync(_root, TestContext.Current.CancellationToken));

        Assert.True(File.Exists(Target));
        Assert.StartsWith(_cache, Target, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(_root, "_meta")), "nothing is written into the repository");
        Assert.False(Directory.Exists(Path.Combine(_root, ".devbook", "_meta")), "nothing is written into .devbook/");
        Assert.False(File.Exists(Target + "-wal"));

        using var database = DevbookDatabase.TryOpen(Target);
        Assert.NotNull(database);
        Assert.Equal(DevbookDatabaseBuilder.GeneratedBy, database.Meta[DevbookDatabaseSchema.GeneratedByKey]);
        Assert.Contains(database.Chapters(".devbook/domain/inbox/domain.md"), chapter => chapter.Slug == "inbox-item");
    }

    [Fact]
    public async Task An_unchanged_repository_is_not_rebuilt()
    {
        await _refresher.RefreshAsync(_root, TestContext.Current.CancellationToken);

        Assert.False(await _refresher.RefreshAsync(_root, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("edit")]
    [InlineData("add")]
    [InlineData("delete")]
    public async Task A_changed_input_is_a_rebuild(string change)
    {
        await _refresher.RefreshAsync(_root, TestContext.Current.CancellationToken);

        switch (change)
        {
            case "edit":
                File.AppendAllText(Absolute(".devbook/arc42/01-introduction.md"), "\nOne more line.\n");
                break;
            case "add":
                Write(".devbook/arc42/02-constraints.md", "# 02. Constraints\n");
                break;
            case "delete":
                File.Delete(Absolute(".devbook/arc42/01-introduction.md"));
                break;
        }

        Assert.False(DevbookDatabaseBuilder.IsCurrent(_root, Target));
        Assert.True(await _refresher.RefreshAsync(_root, TestContext.Current.CancellationToken));
        Assert.True(DevbookDatabaseBuilder.IsCurrent(_root, Target));
    }

    /// <summary>The reading order is derived, not authored (local ADR 0016), so a
    /// <c>_reading-order.json</c> is no input: writing one leaves the database
    /// current and rebuilds nothing.</summary>
    [Fact]
    public async Task A_stray_reading_order_file_is_not_an_input()
    {
        await _refresher.RefreshAsync(_root, TestContext.Current.CancellationToken);

        Write(".devbook/_reading-order.json", "{ \"version\": 1, \"directories\": {} }");
        Write(".devbook/arc42/_reading-order.json", "{ \"version\": 1, \"directories\": {} }");

        Assert.True(DevbookDatabaseBuilder.IsCurrent(_root, Target));
        Assert.False(await _refresher.RefreshAsync(_root, TestContext.Current.CancellationToken));
    }

    /// <summary>Rung three of the ladder, from the writing side: a database in a
    /// schema version this app does not read is ignored by the reader and
    /// replaced by the builder.</summary>
    [Fact]
    public async Task A_database_in_another_schema_version_is_replaced()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Target)!);
        DevbookSchemaSource.Create(Target, DevbookDatabaseSchema.Version + 1);
        Assert.Null(DevbookDatabase.TryOpen(Target));

        Assert.True(await _refresher.RefreshAsync(_root, TestContext.Current.CancellationToken));

        using var database = DevbookDatabase.TryOpen(Target);
        Assert.NotNull(database);
    }

    /// <summary>Rung two holds between builds: an edit the app has not rebuilt
    /// for yet is drifted, so a reader serves that file from its Markdown.</summary>
    [Fact]
    public async Task A_file_edited_after_the_build_is_drifted_until_the_next_one()
    {
        await _refresher.RefreshAsync(_root, TestContext.Current.CancellationToken);
        File.AppendAllText(Absolute(".devbook/arc42/01-introduction.md"), "\nEdited in another editor.\n");

        using (var database = DevbookDatabase.TryOpen(Target))
        {
            Assert.NotNull(database);
            var state = database.FileStates(".devbook/arc42")[".devbook/arc42/01-introduction.md"];
            Assert.True(state.HasDrifted(Absolute(".devbook/arc42/01-introduction.md")));
        }

        await _refresher.RefreshAsync(_root, TestContext.Current.CancellationToken);

        using var rebuilt = DevbookDatabase.TryOpen(Target);
        var current = rebuilt!.FileStates(".devbook/arc42")[".devbook/arc42/01-introduction.md"];
        Assert.False(current.HasDrifted(Absolute(".devbook/arc42/01-introduction.md")));
    }

    [Fact]
    public async Task A_repository_with_no_devbook_gets_no_database()
    {
        var empty = Path.Combine(Path.GetDirectoryName(_root)!, "empty");
        Directory.CreateDirectory(empty);

        Assert.False(await _refresher.RefreshAsync(empty, TestContext.Current.CancellationToken));
        Assert.False(File.Exists(DevbookDatabaseLocation.PathFor(Path.Combine(_cache, DevbookDatabaseLocation.DatabasesFolderName), empty)));
    }

    /// <summary>An ask never waits: it schedules one check and returns, and a
    /// second ask inside the quiet interval schedules nothing.</summary>
    [Fact]
    public async Task A_request_builds_in_the_background_once_per_quiet_interval()
    {
        _refresher.Request(_root);
        var first = _refresher.Pending(_root);
        _refresher.Request(_root);

        Assert.Same(first, _refresher.Pending(_root));
        await first.WaitAsync(TimeSpan.FromMinutes(1), TestContext.Current.CancellationToken);
        Assert.True(File.Exists(Target));

        File.AppendAllText(Absolute(".devbook/arc42/01-introduction.md"), "\nChanged.\n");
        _refresher.Request(_root);

        Assert.Same(first, _refresher.Pending(_root));
        Assert.False(DevbookDatabaseBuilder.IsCurrent(_root, Target), "inside the quiet interval nothing rebuilt");
    }

    /// <summary>Two worktrees of one repository are two databases, each built from
    /// its own files.</summary>
    [Fact]
    public async Task Two_worktrees_are_built_into_two_databases()
    {
        var worktree = Path.Combine(_root, ".claude", "worktrees", "feature");
        Write(Path.Combine(".claude", "worktrees", "feature", ".devbook", "tech", "tooling.md"), "# Tooling\n\n## Git\n\n```meta\nstatus: adopted\n```\n");

        await _refresher.RefreshAsync(_root, TestContext.Current.CancellationToken);
        await _refresher.RefreshAsync(worktree, TestContext.Current.CancellationToken);

        var worktreeTarget = DevbookDatabaseLocation.PathFor(Path.Combine(_cache, DevbookDatabaseLocation.DatabasesFolderName), worktree)!;
        Assert.NotEqual(Target, worktreeTarget);

        using var main = DevbookDatabase.TryOpen(Target);
        using var feature = DevbookDatabase.TryOpen(worktreeTarget);
        Assert.Empty(main!.Chapters(".devbook/tech/tooling.md"));
        Assert.NotEmpty(feature!.Chapters(".devbook/tech/tooling.md"));
    }

    private void Write(string relativePath, string text)
    {
        var file = Absolute(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, text);
    }

    private string Absolute(string relativePath) =>
        Path.Combine(_root, relativePath.Replace('/', Path.DirectorySeparatorChar));

    public void Dispose()
    {
        _refresher.Dispose();
        SqliteConnection.ClearAllPools();

        foreach (var folder in (string[])[Path.GetDirectoryName(_root)!, Path.GetDirectoryName(_cache)!])
        {
            try
            {
                if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // A temp folder that outlives the run is not a test failure.
            }
        }
    }
}
