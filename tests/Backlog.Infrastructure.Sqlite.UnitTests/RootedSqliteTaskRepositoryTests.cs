using Backlog.Modules.Tasks.Abstractions;
using Backlog.Modules.Tasks.DomainModels;

namespace Backlog.Infrastructure.Sqlite.UnitTests;

/// <summary>
/// Somebody can point the app at a different folder while it is open, so the
/// repository reads the root per call. These assert that it actually follows —
/// and, just as importantly, that it does not carry the old folder's tasks over.
/// </summary>
public sealed class RootedSqliteTaskRepositoryTests : IDisposable
{
    private readonly string _first;
    private readonly string _second;
    private string _root;

    public RootedSqliteTaskRepositoryTests()
    {
        var scope = Path.Combine(Path.GetTempPath(), "backlog-rooted-sqlite", Guid.NewGuid().ToString("n"));
        _first = Path.Combine(scope, "first");
        _second = Path.Combine(scope, "second");
        _root = _first;
    }

    [Fact]
    public async Task It_writes_into_the_root_that_is_current_when_it_is_called()
    {
        var repository = new RootedSqliteTaskRepository(() => _root);

        await repository.SaveAsync(new TaskItem("In the first folder", string.Empty, EntryType.Task));

        Assert.True(File.Exists(Path.Combine(_first, "backlog.db")));
        Assert.False(File.Exists(Path.Combine(_second, "backlog.db")));
    }

    [Fact]
    public async Task Moving_the_root_changes_which_database_is_read()
    {
        var repository = new RootedSqliteTaskRepository(() => _root);
        await repository.SaveAsync(new TaskItem("In the first folder", string.Empty, EntryType.Task));

        _root = _second;

        Assert.Empty(await repository.ListAsync());
        Assert.Equal(Path.Combine(_second, "backlog.db"), repository.DatabasePath);
    }

    [Fact]
    public async Task Moving_back_finds_the_tasks_again()
    {
        var repository = new RootedSqliteTaskRepository(() => _root);
        var task = new TaskItem("Still there", string.Empty, EntryType.Task);
        await repository.SaveAsync(task);

        _root = _second;
        await repository.SaveAsync(new TaskItem("Somewhere else", string.Empty, EntryType.Task));

        _root = _first;

        var only = Assert.Single(await repository.ListAsync());
        Assert.Equal("Still there", only.Title);
        Assert.NotNull(await repository.GetAsync(task.Id));
    }

    [Fact]
    public void It_will_not_be_built_without_a_way_to_read_the_root()
    {
        Assert.Throws<ArgumentNullException>(() => new RootedSqliteTaskRepository(null!));
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        var scope = Path.GetDirectoryName(_first);
        if (scope is null || !Directory.Exists(scope)) return;
        try { Directory.Delete(scope, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
