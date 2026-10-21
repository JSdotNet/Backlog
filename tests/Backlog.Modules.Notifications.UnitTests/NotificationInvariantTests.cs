using Backlog.Modules.Notifications.Abstractions;
using Backlog.Modules.Notifications.DomainModels;

using static Backlog.Modules.Notifications.UnitTests.Drafts;

namespace Backlog.Modules.Notifications.UnitTests;

/// <summary>
/// One test or more per rule of <c>.devbook/domain/notifications/domain.invariants.md#notification</c>,
/// named after the invariant it proves.
/// </summary>
public sealed class NotificationInvariantTests
{
    // --- Only the owner instance changes a notification ---------------------------------

    [Fact]
    public void The_publishing_device_owns_the_notification()
    {
        var notification = Published();

        Assert.Equal(Owner, notification.OwnerInstanceId);
        Assert.Equal(NotificationState.Open, notification.State);
    }

    [Fact]
    public void Only_the_owner_instance_closes_a_notification()
    {
        var notification = Published();

        var result = notification.Close(OtherDesktop, "Not needed", Now);

        Assert.Equal("notification.not_owner", result.Error.Code);
        Assert.Equal(NotificationState.Open, notification.State);
    }

    [Fact]
    public void Only_the_owner_instance_accepts_a_reply()
    {
        var notification = Published();

        var result = notification.Accept(Reply(notification, choices: ["yes"]), OtherDesktop, Now);

        Assert.Equal("notification.not_owner", result.Error.Code);
        Assert.Null(notification.Answer);
    }

    [Fact]
    public void Only_the_owner_instance_acknowledges_an_alert()
    {
        var alert = Published(Alert());

        Assert.Equal("notification.not_owner", alert.Acknowledge(Phone, Now).Error.Code);
        Assert.Equal("notification.not_owner", alert.CloseForSubject(Session, "gone", Phone, Now).Error.Code);
    }

    // --- A question has an answer spec and a subject -------------------------------------

    [Fact]
    public void A_question_without_a_subject_is_refused()
    {
        var draft = Question() with { Subject = null };

        var result = Notification.Publish(Guid.NewGuid(), Owner, draft, Now);

        Assert.Equal("notification.question_incomplete", result.Error.Code);
    }

    [Fact]
    public void A_question_without_an_answer_spec_is_refused()
    {
        var draft = Question() with { Answer = null };

        Assert.Equal("notification.question_incomplete", Notification.Publish(Guid.NewGuid(), Owner, draft, Now).Error.Code);
    }

    [Fact]
    public void An_alert_may_have_no_subject_and_carries_no_answer_spec()
    {
        Assert.True(Notification.Publish(Guid.NewGuid(), Owner, Alert(subject: null), Now).IsSuccess);

        var withSpec = Alert() with { Answer = YesNo() };
        Assert.Equal("notification.alert_with_answer_spec", Notification.Publish(Guid.NewGuid(), Owner, withSpec, Now).Error.Code);
    }

    [Fact]
    public void A_subject_names_its_context_type_and_id()
    {
        var draft = Question(subject: new NotificationSubject("sessions", " ", "sess-91"));

        Assert.Equal("notification.subject_incomplete", Notification.Publish(Guid.NewGuid(), Owner, draft, Now).Error.Code);
    }

    [Fact]
    public void A_notification_names_its_source()
    {
        var draft = Question() with { Source = new NotificationSource("sessions", "") };

        Assert.Equal("notification.source_required", Notification.Publish(Guid.NewGuid(), Owner, draft, Now).Error.Code);
    }

    // --- Kind and severity come from their fixed sets ----------------------------------

    [Fact]
    public void An_unknown_kind_is_refused()
    {
        var draft = Question() with { Kind = (NotificationKind)7 };

        Assert.Equal("notification.unknown_kind", Notification.Publish(Guid.NewGuid(), Owner, draft, Now).Error.Code);
    }

    [Fact]
    public void An_unknown_severity_is_refused()
    {
        var draft = Question() with { Severity = (NotificationSeverity)7 };

        Assert.Equal("notification.unknown_severity", Notification.Publish(Guid.NewGuid(), Owner, draft, Now).Error.Code);
    }

    [Theory]
    [InlineData(NotificationKind.Question, NotificationSeverity.Critical)]
    [InlineData(NotificationKind.Alert, NotificationSeverity.Info)]
    [InlineData(NotificationKind.Alert, NotificationSeverity.Critical)]
    public void Severity_is_independent_of_kind(NotificationKind kind, NotificationSeverity severity)
    {
        var draft = (kind == NotificationKind.Question ? Question() : Alert()) with { Severity = severity };

        Assert.True(Notification.Publish(Guid.NewGuid(), Owner, draft, Now).IsSuccess);
    }

    // --- No deadline and no default answer ---------------------------------------------

