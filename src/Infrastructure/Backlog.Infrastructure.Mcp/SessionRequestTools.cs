using System.ComponentModel;

using Backlog.Modules.Sessions.Abstractions;
using Backlog.SharedKernel.Results;

using ModelContextProtocol.Server;

namespace Backlog.Infrastructure.Mcp;

/// <summary>
/// The dispatcher tools of local ADR 0038 §4: claim a session request, report what came
/// of it, and list the queue.
/// <para>
/// Its own group (<see cref="BacklogMcpTools.SessionRequests"/>) behind
/// <c>session-requests</c>, so with the flag off the three leave <c>tools/list</c> and a
/// call to one is an unknown tool. The wait itself is not a tool: it is the
/// <c>GET /session-requests/wait</c> route beside <c>/telemetry</c>, because a tool call
/// held open for a minute would tie up the stateless transport.
/// </para>
/// <para>
/// <b>No tool here creates a request or takes a brief.</b> Only a click in the app
/// queues one, and the brief every answer carries is built by
/// <see cref="SessionRequests.BriefFor"/> from the kind and the id alone.
/// </para>
/// </summary>
/// <param name="requests">This machine's session request queue.</param>
/// <param name="clock">What "now" is, for the state each request reads as; optional so
/// a test can pin it.</param>
[McpServerToolType]
public sealed class SessionRequestTools(ISessionRequestStore requests, TimeProvider? clock = null)
{
    internal const string ClaimSessionRequest = "claim_session_request";
    internal const string MarkSessionRequest = "mark_session_request";
    internal const string ListSessionRequests = "list_session_requests";

    /// <summary>The <c>outcome</c> that reports the session a dispatcher started.</summary>
    internal const string Started = "started";

    /// <summary>The <c>outcome</c> that gives a claim back without starting anything.</summary>
    internal const string Released = "released";

    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    [McpServerTool(Name = ClaimSessionRequest, Title = "Claim a session request", ReadOnly = false, Idempotent = false, Destructive = false, OpenWorld = false)]
    [Description(
        "Claims one Requested session request for this dispatcher under a 2-minute lease, so no other dispatcher "
        + "starts it. Answers claimed:false with the reason when it was taken already - leave that request alone. "
        + "Start the session with the brief the answer carries, in its cloneFolder, then call mark_session_request "
        + "before the lease runs out. Not idempotent: a second claim of the same request is refused.")]
    public async Task<SessionRequestClaimPayload> ClaimSessionRequestAsync(
        [Description("The request's id, as the wait route or list_session_requests gave it.")]
        Guid id,
        [Description("A name for this dispatcher, the same on every call it makes, e.g. manager-1.")]
        string dispatcher,
        CancellationToken cancellationToken = default)
    {
        var who = Required(dispatcher, nameof(dispatcher));
        var outcome = await requests.ClaimAsync(id, who, cancellationToken).ConfigureAwait(false);
        var request = Found(id, outcome);
        var now = _clock.GetUtcNow();

        // A dispatcher whose first answer was lost and that asks again already holds the
        // claim: telling it "taken" would have it leave its own request alone until the
        // lease ran out. The lease is not renewed; the answer says when it ends.
        var held = outcome.Succeeded
            || (request.StateAt(now) == SessionRequestState.Claimed && string.Equals(request.ClaimedBy, who, StringComparison.Ordinal));

        return new SessionRequestClaimPayload(held, held ? null : outcome.Refusal, SessionRequestPayload.From(request, now));
    }

    [McpServerTool(Name = MarkSessionRequest, Title = "Report what came of a claimed session request", ReadOnly = false, Idempotent = false, Destructive = false, OpenWorld = false)]
    [Description(
        "Reports what this dispatcher did with a request it claimed. outcome 'started' with the sessionId of the "
        + "session it started moves the request to Started; outcome 'released' gives the claim back, so the request "
        + "is Requested again for any dispatcher. Answers marked:false with the reason when this dispatcher no "
        + "longer holds the claim - its lease ran out, or another dispatcher holds it.")]
    public async Task<SessionRequestMarkPayload> MarkSessionRequestAsync(
        [Description("The request's id.")]
        Guid id,
        [Description("'started' or 'released'.")]
        string outcome,
        [Description("The dispatcher name the request was claimed with.")]
        string dispatcher,
        [Description("The id of the session started for the request. Required with 'started', ignored with 'released'.")]
        string? sessionId = null,
        CancellationToken cancellationToken = default)
    {
        var who = Required(dispatcher, nameof(dispatcher));
        var asked = (outcome ?? string.Empty).Trim().ToLowerInvariant();

        var result = asked switch
        {
            Started => await requests.ReportStartedAsync(id, who, Required(sessionId, nameof(sessionId)), cancellationToken).ConfigureAwait(false),
            Released => await requests.ReleaseAsync(id, who, cancellationToken).ConfigureAwait(false),
            _ => throw RepositoryScope.Failure(Error.Validation(
                "session_request.outcome",
                $"outcome must be '{Started}' or '{Released}', not '{outcome}'."))
        };

        var request = Found(id, result);

        return new SessionRequestMarkPayload(asked, result.Succeeded, result.Refusal, SessionRequestPayload.From(request, _clock.GetUtcNow()));
    }

    [McpServerTool(Name = ListSessionRequests, Title = "List session requests", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description(
        "This machine's session requests, oldest first, each with its state, clone folder and brief - every one, or "
        + "only those in one state. For a dispatcher that restarts: what it claimed or started before. Read-only.")]
    public async Task<SessionRequestsPayload> ListSessionRequestsAsync(
        [Description("Optional. Only requests reading as this state now: requested, claimed, started, linked or cancelled. A claim whose lease ran out reads as requested.")]
        string? state = null,
        CancellationToken cancellationToken = default)
    {
        SessionRequestState? only = null;

        if (!string.IsNullOrWhiteSpace(state))
        {
            // Matched against the names, not Enum.TryParse: that would also take "1"
            // for Claimed and "claimed,started" for the OR of the two.
            var name = Enum.GetNames<SessionRequestState>()
                .FirstOrDefault(candidate => string.Equals(candidate, state.Trim(), StringComparison.OrdinalIgnoreCase));

            only = name is not null
                ? Enum.Parse<SessionRequestState>(name)
                : throw RepositoryScope.Failure(Error.Validation(
                    "session_request.state",
                    $"state must be requested, claimed, started, linked or cancelled, not '{state}'."));
        }

        var listed = await requests.ListAsync(only, cancellationToken).ConfigureAwait(false);
        var now = _clock.GetUtcNow();

        return new SessionRequestsPayload(
            only is { } narrowed ? SessionRequestPayload.WireName(narrowed) : null,
            listed.Count,
            [.. listed.Select(request => SessionRequestPayload.From(request, now))]);
    }

    private static SessionRequest Found(Guid id, SessionRequestOutcome outcome) =>
        outcome.Request ?? throw RepositoryScope.Failure(Error.NotFound(
            "session_request.not_found",
            $"No session request has the id '{id:D}'. Call {ListSessionRequests} to see the queue."));

    private static string Required(string? value, string name)
    {
        var trimmed = (value ?? string.Empty).Trim();

        return trimmed.Length == 0
            ? throw RepositoryScope.Failure(Error.Validation($"session_request.{name}_required", $"{name} is required."))
            : trimmed;
    }
}
