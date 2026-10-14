using Backlog.Modules.Sync.Abstractions;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.Modules.Sync.Api.Security;
using Backlog.Modules.Sync.Features.PullAnnotations;
using Backlog.Modules.Sync.Features.PushAnnotations;
using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Sync.Api.Endpoints;

/// <summary>
/// The two directions of annotation replication: a device pushes the remarks it
/// changed on its Devbook chapters, and pulls what its other devices changed.
/// <para>
/// Shaped like <see cref="TaskSyncEndpoints"/> deliberately, down to the filter
/// order and the page clamp, and validated at the edge like
/// <see cref="SessionSyncEndpoints"/>: the transport question is the same one
/// and a second answer would be a second place for the owner scope, the replica
/// faults and the cursor handling to drift. Neither route interprets a remark —
/// what goes in comes back out unread.
/// </para>
/// </summary>
internal static class AnnotationSyncEndpoints
{
    private const int DefaultMaxItems = 100;

    private const int MaximumMaxItems = 500;

    internal static IEndpointRouteBuilder MapAnnotationSyncEndpoints(this IEndpointRouteBuilder sync)
    {
        var annotations = sync.MapGroup(string.Empty)
            .WithTags("Annotations")
            .RequireAuthorization(SyncPolicies.PairedDevice)
            .AddEndpointFilter<ReplicaFaultFilter>()
            .RequireOwnerScope();

        annotations.MapPost(SyncRoutes.Annotations, PushAnnotations)
            .WithMetadata(new RequestBodyLimit(SyncRequestLimits.AnnotationPushBodyBytes))
            .WithSummary("Writes a batch of Devbook annotation changes into this owner's replica.");

        annotations.MapGet(SyncRoutes.Annotations, PullAnnotations)
            .WithSummary("Reads this owner's Devbook annotation changes from a cursor.");

        return sync;
    }

    /// <param name="request">Capped and validated here rather than in the
    /// handler: what one request may carry is a property of this transport, and
    /// an in-process caller has no reason to be limited by it.</param>
    private static async Task<IResult> PushAnnotations(
        HttpContext context,
        PushAnnotationsRequest request,
        ICommandHandler<PushAnnotationsCommand, Result<PushAnnotationsResponse>> handler,
        CancellationToken cancellationToken)
    {
        var changes = request.Annotations ?? [];

        if (changes.Count > SyncRequestLimits.MaximumPushAnnotations)
        {
            // Refused whole rather than trimmed to the cap, for the reason the
            // task push refuses: a device told 200 for a trimmed batch would move
            // its watermark past remarks nobody stored.
            return SyncResults.From(
                context,
                Result.Failure<PushAnnotationsResponse>(Error.Validation(
                    SyncErrorCodes.AnnotationBatchTooLarge,
                    $"A push may carry at most {SyncRequestLimits.MaximumPushAnnotations} annotation changes; this one carried {changes.Count}.")),
                Results.Ok);
        }

        if (OutOfBounds(changes) is { } refusal)
        {
            return SyncResults.From(context, Result.Failure<PushAnnotationsResponse>(refusal), Results.Ok);
        }

        var result = await handler.Handle(
            new PushAnnotationsCommand(context.GetOwnerScope(), changes), cancellationToken);

        return SyncResults.From(context, result, Results.Ok);
    }

    private static async Task<IResult> PullAnnotations(
        HttpContext context,
        string? since,
        int? maxItems,
        IQueryHandler<PullAnnotationsQuery, Result<PullAnnotationsResponse>> handler,
        CancellationToken cancellationToken)
    {
        var page = Math.Clamp(maxItems ?? DefaultMaxItems, 1, MaximumMaxItems);

        var result = await handler.Handle(
            new PullAnnotationsQuery(context.GetOwnerScope(), since, page), cancellationToken);

        return SyncResults.From(context, result, Results.Ok);
    }

    /// <summary>
    /// The first change that is not something this service will store, or null
    /// if every one of them is, by <see cref="AnnotationRecordLimits.WhyRefused"/>
    /// — the rule the desktop runs before it pushes. The whole batch is refused
    /// rather than the offending changes dropped, so the service never answers
    /// 200 for a remark it did not store: a device's watermark moves past only
    /// what was stored, or what the device itself left out — and logged —
    /// because this rule would never let it be stored.
    /// </summary>
    private static Error? OutOfBounds(IReadOnlyList<AnnotationChange> changes)
    {
        foreach (var change in changes)
        {
            if (AnnotationRecordLimits.WhyRefused(change) is { } reason)
            {
                return Error.Validation(SyncErrorCodes.AnnotationInvalid, reason);
            }
        }

        return null;
    }
}
