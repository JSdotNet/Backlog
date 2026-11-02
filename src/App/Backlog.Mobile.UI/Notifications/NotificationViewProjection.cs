using Backlog.Infrastructure.Sync;
using Backlog.Infrastructure.Sync.Notifications;
using Backlog.Mobile.UI.Outbox;
using Backlog.Mobile.UI.Services;
using Backlog.Modules.Notifications.Abstractions;
using Backlog.Modules.Sync.Abstractions;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;

namespace Backlog.Mobile.UI.Notifications;

/// <summary>
/// The phone's notifications (local ADR 0039, <c>.devbook/domain/notifications/context.md</c>):
/// the owner's notification feed folded into <see cref="INotificationViewStore"/>, read back as
/// <see cref="PhoneNotification"/>s, and the one thing the phone writes — a reply to a question,
/// queued through the <see cref="DeviceOutbox"/> as a <see cref="ReplyOutboxKind"/> entry.
/// <para>
/// A viewer, never an owner: the phone decides nothing about a notification. A reply changes
/// nothing here but the phone's own record of it, until the owner desktop has accepted or
/// rejected it and the pull that follows brings its verdict back on the notification.
/// </para>
/// <para>
/// One per device, a singleton, for the reason the task projection is one.
/// </para>
/// </summary>
public sealed class NotificationViewProjection : IDisposable
{
    /// <summary>How long a settled notification stays on the phone after its owner last
    /// pushed it — the replica's own retention, after which no pull brings it again.</summary>
    public static readonly TimeSpan SettledRetention = TimeSpan.FromDays(30);

    private readonly INotificationViewStore _store;
    private readonly DeviceOutbox _outbox;
    private readonly CloudSyncClient _sync;
    private readonly IDeviceCredentialStore _credentials;
    private readonly TimeProvider _clock;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Lock _rowsLock = new();
    private readonly Dictionary<Guid, NotificationChangeRecord> _rows;
    private readonly List<NotificationReplyPost> _sent;

    public NotificationViewProjection(
        INotificationViewStore store,
        DeviceOutbox outbox,
        CloudSyncClient sync,
        IDeviceCredentialStore credentials,
        TimeProvider clock)
    {
        _store = store;
        _outbox = outbox;
        _sync = sync;
        _credentials = credentials;
        _clock = clock;

        // Read once, here: the pages are drawn from this before any pull answers.
        _rows = store.ReadRows().ToDictionary(row => row.Change.Id);
        _sent = [.. store.ReadSentReplies()];

        _outbox.Changed += OnOutboxChanged;
    }

    /// <summary>Raised when a notification changed, a reply was queued, or a queued reply
    /// left the outbox. May be raised off the render thread.</summary>
    public event Action? Changed;

    /// <summary>Whether the phone holds any notification to show — what puts the
    /// Notifications tab in the bar. A desktop pushes only with the <c>notifications</c>
    /// feature on, so a phone paired to desktops without it never shows the tab.</summary>
    public bool HoldsAny => Notifications().Count > 0;

    /// <summary>How many open questions this phone could answer now.</summary>
    public int Waiting => Notifications().Count(notification => notification.Standing == NotificationStanding.Answerable);

    /// <summary>
    /// Every notification the phone shows: the open ones first, newest first, then the settled
    /// ones by when they settled, newest first. A record this build cannot read, and a settled
    /// one the replica has expired by now, are passed over.
    /// </summary>
    public IReadOnlyList<PhoneNotification> Notifications()
    {
        List<NotificationChangeRecord> rows;
        lock (_rowsLock) rows = [.. _rows.Values];

        List<NotificationReplyPost> sent;
        lock (_rowsLock) sent = [.. _sent];

        var now = _clock.GetUtcNow();
        var queued = _outbox.Entries
            .Where(entry => entry.Kind == ReplyOutboxKind.Token)
            .ToDictionary(entry => entry.Id);
        var deviceId = _credentials.Current?.DeviceId;

        var read = rows
            .Select(row => (Row: row, Payload: NotificationPayloads.Read(row.Change)))
            .Where(item => item.Payload is not null)
            .Select(item => (item.Row, Payload: item.Payload!))
            .Where(item => item.Payload.Notification.State == NotificationState.Open || item.Row.Change.UpdatedAt >= now - SettledRetention)
            .ToList();

        // An owner's newest stamp on an open question: it pushes those again every few
        // minutes while it runs, so a stale stamp is a desktop asleep.
        var seen = read
            .Where(item => item.Payload.Notification is { State: NotificationState.Open, Kind: NotificationKind.Question })
            .GroupBy(item => item.Row.Change.OwnerInstanceId)
            .ToDictionary(group => group.Key, group => group.Max(item => item.Row.Change.UpdatedAt));

        return
        [
            .. read
                .Select(item => View(item.Row, item.Payload, sent, queued, deviceId, seen, now))
                .OrderByDescending(view => view.IsOpen)
                .ThenByDescending(view => view.IsOpen ? view.Notification.CreatedAt : view.Notification.SettledAt ?? view.Notification.CreatedAt)
                .ThenBy(view => view.Id)
        ];
    }

