using Backlog.Modules.Notifications.Abstractions;
using Backlog.Modules.Notifications.Toasts;

using static Backlog.Modules.Notifications.UnitTests.Drafts;

namespace Backlog.Modules.Notifications.UnitTests;

/// <summary>
/// What an operating-system notification shows for a notification, and what a press in it asks
/// the centre to do — the half of the Windows notifications that does not need Windows.
/// </summary>
public sealed class NotificationToastTests
{
    private static readonly Guid Id = Guid.Parse("0b6a0000-0000-0000-0000-00000000000a");

    private static NotificationView View(
        NotificationKind kind = NotificationKind.Question,
        AnswerSpec? spec = null,
        NotificationState state = NotificationState.Open,
        bool isPrivate = false) => new(
            Id, Owner, Sessions, Session, kind, NotificationSeverity.Warning,
            "Run the migration against staging?", "0042_add_device_kind", "Body", kind == NotificationKind.Question ? spec ?? YesNo() : null,
            null, isPrivate, Now, state, null, null, null);

    private static AnswerSpec Choices(int count, bool multiple = false, OpenAnswer? open = null) =>
        new([.. Enumerable.Range(1, count).Select(i => new AnswerChoice($"c{i}", $"Choice {i}"))], multiple, open);

    [Fact]
    public void Set_answers_become_action_buttons()
    {
        var toast = NotificationToast.For(View())!;

        Assert.Equal(["Yes, run it", "Not now"], toast.Buttons.Select(button => button.Label));
        Assert.Null(toast.TextBox);
        Assert.Equal("yes", toast.Buttons[0].Arguments[NotificationToast.ChoiceArgument]);
        Assert.Equal(AnswerChoiceStyle.Primary, toast.Buttons[0].Style);
    }

    [Fact]
    public void Three_set_answers_still_fit()
    {
        Assert.Equal(3, NotificationToast.For(View(spec: Choices(3)))!.Buttons.Count);
    }

    [Fact]
    public void More_than_three_set_answers_open_the_app_instead()
    {
        var toast = NotificationToast.For(View(spec: Choices(4)))!;

        var only = Assert.Single(toast.Buttons);
        Assert.Equal(NotificationToast.OpenAction, only.Arguments[NotificationToast.ActionArgument]);
    }

    [Fact]
    public void A_multiple_choice_question_opens_the_app()
    {
        var toast = NotificationToast.For(View(spec: Choices(3, multiple: true)))!;

        Assert.Equal(NotificationToast.OpenAction, Assert.Single(toast.Buttons).Arguments[NotificationToast.ActionArgument]);
    }

    [Fact]
    public void An_open_answer_gets_an_inline_text_box_and_a_send_button()
    {
        var toast = NotificationToast.For(View(spec: new AnswerSpec([], Open: new OpenAnswer("Write an answer…"))))!;

        Assert.Equal("Write an answer…", toast.TextBox!.Placeholder);
        var send = Assert.Single(toast.Buttons);
        Assert.True(send.UsesTextBox);
        Assert.Equal("Send", send.Label);
    }

    [Fact]
    public void Choices_with_an_open_answer_keep_the_choices_while_three_buttons_hold_them_and_send()
    {
        var toast = NotificationToast.For(View(spec: Choices(2, open: new OpenAnswer())))!;

        Assert.Equal(["Choice 1", "Choice 2", "Send"], toast.Buttons.Select(button => button.Label));
        Assert.NotNull(toast.TextBox);
    }

    [Fact]
    public void Choices_with_an_open_answer_that_do_not_fit_leave_send_and_open()
    {
        var toast = NotificationToast.For(View(spec: Choices(3, open: new OpenAnswer())))!;

        Assert.Equal(["Send", "Open in Backlog"], toast.Buttons.Select(button => button.Label));
    }

    [Fact]
    public void No_toast_ever_has_more_than_three_buttons()
    {
        foreach (var count in new[] { 2, 3, 4, 5, 6 })
        {
            Assert.True(NotificationToast.For(View(spec: Choices(count)))!.Buttons.Count <= NotificationToast.MaxButtons);
            Assert.True(NotificationToast.For(View(spec: Choices(count, open: new OpenAnswer())))!.Buttons.Count <= NotificationToast.MaxButtons);
        }
    }

    [Fact]
    public void An_alert_offers_acknowledge()
    {
        var toast = NotificationToast.For(View(kind: NotificationKind.Alert))!;

        Assert.Equal("Acknowledge", Assert.Single(toast.Buttons).Label);
    }

    [Fact]
    public void A_private_notification_hides_its_summary()
    {
        Assert.Equal(NotificationToast.PrivateSummary, NotificationToast.For(View(isPrivate: true))!.Summary);
    }

    [Fact]
    public void A_settled_notification_raises_no_toast()
    {
        Assert.Null(NotificationToast.For(View(state: NotificationState.Answered)));
    }

    [Fact]
    public void A_choice_press_reads_back_as_that_answer()
    {
        var button = NotificationToast.For(View())!.Buttons[1];

        var action = NotificationToast.Read(button.Arguments, new Dictionary<string, string>())!;

        Assert.Equal(Id, action.NotificationId);
        Assert.Equal(NotificationToastActionKind.Answer, action.Kind);
        Assert.Equal(["no"], action.ChoiceIds);
        Assert.Null(action.Text);
    }

    [Fact]
    public void A_send_press_reads_the_text_box()
    {
        var send = NotificationToast.For(View(spec: new AnswerSpec([], Open: new OpenAnswer())))!.Buttons[0];

        var action = NotificationToast.Read(send.Arguments, new Dictionary<string, string> { [NotificationToast.TextBoxId] = "Ship it" })!;

        Assert.Equal(NotificationToastActionKind.Answer, action.Kind);
        Assert.Empty(action.ChoiceIds);
        Assert.Equal("Ship it", action.Text);
    }

    [Fact]
    public void A_send_press_with_an_empty_box_opens_the_app_rather_than_answering_nothing()
    {
        var send = NotificationToast.For(View(spec: new AnswerSpec([], Open: new OpenAnswer())))!.Buttons[0];

        Assert.Equal(NotificationToastActionKind.Open, NotificationToast.Read(send.Arguments, new Dictionary<string, string>())!.Kind);
    }

    [Fact]
    public void Acknowledge_reads_back_as_acknowledge()
    {
        var ack = NotificationToast.For(View(kind: NotificationKind.Alert))!.Buttons[0];

        Assert.Equal(NotificationToastActionKind.Acknowledge, NotificationToast.Read(ack.Arguments, new Dictionary<string, string>())!.Kind);
    }

    [Fact]
    public void A_press_on_the_toast_body_opens_the_notification()
    {
        var body = NotificationToast.For(View())!.Arguments;

        var action = NotificationToast.Read(body, new Dictionary<string, string>())!;

        Assert.Equal(NotificationToastActionKind.Open, action.Kind);
        Assert.Equal(Id, action.NotificationId);
    }

    [Fact]
    public void Arguments_without_a_notification_id_read_as_nothing()
    {
        Assert.Null(NotificationToast.Read(new Dictionary<string, string> { [NotificationToast.ActionArgument] = "answer" }, new Dictionary<string, string>()));
    }
}
