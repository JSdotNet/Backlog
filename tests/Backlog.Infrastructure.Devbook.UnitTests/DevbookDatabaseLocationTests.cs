namespace Backlog.Infrastructure.Devbook.UnitTests;

/// <summary>
/// Where a repository's database is (local ADR 0015): under the app's devbook
/// cache folder, keyed by the repository's path, never inside the repository.
/// </summary>
public class DevbookDatabaseLocationTests
{
    private static readonly string Databases = Path.Combine(Path.GetTempPath(), "backlog-location-tests", "_databases");

    [Fact]
    public void Two_worktrees_of_one_repository_get_two_databases()
    {
        var main = Path.Combine(Path.GetTempPath(), "Repos", "Backlog");
        var worktree = Path.Combine(main, ".claude", "worktrees", "feature-a");
        var sibling = Path.Combine(main, ".claude", "worktrees", "feature-b");

        var paths = new[] { main, worktree, sibling }.Select(root => DevbookDatabaseLocation.PathFor(Databases, root)).ToArray();

        Assert.Equal(3, paths.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void Two_clones_with_the_same_folder_name_get_two_databases()
    {
        var first = DevbookDatabaseLocation.KeyFor(Path.Combine(Path.GetTempPath(), "one", "Backlog"));
        var second = DevbookDatabaseLocation.KeyFor(Path.Combine(Path.GetTempPath(), "two", "Backlog"));

        Assert.NotEqual(first, second);
        Assert.StartsWith("backlog-", first, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith("backlog-", second, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void One_repository_spelled_two_ways_is_one_database()
    {
        var root = Path.Combine(Path.GetTempPath(), "Repos", "Backlog");

        Assert.Equal(
            DevbookDatabaseLocation.PathFor(Databases, root),
            DevbookDatabaseLocation.PathFor(Databases, root + Path.DirectorySeparatorChar));

        if (OperatingSystem.IsWindows())
        {
            Assert.Equal(DevbookDatabaseLocation.KeyFor(root), DevbookDatabaseLocation.KeyFor(root.ToUpperInvariant()));
        }
    }

    [Fact]
    public void The_key_is_the_folder_name_and_twelve_hex_characters()
    {
        var key = DevbookDatabaseLocation.KeyFor(Path.Combine(Path.GetTempPath(), "my repo (copy)"));

        Assert.NotNull(key);
        Assert.Matches("^(?i:my-repo--copy)-[0-9a-f]{12}$", key);
    }

    [Fact]
    public void The_database_sits_under_the_databases_folder_and_outside_the_repository()
    {
        var root = Path.Combine(Path.GetTempPath(), "Repos", "Backlog");

        var path = DevbookDatabaseLocation.PathFor(Databases, root)!;

        Assert.StartsWith(Databases, path, StringComparison.Ordinal);
        Assert.Equal(DevbookDatabaseLocation.FileName, Path.GetFileName(path));
        Assert.False(path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void A_devbook_folder_resolves_to_its_repositorys_database_in_either_layout()
    {
        var root = Path.Combine(Path.GetTempPath(), "backlog-location-tests", Guid.NewGuid().ToString("N"));

        var expected = DevbookDatabaseLocation.ForRepositoryRoot(root);

        Assert.Equal(expected, DevbookDatabaseLocation.ForDevbookFolder(Path.Combine(root, ".devbook", "domain")));
        Assert.Equal(expected, DevbookDatabaseLocation.ForDevbookFolder(Path.Combine(root, ".arc42")));
        Assert.StartsWith(TestDevbookStorage.CacheDirectory, expected, StringComparison.Ordinal);
    }

    /// <summary>
    /// A database the Node writer used to leave inside the repository is never
    /// read again: the resolver does not look there, so the reader takes the
    /// Markdown path until the app has built its own.
    /// </summary>
    [Fact]
    public void A_database_left_inside_the_repository_is_ignored()
    {
        using var temporary = new TemporaryDatabase();
        foreach (var legacy in (string[])[Path.Combine("_meta", "devbook.db"), Path.Combine(".devbook", "_meta", "devbook.db"), Path.Combine("_meta", "knowledge.db")])
        {
            var file = temporary.Resolve(legacy);
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            DevbookCorpus.Seed(file, temporary.RootDirectory);
        }

        Assert.Null(DevbookDatabase.TryOpenForFolder(temporary.Folder(Path.Combine(".devbook", "domain"))));
        Assert.Null(DevbookDatabase.TryOpenForFolder(temporary.Folder(".domain")));
    }
}
