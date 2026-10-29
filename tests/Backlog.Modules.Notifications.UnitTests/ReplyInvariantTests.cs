using Backlog.Modules.Notifications.Abstractions;
using Backlog.Modules.Notifications.DomainModels;

using static Backlog.Modules.Notifications.UnitTests.Drafts;

namespace Backlog.Modules.Notifications.UnitTests;

/// <summary>
/// One test or more per rule of <c>.devbook/domain/notifications/domain.invariants.md#reply</c>, and
/// the four answer shapes of <c>.devbook/domain/notifications/domain.md#answer-spec</c>.
/// </summary>
public sealed class ReplyInvariantTests
{
    private static readonly AnswerChoice[] Journeys =
    [
        new("pair", "Pair a phone"),
        new("capture", "Capture from the phone"),
        new("pace", "Roadmap pace")
    ];

    private static Notification Asking(AnswerSpec spec) => Published(Question(spec));

    // --- A reply fits the answer spec ---------------------------------------------------

    [Fact]
    public void A_single_choice_reply_picks_exactly_one_offered_choice()
    {
        var spec = YesNo();

        Assert.True(ReplyValidation.Validate(spec, ["yes"], null).IsSuccess);
        Assert.Equal("reply.invalid", ReplyValidation.Validate(spec, ["yes", "no"], null).Error.Code);
        Assert.Equal("reply.invalid", ReplyValidation.Validate(spec, ["maybe"], null).Error.Code);
        Assert.Equal("reply.invalid", ReplyValidation.Validate(spec, [], null).Error.Code);
        Assert.Equal("reply.invalid", ReplyValidation.Validate(spec, [], "yes please").Error.Code);
    }

    [Fact]
    public void A_multiple_choice_reply_picks_offered_choices_within_the_limits()
    {
        var spec = new AnswerSpec(Journeys, AllowMultiple: true, MinChoices: 1, MaxChoices: 2);

        Assert.True(ReplyValidation.Validate(spec, ["pair"], null).IsSuccess);
        Assert.True(ReplyValidation.Validate(spec, ["pair", "pace"], null).IsSuccess);
        Assert.Equal("reply.invalid", ReplyValidation.Validate(spec, ["pair", "capture", "pace"], null).Error.Code);
        Assert.Equal("reply.invalid", ReplyValidation.Validate(spec, [], null).Error.Code);
        Assert.Equal("reply.invalid", ReplyValidation.Validate(spec, ["pair", "pair"], null).Error.Code);
        Assert.Equal("reply.invalid", ReplyValidation.Validate(spec, ["pair", "other"], null).Error.Code);
    }

    [Fact]
    public void A_multiple_choice_question_with_no_minimum_takes_an_empty_selection()
    {
        var spec = new AnswerSpec(Journeys, AllowMultiple: true, MinChoices: 0);

        Assert.True(ReplyValidation.Validate(spec, [], null).IsSuccess);
        Assert.True(ReplyValidation.Validate(spec, ["pair", "capture", "pace"], null).IsSuccess);
    }

    [Fact]
    public void An_open_reply_is_plain_text_within_the_length_limit()
    {
        var spec = new AnswerSpec([], Open: new OpenAnswer("Write an answer…", MaxLength: 200));

        Assert.True(ReplyValidation.Validate(spec, [], new string('a', 200)).IsSuccess);

        var tooLong = ReplyValidation.Validate(spec, [], new string('a', 250));
        Assert.Equal("reply.invalid", tooLong.Error.Code);
        Assert.Contains("too long", tooLong.Error.Message, StringComparison.Ordinal);

        Assert.Equal("reply.invalid", ReplyValidation.Validate(spec, ["yes"], null).Error.Code);
        Assert.Equal("reply.invalid", ReplyValidation.Validate(spec, [], "  ").Error.Code);
    }

    [Fact]
    public void An_open_answer_runs_to_2000_characters_when_the_question_names_no_limit()
    {
        var spec = new AnswerSpec([], Open: new OpenAnswer());

        Assert.Equal(2000, spec.Open!.MaxLength);
        Assert.True(ReplyValidation.Validate(spec, [], new string('a', 2000)).IsSuccess);
        Assert.Equal("reply.invalid", ReplyValidation.Validate(spec, [], new string('a', 2001)).Error.Code);
    }

