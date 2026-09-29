using System.Globalization;
using System.Text.Json;

namespace Backlog.Infrastructure;

/// <summary>
/// The instants GitHub and Anthropic answer with, and the ones sent back to them
/// in a query string. Always the invariant culture: a machine's own culture — or
/// a time separator its owner changed in the regional settings — must never turn
/// "2026-03-04T05:06:07Z" into another instant or into nothing.
/// </summary>
/// <remarks>
/// One copy for every outbound adapter so the next one cannot drift. The Claude
/// adapter compiles this same file (a linked <c>Compile</c> item in its project)
/// rather than referencing the GitHub adapter.
/// </remarks>
internal static class IsoTimestamps
{
    /// <summary>The ISO-8601 string property <paramref name="name"/> of an object,
    /// or null when the element is not an object, the property is missing or not a
    /// string, or the string is not a timestamp.</summary>
    internal static DateTimeOffset? Timestamp(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String
        && DateTimeOffset.TryParse(value.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : null;

    /// <summary>An instant as RFC 3339 in UTC, to the second: <c>yyyy-MM-ddTHH:mm:ssZ</c>.</summary>
    internal static string Rfc3339(DateTimeOffset instant) =>
        instant.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
}
