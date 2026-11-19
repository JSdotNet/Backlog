using System.Text.Json;

using Backlog.Mobile.UI.Storage;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;

namespace Backlog.Mobile.UI.Notes;

/// <summary>
/// <see cref="INoteViewStore"/> in the device's SQLite file, beside the outbox and
/// <c>task_view</c>: its own tables, the same file. The document is kept whole as
/// JSON, so a field this build does not know is carried rather than dropped.
/// Unpooled, like the device store: a connection per call.
/// </summary>
public sealed class SqliteNoteViewStore : INoteViewStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static readonly SqliteViewMapping<NoteViewRow> Mapping = new(
        row => row.Id,
        [
            new("updated_at", "TEXT    NOT NULL", row => SqliteInstant.Format(row.UpdatedAt)),
            new("deleted_at", "TEXT    NULL", row => SqliteInstant.FormatOrNull(row.DeletedAt)),
            new("server_ts", "INTEGER NOT NULL", row => row.ServerTimestamp),
            new("payload", "TEXT    NOT NULL", row => JsonSerializer.Serialize(row.Note, Json)),
        ],
        reader => JsonSerializer.Deserialize<TaskPayload>(reader.GetString(4), Json) is { } payload
            ? new NoteViewRow(
                Guid.Parse(reader.GetString(0)),
                SqliteInstant.Parse(reader.GetString(1)),
                SqliteInstant.ParseOrNull(reader, 2),
                reader.GetInt64(3),
                payload)
            : null);

    private readonly SqliteViewStore<NoteViewRow> _view;

    public SqliteNoteViewStore(string databasePath) =>
        _view = new SqliteViewStore<NoteViewRow>(databasePath, "note", Mapping);

    public IReadOnlyList<NoteViewRow> ReadRows() => _view.ReadRows();

    public string? ReadCursor() => _view.ReadCursor();

    public Task SaveAsync(IReadOnlyList<NoteViewRow> rows, string? cursor = null, CancellationToken cancellationToken = default) =>
        _view.SaveAsync(rows, cursor, alsoWrite: null, cancellationToken);

    public Task ResetCursorAsync(CancellationToken cancellationToken = default) =>
        _view.ResetCursorAsync(cancellationToken);
}
