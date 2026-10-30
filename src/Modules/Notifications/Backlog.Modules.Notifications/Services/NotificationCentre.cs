using Backlog.Modules.Notifications.Abstractions;
using Backlog.Modules.Notifications.DomainModels;
using Backlog.SharedKernel;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Notifications.Services;

/// <summary>
/// The owner instance's <see cref="INotificationCentre"/>. It lists from the store and answers
/// through <see cref="INotificationReplies"/> as a reply from this device, so an answer given in
/// the shell's centre and one given in a Windows notification take the same path a phone's reply
/// will: checked against the question, first valid one wins, handed to the source's handler.
/// </summary>
internal sealed class NotificationCentre : INotificationCentre, IDisposable
{
    private readonly ObservedNotificationRepository _store;
    private readonly INotificationReplies _replies;
    private readonly IDeviceIdentitySource _device;
    private readonly TimeProvider _time;

    public NotificationCentre(
        ObservedNotificationRepository store,
        INotificationReplies replies,
        IDeviceIdentitySource device,
        TimeProvider time)
    {
        _store = store;
        _replies = replies;
        _device = device;
        _time = time;

        _store.Saved += OnSaved;
    }

    public event EventHandler<NotificationChanged>? Changed;

    public DeviceIdentity ThisDevice => _device.Current;

    public async Task<IReadOnlyList<NotificationView>> ListAsync(CancellationToken cancellationToken = default)
    {
        var since = _time.GetUtcNow() - NotificationView.SettledWindow;
        var stored = await _store.ListOpenOrSettledSinceAsync(since, cancellationToken).ConfigureAwait(false);

        return [.. stored.Select(ToView)];
    }

    public Task<Result<NotificationAnswer>> AnswerAsync(
        Guid notificationId,
        IReadOnlyList<string> choiceIds,
        string? text,
        string? note = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(choiceIds);

        var me = _device.Current.Id;
        var reply = new NotificationReply(
            Guid.NewGuid(),
            notificationId,
            OwnerInstanceId: me,
            FromDeviceId: me,
            [.. choiceIds],
            string.IsNullOrWhiteSpace(text) ? null : text,
            _time.GetUtcNow(),
            string.IsNullOrWhiteSpace(note) ? null : note);

        return _replies.AcceptReplyAsync(reply, cancellationToken);
    }

    public Task<Result> AcknowledgeAsync(Guid notificationId, CancellationToken cancellationToken = default) =>
        _replies.AcknowledgeAsync(notificationId, cancellationToken);

    public void Dispose() => _store.Saved -= OnSaved;

    /// <summary>Every listener hears every change: one that throws — a screen that fell over —
    /// does not keep the change from the badge, the centre or the Windows notifications after it.</summary>
    private void OnSaved(Notification notification, bool isNew)
    {
        if (Changed is not { } changed) return;

        var change = new NotificationChanged(ToView(notification), isNew);
        foreach (var listener in changed.GetInvocationList().Cast<EventHandler<NotificationChanged>>())
        {
            try
            {
                listener(this, change);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                // The change is stored; a listener's failure is its own.
            }
        }
    }

    internal static NotificationView ToView(Notification n) => new(
        n.Id,
        n.OwnerInstanceId,
        n.Source,
        n.Subject,
        n.Kind,
        n.Severity,
        n.Title,
        n.Summary,
        n.BodyMarkdown,
        n.AnswerSpec,
        n.DeepLink,
        n.IsPrivate,
        n.CreatedAt,
        n.State,
        n.SettledAt,
        n.CloseReason,
        n.Answer,
        n.Origin);
}
