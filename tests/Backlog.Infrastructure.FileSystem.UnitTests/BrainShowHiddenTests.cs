using Backlog.Infrastructure.FileSystem;

namespace Backlog.Infrastructure.FileSystem.UnitTests;

/// <summary>
/// Show hidden kept on this device (local ADR 0026 §2; "Show hidden brings hidden
/// folders back", scenario "Off by default"): the shell navigation file holds it
/// under <c>brainHiddenShown</c>, written only while on, never in the Brain folder
/// and never through the Brain setting, and the port reads it from there and says
/// when it moves. The key is a stored name.
/// </summary>
public sealed class BrainShowHiddenTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "backlog-brain-show-hidden-tests", Guid.NewGuid().ToString("n"));

    private string ShellPath => Path.Combine(_root, "shell-navigation.json");

    [Fact]
    public void Show_hidden_is_off_until_turned_on_and_survives_a_restart()
    {
        var store = new ShellNavigationStore(ShellPath);
        Assert.False(store.BrainHiddenShown);

        store.SetBrainHiddenShown(true);
        store.Flush();

        Assert.True(new ShellNavigationStore(ShellPath).BrainHiddenShown);
        Assert.Contains("\"brainHiddenShown\": true", File.ReadAllText(ShellPath), StringComparison.Ordinal);
    }

    /// <summary>Off is the default and writes no key, so a file from before the switch
    /// keeps its shape.</summary>
    [Fact]
    public void Off_writes_no_key()
    {
        var store = new ShellNavigationStore(ShellPath);
        store.SetBrainHiddenShown(true);

        store.SetBrainHiddenShown(false);
        store.Flush();

        Assert.DoesNotContain("brainHiddenShown", File.ReadAllText(ShellPath), StringComparison.Ordinal);
        Assert.False(new ShellNavigationStore(ShellPath).BrainHiddenShown);
    }

    [Fact]
    public void The_port_reads_and_writes_the_devices_file()
    {
        var store = new ShellNavigationStore(ShellPath);
        using var port = new BrainShowHidden(store);
        Assert.False(port.Shown);

        port.SetShown(true);

        Assert.True(port.Shown);
        Assert.True(store.BrainHiddenShown);
        Assert.True(new ShellNavigationStore(ShellPath).BrainHiddenShown);
    }

    /// <summary>The store saves on every scope press and view change, and show
    /// archived lives in it too; the port says only when show hidden itself moved.</summary>
    [Fact]
    public void The_port_announces_only_a_change_of_its_own_choice()
    {
        var store = new ShellNavigationStore(ShellPath);
        using var port = new BrainShowHidden(store);
        var announced = 0;
        port.Changed += () => announced++;

        store.SetRepositoryScope(["backlog"]);
        store.SetLastView("Board");
        store.SetShowArchivedRepositories(true);
        Assert.Equal(0, announced);

        port.SetShown(true);
        port.SetShown(true);
        Assert.Equal(1, announced);

        port.SetShown(false);
        Assert.Equal(2, announced);
    }

    [Fact]
    public void Show_hidden_and_show_archived_are_two_choices()
    {
        var store = new ShellNavigationStore(ShellPath);
        using var port = new BrainShowHidden(store);

        port.SetShown(true);

        Assert.False(store.ShowArchivedRepositories);
    }

    [Fact]
    public void Without_a_store_the_choice_is_held_in_memory()
    {
        using var port = new BrainShowHidden(store: null);
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
