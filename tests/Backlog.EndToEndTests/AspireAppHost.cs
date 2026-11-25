using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Backlog.EndToEndTests;

/// <summary>
/// This worktree's AppHost, reached through the Aspire CLI.
///
/// <para>No test project may reference the AppHost (see <c>AspireAppModelTests</c>),
/// so the suite drives it from outside, as org ADR 0010 Testing
/// (<c>JSdotNet/app-defaults</c>, <c>defaults/arc42/adr/0010-testing.md</c>) says an E2E suite does:
/// <see cref="E2EAppHost"/> runs <c>aspire start --isolated</c> detached before the
/// first test and <c>aspire stop</c> after the last. What the CLI gives a test is
/// enough: every resource's URL, stop and start for the resource a scenario takes
/// away, and the dashboard's telemetry to prove what happened on the wire.</para>
///
/// <para>Every call names this worktree's AppHost with <c>--apphost</c>. Several
/// worktrees run one each on a developer machine, and a CLI left to pick one
/// answers about whichever it found first.</para>
/// </summary>
internal sealed class AspireAppHost
{
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromMinutes(2);

    private readonly string _appHostProject;

    public AspireAppHost(string appHostProject) => _appHostProject = appHostProject;

    /// <summary>The AppHost under <see cref="RepositoryRoot"/>.</summary>
    public static AspireAppHost ForThisWorktree() =>
        new(RepositoryRoot.File("src", "Aspire", "Backlog.Aspire.AppHost", "Backlog.Aspire.AppHost.csproj"));

