using Backlog.Mobile.UI.Storage;

namespace Backlog.Mobile.UI.Goals;

/// <summary>
/// The phone's <c>goals_view</c>: the fold of the task feed's goal, impediment and
/// stand-up context documents, and the cursor the next Goals pull resumes from. A
/// projection, as <see cref="Tasks.ITaskViewStore"/> is, with its own cursor so Goals and
/// the tasks never have to move together.
/// </summary>
public interface IGoalsViewStore
{
    /// <summary>Every row, tombstones included. Synchronous, for the reason the task view's
    /// read is: a first render cannot await.</summary>
    IReadOnlyList<GoalsViewRow> ReadRows();

    /// <summary>Where the next pull starts, or null for the beginning.</summary>
    string? ReadCursor();

    /// <summary>Writes each of <paramref name="rows"/> over what is kept for its id when it is
    /// the later copy by <see cref="GoalsFold.Wins"/>, and, when <paramref name="cursor"/> is
    /// given, moves the cursor, in one transaction, so a page is never half kept. An older
    /// copy is passed over, so two writers racing — a pull and a queued edit — leave the later
    /// one on disk whichever writes last.</summary>
    Task SaveAsync(IReadOnlyList<GoalsViewRow> rows, string? cursor = null, CancellationToken cancellationToken = default);

    /// <summary>Forgets the cursor, so the next pull starts from the beginning. The rows
    /// stay: a full pull folds over them to the same result.</summary>
    Task ResetCursorAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// <see cref="IGoalsViewStore"/> in the device's SQLite file, beside the task and note
/// views: its own tables, the same file. Each document is kept verbatim, so a field this
/// build does not know is carried rather than dropped. Unpooled, like the device store: a
/// connection per call.
/// </summary>
public sealed class SqliteGoalsViewStore : IGoalsViewStore
{
    private static readonly SqliteViewMapping<GoalsViewRow> Mapping = new(
        row => row.Id,
        [
            new("kind", "TEXT    NOT NULL", row => row.Kind),
            new("updated_at", "TEXT    NOT NULL", row => SqliteInstant.Format(row.UpdatedAt)),
            new("deleted_at", "TEXT    NULL", row => SqliteInstant.FormatOrNull(row.DeletedAt)),
            new("server_ts", "INTEGER NOT NULL", row => row.ServerTimestamp),
            new("content", "TEXT    NOT NULL", row => row.Content),
        ],
        reader => new GoalsViewRow(
            Guid.Parse(reader.GetString(0)),
            reader.GetString(1),
            SqliteInstant.Parse(reader.GetString(2)),
            SqliteInstant.ParseOrNull(reader, 3),
            reader.GetInt64(4),
            reader.GetString(5)),
        // The later copy by GoalsFold.Wins, so a pull and a queued edit racing leave the
        // later one on disk whichever writes last.
        KeepLater: view => $"""
            excluded.updated_at > {view}.updated_at
                OR (excluded.updated_at = {view}.updated_at
                    AND (excluded.server_ts > {view}.server_ts
                        OR (excluded.server_ts = {view}.server_ts
                            AND excluded.deleted_at IS NOT NULL AND {view}.deleted_at IS NULL)))
            """);

    private readonly SqliteViewStore<GoalsViewRow> _view;

    public SqliteGoalsViewStore(string databasePath) =>
        // Kept as progress_view and progress_cursor until the context was renamed Goals
        // (local ADR 0073 §10); a phone that still has them carries its rows and cursor over.
        _view = new SqliteViewStore<GoalsViewRow>(databasePath, "goals", Mapping, renamedFrom: "progress");

    public IReadOnlyList<GoalsViewRow> ReadRows() => _view.ReadRows();

    public string? ReadCursor() => _view.ReadCursor();

    public Task SaveAsync(IReadOnlyList<GoalsViewRow> rows, string? cursor = null, CancellationToken cancellationToken = default) =>
        _view.SaveAsync(rows, cursor, alsoWrite: null, cancellationToken);

    public Task ResetCursorAsync(CancellationToken cancellationToken = default) =>
        _view.ResetCursorAsync(cancellationToken);
}
