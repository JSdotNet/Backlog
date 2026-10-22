using System.Collections.Concurrent;
using Backlog.Modules.Notifications.Abstractions;
using Backlog.Modules.Sync.Abstractions;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Backlog.Infrastructure.Sync;

/// <summary>
/// The pairings this desktop has open: one per pairing code it issued, named by an id
/// given on issue, so a notification can be about a pairing without naming the code
/// (<see cref="SyncNotificationSubjects"/>). A pairing ends when its code is redeemed or
/// expires, and every notification about it then closes with
/// <see cref="SyncNotificationSubjects.EndedReason"/>.
/// <para>
/// Kept in memory, as the code itself is: the desktop never stores a pairing code, so a
/// pairing does not outlive the process that issued it, and after a restart the probe
/// (<see cref="PairingSubjectProbe"/>) answers every earlier one ended. Expiry is the
/// service's ten minutes, as the issue answered it, timed here. Redemption is reported by
/// whatever learns of it — <see cref="RedeemedAsync"/> — because the service does not yet
/// tell the issuing desktop.
/// </para>
/// </summary>
public sealed class PairingSubjects : IDisposable
{
    private readonly Func<INotifications?> _notifications;
    private readonly TimeProvider _time;
    private readonly ILogger _logger;
    private readonly ConcurrentDictionary<Guid, Pairing> _open = new();

    public PairingSubjects(Func<INotifications?> notifications, TimeProvider? time = null, ILogger<PairingSubjects>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(notifications);

        _notifications = notifications;
        _time = time ?? TimeProvider.System;
        _logger = (ILogger?)logger ?? NullLogger.Instance;
    }

    /// <summary>The pairing most recently issued that is still open, or null — what a
    /// notification about the code on screen is about.</summary>
    public Guid? Current =>
        _open.Values.Where(pairing => pairing.ExpiresAt > _time.GetUtcNow())
            .OrderByDescending(pairing => pairing.IssuedAt)
            .Select(pairing => (Guid?)pairing.Id)
            .FirstOrDefault();

    /// <summary>Opens a pairing for a code the service just issued, timed to end when the
    /// code expires, and answers its id.</summary>
    public Guid Issued(PairingCodeResponse code)
    {
        ArgumentNullException.ThrowIfNull(code);

        var id = Guid.NewGuid();
        var now = _time.GetUtcNow();
        var due = code.ExpiresAt - now;

        // The timer exists, stopped, before the pairing is open, so an end — the expiry, or
        // a redemption racing it — always finds a timer to stop.
        var expiry = _time.CreateTimer(_ => _ = EndAsync(id), state: null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        _open[id] = new Pairing(id, now, code.ExpiresAt) { Expiry = expiry };

        expiry.Change(due > TimeSpan.Zero ? due : TimeSpan.Zero, Timeout.InfiniteTimeSpan);

        return id;
    }

    /// <summary>Whether <paramref name="pairingId"/> names a pairing still open here: issued
    /// by this process, not redeemed, not expired.</summary>
    public bool IsOpen(Guid pairingId) =>
        _open.TryGetValue(pairingId, out var pairing) && pairing.ExpiresAt > _time.GetUtcNow();

    /// <summary>Ends a pairing whose code was redeemed. Nothing for one already ended.</summary>
    public Task RedeemedAsync(Guid pairingId) => EndAsync(pairingId);

    /// <summary>The close the expiry timer started last; what a test awaits.</summary>
    internal Task LastEnd { get; private set; } = Task.CompletedTask;

    private Task EndAsync(Guid id)
    {
        if (!_open.TryRemove(id, out var pairing)) return Task.CompletedTask;

        pairing.Expiry?.Dispose();
        return LastEnd = CloseAsync(id);
    }

    private async Task CloseAsync(Guid id)
    {
        var subject = new NotificationSubject(SyncNotificationSubjects.Context, SyncNotificationSubjects.Pairing, SyncNotificationSubjects.Id(id));

        try
        {
            if (_notifications() is not { } notifications) return;

            var closed = await notifications.CloseForSubjectAsync(subject, SyncNotificationSubjects.EndedReason).ConfigureAwait(false);
            if (closed.IsFailure) _logger.LogDebug("Notifications about pairing {Id} were not closed: {Error}", id, closed.Error.Message);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Closing the notifications about pairing {Id} failed; the next start's probe closes them.", id);
        }
    }

    public void Dispose()
    {
        foreach (var pairing in _open.Values) pairing.Expiry?.Dispose();
        _open.Clear();
    }

    private sealed record Pairing(Guid Id, DateTimeOffset IssuedAt, DateTimeOffset ExpiresAt)
    {
        public ITimer? Expiry { get; set; }
    }
}
