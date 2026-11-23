using Backlog.Modules.Events.Abstractions;
using Backlog.Modules.Events.DomainModels;

namespace Backlog.Modules.Events.UnitTests;

/// <summary>
/// What the pane's state choice offers (<c>.devbook/domain/events/requirements.md#events-pane</c>,
/// "No way back" and "An attended event cannot be skipped"): only the states after the current
/// one, and Skip only from Spotted, Considering or Going.
/// </summary>
public sealed class KnowledgeEventMovesTests
{
    [Fact]
    public void A_spotted_event_offers_every_later_state_and_skip()
    {
        var offer = KnowledgeEventMoves.Offered(KnowledgeEventState.Spotted);

        Assert.Equal(
            [KnowledgeEventState.Considering, KnowledgeEventState.Going, KnowledgeEventState.Attended, KnowledgeEventState.Harvested],
            offer.Forward);
        Assert.True(offer.CanSkip);
    }

    [Fact]
    public void A_going_event_offers_no_earlier_state_but_offers_skip()
    {
        var offer = KnowledgeEventMoves.Offered(KnowledgeEventState.Going);

        Assert.Equal([KnowledgeEventState.Attended, KnowledgeEventState.Harvested], offer.Forward);
        Assert.True(offer.CanSkip);
    }

    [Fact]
    public void An_attended_event_offers_harvested_and_no_skip()
    {
        var offer = KnowledgeEventMoves.Offered(KnowledgeEventState.Attended);

        Assert.Equal([KnowledgeEventState.Harvested], offer.Forward);
        Assert.False(offer.CanSkip);
    }

    [Theory]
    [InlineData(KnowledgeEventState.Harvested)]
    [InlineData(KnowledgeEventState.Skipped)]
    public void A_final_event_offers_nothing(KnowledgeEventState state)
    {
        var offer = KnowledgeEventMoves.Offered(state);

        Assert.Empty(offer.Forward);
        Assert.False(offer.CanSkip);
    }

    /// <summary>The offer is drawn from Abstractions, the rule lives on the aggregate: every
    /// offered move is one the aggregate takes, and every move it takes is offered.</summary>
    [Theory]
    [InlineData(KnowledgeEventState.Spotted)]
    [InlineData(KnowledgeEventState.Considering)]
    [InlineData(KnowledgeEventState.Going)]
    [InlineData(KnowledgeEventState.Attended)]
    [InlineData(KnowledgeEventState.Harvested)]
    [InlineData(KnowledgeEventState.Skipped)]
    public void The_offer_is_exactly_what_the_aggregate_allows(KnowledgeEventState from)
    {
        var offer = KnowledgeEventMoves.Offered(from);

        foreach (var target in Enum.GetValues<KnowledgeEventState>())
        {
            Assert.Equal(offer.Forward.Contains(target), At(from).MoveTo(target).IsSuccess);
        }

        Assert.Equal(offer.CanSkip, At(from).Skip("Clashes with the release").IsSuccess);
    }

    private static KnowledgeEvent At(KnowledgeEventState state) =>
        KnowledgeEvent.Restore(Guid.NewGuid(), "Techorama", null, null, null, null, null, null, state, null, null, [], null, null, [], [], null);
}
