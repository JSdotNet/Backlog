using Backlog.Modules.Roadmap.Abstractions.DataTransferObjects;

namespace Backlog.Modules.Roadmap.Abstractions.Services;

/// <summary>
/// The notes in this device's Brain, as a roadmap item may gather them (local ADR 0053
/// §3 and §5): a Knowledge Ref written <c>#brain:&lt;path&gt;</c> resolves against
/// them, and gathering by tag reaches every note whose <c>roadmap</c> list names the
/// item's tag.
/// <para>
/// A port Roadmap owns and an adapter answers, as the Inbox's To Brain target is: this
/// context never references the Brain's. Only the Brain's owner — whoever has a Brain
/// folder set on this device — gets notes back. Every other reader, a host with no
/// Brain, or a Brain whose index will not answer, gets null, and the item's Brain refs
/// read as unresolved for want of a Brain rather than as missing from one.
/// </para>
/// <para>
/// Read-only, and read once per gathering: a plan-wide gather asks once, not once per
/// item.
/// </para>
/// </summary>
public interface IRoadmapBrainNotes
{
    /// <summary>Every note and every chapter of a note this device's Brain indexes,
    /// those naming no <c>roadmap</c> item included — a direct reference resolves
    /// against them too. Null when no Brain is set or its index cannot be read, so a
    /// reader can tell that from a Brain that holds no notes; never throws for
    /// either.</summary>
    Task<IReadOnlyList<RoadmapBrainNote>?> ReadAsync(CancellationToken cancellationToken = default);
}
