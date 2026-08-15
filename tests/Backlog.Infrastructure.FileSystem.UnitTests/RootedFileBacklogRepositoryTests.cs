using Backlog.Modules.Backlog.Abstractions;
using Backlog.Modules.Backlog.DomainModels;
using Xunit;

namespace Backlog.Infrastructure.FileSystem.UnitTests;

/// <summary>
/// Somebody can point the app at a different backlog folder while it is open.
/// The handlers behind that hold one repository for the life of the process, so
/// it is this adapter's job to notice the move.
/// </summary>
public sealed class RootedFileBacklogRepositoryTests : IDisposable
{
    private readonly List<string> _directories = [];
    private string _root;

    public RootedFileBacklogRepositoryTests()
    {
        _root = NewDirectory();
    }

    public void Dispose()
    {
        foreach (var directory in _directories.Where(Directory.Exists))
        {
            try { Directory.Delete(directory, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task Entries_written_before_a_move_stay_in_the_old_folder()
    {
        var repository = new RootedFileBacklogRepository(() => _root);
        await repository.SaveAsync(new BacklogEntry("Buy milk", string.Empty, EntryType.Task));

        Assert.Single(await repository.ListAsync());

        _root = NewDirectory();

        Assert.Empty(await repository.ListAsync());
    }

    [Fact]
    public async Task Moving_back_finds_the_entries_again()
    {
        var repository = new RootedFileBacklogRepository(() => _root);
        var entry = new BacklogEntry("Buy milk", string.Empty, EntryType.Task);
        await repository.SaveAsync(entry);

        var original = _root;
        _root = NewDirectory();
        Assert.Empty(await repository.ListAsync());

        _root = original;

        var found = await repository.GetAsync(entry.Id);
        Assert.NotNull(found);
        Assert.Equal("Buy milk", found!.Title);
    }

    [Fact]
    public void The_current_root_is_whatever_the_store_last_said()
    {
        var repository = new RootedFileBacklogRepository(() => _root);
        Assert.Equal(_root, repository.RootDirectory);

        _root = NewDirectory();

        Assert.Equal(_root, repository.RootDirectory);
    }

    private string NewDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "backlog-rooted-" + Guid.NewGuid().ToString("N"));
        FileBacklogRepository.EnsureStorageFolders(directory);
        _directories.Add(directory);
        return directory;
    }
}
