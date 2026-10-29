using Backlog.Modules.Notifications.Abstractions;
using Backlog.SharedKernel.Results;

namespace Backlog.Infrastructure.SpecManager.UnitTests;

/// <summary>
/// The owner's notifications as the question source sees them: what it publishes — kept by
/// the id the draft names, and published once — what it closes, and which of them are open.
/// Enough of the owner's behaviour for the source's rules to be asserted: publishing a held
/// id changes nothing, and closing a settled or unknown one is refused.
/// </summary>
internal sealed class NotificationLedger : INotifications, INotificationReplication, INotificationAnswerReplay
{
    private readonly Lock _gate = new();
    private readonly Dictionary<Guid, Entry> _entries = [];

    public int Replays { get; private set; }

    public IReadOnlyList<(Guid Id, NotificationDraft Draft)> Published
    {
        get
        {
            lock (_gate) return [.. _entries.Select(entry => (entry.Key, entry.Value.Draft))];
        }
    }

    public NotificationDraft Draft(Guid id)
    {
        lock (_gate) return _entries[id].Draft;
    }

    public NotificationState StateOf(Guid id)
    {
        lock (_gate) return _entries[id].State;
    }

    public string? ReasonOf(Guid id)
    {
        lock (_gate) return _entries[id].Reason;
    }

    public Task<Result<Guid>> PublishAsync(NotificationDraft draft, CancellationToken cancellationToken = default)
    {
        var id = draft.Id ?? Guid.NewGuid();
        lock (_gate) _entries.TryAdd(id, new Entry(draft, NotificationState.Open, null));
        return Task.FromResult(Result.Success(id));
    }

    public Task<Result> CloseAsync(Guid notificationId, string? reason = null, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (!_entries.TryGetValue(notificationId, out var entry)) return Task.FromResult(Result.Failure(NotificationsErrors.NotFound(notificationId)));
            if (entry.State != NotificationState.Open) return Task.FromResult(Result.Failure(NotificationsErrors.AlreadySettled(entry.State)));

            _entries[notificationId] = entry with { State = NotificationState.Closed, Reason = reason };
            return Task.FromResult(Result.Success());
        }
    }

    /// <summary>Settles a question as answered here, the way the owner does once it accepts a
    /// reply.</summary>
    public void Answer(Guid id)
    {
        lock (_gate) _entries[id] = _entries[id] with { State = NotificationState.Answered };
    }

    /// <summary>Settles an alert as acknowledged, the way the owner does on any channel's
    /// acknowledgement — the phone's included.</summary>
    public void Acknowledge(Guid id)
    {
        lock (_gate) _entries[id] = _entries[id] with { State = NotificationState.Acknowledged };
    }

    public Task<Result<int>> CloseForSubjectAsync(NotificationSubject subject, string reason, CancellationToken cancellationToken = default) =>
        Task.FromResult(Result.Success(0));

    public Task<Result<NotificationAnswer?>> GetAnswerAsync(Guid notificationId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Result.Success<NotificationAnswer?>(null));

    public Task<IReadOnlyList<NotificationSnapshot>> ListOpenAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<NotificationSnapshot>>(
                [.. _entries.Where(entry => entry.Value.State == NotificationState.Open).Select(entry => Snapshot(entry.Key, entry.Value))]);
        }
    }

    public Task<NotificationSnapshot?> GetAsync(Guid notificationId, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            return Task.FromResult(_entries.TryGetValue(notificationId, out var entry) ? Snapshot(notificationId, entry) : null);
        }
    }

    public Task<int> ReplayAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate) Replays++;
        return Task.FromResult(0);
    }

    private static NotificationSnapshot Snapshot(Guid id, Entry entry) => new(
        id,
        Guid.Empty,
        entry.Draft.Source,
        entry.Draft.Subject,
        entry.Draft.Kind,
        entry.Draft.Severity,
        entry.Draft.Title,
        entry.Draft.Summary,
        entry.Draft.BodyMarkdown,
        entry.Draft.Answer,
        entry.Draft.DeepLink,
        entry.Draft.IsPrivate,
        DateTimeOffset.UnixEpoch,
        entry.State,
        null,
        entry.Reason,
        null,
        entry.Draft.Origin);

    private sealed record Entry(NotificationDraft Draft, NotificationState State, string? Reason);
}
