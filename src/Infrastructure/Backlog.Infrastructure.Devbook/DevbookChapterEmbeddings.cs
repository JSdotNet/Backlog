using System.Buffers.Binary;
using System.Globalization;

using Microsoft.Data.Sqlite;

namespace Backlog.Infrastructure.Devbook;

/// <summary>
/// Turns chapter text into vectors for <see cref="DevbookChapterEmbeddings"/>.
///
/// <para>A port of the database's writing side, as
/// <c>IDevbookQueryEmbedder</c> is of the search panel's: this assembly must not
/// learn about Foundry, so the composition answers it.</para>
/// </summary>
public interface IDevbookChapterEmbedder
{
    /// <summary>The model the vectors are recorded under — what
    /// <c>chapter_embedding.model</c> and <c>meta.embeddingModel</c> say, and what
    /// <see cref="DevbookSemanticSearch"/> filters on.</summary>
    string Model { get; }

    /// <summary>Whether asking can be answered at all: a deployment is configured
    /// and has not refused. False costs nothing — no call is made.</summary>
    bool IsAvailable { get; }

    /// <summary>One vector per text, in order, or <see langword="null"/> when this
    /// batch could not be embedded.</summary>
    Task<IReadOnlyList<float[]>?> EmbedAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken = default);
}

/// <summary>
/// Writes the vectors a devbook database is missing into
/// <c>chapter_embedding</c> — the semantic tier of local ADR 0004, in the app's
/// own storage (local ADR 0015) and never in the folder it was built from
/// (local ADR 0026 §4).
///
/// <para><b>Only what is missing.</b> A row is keyed by <c>content_hash</c>, so a
/// chapter whose text has an embedding under <see cref="IDevbookChapterEmbedder.Model"/>
/// is never asked about again, and <see cref="DevbookDatabaseBuilder"/> carries
/// those rows across a rebuild. An edit costs the chapters it changed; a model
/// change re-embeds everything, which is what being pinned to a model means.</para>
///
/// <para><b>Stops at the first refusal.</b> A batch that could not be embedded
/// ends the pass with what was written so far kept: the next check carries on from
/// there, and a deployment that is not there is asked once rather than once per
/// batch.</para>
/// </summary>
public static class DevbookChapterEmbeddings
{
    /// <summary>Chapters per request: few enough that one refusal wastes little,
    /// many enough that a whole corpus is a few dozen calls.</summary>
    public const int BatchSize = 16;

    /// <summary>
    /// The most text one chapter sends. <c>text-embedding-3-small</c> takes 8191
    /// tokens; this is well under that for prose in any script, and the head of a
    /// long chapter is what it is about.
    /// </summary>
    public const int MaxCharacters = 8000;

    /// <summary>
    /// Embeds every chapter text in <paramref name="databasePath"/> with no vector
    /// under <paramref name="embedder"/>'s model, and records the model in
    /// <c>meta.embeddingModel</c>. Returns how many vectors were written — zero,
    /// without a call, when the embedder is unavailable or nothing is missing.
    /// </summary>
    public static async Task<int> EmbedMissingAsync(string databasePath, IDevbookChapterEmbedder embedder, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        ArgumentNullException.ThrowIfNull(embedder);

        if (!embedder.IsAvailable || string.IsNullOrWhiteSpace(embedder.Model) || !File.Exists(databasePath)) return 0;

        List<Pending> missing;
        using (var connection = Open(databasePath))
        {
            if (!IsReadable(connection)) return 0;
            missing = Missing(connection, embedder.Model);
        }

        var written = 0;

        foreach (var batch in missing.Chunk(BatchSize))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!embedder.IsAvailable) break;

            var vectors = await embedder.EmbedAsync([.. batch.Select(chapter => chapter.Text)], cancellationToken).ConfigureAwait(false);
            if (vectors is null || vectors.Count != batch.Length) break;

