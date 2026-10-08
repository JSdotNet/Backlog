namespace Backlog.Mobile.UI.Notes;

/// <summary>
/// The phone's <c>note_view</c>: the fold of the task feed's note documents, and
/// the cursor the next note pull resumes from. A projection, as
/// <see cref="Tasks.ITaskViewStore"/> is, with its own cursor so the notes and
/// the tasks never have to move together.
/// </summary>
public interface INoteViewStore
{
    /// <summary>Every row, tombstones included. Synchronous, for the reason the
    /// task view's read is: a first render cannot await.</summary>
    IReadOnlyList<NoteViewRow> ReadRows();

    /// <summary>Where the next pull starts, or null for the beginning.</summary>
    string? ReadCursor();

    /// <summary>Writes <paramref name="rows"/> over whatever is kept for their ids
    /// and, when <paramref name="cursor"/> is given, moves the cursor, in one
    /// transaction, so a page is never half kept.</summary>
    Task SaveAsync(IReadOnlyList<NoteViewRow> rows, string? cursor = null, CancellationToken cancellationToken = default);

    /// <summary>Forgets the cursor, so the next pull starts from the beginning.
    /// The rows stay: a full pull folds over them to the same result.</summary>
    Task ResetCursorAsync(CancellationToken cancellationToken = default);
}