    /// <summary>The notification, or null when the phone holds none by that id.</summary>
    public PhoneNotification? Find(Guid id) => Notifications().FirstOrDefault(notification => notification.Id == id);

    /// <summary>
    /// Pulls the notification feed from its cursor until the service says it is drained,
    /// keeping each page and the cursor after it as one write. A cursor the service will not
    /// take is dropped and the pull starts again from the beginning, once; any other failure
    /// is thrown for the screen to word.
    /// </summary>
    public async Task PullAsync(CancellationToken cancellationToken = default)
    {
        if (_credentials.Current is null) return;

        await _gate.WaitAsync(cancellationToken);

        try
        {
            var since = _store.ReadCursor();
            var restarted = false;

            while (true)
            {
                PullNotificationsResponse page;

                try
                {
                    page = await _sync.PullNotificationsAsync(since, cancellationToken);
                }
                catch (SyncUnavailableException ex) when (!restarted
                    && ex.Code is SyncErrorCodes.SyncCursorMalformed or SyncErrorCodes.SyncCursorExpired)
                {
                    await _store.ResetCursorAsync(cancellationToken);
                    since = null;
                    restarted = true;
                    continue;
                }

                List<NotificationChangeRecord> changed;
                lock (_rowsLock)
                {
                    changed = [.. (page.Notifications ?? []).Where(record => !_rows.TryGetValue(record.Change.Id, out var held) || Wins(record, held))];
                }

                await _store.SaveAsync(changed, page.Since, cancellationToken);

                lock (_rowsLock)
                {
                    foreach (var record in changed) _rows[record.Change.Id] = record;
                }

                since = page.Since;

                if (changed.Count > 0) Changed?.Invoke();
                if (!page.HasMore) return;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Answers the question with <paramref name="choiceIds"/> or <paramref name="text"/>, and
    /// <paramref name="note"/> beside it where the question takes a note: keeps
    /// the reply as this phone's, then queues it, so a reply made with no signal is kept and
    /// sent once there is one. Answers null when the notification is not one the phone may
    /// answer now, or the reply does not fit it.
    /// </summary>
    public async Task<NotificationReplyPost?> ReplyAsync(
        Guid notificationId,
        IReadOnlyList<string> choiceIds,
        string? text,
        string? note = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(choiceIds);

        if (Find(notificationId) is not { CanAnswer: true } notification) return null;

        var trimmed = string.IsNullOrWhiteSpace(text) ? null : text.Trim();
        var trimmedNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (!NotificationStandings.Fits(notification.Notification.AnswerSpec, choiceIds, trimmed, trimmedNote)) return null;

        var reply = new NotificationReplyPost(
            Guid.CreateVersion7(),
            notification.Id,
            notification.Notification.OwnerInstanceId,
            [.. choiceIds],
            trimmed,
            _clock.GetUtcNow(),
            trimmedNote);

        await _store.AddSentReplyAsync(reply, cancellationToken);
        lock (_rowsLock) _sent.Add(reply);

        await _outbox.EnqueueAsync(ReplyOutboxKind.Token, reply.Id, ReplyOutboxKind.Write(reply), cancellationToken);

        Changed?.Invoke();
        return reply;
    }

    public void Dispose()
    {
        _outbox.Changed -= OnOutboxChanged;
        _gate.Dispose();
    }

    /// <summary>The newer of two copies of one notification: its owner stamps every push later
    /// than the last, and the store's stamp settles a tie.</summary>
    internal static bool Wins(NotificationChangeRecord incoming, NotificationChangeRecord held) =>
        incoming.Change.UpdatedAt > held.Change.UpdatedAt
        || (incoming.Change.UpdatedAt == held.Change.UpdatedAt && incoming.ServerTimestamp > held.ServerTimestamp);

    private static PhoneNotification View(
        NotificationChangeRecord row,
        NotificationPayload payload,
        IReadOnlyList<NotificationReplyPost> sent,
        IReadOnlyDictionary<Guid, OutboxEntry> queued,
        Guid? deviceId,
        IReadOnlyDictionary<Guid, DateTimeOffset> seen,
        DateTimeOffset now)
    {
        var mine = sent.Where(reply => reply.NotificationId == row.Change.Id).ToList();
        var (standing, verdict) = NotificationStandings.Of(payload, deviceId, mine, id => queued.GetValueOrDefault(id));

        var notification = payload.Notification;
        var seenAt = seen.TryGetValue(row.Change.OwnerInstanceId, out var at) ? at : (DateTimeOffset?)null;
        var presence = notification is { State: NotificationState.Open, Kind: NotificationKind.Question } && seenAt is { } stamp
            ? now - stamp <= NotificationSyncSession.PresenceWindow ? OwnerPresence.Online : OwnerPresence.Offline
            : OwnerPresence.Unknown;

        var waiting = mine.LastOrDefault() is { } last && queued.TryGetValue(last.Id, out var entry) ? entry : null;

        return new PhoneNotification(notification, payload.OwnerName, presence, seenAt, standing, verdict, waiting);
    }

    /// <summary>A reply leaving the outbox, or parking there, moves its card.</summary>
    private void OnOutboxChanged() => Changed?.Invoke();
}
