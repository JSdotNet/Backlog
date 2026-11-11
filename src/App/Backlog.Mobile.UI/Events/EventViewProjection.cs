using Backlog.Infrastructure.Sync;
using Backlog.Infrastructure.Sync.KnowledgeEvents;
using Backlog.Mobile.UI.Services;
using Backlog.Modules.Events.Abstractions;
using Backlog.Modules.Events.Abstractions.DataTransferObjects;
using Backlog.Modules.Sync.Abstractions;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;

namespace Backlog.Mobile.UI.Events;

/// <summary>
/// The phone's knowledge events (local ADR 0050 §2,
/// <c>.devbook/domain/events/requirements.md#events-on-the-phone</c>): the owner's knowledge event
/// feed folded into <see cref="IEventViewStore"/>, one record per id by the later stamp, and read
/// back as <see cref="PhoneEvent"/>s. A reader only: the phone never writes an event, and the one
/// thing it asks about one — attaching a Talk note — goes through
/// <see cref="EventNoteAttachments"/>.
/// <para>
/// The phone keeps no feature switch. A desktop pushes events only while its <c>events</c> area
/// is on, so a phone paired to desktops without it holds none, and <see cref="HoldsAny"/> is
/// what shows the Events tab.
/// </para>
/// <para>
/// One per device, a singleton, for the reason the notification projection is one.
/// </para>
/// </summary>
public sealed class EventViewProjection : IDisposable
{
    private readonly IEventViewStore _store;
    private readonly CloudSyncClient _sync;
    private readonly IDeviceCredentialStore _credentials;
    private readonly TimeProvider _clock;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Lock _rowsLock = new();
    private readonly Dictionary<Guid, KnowledgeEventChangeRecord> _rows;

    public EventViewProjection(
        IEventViewStore store,
        CloudSyncClient sync,
        IDeviceCredentialStore credentials,
        TimeProvider clock)
    {
        _store = store;
        _sync = sync;
        _credentials = credentials;
        _clock = clock;

        // Read once, here: the pages are drawn from this before any pull answers.
        _rows = store.ReadRows().ToDictionary(row => row.Change.Id);
    }

    /// <summary>Raised when a pull changed an event or a series. May be raised off the render
    /// thread.</summary>
    public event Action? Changed;

    /// <summary>Whether the phone holds any event to show — what puts the Events tab in the bar.
    /// A series alone is not one.</summary>
    public bool HoldsAny => Events().Count > 0;

    /// <summary>
    /// The upcoming events: every event not Skipped whose last day — its end, else its start —
    /// is today or later on this phone's calendar, the soonest first; then the events with no
    /// dates yet that are not Skipped, by title. Past events are left off.
    /// </summary>
    public IReadOnlyList<PhoneEvent> Upcoming()
    {
        var today = DateOnly.FromDateTime(_clock.GetLocalNow().DateTime);
        var all = Events().Where(item => item.State != KnowledgeEventState.Skipped).ToList();

        var dated = all
            .Where(item => item.LastDay is { } last && last >= today)
            .OrderBy(item => item.FirstDay ?? item.LastDay)
            .ThenBy(item => item.Event.Start?.Time)
            .ThenBy(item => item.Title, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(item => item.Id);

        var undated = all
            .Where(item => item.LastDay is null)
            .OrderBy(item => item.Title, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(item => item.Id);

        return [.. dated, .. undated];
    }

    /// <summary>Every event the phone holds, Skipped and past ones included, in no set order —
    /// what a Talk note may still be attached to after the event is over.</summary>
    public IReadOnlyList<PhoneEvent> Held() => Events();

    /// <summary>The event with the name of its series, or null when the phone holds none by that
    /// id — Skipped and past events included, so a link to one still opens.</summary>
    public PhoneEvent? Find(Guid id) => Events().FirstOrDefault(item => item.Id == id);

    /// <summary>The events that link the Inbox note <paramref name="noteId"/> as a Talk note, as
    /// the last pull brought them.</summary>
    public IReadOnlyList<PhoneEvent> LinkingNote(Guid noteId)
    {
        var reference = noteId.ToString("D");

        return
        [
            .. Events().Where(item => item.Event.Notes.Any(note =>
                note.Kind == NoteLinkKind.Talk && string.Equals(note.Reference, reference, StringComparison.OrdinalIgnoreCase)))
        ];
    }

    /// <summary>
    /// Pulls the knowledge event feed from its cursor until the service says it is drained,
    /// keeping each page and the cursor after it as one write. A service from before the feed
    /// answers a bare 404, which ends the pull with nothing new. A cursor the service will not
    /// take is dropped and the pull starts again from the beginning, once; any other failure is
    /// thrown for the screen to word.
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
                PullKnowledgeEventsResponse? page;

                try
                {
                    page = await _sync.PullKnowledgeEventsAsync(since, cancellationToken);
                }
                catch (SyncUnavailableException ex) when (!restarted
                    && ex.Code is SyncErrorCodes.SyncCursorMalformed or SyncErrorCodes.SyncCursorExpired)
                {
                    await _store.ResetCursorAsync(cancellationToken);
                    since = null;
                    restarted = true;
                    continue;
                }

                if (page is null) return;

                List<KnowledgeEventChangeRecord> changed;
                lock (_rowsLock)
                {
                    changed = Fold(page.Events ?? []);
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

    public void Dispose() => _gate.Dispose();

    /// <summary>The newer of two copies of one event or series: the later stamp a desktop wrote
    /// it at, and the store's stamp settles a tie.</summary>
    internal static bool Wins(KnowledgeEventChangeRecord incoming, KnowledgeEventChangeRecord held) =>
        incoming.Change.UpdatedAt > held.Change.UpdatedAt
        || (incoming.Change.UpdatedAt == held.Change.UpdatedAt && incoming.ServerTimestamp > held.ServerTimestamp);

    /// <summary>The records of a page that replace what is held, one per id — a page may carry
    /// two copies of one event. Called under the rows lock.</summary>
    private List<KnowledgeEventChangeRecord> Fold(IEnumerable<KnowledgeEventChangeRecord> page)
    {
        var winners = new Dictionary<Guid, KnowledgeEventChangeRecord>();

        foreach (var record in page)
        {
            var id = record.Change.Id;
            var standing = winners.TryGetValue(id, out var won) ? won : _rows.GetValueOrDefault(id);
            if (standing is null || Wins(record, standing)) winners[id] = record;
        }

        return [.. winners.Values];
    }

    /// <summary>Every event the phone can read, each with its series' name. A record this build
    /// cannot read is passed over.</summary>
    private List<PhoneEvent> Events()
    {
        List<KnowledgeEventChangeRecord> rows;
        lock (_rowsLock) rows = [.. _rows.Values];

        var series = rows
            .Select(row => KnowledgeEventDocuments.ReadSeries(row.Change))
            .OfType<EventSeriesReplicaDto>()
            .ToDictionary(item => item.Id, item => item.Name);

        return rows
            .Select(row => KnowledgeEventDocuments.ReadEvent(row.Change))
            .OfType<KnowledgeEventReplicaDto>()
            .Select(item => new PhoneEvent(item, item.SeriesId is { } id ? series.GetValueOrDefault(id) : null))
            .ToList();
    }
}
