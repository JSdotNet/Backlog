using Backlog.Modules.Sync.Abstractions;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.Modules.Sync.Api.Security;
using Backlog.Modules.Sync.Features.PullKnowledgeEvents;
using Backlog.Modules.Sync.Features.PushKnowledgeEvents;
using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Sync.Api.Endpoints;

/// <summary>
/// The two directions of knowledge event replication (local ADR 0050 §2): a desktop pushes
/// the events and series it changed, and every device of the owner — the other desktops to
/// converge, the phone to read — pulls what was pushed.
/// <para>
/// <see cref="DevbookSubjectSyncEndpoints"/>' shape, down to the filter order, the page
/// clamp and the edge validation, for the reason that class gives. Neither route reads an
/// event: the bounds at the edge are on the envelope and the document's size alone.
/// </para>
/// </summary>
internal static class KnowledgeEventSyncEndpoints
{
    private const int DefaultMaxItems = 100;

    private const int MaximumMaxItems = 500;

    internal static IEndpointRouteBuilder MapKnowledgeEventSyncEndpoints(this IEndpointRouteBuilder sync)
    {
        var events = sync.MapGroup(string.Empty)
            .WithTags("KnowledgeEvents")
            .RequireAuthorization(SyncPolicies.PairedDevice)
            .AddEndpointFilter<ReplicaFaultFilter>()
            .RequireOwnerScope();

        events.MapPost(SyncRoutes.KnowledgeEvents, PushKnowledgeEvents)
            .WithMetadata(new RequestBodyLimit(SyncRequestLimits.KnowledgeEventPushBodyBytes))
            .WithSummary("Writes a batch of knowledge event and event series changes into this owner's replica.");

        events.MapGet(SyncRoutes.KnowledgeEvents, PullKnowledgeEvents)
            .WithSummary("Reads this owner's knowledge event and event series changes from a cursor.");

        return sync;
    }

    /// <param name="request">Capped and validated here rather than in the handler: what one
    /// request may carry is a property of this transport.</param>
    private static async Task<IResult> PushKnowledgeEvents(
        HttpContext context,
        PushKnowledgeEventsRequest request,
        ICommandHandler<PushKnowledgeEventsCommand, Result<PushKnowledgeEventsResponse>> handler,
        CancellationToken cancellationToken)
    {
        var changes = request.Events ?? [];

        if (changes.Count > SyncRequestLimits.MaximumPushKnowledgeEvents)
        {
            // Refused whole rather than trimmed, so a device is never told 200 for events
            // nobody stored.
            return SyncResults.From(
                context,
                Result.Failure<PushKnowledgeEventsResponse>(Error.Validation(
                    SyncErrorCodes.KnowledgeEventBatchTooLarge,
                    $"A push may carry at most {SyncRequestLimits.MaximumPushKnowledgeEvents} knowledge event changes; this one carried {changes.Count}.")),
                Results.Ok);
        }

        if (OutOfBounds(changes) is { } refusal)
        {
            return SyncResults.From(context, Result.Failure<PushKnowledgeEventsResponse>(refusal), Results.Ok);
        }

        var result = await handler.Handle(
            new PushKnowledgeEventsCommand(context.GetOwnerScope(), changes), cancellationToken);

        return SyncResults.From(context, result, Results.Ok);
    }

    private static async Task<IResult> PullKnowledgeEvents(
        HttpContext context,
        string? since,
        int? maxItems,
        IQueryHandler<PullKnowledgeEventsQuery, Result<PullKnowledgeEventsResponse>> handler,
        CancellationToken cancellationToken)
    {
        var page = Math.Clamp(maxItems ?? DefaultMaxItems, 1, MaximumMaxItems);

        var result = await handler.Handle(
            new PullKnowledgeEventsQuery(context.GetOwnerScope(), since, page), cancellationToken);

        return SyncResults.From(context, result, Results.Ok);
    }

    /// <summary>
    /// The first change this service will not store, by
    /// <see cref="KnowledgeEventRecordLimits.WhyRefused"/> — the rule the desktop runs before
    /// it pushes — or null. The whole batch is refused rather than the offending change
    /// dropped, so the service never answers 200 for an event it did not store.
    /// </summary>
    private static Error? OutOfBounds(IReadOnlyList<KnowledgeEventChange> changes)
    {
        foreach (var change in changes)
        {
            var reason = change is null
                ? "A push holds no empty knowledge event change."
                : KnowledgeEventRecordLimits.WhyRefused(change);

            if (reason is not null)
            {
                return Error.Validation(SyncErrorCodes.KnowledgeEventInvalid, reason);
            }
        }

        return null;
    }
}