    /// <summary>Whether <c>aspire ps</c> lists this worktree's AppHost as running.</summary>
    public async Task<bool> IsRunningAsync(CancellationToken cancellationToken)
    {
        using var running = JsonDocument.Parse(await RunAsync(["ps", "--format", "Json"], cancellationToken, scoped: false));
        var project = Path.GetFullPath(_appHostProject);

        return running.RootElement.EnumerateArray().Any(appHost =>
            appHost.TryGetProperty("appHostPath", out var path)
            && string.Equals(Path.GetFullPath(path.GetString() ?? ""), project, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Starts the AppHost in the background, isolated: Aspire picks its ports and
    /// keeps its user secrets apart, so another worktree's run never collides with
    /// this one. The command builds the AppHost and returns once it is launched,
    /// not once its resources are up; <see cref="WaitHealthyAsync"/> is that.
    /// </summary>
    public Task StartAppHostAsync(CancellationToken cancellationToken) =>
        RunAsync(["start", "--isolated", "--format", "Json"], cancellationToken, TimeSpan.FromMinutes(10));

    /// <summary>Stops this worktree's AppHost, and only it.</summary>
    public Task StopAppHostAsync(CancellationToken cancellationToken) =>
        RunAsync(["stop"], cancellationToken, TimeSpan.FromMinutes(3));

    /// <summary>The first HTTP URL a resource publishes, by its display name, as
    /// <c>aspire describe</c> answers it — Aspire assigned the port.</summary>
    public async Task<Uri> UrlAsync(string resource, CancellationToken cancellationToken) =>
        (await UrlsAsync(resource, cancellationToken)).FirstOrDefault(url => url.Scheme == Uri.UriSchemeHttp)
        ?? throw new InvalidOperationException(
            $"'{resource}' publishes no HTTP URL. Is this worktree's AppHost running (aspire start --isolated)?");

    /// <summary>Every <c>host:port</c> a resource answers on.</summary>
    private async Task<IReadOnlyList<string>> AuthoritiesAsync(string resource, CancellationToken cancellationToken) =>
        (await UrlsAsync(resource, cancellationToken)).Select(url => url.Authority).Distinct().ToList();

    private async Task<IReadOnlyList<Uri>> UrlsAsync(string resource, CancellationToken cancellationToken)
    {
        using var described = JsonDocument.Parse(
            await RunAsync(["describe", resource, "--format", "Json"], cancellationToken));

        var urls = new List<Uri>();
        foreach (var entry in described.RootElement.GetProperty("resources").EnumerateArray())
        {
            if (entry.GetProperty("displayName").GetString() != resource) continue;

            foreach (var url in entry.GetProperty("urls").EnumerateArray())
            {
                if (Uri.TryCreate(url.GetProperty("url").GetString(), UriKind.Absolute, out var value)) urls.Add(value);
            }
        }

        return urls;
    }

    public Task StopAsync(string resource, CancellationToken cancellationToken) =>
        CommandThenWaitAsync(resource, "stop", "down", cancellationToken);

    public Task StartAsync(string resource, CancellationToken cancellationToken) =>
        CommandThenWaitAsync(resource, "start", "healthy", cancellationToken);

    /// <summary>Runs one of the AppHost's own resource commands, then waits for the
    /// resource to reach <paramref name="status"/>.</summary>
    public Task CommandAsync(string resource, string command, string status, CancellationToken cancellationToken) =>
        CommandThenWaitAsync(resource, command, status, cancellationToken);

    /// <summary>Waits for a resource to be healthy — the Cosmos emulator takes
    /// minutes on a cold start, which is why the timeout is generous.</summary>
    public Task WaitHealthyAsync(string resource, TimeSpan timeout, CancellationToken cancellationToken) =>
        RunAsync(
            ["wait", resource, "--status", "healthy", "--timeout", ((int)timeout.TotalSeconds).ToString(CultureInfo.InvariantCulture)],
            cancellationToken,
            timeout + CommandTimeout);

    /// <summary>
    /// The HTTP calls one resource made to another, oldest first, as the caller's
    /// client spans recorded them — the order a client sent things in is a fact
    /// about the client, so it is read off the client.
    /// </summary>
    public async Task<IReadOnlyList<Span>> CallsAsync(string from, string to, CancellationToken cancellationToken)
    {
        // The dashboard names the destination by its resource only when it can
        // match the peer to one; an isolated run's spans carry "localhost:<port>"
        // instead. Either names the callee.
        var destinations = (await AuthoritiesAsync(to, cancellationToken)).Append(to).ToHashSet(StringComparer.OrdinalIgnoreCase);

        using var spans = JsonDocument.Parse(
            await RunAsync(["otel", "spans", from, "--format", "Json", "--limit", "2000"], cancellationToken));

        return spans.RootElement.EnumerateArray()
            .Where(span => span.GetProperty("kind").GetString() == "Client"
                           && span.TryGetProperty("source", out var source) && source.GetString() == from
                           && span.TryGetProperty("destination", out var destination) && destinations.Contains(destination.GetString() ?? ""))
            .Select(Span.From)
            .OrderBy(span => span.Timestamp)
            .ToList();
    }

    /// <summary>The structured logs at or above a severity, of one resource or, with
    /// none named, of every resource, as the raw JSON the CLI answered, so it can be
    /// kept as evidence unchanged. The CLI answers the newest <c>--limit</c>.</summary>
    public Task<string> LogsJsonAsync(string? resource, string minimumSeverity, CancellationToken cancellationToken) =>
        RunAsync(
            ["otel", "logs", .. resource is null ? Array.Empty<string>() : [resource], "--format", "Json", "--severity", minimumSeverity, "--limit", "5000"],
            cancellationToken);

    /// <summary>The ids of those logs, which only ever grow — what "no new error"
    /// is measured against.</summary>
    public static IReadOnlyList<long> LogIds(string logsJson) => Logs(logsJson).Select(log => log.Id).ToList();

    /// <summary>Those logs, oldest first. The CLI's JSON carries no timestamp, so a
    /// window in time is a window of ids.</summary>
    public static IReadOnlyList<LogEntry> Logs(string logsJson)
    {
        using var logs = JsonDocument.Parse(logsJson);
        return logs.RootElement.EnumerateArray()
            .Select(log => new LogEntry(
                log.GetProperty("logId").GetInt64(),
                log.TryGetProperty("resourceName", out var resource) ? resource.GetString() ?? "" : "",
                log.TryGetProperty("severity", out var severity) ? severity.GetString() ?? "" : "",
                log.TryGetProperty("source", out var source) ? source.GetString() ?? "" : "",
                log.TryGetProperty("message", out var message) ? message.GetString() ?? "" : ""))
            .OrderBy(log => log.Id)
            .ToList();
    }

    /// <summary>The newest log id of any resource at any severity — the mark a
    /// window of expected logs starts or ends at.</summary>
    public async Task<long> LastLogIdAsync(CancellationToken cancellationToken) =>
        LogIds(await RunAsync(["otel", "logs", "--format", "Json", "--limit", "1"], cancellationToken)).DefaultIfEmpty(0).Max();

    private async Task CommandThenWaitAsync(string resource, string command, string status, CancellationToken cancellationToken)
    {
        await RunAsync(["resource", resource, command], cancellationToken);
        await RunAsync(["wait", resource, "--status", status, "--timeout", "180"], cancellationToken, TimeSpan.FromMinutes(4));
    }

    private async Task<string> RunAsync(string[] arguments, CancellationToken cancellationToken, TimeSpan? timeout = null, bool scoped = true)
    {
        var start = new ProcessStartInfo("aspire")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            StandardOutputEncoding = Encoding.UTF8,
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        if (scoped) foreach (var argument in (string[])["--apphost", _appHostProject]) start.ArgumentList.Add(argument);
        foreach (var argument in (string[])["--non-interactive", "--nologo"]) start.ArgumentList.Add(argument);

        using var process = Process.Start(start)
            ?? throw new InvalidOperationException("Could not start the Aspire CLI. Is `aspire` on PATH?");
        process.StandardInput.Close();

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout ?? CommandTimeout);

        var output = process.StandardOutput.ReadToEndAsync(deadline.Token);
        var error = process.StandardError.ReadToEndAsync(deadline.Token);
        try
        {
            await process.WaitForExitAsync(deadline.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"aspire {string.Join(' ', arguments)} did not finish in time.");
        }

        var text = await output;
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"aspire {string.Join(' ', arguments)} exited {process.ExitCode}:{Environment.NewLine}{text}{await error}");
        }

        return text;
    }

