using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Backlog.Mobile.UI.Outbox;

/// <summary>
/// Everything the phone has to send, sent in the order it was made.
/// <para>
/// A capture is written here before anything touches the network, so a phone
/// in a conference hall with no signal keeps what was typed and says it is
/// waiting — it never hands the person an error and an empty field. The flush
/// sends the oldest entry first and stops at the first one the network would
/// not take: whatever is behind it waits behind it, and nothing overtakes it.
/// That is what lets a later kind rely on order (an attachment upload queued
/// ahead of the capture that names it).
/// </para>
/// <para>
/// <b>Backoff.</b> A transient failure — no network, a timeout, a 5xx, a token
/// the service no longer accepts — is retried after <see cref="FirstRetryDelay"/>,
/// doubling each time: 2s, 4s, 8s, 16s. The fifth failure parks the entry
/// ("waiting — tap to retry"), and the entries behind it keep waiting behind it.
/// </para>
/// <para>
/// <b>Refusal.</b> A 4xx the service will give again for the same entry marks
/// it <see cref="OutboxEntry.Refused"/>. It is set aside — retrying would only
/// repeat the answer — and the queue moves past it. It goes again only if tapped.
/// </para>
/// <para>
/// <b>Resume.</b> Coming back to the app and the network coming back both call
/// <see cref="ResumeAsync"/>, which drops whatever backoff is pending, gives an
/// entry the network parked its attempts back, and tries now: the reason for
/// the wait has most likely just gone away. A refused entry stays set aside.
/// </para>
/// <para>
/// One per device — a singleton in both hosts — because there is one file and
/// two flushes over it would send the same entry twice.
/// </para>
/// </summary>
public sealed class DeviceOutbox : IDisposable
{
    /// <summary>Attempts the outbox makes on its own before it parks an entry.</summary>
    public const int MaxAttempts = 5;

    /// <summary>The wait after the first failure; each later one doubles it.</summary>
    public static readonly TimeSpan FirstRetryDelay = TimeSpan.FromSeconds(2);

    private readonly IDeviceStore _store;
    private readonly IReadOnlyDictionary<string, IOutboxKind> _kinds;
    private readonly TimeProvider _clock;
    private readonly ILogger _logger;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Lock _entriesLock = new();
    private readonly List<OutboxEntry> _entries;

    private DateTimeOffset? _notBefore;
    private ITimer? _retryTimer;

    public DeviceOutbox(
        IDeviceStore store,
        IEnumerable<IOutboxKind> kinds,
        TimeProvider clock,
        ILogger<DeviceOutbox>? logger = null)
    {
        _store = store;
        _kinds = kinds.ToDictionary(kind => kind.Kind, StringComparer.Ordinal);
        _clock = clock;
        _logger = logger ?? NullLogger<DeviceOutbox>.Instance;

        // Read once, here: the list is drawn from this, and the first render
        // cannot wait for a file.
        _entries = [.. store.ReadOutbox()];
    }

    /// <summary>Raised whenever an entry is added, changes or leaves. May be
    /// raised off the render thread.</summary>
    public event Action? Changed;

    /// <summary>Raised when the service has taken an entry. The Inbox refreshes
    /// on it: the capture has left the outbox and is now the service's.</summary>
    public event Action<OutboxEntry>? Delivered;

    /// <summary>Every entry still to send, oldest first, parked ones included.</summary>
    public IReadOnlyList<OutboxEntry> Entries
    {
        get
        {
            lock (_entriesLock) return [.. _entries];
        }
    }

    /// <summary>The last attempt could not reach the service. Cleared by the next
    /// delivery.</summary>
    public bool Unreachable { get; private set; }

    /// <summary>The wait after <paramref name="failedAttempts"/> failures.</summary>
    public static TimeSpan DelayAfter(int failedAttempts) =>
        FirstRetryDelay * Math.Pow(2, Math.Max(0, failedAttempts - 1));

    /// <summary>
    /// Keeps the entry and starts a flush without waiting for it: the caller has
    /// what it needs to show the entry as waiting the moment this returns.
    /// </summary>
    public async Task<OutboxEntry> EnqueueAsync(string kind, Guid id, string payloadJson, CancellationToken cancellationToken = default)
    {
        var entry = new OutboxEntry(id, kind, payloadJson, Attempts: 0, LastError: null, _clock.GetUtcNow());

        await _store.AddAsync(entry, cancellationToken);
        lock (_entriesLock) _entries.Add(entry);
        Changed?.Invoke();

        _ = FlushInBackgroundAsync(immediately: false);

        return entry;
    }

    /// <summary>Sends what is due, oldest first, and schedules the next try.</summary>
    public Task FlushAsync(CancellationToken cancellationToken = default) =>
        FlushAsync(immediately: false, cancellationToken);

    /// <summary>The app came back, or the network did: try now rather than when
    /// the backoff says, network-parked entries included.</summary>
    public Task ResumeAsync(CancellationToken cancellationToken = default) =>
        FlushAsync(immediately: true, cancellationToken);

