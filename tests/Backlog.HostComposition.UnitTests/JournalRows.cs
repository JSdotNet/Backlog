using Microsoft.Data.Sqlite;

namespace Backlog.HostComposition.UnitTests;

/// <summary>One row of the change journal's <c>revisions</c>, with the label and actor
/// of the change set it belongs to (local ADR 0061 §2).</summary>
internal sealed record JournalRow(
    long Sequence,
    string ChangeSetId,
    string? Label,
    string ActorKind,
    string DocumentKind,
    string DocumentId,
    string? Before);

/// <summary>Reads the journal straight from a harness's <c>backlog.db</c>, the way a
/// restore would find it.</summary>
internal static class JournalRows
{
    public static async Task<IReadOnlyList<JournalRow>> ReadAsync(string databasePath, CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False");
        await connection.OpenAsync(cancellationToken);

        await using var exists = connection.CreateCommand();
        exists.CommandText = "SELECT count(*) FROM sqlite_master WHERE type = 'table' AND name = 'revisions';";
        if (Convert.ToInt64(await exists.ExecuteScalarAsync(cancellationToken)) == 0) return [];

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT r.sequence, r.change_set_id, c.label, c.actor_kind, r.document_kind, r.document_id, r.before
            FROM revisions r JOIN change_sets c ON c.id = r.change_set_id
            ORDER BY r.sequence;
            """;

        var rows = new List<JournalRow>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new JournalRow(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetString(6)));
        }

        return rows;
    }
}
