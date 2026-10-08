using System.Globalization;

using Backlog.Modules.Sessions.Abstractions;

using Microsoft.Data.Sqlite;

namespace Backlog.Infrastructure.Sqlite.Sessions;

/// <summary>
/// The Sessions context's <see cref="IModelPriceStore"/>: the rates the person entered
/// in Settings, one row per model in the <c>model_prices</c> table of the same
/// <c>backlog.db</c> the <c>claude_api_requests</c> live in.
/// <para>
/// The table is this context's, created by idempotent <c>IF NOT EXISTS</c> DDL on every
/// open (local ADR 0003). A rate is kept as invariant decimal text rather than a REAL,
/// so a price typed as 0.30 reads back as 0.30 and not as the nearest double to it; an
/// empty rate is NULL, which is unknown and never free.
/// </para>
/// <para>
/// A save replaces the table whole, in one transaction, in the order the page lists the
/// rows — the table is a handful of rows the person edits as one thing.
/// </para>
/// </summary>
public sealed class SqliteModelPriceStore : IModelPriceStore
{
    private readonly string _databasePath;

    /// <summary>A store over the database in the given workspace folder.</summary>
    public SqliteModelPriceStore(string rootDir)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDir);

        _databasePath = SqliteTaskRepository.DatabasePathFor(rootDir);
    }

    /// <summary>The database file this store reads and writes.</summary>
    public string DatabasePath => _databasePath;

    public async Task<ModelPriceTable> GetAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(_databasePath, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();

        command.CommandText =
            "SELECT model, input_per_mtok, output_per_mtok, cache_read_per_mtok, cache_write_per_mtok FROM model_prices ORDER BY position, model;";

        var prices = new List<ModelPrice>();

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            prices.Add(new ModelPrice(reader.GetString(0), Rate(reader, 1), Rate(reader, 2), Rate(reader, 3), Rate(reader, 4)));
        }

        return prices.Count == 0 ? ModelPriceTable.Empty : new ModelPriceTable(prices);
    }

    public async Task SaveAsync(ModelPriceTable table, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(table);

        await using var connection = await OpenAsync(_databasePath, cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        await using (var clear = connection.CreateCommand())
        {
            clear.Transaction = transaction;
            clear.CommandText = "DELETE FROM model_prices;";
            await clear.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText =
            """
            INSERT OR REPLACE INTO model_prices (model, position, input_per_mtok, output_per_mtok, cache_read_per_mtok, cache_write_per_mtok)
            VALUES ($model, $position, $input, $output, $cache_read, $cache_write);
            """;

        var position = 0;

        foreach (var price in table.Prices)
        {
            // A row with no model names nothing a run could be priced by.
            if (string.IsNullOrWhiteSpace(price.Model)) continue;

            insert.Parameters.Clear();
            insert.Parameters.AddWithValue("$model", price.Model.Trim());
            insert.Parameters.AddWithValue("$position", position++);
            insert.Parameters.AddWithValue("$input", Text(price.InputPerMTok));
            insert.Parameters.AddWithValue("$output", Text(price.OutputPerMTok));
            insert.Parameters.AddWithValue("$cache_read", Text(price.CacheReadPerMTok));
            insert.Parameters.AddWithValue("$cache_write", Text(price.CacheWritePerMTok));

            await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private static object Text(decimal? rate) => rate is { } value ? value.ToString(CultureInfo.InvariantCulture) : DBNull.Value;

    private static decimal? Rate(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal)
            ? null
            : decimal.TryParse(reader.GetString(ordinal), NumberStyles.Number, CultureInfo.InvariantCulture, out var rate) ? rate : null;

    private static Task<SqliteConnection> OpenAsync(string databasePath, CancellationToken cancellationToken) =>
        SqliteSchema.OpenAsync(databasePath, EnsureSchemaAsync, cancellationToken);

    private static async Task EnsureSchemaAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await SqliteSchema.EnsureAsync(
            connection,
            """
            CREATE TABLE IF NOT EXISTS model_prices (
                model                TEXT PRIMARY KEY NOT NULL COLLATE NOCASE,
                position             INTEGER NOT NULL,
                input_per_mtok       TEXT NULL,
                output_per_mtok      TEXT NULL,
                cache_read_per_mtok  TEXT NULL,
                cache_write_per_mtok TEXT NULL
            );
            """,
            cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>
/// A <see cref="SqliteModelPriceStore"/> that follows a workspace root somebody can move
/// while the app is open, the arrangement <see cref="RootedSqliteClaudeApiRequestStore"/>
/// uses over the same file.
/// </summary>
public sealed class RootedSqliteModelPriceStore(Func<string> currentRootDirectory) : IModelPriceStore
{
    private readonly Func<string> _currentRootDirectory =
        currentRootDirectory ?? throw new ArgumentNullException(nameof(currentRootDirectory));

    private readonly Lock _gate = new();
    private string? _rootDirectory;
    private SqliteModelPriceStore? _store;

    private SqliteModelPriceStore Current
    {
        get
        {
            var root = _currentRootDirectory();

            lock (_gate)
            {
                if (_store is null || !string.Equals(_rootDirectory, root, StringComparison.OrdinalIgnoreCase))
                {
                    _rootDirectory = root;
                    _store = new SqliteModelPriceStore(root);
                }

                return _store;
            }
        }
    }

    public Task<ModelPriceTable> GetAsync(CancellationToken cancellationToken = default) => Current.GetAsync(cancellationToken);

    public Task SaveAsync(ModelPriceTable table, CancellationToken cancellationToken = default) => Current.SaveAsync(table, cancellationToken);
}
