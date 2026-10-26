using Backlog.Mobile.UI.Notifications;
using Backlog.Modules.Notifications.Abstractions;

namespace Backlog.Mobile.UI.UnitTests;

/// <summary>
/// The buttons an Android notification carries for a question (local ADR 0039 §4,
/// <c>.devbook/domain/notifications/requirements.md#requirement-android-is-the-only-phone-platform</c>): set
/// answers as actions and an open answer as an inline reply, within Android's three actions —
/// and none at all where an answer from the tray could not be the whole answer, so the tap opens
/// the question instead.
/// </summary>
public sealed class PushActionsTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 21, 9, 41, 0, TimeSpan.Zero);

    private static PhoneNotification Answerable(AnswerSpec? spec) => new(
        TestNotifications.Question("Run the migration against staging?", Now, spec),
        "WORKSTATION-01",
        OwnerPresence.Online,
        Now,
        NotificationStanding.Answerable,
        Verdict: null,
        Waiting: null);

    private static AnswerSpec Choices(int count, OpenAnswer? open = null, bool multiple = false, AnswerOn answerOn = AnswerOn.AnyInstance) => new(
        [.. Enumerable.Range(1, count).Select(index => new AnswerChoice($"c{index}", $"Choice {index}"))],
        AllowMultiple: multiple,
        Open: open,
        AnswerOn: answerOn);

    [Fact]
    public void A_single_choice_question_gets_one_action_per_choice()
    {
        var actions = PushActions.For(Answerable(TestNotifications.YesNo));

        Assert.Equal(
            [("choice:yes", "Yes, run it", PushActionKind.Choice), ("choice:no", "Not now", PushActionKind.Choice)],
            actions.Select(action => (action.Key, action.Label, action.Kind)));
    }

    [Fact]
    public void Three_choices_still_fit_the_tray()
    {
        Assert.Equal(3, PushActions.For(Answerable(Choices(3))).Count);
    }

    [Fact]
    public void An_open_question_gets_an_inline_reply_with_its_placeholder()
    {
        var action = Assert.Single(PushActions.For(Answerable(TestNotifications.ReleaseNote)));

        Assert.Equal((PushActions.ReplyKey, PushActionKind.InlineReply, "Write an answer…"), (action.Key, action.Kind, action.Placeholder));
    }

    [Fact]
    public void Two_choices_with_an_open_answer_get_both_and_the_inline_reply_last()
    {
        var actions = PushActions.For(Answerable(Choices(2, new OpenAnswer("Or say why", 200))));

        Assert.Equal(
            [PushActionKind.Choice, PushActionKind.Choice, PushActionKind.InlineReply],
            actions.Select(action => action.Kind));
    }

    public static TheoryData<string, PhoneNotification> TapOnly => new()
    {
        { "multiple choice", Answerable(Choices(3, multiple: true)) },
        { "more than three choices", Answerable(Choices(4)) },
        { "three choices and an open answer", Answerable(Choices(3, new OpenAnswer())) },
        { "answered on the owner only", Answerable(Choices(2, answerOn: AnswerOn.Owner)) with { Standing = NotificationStanding.OwnerOnly } },
        { "an alert", Answerable(null) with { Notification = TestNotifications.Alert("Build failed", Now), Standing = NotificationStanding.Alert } },
        { "a reply already waiting for the owner", Answerable(TestNotifications.YesNo) with { Standing = NotificationStanding.AwaitingOwner } }
    };

    [Theory]
    [MemberData(nameof(TapOnly))]
    public void A_question_the_tray_cannot_answer_whole_gets_no_actions(string why, PhoneNotification notification)
    {
        Assert.True(PushActions.For(notification).Count == 0, why);
    }

    [Fact]
    public void A_choice_action_replies_with_its_choice()
    {
        var pick = PushActions.ToReply("choice:yes", text: null);

        Assert.NotNull(pick);
        Assert.Equal(["yes"], pick.ChoiceIds);
        Assert.Null(pick.Text);
    }

    [Fact]
    public void The_inline_reply_replies_with_the_typed_text()
    {
        var pick = PushActions.ToReply(PushActions.ReplyKey, "  Ship it after the demo  ");

        Assert.NotNull(pick);
        Assert.Empty(pick.ChoiceIds);
        Assert.Equal("Ship it after the demo", pick.Text);
    }

    [Theory]
    [InlineData("reply", "   ")]
    [InlineData("reply", null)]
    [InlineData("choice:", null)]
    [InlineData("dismiss", null)]
    [InlineData(null, null)]
    public void An_action_that_names_no_answer_replies_nothing(string? key, string? text)
    {
        Assert.Null(PushActions.ToReply(key, text));
    }
}
