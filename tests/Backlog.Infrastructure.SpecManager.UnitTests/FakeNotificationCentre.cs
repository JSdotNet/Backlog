using Backlog.Modules.Notifications.Abstractions;
using Backlog.SharedKernel;
using Backlog.SharedKernel.Results;

namespace Backlog.Infrastructure.SpecManager.UnitTests;

/// <summary>The owner's notification centre as far as a listener sees it: the stored
/// changes it raises, which the test raises itself.</summary>
internal sealed class FakeNotificationCentre : INotificationCentre
{
    public event EventHandler<NotificationChanged>? Changed;

    public DeviceIdentity ThisDevice { get; } = new(Guid.NewGuid(), "Desk");

    public void Raise(NotificationView view) => Changed?.Invoke(this, new NotificationChanged(view, IsNew: false));

    public Task<IReadOnlyList<NotificationView>> ListAsync(CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<Result<NotificationAnswer>> AnswerAsync(
        Guid notificationId,
        IReadOnlyList<string> choiceIds,
        string? text,
        string? note = null,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<Result> AcknowledgeAsync(Guid notificationId, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();
}
