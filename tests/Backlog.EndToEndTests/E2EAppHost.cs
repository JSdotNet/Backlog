using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text;

[assembly: AssemblyFixture(typeof(Backlog.EndToEndTests.E2EAppHost))]

namespace Backlog.EndToEndTests;

/// <summary>
/// The suite's global setup and teardown, per org ADR 0010 Testing
/// (<c>JSdotNet/app-defaults</c>, <c>defaults/arc42/adr/0010-testing.md</c> — not
/// the repository's own <c>guidelines/0010</c>): before the first test
/// it runs this worktree's AppHost with <c>aspire start --isolated</c>, detached,
/// and waits for the resources the tests drive; after the last it fails the run on
/// any Error log of any resource, then stops the AppHost again.
///
/// <para>Isolated, so Aspire assigns every port and the tests read each frontend's
/// URL from <c>aspire describe</c> (<see cref="AspireAppHost.UrlAsync"/>); no port
/// is written down anywhere. Several worktrees can run the suite at once.</para>
///
/// <para>An AppHost this worktree already runs is not started twice: the setup
/// refuses, since two instances would answer one <c>--apphost</c>.
/// <c>BACKLOG_E2E_ATTACH=1</c> uses that running AppHost instead, leaves it running
/// afterwards, and counts only the errors logged during the run.</para>
///
/// <para>Nothing here runs without <c>BACKLOG_E2E=1</c>, so the solution-wide
/// <c>dotnet test</c> and CI build the project and start nothing.</para>
/// </summary>
public sealed class E2EAppHost : IAsyncLifetime
{
    /// <summary>The resources a test drives or depends on. The frontends are the
    /// three harnesses a browser opens.</summary>
    internal static readonly string[] Frontends = [DesktopHarness.Resource, PhoneHarness.Resource, TabsKeyboardTests.Resource];

    private static readonly string[] Backends = ["cosmos", "storage", "sync"];

    private static readonly TimeSpan Healthy = TimeSpan.FromMinutes(10);

    private static readonly ConcurrentBag<ExpectedErrors> Expected = [];

    private AspireAppHost _appHost = null!;
    private bool _started;
    private long _errorsAfter;

    internal static bool Enabled => Environment.GetEnvironmentVariable("BACKLOG_E2E") == "1";

    private static bool Attach => Environment.GetEnvironmentVariable("BACKLOG_E2E_ATTACH") == "1";

    /// <summary>
    /// Declares the Error logs a test causes on purpose, so the run's check does
    /// not count them: those of <paramref name="resource"/> logged after
    /// <paramref name="afterId"/> and up to <paramref name="untilId"/> whose message
    /// <paramref name="matches"/> accepts. <paramref name="reason"/> goes into the
    /// evidence beside them.
    /// </summary>
    internal static void Expect(string resource, long afterId, long untilId, Func<AspireAppHost.LogEntry, bool> matches, string reason) =>
        Expected.Add(new ExpectedErrors(resource, afterId, untilId, matches, reason));

    /// <summary>The Error logs in <paramref name="logsJson"/> newer than
    /// <paramref name="afterId"/> that no test declared, oldest first.</summary>
    internal static IReadOnlyList<AspireAppHost.LogEntry> Unexpected(string logsJson, long afterId) =>
        AspireAppHost.Logs(logsJson)
            .Where(log => log.Id > afterId && !Expected.Any(expected => expected.Covers(log)))
            .ToList();

    public async ValueTask InitializeAsync()
    {
        if (!Enabled) return;

        _appHost = AspireAppHost.ForThisWorktree();
        using var deadline = new CancellationTokenSource(Healthy + TimeSpan.FromMinutes(15));
        var cancellationToken = deadline.Token;

        if (await _appHost.IsRunningAsync(cancellationToken))
        {
            if (!Attach)
            {
                throw new InvalidOperationException(
                    "This worktree's AppHost is already running. Stop it (aspire stop --apphost src\\Aspire\\Backlog.Aspire.AppHost\\Backlog.Aspire.AppHost.csproj) "
                    + "so the suite starts its own, or set BACKLOG_E2E_ATTACH=1 to run against it.");
            }

            _errorsAfter = AspireAppHost.LogIds(await _appHost.LogsJsonAsync(null, "Error", cancellationToken)).DefaultIfEmpty(0).Max();
        }
        else
        {
            await _appHost.StartAppHostAsync(cancellationToken);
            _started = true;
        }

        try
        {
            foreach (var resource in Backends.Concat(Frontends))
            {
                await _appHost.WaitHealthyAsync(resource, Healthy, cancellationToken);
            }

            // Proves every frontend publishes an address before a test asks for one.
            foreach (var frontend in Frontends)
            {
                _ = await _appHost.UrlAsync(frontend, cancellationToken);
            }

            await WaitUntilSyncReachesCosmosAsync(cancellationToken);

            // Until then sync answers 503 by design (CosmosTaskReplica), and a
            // harness that synced meanwhile logged its retries at Error.
            var ready = await _appHost.LastLogIdAsync(cancellationToken);
            foreach (var harness in (string[])[DesktopHarness.Resource, PhoneHarness.Resource])
            {
                Expect(
                    harness,
                    _errorsAfter,
                    ready,
                    log => log.Source == "Polly" && log.Message.Contains("Result: '503'", StringComparison.Ordinal),
                    "cold start: sync answers 503 until the Cosmos emulator has its containers");
            }
        }
        catch when (_started)
        {
            // A setup that fails leaves nothing running for the next run to refuse.
            await StopStartedAsync();
            throw;
        }
    }

