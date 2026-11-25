namespace Backlog.Modules.Events.Abstractions.DataTransferObjects;

/// <summary>
/// One decided knowledge event as another context may read it
/// (<c>.devbook/domain/events/domain.md#knowledge-event</c>): its days, its place and its key
/// dates, and nothing of its decision or its harvest.
/// </summary>
/// <param name="Start">The first day, or null while the dates are unknown.</param>
/// <param name="End">The last day, or null: an event with a start and no end lasts its start
/// day (<c>.devbook/domain/events/domain.md#event-date</c>).</param>
/// <param name="Venue">The venue's name, or null for an online event, one with no place yet,
/// or one held at an address alone (<c>.devbook/domain/events/domain.md#place</c>).</param>
/// <param name="Online">Whether it happens online rather than at a venue.</param>
/// <param name="KeyDates">The dates that matter before it, in the event's order
/// (<c>.devbook/domain/events/domain.md#key-date</c>), in the pane's own
/// <see cref="KeyDateDto"/> shape.</param>
/// <param name="Address">The in-person event's address on one line, its parts that are there
/// joined in order — street, postcode and town, country — or null when it has none
/// (<c>.devbook/domain/events/domain.md#address</c>). Where there is no venue name, this is
/// where the event is held.</param>
public sealed record DecidedEventDto(
    Guid Id,
    string Title,
    KnowledgeEventState State,
    DateOnly? Start,
    DateOnly? End,
    string? Venue,
    bool Online,
    IReadOnlyList<KeyDateDto> KeyDates,
    string? Address = null);
