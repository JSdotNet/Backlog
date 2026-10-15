using Backlog.Infrastructure.FileSystem;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// Show archived kept on this device (local ADR 0029, §3; scenario "A restart" of
/// "Show archived is remembered on this device"): the shell navigation file holds it
/// under <c>showArchivedRepositories</c>, written only while on, and the shared-kernel
/// port reads it from there and says when it moves. The key is a stored name.
/// </summary>
public sealed class ShowArchivedRepositoriesTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "backlog-show-archived-tests", Guid.NewGuid().ToString("n"));

    private string ShellPath => Path.Combine(_root, "shell-navigation.json");

    [Fact]
    public void Show_archived_is_off_until_turned_on_and_survives_a_restart()
    {
        var store = new ShellNavigationStore(ShellPath);
        Assert.False(store.ShowArchivedRepositories);

        store.SetShowArchivedRepositories(true);
        store.Flush();

        Assert.True(new ShellNavigationStore(ShellPath).ShowArchivedRepositories);
        Assert.Contains("\"showArchivedRepositories\": true", File.ReadAllText(ShellPath), StringComparison.Ordinal);
    }

    /// <summary>Off is the default and writes no key, so a file from before the switch
    /// keeps its shape and an older build reads a newer file.</summary>
    [Fact]
    public void Off_writes_no_key()
    {
        var store = new ShellNavigationStore(ShellPath);
        store.SetShowArchivedRepositories(true);

        store.SetShowArchivedRepositories(false);
        store.Flush();

        Assert.DoesNotContain("showArchivedRepositories", File.ReadAllText(ShellPath), StringComparison.Ordinal);
        Assert.False(new ShellNavigationStore(ShellPath).ShowArchivedRepositories);
    }

    /// <summary>The port reads the file's answer and writes through to it, so a shell
    /// built afresh reads what was left.</summary>
    [Fact]
    public void The_port_reads_and_writes_the_devices_file()
    {
        var store = new ShellNavigationStore(ShellPath);
        using var port = new ShowArchivedRepositories(store);

        port.SetShown(true);

        Assert.True(port.Shown);
        Assert.True(store.ShowArchivedRepositories);
        Assert.True(new ShellNavigationStore(ShellPath).ShowArchivedRepositories);
    }

    /// <summary>The store saves on every scope press and view change; the port says
    /// only when show archived itself moved, so nothing redraws for those.</summary>
    [Fact]
    public void The_port_announces_only_a_change_of_its_own_choice()
    {
        var store = new ShellNavigationStore(ShellPath);
        using var port = new ShowArchivedRepositories(store);
        var announced = 0;
        port.Changed += () => announced++;

        store.SetRepositoryScope(["backlog"]);
        store.SetLastView("Board");
        Assert.Equal(0, announced);

        port.SetShown(true);
        port.SetShown(true);
        Assert.Equal(1, announced);

        port.SetShown(false);
        Assert.Equal(2, announced);
    }

    /// <summary>A host with no navigation file still answers: the choice is held for the
    /// life of the port, off to begin with.</summary>
    [Fact]
    public void Without_a_store_the_choice_is_held_in_memory()
    {
        using var port = new ShowArchivedRepositories(store: null);
        var announced = 0;
        port.Changed += () => announced++;

        Assert.False(port.Shown);

        port.SetShown(true);

        Assert.True(port.Shown);
        Assert.Equal(1, announced);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