    [Fact]
    public void Choices_with_open_take_one_choice_or_the_open_answer_but_not_both()
    {
        var spec = new AnswerSpec(Journeys, Open: new OpenAnswer());

        Assert.True(ReplyValidation.Validate(spec, ["pair"], null).IsSuccess);
        Assert.True(ReplyValidation.Validate(spec, [], "Signals").IsSuccess);
        Assert.Equal("reply.invalid", ReplyValidation.Validate(spec, ["pair"], "Signals").Error.Code);
    }

    // --- A choice that takes text comes with text ----------------------------------------

    private static AnswerSpec Validation(OpenAnswer? open = null) => new(
        [
            new AnswerChoice("approve", "Approve", AnswerChoiceStyle.Primary),
            new AnswerChoice("revise", "Revise", TakesText: true, Placeholder: "What should change?"),
            new AnswerChoice("reject", "Reject", AnswerChoiceStyle.Destructive)
        ],
        Open: open);

    [Fact]
    public void A_choice_that_takes_text_comes_with_text()
    {
        var spec = Validation();

        Assert.True(ReplyValidation.Validate(spec, ["revise"], "Split the migration in two").IsSuccess);
        Assert.Equal("reply.invalid", ReplyValidation.Validate(spec, ["revise"], null).Error.Code);
        Assert.Equal("reply.invalid", ReplyValidation.Validate(spec, ["revise"], "   ").Error.Code);
    }

    [Fact]
    public void A_choice_that_takes_no_text_is_refused_with_text()
    {
        var spec = Validation();

        Assert.True(ReplyValidation.Validate(spec, ["approve"], null).IsSuccess);
        Assert.Equal("reply.invalid", ReplyValidation.Validate(spec, ["approve"], "Looks good").Error.Code);
    }

    [Fact]
    public void A_choices_text_runs_to_2000_characters_when_the_question_has_no_open_answer()
    {
        var spec = Validation();

        Assert.True(ReplyValidation.Validate(spec, ["revise"], new string('a', 2000)).IsSuccess);
        Assert.Equal("reply.invalid", ReplyValidation.Validate(spec, ["revise"], new string('a', 2001)).Error.Code);
    }

    [Fact]
    public void A_choices_text_keeps_within_the_open_answers_limit()
    {
        var spec = Validation(new OpenAnswer(MaxLength: 200));

        Assert.True(ReplyValidation.Validate(spec, ["revise"], new string('a', 200)).IsSuccess);
        Assert.Equal("reply.invalid", ReplyValidation.Validate(spec, ["revise"], new string('a', 201)).Error.Code);
        Assert.True(ReplyValidation.Validate(spec, [], "Signals").IsSuccess);
        Assert.Equal("reply.invalid", ReplyValidation.Validate(spec, ["approve"], "Signals").Error.Code);
    }

    [Fact]
    public void The_owner_accepts_a_choice_with_its_words()
    {
        var notification = Asking(Validation());

        var answer = notification.Accept(Reply(notification, choices: ["revise"], text: "Split the migration in two"), Owner, Now);

        Assert.True(answer.IsSuccess);
        Assert.Equal(["revise"], answer.Value.ChoiceIds);
        Assert.Equal("Split the migration in two", answer.Value.Text);
    }

    [Fact]
    public void The_owner_refuses_a_choice_that_takes_text_sent_without_it_and_stays_open()
    {
        var notification = Asking(Validation());

        var result = notification.Accept(Reply(notification, choices: ["revise"]), Owner, Now);

        Assert.Equal("reply.invalid", result.Error.Code);
        Assert.Equal(NotificationState.Open, notification.State);
    }

    [Fact]
    public void The_owner_accepts_an_open_answer_instead_of_a_choice()
    {
        var notification = Asking(new AnswerSpec(Journeys, Open: new OpenAnswer()));

        var answer = notification.Accept(Reply(notification, text: "Signals"), Owner, Now);

        Assert.True(answer.IsSuccess);
        Assert.Equal("Signals", answer.Value.Text);
        Assert.Empty(answer.Value.ChoiceIds);
    }

    // --- A reply to a closed question is rejected ---------------------------------------

    [Fact]
    public void A_reply_after_the_question_closed_is_rejected_as_closed()
    {
        var notification = Published();
        notification.Close(Owner, "No longer needed", Now);

        var result = notification.Accept(Reply(notification, choices: ["yes"]), Owner, Now);

        Assert.Equal("reply.closed", result.Error.Code);
        Assert.Null(notification.Answer);
    }

