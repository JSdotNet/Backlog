using Backlog.Modules.Notifications.Abstractions;
using Backlog.Modules.Notifications.DomainModels;
using Backlog.SharedKernel.Results;

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

        Assert.IsType<Ok>(ReplyValidation.Validate(spec, ["yes"], null).Value);
        Assert.Equal("reply.invalid", Assert.IsType<Error>(ReplyValidation.Validate(spec, ["yes", "no"], null).Value).Code);
        Assert.Equal("reply.invalid", Assert.IsType<Error>(ReplyValidation.Validate(spec, ["maybe"], null).Value).Code);
        Assert.Equal("reply.invalid", Assert.IsType<Error>(ReplyValidation.Validate(spec, [], null).Value).Code);
        Assert.Equal("reply.invalid", Assert.IsType<Error>(ReplyValidation.Validate(spec, [], "yes please").Value).Code);
    }

    [Fact]
    public void A_multiple_choice_reply_picks_offered_choices_within_the_limits()
    {
        var spec = new AnswerSpec(Journeys, AllowMultiple: true, MinChoices: 1, MaxChoices: 2);

        Assert.IsType<Ok>(ReplyValidation.Validate(spec, ["pair"], null).Value);
        Assert.IsType<Ok>(ReplyValidation.Validate(spec, ["pair", "pace"], null).Value);
        Assert.Equal("reply.invalid", Assert.IsType<Error>(ReplyValidation.Validate(spec, ["pair", "capture", "pace"], null).Value).Code);
        Assert.Equal("reply.invalid", Assert.IsType<Error>(ReplyValidation.Validate(spec, [], null).Value).Code);
        Assert.Equal("reply.invalid", Assert.IsType<Error>(ReplyValidation.Validate(spec, ["pair", "pair"], null).Value).Code);
        Assert.Equal("reply.invalid", Assert.IsType<Error>(ReplyValidation.Validate(spec, ["pair", "other"], null).Value).Code);
    }

    [Fact]
    public void A_multiple_choice_question_with_no_minimum_takes_an_empty_selection()
    {
        var spec = new AnswerSpec(Journeys, AllowMultiple: true, MinChoices: 0);

        Assert.IsType<Ok>(ReplyValidation.Validate(spec, [], null).Value);
        Assert.IsType<Ok>(ReplyValidation.Validate(spec, ["pair", "capture", "pace"], null).Value);
    }

    [Fact]
    public void An_open_reply_is_plain_text_within_the_length_limit()
    {
        var spec = new AnswerSpec([], Open: new OpenAnswer("Write an answer…", MaxLength: 200));

        Assert.IsType<Ok>(ReplyValidation.Validate(spec, [], new string('a', 200)).Value);

        var tooLong = Assert.IsType<Error>(ReplyValidation.Validate(spec, [], new string('a', 250)).Value);
        Assert.Equal("reply.invalid", tooLong.Code);
        Assert.Contains("too long", tooLong.Message, StringComparison.Ordinal);

        Assert.Equal("reply.invalid", Assert.IsType<Error>(ReplyValidation.Validate(spec, ["yes"], null).Value).Code);
        Assert.Equal("reply.invalid", Assert.IsType<Error>(ReplyValidation.Validate(spec, [], "  ").Value).Code);
    }

    [Fact]
    public void An_open_answer_runs_to_2000_characters_when_the_question_names_no_limit()
    {
        var spec = new AnswerSpec([], Open: new OpenAnswer());

        Assert.Equal(2000, spec.Open!.MaxLength);
        Assert.IsType<Ok>(ReplyValidation.Validate(spec, [], new string('a', 2000)).Value);
        Assert.Equal("reply.invalid", Assert.IsType<Error>(ReplyValidation.Validate(spec, [], new string('a', 2001)).Value).Code);
    }

    [Fact]
    public void Choices_with_open_take_one_choice_or_the_open_answer_but_not_both()
    {
        var spec = new AnswerSpec(Journeys, Open: new OpenAnswer());

        Assert.IsType<Ok>(ReplyValidation.Validate(spec, ["pair"], null).Value);
        Assert.IsType<Ok>(ReplyValidation.Validate(spec, [], "Signals").Value);
        Assert.Equal("reply.invalid", Assert.IsType<Error>(ReplyValidation.Validate(spec, ["pair"], "Signals").Value).Code);
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

        Assert.IsType<Ok>(ReplyValidation.Validate(spec, ["revise"], "Split the migration in two").Value);
        Assert.Equal("reply.invalid", Assert.IsType<Error>(ReplyValidation.Validate(spec, ["revise"], null).Value).Code);
        Assert.Equal("reply.invalid", Assert.IsType<Error>(ReplyValidation.Validate(spec, ["revise"], "   ").Value).Code);
    }

    [Fact]
    public void A_choice_that_takes_no_text_is_refused_with_text()
    {
        var spec = Validation();

        Assert.IsType<Ok>(ReplyValidation.Validate(spec, ["approve"], null).Value);
        Assert.Equal("reply.invalid", Assert.IsType<Error>(ReplyValidation.Validate(spec, ["approve"], "Looks good").Value).Code);
    }

    [Fact]
    public void A_choices_text_runs_to_2000_characters_when_the_question_has_no_open_answer()
    {
        var spec = Validation();

        Assert.IsType<Ok>(ReplyValidation.Validate(spec, ["revise"], new string('a', 2000)).Value);
        Assert.Equal("reply.invalid", Assert.IsType<Error>(ReplyValidation.Validate(spec, ["revise"], new string('a', 2001)).Value).Code);
    }

    [Fact]
    public void A_choices_text_keeps_within_the_open_answers_limit()
    {
        var spec = Validation(new OpenAnswer(MaxLength: 200));

        Assert.IsType<Ok>(ReplyValidation.Validate(spec, ["revise"], new string('a', 200)).Value);
        Assert.Equal("reply.invalid", Assert.IsType<Error>(ReplyValidation.Validate(spec, ["revise"], new string('a', 201)).Value).Code);
        Assert.IsType<Ok>(ReplyValidation.Validate(spec, [], "Signals").Value);
        Assert.Equal("reply.invalid", Assert.IsType<Error>(ReplyValidation.Validate(spec, ["approve"], "Signals").Value).Code);
    }

    [Fact]
    public void The_owner_accepts_a_choice_with_its_words()
    {
        var notification = Asking(Validation());

        var answer = Assert.IsType<Ok<NotificationAnswer>>(notification.Accept(Reply(notification, choices: ["revise"], text: "Split the migration in two"), Owner, Now).Value).Value;

        Assert.Equal(["revise"], answer.ChoiceIds);
        Assert.Equal("Split the migration in two", answer.Text);
    }

    [Fact]
    public void The_owner_refuses_a_choice_that_takes_text_sent_without_it_and_stays_open()
    {
        var notification = Asking(Validation());

        var result = Assert.IsType<Error>(notification.Accept(Reply(notification, choices: ["revise"]), Owner, Now).Value);

        Assert.Equal("reply.invalid", result.Code);
        Assert.Equal(NotificationState.Open, notification.State);
    }

    [Fact]
    public void The_owner_accepts_an_open_answer_instead_of_a_choice()
    {
        var notification = Asking(new AnswerSpec(Journeys, Open: new OpenAnswer()));

        var answer = Assert.IsType<Ok<NotificationAnswer>>(notification.Accept(Reply(notification, text: "Signals"), Owner, Now).Value).Value;

        Assert.Equal("Signals", answer.Text);
        Assert.Empty(answer.ChoiceIds);
    }

    [Fact]
    public void A_note_goes_beside_a_choice_or_an_open_answer_within_its_limit()
    {
        var spec = new AnswerSpec(Journeys, Open: new OpenAnswer(), Note: new OpenAnswer("Add a note", MaxLength: 40));

        Assert.IsType<Ok>(ReplyValidation.Validate(spec, ["pair"], null, "Only on Wi-Fi").Value);
        Assert.IsType<Ok>(ReplyValidation.Validate(spec, [], "Signals", "Rename later").Value);
        Assert.IsType<Ok>(ReplyValidation.Validate(spec, ["pair"], null, new string('n', 40)).Value);

        var tooLong = Assert.IsType<Error>(ReplyValidation.Validate(spec, ["pair"], null, new string('n', 41)).Value);
        Assert.Equal("reply.invalid", tooLong.Code);
        Assert.Contains("note", tooLong.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_note_alone_is_no_answer()
    {
        var spec = new AnswerSpec(Journeys, Note: new OpenAnswer());

        Assert.Equal("reply.invalid", Assert.IsType<Error>(ReplyValidation.Validate(spec, [], null, "Just a note").Value).Code);
    }

    [Fact]
    public void A_question_without_a_note_takes_no_note()
    {
        var spec = new AnswerSpec(Journeys);

        Assert.Equal("reply.invalid", Assert.IsType<Error>(ReplyValidation.Validate(spec, ["pair"], null, "A note").Value).Code);
        Assert.IsType<Ok>(ReplyValidation.Validate(spec, ["pair"], null, "   ").Value);
    }

    [Fact]
    public void The_owner_keeps_the_note_with_the_accepted_answer()
    {
        var notification = Asking(new AnswerSpec(Journeys, Note: new OpenAnswer(MaxLength: 4000)));

        var answer = Assert.IsType<Ok<NotificationAnswer>>(notification.Accept(Reply(notification, choices: ["pace"], note: "  Only for Q4  "), Owner, Now).Value).Value;

        Assert.Equal(["pace"], answer.ChoiceIds);
        Assert.Equal("Only for Q4", answer.Note);
        Assert.Equal("Only for Q4", notification.Answer!.Note);
    }

    [Fact]
    public void A_choices_words_and_a_note_travel_apart()
    {
        var spec = Validation() with { Note = new OpenAnswer(MaxLength: 40) };

        Assert.IsType<Ok>(ReplyValidation.Validate(spec, ["revise"], "Split the migration in two", "Only on Wi-Fi").Value);
        Assert.Equal("reply.invalid", Assert.IsType<Error>(ReplyValidation.Validate(spec, ["revise"], null, "Split the migration in two").Value).Code);
        Assert.Equal("reply.invalid", Assert.IsType<Error>(ReplyValidation.Validate(spec, ["approve"], "Looks good", "Only on Wi-Fi").Value).Code);
    }

    // --- A reply to a closed question is rejected ---------------------------------------

    [Fact]
    public void A_reply_after_the_question_closed_is_rejected_as_closed()
    {
        var notification = Published();
        notification.Close(Owner, "No longer needed", Now);

        var result = Assert.IsType<Error>(notification.Accept(Reply(notification, choices: ["yes"]), Owner, Now).Value);

        Assert.Equal("reply.closed", result.Code);
        Assert.Null(notification.Answer);
    }

    // --- An owner-only question takes only the owner's reply ----------------------------

    [Fact]
    public void An_owner_only_question_rejects_a_viewers_reply_and_takes_the_owners()
    {
        var notification = Asking(YesNo(AnswerOn.Owner));

        Assert.Equal("reply.owner_only", Assert.IsType<Error>(notification.Accept(Reply(notification, from: Phone, choices: ["yes"]), Owner, Now).Value).Code);
        Assert.Equal(NotificationState.Open, notification.State);

        Assert.IsType<Ok<NotificationAnswer>>(notification.Accept(Reply(notification, from: Owner, choices: ["yes"]), Owner, Now).Value);
    }

    [Fact]
    public void Any_paired_viewer_answers_a_critical_question_unless_it_says_owner_only()
    {
        var notification = Published(Question() with { Severity = NotificationSeverity.Critical });

        Assert.IsType<Ok<NotificationAnswer>>(notification.Accept(Reply(notification, from: OtherDesktop, choices: ["no"]), Owner, Now).Value);
    }

    // --- A reply comes from the same owner -----------------------------------------------
    // The account half (reply.foreign_owner) needs the account the replica carries, and lands
    // with the sync exchange; the owner instance checks here that the reply is addressed to it.

    [Fact]
    public void A_reply_addressed_to_another_owner_instance_is_rejected()
    {
        var notification = Published();

        var result = Assert.IsType<Error>(notification.Accept(Reply(notification, choices: ["yes"], ownerInstance: OtherDesktop), Owner, Now).Value);

        Assert.Equal("reply.wrong_instance", result.Code);
        Assert.Equal(NotificationState.Open, notification.State);
    }

    [Fact]
    public void A_reply_to_another_notification_is_rejected()
    {
        var notification = Published();
        var other = Published();

        Assert.Equal("reply.invalid", Assert.IsType<Error>(notification.Accept(Reply(other, choices: ["yes"]), Owner, Now).Value).Code);
    }

    // --- The answer spec takes one of four shapes ----------------------------------------

    public static TheoryData<AnswerSpec> Allowed() =>
    [
        YesNo(),
        new AnswerSpec(Journeys, AllowMultiple: true),
        new AnswerSpec([], Open: new OpenAnswer()),
        new AnswerSpec(Journeys, Open: new OpenAnswer("Or suggest one")),
        new AnswerSpec([new AnswerChoice("approve", "Approve"), new AnswerChoice("revise", "Revise", TakesText: true)]),
        new AnswerSpec([new AnswerChoice("approve", "Approve"), new AnswerChoice("revise", "Revise", TakesText: true)], Open: new OpenAnswer()),
        new AnswerSpec(Choices(8)),
        new AnswerSpec(Choices(8), Open: new OpenAnswer(MaxLength: 4000), Note: new OpenAnswer("Add a note", MaxLength: 4000)),
        new AnswerSpec(Journeys, AllowMultiple: true, Note: new OpenAnswer()),
        new AnswerSpec([], Open: new OpenAnswer(), Note: new OpenAnswer()),
        new AnswerSpec([new AnswerChoice("approve", "Approve"), new AnswerChoice("revise", "Revise", TakesText: true), new AnswerChoice("dismiss", "Dismiss", Quiet: true)])
    ];

    private static AnswerChoice[] Choices(int count) =>
        [.. Enumerable.Range(1, count).Select(n => new AnswerChoice($"c{n}", $"Choice {n}"))];

    [Fact]
    public void A_question_offers_two_to_eight_set_choices()
    {
        Assert.Equal(8, AnswerSpecRules.MaximumChoices);
        Assert.IsType<Ok>(AnswerSpecRules.Validate(new AnswerSpec(Choices(8))).Value);
        Assert.Equal("notification.invalid_answer_spec", Assert.IsType<Error>(AnswerSpecRules.Validate(new AnswerSpec(Choices(9))).Value).Code);
    }

    [Theory]
    [MemberData(nameof(Allowed))]
    public void A_question_offers_one_of_four_answer_shapes(AnswerSpec spec) =>
        Assert.IsType<Ok>(AnswerSpecRules.Validate(spec).Value);

    public static TheoryData<AnswerSpec> Refused() =>
    [
        new AnswerSpec([]),
        new AnswerSpec([new AnswerChoice("yes", "Yes")]),
        new AnswerSpec(Choices(9)),
        new AnswerSpec(Journeys, Note: new OpenAnswer(MaxLength: 0)),
        new AnswerSpec([new AnswerChoice("a", "A"), new AnswerChoice("a", "Again")]),
        new AnswerSpec([new AnswerChoice("a", "A"), new AnswerChoice("b", " ")]),
        new AnswerSpec(Journeys, AllowMultiple: true, Open: new OpenAnswer()),
        new AnswerSpec(Journeys, MinChoices: 1),
        new AnswerSpec(Journeys, AllowMultiple: true, MinChoices: 3, MaxChoices: 2),
        new AnswerSpec(Journeys, AllowMultiple: true, MaxChoices: 4),
        new AnswerSpec([], AllowMultiple: true, Open: new OpenAnswer()),
        new AnswerSpec([], Open: new OpenAnswer(MaxLength: 0)),
        new AnswerSpec([new AnswerChoice("a", "A"), new AnswerChoice("b", "B", TakesText: true)], AllowMultiple: true),
        new AnswerSpec([new AnswerChoice("a", "A"), new AnswerChoice("b", "B", TakesText: true, Quiet: true)]),
        new AnswerSpec([new AnswerChoice("a", "A"), new AnswerChoice("b", "B"), new AnswerChoice("c", "C", Quiet: true)], AllowMultiple: true)
    ];

    [Theory]
    [MemberData(nameof(Refused))]
    public void Any_other_answer_spec_is_refused(AnswerSpec spec)
    {
        Assert.Equal("notification.invalid_answer_spec", Assert.IsType<Error>(AnswerSpecRules.Validate(spec).Value).Code);
        Assert.Equal("notification.invalid_answer_spec", Assert.IsType<Error>(Notification.Publish(Guid.NewGuid(), Owner, Question(spec), Now).Value).Code);
    }
}
