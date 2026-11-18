using Backlog.Modules.Events.Abstractions;
using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Events.DomainModels;

/// <summary>
/// The organiser's recurring run of knowledge events: Techorama each year, dotNET Friday each
/// quarter (<c>.devbook/domain/events/domain.md#event-series</c>). It holds a name, the organiser
/// and a link to the series' page. Each edition is a <see cref="KnowledgeEvent"/> that names the
/// series by id, so editing a series never rewrites its editions. A series watches nothing.
/// <para>
/// Its rule is <c>.devbook/domain/events/domain.invariants.md#event-series</c>: a name that is not
/// empty, with a unit test in <c>Backlog.Modules.Events.UnitTests</c>.
/// </para>
/// </summary>
public sealed class EventSeries
{
    private EventSeries(Guid id, string name, string? organiser, string? link)
    {
        Id = id;
        Name = name;
        Organiser = organiser;
        Link = link;
    }

    public Guid Id { get; }

    /// <summary>Never empty.</summary>
    public string Name { get; private set; }

    /// <summary>Who runs the series, in free text, or null.</summary>
    public string? Organiser { get; }

    /// <summary>The series' page, or null.</summary>
    public string? Link { get; }

    /// <summary>A new series, its texts trimmed and a blank organiser or link read as none.
    /// Refused for a blank name (<c>event_series.name_missing</c>).</summary>
    public static Result<EventSeries> Create(Guid id, string name, string? organiser, string? link)
    {
        var trimmed = name?.Trim() ?? string.Empty;
        if (trimmed.Length == 0) return EventsErrors.SeriesNameMissing();

        return new EventSeries(id, trimmed, Blank(organiser), Blank(link));
    }

    /// <summary>Rehydration from storage. Trusted, as every stored aggregate is: the rules held
    /// when it was written.</summary>
    public static EventSeries Restore(Guid id, string name, string? organiser, string? link) =>
        new(id, name, Blank(organiser), Blank(link));

    /// <summary>Changes the name, trimmed. Refused for a blank one
    /// (<c>event_series.name_missing</c>).</summary>
    public Result Rename(string name)
    {
        var trimmed = name?.Trim() ?? string.Empty;
        if (trimmed.Length == 0) return Result.Failure(EventsErrors.SeriesNameMissing());

        Name = trimmed;
        return Result.Success();
    }

    private static string? Blank(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}
