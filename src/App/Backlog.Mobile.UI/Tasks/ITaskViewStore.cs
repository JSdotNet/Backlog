namespace Backlog.Mobile.UI.Tasks;

/// <summary>
/// The phone's <c>task_view</c>: the fold of the task feed, and the cursor the
/// next pull resumes from. A projection, not a repository — it keeps what the
/// replica said and one kind of row of the phone's own, a task added here and
/// not yet pulled back.
/// </summary>
public interface ITaskViewStore
{
    /// <summary>Every row, tombstones included. Synchronous for the reason the
    /// cached inbox is: the list is drawn before the service is asked anything,
    /// and a first render cannot await.</summary>
    IReadOnlyList<TaskViewRow> ReadRows();

    /// <summary>Where the next pull starts, or null for the beginning.</summary>
    string? ReadCursor();

    /// <summary>Writes <paramref name="rows"/> over whatever is kept for their ids
    /// and, when <paramref name="cursor"/> is given, moves the cursor — in one
    /// transaction, so a page is never half kept.</summary>
    Task SaveAsync(IReadOnlyList<TaskViewRow> rows, string? cursor = null, CancellationToken cancellationToken = default);

    /// <summary>Forgets the cursor, so the next pull starts from the beginning.
    /// The rows stay: a full pull folds over them to the same result.</summary>
    Task ResetCursorAsync(CancellationToken cancellationToken = default);
}
