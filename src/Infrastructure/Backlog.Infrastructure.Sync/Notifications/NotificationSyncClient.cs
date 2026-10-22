using System.Net.Http.Json;

using Backlog.Modules.Sync.Abstractions;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Backlog.SharedKernel.Results;

namespace Backlog.Infrastructure.Sync.Notifications;

/// <summary>
/// The notification exchange as a device calls it (local ADR 0038): push this owner
/// instance's notifications, pull the owner's notification feed, post a reply, list the replies
/// addressed to this instance, and delete one once processed. Transport and nothing else, on
/// <see cref="Annotations.AnnotationSyncClient"/>'s terms and its failure convention.
/// </summary>
public sealed class NotificationSyncClient
{
    /// <summary>What a feed pull asks for when the caller does not say.</summary>
    public const int DefaultMaxItems = 100;

    /// <summary>How many replies one listing asks for.</summary>
    public const int DefaultMaxReplies = 50;

    private readonly HttpClient _http;

    public NotificationSyncClient(HttpClient http)
    {
        ArgumentNullException.ThrowIfNull(http);

        _http = http;
    }

    /// <summary>Writes a batch of this instance's notifications into the owner's replica.
    /// Safe to send twice: the same document lands in the same state.</summary>
    public Task<Result<PushNotificationsResponse>> PushAsync(IReadOnlyList<NotificationChange> changes, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(changes);

        return SyncHttp.SendAsync<PushNotificationsResponse>(
            () => _http.PostAsJsonAsync(SyncRoutes.Absolute(SyncRoutes.Notifications), new PushNotificationsRequest(changes), cancellationToken),
            cancellationToken);
    }

    /// <summary>Reads one page of the owner's notification feed — what a viewer shows.</summary>
    public Task<Result<PullNotificationsResponse>> PullAsync(string? since, int maxItems = DefaultMaxItems, CancellationToken cancellationToken = default) =>
        SyncHttp.SendAsync<PullNotificationsResponse>(
            () => _http.GetAsync(ReplicaRoutes.PullRoute(SyncRoutes.Notifications, since, maxItems), cancellationToken),
            cancellationToken);

    /// <summary>Posts a reply to one of the owner's questions. Posting the same reply id
    /// again stores it once.</summary>
    public Task<Result<PostNotificationReplyResponse>> PostReplyAsync(NotificationReplyPost reply, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reply);

        return SyncHttp.SendAsync<PostNotificationReplyResponse>(
            () => _http.PostAsJsonAsync(SyncRoutes.Absolute(SyncRoutes.NotificationReplies), reply, cancellationToken),
            cancellationToken);
    }

    /// <summary>The replies addressed to <paramref name="ownerInstanceId"/> — this desktop's
    /// installation identity — oldest first.</summary>
    public Task<Result<PullNotificationRepliesResponse>> ListRepliesAsync(Guid ownerInstanceId, int maxItems = DefaultMaxReplies, CancellationToken cancellationToken = default) =>
        SyncHttp.SendAsync<PullNotificationRepliesResponse>(
            () => _http.GetAsync(
                FormattableString.Invariant($"{SyncRoutes.Absolute(SyncRoutes.NotificationReplies)}?instance={ownerInstanceId:D}&maxItems={maxItems}"),
                cancellationToken),
            cancellationToken);

    /// <summary>Asks for the Web PubSub URL this device's nudge socket opens on (local ADR 0038
    /// §4). A success with no URL means the service runs without a hub, and the desktop keeps
    /// polling.</summary>
    public Task<Result<NotificationNudgeNegotiateResponse>> NegotiateNudgeAsync(CancellationToken cancellationToken = default) =>
        SyncHttp.SendAsync<NotificationNudgeNegotiateResponse>(
            () => _http.GetAsync(SyncRoutes.Absolute(SyncRoutes.NotificationNudge), cancellationToken),
            cancellationToken);

    /// <summary>Deletes a reply this instance has processed.</summary>
    public async Task<Result> DeleteReplyAsync(Guid replyId, CancellationToken cancellationToken = default)
    {
        var deleted = await SyncHttp.SendAsync(
            () => _http.DeleteAsync(SyncRoutes.NotificationReplyFor(replyId), cancellationToken),
            (_, _) => Task.FromResult(true),
            cancellationToken).ConfigureAwait(false);

        return deleted.IsSuccess ? Result.Success() : Result.Failure(deleted.Error);
    }
}
