using Backlog.Infrastructure.Sqlite;
using Backlog.Modules.Tasks.Abstractions;
using Backlog.Modules.Tasks.DomainModels;
using Microsoft.Data.Sqlite;

namespace Backlog.Infrastructure.FileSystem.UnitTests;

/// <summary>
/// The first start after the manifest stopped redirecting the app's writes.
/// Until then a packaged install kept everything in
/// <c>Packages\&lt;family&gt;\LocalCache\Local\Backlog</c>; from then on the app
/// reads <c>%LocalAppData%\Backlog</c>, which is empty on the machine that has
/// been using it. What these prove is that the state comes across — and only
/// the app's state, never over something already there, never by removing the
/// old copy, and never by writing the settings file before the database it
/// describes is in place.
/// <para>
/// Two temporary folders stand in for the redirected and the real one; the
/// adoption takes both paths for exactly this reason.
/// </para>
/// </summary>
public sealed class PackagedAppDataTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "backlog-packaged-appdata-tests",
        Guid.NewGuid().ToString("n"));

    private string Redirected => Path.Combine(_root, "Packages", "LocalCache", "Local", "Backlog");

    private string AppData => Path.Combine(_root, "Local", "Backlog");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public async Task The_database_and_the_apps_own_folders_come_across()
    {
        var task = new TaskItem("Still mine", string.Empty, EntryType.Task);
        await new SqliteTaskRepository(Redirected).SaveAsync(task, TestContext.Current.CancellationToken);
        Write(Path.Combine(Redirected, "_inbox", "capture.md"), "captured");
        Write(Path.Combine(Redirected, "config", "repos.json"), "[]");
        Write(Path.Combine(Redirected, ".tools", "ai-tools.json"), "{}");

        var adoption = PackagedAppData.Adopt(Redirected, AppData);

        Assert.Equal(AppDataAdoptionOutcome.Adopted, adoption.Outcome);
        var arrived = await new SqliteTaskRepository(AppData).GetAsync(task.Id, TestContext.Current.CancellationToken);
        Assert.NotNull(arrived);
        Assert.Equal("Still mine", arrived.Title);
        Assert.Equal("captured", File.ReadAllText(Path.Combine(AppData, "_inbox", "capture.md")));
        Assert.Equal("[]", File.ReadAllText(Path.Combine(AppData, "config", "repos.json")));
        Assert.Equal("{}", File.ReadAllText(Path.Combine(AppData, ".tools", "ai-tools.json")));
    }

    /// <summary>One JSON file per store at the top of the folder is every
    /// per-device setting the app has — the device identity, the sync
    /// watermark, the feature flags. A cache folder is not one of them: it is
    /// rebuilt from wherever it came, and it can be large.</summary>
    [Fact]
    public void The_per_device_settings_files_come_across_and_the_caches_do_not()
    {
        Write(Path.Combine(Redirected, "settings.json"), "{}");
        Write(Path.Combine(Redirected, "device.json"), "{\"id\":\"x\"}");
        Write(Path.Combine(Redirected, "task-sync-state.json"), "{\"watermark\":1}");
        Write(Path.Combine(Redirected, "devbook-cache", "snapshot", "chapter.md"), "# big");
        Write(Path.Combine(Redirected, "activity-cache", "index.json"), "{}");

        var adoption = PackagedAppData.Adopt(Redirected, AppData);

        Assert.Equal(AppDataAdoptionOutcome.Adopted, adoption.Outcome);
        Assert.Equal("{\"id\":\"x\"}", File.ReadAllText(Path.Combine(AppData, "device.json")));
        Assert.Equal("{\"watermark\":1}", File.ReadAllText(Path.Combine(AppData, "task-sync-state.json")));
        Assert.True(File.Exists(Path.Combine(AppData, "settings.json")));
        Assert.False(Directory.Exists(Path.Combine(AppData, "devbook-cache")));
        Assert.False(Directory.Exists(Path.Combine(AppData, "activity-cache")));
    }

    /// <summary>The default root was the markdown store's folder once, and a
    /// person who has used the app that long still has their own notes, images
    /// and folders beside the database — and the app's retired
    /// <c>_backlog</c> beside those. None of that is the app's to move.</summary>
    [Fact]
    public async Task What_the_person_kept_beside_the_backlog_stays_where_it_was()
    {
        await new SqliteTaskRepository(Redirected).SaveAsync(new TaskItem("Any", string.Empty, EntryType.Task), TestContext.Current.CancellationToken);
        Write(Path.Combine(Redirected, "Cost.md"), "# my notes");
        Write(Path.Combine(Redirected, "image.png"), "png");
        Write(Path.Combine(Redirected, "Documents", "letter.docx"), "docx");
        Write(Path.Combine(Redirected, "_backlog", "old-entry.md"), "# old");
        Write(Path.Combine(Redirected, "backlog-JS-DESKTOP.db"), "not the database");

        var adoption = PackagedAppData.Adopt(Redirected, AppData);

        Assert.Equal(AppDataAdoptionOutcome.Adopted, adoption.Outcome);
        Assert.False(File.Exists(Path.Combine(AppData, "Cost.md")));
        Assert.False(File.Exists(Path.Combine(AppData, "image.png")));
        Assert.False(Directory.Exists(Path.Combine(AppData, "Documents")));
        Assert.False(Directory.Exists(Path.Combine(AppData, "_backlog")));
        Assert.False(File.Exists(Path.Combine(AppData, "backlog-JS-DESKTOP.db")));
    }

    /// <summary>Copied, not moved. The redirected folder is the only copy
    /// until the adoption is known to be good, and it is the person's to
    /// remove once they are sure — the same terms a moved root is left on.</summary>
    [Fact]
    public async Task The_redirected_folder_is_left_as_it_was()
    {
        var task = new TaskItem("Kept", string.Empty, EntryType.Task);
        await new SqliteTaskRepository(Redirected).SaveAsync(task, TestContext.Current.CancellationToken);
        Write(Path.Combine(Redirected, "settings.json"), "{}");

        _ = PackagedAppData.Adopt(Redirected, AppData);

        Assert.True(File.Exists(Path.Combine(Redirected, "settings.json")));
        Assert.NotNull(await new SqliteTaskRepository(Redirected).GetAsync(task.Id, TestContext.Current.CancellationToken));
    }

    /// <summary>A root pointed somewhere else was a real path all along and
    /// was never redirected. Whatever the redirected folder still holds beside
    /// the settings file is a backlog the app stopped reading when the root
    /// moved, and bringing it across would put a stale database in the
    /// default folder for the next "move to the default folder" to refuse.</summary>
    [Fact]
    public async Task A_root_pointed_elsewhere_brings_the_settings_and_not_a_stale_database()
    {
        await new SqliteTaskRepository(Redirected).SaveAsync(new TaskItem("Stale", string.Empty, EntryType.Task), TestContext.Current.CancellationToken);
        Write(Path.Combine(Redirected, "config", "repos.json"), "[]");
        var elsewhere = Path.Combine(_root, "Notes");
        Write(Path.Combine(Redirected, "settings.json"), $"{{\"rootDirectory\":{System.Text.Json.JsonSerializer.Serialize(elsewhere)}}}");

        var adoption = PackagedAppData.Adopt(Redirected, AppData);

        Assert.Equal(AppDataAdoptionOutcome.Adopted, adoption.Outcome);
        Assert.True(File.Exists(Path.Combine(AppData, "settings.json")));
        Assert.False(File.Exists(Path.Combine(AppData, "backlog.db")));
        Assert.False(Directory.Exists(Path.Combine(AppData, "config")));
    }

    /// <summary>The settings file names the path the app saw — the real one —
    /// while sitting at the redirected one. That spelling of the default is
    /// still the default.</summary>
    [Fact]
    public async Task A_root_naming_the_real_default_folder_is_the_default()
    {
        var task = new TaskItem("Home", string.Empty, EntryType.Task);
        await new SqliteTaskRepository(Redirected).SaveAsync(task, TestContext.Current.CancellationToken);
        Write(Path.Combine(Redirected, "settings.json"), $"{{\"rootDirectory\":{System.Text.Json.JsonSerializer.Serialize(AppData + Path.DirectorySeparatorChar)}}}");

        var adoption = PackagedAppData.Adopt(Redirected, AppData);

        Assert.Equal(AppDataAdoptionOutcome.Adopted, adoption.Outcome);
        Assert.NotNull(await new SqliteTaskRepository(AppData).GetAsync(task.Id, TestContext.Current.CancellationToken));
    }

    /// <summary>A real folder that already has settings or a database is one
    /// an earlier start adopted, or one the install wrote directly. Nothing in
    /// it is replaced, and nothing beside it is added.</summary>
    [Fact]
    public async Task A_folder_already_in_use_is_left_alone()
    {
        await new SqliteTaskRepository(Redirected).SaveAsync(new TaskItem("Theirs", string.Empty, EntryType.Task), TestContext.Current.CancellationToken);
        Write(Path.Combine(Redirected, "device.json"), "{\"id\":\"old\"}");
        var mine = new TaskItem("Mine", string.Empty, EntryType.Task);
        await new SqliteTaskRepository(AppData).SaveAsync(mine, TestContext.Current.CancellationToken);

        var adoption = PackagedAppData.Adopt(Redirected, AppData);

        Assert.Equal(AppDataAdoptionOutcome.AlreadyInPlace, adoption.Outcome);
        var only = Assert.Single(await new SqliteTaskRepository(AppData).ListAsync(TestContext.Current.CancellationToken));
        Assert.Equal("Mine", only.Title);
        Assert.False(File.Exists(Path.Combine(AppData, "device.json")));
    }

    [Fact]
    public void No_redirected_folder_is_nothing_to_adopt()
    {
        var adoption = PackagedAppData.Adopt(Redirected, AppData);

        Assert.Equal(AppDataAdoptionOutcome.NothingToAdopt, adoption.Outcome);
        Assert.False(Directory.Exists(AppData));
    }

    /// <summary>A redirected folder with nothing of the app's in it — an
    /// install that was opened once and never wrote — is the same answer.</summary>
    [Fact]
    public void A_redirected_folder_with_nothing_of_the_apps_is_nothing_to_adopt()
    {
        Directory.CreateDirectory(Path.Combine(Redirected, "_inbox"));

        var adoption = PackagedAppData.Adopt(Redirected, AppData);

        Assert.Equal(AppDataAdoptionOutcome.NothingToAdopt, adoption.Outcome);
    }

    /// <summary>The settings file is the marker that adoption is done, so it is
    /// written last. A database that cannot be copied leaves no settings file
    /// behind, and the next start finds the real folder still unclaimed and
    /// tries again — rather than an app pointed at an empty folder it believes
    /// it adopted.</summary>
    [Fact]
    public void A_database_that_cannot_be_copied_is_reported_and_leaves_no_settings_file()
    {
        Write(Path.Combine(Redirected, "backlog.db"), "this is not a database");
        Write(Path.Combine(Redirected, "settings.json"), "{}");

        var adoption = PackagedAppData.Adopt(Redirected, AppData);

        Assert.Equal(AppDataAdoptionOutcome.Failed, adoption.Outcome);
        Assert.NotNull(adoption.Error);
        Assert.False(File.Exists(Path.Combine(AppData, "settings.json")));

        // And the failure is what stops the next start from calling it done.
        Assert.NotEqual(AppDataAdoptionOutcome.AlreadyInPlace, PackagedAppData.Adopt(Redirected, AppData).Outcome);
    }

    private static void Write(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }
}
