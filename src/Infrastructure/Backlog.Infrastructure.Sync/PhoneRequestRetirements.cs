using Backlog.Infrastructure.Sync.KnowledgeEvents;
using Backlog.Modules.Progress.Abstractions.DataTransferObjects;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;

namespace Backlog.Infrastructure.Sync;

/// <summary>
/// The phone's requests on the task feed — impediment edits (local ADR 0035 §8) and
/// attachment requests (local ADR 0050 §3) — that a pull could not answer yet, and the
/// tombstones of those it answered that the replica has not yet been told about. The merge
/// defers a request and adds each answered one's tombstone; the push that follows sends them.
/// <para>
/// <b>An attachment request is held durably</b>, in <see cref="IHeldAttachmentRequestStore"/>
/// when the host composed one: the task cursor has moved past it, and a desktop closed while
/// the <c>events</c> area was off, or before the event arrived, must still apply it once it
/// can (<c>.devbook/domain/events/requirements.md</c>, "Held while the area is off"). It is
/// released once Events has answered it.
/// </para>
/// <para>
/// <b>Everything else lives for the process.</b> An impediment edit whose tombstone is lost to
/// a closed app changes nothing when a desktop meets it again: its field already says what it
/// asked for, or no longer says what the phone saw (<see cref="ImpedimentEditDto"/>). A lost
/// attachment tombstone is the same: the event already links the note.
/// </para>
/// </summary>
public sealed class PhoneRequestRetirements
{
    private readonly Lock _gate = new();
    private readonly Dictionary<Guid, TaskChange> _waiting = [];
    private readonly Dictionary<Guid, TaskChange> _deferred = [];
    private readonly IHeldAttachmentRequestStore? _held;

    /// <param name="held">Where attachment requests are kept between runs, or null on a head
    /// that composed none — then they are held for the process, as impediment edits are.</param>
    public PhoneRequestRetirements(IHeldAttachmentRequestStore? held = null)
    {
        _held = held;
    }

    /// <summary>Keeps a request this desktop cannot answer yet, so it is offered again after a
    /// later pull rather than passed by with the cursor.</summary>
    public void Defer(TaskChange request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (_held is not null && EventAttachmentReplicaDocuments.Is(request))
        {
            _held.Hold(request);
            return;
        }

        lock (_gate) _deferred[request.Id] = request;
    }

    /// <summary>Every deferred request, handed over once: one still unanswerable is deferred
    /// again. A held attachment request stays held until <see cref="Add"/> retires it, so a
    /// process that ends mid-retry loses nothing.</summary>
    public IReadOnlyList<TaskChange> TakeDeferred()
    {
        List<TaskChange> taken;
        lock (_gate)
        {
            taken = [.. _deferred.Values];
            _deferred.Clear();
        }

        if (_held is not null) taken.AddRange(_held.ReadAll().Where(held => taken.TrueForAll(each => each.Id != held.Id)));

        return taken;
    }

    /// <summary>Queues <paramref name="tombstone"/>, replacing one already queued for its id,
    /// and lets go of the request it answers.</summary>
    public void Add(TaskChange tombstone)
    {
        ArgumentNullException.ThrowIfNull(tombstone);
        lock (_gate) _waiting[tombstone.Id] = tombstone;

        Release(tombstone.Id);
    }

    /// <summary>Lets go of a held request another desktop retired, or this one answered.</summary>
    public void Release(Guid requestId) => _held?.Release(requestId);

    /// <summary>Every tombstone waiting to be sent.</summary>
    public IReadOnlyList<TaskChange> Waiting()
    {
        lock (_gate) return [.. _waiting.Values];
    }

    /// <summary>Forgets the tombstones the replica has taken.</summary>
    public void Sent(IEnumerable<TaskChange> sent)
    {
        ArgumentNullException.ThrowIfNull(sent);

        lock (_gate)
        {
            foreach (var tombstone in sent)
            {
                if (_waiting.TryGetValue(tombstone.Id, out var held) && held.UpdatedAt == tombstone.UpdatedAt) _waiting.Remove(tombstone.Id);
            }
        }
    }
}
