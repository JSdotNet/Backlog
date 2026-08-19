using System.Text.Json;
using System.Text.Json.Serialization;

namespace Backlog.Infrastructure.Sqlite;

/// <summary>
/// The owned collections of a task, as they are written into their JSON columns.
/// <para>
/// A task is one consistency boundary and the port only ever reads or writes a
/// whole one, so these collections are payload rather than query surface. Child
/// tables would buy six joins and a cascade policy for a shape nothing ever
/// queries into.
/// </para>
/// </summary>
internal static class TaskPayloads
{
    /// <summary>Shared options for every JSON column. Names are written as they
    /// are declared so the columns read like the domain does; nulls are dropped
    /// because an absent note and a null note are the same absent note.</summary>
    public static readonly JsonSerializerOptions Options = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string Write<T>(IReadOnlyList<T> items) =>
        items.Count == 0 ? "[]" : JsonSerializer.Serialize(items, Options);

    public static List<T> Read<T>(string? json) =>
        string.IsNullOrWhiteSpace(json)
            ? []
            : JsonSerializer.Deserialize<List<T>>(json, Options) ?? [];
}

internal sealed record SubItemPayload(string Id, string Title, string Status, string? Notes, int Order);

internal sealed record UsageEventPayload(DateTimeOffset Timestamp, string Action);

internal sealed record ProjectionPayload(string RepoId, string ExternalId, string TargetType);
