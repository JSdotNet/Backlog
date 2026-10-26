using Backlog.Mobile.UI.Notifications;
using Backlog.Modules.Notifications.Abstractions;

namespace Backlog.Mobile.UI.UnitTests;

/// <summary>
/// What the tray shows for a push once the phone has pulled (local ADR 0038 §4): the words the
/// service sent — private wording included — the answers the question offers from the tray, and
/// nothing at all for a notification that has settled, by the message or by the pull.
/// </summary>
public sealed class PushTrayTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 21, 9, 41, 0, TimeSpan.Zero);

    private static PhoneNotification Held(NotificationSnapshot notification, NotificationStanding standing = NotificationStanding.Answerable) =>
        new(notification, "WORKSTATION-01", OwnerPresence.Online, Now, standing, Verdict: null, Waiting: null);

    private static NotificationSnapshot Question(bool isPrivate = false) =>
        TestNotifications.Question("Run the migration against staging?", Now, summary: "Applies 0042_add_device_kind.") with { IsPrivate = isPrivate };

    [Fact]
    public void An_open_question_shows_the_services_words_and_its_answers()
    {
        var question = Question();
        var message = new PushMessage(question.Id, "open", "question", "critical", "Run the migration against staging?", "Backlog has a question for you");

        var entry = PushTray.Entry(message, Held(question));

        Assert.NotNull(entry);
        Assert.Equal((question.Id, "Run the migration against staging?", "Backlog has a question for you", true),
            (entry.Id, entry.Title, entry.Text, entry.Urgent));
        Assert.Equal(["choice:yes", "choice:no"], entry.Actions.Select(action => action.Key));
    }

    [Fact]
    public void A_message_without_a_preview_falls_back_on_what_the_phone_pulled()
    {
        var question = Question();

        var entry = PushTray.Entry(new PushMessage(question.Id, "open", null, null, null, null), Held(question));

        Assert.Equal(("Run the migration against staging?", "Applies 0042_add_device_kind.", false), (entry!.Title, entry.Text, entry.Urgent));
    }

    [Fact]
    public void A_private_notification_pulled_without_a_preview_still_shows_the_fixed_text()
    {
        var question = Question(isPrivate: true);

        var entry = PushTray.Entry(new PushMessage(question.Id, "open", null, null, null, null), Held(question));

        Assert.Equal("Backlog has a question for you", entry!.Text);
    }

    [Fact]
    public void A_push_the_pull_did_not_bring_shows_with_no_actions()
    {
        var message = new PushMessage(Guid.NewGuid(), "open", "question", "info", "Deploy?", null);

        var entry = PushTray.Entry(message, held: null);

        Assert.Equal(("Deploy?", (string?)null), (entry!.Title, entry.Text));
        Assert.Empty(entry.Actions);
    }

    [Fact]
    public void A_settled_message_shows_nothing()
    {
        var question = Question();

        Assert.Null(PushTray.Entry(new PushMessage(question.Id, "answered", null, null, null, null), Held(question)));
    }

    [Fact]
    public void A_notification_the_pull_found_settled_shows_nothing()
    {
        var question = TestNotifications.Answered(Question(), Guid.NewGuid(), Guid.NewGuid(), Now, "yes");

        Assert.Null(PushTray.Entry(new PushMessage(question.Id, "open", null, null, "Run it?", null), Held(question, NotificationStanding.AnsweredElsewhere)));
    }
}
