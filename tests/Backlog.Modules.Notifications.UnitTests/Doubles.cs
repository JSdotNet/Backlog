using Backlog.Modules.Notifications.Abstractions;
using Backlog.Modules.Notifications.DomainModels;
using Backlog.SharedKernel;

namespace Backlog.Modules.Notifications.UnitTests;

/// <summary>The drafts and replies the tests start from, and the owner they are published on.</summary>
internal static class Drafts
{
    public static readonly Guid Owner = Guid.Parse("7f3a0000-0000-0000-0000-000000000001");
    public static readonly Guid Phone = Guid.Parse("9b1c0000-0000-0000-0000-000000000002");
    public static readonly Guid OtherDesktop = Guid.Parse("4d2e0000-0000-0000-0000-000000000003");

    public static readonly DateTimeOffset Now = new(2026, 10, 22, 9, 41, 0, TimeSpan.Zero);

    public static readonly NotificationSource Sessions = new("sessions", "run-58");
    public static readonly NotificationSubject Session = new("sessions", "session", "sess-91");

    public static AnswerSpec YesNo(AnswerOn answerOn = AnswerOn.AnyInstance) =>
        new([new AnswerChoice("yes", "Yes, run it", AnswerChoiceStyle.Primary), new AnswerChoice("no", "Not now")], AnswerOn: answerOn);

    public static NotificationDraft Question(AnswerSpec? spec = null, NotificationSubject? subject = null) =>
        new(Sessions, subject ?? Session, NotificationKind.Question, NotificationSeverity.Warning,
            "Run the migration against staging?", "0042_add_device_kind", "3 tables changed.", spec ?? YesNo());

    public static NotificationDraft Alert(NotificationSubject? subject = null) =>
        new(new NotificationSource("devbook", "remark-12"), subject, NotificationKind.Alert, NotificationSeverity.Info,
            "Remark left on Storage and sync", "Does the reply container need its own TTL?", string.Empty);

    public static Notification Published(NotificationDraft? draft = null) =>
        Notification.Publish(Guid.NewGuid(), Owner, draft ?? Question(), Now).Value;

    public static NotificationReply Reply(
        Notification notification,
        Guid? from = null,
        string[]? choices = null,
        string? text = null,
        Guid? ownerInstance = null,
        string? note = null) =>
        new(Guid.NewGuid(), notification.Id, ownerInstance ?? notification.OwnerInstanceId, from ?? Phone, choices ?? [], text, Now, note);
}

internal sealed class FixedDevice(Guid id) : IDeviceIdentitySource
{
    public DeviceIdentity Current { get; } = new(id, "WORKSTATION-01");
}

/// <summary>An in-memory <see cref="INotificationRepository"/> that stores each notification as a
/// fresh copy, the way a database row would, so a test sees only what was saved.</summary>
internal sealed class InMemoryNotificationRepository : INotificationRepository
{
    private readonly Dictionary<Guid, Notification> _rows = [];
    private readonly Lock _lock = new();

    public int Saves { get; private set; }

    public Task<Notification?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        lock (_lock) return Task.FromResult(_rows.TryGetValue(id, out var row) ? Copy(row) : null);
    }

    public Task<IReadOnlyList<Notification>> ListOpenAsync(CancellationToken cancellationToken = default)
    {
        lock (_lock) return Task.FromResult<IReadOnlyList<Notification>>([.. _rows.Values.Where(row => row.IsOpen).Select(Copy)]);
    }

    public Task<IReadOnlyList<Notification>> ListAwaitingDeliveryAsync(CancellationToken cancellationToken = default)
    {
        lock (_lock) return Task.FromResult<IReadOnlyList<Notification>>([.. _rows.Values.Where(row => row.AwaitsDelivery).Select(Copy)]);
    }

    public Task<IReadOnlyList<Notification>> ListOpenOrSettledSinceAsync(DateTimeOffset settledSince, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            return Task.FromResult<IReadOnlyList<Notification>>(
                [.. _rows.Values
                    .Where(row => row.IsOpen || row.SettledAt >= settledSince)
                    .OrderByDescending(row => row.CreatedAt)
                    .Select(Copy)]);
        }
    }

    public Task SaveAsync(Notification notification, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            _rows[notification.Id] = Copy(notification);
            Saves++;
        }

        return Task.CompletedTask;
    }

    public void Put(Notification notification)
    {
        lock (_lock) _rows[notification.Id] = Copy(notification);
    }

    private static Notification Copy(Notification n) => Notification.Restore(
        n.Id, n.OwnerInstanceId, n.Source, n.Subject, n.Kind, n.Severity, n.Title, n.Summary, n.BodyMarkdown,
        n.AnswerSpec, n.DeepLink, n.IsPrivate, n.CreatedAt, n.State, n.SettledAt, n.CloseReason, n.Answer, n.AnswerDeliveredAt);
}

/// <summary>A handler that records every answer it is handed, and can be told to throw.</summary>
internal sealed class RecordingAnswerHandler(string source = "sessions") : INotificationAnswerHandler
{
    public string Source { get; } = source;

    public List<NotificationAnswer> Received { get; } = [];

    public bool Throws { get; set; }

    public Task HandleAnswerAsync(NotificationAnswer answer, CancellationToken cancellationToken = default)
    {
        if (Throws) throw new InvalidOperationException("The handler is not ready.");

        Received.Add(answer);
        return Task.CompletedTask;
    }
}

/// <summary>A handler that records every answer from any thread, yielding first so concurrent
/// deliveries overlap.</summary>
internal sealed class ConcurrentRecordingHandler : INotificationAnswerHandler
{
    private readonly System.Collections.Concurrent.ConcurrentQueue<NotificationAnswer> _received = new();

    public string Source => "sessions";

    public IReadOnlyList<NotificationAnswer> Received => [.. _received];

    public async Task HandleAnswerAsync(NotificationAnswer answer, CancellationToken cancellationToken = default)
    {
        await Task.Yield();
        _received.Enqueue(answer);
    }
}

/// <summary>A probe that answers from a set of ended subject ids.</summary>
internal sealed class SetProbe(string context, params string[] ended) : INotificationSubjectProbe
{
    public string Context { get; } = context;

    public List<NotificationSubject> Asked { get; } = [];

    public bool Throws { get; set; }

    public Task<bool> IsLiveAsync(NotificationSubject subject, CancellationToken cancellationToken = default)
    {
        if (Throws) throw new InvalidOperationException("The context is not ready.");

        Asked.Add(subject);
        return Task.FromResult(!ended.Contains(subject.Id));
    }
}
