namespace Backlog.Modules.Events.Abstractions;

/// <summary>Where a knowledge event stands
/// (<c>.devbook/domain/events/domain.md#knowledge-event-state</c>). It moves only forward on the
/// path Spotted, Considering, Going, Attended, Harvested; Skipped is reached from Spotted or
/// Considering alone. Harvested and Skipped are final.</summary>
public enum KnowledgeEventState
{
    /// <summary>Found, from a watched page, a colleague or a link. Dates may be unknown.</summary>
    Spotted,

    /// <summary>The person weighs what they would learn and what it costs.</summary>
    Considering,

    /// <summary>Decided. Ticket, travel and prep tasks follow.</summary>
    Going,

    /// <summary>The person was there.</summary>
    Attended,

    /// <summary>The takeaways are written up. Final.</summary>
    Harvested,

    /// <summary>Decided against, with a reason. Final.</summary>
    Skipped
}

/// <summary>Whether the person holds what the event needs to get in
/// (<c>.devbook/domain/events/domain.md#ticket-state</c>).</summary>
public enum TicketState
{
    /// <summary>The event needs a ticket the person does not have yet.</summary>
    NotBought,

    /// <summary>The person holds a ticket or a registration.</summary>
    Bought,

    /// <summary>The event is free and open, or needs no registration.</summary>
    NotNeeded
}

/// <summary>Where a knowledge event's dates came from
/// (<c>.devbook/domain/events/domain.md#date-source</c>).</summary>
public enum DateSource
{
    /// <summary>The person entered them.</summary>
    Typed,

    /// <summary>The capture read them from the edition's page.</summary>
    EventPage
}

/// <summary>Why an event points at a task (<c>.devbook/domain/events/domain.md#task-purpose</c>).</summary>
public enum TaskPurpose
{
    /// <summary>A task the pane filed from the event, such as booking a hotel.</summary>
    Prep,

    /// <summary>The task the Harvest Task Policy filed. An event holds at most one.</summary>
    Harvest,

    /// <summary>An existing task the person linked.</summary>
    Linked
}

/// <summary>Which kind of note a note link names (<c>.devbook/domain/events/domain.md#note-link</c>).</summary>
public enum NoteLinkKind
{
    /// <summary>A Brain knowledge note, named by its path from the Brain's root.</summary>
    Brain,

    /// <summary>A Talk note, an Inbox note named by its id.</summary>
    Talk
}
