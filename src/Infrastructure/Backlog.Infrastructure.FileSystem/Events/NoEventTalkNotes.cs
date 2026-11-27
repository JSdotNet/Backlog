using Backlog.Modules.Events.Abstractions;
using Backlog.SharedKernel.Results;

namespace Backlog.Infrastructure.FileSystem.Events;

/// <summary>The Talk notes of a host composed without a Brain: on harvest every note stays in the
/// Inbox and the event keeps its Talk link (local ADR 0073 §8).</summary>
internal sealed class NoEventTalkNotes : IEventTalkNotes
{
    private static readonly Error NoBrain =
        Error.Unexpected("event_talk_note.no_brain", "There is no Brain here to file the note into.");

    public Task<EventBrainNoteDto?> FileIntoBrainAsync(Guid noteId, CancellationToken cancellationToken = default) =>
        Task.FromResult<EventBrainNoteDto?>(null);

    public Task<Result> ArchiveAsync(Guid noteId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Result.Failure(NoBrain));
}
