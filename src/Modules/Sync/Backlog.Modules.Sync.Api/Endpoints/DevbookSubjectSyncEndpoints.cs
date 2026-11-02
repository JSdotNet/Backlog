using Backlog.Modules.Sync.Abstractions;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.Modules.Sync.Api.Security;
using Backlog.Modules.Sync.Features.PullDevbookSubjects;
using Backlog.Modules.Sync.Features.PushDevbookSubjects;
using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Sync.Api.Endpoints;

/// <summary>
/// The two directions of devbook subject state replication: a device pushes the
/// states it set on change proposals, decision records and debt records, and
/// pulls what its other devices set.
/// <para>
/// <see cref="AnnotationSyncEndpoints"/>' shape, down to the filter order, the
/// page clamp and the edge validation, for the reason that class gives: the
/// transport question is the same one, and a second answer would be a second
/// place for the owner scope, the replica faults and the cursor handling to
/// drift. Neither route interprets a state.
/// </para>
/// </summary>
internal static class DevbookSubjectSyncEndpoints
{
    private const int DefaultMaxItems = 100;

    private const int MaximumMaxItems = 500;

    internal static IEndpointRouteBuilder MapDevbookSubjectSyncEndpoints(this IEndpointRouteBuilder sync)
    {
        var subjects = sync.MapGroup(string.Empty)
            .WithTags("DevbookSubjects")
            .RequireAuthorization(SyncPolicies.PairedDevice)
            .AddEndpointFilter<ReplicaFaultFilter>()
            .RequireOwnerScope();

        subjects.MapPost(SyncRoutes.DevbookSubjects, PushDevbookSubjects)
            .WithMetadata(new RequestBodyLimit(SyncRequestLimits.DevbookSubjectPushBodyBytes))
            .WithSummary("Writes a batch of devbook subject state changes into this owner's replica.");

        subjects.MapGet(SyncRoutes.DevbookSubjects, PullDevbookSubjects)
            .WithSummary("Reads this owner's devbook subject state changes from a cursor.");

        return sync;
    }

    /// <param name="request">Capped and validated here rather than in the
    /// handler: what one request may carry is a property of this
    /// transport.</param>
    private static async Task<IResult> PushDevbookSubjects(
        HttpContext context,
        PushDevbookSubjectsRequest request,
        ICommandHandler<PushDevbookSubjectsCommand, Result<PushDevbookSubjectsResponse>> handler,
        CancellationToken cancellationToken)
    {
        var changes = request.Subjects ?? [];

        if (changes.Count > SyncRequestLimits.MaximumPushDevbookSubjects)
        {
            // Refused whole rather than trimmed, so a device is never told 200
            // for states nobody stored.
            return SyncResults.From(
                context,
                Result.Failure<PushDevbookSubjectsResponse>(Error.Validation(
                    SyncErrorCodes.DevbookSubjectBatchTooLarge,
                    $"A push may carry at most {SyncRequestLimits.MaximumPushDevbookSubjects} devbook subject changes; this one carried {changes.Count}.")),
                Results.Ok);
        }

        if (OutOfBounds(changes) is { } refusal)
        {
            return SyncResults.From(context, Result.Failure<PushDevbookSubjectsResponse>(refusal), Results.Ok);
        }

        var result = await handler.Handle(
            new PushDevbookSubjectsCommand(context.GetOwnerScope(), changes), cancellationToken);

        return SyncResults.From(context, result, Results.Ok);
    }

    private static async Task<IResult> PullDevbookSubjects(
        HttpContext context,
        string? since,
        int? maxItems,
        IQueryHandler<PullDevbookSubjectsQuery, Result<PullDevbookSubjectsResponse>> handler,
        CancellationToken cancellationToken)
    {
        var page = Math.Clamp(maxItems ?? DefaultMaxItems, 1, MaximumMaxItems);

        var result = await handler.Handle(
            new PullDevbookSubjectsQuery(context.GetOwnerScope(), since, page), cancellationToken);

        return SyncResults.From(context, result, Results.Ok);
    }

    /// <summary>
    /// The first change this service will not store, by
    /// <see cref="DevbookSubjectRecordLimits.WhyRefused"/> — the rule the desktop
    /// runs before it pushes — or null. The whole batch is refused rather than
    /// the offending change dropped, so the service never answers 200 for a
    /// state it did not store.
    /// </summary>
    private static Error? OutOfBounds(IReadOnlyList<DevbookSubjectChange> changes)
    {
        foreach (var change in changes)
        {
            if (DevbookSubjectRecordLimits.WhyRefused(change) is { } reason)
            {
                return Error.Validation(SyncErrorCodes.DevbookSubjectInvalid, reason);
            }
        }

        return null;
    }
}