    // --- An owner-only question takes only the owner's reply ----------------------------

    [Fact]
    public void An_owner_only_question_rejects_a_viewers_reply_and_takes_the_owners()
    {
        var notification = Asking(YesNo(AnswerOn.Owner));

        Assert.Equal("reply.owner_only", notification.Accept(Reply(notification, from: Phone, choices: ["yes"]), Owner, Now).Error.Code);
        Assert.Equal(NotificationState.Open, notification.State);

        Assert.True(notification.Accept(Reply(notification, from: Owner, choices: ["yes"]), Owner, Now).IsSuccess);
    }

    [Fact]
    public void Any_paired_viewer_answers_a_critical_question_unless_it_says_owner_only()
    {
        var notification = Published(Question() with { Severity = NotificationSeverity.Critical });

        Assert.True(notification.Accept(Reply(notification, from: OtherDesktop, choices: ["no"]), Owner, Now).IsSuccess);
    }

    // --- A reply comes from the same owner -----------------------------------------------
    // The account half (reply.foreign_owner) needs the account the replica carries, and lands
    // with the sync exchange; the owner instance checks here that the reply is addressed to it.

    [Fact]
    public void A_reply_addressed_to_another_owner_instance_is_rejected()
    {
        var notification = Published();

        var result = notification.Accept(Reply(notification, choices: ["yes"], ownerInstance: OtherDesktop), Owner, Now);

        Assert.Equal("reply.wrong_instance", result.Error.Code);
        Assert.Equal(NotificationState.Open, notification.State);
    }

    [Fact]
    public void A_reply_to_another_notification_is_rejected()
    {
        var notification = Published();
        var other = Published();

        Assert.Equal("reply.invalid", notification.Accept(Reply(other, choices: ["yes"]), Owner, Now).Error.Code);
    }

    // --- The answer spec takes one of four shapes ----------------------------------------

    public static TheoryData<AnswerSpec> Allowed() =>
    [
        YesNo(),
        new AnswerSpec(Journeys, AllowMultiple: true),
        new AnswerSpec([], Open: new OpenAnswer()),
        new AnswerSpec(Journeys, Open: new OpenAnswer("Or suggest one")),
        new AnswerSpec([new AnswerChoice("approve", "Approve"), new AnswerChoice("revise", "Revise", TakesText: true)]),
        new AnswerSpec([new AnswerChoice("approve", "Approve"), new AnswerChoice("revise", "Revise", TakesText: true)], Open: new OpenAnswer())
    ];

    [Theory]
    [MemberData(nameof(Allowed))]
    public void A_question_offers_one_of_four_answer_shapes(AnswerSpec spec) =>
        Assert.True(AnswerSpecRules.Validate(spec).IsSuccess);

    public static TheoryData<AnswerSpec> Refused() =>
    [
        new AnswerSpec([]),
        new AnswerSpec([new AnswerChoice("yes", "Yes")]),
        new AnswerSpec([.. Enumerable.Range(1, 7).Select(n => new AnswerChoice($"c{n}", $"Choice {n}"))]),
        new AnswerSpec([new AnswerChoice("a", "A"), new AnswerChoice("a", "Again")]),
        new AnswerSpec([new AnswerChoice("a", "A"), new AnswerChoice("b", " ")]),
        new AnswerSpec(Journeys, AllowMultiple: true, Open: new OpenAnswer()),
        new AnswerSpec(Journeys, MinChoices: 1),
        new AnswerSpec(Journeys, AllowMultiple: true, MinChoices: 3, MaxChoices: 2),
        new AnswerSpec(Journeys, AllowMultiple: true, MaxChoices: 4),
        new AnswerSpec([], AllowMultiple: true, Open: new OpenAnswer()),
        new AnswerSpec([], Open: new OpenAnswer(MaxLength: 0)),
        new AnswerSpec([new AnswerChoice("a", "A"), new AnswerChoice("b", "B", TakesText: true)], AllowMultiple: true)
    ];

    [Theory]
    [MemberData(nameof(Refused))]
    public void Any_other_answer_spec_is_refused(AnswerSpec spec)
    {
        Assert.Equal("notification.invalid_answer_spec", AnswerSpecRules.Validate(spec).Error.Code);
        Assert.Equal("notification.invalid_answer_spec", Notification.Publish(Guid.NewGuid(), Owner, Question(spec), Now).Error.Code);
    }
}
