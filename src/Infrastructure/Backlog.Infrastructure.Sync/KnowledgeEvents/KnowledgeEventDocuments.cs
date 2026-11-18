using System.Text.Json;
using System.Text.Json.Serialization;

using Backlog.Modules.Events.Abstractions.DataTransferObjects;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;

namespace Backlog.Infrastructure.Sync.KnowledgeEvents;

/// <summary>
/// The knowledge event feed's opaque document (local ADR 0050 §2), written and read: an
/// <see cref="KnowledgeEventReplicaDto"/> or an <see cref="EventSeriesReplicaDto"/> as the JSON
/// the sync service stores unread inside a <see cref="KnowledgeEventChange"/>. The one place
/// the wire shape is serialized (guideline ADR 0014), so the desktop that pushes and the phone
/// that pulls read it alike.
/// <para>
/// camelCase names, and enum values as kebab-case tokens like the feed's own kinds —
/// <c>"going"</c>, <c>"event-page"</c>, <c>"talk"</c>, <c>"not-bought"</c>. Dates are ISO
/// dates, times <c>"HH:mm:ss"</c>, offsets <c>"hh:mm:ss"</c>; a value that is not set is left
/// out. A document is read leniently in what it leaves out — an absent list is none — and
/// strictly in what it says: a value outside its set, or an id other than the envelope's,
/// does not read.
/// </para>
/// </summary>
public static class KnowledgeEventDocuments
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.KebabCaseLower, allowIntegerValues: false) },
    };

    /// <summary>The change that carries <paramref name="copy"/>, stamped with its
    /// <see cref="KnowledgeEventReplicaDto.UpdatedAt"/>.</summary>
    public static KnowledgeEventChange ToChange(KnowledgeEventReplicaDto copy)
    {
        ArgumentNullException.ThrowIfNull(copy);

        return new KnowledgeEventChange(
            copy.Id,
            KnowledgeEventKinds.KnowledgeEvent,
            copy.UpdatedAt,
            JsonSerializer.SerializeToElement(copy, Options));
    }

    /// <summary>The change that carries <paramref name="copy"/>.</summary>
    public static KnowledgeEventChange ToChange(EventSeriesReplicaDto copy)
    {
        ArgumentNullException.ThrowIfNull(copy);

        return new KnowledgeEventChange(
            copy.Id,
            KnowledgeEventKinds.EventSeries,
            copy.UpdatedAt,
            JsonSerializer.SerializeToElement(copy, Options));
    }

    /// <summary>
    /// The event <paramref name="change"/> carries, at the envelope's stamp — the one the replica
    /// compared — or null when it is not a <see cref="KnowledgeEventKinds.KnowledgeEvent"/>, or its
    /// document does not read as the event the envelope names.
    /// </summary>
    public static KnowledgeEventReplicaDto? ReadEvent(KnowledgeEventChange change)
    {
        ArgumentNullException.ThrowIfNull(change);

        if (!string.Equals(change.Kind, KnowledgeEventKinds.KnowledgeEvent, StringComparison.Ordinal)) return null;

        if (Read<KnowledgeEventReplicaDto>(change.Document) is not { } copy
            || copy.Id != change.Id
            || copy.Title is null)
        {
            return null;
        }

        return copy with
        {
            KeyDates = copy.KeyDates ?? [],
            Notes = copy.Notes ?? [],
            Tasks = copy.Tasks ?? [],
            UpdatedAt = change.UpdatedAt,
        };
    }

    /// <summary>The series <paramref name="change"/> carries, at the envelope's stamp, or null
    /// on <see cref="ReadEvent"/>'s terms.</summary>
    public static EventSeriesReplicaDto? ReadSeries(KnowledgeEventChange change)
    {
        ArgumentNullException.ThrowIfNull(change);

        if (!string.Equals(change.Kind, KnowledgeEventKinds.EventSeries, StringComparison.Ordinal)) return null;

        if (Read<EventSeriesReplicaDto>(change.Document) is not { } copy
            || copy.Id != change.Id
            || copy.Name is null)
        {
            return null;
        }

        return copy with { UpdatedAt = change.UpdatedAt };
    }

    private static T? Read<T>(JsonElement document) where T : class
    {
        if (document.ValueKind is not JsonValueKind.Object) return null;

        try
        {
            return document.Deserialize<T>(Options);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }
}
