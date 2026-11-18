using Backlog.Modules.Sync.Abstractions.DataTransferObjects;

namespace Backlog.Mobile.UI.Events;

/// <summary>
/// The phone's <c>event_view</c>: the knowledge events and event series the feed brought, as the
/// feed carried them, and the cursor the next pull resumes from. The phone writes nothing else
/// about an event here; an attachment it asks for waits in the device outbox.
/// </summary>
public interface IEventViewStore
{
    /// <summary>Every event and series kept, one record per id. Synchronous, for the reason the
    /// task view's read is: a first render cannot await.</summary>
    IReadOnlyList<KnowledgeEventChangeRecord> ReadRows();

    /// <summary>Where the next pull starts, or null for the beginning.</summary>
    string? ReadCursor();

    /// <summary>Writes <paramref name="rows"/> over whatever is kept for their ids and, when
    /// <paramref name="cursor"/> is given, moves the cursor, in one transaction.</summary>
    Task SaveAsync(IReadOnlyList<KnowledgeEventChangeRecord> rows, string? cursor = null, CancellationToken cancellationToken = default);

    /// <summary>Forgets the cursor, so the next pull starts from the beginning.</summary>
    Task ResetCursorAsync(CancellationToken cancellationToken = default);
}
