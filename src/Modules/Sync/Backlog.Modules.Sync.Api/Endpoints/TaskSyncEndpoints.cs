using Backlog.Modules.Sync.Abstractions;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.Modules.Sync.Api.Security;
using Backlog.Modules.Sync.Features.PullTasks;
using Backlog.Modules.Sync.Features.PushTasks;
using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Sync.Api.Endpoints;

/// <summary>
/// The two directions of task replication: a device pushes what it changed, and
/// pulls what its other devices changed.
/// <para>
/// One route for both, because they are two halves of one exchange over one
/// collection. Neither takes an owner — the scope comes from the token and from
/// nowhere else (.arc42/adr/0005 §Identity) — and neither interprets a task:
/// what goes in comes back out unread.
/// </para>
/// </summary>
internal static class TaskSyncEndpoints
{
    /// <summary>How many changes a pull returns when the caller does not say.
    /// Enough for a device that has been off for an afternoon to catch up in one
    /// round trip.</summary>
    private const int DefaultMaxItems = 100;

    /// <summary>And the most it will return however large a number is asked
    /// for. The cap is the service's, not the client's: a page is a request unit
    /// against the store and a caller asking for a hundred thousand is asking
    /// for a timeout.</summary>
    private const int MaximumMaxItems = 500;

    internal static IEndpointRouteBuilder MapTaskSyncEndpoints(this IEndpointRouteBuilder sync)
    {
        var tasks = sync.MapGroup(string.Empty)
            .WithTags("Tasks")
            .RequireAuthorization(SyncPolicies.PairedDevice)
            .AddEndpointFilter<ReplicaFaultFilter>()
            .RequireOwnerScope();

        tasks.MapPost(SyncRoutes.Tasks, PushTasks)
            .WithMetadata(new RequestBodyLimit(SyncRequestLimits.PushBodyBytes))
            .WithSummary("Writes a batch of task changes into this owner's replica.");

        tasks.MapGet(SyncRoutes.Tasks, PullTasks)
            .WithSummary("Reads this owner's task changes from a cursor.");

        return sync;
    }

    /// <param name="request">Capped here rather than in the handler, for the same
    /// reason the page size is: how much one request may carry is a property of
    /// this transport, and an in-process caller has no reason to be limited by
    /// it.</param>
    private static async Task<IResult> PushTasks(
        HttpContext context,
        PushTasksRequest request,
        ICommandHandler<PushTasksCommand, Result<PushTasksResponse>> handler,
        CancellationToken cancellationToken)
    {
        var changes = request.Tasks ?? [];

        if (changes.Count > SyncRequestLimits.MaximumPushTasks)
        {
            // Refused whole rather than trimmed to the cap: storing the first 500
            // of 600 and answering 200 would leave the device believing the other
            // hundred were stored, and its watermark would move past them.
            return SyncResults.From(
                context,
                Result.Failure<PushTasksResponse>(Error.Validation(
                    SyncErrorCodes.PushBatchTooLarge,
                    $"A push may carry at most {SyncRequestLimits.MaximumPushTasks} task changes; this one carried {changes.Count}.")),
                Results.Ok);
        }

        var result = await handler.Handle(
            new PushTasksCommand(context.GetOwnerScope(), changes), cancellationToken);

        // 200 rather than 201: nothing was created at a URL a client could go
        // and read, and the batch is an upsert of documents the client already
        // names.
        return SyncResults.From(context, result, Results.Ok);
    }

    /// <param name="since">The cursor from the previous page, or absent to start
    /// from the beginning — which is what a freshly paired device sends.</param>
    /// <param name="maxItems">Clamped here rather than in the handler, because a
    /// page size is a property of this transport and an in-process caller has no
    /// reason to be limited by it.</param>
    private static async Task<IResult> PullTasks(
        HttpContext context,
        string? since,
        int? maxItems,
        IQueryHandler<PullTasksQuery, Result<PullTasksResponse>> handler,
        CancellationToken cancellationToken)
    {
        var page = Math.Clamp(maxItems ?? DefaultMaxItems, 1, MaximumMaxItems);

        var result = await handler.Handle(
            new PullTasksQuery(context.GetOwnerScope(), since, page), cancellationToken);

        return SyncResults.From(context, result, Results.Ok);
    }
}
