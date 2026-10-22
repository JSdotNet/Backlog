using Backlog.Modules.Sync.Abstractions;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.Modules.Sync.Api.Security;
using Backlog.Modules.Sync.Features.DeleteNotificationReply;
using Backlog.Modules.Sync.Features.ListNotificationReplies;
using Backlog.Modules.Sync.Features.NegotiateNotificationNudge;
using Backlog.Modules.Sync.Features.PostNotificationReply;
using Backlog.Modules.Sync.Features.PullNotifications;
using Backlog.Modules.Sync.Features.PushNotifications;
using Backlog.SharedKernel.Handlers;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Sync.Api.Endpoints;

/// <summary>
/// The notification exchange (local ADR 0038): an owner instance pushes the notifications it
/// published and their state changes, every device of the owner pulls the feed, a viewer posts
/// a reply, and the owner instance lists the replies addressed to it (<c>?instance=</c>) and
/// deletes each once it has processed it.
/// <para>
/// Shaped like <see cref="AnnotationSyncEndpoints"/> — the same filter order, the same page
/// clamp, bounds checked at the edge — and all five routes sit behind
/// <see cref="OwnerScopeFilter"/>: the owner is read from the token and from nowhere else, so a
/// reply is stored only in the caller's own partition. That is the service's half of "a reply
/// comes from a device under the same owner"; the owner instance checks it again.
/// </para>
/// <para>
/// A sixth route, negotiate, hands a desktop the Web PubSub URL its nudge socket opens on
/// (ADR 0038 §4), behind the same filter: the group in the URL is the token's owner, so no
/// device can listen to another owner's nudges.
/// </para>
/// </summary>
internal static class NotificationSyncEndpoints
{
    private const int DefaultMaxItems = 100;

    private const int MaximumMaxItems = 500;

    private const int DefaultMaxReplies = 50;

    private const int MaximumMaxReplies = 200;

    internal static IEndpointRouteBuilder MapNotificationSyncEndpoints(this IEndpointRouteBuilder sync)
    {
        var notifications = sync.MapGroup(string.Empty)
            .WithTags("Notifications")
            .RequireAuthorization(SyncPolicies.PairedDevice)
            .AddEndpointFilter<ReplicaFaultFilter>()
            .RequireOwnerScope();

        notifications.MapPost(SyncRoutes.Notifications, PushNotifications)
            .WithMetadata(new RequestBodyLimit(SyncRequestLimits.NotificationPushBodyBytes))
            .WithSummary("Writes a batch of the calling owner instance's notifications into this owner's replica.");

        notifications.MapGet(SyncRoutes.Notifications, PullNotifications)
            .WithSummary("Reads this owner's notification changes from a cursor.");

        notifications.MapPost(SyncRoutes.NotificationReplies, PostReply)
            .WithMetadata(new RequestBodyLimit(SyncRequestLimits.NotificationReplyBodyBytes))
            .WithSummary("Stores a reply to one of this owner's notifications, from any of the owner's devices.");

        notifications.MapGet(SyncRoutes.NotificationReplies, ListReplies)
            .WithSummary("Lists the replies addressed to one owner instance, oldest first, for the device that speaks for it.");

        notifications.MapDelete(SyncRoutes.NotificationReply, DeleteReply)
            .WithSummary("Deletes a reply the calling owner instance has processed.");

        notifications.MapGet(SyncRoutes.NotificationNudge, NegotiateNudge)
            .WithSummary("Hands the calling device a Web PubSub client access URL for its owner's nudge group, or none where the service runs without one.");

        return sync;
    }

    private static async Task<IResult> PushNotifications(
        HttpContext context,
        PushNotificationsRequest request,
        ICommandHandler<PushNotificationsCommand, Result<PushNotificationsResponse>> handler,
        CancellationToken cancellationToken)
    {
        var changes = request.Notifications ?? [];

        if (changes.Count > SyncRequestLimits.MaximumPushNotifications)
        {
            // Refused whole rather than trimmed, for the reason the task push is.
            return SyncResults.From(
                context,
                Result.Failure<PushNotificationsResponse>(Error.Validation(
                    SyncErrorCodes.NotificationBatchTooLarge,
                    $"A push may carry at most {SyncRequestLimits.MaximumPushNotifications} notifications; this one carried {changes.Count}.")),
                Results.Ok);
        }

        foreach (var change in changes)
        {
            var reason = change is null ? "A push holds no empty notification." : NotificationRecordLimits.WhyRefused(change);
            if (reason is not null)
            {
                return SyncResults.From(
                    context,
                    Result.Failure<PushNotificationsResponse>(Error.Validation(SyncErrorCodes.NotificationInvalid, reason)),
                    Results.Ok);
            }
        }

        var result = await handler.Handle(new PushNotificationsCommand(context.GetOwnerScope(), changes), cancellationToken);

        return SyncResults.From(context, result, Results.Ok);
    }

    private static async Task<IResult> PullNotifications(
        HttpContext context,
        string? since,
        int? maxItems,
        IQueryHandler<PullNotificationsQuery, Result<PullNotificationsResponse>> handler,
        CancellationToken cancellationToken)
    {
        var page = Math.Clamp(maxItems ?? DefaultMaxItems, 1, MaximumMaxItems);

        var result = await handler.Handle(new PullNotificationsQuery(context.GetOwnerScope(), since, page), cancellationToken);

        return SyncResults.From(context, result, Results.Ok);
    }

    private static async Task<IResult> PostReply(
        HttpContext context,
        NotificationReplyPost reply,
        ICommandHandler<PostNotificationReplyCommand, Result<PostNotificationReplyResponse>> handler,
        CancellationToken cancellationToken)
    {
        if (NotificationRecordLimits.WhyRefused(reply) is { } reason)
        {
            return SyncResults.From(
                context,
                Result.Failure<PostNotificationReplyResponse>(Error.Validation(SyncErrorCodes.NotificationInvalid, reason)),
                Results.Ok);
        }

        var result = await handler.Handle(new PostNotificationReplyCommand(context.GetOwnerScope(), reply), cancellationToken);

        return SyncResults.From(context, result, Results.Ok);
    }

    /// <param name="instance">The owner instance the replies are addressed to — the publishing
    /// desktop's installation identity. Answered only for the device that last pushed that
    /// instance's notifications; any other device of the owner gets an empty list.</param>
    private static async Task<IResult> ListReplies(
        HttpContext context,
        Guid instance,
        int? maxItems,
        IQueryHandler<ListNotificationRepliesQuery, Result<PullNotificationRepliesResponse>> handler,
        CancellationToken cancellationToken)
    {
        var page = Math.Clamp(maxItems ?? DefaultMaxReplies, 1, MaximumMaxReplies);

        var result = await handler.Handle(new ListNotificationRepliesQuery(context.GetOwnerScope(), instance, page), cancellationToken);

        return SyncResults.From(context, result, Results.Ok);
    }

    private static async Task<IResult> NegotiateNudge(
        HttpContext context,
        IQueryHandler<NegotiateNotificationNudgeQuery, Result<NotificationNudgeNegotiateResponse>> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.Handle(new NegotiateNotificationNudgeQuery(context.GetOwnerScope()), cancellationToken);

        return SyncResults.From(context, result, Results.Ok);
    }

    private static async Task<IResult> DeleteReply(
        HttpContext context,
        Guid id,
        ICommandHandler<DeleteNotificationReplyCommand, Result> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.Handle(new DeleteNotificationReplyCommand(context.GetOwnerScope(), id), cancellationToken);

        return SyncResults.From(context, result, Results.NoContent);
    }
}
