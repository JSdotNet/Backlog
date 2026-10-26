using Backlog.Infrastructure.Sync.Notifications;
using Backlog.Mobile.UI.Outbox;
using Backlog.Modules.Notifications.Abstractions;

using Microsoft.Extensions.Time.Testing;

namespace Backlog.Mobile.UI.UnitTests;

/// <summary>
/// The phone's Notifications view (<c>.devbook/domain/notifications/features.md#answering-from-another-device</c>):
/// the feed pulled when it opens, a card per notification with its kind, severity, age, owner
/// and whether the owner is running, the answer controls a question's shape asks for, the reply
/// queued through the outbox, and the owner's verdict once the feed brings it back.
/// </summary>
public sealed class NotificationScreenTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 21, 9, 41, 0, TimeSpan.Zero);

    private static FakeTimeProvider Clock() => new(Now);

    private static (ShellHost Host, ScriptedNotificationService Service, FakeTimeProvider Clock) Phone(params NotificationSnapshot[] notifications)
    {
        var clock = Clock();
        var service = new ScriptedNotificationService();
        foreach (var notification in notifications) service.Publish(notification, Now.AddMinutes(-1));

        return (ShellHost.Paired(clock: clock, notifications: service), service, clock);
    }

    private static NotificationSnapshot Migration(AnswerSpec? spec = null) =>
        TestNotifications.Question("Run the migration against staging?", Now.AddMinutes(-2), spec,
            summary: "Session sync-version-check wants to apply 0042_add_device_kind.",
            body: "Three tables change.\n\n```mermaid\nflowchart LR\n  A[Pair phone] --> B[Capture note]\n```\n");

    [Fact]
    public void The_tab_shows_once_the_phone_holds_a_notification()
    {
        var (host, _, _) = Phone(Migration());
        using var disposing = host;

        var app = host.Open("");

        app.WaitForAssertion(() => Assert.Contains(
            "Notifications",
            app.FindAll("[data-testid='tab-bar'] a").Select(tab => tab.TextContent.Trim())));
        var tabs = app.FindAll("[data-testid='tab-bar'] a").Select(tab => tab.TextContent.Trim()).ToList();
        Assert.Equal(["Today", "Inbox", "Notifications", "Notes"], tabs);
        Assert.NotNull(app.Find("[data-testid='tab-bar'] a[href='notifications'] .bell-icon"));
    }

    [Fact]
    public void No_notification_means_no_tab()
    {
        var (host, service, _) = Phone();
        using var disposing = host;

        var app = host.Open("");

        app.WaitForAssertion(() => Assert.True(service.Pulls > 0));
        Assert.DoesNotContain("Notifications", app.FindAll("[data-testid='tab-bar'] a").Select(tab => tab.TextContent.Trim()));
    }

    [Fact]
    public void A_card_says_its_kind_severity_age_and_that_its_owner_is_online()
    {
        var (host, service, _) = Phone(Migration());
        using var disposing = host;

        var app = host.Open("notifications");

        app.WaitForAssertion(() => Assert.Single(app.FindAll("[data-testid='notification-card']")));
        Assert.Equal("Question", app.Find("[data-testid='notification-kind']").TextContent);
        Assert.Equal("Warning", app.Find("[data-testid='notification-severity']").TextContent);
        Assert.Equal("2m ago", app.Find("[data-testid='notification-age']").TextContent);
        Assert.Equal("Owned by WORKSTATION-01 · online", app.Find("[data-testid='notification-owner']").TextContent.Trim());
        Assert.Equal("done", app.Find("[data-testid='notification-presence']").GetAttribute("data-tone"));
        Assert.Equal("1 question waiting", app.Find("[data-testid='notifications-count']").TextContent);
        Assert.True(service.Pulls > 0);

        // The list leaves the body to the detail.
        Assert.Empty(app.FindAll("[data-testid='notification-body']"));
    }

    [Fact]
    public void An_owner_that_stopped_pushing_is_offline_and_a_reply_delivers_on_wake()
    {
        var clock = Clock();
        var service = new ScriptedNotificationService();
        service.Publish(TestNotifications.Question("What should the release note say?", Now.AddMinutes(-40), TestNotifications.ReleaseNote),
            Now.AddMinutes(-31), ownerName: "LAPTOP-JS");
        using var host = ShellHost.Paired(clock: clock, notifications: service);

        var app = host.Open("notifications");

        app.WaitForAssertion(() => Assert.Equal(
            "Owned by LAPTOP-JS · offline since 09:10, delivers on wake",
            app.Find("[data-testid='notification-owner']").TextContent.Trim()));
        Assert.Equal("pending", app.Find("[data-testid='notification-presence']").GetAttribute("data-tone"));
    }

    [Fact]
    public void Tapping_a_choice_queues_one_reply_and_the_card_waits_for_the_owner()
    {
        var question = Migration();
        var (host, service, _) = Phone(question);
        using var disposing = host;
        var app = host.Open("notifications");
        app.WaitForAssertion(() => Assert.NotNull(app.Find("[data-testid='notification-choice-yes']")));

        Assert.Contains("btn--primary", app.Find("[data-testid='notification-choice-yes']").ClassList);
        app.Find("[data-testid='notification-choice-yes']").Click();

        app.WaitForAssertion(() => Assert.Single(service.Replies));
        var reply = service.Replies[0];
        Assert.Equal(question.Id, reply.NotificationId);
        Assert.Equal(TestNotifications.Owner, reply.OwnerInstanceId);
        Assert.Equal(["yes"], reply.ChoiceIds);
        Assert.Null(reply.Text);

        app.WaitForAssertion(() => Assert.Equal(
            "Answer sent, waiting for WORKSTATION-01 to accept it.",
            app.Find("[data-testid='notification-standing']").TextContent));
        Assert.Empty(app.FindAll("[data-testid='notification-answer']"));
        Assert.Empty(host.Outbox.Entries);
    }

    [Fact]
    public void A_reply_made_with_no_signal_is_kept_in_the_outbox()
    {
        var (host, service, _) = Phone(Migration());
        using var disposing = host;
        var app = host.Open("notifications");
        app.WaitForAssertion(() => Assert.NotNull(app.Find("[data-testid='notification-choice-no']")));
        service.State = InboxServiceState.Unreachable;

        app.Find("[data-testid='notification-choice-no']").Click();

        app.WaitForAssertion(() => Assert.Equal(
            "Your answer is waiting to be sent.",
            app.Find("[data-testid='notification-standing']").TextContent));
        var entry = Assert.Single(host.Outbox.Entries);
        Assert.Equal(ReplyOutboxKind.Token, entry.Kind);
        Assert.Equal(["no"], ReplyOutboxKind.Read(entry).ChoiceIds);
        Assert.Empty(service.Replies);
    }

    [Fact]
    public void The_owner_accepting_the_phones_reply_reads_answered_from_this_phone()
    {
        var question = Migration();
        var (host, service, clock) = Phone(question);
        using var disposing = host;
        var app = host.Open("notifications");
        app.WaitForAssertion(() => Assert.NotNull(app.Find("[data-testid='notification-choice-yes']")));
        app.Find("[data-testid='notification-choice-yes']").Click();
        app.WaitForAssertion(() => Assert.Single(service.Replies));

        // The owner accepts it on its next exchange, and pushes the notification answered.
        service.Publish(TestNotifications.Answered(question, service.Replies[0].Id, Guid.NewGuid(), Now.AddMinutes(1), "yes"), Now.AddMinutes(1));
        app.Find("[data-testid='notifications-refresh']").Click();

        app.WaitForAssertion(() => Assert.Equal(
            "Answered from this phone · Yes, run it",
            app.Find("[data-testid='notification-standing']").TextContent));
        Assert.Equal("answered-here", app.Find("[data-testid='notification-card']").GetAttribute("data-standing"));
        Assert.Equal("Nothing waiting for you", app.Find("[data-testid='notifications-count']").TextContent);
    }

    [Fact]
    public void An_answer_from_elsewhere_shows_the_answer_that_won()
    {
        var question = Migration();
        var (host, service, _) = Phone(question);
        using var disposing = host;
        service.Publish(TestNotifications.Answered(question, Guid.NewGuid(), Guid.NewGuid(), Now, "no"), Now);

        var app = host.Open("notifications");

        app.WaitForAssertion(() => Assert.Equal(
            "Answered elsewhere · Not now",
            app.Find("[data-testid='notification-standing']").TextContent));
        Assert.Empty(app.FindAll("[data-testid='notification-answer']"));
    }

    [Fact]
    public void A_rejected_reply_says_why_and_offers_the_answer_again()
    {
        var question = Migration();
        var (host, service, _) = Phone(question);
        using var disposing = host;
        var app = host.Open("notifications");
        app.WaitForAssertion(() => Assert.NotNull(app.Find("[data-testid='notification-choice-yes']")));
        app.Find("[data-testid='notification-choice-yes']").Click();
        app.WaitForAssertion(() => Assert.Single(service.Replies));

        service.Publish(question, Now.AddMinutes(1), rejected:
            [TestNotifications.Rejected(service.Replies[0].Id, Guid.NewGuid(), "This question is closed.", Now.AddMinutes(1))]);
        app.Find("[data-testid='notifications-refresh']").Click();

        app.WaitForAssertion(() => Assert.Contains(
            "WORKSTATION-01 turned your answer down: This question is closed. You can answer again.",
            app.Find("[data-testid='notification-rejected']").TextContent,
            StringComparison.Ordinal));
        Assert.NotNull(app.Find("[data-testid='notification-choice-yes']"));
        Assert.Equal("rejected", app.Find("[data-testid='notification-card']").GetAttribute("data-standing"));
    }

    [Fact]
    public void An_owner_only_question_is_read_only_here()
    {
        var (host, _, _) = Phone(Migration(TestNotifications.YesNo with { AnswerOn = AnswerOn.Owner }));
        using var disposing = host;

        var app = host.Open("notifications");

        app.WaitForAssertion(() => Assert.Equal(
            "Only WORKSTATION-01 takes an answer to this one.",
            app.Find("[data-testid='notification-standing']").TextContent));
        Assert.Empty(app.FindAll("[data-testid='notification-answer']"));
        Assert.Empty(app.FindAll("button[data-testid^='notification-choice-']"));
    }

    [Fact]
    public void A_multiple_choice_question_sends_the_picked_boxes()
    {
        var (host, service, _) = Phone(TestNotifications.Question("Which journeys should QA rerun?", Now.AddMinutes(-1), TestNotifications.Journeys));
        using var disposing = host;
        var app = host.Open("notifications");
        app.WaitForAssertion(() => Assert.NotNull(app.Find("[data-testid='notification-checkboxes']")));

        Assert.True(app.Find("[data-testid='notification-submit']").HasAttribute("disabled"));
        Assert.Equal("Pick 1 to 2", app.Find(".phone-notification__legend").TextContent);

        app.Find("[data-testid='notification-check-pair'] input").Change(true);
        app.Find("[data-testid='notification-check-sync'] input").Change(true);
        Assert.False(app.Find("[data-testid='notification-submit']").HasAttribute("disabled"));
        app.Find("[data-testid='notification-submit']").Click();

        app.WaitForAssertion(() => Assert.Equal(["pair", "sync"], Assert.Single(service.Replies).ChoiceIds));
    }

    [Fact]
    public void An_open_question_sends_the_typed_answer_as_plain_text()
    {
        var (host, service, _) = Phone(TestNotifications.Question("What should the release note say?", Now.AddMinutes(-14), TestNotifications.ReleaseNote));
        using var disposing = host;
        var app = host.Open("notifications");
        app.WaitForAssertion(() => Assert.NotNull(app.Find("[data-testid='notification-text'] textarea")));

        Assert.Equal("Write an answer…", app.Find("[data-testid='notification-text'] textarea").GetAttribute("placeholder"));
        Assert.True(app.Find("[data-testid='notification-send']").HasAttribute("disabled"));

        app.Find("[data-testid='notification-text'] textarea").Input("Phones answer questions now.");
        app.Find("[data-testid='notification-send']").Click();

        app.WaitForAssertion(() => Assert.Equal("Phones answer questions now.", Assert.Single(service.Replies).Text));
        Assert.Empty(service.Replies[0].ChoiceIds);
    }

    [Fact]
    public void Eight_choices_wrap_and_a_note_goes_with_the_choice_into_the_outbox()
    {
        var spec = new AnswerSpec(
            [.. Enumerable.Range(1, 8).Select(n => new AnswerChoice($"c{n}", $"Choice {n}"))],
            Note: new OpenAnswer("Why, or anything the asker should know", 4000));
        var (host, service, _) = Phone(TestNotifications.Question("Which rounding rule applies to invoice totals?", Now.AddMinutes(-4), spec));
        using var disposing = host;
        var app = host.Open("notifications");
        app.WaitForAssertion(() => Assert.NotNull(app.Find("[data-testid='notification-note-text'] textarea")));

        Assert.Equal(8, app.FindAll("button[data-testid^='notification-choice-']").Count);
        Assert.Contains("phone-notification__choices--many", app.Find("[data-testid='notification-choices']").ClassList);
        Assert.Contains("Add a note (optional)", app.Find("[data-testid='notification-note']").TextContent);
        Assert.Equal("4000", app.Find("[data-testid='notification-note-text'] textarea").GetAttribute("maxlength"));

        app.Find("[data-testid='notification-note-text'] textarea").Input("Only for invoice totals");
        app.Find("[data-testid='notification-choice-c8']").Click();

        app.WaitForAssertion(() => Assert.Equal("Only for invoice totals", Assert.Single(service.Replies).Note));
        Assert.Equal(["c8"], service.Replies[0].ChoiceIds);
    }

    [Fact]
    public void A_question_without_a_note_draws_no_note_box()
    {
        var (host, _, _) = Phone(Migration());
        using var disposing = host;
        var app = host.Open("notifications");
        app.WaitForAssertion(() => Assert.NotNull(app.Find("[data-testid='notification-choice-yes']")));

        Assert.Empty(app.FindAll("[data-testid='notification-note']"));
        Assert.DoesNotContain("phone-notification__choices--many", app.Find("[data-testid='notification-choices']").ClassList);
    }

    [Fact]
    public void An_alert_draws_no_answer_controls()
    {
        var (host, _, _) = Phone(TestNotifications.Alert("Remark left on “Storage and sync”", Now.AddHours(-1)));
        using var disposing = host;

        var app = host.Open("notifications");

        app.WaitForAssertion(() => Assert.Equal("Alert", app.Find("[data-testid='notification-kind']").TextContent));
        Assert.Equal("Info", app.Find("[data-testid='notification-severity']").TextContent);
        Assert.Equal("1h ago", app.Find("[data-testid='notification-age']").TextContent);
        Assert.Empty(app.FindAll("[data-testid='notification-answer']"));
        Assert.Empty(app.FindAll("[data-testid='notification-standing']"));
        Assert.Equal("Owned by WORKSTATION-01", app.Find("[data-testid='notification-owner']").TextContent.Trim());
    }

    [Fact]
    public void The_detail_draws_the_markdown_body_with_its_diagram()
    {
        var question = Migration();
        var (host, _, _) = Phone(question);
        using var disposing = host;

        var app = host.Open($"notifications/{question.Id}");

        app.WaitForAssertion(() => Assert.Equal("Run the migration against staging?", app.Find("[data-testid='notification-title']").TextContent));
        Assert.Contains("Three tables change.", app.Find("[data-testid='notification-body']").TextContent, StringComparison.Ordinal);
        Assert.NotNull(app.Find("[data-testid='notification-body'] [data-testid='diagram-view']"));
        Assert.NotNull(app.Find("[data-testid='notification-choice-yes']"));
    }

    [Fact]
    public void The_list_title_opens_the_detail()
    {
        var question = Migration();
        var (host, _, _) = Phone(question);
        using var disposing = host;

        var app = host.Open("notifications");

        app.WaitForAssertion(() => Assert.Equal(
            $"notifications/{question.Id}",
            app.Find("[data-testid='notification-open']").GetAttribute("href")));
    }

    [Fact]
    public void Coming_back_to_the_front_pulls_the_feed_again()
    {
        var (host, service, _) = Phone(Migration());
        using var disposing = host;
        var app = host.Open("notifications");
        app.WaitForAssertion(() => Assert.Single(app.FindAll("[data-testid='notification-card']")));
        var before = service.Pulls;

        service.Publish(TestNotifications.Question("Approve PR #1431?", Now, TestNotifications.YesNo), Now);
        host.Service<Backlog.Mobile.UI.Services.AppLifecycle>().Resume();

        app.WaitForAssertion(() => Assert.Equal(2, app.FindAll("[data-testid='notification-card']").Count));
        Assert.True(service.Pulls > before);
    }
}
