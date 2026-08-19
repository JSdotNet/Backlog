using Backlog.Modules.Sessions.Abstractions;

namespace Backlog.Modules.Sessions.UI.Adapters;

/// <summary>
/// One reader's answer: the sessions it will describe, and how many it found.
/// <para>
/// The count is separate from the list because the two differ by design — a reader
/// stops at <see cref="AgentSessionLimits.PerAgent"/> — and a reader that returned
/// only the list would leave the surface unable to say what it dropped.
/// </para>
/// </summary>
internal sealed record SessionReading(IReadOnlyList<AgentSession> Sessions, int Discovered)
{
    internal static SessionReading None { get; } = new([], 0);
}
