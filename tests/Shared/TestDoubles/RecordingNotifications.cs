using Backlog.Modules.Notifications.Abstractions;
using Backlog.SharedKernel.Results;

namespace Backlog.Tests;

/// <summary>
/// An <see cref="INotifications"/> that records every subject a boundary closes, with
/// its reason, and answers each close with one notification closed. What a boundary's
/// own tests assert on: that its end reached Notifications, about the right subject,
/// with the right reason — never how Notifications stores it.
/// </summary>
internal sealed class RecordingNotifications : INotifications
{
    private readonly List<(NotificationSubject Subject, string Reason)> _closed = [];
    private readonly Lock _gate = new();

    /// <summary>Thrown from <see cref="CloseForSubjectAsync"/> when set, so a test can
    /// show the boundary's own write does not depend on the close.</summary>
    public Exception? Fails { get; set; }

    /// <summary>Every subject closed so far, in order.</summary>
    public IReadOnlyList<(NotificationSubject Subject, string Reason)> Closed
    {
        get
        {
            lock (_gate) return [.. _closed];
        }
    }

    public Task<Result<Guid>> PublishAsync(NotificationDraft draft, CancellationToken cancellationToken = default) =>
        Task.FromResult(Result.Success(Guid.NewGuid()));

    public Task<Result> CloseAsync(Guid notificationId, string? reason = null, CancellationToken cancellationToken = default) =>
        Task.FromResult(Result.Success());

    public Task<Result<int>> CloseForSubjectAsync(NotificationSubject subject, string reason, CancellationToken cancellationToken = default)
    {
        if (Fails is { } failure) throw failure;

        lock (_gate) _closed.Add((subject, reason));
        return Task.FromResult(Result.Success(1));
    }

    public Task<Result<NotificationAnswer?>> GetAnswerAsync(Guid notificationId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Result.Success<NotificationAnswer?>(null));
}
