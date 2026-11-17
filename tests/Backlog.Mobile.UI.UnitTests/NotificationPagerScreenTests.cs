using Backlog.Mobile.UI.Components;
using Backlog.Mobile.UI.Components.Notifications;
using Backlog.Mobile.UI.Notifications;
using Backlog.Modules.Notifications.Abstractions;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Backlog.Mobile.UI.UnitTests;

/// <summary>
/// The Notifications tab, the pager
/// (<c>.devbook/domain/notifications/features.md#answering-from-another-device</c>): one open item
/// waiting on the person per screen, oldest first, under one row; Previous, Next and a swipe that
/// move and never answer; an answer that is confirmed and then gives way to the next card; the end
/// of the stack; and the detail its title opens.
/// </summary>
public sealed class NotificationPagerScreenTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 4, 9, 0, 0, TimeSpan.Zero);

    private static readonly NotificationSnapshot Older =
        TestNotifications.Question("Run the migration against staging?", Now.AddMinutes(-30),
            body: "Three tables change.\n\n```mermaid\nflowchart LR\n  A[Pair phone] --> B[Capture note]\n```\n");

    private static readonly NotificationSnapshot Newer =
        TestNotifications.Question("Approve PR #1431?", Now.AddMinutes(-5));

    private static (ShellHost Host, ScriptedNotificationService Service, FakeTimeProvider Clock) Phone(params NotificationSnapshot[] notifications)
    {
        var clock = new FakeTimeProvider(Now);
        var service = new ScriptedNotificationService();
        foreach (var notification in notifications) service.Publish(notification, Now.AddMinutes(-1));

        return (ShellHost.Paired(clock: clock, notifications: service), service, clock);
    }

    private static string Position(IRenderedComponent<Routes> app) =>
        app.Find("[data-testid='notifications-pager-position']").TextContent.Trim();

    private static string Title(IRenderedComponent<Routes> app) =>
        app.Find("[data-testid='notification-open']").TextContent.Trim();

    [Fact]
    public void The_tab_opens_on_the_pager_at_the_oldest_waiting_item()
    {
        var settled = TestNotifications.Answered(
            TestNotifications.Question("Already answered", Now.AddMinutes(-60)), Guid.NewGuid(), Guid.NewGuid(), Now.AddMinutes(-50), "yes");
        var (host, _, _) = Phone(Newer, settled, Older);
        using var disposing = host;

        var app = host.Open("notifications");

        app.WaitForAssertion(() => Assert.Equal("1 / 2", Position(app)));
        Assert.Equal(Older.Title, Title(app));
        Assert.Single(app.FindAll("[data-testid='notification-card']"));

        // The whole notification: its Markdown body, the diagram included, and the answer controls.
        Assert.NotNull(app.Find("[data-testid='notification-body']"));
        Assert.NotNull(app.Find("[data-testid='notification-choice-yes']"));

        // One dot per item, the current one marked by its own modifier, not colour alone.
        Assert.Equal(2, app.FindAll(".notifications-pager__dot").Count);
        Assert.Single(app.FindAll(".notifications-pager__dot--current"));
        Assert.Equal("polite", app.Find("[data-testid='notifications-pager-position']").GetAttribute("aria-live"));
    }

    /// <summary>Personal Validation's revise: nothing above the card but the one row — no
    /// waiting line, no view switch, no refresh button.</summary>
    [Fact]
    public void Nothing_but_the_pagers_row_sits_above_the_card()
    {
        var (host, _, _) = Phone(Newer, Older);
        using var disposing = host;

        var app = host.Open("notifications");
        app.WaitForAssertion(() => Assert.Equal("1 / 2", Position(app)));

        Assert.Empty(app.FindAll("[data-testid='notifications-count']"));
        Assert.Empty(app.FindAll("[data-testid='notifications-view-toggle']"));
        Assert.Empty(app.FindAll("[data-testid='notifications-refresh']"));
        Assert.Equal("Notifications", app.Find("h1.sr-only").TextContent);
    }

    [Fact]
    public void The_cards_title_opens_the_detail()
    {
        var (host, _, _) = Phone(Older);
        using var disposing = host;

        var app = host.Open("notifications");

        app.WaitForAssertion(() => Assert.Equal($"notifications/{Older.Id}", app.Find("[data-testid='notification-open']").GetAttribute("href")));
    }

    [Fact]
    public void Previous_and_next_are_chevron_icon_buttons_named_for_a_screen_reader()
    {
        var (host, _, _) = Phone(Newer, Older);
        using var disposing = host;

        var app = host.Open("notifications");
        app.WaitForAssertion(() => Assert.Equal("1 / 2", Position(app)));

        foreach (var (testId, name) in new[] { ("notifications-pager-previous", "Previous"), ("notifications-pager-next", "Next") })
        {
            var button = app.Find($"[data-testid='{testId}']");
            Assert.Equal(name, button.GetAttribute("aria-label"));
            Assert.Equal(string.Empty, button.TextContent.Trim());
            Assert.NotNull(button.QuerySelector("svg.chevron-up-icon"));
        }
    }

    [Fact]
    public void Previous_and_next_move_one_item_and_stop_at_the_first()
    {
        var (host, _, _) = Phone(Newer, Older);
        using var disposing = host;
        var app = host.Open("notifications");
        app.WaitForAssertion(() => Assert.Equal("1 / 2", Position(app)));

        Assert.True(app.Find("[data-testid='notifications-pager-previous']").HasAttribute("disabled"));

        app.Find("[data-testid='notifications-pager-next']").Click();

        app.WaitForAssertion(() => Assert.Equal("2 / 2", Position(app)));
        Assert.Equal(Newer.Title, Title(app));

        app.Find("[data-testid='notifications-pager-previous']").Click();

        app.WaitForAssertion(() => Assert.Equal("1 / 2", Position(app)));
        Assert.Equal(Older.Title, Title(app));
    }

    [Fact]
    public async Task While_a_reply_is_kept_next_is_busy_not_disabled_and_ignores_a_press()
    {
        var (host, service, clock) = Phone(Newer, Older);
        using var disposing = host;
        var app = host.Open("notifications");
        app.WaitForAssertion(() => Assert.Equal("1 / 2", Position(app)));

        app.Find("[data-testid='notification-choice-yes']").Click();
        app.WaitForAssertion(() => Assert.NotEmpty(app.FindAll("[data-testid='notifications-pager-confirmation']")));

        // Waiting, not unavailable: Next keeps its tab stop and says aria-disabled.
        var next = app.Find("[data-testid='notifications-pager-next']");
        Assert.Equal("true", next.GetAttribute("aria-disabled"));
        Assert.False(next.HasAttribute("disabled"));
        // At the first card Previous is unavailable, so it stays natively disabled.
        Assert.True(app.Find("[data-testid='notifications-pager-previous']").HasAttribute("disabled"));

        next.Click();
        // A swipe goes through the same guard.
        await app.FindComponent<NotificationPager>().Instance.OnSwiped("next");

        Assert.Equal("1 / 2", Position(app));
        Assert.Equal(Older.Title, Title(app));

        clock.Advance(NotificationPager.ConfirmationPause);

        app.WaitForAssertion(() => Assert.Equal(Newer.Title, Title(app)));
        Assert.Single(service.Replies);
        Assert.False(app.Find("[data-testid='notifications-pager-next']").HasAttribute("aria-disabled"));
    }

    [Fact]
    public void While_a_reply_is_kept_previous_is_busy_not_disabled_and_ignores_a_press()
    {
        var (host, _, _) = Phone(Newer, Older);
        using var disposing = host;
        var app = host.Open("notifications");
        app.WaitForAssertion(() => Assert.Equal("1 / 2", Position(app)));
        app.Find("[data-testid='notifications-pager-next']").Click();
        app.WaitForAssertion(() => Assert.Equal("2 / 2", Position(app)));

        app.Find("[data-testid='notification-choice-yes']").Click();
        app.WaitForAssertion(() => Assert.NotEmpty(app.FindAll("[data-testid='notifications-pager-confirmation']")));

        var previous = app.Find("[data-testid='notifications-pager-previous']");
        Assert.Equal("true", previous.GetAttribute("aria-disabled"));
        Assert.False(previous.HasAttribute("disabled"));
        // Next leads past the last card to the end screen, so it is waiting too, not unavailable.
        var next = app.Find("[data-testid='notifications-pager-next']");
        Assert.Equal("true", next.GetAttribute("aria-disabled"));
        Assert.False(next.HasAttribute("disabled"));

        previous.Click();

        Assert.Equal("2 / 2", Position(app));
        Assert.Equal(Newer.Title, Title(app));
    }

    [Fact]
    public async Task A_swipe_moves_between_items_and_never_answers()
    {
        var (host, service, _) = Phone(Newer, Older);
        using var disposing = host;
        var app = host.Open("notifications");
        app.WaitForAssertion(() => Assert.Equal("1 / 2", Position(app)));
        var pager = app.FindComponent<NotificationPager>();

        await pager.Instance.OnSwiped("next");
        app.WaitForAssertion(() => Assert.Equal("2 / 2", Position(app)));

        await pager.Instance.OnSwiped("previous");
        app.WaitForAssertion(() => Assert.Equal("1 / 2", Position(app)));

        // Past the first does nothing.
        await pager.Instance.OnSwiped("previous");
        app.WaitForAssertion(() => Assert.Equal("1 / 2", Position(app)));

        Assert.Empty(host.Outbox.Entries);
        Assert.Empty(service.Replies);
    }

    [Fact]
    public void Answering_queues_one_reply_confirms_it_and_moves_to_the_next_card()
    {
        var (host, service, clock) = Phone(Newer, Older);
        using var disposing = host;
        var app = host.Open("notifications");
        app.WaitForAssertion(() => Assert.Equal("1 / 2", Position(app)));

        app.Find("[data-testid='notification-choice-yes']").Click();

        app.WaitForAssertion(() => Assert.Equal(
            "Yes, run it ✓",
            app.Find("[data-testid='notifications-pager-confirmation']").TextContent.Trim()));
        Assert.Equal("status", app.Find("[data-testid='notifications-pager-confirmation']").GetAttribute("role"));
        // The confirmation shows with its card before the pager moves on.
        Assert.Equal(Older.Title, Title(app));

        clock.Advance(NotificationPager.ConfirmationPause);

        app.WaitForAssertion(() => Assert.Equal(Newer.Title, Title(app)));
        Assert.Equal("1 / 1", Position(app));
        app.WaitForAssertion(() => Assert.Single(service.Replies));
        Assert.Equal(Older.Id, service.Replies[0].NotificationId);

        // The tick goes with the card it was about: the next, unanswered card stands without it.
        Assert.Empty(app.FindAll("[data-testid='notifications-pager-confirmation']"));
    }

    [Fact]
    public void Every_move_takes_the_focus_to_the_card_it_lands_on_but_opening_does_not()
    {
        var (host, _, clock) = Phone(Newer, Older);
        using var disposing = host;
        var app = host.Open("notifications");
        app.WaitForAssertion(() => Assert.Equal("1 / 2", Position(app)));
        Assert.Equal(0, Focuses(host));

        app.Find("[data-testid='notifications-pager-next']").Click();
        app.WaitForAssertion(() => Assert.Equal(1, Focuses(host)));
        Assert.Equal(Newer.Title, app.Find("[data-testid='notifications-pager-card']").GetAttribute("aria-label"));
        Assert.Equal("-1", app.Find("[data-testid='notifications-pager-card']").GetAttribute("tabindex"));

        app.Find("[data-testid='notifications-pager-previous']").Click();
        app.WaitForAssertion(() => Assert.Equal(2, Focuses(host)));

        app.Find("[data-testid='notification-choice-yes']").Click();
        app.WaitForAssertion(() => Assert.NotEmpty(app.FindAll("[data-testid='notifications-pager-confirmation']")));
        clock.Advance(NotificationPager.ConfirmationPause);

        // Answering moves on as well.
        app.WaitForAssertion(() => Assert.Equal(3, Focuses(host)));
    }

    [Fact]
    public void Leaving_during_the_confirmation_keeps_the_one_reply_and_moves_on_no_further()
    {
        var (host, service, clock) = Phone(Newer, Older);
        using var disposing = host;
        var app = host.Open("notifications");
        app.WaitForAssertion(() => Assert.Equal("1 / 2", Position(app)));
        app.Find("[data-testid='notification-choice-yes']").Click();
        app.WaitForAssertion(() => Assert.NotEmpty(app.FindAll("[data-testid='notifications-pager-confirmation']")));

        host.Navigation.NavigateTo("notes");
        app.WaitForAssertion(() => Assert.Empty(app.FindComponents<NotificationPager>()));
        var focuses = Focuses(host);

        clock.Advance(NotificationPager.ConfirmationPause);

        app.WaitForAssertion(() => Assert.Single(service.Replies));
        Assert.Equal(focuses, Focuses(host));
        Assert.Empty(app.FindComponents<NotificationPager>());
    }

    [Fact]
    public void The_detail_goes_back_to_the_tab()
    {
        var (host, _, _) = Phone(Older);
        using var disposing = host;

        var app = host.Open($"notifications/{Older.Id}");

        app.WaitForAssertion(() => Assert.Equal("notifications", app.Find("[data-testid='notification-back']").GetAttribute("href")));
    }

    private static int Focuses(ShellHost host) =>
        host.JSInterop.Invocations.Count(invocation => invocation.Identifier == "Blazor._internal.domWrapper.focus");

    [Fact]
    public void Answering_the_last_item_is_all_caught_up()
    {
        var (host, _, clock) = Phone(Older);
        using var disposing = host;
        var app = host.Open("notifications");
        app.WaitForAssertion(() => Assert.Equal("1 / 1", Position(app)));

        app.Find("[data-testid='notification-choice-no']").Click();
        app.WaitForAssertion(() => Assert.Equal(
            "Not now ✓",
            app.Find("[data-testid='notifications-pager-confirmation']").TextContent.Trim()));
        clock.Advance(NotificationPager.ConfirmationPause);

        app.WaitForAssertion(() => Assert.Contains("All caught up", app.Find("[data-testid='notifications-pager-done']").TextContent));
        Assert.Empty(app.FindAll("[data-testid='notifications-pager-done'] button"));
    }

    [Fact]
    public void Items_moved_past_are_counted_at_the_end_and_can_be_gone_through_again()
    {
        var (host, _, _) = Phone(Newer, Older);
        using var disposing = host;
        var app = host.Open("notifications");
        app.WaitForAssertion(() => Assert.Equal("1 / 2", Position(app)));

        app.Find("[data-testid='notifications-pager-next']").Click();
        app.WaitForAssertion(() => Assert.Equal("2 / 2", Position(app)));
        app.Find("[data-testid='notifications-pager-next']").Click();

        app.WaitForAssertion(() => Assert.Contains("2 skipped", app.Find("[data-testid='notifications-pager-end']").TextContent));
        Assert.True(app.Find("[data-testid='notifications-pager-next']").HasAttribute("disabled"));

        app.Find("[data-testid='notifications-pager-again']").Click();

        app.WaitForAssertion(() => Assert.Equal("1 / 2", Position(app)));
        Assert.Equal(Older.Title, Title(app));
    }

    [Fact]
    public void A_question_only_the_owner_answers_is_in_the_stack_read_only()
    {
        var ownerOnly = Older with { AnswerSpec = TestNotifications.YesNo with { AnswerOn = AnswerOn.Owner } };
        var (host, _, _) = Phone(ownerOnly);
        using var disposing = host;
        var app = host.Open("notifications");

        app.WaitForAssertion(() => Assert.Equal("1 / 1", Position(app)));
        Assert.Empty(app.FindAll("[data-testid='notification-answer']"));
        Assert.Equal(
            "Only WORKSTATION-01 takes an answer to this one.",
            app.Find("[data-testid='notification-standing']").TextContent.Trim());
    }

    [Fact]
    public void An_alert_is_in_the_stack_with_no_answer_control()
    {
        var (host, _, _) = Phone(TestNotifications.Alert("Remark left on the tech chapter", Now.AddMinutes(-3)));
        using var disposing = host;
        var app = host.Open("notifications");

        app.WaitForAssertion(() => Assert.Equal("1 / 1", Position(app)));
        Assert.Equal("Alert", app.Find("[data-testid='notification-kind']").TextContent);
        Assert.Empty(app.FindAll("[data-testid='notification-answer']"));
    }

    [Fact]
    public void Nothing_waiting_on_open_is_all_caught_up()
    {
        var settled = TestNotifications.Answered(Older, Guid.NewGuid(), Guid.NewGuid(), Now.AddMinutes(-20), "yes");
        var (host, _, _) = Phone(settled);
        using var disposing = host;

        var app = host.Open("notifications");

        app.WaitForAssertion(() => Assert.Contains("All caught up", app.Find("[data-testid='notifications-pager-done']").TextContent));
        Assert.Empty(app.FindAll("[data-testid='notification-card']"));
    }

    [Fact]
    public void A_new_arrival_joins_the_stack_without_moving_the_current_card()
    {
        var (host, service, _) = Phone(Older);
        using var disposing = host;
        var app = host.Open("notifications");
        app.WaitForAssertion(() => Assert.Equal("1 / 1", Position(app)));

        service.Publish(Newer, Now);
        host.Service<Backlog.Mobile.UI.Services.AppLifecycle>().Resume();

        app.WaitForAssertion(() => Assert.Equal("1 / 2", Position(app)));
        Assert.Equal(Older.Title, Title(app));
    }

    [Fact]
    public void A_card_answered_elsewhere_gives_way_to_the_next_one()
    {
        var (host, service, _) = Phone(Newer, Older);
        using var disposing = host;
        var app = host.Open("notifications");
        app.WaitForAssertion(() => Assert.Equal("1 / 2", Position(app)));

        service.Publish(TestNotifications.Answered(Older, Guid.NewGuid(), Guid.NewGuid(), Now, "no"), Now);
        host.Service<Backlog.Mobile.UI.Services.AppLifecycle>().Resume();

        app.WaitForAssertion(() => Assert.Equal(Newer.Title, Title(app)));
        Assert.Equal("1 / 1", Position(app));
    }

    [Fact]
    public async Task Swiping_away_while_dictating_stops_the_mic_and_the_next_card_starts_empty()
    {
        var older = TestNotifications.Question("What should the release note say?", Now.AddMinutes(-30), TestNotifications.ReleaseNote);
        var newer = TestNotifications.Question("What should the changelog say?", Now.AddMinutes(-5), TestNotifications.ReleaseNote);
        var (host, service, _) = Phone(newer, older);
        using var disposing = host;
        host.Speech.Supported = true;
        var app = host.Open("notifications");
        app.WaitForAssertion(() => Assert.False(app.Find("[data-testid='notification-open-dictate']").HasAttribute("disabled")));
        app.Find("[data-testid='notification-text'] textarea").Input("For the first");
        app.Find("[data-testid='notification-open-dictate']").Click();
        app.WaitForAssertion(() => Assert.True(host.Speech.IsListening));

        await app.FindComponent<NotificationPager>().Instance.OnSwiped("next");
        app.WaitForAssertion(() => Assert.Equal(newer.Title, Title(app)));
        app.WaitForAssertion(() => Assert.Equal(1, host.Speech.StopCount));

        // Words the first card's turn hears after the swipe go nowhere.
        host.Speech.Complete(SpeechTranscript.Heard("too late"));

        app.WaitForAssertion(() =>
        {
            Assert.Equal(string.Empty, app.Find("[data-testid='notification-text'] textarea").GetAttribute("value") ?? string.Empty);
            Assert.Equal("false", app.Find("[data-testid='notification-open-dictate']").GetAttribute("aria-pressed"));
        });
        Assert.Empty(app.FindAll("[data-testid='notification-speech-status']"));
        Assert.Empty(service.Replies);
        Assert.Empty(host.Outbox.Entries);
    }

    [Fact]
    public void Dictating_on_the_pager_fills_the_box_and_waits_for_send()
    {
        var question = TestNotifications.Question("What should the release note say?", Now.AddMinutes(-30), TestNotifications.ReleaseNote);
        var (host, service, _) = Phone(question);
        using var disposing = host;
        host.Speech.Supported = true;
        var app = host.Open("notifications");
        app.WaitForAssertion(() => Assert.False(app.Find("[data-testid='notification-open-dictate']").HasAttribute("disabled")));

        app.Find("[data-testid='notification-open-dictate']").Click();
        app.WaitForAssertion(() => Assert.True(host.Speech.IsListening));
        host.Speech.Complete(SpeechTranscript.Heard("Phones answer questions now"));

        app.WaitForAssertion(() => Assert.Equal(
            "Phones answer questions now",
            app.Find("[data-testid='notification-text'] textarea").GetAttribute("value")));
        Assert.Empty(service.Replies);
        Assert.Empty(host.Outbox.Entries);
    }

    [Fact]
    public void A_busy_card_keeps_its_mics_focusable_and_ignores_their_press()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var speech = new ScriptedSpeechTranscriber { Supported = true };
        context.Services.AddSingleton<ISpeechTranscriber>(speech);
        var question = TestNotifications.Question("What should the release note say?", Now.AddMinutes(-30), TestNotifications.ReleaseNote);
        var notification = new PhoneNotification(question, "WORKSTATION-01", OwnerPresence.Online, Now, NotificationStanding.Answerable, null, null);

        var controls = context.Render<NotificationAnswerControls>(parameters => parameters
            .Add(p => p.Notification, notification)
            .Add(p => p.Busy, true));

        controls.WaitForAssertion(() =>
        {
            var mic = controls.Find("[data-testid='notification-open-dictate']");
            Assert.Equal("true", mic.GetAttribute("aria-disabled"));
            Assert.False(mic.HasAttribute("disabled"));
        });

        controls.Find("[data-testid='notification-open-dictate']").Click();

        Assert.False(speech.IsListening);
        Assert.Equal("false", controls.Find("[data-testid='notification-open-dictate']").GetAttribute("aria-pressed"));

        controls.Render(parameters => parameters.Add(p => p.Busy, false));

        controls.WaitForAssertion(() =>
        {
            var mic = controls.Find("[data-testid='notification-open-dictate']");
            Assert.False(mic.HasAttribute("aria-disabled"));
            Assert.False(mic.HasAttribute("disabled"));
        });
    }

    [Fact]
    public void A_card_that_stops_taking_answers_mid_turn_stops_the_mic_and_drops_its_words()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var speech = new ScriptedSpeechTranscriber { Supported = true };
        context.Services.AddSingleton<ISpeechTranscriber>(speech);
        var question = TestNotifications.Question("What should the release note say?", Now.AddMinutes(-30), TestNotifications.ReleaseNote);
        var answerable = new PhoneNotification(question, "WORKSTATION-01", OwnerPresence.Online, Now, NotificationStanding.Answerable, null, null);

        var controls = context.Render<NotificationAnswerControls>(parameters => parameters
            .Add(p => p.Notification, answerable));
        controls.WaitForAssertion(() => Assert.False(controls.Find("[data-testid='notification-open-dictate']").HasAttribute("disabled")));
        controls.Find("[data-testid='notification-text'] textarea").Input("Typed first");
        controls.Find("[data-testid='notification-open-dictate']").Click();
        controls.WaitForAssertion(() => Assert.True(speech.IsListening));

        // Answered on another device while this phone was still listening.
        controls.Render(parameters => parameters.Add(p => p.Notification, answerable with { Standing = NotificationStanding.AnsweredElsewhere }));

        controls.WaitForAssertion(() => Assert.Equal(1, speech.StopCount));
        Assert.Empty(controls.FindAll("[data-testid='notification-open-dictate']"));

        // Words the turn hears after the card stopped taking answers go nowhere.
        speech.Complete(SpeechTranscript.Heard("too late"));

        // Should the question take answers here again, its box holds only what was typed.
        controls.Render(parameters => parameters.Add(p => p.Notification, answerable with { Standing = NotificationStanding.Rejected }));

        controls.WaitForAssertion(() =>
        {
            Assert.Equal("Typed first", controls.Find("[data-testid='notification-text'] textarea").GetAttribute("value"));
            Assert.Equal("false", controls.Find("[data-testid='notification-open-dictate']").GetAttribute("aria-pressed"));
        });
        Assert.Empty(controls.FindAll("[data-testid='notification-speech-status']"));
    }
}
