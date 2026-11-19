using Microsoft.Data.Sqlite;

namespace Backlog.Mobile.UI.Storage;

/// <summary>One column of a <c>&lt;prefix&gt;_view</c> table beside its <c>id</c>: its name, its
/// SQLite declaration, and what a row writes into it — null for SQL <c>NULL</c>.</summary>
internal sealed record SqliteViewColumn<TRow>(string Name, string Declaration, Func<TRow, object?> Value);

/// <summary>How one view's rows map onto its <c>&lt;prefix&gt;_view</c> table.</summary>
/// <param name="Id">The row's primary key.</param>
/// <param name="Columns">Every column after <c>id</c>, in table order.</param>
/// <param name="Read">A row from a reader whose ordinal 0 is <c>id</c> and whose next ordinals
/// are <paramref name="Columns"/> in order; null passes the row over.</param>
/// <param name="KeepLater">Given the view table's name, a condition on <c>excluded</c> and that
/// table which an upsert must meet to overwrite what is kept, or null to overwrite always.</param>
internal sealed record SqliteViewMapping<TRow>(
    Func<TRow, Guid> Id,
    IReadOnlyList<SqliteViewColumn<TRow>> Columns,
    Func<SqliteDataReader, TRow?> Read,
    Func<string, string>? KeepLater = null);
