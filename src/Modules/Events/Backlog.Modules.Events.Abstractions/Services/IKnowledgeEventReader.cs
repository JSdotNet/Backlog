using Backlog.Modules.Events.Abstractions.DataTransferObjects;

namespace Backlog.Modules.Events.Abstractions.Services;

/// <summary>
/// The events read contract (local ADR 0050 §7, <c>.devbook/domain/context-map.md</c>): the
/// decided events and their key dates, for a context that shows them and never changes them —
/// the Tasks Calendar, through a port of its own that an infrastructure adapter answers over
/// this one.
/// <para>
/// Read-only by design: a reader never reaches the aggregate or its repository, so the
/// Events context's rules hold wherever its events are shown.
/// </para>
/// </summary>
public interface IKnowledgeEventReader
{
    /// <summary>
    /// Every decided event — Considering, Going, Attended or Harvested — in the order the
    /// events were first saved. Spotted and Skipped are left out, so nothing the person has not
    /// weighed up reaches another context.
    /// </summary>
    Task<IReadOnlyList<DecidedEventDto>> ListDecidedAsync(CancellationToken cancellationToken = default);
}
