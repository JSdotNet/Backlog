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
/// (<see cref="SyncNotificationSubjects"/>).
/// <para>
/// While a code waits to be redeemed, the owner's device list is read every
/// <see cref="PollInterval"/> through <see cref="IDeviceRoster"/>, because the service does
/// not tell the issuing desktop when its code is redeemed. A device this desktop's code
/// paired is the redemption: the pairing stops expiring and the person is asked on this
/// desktop, and only here, "Pair &lt;device name&gt;?" — Allow or Refuse
/// (<c>.devbook/domain/notifications/context.md</c>). The pairing ends when that question is
/// settled (<see cref="SettledAsync"/>), or when the code expires unredeemed; every
/// notification about it then closes with <see cref="SyncNotificationSubjects.EndedReason"/>.
/// A code is watched for <see cref="ExpiryGrace"/> past its expiry, with one last read
/// before it ends, so a device that redeemed it in its final seconds is still asked about.
/// </para>
/// <para>
/// Kept in memory, as the code itself is: the desktop never stores a pairing code. A
/// question about a redeemed pairing must not vanish with the process, though — the device
/// is paired, and closing the question unanswered would keep it without the person saying
/// so. So the probe (<see cref="PairingSubjectProbe"/>) calls a pairing ended only when this
/// process saw it end, and the question from an earlier run stays open until it is
/// answered; <see cref="PairingAnswers"/> reads the device from its source key.
/// </para>
/// </summary>
public sealed class PairingSubjects : IDisposable
{
    /// <summary>How often the device list is read while a code waits to be redeemed.</summary>
    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    /// <summary>How long past its expiry a code is still watched: a redemption in the code's
    /// last seconds shows up in the device list a read later, and the service's clock and this
    /// one disagree a little.</summary>
    public static readonly TimeSpan ExpiryGrace = TimeSpan.FromSeconds(30);

    /// <summary>How much earlier than the code's issue a device may be stamped and still be
    /// taken as its redemption, for the same disagreement between clocks.</summary>
    private static readonly TimeSpan ClockAllowance = TimeSpan.FromMinutes(1);

    private readonly Func<INotifications?> _notifications;
    private readonly Func<IDeviceRoster?> _roster;
    private readonly TimeProvider _time;
    private readonly ILogger _logger;
    private readonly ConcurrentDictionary<Guid, Pairing> _open = new();
    private readonly ConcurrentDictionary<Guid, byte> _ended = new();
    private readonly ConcurrentDictionary<Guid, byte> _claimed = new();
    private readonly SemaphoreSlim _reading = new(1, 1);
    private readonly Lock _schedule = new();
    private readonly ITimer _poll;

