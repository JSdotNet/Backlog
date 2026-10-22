using Backlog.Modules.Notifications.Abstractions;
using Backlog.SharedKernel.Results;

namespace Backlog.Tests;

/// <summary>
/// An <see cref="INotifications"/> that records what a boundary publishes, every
/// notification it closes by id, and every subject it closes, with its reason, and answers
/// each close of a subject with one notification closed. What a boundary's own tests
/// assert on: that its question, its alert or its end reached Notifications, about the
/// right subject, with the right content — never how Notifications stores it.
/// </summary>
internal sealed class RecordingNotifications : INotifications
{
    private readonly List<(NotificationSubject Subject, string Reason)> _closed = [];
    private readonly List<(Guid Id, NotificationDraft Draft)> _published = [];
    private readonly List<(Guid Id, string? Reason)> _closedById = [];
    private readonly Lock _gate = new();

    /// <summary>Thrown from <see cref="CloseForSubjectAsync"/> and <see cref="PublishAsync"/>
    /// when set, so a test can show the boundary's own write does not depend on them.</summary>
    public Exception? Fails { get; set; }

    /// <summary>Every subject closed so far, in order.</summary>
    public IReadOnlyList<(NotificationSubject Subject, string Reason)> Closed
    {
        get
        {
            lock (_gate) return [.. _closed];
        }
    }

    /// <summary>Every notification published so far, with the id it was given, in order.</summary>
    public IReadOnlyList<(Guid Id, NotificationDraft Draft)> Published
    {
        get
        {
            lock (_gate) return [.. _published];
        }
    }

    /// <summary>Every notification closed by its id so far, with its reason, in order.</summary>
    public IReadOnlyList<(Guid Id, string? Reason)> ClosedById
    {
        get
        {
            lock (_gate) return [.. _closedById];
        }
    }

    public Task<Result<Guid>> PublishAsync(NotificationDraft draft, CancellationToken cancellationToken = default)
    {
        if (Fails is { } failure) throw failure;

        var id = Guid.NewGuid();
        lock (_gate) _published.Add((id, draft));
        return Task.FromResult(Result.Success(id));
    }

    public Task<Result> CloseAsync(Guid notificationId, string? reason = null, CancellationToken cancellationToken = default)
    {
        lock (_gate) _closedById.Add((notificationId, reason));
        return Task.FromResult(Result.Success());
    }

    public Task<Result<int>> CloseForSubjectAsync(NotificationSubject subject, string reason, CancellationToken cancellationToken = default)
    {
        if (Fails is { } failure) throw failure;

        lock (_gate) _closed.Add((subject, reason));
        return Task.FromResult(Result.Success(1));
    }

    public Task<Result<NotificationAnswer?>> GetAnswerAsync(Guid notificationId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Result.Success<NotificationAnswer?>(null));
}
