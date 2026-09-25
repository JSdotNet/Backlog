namespace Backlog.Modules.Roadmap.Abstractions.DataTransferObjects;

/// <summary>
/// What re-lengthening an item from its tasks would do to its window, asked before a
/// person is offered the action.
/// <para>
/// Only ever handed out for an item whose window is still sized by effort and whose
/// tasks now make a different one — the rule an import applies (ADR 0013, ruling 5),
/// shown rather than applied. The start never moves, so only the end is proposed.
/// </para>
/// </summary>
/// <param name="ItemId">The item it concerns.</param>
/// <param name="Start">The window's first day, inclusive. Kept as it is.</param>
/// <param name="CurrentEnd">The last day the window has now, inclusive.</param>
/// <param name="ProposedEnd">The last day the gathered effort makes it, inclusive.</param>
/// <param name="GatheredEffort">The story points the proposal was worked out from.</param>
public sealed record RoadmapRelengthProposalDto(
    Guid ItemId,
    DateOnly Start,
    DateOnly CurrentEnd,
    DateOnly ProposedEnd,
    int GatheredEffort);

/// <summary>
/// What re-lengthening an item from its tasks did: the item as it now stands, the
/// window it replaced, and the work that now overlaps it.
/// </summary>
/// <param name="Item">The item, re-lengthened — still placed by effort.</param>
/// <param name="PreviousStart">The replaced window's first day. The same as the new
/// one: the start is kept.</param>
/// <param name="PreviousEnd">The replaced window's last day, inclusive.</param>
/// <param name="NowOverlapping">The nodes waiting on this item that open before it now
/// closes and did not before. Named, never moved: a contradiction is reported, and
/// shifting the dependent work would hide it.</param>
public sealed record RoadmapRelengthResultDto(
    RoadmapItemDto Item,
    DateOnly PreviousStart,
    DateOnly PreviousEnd,
    IReadOnlyList<RoadmapNodeRefDto> NowOverlapping);

/// <summary>An item or a milestone, by the two things a sentence needs to name it.</summary>
public sealed record RoadmapNodeRefDto(Guid Id, string Title);
