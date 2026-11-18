using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Events.Abstractions;

/// <summary>
/// Every way changing a knowledge event or an event series can be refused, named once. The
/// codes are the ones <c>.devbook/domain/events/domain.invariants.md</c> gives each rule, so a
/// screen can match on them rather than on a message.
/// </summary>
public static partial class EventsErrors
{
    // --- Knowledge Event (.devbook/domain/events/domain.invariants.md#knowledge-event) ---

    public static Error TitleMissing() =>
        Error.Validation("knowledge_event.title_missing", "A knowledge event needs a title.");

    public static Error DatesOutOfOrder() =>
        Error.Validation("knowledge_event.dates_out_of_order", "An event ends on or after its start, and an end needs a start.");

    public static Error DateSourceMismatch() =>
        Error.Validation("knowledge_event.date_source_mismatch", "An event with a start names where its dates came from, and one with no dates names no source.");

    public static Error StateBackward(KnowledgeEventState from, KnowledgeEventState to) =>
        Error.Conflict("knowledge_event.state_backward", to == KnowledgeEventState.Skipped
            ? "Skipped is reached by skipping the event, with a reason."
            : $"The event is {Named(from)} and moves only forward, never to {Named(to)}.");

    public static Error CannotSkip(KnowledgeEventState state) =>
        Error.Conflict("knowledge_event.cannot_skip", $"Only a Spotted or Considering event is skipped, and this one is {Named(state)}.");

    public static Error SkipReasonMissing() =>
        Error.Validation("knowledge_event.skip_reason_missing", "A skipped event needs a reason.");

    public static Error StateFinal(KnowledgeEventState state) =>
        Error.Conflict("knowledge_event.state_final", $"The event is {Named(state)}, which is final.");

    public static Error VerdictTooEarly(KnowledgeEventState state) =>
        Error.Conflict("knowledge_event.verdict_too_early", $"A verdict waits until the event is Attended, and this one is {Named(state)}.");

    public static Error HarvestTaskExists() =>
        Error.Conflict("knowledge_event.harvest_task_exists", "The event already holds its harvest task.");

    // --- The value objects a knowledge event owns ---------------------------------------

    public static Error EventDateOffsetMissing() =>
        Error.Validation("event_date.offset_missing", "A time of day carries its UTC offset.");

    public static Error PlaceInvalid() =>
        Error.Validation("place.invalid", "A place is a venue or online, never both and never neither.");

    public static Error KeyDateLabelMissing() =>
        Error.Validation("key_date.label_missing", "A key date needs a label.");

    public static Error CostInvalid(string reason) =>
        Error.Validation("cost.invalid", reason);

    // --- Event Series (.devbook/domain/events/domain.invariants.md#event-series) ---------

    public static Error SeriesNameMissing() =>
        Error.Validation("event_series.name_missing", "An event series needs a name.");

    private static string Named(KnowledgeEventState state) => state.ToString();
}
