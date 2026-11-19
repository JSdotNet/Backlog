using Backlog.Modules.Events.Abstractions;
using Backlog.Modules.Events.Abstractions.DataTransferObjects;

namespace Backlog.Mobile.UI.Events;

/// <summary>
/// One knowledge event as the phone shows it: the whole event a desktop pushed, read only, and
/// the name of the series it is an edition of when the phone holds that series
/// (<c>.devbook/domain/events/requirements.md#events-on-the-phone</c>). The screens show the
/// title, state, dates, place, link, key dates, what to learn and notes, and ignore the rest.
/// </summary>
/// <param name="Event">The event as it travelled.</param>
/// <param name="SeriesName">Its series' name, or null when it names none or the phone holds none by that id.</param>
public sealed record PhoneEvent(KnowledgeEventReplicaDto Event, string? SeriesName)
{
    public Guid Id => Event.Id;

    public string Title => Event.Title;

    public KnowledgeEventState State => Event.State;

    /// <summary>The first day, or null for an event with no dates yet.</summary>
    public DateOnly? FirstDay => Event.Start?.Date;

    /// <summary>The last day: the end, else the start — an event with a start and no end lasts
    /// its start day. Null for an event with no dates yet.</summary>
    public DateOnly? LastDay => Event.End?.Date ?? Event.Start?.Date;
}
