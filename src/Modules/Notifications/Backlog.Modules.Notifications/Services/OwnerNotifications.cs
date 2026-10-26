using Backlog.Modules.Notifications.Abstractions;
using Backlog.Modules.Notifications.DomainModels;
using Backlog.SharedKernel;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Notifications.Services;

/// <summary>
/// The owner instance's <see cref="INotifications"/> and <see cref="INotificationReplies"/>: a
/// desktop publishes notifications it then owns under its <see cref="DeviceIdentity.Id"/>, and is
/// the only instance that changes them or decides their answer (local ADR 0039 §1–2).
/// <para>
/// One change at a time, under one gate, so two replies racing for the same question are
/// processed in turn and the first valid one wins; the second meets an Answered question and is
/// rejected as already answered. The answer is stored before it is handed to the source's handler,
/// so a handler that is not registered yet, or throws, gets it on the next start
/// (<see cref="AnswerDelivery"/>).
/// </para>
/// </summary>
internal sealed class OwnerNotifications(
    INotificationRepository repository,
    IDeviceIdentitySource device,
    AnswerDelivery delivery,
    TimeProvider time,
    IAppFeatureSettings? features = null) : INotifications, INotificationReplies, IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    private Guid Me => device.Current.Id;

    public async Task<Result<Guid>> PublishAsync(NotificationDraft draft, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);

        if (features is not null && !features.IsEnabled(NotificationsFeatures.Notifications)) return NotificationsErrors.NotAvailable();

        var published = Notification.Publish(Guid.NewGuid(), Me, draft, time.GetUtcNow());
        if (published.IsFailure) return published.Error;

        await repository.SaveAsync(published.Value, cancellationToken).ConfigureAwait(false);
        return published.Value.Id;
    }

    public Task<Result> CloseAsync(Guid notificationId, string? reason = null, CancellationToken cancellationToken = default) =>
        ChangeAsync(notificationId, notification => notification.Close(Me, reason, time.GetUtcNow()), cancellationToken);

    public async Task<Result<int>> CloseForSubjectAsync(NotificationSubject subject, string reason, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subject);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var closed = 0;
            foreach (var notification in await repository.ListOpenAsync(cancellationToken).ConfigureAwait(false))
            {
                if (notification.OwnerInstanceId != Me || !notification.IsAbout(subject)) continue;
                if (notification.CloseForSubject(subject, reason, Me, time.GetUtcNow()).IsFailure) continue;

                await repository.SaveAsync(notification, cancellationToken).ConfigureAwait(false);
                closed++;
            }

            return closed;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<Result<NotificationAnswer?>> GetAnswerAsync(Guid notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await repository.GetAsync(notificationId, cancellationToken).ConfigureAwait(false);

        return notification is null
            ? Result.Failure<NotificationAnswer?>(NotificationsErrors.NotFound(notificationId))
            : Result.Success(notification.Answer);
    }

    public async Task<Result<NotificationAnswer>> AcceptReplyAsync(NotificationReply reply, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reply);

        Result<NotificationAnswer> accepted;

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var notification = await repository.GetAsync(reply.NotificationId, cancellationToken).ConfigureAwait(false);
            if (notification is null) return NotificationsErrors.NotFound(reply.NotificationId);

            accepted = notification.Accept(reply, Me, time.GetUtcNow());
            if (accepted.IsFailure) return accepted;

            await repository.SaveAsync(notification, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }

        // The answer is stored and settled: a delivery that fails past the handler — the store
        // refusing the delivered mark — leaves it waiting for the next start's replay, and the
        // reply is still the accepted one.
        try
        {
            await delivery.DeliverAsync(reply.NotificationId, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Nothing to undo; the replay hands it over.
        }

        return accepted;
    }

    public Task<Result> AcknowledgeAsync(Guid notificationId, CancellationToken cancellationToken = default) =>
        ChangeAsync(notificationId, notification => notification.Acknowledge(Me, time.GetUtcNow()), cancellationToken);

    public void Dispose() => _gate.Dispose();

    private async Task<Result> ChangeAsync(Guid notificationId, Func<Notification, Result> change, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var notification = await repository.GetAsync(notificationId, cancellationToken).ConfigureAwait(false);
            if (notification is null) return Result.Failure(NotificationsErrors.NotFound(notificationId));

            var changed = change(notification);
            if (changed.IsFailure) return changed;

            await repository.SaveAsync(notification, cancellationToken).ConfigureAwait(false);
            return changed;
        }
        finally
        {
            _gate.Release();
        }
    }
}
