using Backlog.Infrastructure.FileSystem;
using Backlog.Modules.Devbook.Abstractions;

namespace Backlog.Infrastructure.FileSystem.UnitTests;

/// <summary>
/// The Brain folder setting (local ADR 0026): no default, kept in the app's own
/// settings file and never in the folder it names, and a refusal in words for a
/// path that cannot be a Brain.
/// </summary>
public sealed class BrainSettingsStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "brain-settings-store-tests-" + Guid.NewGuid().ToString("N"));

    public BrainSettingsStoreTests() => Directory.CreateDirectory(_root);

    private string SettingsFile => Path.Combine(_root, "app-data", BrainSettingsStore.FileName);

    private BrainSettingsStore Store() => new(SettingsFile);

    [Fact]
    public void A_fresh_install_has_no_brain_folder_and_writes_no_file()
    {
        var store = Store();

        Assert.Null(store.BrainFolder);
        Assert.False(((IBrainSettings)store).IsSet);
        Assert.False(File.Exists(SettingsFile));
    }

    [Fact]
    public void A_picked_folder_is_kept_in_the_settings_and_survives_a_restart()
    {
        var brain = Path.Combine(_root, "OneDrive", "Brain");

        Assert.Null(Store().TrySetBrainFolder(brain));

        var reopened = Store();
        Assert.Equal(brain, reopened.BrainFolder);
        Assert.Contains("Brain", File.ReadAllText(SettingsFile), StringComparison.Ordinal);
    }

    /// <summary>ADR 0026's Verification 2: after the person picks a folder, the
    /// path is in the app's settings and no file in the folder names it.</summary>
    [Fact]
    public void Picking_an_empty_folder_leaves_it_empty()
    {
        var brain = Path.Combine(_root, "empty-brain");
        Directory.CreateDirectory(brain);

        Assert.Null(Store().TrySetBrainFolder(brain));

        Assert.Empty(Directory.EnumerateFileSystemEntries(brain));
        Assert.False(SettingsFile.StartsWith(brain, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void A_folder_that_does_not_exist_yet_is_created()
    {
        var brain = Path.Combine(_root, "new", "Brain");

        Assert.Null(Store().TrySetBrainFolder(brain));

        Assert.True(Directory.Exists(brain));
    }

    [Fact]
    public void A_relative_path_is_refused_in_words()
    {
        var store = Store();

        var error = store.TrySetBrainFolder("notes\\brain");

        Assert.NotNull(error);
        Assert.Contains("full path", error, StringComparison.Ordinal);
        Assert.Null(store.BrainFolder);
    }

    [Fact]
    public void A_folder_inside_a_repository_is_refused()
    {
        var repository = Path.Combine(_root, "repo");
        Directory.CreateDirectory(Path.Combine(repository, ".git"));
        var store = Store();

        var error = store.TrySetBrainFolder(Path.Combine(repository, "notes"));

        Assert.NotNull(error);
        Assert.Contains("outside every repository", error, StringComparison.Ordinal);
        Assert.Null(store.BrainFolder);
    }

    /// <summary>A linked worktree marks itself with a <c>.git</c> file rather
    /// than a folder, and is a repository all the same.</summary>
    [Fact]
    public void A_folder_inside_a_linked_worktree_is_refused()
    {
        var worktree = Path.Combine(_root, "worktree");
        Directory.CreateDirectory(worktree);
        File.WriteAllText(Path.Combine(worktree, ".git"), "gitdir: elsewhere");

        Assert.NotNull(Store().TrySetBrainFolder(worktree));
    }

    [Fact]
    public void A_file_is_not_a_folder()
    {
        var file = Path.Combine(_root, "brain.txt");
        File.WriteAllText(file, "not a folder");

        Assert.NotNull(Store().TrySetBrainFolder(file));
    }

    [Fact]
    public void A_blank_path_clears_the_folder_and_raises_once()
    {
        var store = Store();
        _ = store.TrySetBrainFolder(Path.Combine(_root, "Brain"));

        var raised = 0;
        store.Changed += () => raised++;

        Assert.Null(store.TrySetBrainFolder("  "));
        Assert.Null(store.BrainFolder);
        Assert.Null(Store().BrainFolder);
        Assert.Equal(1, raised);

        // Clearing what is already clear is not a change.
        Assert.Null(store.TrySetBrainFolder(null));
        Assert.Equal(1, raised);
    }

    [Fact]
    public void Setting_the_same_folder_again_is_not_a_change()
    {
        var brain = Path.Combine(_root, "Brain");
        var store = Store();
        _ = store.TrySetBrainFolder(brain);

        var raised = 0;
        store.Changed += () => raised++;

        Assert.Null(store.TrySetBrainFolder(brain + Path.DirectorySeparatorChar));
        Assert.Equal(0, raised);
    }

    [Fact]
    public void An_unreadable_settings_file_reads_as_no_folder()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsFile)!);
        File.WriteAllText(SettingsFile, "{ not json");

        Assert.Null(Store().BrainFolder);
    }

    [Fact]
    public void The_chip_reads_set_up_your_brain_until_a_folder_is_set()
    {
        var store = Store();

        Assert.Equal("Set up your Brain", BrainSetup.ChipLabel(store));

        _ = store.TrySetBrainFolder(Path.Combine(_root, "Brain"));

        Assert.Equal("Brain", BrainSetup.ChipLabel(store));
        Assert.Equal("settings?page=storage#brain-folder", BrainSetup.SettingsHref);
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
