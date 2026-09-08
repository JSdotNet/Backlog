using Backlog.Modules.Sync.Abstractions;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.Modules.Sync.Api.Security;
using Backlog.Modules.Sync.Features.PullSessions;
using Backlog.Modules.Sync.Features.PushSessions;
using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Sync.Api.Endpoints;

/// <summary>
/// The two directions of session replication: a machine appends what its agents
/// have been doing, and pulls what the owner's other machines have been doing.
/// <para>
/// One route for both, because they are two halves of one exchange over one
/// collection. Neither takes an owner and neither takes a machine — both come
/// from the token and from nowhere else (.arc42/adr/0005 §Identity) — and neither
/// interprets a record: what goes in comes back out unread.
/// </para>
/// <para>
/// Shaped like <see cref="TaskSyncEndpoints"/> deliberately, down to the filter
/// order and the page clamp. The two feeds have different conflict stories and
/// different retentions, but the transport question they answer is the same one,
/// and a second answer to it would be a second place for the owner scope, the
/// replica faults and the cursor handling to drift.
/// </para>
/// </summary>
internal static class SessionSyncEndpoints
{
    /// <summary>How many records a pull returns when the caller does not say.
    /// Enough for a device that has been off for an afternoon to catch up with a
    /// small fleet in one round trip.</summary>
    private const int DefaultMaxItems = 100;

    /// <summary>And the most it will return however large a number is asked for.
    /// The cap is the service's, not the client's: a page is a request unit
    /// against the store and a caller asking for a hundred thousand is asking for
    /// a timeout.</summary>
    private const int MaximumMaxItems = 500;

    internal static IEndpointRouteBuilder MapSessionSyncEndpoints(this IEndpointRouteBuilder sync)
    {
        var sessions = sync.MapGroup(string.Empty)
            .WithTags("Sessions")
            .RequireAuthorization(SyncPolicies.PairedDevice)
            .AddEndpointFilter<ReplicaFaultFilter>()
            .RequireOwnerScope();

        sessions.MapPost(SyncRoutes.Sessions, PushSessions)
            .WithMetadata(new RequestBodyLimit(SyncRequestLimits.SessionPushBodyBytes))
            .WithSummary("Appends this machine's session records to the owner's replica.");

        sessions.MapGet(SyncRoutes.Sessions, PullSessions)
            .WithSummary("Reads this owner's session records from a cursor.");

        return sync;
    }

    /// <param name="request">Capped and validated here rather than in the handler,
    /// for the same reason the page size is: what one request may carry is a
    /// property of this transport, and an in-process caller has no reason to be
    /// limited by it.</param>
    private static async Task<IResult> PushSessions(
        HttpContext context,
        PushSessionsRequest request,
        ICommandHandler<PushSessionsCommand, Result<PushSessionsResponse>> handler,
        CancellationToken cancellationToken)
    {
        var records = request.Sessions ?? [];

        if (records.Count > SyncRequestLimits.MaximumPushSessions)
        {
            // Refused whole rather than trimmed to the cap: storing the first 500
            // of 600 and answering 200 would leave the machine believing the other
            // hundred were stored, and its watermark would move past them.
            return SyncResults.From(
                context,
                Result.Failure<PushSessionsResponse>(Error.Validation(
                    SyncErrorCodes.SessionBatchTooLarge,
                    $"A push may carry at most {SyncRequestLimits.MaximumPushSessions} session records; this one carried {records.Count}.")),
                Results.Ok);
        }

        if (OutOfBounds(records) is { } refusal)
        {
            return SyncResults.From(context, Result.Failure<PushSessionsResponse>(refusal), Results.Ok);
        }

        var result = await handler.Handle(
            new PushSessionsCommand(context.GetOwnerScope(), records), cancellationToken);

        // 200 rather than 201: nothing was created at a URL a client could go and
        // read, and the batch addresses records the machine already names.
        return SyncResults.From(context, result, Results.Ok);
    }

    /// <param name="since">The cursor from the previous page, or absent to start
    /// from the beginning — which is what a freshly paired device sends.</param>
    /// <param name="maxItems">Clamped here rather than in the handler, because a
    /// page size is a property of this transport and an in-process caller has no
    /// reason to be limited by it.</param>
    private static async Task<IResult> PullSessions(
        HttpContext context,
        string? since,
        int? maxItems,
        IQueryHandler<PullSessionsQuery, Result<PullSessionsResponse>> handler,
        CancellationToken cancellationToken)
    {
        var page = Math.Clamp(maxItems ?? DefaultMaxItems, 1, MaximumMaxItems);

        var result = await handler.Handle(
            new PullSessionsQuery(context.GetOwnerScope(), since, page), cancellationToken);

        return SyncResults.From(context, result, Results.Ok);
    }

    /// <summary>
    /// The first record that is not something this service will store, or null if
    /// every one of them is.
    /// <para>
    /// <strong>The whole batch is refused rather than the offending records being
    /// dropped from it</strong>, which is the same rule the batch cap above
    /// follows and it is there for the same reason: a push that silently stored
    /// nine of ten records and answered 200 would move the machine's watermark
    /// past the tenth, and that record would never be offered again. A refusal
    /// the machine can see is recoverable; a watermark past a record nobody
    /// stored is not.
    /// </para>
    /// <para>
    /// Two kinds of check, and they fail differently. An empty session id or
    /// agent kind is a record with no identity — .domain/sessions/naming.md#session-identity
    /// needs both halves, so a record missing either could not be addressed and
    /// would collide with every other record missing the same one. A field longer
    /// than its bound is the other kind: nothing a device of ours sends, and the
    /// thing that stops a caller posting a two-megabyte branch name into durable,
    /// per-request-billed storage.
    /// </para>
    /// </summary>
    private static Error? OutOfBounds(IReadOnlyList<SessionRecord> records)
    {
        foreach (var record in records)
        {
            if (string.IsNullOrWhiteSpace(record.SessionId))
            {
                return Invalid("A session record needs the session id its agent issued.");
            }

            if (string.IsNullOrWhiteSpace(record.AgentKind))
            {
                return Invalid("A session record needs the agent kind that ran it.");
            }

            var refusal =
                TooLong("session id", record.SessionId, SyncRequestLimits.MaximumSessionId)
                ?? TooLong("agent kind", record.AgentKind, SyncRequestLimits.MaximumAgentKind)
                ?? TooLong("machine name", record.MachineName, SyncRequestLimits.MaximumMachineName)
                ?? TooLong("repository alias", record.RepositoryAlias, SyncRequestLimits.MaximumRepositoryAlias)
                ?? TooLong("branch", record.Branch, SyncRequestLimits.MaximumBranch);

            if (refusal is not null)
            {
                return refusal;
            }
        }

        return null;
    }

    /// <summary>A null free-text field is absent rather than too long. All three
    /// of the nullable ones are genuinely unknown for some sessions — no
    /// repository, a detached head, an agent that recorded no start time — and
    /// none of the three is a reason to refuse the record.</summary>
    private static Error? TooLong(string field, string? value, int limit) =>
        value is not null && value.Length > limit
            ? Invalid($"A session record's {field} may be at most {limit} characters; this one was {value.Length}.")
            : null;

    private static Error Invalid(string message) => Error.Validation(SyncErrorCodes.SessionInvalid, message);
}
