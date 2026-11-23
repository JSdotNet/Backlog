using System.Text.Json;
using System.Text.Json.Serialization;

using Backlog.Infrastructure.Mcp;
using Backlog.Modules.Sessions.Abstractions;
using Backlog.SharedKernel;

namespace Backlog.Desktop.UI.Mcp;

/// <summary>
/// The route a scheduled run's hook posts its report to (local ADR 0051 §1):
/// <c>POST /schedule-runs</c>, on the MCP listener beside <c>/session-requests/wait</c> and
/// behind the same <c>Origin</c> check and bearer token. The same report
/// <c>report_schedule_run</c> takes, as JSON — <c>scheduleId</c>, <c>sessionId</c>,
/// <c>event</c>, and <c>title</c>, <c>repository</c>, <c>cron</c>, <c>verdict</c>,
/// <c>needsYou</c> and <c>heldBy</c> where they are known — recorded through
/// <see cref="ScheduleRunReporting"/>, so the two record and answer alike.
/// <para>
/// It answers <c>200</c> with the schedule's state as the tool answers it, <c>400</c> for a
/// body it cannot read or a report missing its ids, <c>413</c> for a body over
/// <see cref="MaxBodyBytes"/>, and <c>503</c> with a <c>Retry-After</c> when the register
/// cannot be written. While <c>sessions</c> is off it answers <c>404</c> and records
/// nothing (§6).
/// </para>
/// <para>
/// A route as well as a tool because a hook is a script, not a model: a Stop hook that had
/// to ask the session to call a tool would cost the run a turn after it had finished.
/// </para>
/// </summary>
public static class ScheduleRunReportEndpoint
{
    /// <summary>Where the route is served, under the listener's address.</summary>
    public const string RoutePath = "/schedule-runs";

    /// <summary>The largest body read: a report is a few lines, and a summary pasted whole is not one.</summary>
    public const int MaxBodyBytes = 256 * 1024;

    /// <summary>How long a hook is asked to wait after a <c>503</c>.</summary>
    public const int RetryAfterSeconds = 5;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>What to send: the status code, the JSON body, and with a <c>503</c> the
    /// failure for the head to log and the seconds for a <c>Retry-After</c>.</summary>
    public sealed record Reply(int StatusCode, byte[] Body, Exception? Failure = null, int? RetryAfterSeconds = null)
    {
        public const string ContentType = "application/json; charset=utf-8";
    }

    /// <summary>The report's wire shape.</summary>
    private sealed record Body(
        [property: JsonPropertyName("scheduleId")] string? ScheduleId,
        [property: JsonPropertyName("sessionId")] string? SessionId,
        [property: JsonPropertyName("event")] string? Event,
        [property: JsonPropertyName("title")] string? Title,
        [property: JsonPropertyName("repository")] string? Repository,
        [property: JsonPropertyName("cron")] string? Cron,
        [property: JsonPropertyName("verdict")] string? Verdict,
        [property: JsonPropertyName("needsYou")] string[]? NeedsYou,
        [property: JsonPropertyName("heldBy")] string? HeldBy);

    /// <summary>Reads, records and answers one report.</summary>
    /// <param name="sessions">The session list, for whether a run's session is archived.</param>
    /// <param name="zone">The zone the crons are evaluated in; this machine's when null.</param>
    public static async Task<Reply> AcceptAsync(
        Stream body,
        long? contentLength,
        IScheduleRegister register,
        IAppFeatureSettings features,
        IAgentSessionSource? sessions,
        TimeProvider clock,
        CancellationToken cancellationToken,
        TimeZoneInfo? zone = null)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(register);
        ArgumentNullException.ThrowIfNull(features);
        ArgumentNullException.ThrowIfNull(clock);

        if (!features.IsEnabled(SessionFeatures.Sessions)) return new Reply(404, []);
        if (contentLength > MaxBodyBytes) return Refused(413, "The report is larger than a report is.");

        Body? read;

        try
        {
            using var buffer = new MemoryStream();
            var chunk = new byte[16 * 1024];
            int count;

            while ((count = await body.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) > 0)
            {
                if (buffer.Length + count > MaxBodyBytes) return Refused(413, "The report is larger than a report is.");

                buffer.Write(chunk, 0, count);
            }

            read = buffer.Length == 0 ? null : JsonSerializer.Deserialize<Body>(buffer.ToArray(), Json);
        }
        catch (JsonException)
        {
            return Refused(400, "The body is not a JSON report.");
        }

        if (read is null) return Refused(400, "The body is not a JSON report.");

        if (!ScheduleRunReporting.TryEvent(read.Event, out var kind))
        {
            return Refused(400, $"event must be started, skipped, finished or failed, not '{read.Event}'.");
        }

        var report = new ScheduledRunReport(
            read.ScheduleId ?? string.Empty,
            read.Title,
            read.Repository,
            read.Cron,
            read.SessionId ?? string.Empty,
            kind,
            read.Verdict,
            read.NeedsYou,
            read.HeldBy);

        ScheduleRunReporting.Outcome outcome;

        try
        {
            outcome = await ScheduleRunReporting.RecordAsync(report, register, sessions, clock, zone ?? TimeZoneInfo.Local, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception failure) when (failure is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            return new Reply(503, [], failure, RetryAfterSeconds);
        }

        return outcome.Payload is { } payload
            ? new Reply(200, JsonSerializer.SerializeToUtf8Bytes(payload, Json))
            : Refused(400, outcome.Refusal ?? "The report was refused.");
    }

    private static Reply Refused(int statusCode, string reason) =>
        new(statusCode, JsonSerializer.SerializeToUtf8Bytes(new { error = reason }, Json));
}
