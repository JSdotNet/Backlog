using System.Text.Json;
using System.Text.Json.Serialization;

using Backlog.Modules.Events.DomainModels;

namespace Backlog.Infrastructure.Sqlite.Events;

/// <summary>
/// One event series as the JSON document its row holds: the owner's local copy of the module's
/// owned document (local ADR 0050 §2). Versioned so a later shape can be told from this one.
/// </summary>
internal sealed record EventSeriesDocument(
    int Version,
    Guid Id,
    string Name,
    string? Organiser,
    string? Link)
{
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>The only shape written so far.</summary>
    public const int CurrentVersion = 1;

    public static EventSeriesDocument From(EventSeries series) => new(
        CurrentVersion,
        series.Id,
        series.Name,
        series.Organiser,
        series.Link);

    /// <summary>The series this document holds. Throws <see cref="JsonException"/> for one that
    /// parsed but does not read as a series — a later shape, no id, no name — so the store skips
    /// the row rather than handing the module a series without them.</summary>
    public EventSeries ToEventSeries()
    {
        if (Version != CurrentVersion) throw new JsonException($"An event series document of version {Version} is not one this build reads.");
        if (Id == Guid.Empty) throw new JsonException("An event series document names its id.");
        if (string.IsNullOrWhiteSpace(Name)) throw new JsonException("An event series document has a name.");

        return EventSeries.Restore(Id, Name.Trim(), Organiser, Link);
    }
}
