using System.Text.Json;

using Backlog.Mobile.UI.Storage;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;

namespace Backlog.Mobile.UI.Events;

/// <summary>
/// <see cref="IEventViewStore"/> in the device's SQLite file, beside the outbox and the other
/// views: its own tables, the same file. Each record is kept whole as JSON, so a field this
/// build does not know is carried rather than dropped. Unpooled, like the device store: a
/// connection per call.
/// </summary>
public sealed class SqliteEventViewStore : IEventViewStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static readonly SqliteViewMapping<KnowledgeEventChangeRecord> Mapping = new(
        row => row.Change.Id,
        [new("record", "TEXT NOT NULL", row => JsonSerializer.Serialize(row, Json))],
        reader => Deserialize(reader.GetString(1)));

    private readonly SqliteViewStore<KnowledgeEventChangeRecord> _view;

    public SqliteEventViewStore(string databasePath) =>
        _view = new SqliteViewStore<KnowledgeEventChangeRecord>(databasePath, "event", Mapping);

    public IReadOnlyList<KnowledgeEventChangeRecord> ReadRows() => _view.ReadRows();

    public string? ReadCursor() => _view.ReadCursor();

    public Task SaveAsync(IReadOnlyList<KnowledgeEventChangeRecord> rows, string? cursor = null, CancellationToken cancellationToken = default) =>
        _view.SaveAsync(rows, cursor, alsoWrite: null, cancellationToken);

    public Task ResetCursorAsync(CancellationToken cancellationToken = default) =>
        _view.ResetCursorAsync(cancellationToken);

    /// <summary>A row this build cannot read is passed over rather than failing the list.</summary>
    private static KnowledgeEventChangeRecord? Deserialize(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<KnowledgeEventChangeRecord>(json, Json);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
