using Backlog.Modules.Events.Abstractions;

namespace Backlog.Infrastructure.FileSystem.Events;

/// <summary>The Brain notes of a host composed without a Brain: every linked note reads as
/// missing, the picker offers none, and the pane says why.</summary>
internal sealed class NoEventBrainNotes : IEventBrainNotes
{
    /// <summary>What the pane says in a host composed without a Brain.</summary>
    internal const string NoBrainHere = "There is no Brain here to link notes from.";

    public string? UnavailableReason => NoBrainHere;

    public Task<IReadOnlyDictionary<string, EventBrainNoteDto>> ResolveAsync(IReadOnlyCollection<string> paths, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyDictionary<string, EventBrainNoteDto>>(new Dictionary<string, EventBrainNoteDto>());

    public Task<IReadOnlyList<EventBrainNoteDto>> CandidatesAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<EventBrainNoteDto>>([]);

    // Nothing to watch: a host without a Brain never gets one.
    public event Action? Changed
    {
        add { }
        remove { }
    }
}