    public PairingSubjects(
        Func<INotifications?> notifications,
        TimeProvider? time = null,
        ILogger<PairingSubjects>? logger = null,
        Func<IDeviceRoster?>? roster = null)
    {
        ArgumentNullException.ThrowIfNull(notifications);

        _notifications = notifications;
        _roster = roster ?? (() => null);
        _time = time ?? TimeProvider.System;
        _logger = (ILogger?)logger ?? NullLogger.Instance;
        _poll = _time.CreateTimer(_ => LastPoll = PollAsync(), state: null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    /// <summary>The pairing most recently issued whose code still works, or null — what a
    /// notification about the code on screen is about.</summary>
    public Guid? Current =>
        _open.Values.Where(pairing => pairing.RedeemedBy is null && pairing.ExpiresAt > _time.GetUtcNow())
            .OrderByDescending(pairing => pairing.IssuedAt)
            .Select(pairing => (Guid?)pairing.Id)
            .FirstOrDefault();

    /// <summary>Opens a pairing for a code the service just issued, timed to end once the
    /// code has expired, starts watching for its redemption, and answers its id.</summary>
    public Guid Issued(PairingCodeResponse code)
    {
        ArgumentNullException.ThrowIfNull(code);

        var id = Guid.NewGuid();
        var now = _time.GetUtcNow();
        var due = code.ExpiresAt + ExpiryGrace - now;

        // The timer exists, stopped, before the pairing is open, so an end — the expiry, or
        // an answer racing it — always finds a timer to stop.
        var expiry = _time.CreateTimer(_ => LastEnd = ExpireAsync(id), state: null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);

        lock (_schedule)
        {
            _open[id] = new Pairing(id, now, code.ExpiresAt) { Expiry = expiry };
            _poll.Change(PollInterval, PollInterval);
        }

        expiry.Change(due > TimeSpan.Zero ? due : TimeSpan.Zero, Timeout.InfiniteTimeSpan);

        return id;
    }

    /// <summary>Whether <paramref name="pairingId"/> names a pairing still open here: issued
    /// by this process, and either waiting for its code to be redeemed or redeemed with the
    /// question about its device not yet settled.</summary>
    public bool IsOpen(Guid pairingId) => _open.ContainsKey(pairingId);

    /// <summary>Whether this process saw <paramref name="pairingId"/> end: its code expired
    /// unredeemed, or the question about its device was settled.</summary>
    public bool HasEnded(Guid pairingId) => _ended.ContainsKey(pairingId);

    /// <summary>
    /// A device redeemed the code of <paramref name="pairingId"/>: the pairing stops
    /// expiring, and the question "Pair &lt;device name&gt;?" is published on this desktop,
    /// to be answered here only. Nothing for a pairing already redeemed or ended.
    /// </summary>
    public Task RedeemedAsync(Guid pairingId, Guid deviceId, string deviceName)
    {
        if (!_open.TryGetValue(pairingId, out var pairing)) return Task.CompletedTask;

        lock (pairing)
        {
            if (pairing.Ended || pairing.RedeemedBy is not null) return Task.CompletedTask;

            pairing.RedeemedBy = deviceId;
        }

        _claimed[deviceId] = 0;
        pairing.Expiry?.Dispose();
        StopPollingWhenNothingWaits();

        return LastAsk = AskAsync(pairing, deviceId, deviceName);
    }

    /// <summary>The question about the device that redeemed <paramref name="pairingId"/> was
    /// settled: the pairing ends. Nothing for one already ended.</summary>
    public Task SettledAsync(Guid pairingId) => EndAsync(pairingId);

    /// <summary>The close the last end started; what a test awaits.</summary>
    internal Task LastEnd { get; private set; } = Task.CompletedTask;

    /// <summary>The publish the last redemption started; what a test awaits.</summary>
    internal Task LastAsk { get; private set; } = Task.CompletedTask;

    /// <summary>The device-list read the poll timer started last; what a test awaits.</summary>
    internal Task LastPoll { get; private set; } = Task.CompletedTask;

    /// <summary>The code's window and its grace are over: one last read of the device list,
    /// then the pairing ends unless that read found its redemption.</summary>
    private async Task ExpireAsync(Guid id)
    {
        if (!_open.TryGetValue(id, out var pairing) || pairing.RedeemedBy is not null) return;

        await ReadAsync(final: id).ConfigureAwait(false);

        lock (pairing)
        {
            if (pairing.RedeemedBy is not null) return;

            pairing.Ended = true;
        }

        await EndAsync(id).ConfigureAwait(false);
    }

    private Task EndAsync(Guid id)
    {
        if (!_open.TryRemove(id, out var pairing)) return Task.CompletedTask;

        lock (pairing) pairing.Ended = true;

        _ended[id] = 0;
        pairing.Expiry?.Dispose();
        StopPollingWhenNothingWaits();

        return LastEnd = CloseAsync(id);
    }

    private void StopPollingWhenNothingWaits()
    {
        lock (_schedule)
        {
            if (!_open.Values.Any(Waiting)) _poll.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }
    }

    private bool Waiting(Pairing pairing) =>
        pairing.RedeemedBy is null && !pairing.Ended && pairing.ExpiresAt + ExpiryGrace > _time.GetUtcNow();

    private Task PollAsync()
    {
        if (!_open.Values.Any(Waiting))
        {
            StopPollingWhenNothingWaits();
            return Task.CompletedTask;
        }

        return ReadAsync(final: null);
    }

    /// <summary>One read of the device list, attributing each device this desktop's codes
    /// paired to the newest waiting code issued before it came in. One read at a time: a slow
    /// service must not stack reads up behind the timer. A tick that finds a read under way
    /// skips; the final read before an expiry waits for it and reads again.</summary>
    private async Task ReadAsync(Guid? final)
    {
        if (final is null)
        {
            if (!_reading.Wait(0)) return;
        }
        else
        {
            await _reading.WaitAsync().ConfigureAwait(false);
        }

        try
        {
            if (_roster() is not { } roster) return;

            var listed = await roster.ListDevicesAsync().ConfigureAwait(false);
            if (listed.IsFailure)
            {
                _logger.LogDebug("The device list was not read while a pairing code is open: {Error}", listed.Error.Message);
                return;
            }

            foreach (var device in listed.Value.Devices.Where(device => device.PairedBy == listed.Value.CallerDeviceId))
            {
                if (_claimed.ContainsKey(device.DeviceId)) continue;

                var redeemed = _open.Values
                    .Where(pairing => Waiting(pairing) || pairing.Id == final)
                    .Where(pairing => pairing.RedeemedBy is null && device.RegisteredAt >= pairing.IssuedAt - ClockAllowance)
                    .OrderByDescending(pairing => pairing.IssuedAt)
                    .FirstOrDefault();

                if (redeemed is not null) await RedeemedAsync(redeemed.Id, device.DeviceId, device.Name).ConfigureAwait(false);
            }
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Reading the device list while a pairing code is open failed; the next read tries again.");
        }
        finally
        {
            _reading.Release();
        }
    }

    private async Task AskAsync(Pairing pairing, Guid deviceId, string deviceName)
    {
        try
        {
            if (_notifications() is not { } notifications) return;

            var asked = await notifications.PublishAsync(PairingQuestion.Draft(pairing.Id, deviceId, deviceName)).ConfigureAwait(false);
            if (asked.IsFailure) _logger.LogDebug("The question about the device that redeemed pairing {Id} was not published: {Error}", pairing.Id, asked.Error.Message);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Publishing the question about the device that redeemed pairing {Id} failed; the device stays paired.", pairing.Id);
        }
    }

    private async Task CloseAsync(Guid id)
    {
        try
        {
            if (_notifications() is not { } notifications) return;

            var closed = await notifications.CloseForSubjectAsync(PairingQuestion.Subject(id), SyncNotificationSubjects.EndedReason).ConfigureAwait(false);
            if (closed.IsFailure) _logger.LogDebug("Notifications about pairing {Id} were not closed: {Error}", id, closed.Error.Message);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Closing the notifications about pairing {Id} failed.", id);
        }
    }

    public void Dispose()
    {
        _poll.Dispose();
        foreach (var pairing in _open.Values) pairing.Expiry?.Dispose();
        _open.Clear();
        _reading.Dispose();
    }

    private sealed record Pairing(Guid Id, DateTimeOffset IssuedAt, DateTimeOffset ExpiresAt)
    {
        public ITimer? Expiry { get; set; }

        public Guid? RedeemedBy { get; set; }

        public bool Ended { get; set; }
    }
}
