namespace Backlog.Modules.Events.Abstractions.DataTransferObjects;

/// <summary>
/// One decided knowledge event as another context may read it
/// (<c>.devbook/domain/events/domain.md#knowledge-event</c>): its days, its place and its key
/// dates, and nothing of its decision or its harvest.
/// </summary>
/// <param name="Start">The first day, or null while the dates are unknown.</param>
/// <param name="End">The last day, or null: an event with a start and no end lasts its start
/// day (<c>.devbook/domain/events/domain.md#event-date</c>).</param>
/// <param name="Venue">Where it happens, or null for an online event or one with no place
/// yet (<c>.devbook/domain/events/domain.md#place</c>).</param>
/// <param name="Online">Whether it happens online rather than at a venue.</param>
/// <param name="KeyDates">The dates that matter before it, in the event's order
/// (<c>.devbook/domain/events/domain.md#key-date</c>).</param>
public sealed record KnowledgeEventDto(
    Guid Id,
    string Title,
    KnowledgeEventState State,
    DateOnly? Start,
    DateOnly? End,
    string? Venue,
    bool Online,
    IReadOnlyList<KeyDateDto> KeyDates);

/// <summary>A date that matters before an event, with its label, such as "Early bird
/// ends".</summary>
public sealed record KeyDateDto(DateOnly Date, string Label);
