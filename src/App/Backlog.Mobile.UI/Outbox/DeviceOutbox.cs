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
/// What waits behind it about it — an edit of a capture the service never got —
/// is folded into it first (<see cref="IOutboxFold"/>), so the queue moving past
/// it never sends a change of something the service does not have. A withdrawal
/// of it takes it out with the withdrawal, neither sent.
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

    /// <summary>The wait after an entry was held for a service newer than this
    /// app: the time until the compatibility check asks the service again, so
    /// the retry meets a fresh verdict rather than the cached one.</summary>
    public static readonly TimeSpan HeldRetryDelay = Backlog.Infrastructure.Sync.SyncCompatibility.VerdictWindow;

    private readonly IDeviceStore _store;
    private readonly IReadOnlyDictionary<string, IOutboxKind> _kinds;
    private readonly IReadOnlyList<IOutboxFold> _folds;
    private readonly TimeProvider _clock;
    private readonly ILogger _logger;

    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>One change to the entries at a time, in memory and on the device
    /// together, so the file never takes them in another order than the list did.
    /// Separate from <see cref="_gate"/>, which a flush holds for a whole send.</summary>
    private readonly SemaphoreSlim _writes = new(1, 1);
    private readonly Lock _entriesLock = new();
    private readonly List<OutboxEntry> _entries;

    /// <summary>Entries <see cref="RemoveAsync"/> is taking off the device, under
    /// <see cref="_entriesLock"/>: a flush does not claim one, so an entry is never
    /// sent while the file is losing it.</summary>
    private readonly HashSet<Guid> _removing = [];

    private DateTimeOffset? _notBefore;
    private ITimer? _retryTimer;

    public DeviceOutbox(
        IDeviceStore store,
        IEnumerable<IOutboxKind> kinds,
        TimeProvider clock,
        ILogger<DeviceOutbox>? logger = null,
        IEnumerable<IOutboxFold>? folds = null)
    {
        _store = store;
        _kinds = kinds.ToDictionary(kind => kind.Kind, StringComparer.Ordinal);
        _folds = [.. folds ?? []];
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

    /// <summary>The entry an attempt is under way for, or null between attempts.
    /// A screen reads it to say "uploading" rather than "waiting".</summary>
    public Guid? Sending { get; private set; }

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

        await _writes.WaitAsync(cancellationToken);
        try
        {
            await _store.AddAsync(entry, cancellationToken);
            lock (_entriesLock) _entries.Add(entry);
        }
        finally
        {
            _writes.Release();
        }

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

    /// <summary>
    /// Rewrites the payload of an entry still waiting — the person changed what it
    /// says before it left — keeping its place, its attempts and its id. Answers
    /// false, and changes nothing, for an entry that is not there or is being
    /// sent right now: that one has already been said, so the caller queues its
    /// change behind it instead. A set-aside entry is rewritten too, and stays
    /// set aside until <see cref="RetryAsync"/>.
    /// </summary>
    public async Task<bool> ReplacePayloadAsync(Guid id, string payloadJson, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(payloadJson);

        // Answered before waiting for the writes: the one in progress may be this
        // entry's outcome, and the answer is already known. Checked again under the
        // lock, where it counts.
        if (Sending == id) return false;

        var replaced = await UpdateEntryAsync(
            id,
            entry => Sending == id ? null : entry with { PayloadJson = payloadJson },
            cancellationToken);

        if (replaced is null) return false;

        Changed?.Invoke();
        return true;
    }

    /// <summary>
    /// Takes an entry still waiting out of the outbox, so it is never sent.
    /// Answers false, and changes nothing, for an entry that is not there or is
    /// being sent right now — the caller queues what undoes it instead.
    /// </summary>
    public async Task<bool> RemoveAsync(Guid id, CancellationToken cancellationToken = default)
    {
        // As in ReplacePayloadAsync: known before the writes are waited for.
        if (Sending == id) return false;

        await _writes.WaitAsync(cancellationToken);
        try
        {
            lock (_entriesLock)
            {
                if (Sending == id || !_entries.Exists(entry => entry.Id == id)) return false;
                _removing.Add(id);
            }

            // The file first, then the list: a delete the file refuses leaves the
            // entry where it was, and a second try finds it. Taken out of the list
            // first, the entry would be gone from the screen and back on the next
            // start. Meanwhile it is held from a flush (Claim skips it) and from
            // every other write (this holds the writes).
            try
            {
                await _store.RemoveAsync(id, cancellationToken);

                lock (_entriesLock) _entries.RemoveAll(entry => entry.Id == id);
            }
            finally
            {
                lock (_entriesLock) _removing.Remove(id);
            }
        }
        finally
        {
            _writes.Release();
        }

        Changed?.Invoke();
        return true;
    }

    /// <summary>
    /// Puts a set-aside or parked entry back in line from no attempts, and starts a
    /// flush nobody waits for — for a caller that changed the entry, such as a
    /// capture reworded after the service refused its words, and must not hold the
    /// screen for a network round trip. <see cref="RetryAsync"/> is the tap that
    /// waits.
    /// </summary>
    public async Task RequeueAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var reset = await UpdateEntryAsync(
            id,
            entry => entry with { Attempts = 0, LastError = null, Refused = false },
            cancellationToken);

        if (reset is null) return;

        Changed?.Invoke();
        _ = FlushInBackgroundAsync(immediately: false);
    }

    /// <summary>The person tapped a parked entry: it starts again from no attempts.</summary>
    public async Task RetryAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var reset = await UpdateEntryAsync(
            id,
            entry => entry with { Attempts = 0, LastError = null, Refused = false },
            cancellationToken);

        if (reset is null) return;

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
        _writes.Dispose();
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
        // A change queued behind an entry while its refusal was being written, or
        // one a restart found waiting, is folded before anything is sent.
        if (await FoldIntoSetAsideAsync(cancellationToken)) Changed?.Invoke();

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

            // Claimed as it stands now, not as the pass first read it: the person
            // may have rewritten it or taken it out while the queue waited.
            if (Claim(entry.Id) is not { } claimed) continue;

            // Claimed until its outcome is written: until then the service may
            // already have it, so it is neither rewritten nor taken out, and the
            // outcome lands on the entry as it stands — a staged kind may have
            // checkpointed while it ran — rather than on a copy.
            OutboxDelivery delivery;
            OutboxEntry? failed = null;

            try
            {
                delivery = await SendAsync(claimed, cancellationToken);

                switch (delivery.Kind)
                {
                    case OutboxDeliveryKind.Delivered:
                        await RemoveDeliveredAsync(claimed.Id, cancellationToken);
                        break;

                    case OutboxDeliveryKind.Refused:
                        // Not a network problem, and not one a timer will fix — so it
                        // is set aside rather than holding up the entries behind it.
                        await SetAsideAsync(claimed.Id, delivery.Error, cancellationToken);
                        break;

                    case OutboxDeliveryKind.Held:
                        await UpdateEntryAsync(claimed.Id, current => current with { LastError = delivery.Error }, cancellationToken);
                        break;

                    default:
                        failed = await UpdateEntryAsync(
                            claimed.Id,
                            current => current with { Attempts = current.Attempts + 1, LastError = delivery.Error },
                            cancellationToken)
                            ?? claimed with { Attempts = claimed.Attempts + 1, LastError = delivery.Error };
                        break;
                }
            }
            finally
            {
                Sending = null;
            }

            switch (delivery.Kind)
            {
                case OutboxDeliveryKind.Delivered:
                    _notBefore = null;
                    Unreachable = false;
                    Changed?.Invoke();
                    Delivered?.Invoke(claimed);
                    continue;

                case OutboxDeliveryKind.Refused:
                    Changed?.Invoke();
                    continue;

                case OutboxDeliveryKind.Held:
                    // The service is newer than this app. Not the entry's fault
                    // and not the network's: it keeps its place and its attempts,
                    // and nothing behind it goes either. Asked again when the
                    // compatibility verdict is next due, which is what lets a
                    // rolled-back service take the queue without a tap.
                    Changed?.Invoke();
                    _notBefore = now + HeldRetryDelay;
                    Schedule(HeldRetryDelay);
                    return;

                default:
                    Unreachable = true;
                    Changed?.Invoke();

                    if (failed!.IsParked)
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

    /// <summary>
    /// Sets the refused entry <paramref name="id"/> aside and folds into it what waits
    /// behind it, in one hold of <see cref="_writes"/>: a reword or a tap landing
    /// between the two would otherwise take the entry out of the fold's reach and
    /// let this pass send the change on its own.
    /// </summary>
    private async Task SetAsideAsync(Guid id, string? error, CancellationToken cancellationToken)
    {
        await _writes.WaitAsync(cancellationToken);

        try
        {
            OutboxEntry updated;

            lock (_entriesLock)
            {
                var index = _entries.FindIndex(queued => queued.Id == id);
                if (index < 0) return;

                updated = _entries[index] with { Refused = true, LastError = error };
                _entries[index] = updated;
            }

            await _store.UpdateAsync(updated, cancellationToken);
            await FoldWhileWritingAsync(id, cancellationToken);
        }
        finally
        {
            _writes.Release();
        }
    }

    /// <summary>
    /// Lets the folds take waiting entries into every set-aside one — a change queued
    /// behind an entry while it was being refused, or one a restart found waiting.
    /// Only a flush calls it, holding the gate, so nothing is being sent; answers
    /// whether anything was folded.
    /// </summary>
    private async Task<bool> FoldIntoSetAsideAsync(CancellationToken cancellationToken)
    {
        if (_folds.Count == 0) return false;

        var folded = false;

        foreach (var setAside in Entries.Where(entry => entry.Refused))
        {
            await _writes.WaitAsync(cancellationToken);

            try
            {
                folded |= await FoldWhileWritingAsync(setAside.Id, cancellationToken);
            }
            finally
            {
                _writes.Release();
            }
        }

        return folded;
    }

    /// <summary>The first fold's answer for the set-aside entry <paramref name="id"/>,
    /// applied by a caller holding <see cref="_writes"/> — read and written with no
    /// other change between, so an entry rewritten meanwhile is never folded from its
    /// old words. The file first, then the list, as <see cref="RemoveAsync"/> does: a
    /// store that fails part-way leaves what the next pass folds again.
    /// <para>
    /// A fold with no payload takes the set-aside entry out too — a capture deleted
    /// before it ever reached the service. The waiting entries go first and the
    /// set-aside one last, so a store failing part-way never leaves a withdrawal
    /// waiting for a capture already gone — sent, it would tell the person the
    /// desktop had dealt with it. A failure after the withdrawal left keeps the
    /// capture set aside with nothing naming it: on the phone, where a second
    /// Delete takes it out. The fold hears of the removal only once the entry is
    /// off the device.
    /// </para></summary>
    private async Task<bool> FoldWhileWritingAsync(Guid id, CancellationToken cancellationToken)
    {
        if (_folds.Count == 0) return false;

        OutboxEntry setAside;
        (IOutboxFold By, OutboxFold Answer)? asked;

        lock (_entriesLock)
        {
            if (_entries.Find(queued => queued.Id == id) is not { Refused: true } current) return false;

            setAside = current;
            asked = AskFolds(setAside, [.. _entries]);
        }

        if (asked is not { } answered) return false;

        var (by, fold) = answered;

        if (fold.PayloadJson is { } payloadJson)
        {
            var updated = setAside with { PayloadJson = payloadJson };
            await _store.UpdateAsync(updated, cancellationToken);
            lock (_entriesLock) _entries[_entries.FindIndex(queued => queued.Id == id)] = updated;
        }

        foreach (var absorbed in fold.Absorbed.Where(absorbed => absorbed != id).Distinct())
        {
            await _store.RemoveAsync(absorbed, cancellationToken);
            lock (_entriesLock) _entries.RemoveAll(queued => queued.Id == absorbed);
        }

        if (fold.PayloadJson is null)
        {
            await _store.RemoveAsync(id, cancellationToken);
            lock (_entriesLock) _entries.RemoveAll(queued => queued.Id == id);

            try
            {
                by.Removed(setAside);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Outbox fold {Fold} could not let go of entry {Id}; it has left the outbox.", by.GetType().Name, id);
            }
        }

        return true;
    }

    /// <summary>The first fold that answers, and its answer. One that throws is logged
    /// and passed over: an entry it cannot read must not stop every flush after it.</summary>
    private (IOutboxFold By, OutboxFold Answer)? AskFolds(OutboxEntry setAside, IReadOnlyList<OutboxEntry> waiting)
    {
        foreach (var candidate in _folds)
        {
            try
            {
                if (candidate.Fold(setAside, waiting) is { } fold) return (candidate, fold);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Outbox fold {Fold} could not read entry {Id}; it is left as it is.", candidate.GetType().Name, setAside.Id);
            }
        }

        return null;
    }

    /// <summary>Gives every entry the network parked its attempts back.</summary>
    private async Task UnparkAsync(CancellationToken cancellationToken)
    {
        foreach (var entry in Entries.Where(entry => entry.IsParked && !entry.Refused))
        {
            await UpdateEntryAsync(entry.Id, current => current with { Attempts = 0 }, cancellationToken);
        }
    }

    private async Task<OutboxDelivery> SendAsync(OutboxEntry entry, CancellationToken cancellationToken)
    {
        if (!_kinds.TryGetValue(entry.Kind, out var kind))
        {
            return OutboxDelivery.Refused($"Nothing on this phone sends a '{entry.Kind}' entry.");
        }

        Changed?.Invoke();

        try
        {
            return kind is IStagedOutboxKind staged
                ? await staged.SendAsync(entry, (payload, ct) => CheckpointAsync(entry.Id, payload, ct), cancellationToken)
                : await kind.SendAsync(entry, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // A kind reports what it can classify; anything that still escapes is
            // treated as the network's, which is the case a retry can cure.
            return OutboxDelivery.Transient(ex.Message);
        }
    }

    /// <summary>A staged kind's request landed: keep what it has sent, and give
    /// the entry its attempts back for the next one.</summary>
    private async Task CheckpointAsync(Guid id, string payloadJson, CancellationToken cancellationToken)
    {
        if (await UpdateEntryAsync(id, current => current with { PayloadJson = payloadJson, Attempts = 0, LastError = null }, cancellationToken) is null)
        {
            return;
        }

        Changed?.Invoke();
    }

    /// <summary>The entry as it stands, marked as the one being sent — in the
    /// same lock <see cref="ReplacePayloadAsync"/> and <see cref="RemoveAsync"/>
    /// check, so neither can change an entry between this read and its send.
    /// Null when it was taken out, or is being taken out.</summary>
    private OutboxEntry? Claim(Guid id)
    {
        lock (_entriesLock)
        {
            var current = _entries.Find(queued => queued.Id == id);
            if (current is null || _removing.Contains(id)) return null;

            Sending = id;
            return current;
        }
    }

    /// <summary>
    /// Applies <paramref name="change"/> to the entry as it stands — never to a copy
    /// read earlier — in the list and then on the device, under
    /// <see cref="_writes"/> so the file takes changes in the order the list did.
    /// Answers the entry as written, or null when it is not there or
    /// <paramref name="change"/> declined (answered null).
    /// </summary>
    private async Task<OutboxEntry?> UpdateEntryAsync(
        Guid id,
        Func<OutboxEntry, OutboxEntry?> change,
        CancellationToken cancellationToken)
    {
        await _writes.WaitAsync(cancellationToken);

        try
        {
            OutboxEntry? updated;

            lock (_entriesLock)
            {
                var index = _entries.FindIndex(queued => queued.Id == id);
                if (index < 0) return null;

                updated = change(_entries[index]);
                if (updated is null) return null;

                _entries[index] = updated;
            }

            await _store.UpdateAsync(updated, cancellationToken);
            return updated;
        }
        finally
        {
            _writes.Release();
        }
    }

    /// <summary>The service has the entry: out of the list and off the device.</summary>
    private async Task RemoveDeliveredAsync(Guid id, CancellationToken cancellationToken)
    {
        await _writes.WaitAsync(cancellationToken);

        try
        {
            await _store.RemoveAsync(id, cancellationToken);
            lock (_entriesLock) _entries.RemoveAll(queued => queued.Id == id);
        }
        finally
        {
            _writes.Release();
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
