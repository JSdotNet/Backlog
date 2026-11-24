using System.Globalization;
using System.Text;
using System.Text.Json;
using Backlog.Modules.Tasks.Services;
using Backlog.SharedKernel.Changes;
using Microsoft.Data.Sqlite;

namespace Backlog.Infrastructure.Sqlite.Changes;

/// <summary>
/// The change journal's read side (local ADR 0061 §2) over one database: the
/// <c>change_sets</c> newest first, and per change set the documents its
/// <c>revisions</c> name, each read as the text it was before and the text it is now.
/// <para>
/// A task reads as its entry text, the same text the pane and <c>read_item</c> show,
/// so the two sides compare as a person reads an entry. Any other kind is shown as
/// its stored fields, one per line, until it has a text of its own.
/// </para>
/// </summary>
public sealed class SqliteChangeJournal(string databasePath) : IChangeJournal
{
    /// <summary>What <c>revisions.document_kind</c> calls a task.</summary>
    private const string TaskKind = "task";

    private readonly string _databasePath = databasePath ?? throw new ArgumentNullException(nameof(databasePath));

    public async Task<IReadOnlyList<ChangeSetEntry>> ListAsync(
        string? sessionId = null,
        int limit = 200,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await SqliteTaskRepository.OpenForJournalAsync(_databasePath, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT c.id, c.actor_kind, c.caller, c.session_id, c.correlation_id, c.label, c.started_at,
                   (SELECT COUNT(*) FROM revisions r WHERE r.change_set_id = c.id)
            FROM change_sets c
            {(string.IsNullOrWhiteSpace(sessionId) ? string.Empty : "WHERE c.session_id = $session_id COLLATE NOCASE")}
            ORDER BY c.started_at DESC, c.rowid DESC
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$limit", Math.Max(1, limit));
        if (!string.IsNullOrWhiteSpace(sessionId)) command.Parameters.AddWithValue("$session_id", sessionId.Trim());

        var entries = new List<ChangeSetEntry>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            // A row a hand or an older build wrote with an id or a time this cannot
            // read is left out rather than failing the list.
            if (!Guid.TryParse(reader.GetString(0), out var id)
                || !DateTimeOffset.TryParse(reader.GetString(6), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var startedAt))
            {
                continue;
            }

            var changeSet = new ChangeSet(id, ChangeSet.FromWire(reader.GetString(1)), Text(reader, 5), startedAt)
            {
                Caller = Text(reader, 2),
                SessionId = Text(reader, 3),
                CorrelationId = Text(reader, 4),
            };
            entries.Add(new ChangeSetEntry(changeSet, reader.GetInt32(7)));
        }

        return entries;
    }

    public async Task<IReadOnlyList<ChangedDocument>> ReadAsync(Guid changeSetId, CancellationToken cancellationToken = default)
    {
        await using var connection = await SqliteTaskRepository.OpenForJournalAsync(_databasePath, cancellationToken).ConfigureAwait(false);

        var revisions = new List<(string Kind, string Id, string? Before)>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT document_kind, document_id, before FROM revisions
                WHERE change_set_id = $change_set_id
                ORDER BY sequence;
                """;
            command.Parameters.AddWithValue("$change_set_id", changeSetId.ToString());

            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                revisions.Add((reader.GetString(0), reader.GetString(1), Text(reader, 2)));
            }
        }

        var documents = new List<ChangedDocument>(revisions.Count);
        foreach (var (kind, id, before) in revisions)
        {
            documents.Add(kind == TaskKind
                ? await TaskDocumentAsync(connection, id, before, cancellationToken).ConfigureAwait(false)
                : new ChangedDocument(kind, id, TitleOf(before) ?? id, before is null ? null : Fields(before), null));
        }

        return documents;
    }

    private static async Task<ChangedDocument> TaskDocumentAsync(
        SqliteConnection connection,
        string id,
        string? before,
        CancellationToken cancellationToken)
    {
        var current = await SqliteTaskRepository.ReadCurrentAsync(connection, id, cancellationToken).ConfigureAwait(false);
        var previous = before is null
            ? null
            : await SqliteTaskRepository.ReadBeforeImageAsync(connection, before, cancellationToken).ConfigureAwait(false);

        var beforeText = before is null ? null
            : previous is not null ? TaskEntryText.Of(previous)
            : Fields(before);

        return new ChangedDocument(
            TaskKind,
            id,
            current?.Title ?? previous?.Title ?? TitleOf(before) ?? id,
            beforeText,
            current is null ? null : TaskEntryText.Of(current));
    }

    /// <summary>A before-image's fields, one <c>name: value</c> per line in the order
    /// the image holds them; a null is an empty value. The image itself when it is
    /// not a JSON object.</summary>
    internal static string Fields(string image)
    {
        try
        {
            using var document = JsonDocument.Parse(image);
            if (document.RootElement.ValueKind is not JsonValueKind.Object) return image;

            var lines = new StringBuilder();
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (lines.Length > 0) lines.Append('\n');
                lines.Append(property.Name).Append(": ").Append(property.Value.ValueKind switch
                {
                    JsonValueKind.Null => string.Empty,
                    JsonValueKind.String => property.Value.GetString(),
                    _ => property.Value.GetRawText(),
                });
            }

            return lines.ToString();
        }
        catch (JsonException)
        {
            return image;
        }
    }

    /// <summary>The <c>title</c> a before-image holds, or null.</summary>
    private static string? TitleOf(string? image)
    {
        if (image is null) return null;

        try
        {
            using var document = JsonDocument.Parse(image);
            return document.RootElement.ValueKind is JsonValueKind.Object
                && document.RootElement.TryGetProperty("title", out var title)
                && title.ValueKind is JsonValueKind.String
                    ? title.GetString()
                    : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? Text(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
}
