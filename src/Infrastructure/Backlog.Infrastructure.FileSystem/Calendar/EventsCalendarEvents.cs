using Backlog.Modules.Calendar.Abstractions.DataTransferObjects;
using Backlog.Modules.Calendar.Abstractions.Services;
using Backlog.Modules.Events.Abstractions;
using Backlog.Modules.Events.Abstractions.DataTransferObjects;
using Backlog.Modules.Events.Abstractions.Services;
using Backlog.SharedKernel;

namespace Backlog.Infrastructure.FileSystem.Calendar;

/// <summary>
/// ADAPTER — answers the Calendar's <see cref="ICalendarEvents"/> (local ADR 0055 §2) from
/// the Events context's published read contract, <see cref="IKnowledgeEventReader"/>, the
/// way <see cref="RoadmapCalendarPlans"/> answers the plans from Roadmap's.
/// <para>
/// It holds no event rule: which events are decided is the contract's answer. It only puts
/// them in the Calendar's words — Considering tentative and the rest filled, the place as the
/// venue, else the address on one line, or <c>Online</c>, an event with no end as its start day — and drops the bar of an
/// event with no start while keeping its key dates, which still have a day.
/// </para>
/// <para>
/// With the <c>events</c> area off nothing is read, and the Calendar offers no events. No
/// feature settings to ask reads as off too — unlike the roadmap's, the flag starts off, so
/// the fail-closed answer is the area's own default. Whether the events are shown is the
/// Calendar's own view choice, not this adapter's.
/// </para>
/// </summary>
public sealed class EventsCalendarEvents : ICalendarEvents
{
    private const string Online = "Online";

    private readonly IKnowledgeEventReader _reader;
    private readonly IAppFeatureSettings? _features;

    public EventsCalendarEvents(IKnowledgeEventReader reader, IAppFeatureSettings? features = null)
    {
        ArgumentNullException.ThrowIfNull(reader);

        _reader = reader;
        _features = features;
    }

    public async Task<CalendarEventsDto> ReadAsync(CancellationToken cancellationToken = default)
    {
        if (!EventsOn) return CalendarEventsDto.Off;

        var decided = await _reader.ListDecidedAsync(cancellationToken).ConfigureAwait(false);

        return new CalendarEventsDto(
            [
                .. decided
                    .Where(knowledgeEvent => knowledgeEvent.Start is not null)
                    .Select(knowledgeEvent => new CalendarEventDto(
                        knowledgeEvent.Title,
                        knowledgeEvent.State.ToString(),
                        PlaceOf(knowledgeEvent),
                        knowledgeEvent.Start!.Value,
                        knowledgeEvent.End ?? knowledgeEvent.Start.Value,
                        Tentative: knowledgeEvent.State == KnowledgeEventState.Considering))
            ],
            [
                .. decided.SelectMany(knowledgeEvent => knowledgeEvent.KeyDates.Select(keyDate =>
                    new CalendarKeyDateDto(keyDate.Label, knowledgeEvent.Title, keyDate.Date)))
            ]);
    }

    private bool EventsOn => _features?.IsEnabled(EventsFeatures.Events) ?? false;

    private static string? PlaceOf(DecidedEventDto knowledgeEvent) =>
        knowledgeEvent.Venue ?? knowledgeEvent.Address ?? (knowledgeEvent.Online ? Online : null);
}
