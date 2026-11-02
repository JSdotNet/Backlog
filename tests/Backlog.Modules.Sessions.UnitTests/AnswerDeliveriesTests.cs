using Backlog.Modules.Sessions.Abstractions;

namespace Backlog.Modules.Sessions.UnitTests;

/// <summary>
/// An answer delivery's claim (local ADR 0041, Amends;
/// <c>.devbook/domain/sessions/domain.invariants.md#invariant-only-one-manager-holds-a-delivery</c>):
/// claimed only while Pending under a two-minute lease, a lapsed lease reading Pending again,
/// a release holding it back from the wait route for one lease, and Delivered final.
/// </summary>
public sealed class AnswerDeliveriesTests
{
    private static readonly DateTimeOffset Noon = new(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);

    private static WaitingQuestion Pending(string session = "session-1", string question = "Which database?", string answer = "Postgres", DateTimeOffset? answeredAt = null)
    {
        var asked = WaitingQuestions.Ask(session, null, [RelayQuestion.Prose(question)], isAway: true, TimeSpan.Zero, Noon.AddMinutes(-30));
        var notified = WaitingQuestions.Notify(asked, [Guid.NewGuid()]).Question!;

        return WaitingQuestions.Answer(notified, notified.NotificationIds[0], RelayAnswer.Open(answer), answeredAt ?? Noon).Question!;
    }

    [Fact]
    public void An_answer_the_hook_no_longer_waits_for_is_a_pending_delivery_due_at_once()
    {
        var delivery = Pending();

        Assert.Equal(AnswerDeliveryState.Pending, delivery.DeliveryAt(Noon));
        Assert.True(delivery.DeliveryIsDueAt(Noon));
        Assert.Null(delivery.DeliveryClaimedBy);
    }

    [Fact]
    public void A_claim_takes_a_pending_delivery_under_a_two_minute_lease()
    {
        var claimed = WaitingQuestions.ClaimDelivery(Pending(), " manager-1 ", Noon);

        Assert.True(claimed.Succeeded);
        Assert.Equal(AnswerDeliveryState.Claimed, claimed.Question!.DeliveryAt(Noon));
        Assert.Equal("manager-1", claimed.Question.DeliveryClaimedBy);
        Assert.Equal(Noon + TimeSpan.FromMinutes(2), claimed.Question.DeliveryLeaseEndsAt);
        Assert.False(claimed.Question.DeliveryIsDueAt(Noon));
    }

    [Fact]
    public void A_second_dispatcher_cannot_claim_a_held_delivery()
    {
        var claimed = WaitingQuestions.ClaimDelivery(Pending(), "manager-1", Noon).Question!;

        var second = WaitingQuestions.ClaimDelivery(claimed, "manager-2", Noon.AddMinutes(1));

        Assert.False(second.Succeeded);
        Assert.Contains("Claimed", second.Refusal, StringComparison.Ordinal);
    }

    [Fact]
    public void A_lapsed_claim_reads_as_pending_again_and_can_be_claimed_by_another()
    {
        var claimed = WaitingQuestions.ClaimDelivery(Pending(), "manager-1", Noon).Question!;
        var lapsed = Noon + WaitingQuestions.DeliveryLease;

        Assert.Equal(AnswerDeliveryState.Pending, claimed.DeliveryAt(lapsed));
        Assert.True(claimed.DeliveryIsDueAt(lapsed));

        var taken = WaitingQuestions.ClaimDelivery(claimed, "manager-2", lapsed);
        var late = WaitingQuestions.MarkDelivered(taken.Question!, "manager-1", lapsed);

        Assert.True(taken.Succeeded);
        Assert.Equal("manager-2", taken.Question!.DeliveryClaimedBy);
        Assert.False(late.Succeeded);
        Assert.Equal("Another dispatcher holds the claim.", late.Refusal);
    }

    /// <summary>The answer was sent: refusing the report only because the lease ran out
    /// would have the route offer it again and the session receive it twice.</summary>
    [Fact]
    public void A_lapsed_claim_nobody_took_since_is_still_marked_delivered_by_its_claimant()
    {
        var claimed = WaitingQuestions.ClaimDelivery(Pending(), "manager-1", Noon).Question!;
        var lapsed = Noon + WaitingQuestions.DeliveryLease + TimeSpan.FromSeconds(5);

        var late = WaitingQuestions.MarkDelivered(claimed, "manager-1", lapsed);
        var release = WaitingQuestions.ReleaseDelivery(claimed, "manager-1", lapsed);

        Assert.True(late.Succeeded);
        Assert.Equal(AnswerDeliveryState.Delivered, late.Question!.Delivery);
        Assert.False(release.Succeeded);
        Assert.Contains("lease has run out", release.Refusal, StringComparison.Ordinal);
    }

