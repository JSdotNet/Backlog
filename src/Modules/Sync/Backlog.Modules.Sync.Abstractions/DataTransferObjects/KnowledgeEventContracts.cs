using System.Text.Json;

namespace Backlog.Modules.Sync.Abstractions.DataTransferObjects;

/// <summary>
/// One knowledge event or event series as a device pushes it or pulls it (local ADR 0050
/// §2): the envelope the sync service reads, and the event itself as an opaque document it
/// stores and hands back unread.
/// <para>
/// The service reads only <see cref="Id"/>, <see cref="Kind"/> and <see cref="UpdatedAt"/>.
/// Everything a viewer shows — title, dates, place, link, state, key dates, what to learn,
/// notes, note links — travels inside <see cref="Document"/>, whose shape belongs to the
/// Events context and not to Sync (<c>.devbook/domain/events/context.md</c>: Sync stores the
/// documents and reads none of their fields). The notification envelope's arrangement
/// (local ADR 0039), on the devbook subject feed's terms (local ADR 0044): every desktop may
/// write, the later <see cref="UpdatedAt"/> wins whole, and nothing is deleted.
/// </para>
/// <para>
/// No owner and no device id, for the reason <see cref="TaskChange"/> gives: both come from
/// the caller's validated token.
/// </para>
/// </summary>
/// <param name="Id">The event's or the series' own id.</param>
/// <param name="Kind">Which of the two documents this is — one of
/// <see cref="KnowledgeEventKinds"/>.</param>
/// <param name="UpdatedAt">When a desktop last changed it, on that desktop's clock, and what
/// the replica compares.</param>
/// <param name="Document">The event or the series itself, stored whole and never read
/// here.</param>
public sealed record KnowledgeEventChange(Guid Id, string Kind, DateTimeOffset UpdatedAt, JsonElement Document);

/// <summary>The document kinds the knowledge event feed carries. Series ride the same feed as
/// the events that name them, so a phone that holds an event can show its series.</summary>
public static class KnowledgeEventKinds
{
    /// <summary>One knowledge event.</summary>
    public const string KnowledgeEvent = "knowledge-event";

    /// <summary>One event series.</summary>
    public const string EventSeries = "event-series";

    /// <summary>Every kind a push may carry.</summary>
    public static IReadOnlyList<string> All { get; } = [KnowledgeEvent, EventSeries];

    /// <summary>Whether <paramref name="kind"/> is one of <see cref="All"/>, compared
    /// ordinally.</summary>
    public static bool IsKnown(string? kind) => kind is not null && All.Contains(kind, StringComparer.Ordinal);
}

/// <summary>
/// A change as it comes back out of the replica: the change itself, the device that wrote
/// it, and the store's own ordering stamp — see <see cref="TaskChangeRecord"/> for why both
/// are on the way out and neither on the way in.
/// </summary>
public sealed record KnowledgeEventChangeRecord(KnowledgeEventChange Change, Guid DeviceId, long ServerTimestamp);

/// <summary>A batch of changes from one device.</summary>
public sealed record PushKnowledgeEventsRequest(IReadOnlyList<KnowledgeEventChange> Events);

/// <summary>How many changes the replica took — short of the batch by every change that was
/// not a later version than the one it held.</summary>
public sealed record PushKnowledgeEventsResponse(int Accepted);

/// <summary>One page of the owner's knowledge event feed, and where to resume —
/// <see cref="PullTasksResponse"/>'s contract: <paramref name="Since"/> is always fresh, and
/// only <paramref name="HasMore"/> being false ends a client's loop.</summary>
public sealed record PullKnowledgeEventsResponse(IReadOnlyList<KnowledgeEventChangeRecord> Events, string Since, bool HasMore);

/// <summary>
/// What a <see cref="KnowledgeEventChange"/> must be for the service to store it, and
/// <see cref="WhyRefused"/>, the one rule both ends run: the service refuses a push whole
/// when one change in it breaks the rule, so the desktop leaves such a change out before it
/// batches rather than have it refuse every batch it is in, on every cycle.
/// </summary>
public static class KnowledgeEventRecordLimits
{
    /// <summary>The most an event document may weigh as serialized JSON. An event's notes
    /// have no bound of their own in the Events model, so this is the wire's: far above a
    /// conference's worth of notes, and twice a notification's bound.</summary>
    public const int DocumentBytes = 128 * 1024;

    /// <summary>
    /// Why <paramref name="change"/> would be refused, or null when it would be stored. A
    /// reason names the field and never repeats the caller's value, for the reason
    /// <see cref="DevbookSubjectRecordLimits.WhyRefused"/> gives: it goes back in the
    /// <c>400</c> and into the desktop's warning.
    /// </summary>
    public static string? WhyRefused(KnowledgeEventChange change)
    {
        ArgumentNullException.ThrowIfNull(change);

        if (change.Id == Guid.Empty) return "A knowledge event change needs the id of the event or series it changes.";
        if (!KnowledgeEventKinds.IsKnown(change.Kind))
            return $"A knowledge event change's kind is {string.Join(" or ", KnowledgeEventKinds.All)}.";
        if (change.Document.ValueKind is not JsonValueKind.Object) return "A knowledge event document is a JSON object.";
        if (change.Document.GetRawText().Length > DocumentBytes)
            return $"A knowledge event document holds at most {DocumentBytes} characters.";

        return null;
    }
}
