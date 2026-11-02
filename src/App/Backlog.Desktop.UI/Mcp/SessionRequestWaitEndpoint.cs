using System.Text.Json;
using System.Text.Json.Nodes;

using Backlog.Infrastructure.Mcp;
using Backlog.Modules.Sessions.Abstractions;
using Backlog.SharedKernel;

namespace Backlog.Desktop.UI.Mcp;

/// <summary>
/// The long poll a dispatcher waits on for session requests (local ADR 0038 §4):
/// <c>GET /session-requests/wait</c>, on the MCP listener beside <c>/telemetry</c> and
/// behind the same <c>Origin</c> check and bearer token.
/// <para>
/// It answers <c>200</c> as soon as there is work, and <c>204</c> once <see cref="Hold"/>
/// has passed without any. While <c>session-requests</c> is off it answers <c>404</c> at
/// once (§5). The work is one item whose <c>kind</c> says what it is:
/// <list type="bullet">
/// <item><c>delivery</c> — an answer delivery (local ADR 0041, Amends), Pending and not held
/// back by a release, for a session the Claude desktop app holds open:
/// <see cref="AnswerDeliveryPayload"/>. Deliveries come first, oldest answer first, because
/// their sessions already hold a worktree. One whose session the desktop app holds no record
/// of — a CLI session, an ended one — is never offered: nobody could send it.</item>
/// <item><c>request</c> — the oldest Requested session request, a lapsed claim included:
/// <see cref="SessionRequestPayload"/> with its own entry-or-plan kind as <c>requestKind</c>.</item>
/// </list>
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

    /// <summary>The <c>kind</c> a session request is handed out with.</summary>
    public const string RequestKind = "request";

    /// <summary>
    /// Waits for a delivery or the oldest Requested request, for up to <paramref name="hold"/>
    /// (<see cref="Hold"/> when null), asking the queue every
    /// <paramref name="pollInterval"/> (<see cref="PollInterval"/> when null).
    /// <para>
    /// The flag is read on arrival and again on every poll, so a call held when the
    /// flag goes off answers <c>404</c> rather than handing out work. A queue that cannot
    /// be read answers <c>503</c> with a <c>Retry-After</c>, so a dispatcher backs off
    /// rather than calling again at once. A caller that goes away ends the wait through
    /// <paramref name="cancellationToken"/>.
    /// </para>
    /// <para>
    /// The session list is read for a delivery's desktop session once per session per call,
    /// not on every poll: a delivery for a session read as having none waits for the next call.
    /// </para>
    /// </summary>
    /// <param name="questions">The waiting questions whose deliveries are handed out; null
    /// where the host composed no relay, and then only requests are.</param>
    /// <param name="sessions">The session list, for each delivery's desktop session id.</param>
    public static async Task<Reply> WaitAsync(
        ISessionRequestStore requests,
        IAppFeatureSettings features,
        TimeProvider clock,
        CancellationToken cancellationToken,
        TimeSpan? hold = null,
        TimeSpan? pollInterval = null,
        IWaitingQuestionStore? questions = null,
        IAgentSessionSource? sessions = null)
    {
        ArgumentNullException.ThrowIfNull(requests);
        ArgumentNullException.ThrowIfNull(features);
        ArgumentNullException.ThrowIfNull(clock);

        var interval = pollInterval ?? PollInterval;
        var deadline = clock.GetUtcNow() + (hold ?? Hold);
        var desktop = new DesktopSessionIds(sessions);
        Exception? deliveryFailure = null;

        while (true)
        {
            if (!features.IsEnabled(SessionFeatures.SessionRequests)) return new Reply(404, []);

            byte[]? body;

            try
            {
                body = await NextDeliveryAsync(questions, desktop, clock, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception failure) when (failure is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                // The deliveries could not be read: the session requests do not depend on
                // them, so the queue is still answered rather than held up behind them.
                deliveryFailure ??= failure;
                body = null;
            }

            try
            {
                body ??= await requests.NextAsync(cancellationToken).ConfigureAwait(false) is { } next
                    ? RequestItem(SessionRequestPayload.From(next, clock.GetUtcNow()))
                    : null;
            }
            catch (Exception failure) when (failure is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                return new Reply(503, [], failure, RetryAfterSeconds);
            }

            if (body is not null) return new Reply(200, body, deliveryFailure);

            var left = deadline - clock.GetUtcNow();

            if (left <= TimeSpan.Zero) return new Reply(204, [], deliveryFailure);

            await Task.Delay(left < interval ? left : interval, clock, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>The oldest due delivery whose session the desktop app holds open, as JSON,
    /// or null.</summary>
    private static async Task<byte[]?> NextDeliveryAsync(
        IWaitingQuestionStore? questions,
        DesktopSessionIds desktop,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        if (questions is null) return null;

        var now = clock.GetUtcNow();
        var due = WaitingQuestions.DueDeliveries(await questions.ListUndeliveredAsync(cancellationToken).ConfigureAwait(false), now);

        if (due.Count == 0) return null;

        var ids = await desktop.ResolveAsync(due.Select(question => question.SessionId), cancellationToken).ConfigureAwait(false);

        return due.FirstOrDefault(question => ids[question.SessionId] is not null) is { } first
            ? JsonSerializer.SerializeToUtf8Bytes(AnswerDeliveryPayload.From(first, ids[first.SessionId], now), Json)
            : null;
    }

    /// <summary>A session request as the route hands it out: <c>kind</c> <c>request</c>, and
    /// the request's own entry-or-plan kind as <c>requestKind</c>.</summary>
    private static byte[] RequestItem(SessionRequestPayload request)
    {
        var item = JsonSerializer.SerializeToNode(request, Json)!.AsObject();
        var requestKind = item["kind"]?.DeepClone();

        item.Remove("kind");

        var shaped = new JsonObject { ["kind"] = RequestKind, ["requestKind"] = requestKind };

        foreach (var (name, value) in item.ToList())
        {
            item.Remove(name);
            shaped[name] = value;
        }

        return JsonSerializer.SerializeToUtf8Bytes(shaped, Json);
    }
}