    [Fact]
    public void Neither_the_draft_nor_the_notification_carries_a_deadline_or_a_default_answer()
    {
        static IEnumerable<string> Names(Type type) => type.GetProperties().Select(property => property.Name);

        foreach (var name in Names(typeof(NotificationDraft)).Concat(Names(typeof(Notification))).Concat(Names(typeof(AnswerSpec))))
        {
            Assert.DoesNotContain("Deadline", name, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Expires", name, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Default", name, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Timeout", name, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void A_question_nobody_answered_is_still_open_however_much_time_passes()
    {
        var notification = Published();

        // Nothing in the aggregate reads a clock: a day later, it is exactly as it was.
        var aDayLater = Now.AddDays(1);

        Assert.Equal(NotificationState.Open, notification.State);
        Assert.Null(notification.Answer);
        Assert.True(notification.Accept(Reply(notification, choices: ["no"]), Owner, aDayLater).IsSuccess);
    }

    // --- State moves only forward from Open --------------------------------------------

    [Fact]
    public void An_alert_moves_once_from_open_to_acknowledged()
    {
        var alert = Published(Alert());

        Assert.True(alert.Acknowledge(Owner, Now).IsSuccess);
        Assert.Equal(NotificationState.Acknowledged, alert.State);
        Assert.Equal(Now, alert.SettledAt);

        Assert.Equal("notification.already_settled", alert.Acknowledge(Owner, Now).Error.Code);
        Assert.Equal("notification.already_settled", alert.Close(Owner, null, Now).Error.Code);
        Assert.Equal(NotificationState.Acknowledged, alert.State);
    }

    [Fact]
    public void Only_an_alert_is_acknowledged()
    {
        var question = Published();

        Assert.Equal("notification.wrong_kind", question.Acknowledge(Owner, Now).Error.Code);
    }

    [Fact]
    public void Only_a_question_is_answered()
    {
        var alert = Published(Alert());

        var result = alert.Accept(Reply(alert, choices: ["yes"]), Owner, Now);

        Assert.Equal("notification.wrong_kind", result.Error.Code);
    }

    [Fact]
    public void A_closed_notification_stays_closed()
    {
        var notification = Published();

        Assert.True(notification.Close(Owner, "Run finished", Now).IsSuccess);
        Assert.Equal(NotificationState.Closed, notification.State);
        Assert.Equal("Run finished", notification.CloseReason);

        Assert.Equal("notification.already_settled", notification.Close(Owner, "again", Now).Error.Code);
        Assert.Equal("notification.already_settled", notification.CloseForSubject(Session, "again", Owner, Now).Error.Code);
    }

    [Fact]
    public void An_answered_question_is_not_closed_afterwards()
    {
        var notification = Published();
        notification.Accept(Reply(notification, choices: ["yes"]), Owner, Now);

        Assert.Equal("notification.already_settled", notification.Close(Owner, null, Now).Error.Code);
        Assert.Equal(NotificationState.Answered, notification.State);
    }

    [Fact]
    public void Closing_for_a_subject_closes_only_a_notification_about_that_subject()
    {
        var notification = Published();

        Assert.Equal("notification.other_subject", notification.CloseForSubject(new("tasks", "task", "T-1"), "Task done", Owner, Now).Error.Code);
        Assert.True(notification.CloseForSubject(Session, "Session archived", Owner, Now).IsSuccess);
        Assert.Equal("Session archived", notification.CloseReason);
    }

    // --- A question has at most one answer ---------------------------------------------

    [Fact]
    public void The_first_valid_reply_becomes_the_answer_and_every_later_one_is_rejected()
    {
        var notification = Published();

        var first = notification.Accept(Reply(notification, from: Phone, choices: ["yes"]), Owner, Now);
        var second = notification.Accept(Reply(notification, from: OtherDesktop, choices: ["no"]), Owner, Now);

        Assert.True(first.IsSuccess);
        Assert.Equal("notification.already_answered", second.Error.Code);
        Assert.Equal(NotificationState.Answered, notification.State);
        Assert.Equal(["yes"], notification.Answer!.ChoiceIds);
        Assert.Equal(Phone, notification.Answer.FromDeviceId);
    }

    [Fact]
    public void An_invalid_reply_leaves_the_question_open_for_another_reply()
    {
        var notification = Published();

        Assert.Equal("reply.invalid", notification.Accept(Reply(notification, choices: ["maybe"]), Owner, Now).Error.Code);
        Assert.Equal(NotificationState.Open, notification.State);
        Assert.True(notification.Accept(Reply(notification, choices: ["no"]), Owner, Now).IsSuccess);
    }

    // --- Title and summary stay within their bounds -------------------------------------

    [Fact]
    public void A_title_holds_at_most_80_characters()
    {
        Assert.True(Notification.Publish(Guid.NewGuid(), Owner, Question() with { Title = new string('t', 80) }, Now).IsSuccess);

        var result = Notification.Publish(Guid.NewGuid(), Owner, Question() with { Title = new string('t', 81) }, Now);
        Assert.Equal("notification.content_too_long", result.Error.Code);
    }

    [Fact]
    public void A_summary_holds_at_most_180_characters()
    {
        Assert.True(Notification.Publish(Guid.NewGuid(), Owner, Question() with { Summary = new string('s', 180) }, Now).IsSuccess);

        var result = Notification.Publish(Guid.NewGuid(), Owner, Question() with { Summary = new string('s', 181) }, Now);
        Assert.Equal("notification.content_too_long", result.Error.Code);
    }

    [Fact]
    public void A_body_holds_at_most_32_KB_of_Markdown()
    {
        Assert.True(Notification.Publish(Guid.NewGuid(), Owner, Question() with { BodyMarkdown = new string('b', 32 * 1024) }, Now).IsSuccess);

        var tooLong = Notification.Publish(Guid.NewGuid(), Owner, Question() with { BodyMarkdown = new string('b', (32 * 1024) + 1) }, Now);
        Assert.Equal("notification.content_too_long", tooLong.Error.Code);

        // Measured in bytes: 11,000 three-byte characters are 33,000 bytes.
        var wide = Notification.Publish(Guid.NewGuid(), Owner, Question() with { BodyMarkdown = new string('€', 11_000) }, Now);
        Assert.Equal("notification.content_too_long", wide.Error.Code);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Two\nlines")]
    public void A_title_is_one_line_of_text(string title)
    {
        var result = Notification.Publish(Guid.NewGuid(), Owner, Question() with { Title = title }, Now);

        Assert.Equal("notification.title_required", result.Error.Code);
    }
}
