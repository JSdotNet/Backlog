using Backlog.SharedKernel.Results;

namespace Backlog.Modules.Events.Abstractions;

/// <summary>
/// Tasks, as Events is allowed to see them (<c>.devbook/domain/events/context.md</c>, local ADR
/// 0050 §3): a place that files a task with no repository, so it lands under the Brain scope, and
/// answers what a task id is called and where it stands. A port declared here and answered in
/// <c>src/Infrastructure</c> over Tasks' published contract, never a reference to Tasks.
/// <para>
/// Links run one way: nothing written through this port names the event.
/// </para>
/// </summary>
public interface IEventTaskTarget
{
    /// <summary>Files a task with <paramref name="title"/> alone — no repository, no tag, no
    /// body — and answers its id. A refusal names why, and no task is filed.</summary>
    Task<Result<string>> FileAsync(string title, CancellationToken cancellationToken = default);

    /// <summary>The tasks this backlog holds among <paramref name="taskIds"/>, by id. An id it
    /// does not hold is left out.</summary>
    Task<IReadOnlyDictionary<string, EventTaskFactsDto>> LookupAsync(
        IReadOnlyCollection<string> taskIds,
        CancellationToken cancellationToken = default);

    /// <summary>Raised after a task was written on this machine, on the writer's thread. It
    /// carries nothing: a reader asks again.</summary>
    event Action? Changed;
}
