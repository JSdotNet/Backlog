using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Backlog.EndToEndTests;

/// <summary>
/// This worktree's running AppHost, reached through the Aspire CLI.
///
/// <para>The tests never start the AppHost themselves. No test project may
/// reference it (see <c>AspireAppModelTests</c>), and starting the whole app model
/// is a person's decision anyway: it takes a Cosmos emulator, minutes of warm-up,
/// and it is the same `aspire start --isolated` every other QA run on this
/// machine uses. What the CLI gives a test instead is enough: every resource's
/// URL, stop and start for the resource a scenario takes away, and the dashboard's
/// telemetry to prove what happened on the wire.</para>
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

    /// <summary>The first HTTP URL a resource publishes, by its display name.</summary>
    public async Task<Uri> UrlAsync(string resource, CancellationToken cancellationToken)
    {
        using var described = JsonDocument.Parse(
            await RunAsync(["describe", resource, "--format", "Json"], cancellationToken));

        foreach (var entry in described.RootElement.GetProperty("resources").EnumerateArray())
        {
            if (entry.GetProperty("displayName").GetString() != resource) continue;

            foreach (var url in entry.GetProperty("urls").EnumerateArray())
            {
                var value = url.GetProperty("url").GetString();
                if (value is not null && value.StartsWith("http://", StringComparison.Ordinal)) return new Uri(value);
            }
        }

        throw new InvalidOperationException(
            $"'{resource}' publishes no HTTP URL. Is this worktree's AppHost running (aspire start --isolated)?");
    }

    public Task StopAsync(string resource, CancellationToken cancellationToken) =>
        CommandThenWaitAsync(resource, "stop", "down", cancellationToken);

    public Task StartAsync(string resource, CancellationToken cancellationToken) =>
        CommandThenWaitAsync(resource, "start", "healthy", cancellationToken);

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
        using var spans = JsonDocument.Parse(
            await RunAsync(["otel", "spans", from, "--format", "Json", "--limit", "2000"], cancellationToken));

        return spans.RootElement.EnumerateArray()
            .Where(span => span.GetProperty("kind").GetString() == "Client"
                           && span.TryGetProperty("source", out var source) && source.GetString() == from
                           && span.TryGetProperty("destination", out var destination) && destination.GetString() == to)
            .Select(Span.From)
            .OrderBy(span => span.Timestamp)
            .ToList();
    }

    /// <summary>The structured logs of one resource at or above a severity, as the
    /// raw JSON the CLI answered, so it can be kept as evidence unchanged.</summary>
    public Task<string> LogsJsonAsync(string resource, string minimumSeverity, CancellationToken cancellationToken) =>
        RunAsync(["otel", "logs", resource, "--format", "Json", "--severity", minimumSeverity, "--limit", "5000"], cancellationToken);

    /// <summary>The ids of those logs, which only ever grow — what "no new error"
    /// is measured against.</summary>
    public static IReadOnlyList<long> LogIds(string logsJson)
    {
        using var logs = JsonDocument.Parse(logsJson);
        return logs.RootElement.EnumerateArray().Select(log => log.GetProperty("logId").GetInt64()).ToList();
    }

    private async Task CommandThenWaitAsync(string resource, string command, string status, CancellationToken cancellationToken)
    {
        await RunAsync(["resource", resource, command], cancellationToken);
        await RunAsync(["wait", resource, "--status", status, "--timeout", "180"], cancellationToken, TimeSpan.FromMinutes(4));
    }

    private async Task<string> RunAsync(string[] arguments, CancellationToken cancellationToken, TimeSpan? timeout = null)
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
        foreach (var argument in (string[])["--apphost", _appHostProject, "--non-interactive", "--nologo"]) start.ArgumentList.Add(argument);

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
