using Backlog.Infrastructure.FileSystem.Logging;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace Backlog.Infrastructure.FileSystem.UnitTests;

/// <summary>
/// The log file an installed build keeps, so an exception a background loop
/// swallowed can still be read the next morning. Real temp directories, for
/// the reason <see cref="CaptureSourcesSettingsStoreTests"/> gives: the file
/// is the thing under test.
/// </summary>
public sealed class FileLoggerProviderTests : IDisposable
{
    /// <summary>08:47 UTC, which the two-hour zone below shows as 10:47 — the
    /// line carries local time, because that is the clock the person reading
    /// the file has in front of them.</summary>
    private static readonly DateTimeOffset Morning = new(2026, 9, 17, 8, 47, 12, 123, TimeSpan.Zero);

    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "file-logger-provider-tests-" + Guid.NewGuid().ToString("N"));

    private readonly FakeTimeProvider _clock = new(Morning);

    public FileLoggerProviderTests() => _clock.SetLocalTimeZone(TimeZoneInfo.CreateCustomTimeZone("Test", TimeSpan.FromHours(2), "Test", "Test"));

    [Fact]
    public void A_line_carries_when_where_and_what_and_the_exception_underneath()
    {
        using var provider = new FileLoggerProvider(_root, _clock);
        var log = provider.CreateLogger("Backlog.Infrastructure.Sync.TaskSyncWorker");

        log.LogError(new InvalidOperationException("No endpoints found for service 'sync'."), "A task sync cycle threw.");

        var text = File.ReadAllText(Path.Combine(_root, "backlog-20260917.log"));
        Assert.StartsWith("2026-09-17 10:47:12.123 +02:00 [ERR] Backlog.Infrastructure.Sync.TaskSyncWorker: A task sync cycle threw.", text, StringComparison.Ordinal);
        Assert.Contains("System.InvalidOperationException: No endpoints found for service 'sync'.", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Lines_append_and_a_new_day_starts_a_new_file()
    {
        using var provider = new FileLoggerProvider(_root, _clock);
        var log = provider.CreateLogger("Backlog.Test");

        log.LogWarning("first");
        log.LogWarning("second");
        _clock.Advance(TimeSpan.FromDays(1));
        log.LogWarning("third");

        var today = File.ReadAllLines(Path.Combine(_root, "backlog-20260917.log"));
        var tomorrow = File.ReadAllLines(Path.Combine(_root, "backlog-20260918.log"));
        Assert.Equal(2, today.Length);
        Assert.EndsWith("[WRN] Backlog.Test: second", today[1], StringComparison.Ordinal);
        Assert.Single(tomorrow);
    }

    /// <summary>Two weeks is plenty to come back to a failure; a folder that
    /// only ever grew would be a leak with a date on it.</summary>
    [Fact]
    public void Files_older_than_the_retention_are_removed_when_the_provider_starts()
    {
        Directory.CreateDirectory(_root);
        var stale = Path.Combine(_root, "backlog-20260801.log");
        var recent = Path.Combine(_root, "backlog-20260910.log");
        var unrelated = Path.Combine(_root, "notes.txt");
        File.WriteAllText(stale, "old");
        File.WriteAllText(recent, "recent");
        File.WriteAllText(unrelated, "keep");

        using var provider = new FileLoggerProvider(_root, _clock);

        Assert.False(File.Exists(stale));
        Assert.True(File.Exists(recent));
        Assert.True(File.Exists(unrelated));
    }

    /// <summary>
    /// The registration filters: the app's own categories from Information up,
    /// because "a task sync cycle did not complete" is logged there and is the
    /// line a person wants; everything else from Warning, because the framework
    /// narrates every request at Information and the file is for failures.
    /// </summary>
    [Fact]
    public void The_registration_keeps_the_apps_own_information_and_only_others_warnings()
    {
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(_clock);
        services.AddLogging(logging => logging.AddFileLogging(_root));
        using var host = services.BuildServiceProvider();
        var loggers = host.GetRequiredService<ILoggerFactory>();

        loggers.CreateLogger("Backlog.Infrastructure.Sync.TaskSyncWorker").LogInformation("kept");
        loggers.CreateLogger("System.Net.Http.HttpClient.sync.LogicalHandler").LogInformation("dropped");
        loggers.CreateLogger("System.Net.Http.HttpClient.sync.LogicalHandler").LogWarning("kept too");
        loggers.CreateLogger("Backlog.Test").LogDebug("dropped too");

        Assert.Equal(_root, host.GetRequiredService<AppLogLocation>().Directory);
        var lines = File.ReadAllLines(Path.Combine(_root, "backlog-20260917.log"));
        Assert.Equal(2, lines.Length);
        Assert.EndsWith("kept", lines[0], StringComparison.Ordinal);
        Assert.EndsWith("kept too", lines[1], StringComparison.Ordinal);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
