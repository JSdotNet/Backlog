namespace Backlog.Modules.Events.Abstractions;

/// <summary>
/// The moves a state choice offers for an event in <c>state</c>
/// (<c>.devbook/domain/events/domain.md#knowledge-event-state</c>): the later states on the path,
/// in order, and whether Skip is offered.
/// </summary>
/// <param name="Forward">The states after the current one, Skipped never among them.</param>
/// <param name="CanSkip">Whether Skip, with a reason, is offered: from Spotted or Considering only.</param>
public sealed record KnowledgeEventMoveOffer(IReadOnlyList<KnowledgeEventState> Forward, bool CanSkip);

/// <summary>
/// The forward path as a screen draws it, so the pane offers only moves the aggregate takes. The
/// aggregate stays the rule; this is the same path read ahead of a move.
/// </summary>
public static class KnowledgeEventMoves
{
    /// <summary>The path Spotted, Considering, Going, Attended, Harvested, in order.</summary>
    public static IReadOnlyList<KnowledgeEventState> Path { get; } =
    [
        KnowledgeEventState.Spotted,
        KnowledgeEventState.Considering,
        KnowledgeEventState.Going,
        KnowledgeEventState.Attended,
        KnowledgeEventState.Harvested
    ];

    /// <summary>What an event in <paramref name="state"/> may move to. Harvested and Skipped
    /// are final and offer nothing.</summary>
    public static KnowledgeEventMoveOffer Offered(KnowledgeEventState state)
    {
        var at = Path.ToList().IndexOf(state);
        if (at < 0) return new KnowledgeEventMoveOffer([], CanSkip: false);

        return new KnowledgeEventMoveOffer(
            [.. Path.Skip(at + 1)],
            CanSkip: state is KnowledgeEventState.Spotted or KnowledgeEventState.Considering);
    }
}
