using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Events.Abstractions;

/// <summary>The refusals the Events pane adds: what it asks for is not there, or not linked.</summary>
public static partial class EventsErrors
{
    public static Error KnowledgeEventNotFound(Guid id) =>
        Error.NotFound("knowledge_event.not_found", $"No knowledge event has the id {id:D}.");

    public static Error SeriesNotFound(Guid id) =>
        Error.NotFound("event_series.not_found", $"No event series has the id {id:D}.");

    public static Error NoteNotLinked() =>
        Error.Conflict("note_link.not_linked", "The event does not link that note.");

    public static Error NotePathMissing() =>
        Error.Validation("note_link.path_missing", "A Brain note is named by its path in the Brain.");

    public static Error PrepTaskTitleMissing() =>
        Error.Validation("event_task.title_missing", "A prep task needs a title.");

    public static Error TaskNotInBacklog() =>
        Error.NotFound("event_task.not_in_backlog", "No task with that id in this backlog.");
}
