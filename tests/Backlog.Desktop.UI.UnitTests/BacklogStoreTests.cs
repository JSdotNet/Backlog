using Backlog.Desktop.UI.Services;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The store decides where a person's backlog lives. Getting it wrong loses
/// their work, so the failure paths matter more than the happy one.
/// </summary>
[Collection(BacklogStoreCollection.Name)]
public sealed class BacklogStoreTests : IDisposable
{
    private readonly List<string> _tempDirs = [];

    private string TempDir()
    {
        var path = Path.Combine(Path.GetTempPath(), "backlog-store-tests", Guid.NewGuid().ToString("n"));
        _tempDirs.Add(path);
        return path;
    }

    private BacklogStore Store()
    {
        var appData = TempDir();
        return new BacklogStore(appData, Path.Combine(appData, "settings.json"));
    }

    public void Dispose()
    {
        foreach (var dir in _tempDirs.Where(Directory.Exists))
        {
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public void Starts_in_a_usable_folder()
    {
        var store = Store();

        Assert.False(string.IsNullOrWhiteSpace(store.RootDirectory));
        Assert.True(Directory.Exists(store.EntriesDirectory));
        Assert.NotNull(store.Repository);
    }

    [Fact]
    public void Entries_live_in_an_entries_folder_under_the_root()
    {
        var store = Store();
        var target = TempDir();

        Assert.Null(store.TryUseRoot(target));

        Assert.Equal(Path.Combine(target, "entries"), store.EntriesDirectory);
    }

    [Fact]
    public void Moving_creates_the_folder_that_was_asked_for()
    {
        var store = Store();
        var target = TempDir();

        Assert.False(Directory.Exists(target));
        Assert.Null(store.TryUseRoot(target));

        Assert.True(Directory.Exists(Path.Combine(target, "entries")));
        Assert.Equal(target, store.RootDirectory);
    }

    [Fact]
    public void Moving_hands_out_a_repository_pointed_at_the_new_folder()
    {
        var store = Store();
        var before = store.Repository;

        Assert.Null(store.TryUseRoot(TempDir()));

        Assert.NotSame(before, store.Repository);
    }

    [Fact]
    public void Moving_announces_itself_so_open_views_can_reload()
    {
        var store = Store();
        var announced = 0;
        store.RootChanged += () => announced++;

        Assert.Null(store.TryUseRoot(TempDir()));

        Assert.Equal(1, announced);
    }

    [Fact]
    public void Re_selecting_the_folder_it_is_already_in_changes_nothing()
    {
        var store = Store();
        var target = TempDir();
        Assert.Null(store.TryUseRoot(target));

        var announced = 0;
        store.RootChanged += () => announced++;
        var repository = store.Repository;

        Assert.Null(store.TryUseRoot(target));

        Assert.Equal(0, announced);
        Assert.Same(repository, store.Repository);
    }

    [Fact]
    public void Trailing_separators_and_casing_are_the_same_folder()
    {
        var store = Store();
        var target = TempDir();
        Assert.Null(store.TryUseRoot(target));

        var announced = 0;
        store.RootChanged += () => announced++;

        Assert.Null(store.TryUseRoot(target.ToUpperInvariant() + Path.DirectorySeparatorChar));

        Assert.Equal(0, announced);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void An_empty_path_is_answered_not_thrown(string? path)
    {
        var store = Store();
        var before = store.RootDirectory;

        var error = store.TryUseRoot(path);

        Assert.False(string.IsNullOrWhiteSpace(error));
        Assert.Equal(before, store.RootDirectory);
    }

    [Fact]
    public void A_path_that_cannot_be_a_folder_is_answered_not_thrown()
    {
        var store = Store();
        var before = store.RootDirectory;

        var error = store.TryUseRoot("\0not a path\0");

        Assert.False(string.IsNullOrWhiteSpace(error));
        Assert.Equal(before, store.RootDirectory);
    }

    [Fact]
    public void A_relative_path_is_refused_rather_than_quietly_resolved()
    {
        var store = Store();
        var before = store.RootDirectory;

        var error = store.TryUseRoot("notes\\backlog");

        Assert.False(string.IsNullOrWhiteSpace(error));
        Assert.Equal(before, store.RootDirectory);
    }

    [Fact]
    public void A_rejected_path_leaves_the_working_repository_alone()
    {
        var store = Store();
        var repository = store.Repository;

        store.TryUseRoot("   ");

        Assert.Same(repository, store.Repository);
    }

    [Fact]
    public void The_default_folder_knows_it_is_the_default()
    {
        var store = Store();
        Assert.Null(store.ResetToDefault());

        Assert.True(store.IsDefaultRoot);
        Assert.Equal(store.DefaultRootDirectory, store.RootDirectory);
    }

    [Fact]
    public void A_chosen_folder_is_not_the_default()
    {
        var store = Store();

        Assert.Null(store.TryUseRoot(TempDir()));

        Assert.False(store.IsDefaultRoot);
    }

    [Fact]
    public void The_choice_survives_a_restart()
    {
        var appData = TempDir();
        var settingsPath = Path.Combine(appData, "settings.json");
        var store = new BacklogStore(appData, settingsPath);
        var target = TempDir();

        Assert.Null(store.TryUseRoot(target));

        var reopened = new BacklogStore(appData, settingsPath);
        Assert.Equal(target, reopened.RootDirectory);
    }

    [Fact]
    public void The_setting_is_not_kept_inside_the_folder_it_points_at()
    {
        var store = Store();
        var target = TempDir();

        Assert.Null(store.TryUseRoot(target));

        Assert.False(File.Exists(Path.Combine(target, "settings.json")));
    }

    [Fact]
    public void Storage_repository_metadata_survives_a_restart()
    {
        var appData = TempDir();
        var settingsPath = Path.Combine(appData, "settings.json");
        var store = new BacklogStore(appData, settingsPath);

        Assert.Null(store.TrySetRepository("JSdotNet/Backlog"));

        var reopened = new BacklogStore(appData, settingsPath);
        Assert.NotNull(reopened.RootRepository);
        Assert.Equal("JSdotNet/Backlog", reopened.RootRepository!.FullName);
    }

    [Fact]
    public void Storage_repository_metadata_can_be_cleared()
    {
        var appData = TempDir();
        var settingsPath = Path.Combine(appData, "settings.json");
        var store = new BacklogStore(appData, settingsPath);
        Assert.Null(store.TrySetRepository("JSdotNet/Backlog"));

        Assert.Null(store.ClearRepository());

        Assert.Null(store.RootRepository);
        Assert.Null(new BacklogStore(appData, settingsPath).RootRepository);
    }

}