    /// <summary>
    /// Waits for sync to answer from Cosmos rather than 503. The emulator reports
    /// healthy minutes before its containers answer, and the AppHost deliberately
    /// starts sync without waiting for it, so <c>healthy</c> is not ready. A
    /// well-formed pairing code nobody issued is looked up in the pairing-code
    /// container: 404 once that answers, 503 <c>sync.replica_unavailable</c> until
    /// then. It changes nothing.
    /// </summary>
    private async Task WaitUntilSyncReachesCosmosAsync(CancellationToken cancellationToken)
    {
        var sync = await _appHost.UrlAsync("sync", cancellationToken);
        using var http = new HttpClient { BaseAddress = sync, Timeout = TimeSpan.FromSeconds(30) };
        var last = "no answer";

        await Interactive.EventuallyAsync(
            async () =>
            {
                try
                {
                    using var answer = await http.PostAsJsonAsync(
                        "api/sync/devices/pair",
                        new { code = "22222222", deviceName = "e2e-readiness-probe" },
                        cancellationToken);
                    last = $"{(int)answer.StatusCode} {answer.StatusCode}";
                    if (answer.StatusCode == HttpStatusCode.NotFound) return true;
                }
                catch (HttpRequestException exception)
                {
                    last = exception.Message;
                }

                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
                return false;
            },
            Healthy,
            "sync to answer the readiness probe from Cosmos (404) rather than 503");
        TestContext.Current.SendDiagnosticMessage($"sync reaches Cosmos: {last}");
    }

    private async Task StopStartedAsync()
    {
        if (!_started) return;

        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(4));
        await _appHost.StopAppHostAsync(deadline.Token);
        _started = false;
    }

    public async ValueTask DisposeAsync()
    {
        if (!Enabled) return;

        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        var cancellationToken = deadline.Token;

        try
        {
            var errors = await _appHost.LogsJsonAsync(null, "Error", cancellationToken);
            var evidence = Evidence.For("run");
            await evidence.WriteAsync("errors.json", errors);

            var unexpected = Unexpected(errors, _errorsAfter);
            var expected = AspireAppHost.Logs(errors).Where(log => log.Id > _errorsAfter).Except(unexpected).ToList();
            var report = new StringBuilder();
            report.AppendLine($"Unexpected Error logs: {unexpected.Count}");
            foreach (var log in unexpected) report.AppendLine($"  {log}");
            report.AppendLine($"Expected Error logs: {expected.Count}");
            foreach (var log in expected)
            {
                report.AppendLine($"  {log}  [{Expected.First(declared => declared.Covers(log)).Reason}]");
            }

            await evidence.WriteAsync("errors.log", report.ToString());

            if (unexpected.Count > 0)
            {
                throw new InvalidOperationException(
                    $"The run logged {unexpected.Count} Error log(s) no test expected; see {Path.Combine(evidence.Folder, "errors.log")}:{Environment.NewLine}"
                    + string.Join(Environment.NewLine, unexpected.Take(20)));
            }
        }
        finally
        {
            // Its own deadline: a check that used up the one above still stops the host.
            await StopStartedAsync();
        }
    }

    private sealed record ExpectedErrors(string Resource, long AfterId, long UntilId, Func<AspireAppHost.LogEntry, bool> Matches, string Reason)
    {
        public bool Covers(AspireAppHost.LogEntry log) =>
            log.Resource == Resource && log.Id > AfterId && log.Id <= UntilId && Matches(log);
    }
}
