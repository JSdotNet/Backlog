using Backlog.Infrastructure.Sync.Notifications;
using Backlog.Mobile.UI.Notifications;
using Backlog.Mobile.UI.Outbox;
using Backlog.Modules.Notifications.Abstractions;
using Backlog.Modules.Sync.Abstractions.DataTransferObjects;

namespace Backlog.Mobile.UI.UnitTests;

/// <summary>
/// Where a notification stands for the phone (<c>.devbook/domain/notifications/features.md#answering-from-another-device</c>):
/// the owner's state read against the replies this phone made — what each card state draws.
/// </summary>
public sealed class NotificationStandingTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 21, 9, 41, 0, TimeSpan.Zero);
    private static readonly Guid Phone = Guid.Parse("e0000000-0000-4000-8000-0000000000ee");

    private static readonly NotificationSnapshot Question = TestNotifications.Question("Run the migration against staging?", Now);

    private static NotificationReplyPost Reply(string choice = "yes") =>
        new(Guid.CreateVersion7(), Question.Id, TestNotifications.Owner, [choice], null, Now.AddMinutes(1));

    private static (NotificationStanding Standing, string? Verdict) Of(
        NotificationSnapshot notification,
        IReadOnlyList<NotificationReplyPost>? mine = null,
        IReadOnlyList<NotificationReplyRejection>? rejected = null,
        Func<Guid, OutboxEntry?>? queued = null) =>
        NotificationStandings.Of(new NotificationPayload(notification, rejected ?? [], "WORKSTATION-01"), Phone, mine ?? [], queued ?? (_ => null));

    private static OutboxEntry Queued(NotificationReplyPost reply, bool refused = false) =>
        new(reply.Id, ReplyOutboxKind.Token, ReplyOutboxKind.Write(reply), 0, refused ? "A reply picks at most 6 choices." : null, reply.RepliedAt, refused);

    [Fact]
    public void An_open_question_nobody_here_answered_is_answerable() =>
        Assert.Equal(NotificationStanding.Answerable, Of(Question).Standing);

    [Fact]
    public void A_question_that_says_answer_on_owner_is_read_only_here() =>
        Assert.Equal(
            NotificationStanding.OwnerOnly,
            Of(Question with { AnswerSpec = TestNotifications.YesNo with { AnswerOn = AnswerOn.Owner } }).Standing);

    [Fact]
    public void An_open_alert_has_nothing_to_answer() =>
        Assert.Equal(NotificationStanding.Alert, Of(TestNotifications.Alert("Remark left", Now)).Standing);

    [Fact]
    public void A_reply_still_in_the_outbox_is_sending()
    {
        var reply = Reply();

        Assert.Equal(NotificationStanding.Sending, Of(Question, [reply], queued: id => id == reply.Id ? Queued(reply) : null).Standing);
    }

    [Fact]
    public void A_reply_cloud_sync_refused_lets_the_phone_answer_again()
    {
        var reply = Reply();

        var (standing, verdict) = Of(Question, [reply], queued: id => id == reply.Id ? Queued(reply, refused: true) : null);

        Assert.Equal(NotificationStanding.Rejected, standing);
        Assert.Equal("A reply picks at most 6 choices.", verdict);
    }

    [Fact]
    public void A_reply_that_left_the_outbox_waits_for_the_owner() =>
        Assert.Equal(NotificationStanding.AwaitingOwner, Of(Question, [Reply()]).Standing);

    [Fact]
    public void A_rejection_of_the_last_reply_made_here_carries_the_owners_reason()
    {
        var reply = Reply();

        var (standing, verdict) = Of(Question, [reply], [TestNotifications.Rejected(reply.Id, Phone, "Pick one of the choices.", Now.AddMinutes(2))]);

        Assert.Equal(NotificationStanding.Rejected, standing);
        Assert.Equal("Pick one of the choices.", verdict);
    }

    [Fact]
    public void A_rejection_of_an_earlier_reply_does_not_stand_for_the_newer_one()
    {
        var first = Reply("maybe");
        var second = Reply();

        var standing = Of(Question, [first, second], [TestNotifications.Rejected(first.Id, Phone, "Pick one of the choices.", Now)]).Standing;

        Assert.Equal(NotificationStanding.AwaitingOwner, standing);
    }

    [Fact]
    public void The_owner_accepting_a_reply_made_here_is_answered_from_this_phone()
    {
        var reply = Reply();

        var (standing, verdict) = Of(TestNotifications.Answered(Question, reply.Id, Guid.NewGuid(), Now.AddMinutes(3), "yes"), [reply]);

        Assert.Equal(NotificationStanding.AnsweredHere, standing);
        Assert.Equal("Yes, run it", verdict);
    }

    [Fact]
    public void An_answer_from_another_device_is_answered_elsewhere_with_the_winning_answer()
    {
        var mine = Reply("yes");

        var (standing, verdict) = Of(TestNotifications.Answered(Question, Guid.NewGuid(), Guid.NewGuid(), Now.AddMinutes(3), "no"), [mine]);

        Assert.Equal(NotificationStanding.AnsweredElsewhere, standing);
        Assert.Equal("Not now", verdict);
    }

    [Fact]
    public void A_closed_notification_says_why() =>
        Assert.Equal(
            (NotificationStanding.Closed, "Session archived"),
            Of(Question with { State = NotificationState.Closed, CloseReason = "Session archived", SettledAt = Now }));

    [Fact]
    public void An_open_answer_reads_as_the_text_in_quotes() =>
        Assert.Equal("“Signals”", NotificationStandings.AnswerText(TestNotifications.ReleaseNote, [], " Signals "));

    [Theory]
    [InlineData(new[] { "yes" }, null, true)]
    [InlineData(new[] { "yes", "no" }, null, false)]
    [InlineData(new[] { "maybe" }, null, false)]
    [InlineData(new string[0], "free text", false)]
    public void A_single_choice_reply_fits_one_set_answer(string[] choices, string? text, bool fits) =>
        Assert.Equal(fits, NotificationStandings.Fits(TestNotifications.YesNo, choices, text));

    [Theory]
    [InlineData(new string[0], false)]
    [InlineData(new[] { "pair" }, true)]
    [InlineData(new[] { "pair", "sync" }, true)]
    [InlineData(new[] { "pair", "capture", "sync" }, false)]
    public void A_multiple_choice_reply_fits_within_its_bounds(string[] choices, bool fits) =>
        Assert.Equal(fits, NotificationStandings.Fits(TestNotifications.Journeys, choices, null));

    [Fact]
    public void A_multiple_choice_question_with_no_minimum_takes_an_empty_reply() =>
        Assert.True(NotificationStandings.Fits(TestNotifications.Journeys with { MinChoices = 0 }, [], null));

    // The owner's ReplyValidation, case for case: a choice that takes text comes with
    // non-empty text within the open answer's limit, or 2000 characters without one; a choice
    // that takes none comes without text; open text alone still fits where the spec has it.
    [Theory]
    [InlineData(new[] { "revise" }, "Split the migration in two", false, true)]
    [InlineData(new[] { "revise" }, null, false, false)]
    [InlineData(new[] { "revise" }, "   ", false, false)]
    [InlineData(new[] { "approve" }, null, false, true)]
    [InlineData(new[] { "approve" }, "Looks good", false, false)]
    [InlineData(new string[0], "Looks good", false, false)]
    [InlineData(new string[0], "Looks good", true, true)]
    [InlineData(new[] { "approve" }, "Looks good", true, false)]
    public void A_choice_that_takes_text_fits_only_with_its_text(string[] choices, string? text, bool withOpen, bool fits) =>
        Assert.Equal(fits, NotificationStandings.Fits(TestNotifications.Validation(withOpen ? new OpenAnswer(MaxLength: 200) : null), choices, text));

    [Fact]
    public void A_choices_text_keeps_within_the_open_answers_limit_or_2000()
    {
        Assert.True(NotificationStandings.Fits(TestNotifications.Validation(), ["revise"], new string('x', 2000)));
        Assert.False(NotificationStandings.Fits(TestNotifications.Validation(), ["revise"], new string('x', 2001)));
        Assert.True(NotificationStandings.Fits(TestNotifications.Validation(new OpenAnswer(MaxLength: 200)), ["revise"], new string('x', 200)));
        Assert.False(NotificationStandings.Fits(TestNotifications.Validation(new OpenAnswer(MaxLength: 200)), ["revise"], new string('x', 201)));
    }

    [Fact]
    public void An_open_reply_fits_up_to_its_limit()
    {
        Assert.True(NotificationStandings.Fits(TestNotifications.ReleaseNote, [], "Ship it"));
        Assert.False(NotificationStandings.Fits(TestNotifications.ReleaseNote, [], new string('x', 201)));
        Assert.False(NotificationStandings.Fits(TestNotifications.ReleaseNote, [], "   "));
    }
}
