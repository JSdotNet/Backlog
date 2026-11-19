using System.Globalization;

using Microsoft.Data.Sqlite;

namespace Backlog.Mobile.UI.Storage;

/// <summary>The one way a view writes and reads an instant.</summary>
internal static class SqliteInstant
{
    /// <summary>UTC, round-trip: the stamps are compared, so none may lose a tick.</summary>
    public static string Format(DateTimeOffset value) =>
        value.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);

    public static DateTimeOffset Parse(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    /// <summary>Null for a SQL <c>NULL</c>.</summary>
    public static object? FormatOrNull(DateTimeOffset? value) => value is { } instant ? Format(instant) : null;

    public static DateTimeOffset? ParseOrNull(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : Parse(reader.GetString(ordinal));
}
