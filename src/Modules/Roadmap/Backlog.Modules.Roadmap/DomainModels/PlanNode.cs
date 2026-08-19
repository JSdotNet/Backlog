namespace Backlog.Modules.Roadmap.DomainModels;

/// <summary>
/// One end of a dependency, whichever kind of thing it is: an id, what it is
/// called, the day it opens, the day it closes, and what it waits for.
/// <para>
/// This exists so the graph questions can be asked once instead of twice. An item
/// opens and closes on the two ends of its window; a milestone opens and closes on
/// the single day it falls. Every rule about waiting is the same for both after
/// that, and a common base class on the entities themselves would have implied a
/// shared lifecycle they do not have.
/// </para>
/// </summary>
/// <param name="Opens">The first day the thing occupies.</param>
/// <param name="Closes">The last day it occupies — the same day as
/// <paramref name="Opens"/> for a milestone, which has no duration.</param>
public readonly record struct PlanNode(
    Guid Id,
    string Title,
    DateOnly Opens,
    DateOnly Closes,
    IReadOnlyList<Guid> DependsOn);

/// <summary>
/// Somewhere the plan disagrees with itself about dates: <paramref name="NodeId"/>
/// opens on or before <paramref name="DependsOnId"/> closes, even though it is
/// supposed to wait for it.
/// <para>
/// Not a cycle. A cycle is refused when it is proposed and never becomes part of a
/// plan; a contradiction is stored, reported, and left for the person to resolve —
/// discovering that a date does not fit is the point of drawing the plan.
/// </para>
/// </summary>
public sealed record PlanContradiction(Guid NodeId, Guid DependsOnId, string Reason);
