using System.Text.Json;
using System.Text.Json.Serialization;

using Backlog.Modules.Tasks.Abstractions;
using Backlog.Modules.Tasks.Abstractions.Connectors;

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

    /// <summary>The <c>source_ref</c> column: one JSON object, or null for local
    /// work.</summary>
    public static string? WriteSourceRef(SourceRef? sourceRef) =>
        sourceRef is null
            ? null
            : JsonSerializer.Serialize(
                new SourceRefPayload(
                    sourceRef.ConnectorId,
                    sourceRef.Target,
                    sourceRef.ExternalId,
                    sourceRef.Url,
                    sourceRef.DisplayKey,
                    sourceRef.Assignee,
                    sourceRef.SourceState,
                    sourceRef.SourceUpdatedAt,
                    sourceRef.Flags.Count == 0 ? null : [.. sourceRef.Flags],
                    sourceRef.NormalisedState is { } state ? NormalisedSourceStates.ToWire(state) : null,
                    sourceRef.SourceTitle,
                    sourceRef.Blocked ? true : null,
                    sourceRef.BlockedReason),
                Options);

    /// <summary>Reads the <c>source_ref</c> column back. A value that does not parse,
    /// or names no connector or item, reads as no reference: one hand-edited row
    /// must cost that link, not every read of the task — the rule the Devbook
    /// references are read by.</summary>
    public static SourceRef? ReadSourceRef(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;

        SourceRefPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<SourceRefPayload>(json, Options);
        }
        catch (JsonException)
        {
            return null;
        }

        if (payload is null
            || string.IsNullOrWhiteSpace(payload.ConnectorId)
            || string.IsNullOrWhiteSpace(payload.ExternalId))
        {
            return null;
        }

        return new SourceRef(
            payload.ConnectorId,
            payload.Target,
            payload.ExternalId,
            payload.Url,
            payload.DisplayKey,
            payload.Assignee,
            payload.SourceState,
            payload.SourceUpdatedAt,
            payload.Flags,
            NormalisedSourceStates.FromWire(payload.NormalisedState),
            payload.SourceTitle)
        {
            Blocked = payload.Blocked == true,
            BlockedReason = payload.BlockedReason,
        };
    }
}

internal sealed record SubItemPayload(string Id, string Title, string Status, string? Notes, int Order);

internal sealed record UsageEventPayload(DateTimeOffset Timestamp, string Action);

internal sealed record ProjectionPayload(string RepoId, string ExternalId, string TargetType);

internal sealed record SourceRefPayload(
    string ConnectorId,
    string Target,
    string ExternalId,
    string Url,
    string DisplayKey,
    string? Assignee,
    string SourceState,
    DateTimeOffset SourceUpdatedAt,
    List<string>? Flags,
    string? NormalisedState = null,
    string? SourceTitle = null,
    bool? Blocked = null,
    string? BlockedReason = null);
