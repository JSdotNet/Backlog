using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Events.Abstractions;

/// <summary>
/// The Events context's published contract (<c>.devbook/domain/context-map.md</c>, local ADR 0050):
/// what another context may ask of Events, in Events' own words. Today one ask — the Inbox's
/// To Event route (<c>.devbook/domain/events/features.md#to-event-route</c>) — answered in
/// <c>src/Infrastructure</c> over the Inbox's own port, never by a reference to the Inbox.
/// <para>
/// Not gated on <see cref="EventsFeatures.Events"/>: whoever offers the route withdraws it while
/// the area is off, and Events answers whoever asks.
/// </para>
/// </summary>
public interface IKnowledgeEvents
{
    /// <summary>
    /// Makes a Spotted knowledge event from what was found: its title, its link, and the dates
    /// and place a capture read, with the date source Event page. The event joins the series
    /// whose page is <see cref="SpotKnowledgeEventDto.SeriesPage"/>; when none has it, a new
    /// series is started, named after the page's title and linked to the page. With no page the
    /// event joins no series. Dates that break a rule are not a reason to refuse: an end before
    /// the start is dropped, a time with no offset keeps its day. Refused for a blank title
    /// (<c>knowledge_event.title_missing</c>), and then nothing is written.
    /// </summary>
    Task<Result<SpottedKnowledgeEventDto>> SpotAsync(SpotKnowledgeEventDto request, CancellationToken cancellationToken = default);
}

/// <summary>A date as a source wrote it: the day, and a time with its UTC offset only when
/// known (<c>.devbook/domain/events/domain.md#event-date</c>).</summary>
public sealed record EventDateDto(DateOnly Date, TimeOnly? Time = null, TimeSpan? UtcOffset = null);

/// <summary>What a new Spotted knowledge event is made from.</summary>
/// <param name="Title">The event's title.</param>
/// <param name="Link">The edition's own page, or null.</param>
/// <param name="Start">When it starts, or null when nothing was found.</param>
/// <param name="End">When it ends, or null.</param>
/// <param name="Place">Where, as written — a venue, a town, or <c>Online</c> — or null.</param>
/// <param name="SeriesPage">The watched page the edition was read from, which picks the
/// series, or null for an item from no watched page.</param>
/// <param name="SeriesPageTitle">That page's title, which names a new series.</param>
public sealed record SpotKnowledgeEventDto(
    string Title,
    string? Link,
    EventDateDto? Start,
    EventDateDto? End,
    string? Place,
    string? SeriesPage,
    string? SeriesPageTitle);

/// <summary>The event made, the series it joined, and whether that series is new.</summary>
public sealed record SpottedKnowledgeEventDto(Guid EventId, Guid? SeriesId, bool SeriesStarted);