    /// <summary>One structured log, as much of it as a check reads.</summary>
    internal sealed record LogEntry(long Id, string Resource, string Severity, string Source, string Message)
    {
        public override string ToString() => $"#{Id} {Resource} {Severity} {Source}: {Message}";
    }

    /// <summary>One HTTP call: what was asked, how it was answered, and when. A
    /// call that never got an answer — the service was down — has status 0.</summary>
    internal sealed record Span(string Method, string Path, int StatusCode, DateTimeOffset Timestamp)
    {
        public static Span From(JsonElement span)
        {
            var attributes = span.GetProperty("attributes");

            var status = 0;
            if (attributes.TryGetProperty("http.response.status_code", out var code))
            {
                // The dashboard answers "201 Created", not 201.
                var digits = new string((code.GetString() ?? "").TakeWhile(char.IsDigit).ToArray());
                _ = int.TryParse(digits, CultureInfo.InvariantCulture, out status);
            }

            var path = attributes.TryGetProperty("url.full", out var url) && Uri.TryCreate(url.GetString(), UriKind.Absolute, out var parsed)
                ? parsed.AbsolutePath
                : "";

            return new Span(
                span.GetProperty("name").GetString() ?? "",
                path,
                status,
                DateTimeOffset.Parse(span.GetProperty("timestamp").GetString()!, CultureInfo.InvariantCulture));
        }

        public bool Succeeded => StatusCode is >= 200 and < 300;

        public override string ToString() =>
            $"{Timestamp:HH:mm:ss.fff} {Method} {Path} {StatusCode}";
    }
}
