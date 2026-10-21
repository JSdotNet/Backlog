using System.Text.Json;

using Backlog.Infrastructure.Mcp;
using Backlog.Modules.Sessions.Abstractions;
using Backlog.SharedKernel;

namespace Backlog.Desktop.UI.Mcp;

/// <summary>
/// The long poll a dispatcher waits on for session requests (local ADR 0037 §4):
/// <c>GET /session-requests/wait</c>, on the MCP listener beside <c>/telemetry</c> and
/// behind the same <c>Origin</c> check and bearer token.
/// <para>
/// It answers <c>200</c> with the oldest Requested request as soon as there is one —
/// a lapsed claim counts — and <c>204</c> once <see cref="Hold"/> has passed without one.
/// While <c>session-requests</c> is off it answers <c>404</c> at once (§5).
/// </para>
/// <para>
/// <b>It hands a request out; it does not claim it.</b> Two dispatchers waiting at once
/// may both be answered with the same request, and the claim tool decides which of them
/// starts it. That keeps the route a read, and a dispatcher that dies between the answer
/// and the claim leaves nothing to clean up.
/// </para>
/// <para>
/// The queue is asked every <see cref="PollInterval"/> while the call is held. The store
/// raises no change event, and one indexed read a second against the local database
/// costs nothing a person would notice; what the long poll saves is the dispatcher's
/// model turn, not the server's read.
/// </para>
/// <para>
/// Here rather than in either head because both map it and the handling is what is
/// worth testing, the arrangement <see cref="BacklogTelemetryEndpoint"/> describes.
/// </para>
/// </summary>
public static class SessionRequestWaitEndpoint
{
    /// <summary>Where the route is served, under the listener's address.</summary>
    public const string RoutePath = "/session-requests/wait";

    /// <summary>How long a call is held open before it answers <c>204</c>.</summary>
    public static readonly TimeSpan Hold = TimeSpan.FromSeconds(50);

    /// <summary>How often the queue is asked while a call is held.</summary>
    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>How long a dispatcher is asked to wait after a <c>503</c>.</summary>
    public const int RetryAfterSeconds = 5;

    /// <summary>What to send: the status code, the request as JSON with a <c>200</c>,
    /// and with a <c>503</c> the failure for the head to log and the seconds for a
    /// <c>Retry-After</c>.</summary>
    public sealed record Reply(int StatusCode, byte[] Body, Exception? Failure = null, int? RetryAfterSeconds = null)
    {
        public const string ContentType = "application/json; charset=utf-8";
    }

    /// <summary>
    /// Waits for the oldest Requested request, for up to <paramref name="hold"/>
    /// (<see cref="Hold"/> when null), asking the queue every
    /// <paramref name="pollInterval"/> (<see cref="PollInterval"/> when null).
    /// <para>
    /// The flag is read on arrival and again on every poll, so a call held when the
    /// flag goes off answers <c>404</c> rather than handing out work. A queue that cannot
    /// be read answers <c>503</c> with a <c>Retry-After</c>, so a dispatcher backs off
    /// rather than calling again at once. A caller that goes away ends the wait through
    /// <paramref name="cancellationToken"/>.
    /// </para>
    /// </summary>
    public static async Task<Reply> WaitAsync(
        ISessionRequestStore requests,
        IAppFeatureSettings features,
        TimeProvider clock,
        CancellationToken cancellationToken,
        TimeSpan? hold = null,
        TimeSpan? pollInterval = null)
    {
        ArgumentNullException.ThrowIfNull(requests);
        ArgumentNullException.ThrowIfNull(features);
        ArgumentNullException.ThrowIfNull(clock);

        var interval = pollInterval ?? PollInterval;
        var deadline = clock.GetUtcNow() + (hold ?? Hold);

        while (true)
        {
            if (!features.IsEnabled(SessionFeatures.SessionRequests)) return new Reply(404, []);

            SessionRequestPayload? payload;

            try
            {
                payload = await requests.NextAsync(cancellationToken).ConfigureAwait(false) is { } next
                    ? SessionRequestPayload.From(next, clock.GetUtcNow())
                    : null;
            }
            catch (Exception failure) when (failure is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                return new Reply(503, [], failure, RetryAfterSeconds);
            }

            if (payload is not null) return new Reply(200, JsonSerializer.SerializeToUtf8Bytes(payload, Json));

            var left = deadline - clock.GetUtcNow();

            if (left <= TimeSpan.Zero) return new Reply(204, []);

            await Task.Delay(left < interval ? left : interval, clock, cancellationToken).ConfigureAwait(false);
        }
    }
}