            // A connection per batch, never held across a call to the model: a
            // rebuild moving a new file over this one must not wait on the network.
            if (!File.Exists(databasePath)) break;
            using var connection = Open(databasePath);
            written += Write(connection, embedder.Model, batch, vectors);
        }

        return written;
    }

    private static SqliteConnection Open(string databasePath)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWrite,
            Pooling = false,
            DefaultTimeout = 5
        }.ToString());

        connection.Open();
        return connection;
    }

    /// <summary>The text a chapter is embedded as: its title, then its prose —
    /// <c>search_text</c>, the same text full-text search matches, with the
    /// <c>meta</c> and <c>annotation</c> fences and the diagrams already out.</summary>
    internal static string TextFor(string? title, string? searchText)
    {
        var text = string.IsNullOrWhiteSpace(title)
            ? (searchText ?? string.Empty).Trim()
            : $"{title.Trim()}\n\n{(searchText ?? string.Empty).Trim()}".Trim();

        return text.Length <= MaxCharacters ? text : text[..MaxCharacters];
    }

    /// <summary>A database this app wrote, in the schema it reads; anything else is
    /// left alone, and the next build replaces it.</summary>
    private static bool IsReadable(SqliteConnection connection)
    {
        try
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT value FROM meta WHERE key = $key";
            command.Parameters.AddWithValue("$key", DevbookDatabaseSchema.SchemaVersionKey);
            return command.ExecuteScalar() is string version
                && int.TryParse(version, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
                && parsed == DevbookDatabaseSchema.Version;
        }
        catch (SqliteException)
        {
            return false;
        }
    }

    /// <summary>One entry per distinct chapter text with no vector under
    /// <paramref name="model"/> and something to say, in reading order.</summary>
    private static List<Pending> Missing(SqliteConnection connection, string model)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT chapter.content_hash, chapter.title, chapter.search_text
            FROM chapter
            WHERE chapter.id IN (SELECT min(id) FROM chapter GROUP BY content_hash)
              AND NOT EXISTS (
                  SELECT 1 FROM chapter_embedding AS embedding
                  WHERE embedding.content_hash = chapter.content_hash AND embedding.model = $model)
            ORDER BY chapter.path, chapter.line
            """;
        command.Parameters.AddWithValue("$model", model);

        var pending = new List<Pending>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var text = TextFor(reader.IsDBNull(1) ? null : reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2));
            if (text.Length > 0) pending.Add(new Pending(reader.GetString(0), text));
        }

        return pending;
    }

    /// <summary>One batch's vectors and the model they are pinned to, in one
    /// transaction, so a reader never sees rows the meta does not account
    /// for.</summary>
    private static int Write(SqliteConnection connection, string model, IReadOnlyList<Pending> batch, IReadOnlyList<float[]> vectors)
    {
        using var transaction = connection.BeginTransaction();
        using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = """
            INSERT OR REPLACE INTO chapter_embedding (content_hash, model, dimensions, vector)
            VALUES ($hash, $model, $dimensions, $vector)
            """;

        var written = 0;
        for (var index = 0; index < batch.Count; index++)
        {
            var vector = vectors[index];
            if (vector is not { Length: > 0 }) continue;

            insert.Parameters.Clear();
            insert.Parameters.AddWithValue("$hash", batch[index].ContentHash);
            insert.Parameters.AddWithValue("$model", model);
            insert.Parameters.AddWithValue("$dimensions", vector.Length);
            insert.Parameters.AddWithValue("$vector", Encode(vector));
            insert.ExecuteNonQuery();
            written++;
        }

        using var meta = connection.CreateCommand();
        meta.Transaction = transaction;
        meta.CommandText = "INSERT OR REPLACE INTO meta (key, value) VALUES ($key, $value)";
        meta.Parameters.AddWithValue("$key", DevbookDatabaseSchema.EmbeddingModelKey);
        meta.Parameters.AddWithValue("$value", model);
        meta.ExecuteNonQuery();

        transaction.Commit();

        // Folded back into the file and the log emptied, so the -wal beside it
        // holds nothing a rebuild landing next could leave stale under a reader.
        using var checkpoint = connection.CreateCommand();
        checkpoint.CommandText = "PRAGMA wal_checkpoint(TRUNCATE)";
        checkpoint.ExecuteNonQuery();

        return written;
    }

    /// <summary>The layout <c>DevbookDatabase.ReadVector</c> decodes and
    /// <c>tools/devbook/devbook-schema.mjs</c> writes down: single-precision,
    /// little-endian, no header.</summary>
    internal static byte[] Encode(ReadOnlySpan<float> vector)
    {
        var bytes = new byte[vector.Length * sizeof(float)];
        for (var index = 0; index < vector.Length; index++)
        {
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(index * sizeof(float)), vector[index]);
        }

        return bytes;
    }

    private sealed record Pending(string ContentHash, string Text);
}
