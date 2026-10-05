using Backlog.Infrastructure.GitHub;
using Backlog.Infrastructure.Sqlite;
using Backlog.Infrastructure.Sync;
using Backlog.Modules.Sync.Abstractions.Services;
using Backlog.Modules.Tasks;
using Backlog.SharedKernel.Results;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// Two paired desktops sharing their repository registry and GitHub accounts through
/// the task feed (local ADR 0021, Verification 1, 2, 5, 7 and 8 end to end).
/// <para>
/// Each device is composed the way a host composes itself — its own per-user
/// <c>github.json</c> over its own workspace root, the port <c>AddGitHub</c>
/// registers, and the real merge and session — and the two exchange through one
/// replica that keeps what the deployed one keeps. The store's own tests pin each
/// rule; these say two devices built on it end holding the same registry and the
/// same identities, and never each other's credentials.
/// </para>
/// </summary>
[Collection(SqlitePoolClearingCollection.Name)]
public sealed class GitHubSettingsSyncBetweenDevicesTests : IDisposable
{
    private static readonly Guid Owner = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private const string Work = "j-schepers_innobv";
    private const string SpecManager = "innovadis-dev/spec-manager";

    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "github-settings-sync-devices-" + Guid.NewGuid().ToString("N"));

    private readonly FakeTaskReplica _replica = new();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();

        if (!Directory.Exists(_root)) return;
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    /// <summary>Verification 1 and 2: A adds the work account with a pasted token and
    /// binds a repository to it; after both sync, B lists the account backed by the
    /// GitHub CLI, holds the binding, and the replica never saw the token.</summary>
    [Fact]
    public async Task A_binding_and_its_account_reach_the_other_device_without_the_token()
    {
        using var a = Device("a");
        using var b = Device("b");

        Assert.Null(a.Settings.SetAccounts([new GitHubAccount(Work)
        {
            DisplayName = "Work",
            Credential = GitHubCredentialKind.PersonalAccessToken,
            Token = "ghp_stays_on_a"
        }]));
        Assert.Null(a.Settings.SetRepositories(GitHubSettings.ParseText(SpecManager).Repositories));
        Assert.Null(a.Settings.SetRepositoryAccount("spec-manager", Work));

        Assert.True((await a.SyncAsync()).IsSuccess);
        Assert.True((await b.SyncAsync()).IsSuccess);

        Assert.Equal(Work, b.Settings.Current.AccountForPath($"repos/{SpecManager}/issues").Login);
        var account = Assert.Single(b.Settings.Current.Accounts);
        Assert.Equal("Work", account.DisplayName);
        Assert.Equal(GitHubCredentialKind.GhCli, account.Credential);
        Assert.Null(account.Token);

        var travelled = _replica.Held(GitHubReplicaDocuments.AccountsId);
        Assert.NotNull(travelled);
        Assert.Equal("github-accounts", travelled.Task.Type);
        Assert.DoesNotContain("ghp_stays_on_a", travelled.Task.ContentMd, StringComparison.Ordinal);
        Assert.DoesNotContain("credential", travelled.Task.ContentMd!, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ghp_stays_on_a", _replica.Held(GitHubReplicaDocuments.RegistryId)!.Task.ContentMd, StringComparison.Ordinal);

        // A's own credential is untouched by its own echo coming back.
        Assert.True((await a.SyncAsync()).IsSuccess);
        Assert.Equal("ghp_stays_on_a", Assert.Single(a.Settings.Current.Accounts).Token);
    }

    /// <summary>Verification 5: a removal on A stays a removal on B, across B's
    /// restart.</summary>
    [Fact]
    public async Task A_removal_reaches_the_other_device_and_survives_its_restart()
    {
        using var a = Device("a");
        Assert.Null(a.Settings.SetRepositories(GitHubSettings.ParseText($"{SpecManager}\nJSdotNet/Backlog").Repositories));
        Assert.True((await a.SyncAsync()).IsSuccess);

        using (var b = Device("b"))
        {
            Assert.True((await b.SyncAsync()).IsSuccess);
            Assert.Equal(2, b.Settings.Current.Repositories.Count);
        }

        Assert.Null(a.Settings.RemoveRepository("spec-manager"));
        Assert.True((await a.SyncAsync()).IsSuccess);

        using (var b = Device("b"))
        {
            Assert.True((await b.SyncAsync()).IsSuccess);
        }

        using var restarted = Device("b");
        Assert.Equal(["backlog"], restarted.Settings.Current.Repositories.Select(r => r.Alias));
        Assert.True(restarted.Settings.Current.WasRemoved(SpecManager));
    }

    /// <summary>Verification 7: a newly paired device with neither document pushes
    /// nothing of its own, and takes A's — never replacing them with empty
    /// lists.</summary>
    [Fact]
    public async Task A_new_device_sends_neither_document_and_takes_both()
    {
        using var a = Device("a");
        Assert.Null(a.Settings.SetAccounts([new GitHubAccount(Work)]));
        Assert.Null(a.Settings.SetRepositories(GitHubSettings.ParseText(SpecManager).Repositories));
        Assert.True((await a.SyncAsync()).IsSuccess);

        using var fresh = Device("fresh");
        var pushed = await fresh.PushAsync();
        Assert.Equal(0, pushed.Pushed);
        Assert.Equal(0, _replica.PushesFrom(fresh.DeviceId));

        Assert.True((await fresh.SyncAsync()).IsSuccess);

        Assert.Equal(SpecManager, Assert.Single(fresh.Settings.Current.Repositories).FullName);
        Assert.Equal(Work, Assert.Single(fresh.Settings.Current.Accounts).Login);
        Assert.Equal(Work, Assert.Single(a.Settings.Current.Accounts).Login);
    }

    /// <summary>Verification 8, across the wire: both devices edit the registry before
    /// either syncs; after both sync, both hold the later edit, and the older push was
    /// refused.</summary>
    [Fact]
    public async Task The_later_registry_edit_wins_on_both_devices()
    {
        using var a = Device("a");
        using var b = Device("b");

        Assert.Null(a.Settings.SetRepositories(GitHubSettings.ParseText("JSdotNet/Earlier").Repositories));
        a.Clock.Advance(TimeSpan.FromMinutes(1));
        b.Clock.Advance(TimeSpan.FromMinutes(2));
        Assert.Null(b.Settings.SetRepositories(GitHubSettings.ParseText("JSdotNet/Later").Repositories));

        Assert.True((await b.SyncAsync()).IsSuccess);
        Assert.Equal(1, (await a.PushAsync()).Refused);
        Assert.True((await a.SyncAsync()).IsSuccess);

        Assert.Equal("JSdotNet/Later", Assert.Single(a.Settings.Current.Repositories).FullName);
        Assert.Equal("JSdotNet/Later", Assert.Single(b.Settings.Current.Repositories).FullName);
    }

    private DesktopDevice Device(string name) =>
        new(Path.Combine(_root, name), _replica, DeviceIds.GetOrAdd(name, _ => Guid.NewGuid()));

    private System.Collections.Concurrent.ConcurrentDictionary<string, Guid> DeviceIds { get; } = new();

    /// <summary>
    /// One desktop: its own GitHub settings over its own workspace root and its own
    /// sync state, composed through <c>AddGitHub</c>, with a session over the shared
    /// replica. Asking for the same name again is that device after a restart.
    /// </summary>
    private sealed class DesktopDevice : IDisposable
    {
        private readonly ServiceProvider _provider;
        private readonly HttpClient _http;
        private readonly TaskSyncSession _session;

        public DesktopDevice(string root, FakeTaskReplica replica, Guid deviceId)
        {
            DeviceId = deviceId;
            Clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 5, 9, 0, 0, TimeSpan.Zero));

            var workspace = Path.Combine(root, "workspace");
            Settings = new GitHubSettingsStore(Path.Combine(root, "local", "github.json"), () => workspace, Clock);

            var services = new ServiceCollection();
            services.AddSingleton(Settings);
            services.AddSingleton<ITaskRepository>(_ => new RootedSqliteTaskRepository(() => workspace));
            services.AddGitHub();
            _provider = services.BuildServiceProvider();

            var tasks = _provider.GetRequiredService<ITaskRepository>();
            var github = _provider.GetRequiredService<IGitHubSettingsReplication>();

            _http = new HttpClient(replica.For(deviceId)) { BaseAddress = new Uri("https://sync.test") };
            _session = new TaskSyncSession(
                new TaskSyncClient(_http),
                new TaskReplicaMerge(tasks, github: github),
                tasks,
                new FileTaskSyncStateStore(Path.Combine(root, "sync", "task-sync-state.json")),
                new InMemoryDeviceCredentialStore(new DeviceCredential(Owner, deviceId, $"PC {deviceId:N}", "credential")),
                Clock,
                github: github);
        }

        public Guid DeviceId { get; }

        public FakeTimeProvider Clock { get; }

        public GitHubSettingsStore Settings { get; }

        public Task<Result<TaskSyncSummary>> SyncAsync() => _session.SyncAsync(Cancellation);

        public async Task<TaskSyncSummary> PushAsync()
        {
            var pushed = await _session.PushAsync(Cancellation);
            Assert.True(pushed.IsSuccess);
            return pushed.Value;
        }

        public void Dispose()
        {
            _http.Dispose();
            _provider.Dispose();
        }
    }
}
