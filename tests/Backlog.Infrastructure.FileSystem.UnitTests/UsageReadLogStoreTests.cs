using Backlog.Infrastructure.FileSystem;
using Backlog.Infrastructure.GitHub;
using Backlog.Modules.Dashboard.Abstractions.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Backlog.Infrastructure.FileSystem.UnitTests;

/// <summary>
/// When each usage source was last read, on disk: a success, a failure with its one-line
/// reason, a success that clears the failure, a restart that keeps all of it, and a file
/// that stays out of everything another device reads.
/// </summary>
public sealed class UsageReadLogStoreTests : IDisposable
{
    private static readonly DateTimeOffset Morning = new(2026, 10, 13, 8, 0, 0, TimeSpan.Zero);

    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "usage-read-log-store-tests-" + Guid.NewGuid().ToString("N"));

    private readonly FakeTimeProvider _time = new(Morning);

    public UsageReadLogStoreTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private string LogFile => Path.Combine(_root, UsageReadLogStore.FileName);

    private UsageReadLogStore Store() => new(LogFile, _time);

    [Fact]
    public void A_source_never_read_has_no_status_and_no_file()
    {
        var store = Store();

        Assert.Null(store.For(UsageSource.GitHub));
        Assert.Empty(store.All);
        Assert.False(File.Exists(LogFile));
    }

    [Fact]
    public void A_success_records_its_time_and_no_failure()
    {
        var store = Store();

        store.Succeeded(UsageSource.Claude("personal"));

        var status = Assert.IsType<UsageReadStatus>(store.For(UsageSource.Claude("personal")));
        Assert.Equal(Morning, status.SucceededAt);
        Assert.Null(status.FailedAt);
        Assert.Null(status.FailureReason);
        Assert.False(status.LastReadFailed);
    }

    [Fact]
    public void A_failure_records_its_time_and_one_line_reason_and_keeps_the_last_success()
    {
        var store = Store();
        store.Succeeded(UsageSource.GitHub);

        _time.Advance(TimeSpan.FromHours(2));
        store.Failed(UsageSource.GitHub, "GitHub answered 403.\nStack trace that is no use on a card.");

        var status = Assert.IsType<UsageReadStatus>(store.For(UsageSource.GitHub));
        Assert.Equal(Morning, status.SucceededAt);
        Assert.Equal(Morning.AddHours(2), status.FailedAt);
        Assert.Equal("GitHub answered 403.", status.FailureReason);
        Assert.True(status.LastReadFailed);
    }

    [Fact]
    public void A_failure_with_no_success_before_it_has_no_success_time()
    {
        var store = Store();

        store.Failed(UsageSource.Claude("work"), "The admin key was refused.");

        var status = Assert.IsType<UsageReadStatus>(store.For(UsageSource.Claude("work")));
        Assert.Null(status.SucceededAt);
        Assert.Equal(Morning, status.FailedAt);
    }

    [Fact]
    public void A_success_after_a_failure_clears_the_failure()
    {
        var store = Store();
        store.Failed(UsageSource.GitHub, "Not reachable.");

        _time.Advance(TimeSpan.FromMinutes(5));
        store.Succeeded(UsageSource.GitHub);

        var status = Assert.IsType<UsageReadStatus>(store.For(UsageSource.GitHub));
        Assert.Equal(Morning.AddMinutes(5), status.SucceededAt);
        Assert.Null(status.FailedAt);
        Assert.Null(status.FailureReason);
    }

    [Fact]
    public void Each_claude_organization_keeps_its_own_status()
    {
        var store = Store();

        store.Succeeded(UsageSource.Claude("personal"));
        store.Failed(UsageSource.Claude("work"), "Refused.");

        Assert.False(store.For(UsageSource.Claude("personal"))!.LastReadFailed);
        Assert.True(store.For(UsageSource.Claude("work"))!.LastReadFailed);
        Assert.Null(store.For(UsageSource.GitHub));
    }

    [Fact]
    public void Every_status_survives_a_restart()
    {
        var store = Store();
        store.Succeeded(UsageSource.Claude("personal"));
        store.Succeeded(UsageSource.GitHub);
        _time.Advance(TimeSpan.FromHours(1));
        store.Failed(UsageSource.GitHub, "Rate limited.");

        var reopened = Store();

        Assert.Equal(store.All, reopened.All);
        Assert.Equal("Rate limited.", reopened.For(UsageSource.GitHub)!.FailureReason);
    }

    [Fact]
    public void Every_recorded_read_raises_changed()
    {
        var store = Store();
        var raised = 0;
        store.Changed += () => raised++;

        store.Succeeded(UsageSource.GitHub);
        store.Failed(UsageSource.GitHub, "Refused.");

        Assert.Equal(2, raised);
    }

    [Fact]
    public void An_unreadable_file_starts_empty()
    {
        File.WriteAllText(LogFile, "{ not json");

        var store = Store();

        Assert.Empty(store.All);

        store.Succeeded(UsageSource.GitHub);
        Assert.NotNull(Store().For(UsageSource.GitHub));
    }

    [Fact]
    public void A_long_reason_is_cut_to_one_card_line()
    {
        var store = Store();

        store.Failed(UsageSource.GitHub, new string('x', 1000));

        Assert.Equal(UsageReadLogStore.MaxReasonLength, store.For(UsageSource.GitHub)!.FailureReason!.Length);
    }

    /// <summary>
    /// The workspace file and the GitHub registry are what travel to another device
    /// (ADR 0005, local ADR 0021). Recording reads through the host's composition writes
    /// the log beside the per-user settings and touches neither.
    /// </summary>
    [Fact]
    public void Recording_reads_writes_nothing_to_synced_settings()
    {
        var appData = Path.Combine(_root, "appdata");
        var syncedRoot = Path.Combine(_root, "synced-backlog");
        Directory.CreateDirectory(appData);
        Directory.CreateDirectory(syncedRoot);

        var workspace = new WorkspaceSettingsStore(appData, Path.Combine(appData, "settings.json"), _ => null);
        Assert.Null(workspace.TryUseRoot(syncedRoot));

        var github = new GitHubSettingsStore(Path.Combine(appData, "github.json"), () => syncedRoot);
        Assert.Null(github.SetRepositories(GitHubSettings.ParseText("JSdotNet/Backlog").Repositories));

        var services = new ServiceCollection();
        services.AddSingleton(workspace);
        services.AddWorkspaceCaches();
        using var provider = services.BuildServiceProvider();

        var before = Snapshot(syncedRoot, Path.Combine(appData, "settings.json"), Path.Combine(appData, "github.json"));

        var recorder = provider.GetRequiredService<IUsageReadRecorder>();
        recorder.Succeeded(UsageSource.Claude("personal"));
        recorder.Failed(UsageSource.GitHub, "Refused.");

        Assert.Equal(before, Snapshot(syncedRoot, Path.Combine(appData, "settings.json"), Path.Combine(appData, "github.json")));
        Assert.Equal(Path.Combine(appData, UsageReadLogStore.FileName), workspace.UsageReadsPath);
        Assert.True(File.Exists(workspace.UsageReadsPath));
        Assert.Same(recorder, provider.GetRequiredService<IUsageReadStatus>());
        Assert.NotNull(provider.GetRequiredService<IUsageReadStatus>().For(UsageSource.GitHub));
    }

    private static string Snapshot(string folder, params string[] files) =>
        string.Join(
            "\n",
            Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
                .Concat(files.Where(File.Exists))
                .Order(StringComparer.Ordinal)
                .Select(path => path + "=" + File.ReadAllText(path)));
}
