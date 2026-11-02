using Backlog.Infrastructure.FileSystem;
using Backlog.Modules.Devbook.Abstractions;

using Microsoft.Extensions.Time.Testing;

namespace Backlog.Infrastructure.FileSystem.UnitTests;

/// <summary>
/// The direction map on disk: kept in the subject store's per-repository file
/// beside the subjects, what survives a new store over the same folder, that a
/// write of either kind keeps the other, that an unreadable entry is skipped, and
/// that the Markdown's statements are copied in once and never over a person's
/// choice.
/// </summary>
public sealed class DevbookSyncDirectionStoreTests : IDisposable
{
    private const string Repository = "JSdotNet/Backlog";
    private const string Host = "DEV-TOWER";
    private const string Inbox = "domain/inbox/context.md";
    private const string TaskUnit = "domain/tasks/domain.md#task";

    private static readonly DevbookSubject Debt = DevbookSubject.Debt(".devbook/arc42/tdr/0001-archify.md");

    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "devbook-sync-direction-store-tests-" + Guid.NewGuid().ToString("N"));

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 2, 9, 0, 0, TimeSpan.Zero));

    public DevbookSyncDirectionStoreTests() => Directory.CreateDirectory(_root);

    private string Storage => Path.Combine(_root, "storage");

    private IDevbookSyncDirectionStore Store(IDevbookFolderSource? folders = null) => new DevbookSubjectStateStore(Storage, _time, folders);

    [Fact]
    public void A_direction_survives_a_new_store_over_the_same_folder()
    {
        var kept = Store().Set(Repository, Inbox, "Pull", Host);

        var reread = Store();

        Assert.NotNull(kept);
        Assert.Equal(new DevbookSyncStatement(Inbox, "pull", Host, _time.GetUtcNow()), kept);
        Assert.Equal("pull", reread.Get(Repository, Inbox));
        Assert.Equal([kept], reread.List(Repository));
    }

    [Theory]
    [InlineData(".devbook/domain/inbox/context.md")]
    [InlineData(".domain/inbox/context.md")]
    [InlineData("domain/inbox/context.md")]
    public void One_block_is_one_entry_whichever_layout_names_it(string address)
    {
        var store = Store();
        store.Set(Repository, ".devbook/domain/inbox/context.md", "push", Host);

        Assert.Equal("push", store.Get(Repository, address));
        Assert.Equal("push", store.Get("jsdotnet/backlog", address));
        Assert.Single(store.List(Repository));
    }

    [Fact]
    public void Clearing_a_direction_removes_the_entry_so_the_block_inherits()
    {
        var store = Store();
        store.Set(Repository, TaskUnit, "push", Host);

        var cleared = store.Set(Repository, TaskUnit, null, Host);

        Assert.Null(cleared);
        Assert.Null(Store().Get(Repository, TaskUnit));
        Assert.Empty(Store().List(Repository));
    }

    [Fact]
    public void Clearing_what_nothing_states_writes_and_raises_nothing()
    {
        var store = Store();
        var raised = 0;
        store.Changed += () => raised++;

        Assert.Null(store.Set(Repository, TaskUnit, "  ", Host));

        Assert.Equal(0, raised);
        Assert.False(Directory.Exists(Path.Combine(Storage, DevbookSubjectStateStore.FolderName)));
    }

    [Fact]
    public void A_value_outside_the_five_and_an_address_in_no_folder_are_refused_before_anything_is_written()
    {
        var store = Store();

        Assert.Throws<ArgumentException>(() => store.Set(Repository, TaskUnit, "both", Host));
        Assert.Throws<ArgumentException>(() => store.Set(Repository, "src/Program.cs", "push", Host));

        Assert.Empty(store.List(Repository));
        Assert.False(Directory.Exists(Path.Combine(Storage, DevbookSubjectStateStore.FolderName)));
    }

    [Fact]
    public void A_direction_write_keeps_the_subjects_and_a_subject_write_keeps_the_directions()
    {
        var subjects = new DevbookSubjectStateStore(Storage, _time);
        subjects.SetState(Repository, Debt, "planned", Host);

        ((IDevbookSyncDirectionStore)subjects).Set(Repository, Inbox, "pull", Host);
        subjects.SetState(Repository, Debt, "in-progress", Host);

        var reread = new DevbookSubjectStateStore(Storage, _time);
        Assert.Equal("in-progress", reread.Get(Repository, Debt)?.State);
        Assert.Equal("pull", reread.Get(Repository, Inbox));
        Assert.Single(Directory.GetFiles(Path.Combine(Storage, DevbookSubjectStateStore.FolderName), "*.json"));
    }

    [Fact]
    public void Each_port_announces_only_its_own_writes()
    {
        var store = new DevbookSubjectStateStore(Storage, _time);
        var subjectEvents = 0;
        var directionEvents = 0;
        ((IDevbookSubjectStateStore)store).Changed += () => subjectEvents++;
        ((IDevbookSyncDirectionStore)store).Changed += () => directionEvents++;

        store.SetState(Repository, Debt, "planned", Host);
        Assert.Equal((1, 0), (subjectEvents, directionEvents));

        ((IDevbookSyncDirectionStore)store).Set(Repository, Inbox, "pull", Host);
        Assert.Equal((1, 1), (subjectEvents, directionEvents));

        store.Import("JSdotNet/devbook", [(Inbox, "push")]);
        Assert.Equal((1, 2), (subjectEvents, directionEvents));
    }

    [Fact]
    public void An_entry_this_version_cannot_read_is_skipped_and_the_rest_of_the_file_kept()
    {
        var folder = Path.Combine(Storage, DevbookSubjectStateStore.FolderName);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "backlog.json"), """
            {
              "repositoryAlias": "jsdotnet/backlog",
              "subjects": [],
              "directions": [
                null,
                { "address": "", "direction": "push", "by": "x", "at": "2026-11-01T00:00:00Z" },
                { "address": "src/Program.cs", "direction": "push", "by": "x", "at": "2026-11-01T00:00:00Z" },
                { "address": "domain/tasks/domain.md#task", "direction": "both", "by": "x", "at": "2026-11-01T00:00:00Z" },
                { "address": "domain/inbox/context.md", "direction": null, "by": "x", "at": "2026-11-01T00:00:00Z" },
                { "address": ".devbook/domain/sessions/context.md", "direction": "Push", "by": "x", "at": "2026-11-01T00:00:00Z" }
              ],
              "directionsImportedAt": "2026-11-01T00:00:00Z"
            }
            """);

        var store = Store();

        var only = Assert.Single(store.List(Repository));
        Assert.Equal(new DevbookSyncStatement("domain/sessions/context.md", "push", "x", new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero)), only);
        Assert.True(store.IsImported(Repository));
    }

    [Fact]
    public void A_file_written_before_the_map_existed_reads_as_an_empty_unimported_map()
    {
        var folder = Path.Combine(Storage, DevbookSubjectStateStore.FolderName);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "backlog.json"), """{ "repositoryAlias": "jsdotnet/backlog", "subjects": [], "directions": null }""");

        var store = Store();

        Assert.Empty(store.List(Repository));
        Assert.False(store.IsImported(Repository));
    }

    [Fact]
    public void An_import_adds_only_what_the_map_lacks_marks_the_repository_and_survives_a_restart()
    {
        var store = Store();
        store.Set(Repository, Inbox, "off", Host);

        store.Import(Repository, [(Inbox, "pull"), ("domain/sessions/context.md", "push"), ("domain/tasks/domain.md#task", "both")]);

        var reread = Store();
        Assert.True(reread.IsImported(Repository));
        Assert.Equal("off", reread.Get(Repository, Inbox));
        Assert.Equal("push", reread.Get(Repository, "domain/sessions/context.md"));
        Assert.Null(reread.Get(Repository, TaskUnit));
        Assert.Equal(DevbookSyncStatement.ImportedBy, reread.List(Repository).Single(entry => entry.Address == "domain/sessions/context.md").By);
    }

    [Fact]
    public void A_second_import_is_a_no_op()
    {
        var store = Store();
        store.Import(Repository, [(Inbox, "pull")]);
        store.Set(Repository, Inbox, null, Host);

        store.Import(Repository, [(Inbox, "pull"), (TaskUnit, "push")]);

        Assert.Empty(store.List(Repository));
    }

    [Fact]
    public void The_first_read_imports_what_the_markdown_states_once()
    {
        var domain = Path.Combine(_root, "clone", ".devbook", "domain");
        Directory.CreateDirectory(Path.Combine(domain, "inbox"));
        File.WriteAllText(Path.Combine(domain, "inbox", "context.md"), "# Inbox\n\n```meta\ntype: context\nsync: pull\n```\n");
        var folders = new StubFolders(new Dictionary<string, Location> { [".domain"] = new(domain) });
        var store = Store(folders);

        Assert.Equal("pull", store.Get(Repository, Inbox));
        Assert.True(store.IsImported(Repository));

        // A person's choice stands, and the Markdown is not read again.
        store.Set(Repository, Inbox, null, Host);
        File.WriteAllText(Path.Combine(domain, "inbox", "context.md"), "# Inbox\n\n```meta\ntype: context\nsync: push\n```\n");

        Assert.Null(store.Get(Repository, Inbox));
        Assert.Null(Store(folders).Get(Repository, Inbox));
    }

    [Fact]
    public void A_direction_cleared_before_the_first_read_is_not_put_back_by_the_import()
    {
        var domain = DomainStating("pull");
        var store = Store(new StubFolders(new Dictionary<string, Location> { [".domain"] = new(domain) }));

        store.Set(Repository, Inbox, null, Host);

        Assert.True(store.IsImported(Repository));
        Assert.Null(store.Get(Repository, Inbox));
        Assert.Empty(store.List(Repository));
    }

    [Fact]
    public void A_repository_none_of_whose_folders_resolves_is_not_marked_imported()
    {
        var store = Store(new StubFolders(new Dictionary<string, Location>()));

        Assert.Empty(store.List(Repository));
        Assert.False(store.IsImported(Repository));
    }

    /// <summary>
    /// Only a clone's working copy is imported. A branch snapshot holds the files
    /// somebody opened, so a scan of it would mark the repository done with values
    /// missing; one still being fetched holds less. Either one among the three
    /// folders stops the whole import, and the repository stays unmarked.
    /// </summary>
    [Theory]
    [InlineData(DevbookSourceKind.Branch, false)]
    [InlineData(DevbookSourceKind.LocalFolder, true)]
    public void A_snapshot_or_a_pending_folder_imports_nothing_and_leaves_the_repository_unmarked(DevbookSourceKind arc42Source, bool arc42Pending)
    {
        var domain = DomainStating("pull");
        var store = Store(new StubFolders(new Dictionary<string, Location>
        {
            [".domain"] = new(domain),
            [".arc42"] = new(Path.Combine(_root, "clone", ".devbook", "arc42"), arc42Source, arc42Pending),
        }));

        Assert.Null(store.Get(Repository, Inbox));
        Assert.False(store.IsImported(Repository));
    }

    [Fact]
    public void A_repository_read_only_from_a_branch_is_not_imported()
    {
        var domain = DomainStating("pull");
        var store = Store(new StubFolders(new Dictionary<string, Location> { [".domain"] = new(domain, DevbookSourceKind.Branch) }));

        Assert.Empty(store.List(Repository));
        Assert.False(store.IsImported(Repository));
    }

    /// <summary>A clone's domain folder whose inbox context states
    /// <paramref name="direction"/>.</summary>
    private string DomainStating(string direction)
    {
        var domain = Path.Combine(_root, "clone", ".devbook", "domain");
        Directory.CreateDirectory(Path.Combine(domain, "inbox"));
        File.WriteAllText(Path.Combine(domain, "inbox", "context.md"), $"# Inbox\n\n```meta\ntype: context\nsync: {direction}\n```\n");
        return domain;
    }

    [Fact]
    public void With_no_folder_source_nothing_is_imported()
    {
        var store = Store();

        Assert.Null(store.Get(Repository, Inbox));
        Assert.False(store.IsImported(Repository));
    }

    public void Dispose()
    {
        if (!Directory.Exists(_root)) return;

        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    /// <summary>Where one key resolves: a folder, from a clone or a branch, or
    /// pending — a branch whose first fetch is running, which is unavailable.</summary>
    private sealed record Location(string Path, DevbookSourceKind Source = DevbookSourceKind.LocalFolder, bool Pending = false);

    /// <summary>Resolves the keys it is given to their folders, for any alias, and
    /// every other key to nothing.</summary>
    private sealed class StubFolders(IReadOnlyDictionary<string, Location> byKey) : IDevbookFolderSource
    {
        public event Action? Changed;

        public void NotifyContentChanged() => Changed?.Invoke();

        public IReadOnlyList<DevbookFolderSetting> Folders(string? repositoryAlias) => [];

        public DevbookFolderLocation Resolve(string key, string? repositoryAlias = null) =>
            byKey.TryGetValue(key, out var location)
                ? location.Pending
                    ? DevbookFolderLocation.Unavailable(key, "Fetching.", source: location.Source, pending: true)
                    : new DevbookFolderLocation(key, true, null, Repository, null, location.Path, Source: location.Source)
                : DevbookFolderLocation.Unavailable(key, "Not configured.");
    }
}