    /// <summary><see cref="ResumeAsync"/> for a host event nobody awaits — a
    /// window resuming, the network coming back.</summary>
    public void Resume() => _ = FlushInBackgroundAsync(immediately: true);

    /// <summary>The person tapped a parked entry: it starts again from no attempts.</summary>
    public async Task RetryAsync(Guid id, CancellationToken cancellationToken = default)
    {
        OutboxEntry? reset = null;

        lock (_entriesLock)
        {
            var index = _entries.FindIndex(entry => entry.Id == id);
            if (index >= 0)
            {
                reset = _entries[index] with { Attempts = 0, LastError = null, Refused = false };
                _entries[index] = reset;
            }
        }

        if (reset is null) return;

        await _store.UpdateAsync(reset, cancellationToken);
        Changed?.Invoke();

        await FlushAsync(immediately: true, cancellationToken);
    }

    /// <summary>Completes once no flush is running. The tests wait on it after
    /// moving the clock, because a retry timer's flush is not awaited by anyone.</summary>
    internal async Task WhenIdleAsync()
    {
        await _gate.WaitAsync();
        _gate.Release();
    }

    public void Dispose()
    {
        _retryTimer?.Dispose();
        _gate.Dispose();
    }

    private async Task FlushAsync(bool immediately, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);

        try
        {
            if (immediately)
            {
                _notBefore = null;
                await UnparkAsync(cancellationToken);
            }

            await FlushDueAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>For the callers nobody awaits — an enqueue, a timer. A failure
    /// here is the store's, and it is logged rather than lost: the entry is still
    /// in the file, and the next resume tries again.</summary>
    private async Task FlushInBackgroundAsync(bool immediately)
    {
        try
        {
            await FlushAsync(immediately, CancellationToken.None);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "The outbox flush failed; its entries stay queued for the next one.");
        }
    }

    private async Task FlushDueAsync(CancellationToken cancellationToken)
    {
        foreach (var entry in Entries)
        {
            if (entry.Refused) continue;

            // Parked by the network, not refused: it is still first in line, and
            // sending what is behind it would deliver them out of order.
            if (entry.IsParked) return;

            var now = _clock.GetUtcNow();
            if (_notBefore is { } due && now < due)
            {
                Schedule(due - now);
                return;
            }

            var delivery = await SendAsync(entry, cancellationToken);

            switch (delivery.Kind)
            {
                case OutboxDeliveryKind.Delivered:
                    await _store.RemoveAsync(entry.Id, cancellationToken);
                    lock (_entriesLock) _entries.RemoveAll(queued => queued.Id == entry.Id);
                    _notBefore = null;
                    Unreachable = false;
                    Changed?.Invoke();
                    Delivered?.Invoke(entry);
                    continue;

                case OutboxDeliveryKind.Refused:
                    // Not a network problem, and not one a timer will fix — so it
                    // is set aside rather than holding up the entries behind it.
                    await ReplaceAsync(entry with { Refused = true, LastError = delivery.Error }, cancellationToken);
                    Changed?.Invoke();
                    continue;

                default:
                    var failed = entry with { Attempts = entry.Attempts + 1, LastError = delivery.Error };
                    await ReplaceAsync(failed, cancellationToken);
                    Unreachable = true;
                    Changed?.Invoke();

                    if (failed.IsParked)
                    {
                        _notBefore = null;
                        return;
                    }

                    var delay = DelayAfter(failed.Attempts);
                    _notBefore = now + delay;
                    Schedule(delay);
                    return;
            }
        }
    }

    /// <summary>Gives every entry the network parked its attempts back.</summary>
    private async Task UnparkAsync(CancellationToken cancellationToken)
    {
        foreach (var entry in Entries.Where(entry => entry.IsParked && !entry.Refused))
        {
            await ReplaceAsync(entry with { Attempts = 0 }, cancellationToken);
        }
    }

    private async Task<OutboxDelivery> SendAsync(OutboxEntry entry, CancellationToken cancellationToken)
    {
        if (!_kinds.TryGetValue(entry.Kind, out var kind))
        {
            return OutboxDelivery.Refused($"Nothing on this phone sends a '{entry.Kind}' entry.");
        }

        try
        {
            return await kind.SendAsync(entry, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // A kind reports what it can classify; anything that still escapes is
            // treated as the network's, which is the case a retry can cure.
            return OutboxDelivery.Transient(ex.Message);
        }
    }

    private async Task ReplaceAsync(OutboxEntry entry, CancellationToken cancellationToken)
    {
        await _store.UpdateAsync(entry, cancellationToken);

        lock (_entriesLock)
        {
            var index = _entries.FindIndex(queued => queued.Id == entry.Id);
            if (index >= 0) _entries[index] = entry;
        }
    }

    private void Schedule(TimeSpan delay)
    {
        _retryTimer?.Dispose();
        _retryTimer = _clock.CreateTimer(
            _ => _ = FlushInBackgroundAsync(immediately: false),
            state: null,
            delay,
            Timeout.InfiniteTimeSpan);
    }
}