    [Fact]
    public void Delivered_is_final()
    {
        var claimed = WaitingQuestions.ClaimDelivery(Pending(), "manager-1", Noon).Question!;
        var delivered = WaitingQuestions.MarkDelivered(claimed, "manager-1", Noon.AddSeconds(10)).Question!;

        Assert.Equal(AnswerDeliveryState.Delivered, delivered.DeliveryAt(Noon.AddHours(1)));
        Assert.False(delivered.DeliveryIsDueAt(Noon.AddHours(1)));
        Assert.False(WaitingQuestions.ClaimDelivery(delivered, "manager-1", Noon.AddHours(1)).Succeeded);
        Assert.False(WaitingQuestions.ReleaseDelivery(delivered, "manager-1", Noon.AddHours(1)).Succeeded);
    }

    [Fact]
    public void Only_the_holder_marks_a_delivery()
    {
        var claimed = WaitingQuestions.ClaimDelivery(Pending(), "manager-1", Noon).Question!;

        var other = WaitingQuestions.MarkDelivered(claimed, "manager-2", Noon.AddSeconds(5));

        Assert.False(other.Succeeded);
        Assert.Equal("Another dispatcher holds the claim.", other.Refusal);
    }

    [Fact]
    public void A_released_delivery_is_pending_and_held_back_from_the_route_for_one_lease()
    {
        var claimed = WaitingQuestions.ClaimDelivery(Pending(), "manager-1", Noon).Question!;
        var released = WaitingQuestions.ReleaseDelivery(claimed, "manager-1", Noon.AddSeconds(30)).Question!;

        Assert.Equal(AnswerDeliveryState.Pending, released.DeliveryAt(Noon.AddSeconds(30)));
        Assert.Null(released.DeliveryClaimedBy);
        Assert.False(released.DeliveryIsDueAt(Noon.AddSeconds(31)));
        Assert.True(released.DeliveryIsDueAt(Noon.AddSeconds(30) + WaitingQuestions.DeliveryLease));

        var early = WaitingQuestions.ClaimDelivery(released, "manager-1", Noon.AddSeconds(31));

        Assert.False(early.Succeeded);
        Assert.Contains("held back", early.Refusal, StringComparison.Ordinal);
        Assert.True(WaitingQuestions.ClaimDelivery(released, "manager-1", Noon.AddSeconds(30) + WaitingQuestions.DeliveryLease).Succeeded);
    }

    [Fact]
    public void A_question_without_a_delivery_cannot_be_claimed()
    {
        var open = WaitingQuestions.Ask("session-1", null, [RelayQuestion.Prose("A?")], false, TimeSpan.FromMinutes(15), Noon);

        var claim = WaitingQuestions.ClaimDelivery(open, "manager-1", Noon);

        Assert.False(claim.Succeeded);
        Assert.Equal("The question has no delivery.", claim.Refusal);
    }

    [Fact]
    public void Due_deliveries_come_oldest_answer_first_and_leave_out_held_and_delivered_ones()
    {
        var newer = Pending("s-2", answeredAt: Noon.AddMinutes(5));
        var older = Pending("s-1", answeredAt: Noon);
        var held = WaitingQuestions.ClaimDelivery(Pending("s-3", answeredAt: Noon.AddMinutes(-5)), "m", Noon.AddMinutes(5)).Question!;
        var delivered = WaitingQuestions.MarkDelivered(
            WaitingQuestions.ClaimDelivery(Pending("s-4", answeredAt: Noon.AddMinutes(-10)), "m", Noon.AddMinutes(5)).Question!, "m", Noon.AddMinutes(5)).Question!;

        var due = WaitingQuestions.DueDeliveries([newer, held, delivered, older], Noon.AddMinutes(6));

        Assert.Equal([older.Id, newer.Id], due.Select(question => question.Id));
    }

    [Fact]
    public void A_delivery_carries_the_stored_question_and_the_accepted_answer()
    {
        var (question, answer) = WaitingQuestions.DeliveryText(Pending(question: " Which database? ", answer: "Postgres"));

        Assert.Equal("Which database?", question);
        Assert.Equal("Postgres", answer);
    }

    [Fact]
    public void A_dialog_of_several_questions_is_delivered_numbered_one_per_line()
    {
        var pick = new RelayQuestion("Which database?", "Database", [new RelayOption("SQLite"), new RelayOption("Postgres")]);
        var asked = WaitingQuestions.Ask("session-1", "toolu_1", [pick, RelayQuestion.Prose("Why?")], true, TimeSpan.Zero, Noon);
        var notified = WaitingQuestions.Notify(asked, [Guid.NewGuid(), Guid.NewGuid()]).Question!;
        var first = WaitingQuestions.Answer(notified, notified.NotificationIds[0], new RelayAnswer(["Postgres"]), Noon).Question!;
        var both = WaitingQuestions.Answer(first, first.NotificationIds[1], RelayAnswer.Open("Scale"), Noon).Question!;

        var (question, answer) = WaitingQuestions.DeliveryText(both);

        Assert.Equal("1. Which database?\n2. Why?", question);
        Assert.Equal("1. Postgres\n2. Scale", answer);
    }
}
