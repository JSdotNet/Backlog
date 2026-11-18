using Backlog.Modules.Notifications.Abstractions;
using Backlog.Modules.Notifications.DomainModels;
using Backlog.Modules.Notifications.Services;

using Microsoft.Extensions.Time.Testing;

using static Backlog.Modules.Notifications.UnitTests.Drafts;

namespace Backlog.Modules.Notifications.UnitTests;

/// <summary>
/// What an alert offers to open beside Acknowledge — "Open run", "Open Sessions": at most three,
/// a one-line label of 40 characters each, a <c>backlog://window/…</c> link, on an alert only; kept
/// as published and handed to the centre's screens.
/// </summary>
public sealed class NotificationActionTests
{
    private static readonly NotificationAction OpenRun = new("Open run", "backlog://window/sessions?session=run-1");
    private static readonly NotificationAction OpenSchedules = new("Open Schedules", "backlog://window/sessions?page=schedules");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void An_alert_keeps_its_actions_in_order_with_their_labels_trimmed()
    {
        var alert = Published(Alert() with { Actions = [OpenRun with { Label = "  Open run " }, OpenSchedules] });

        Assert.Equal([OpenRun, OpenSchedules], alert.Actions);
    }

    [Fact]
    public void A_notification_published_without_actions_has_none()
    {
        Assert.Empty(Published(Alert()).Actions);
        Assert.Empty(Published(Question()).Actions);
    }

    [Fact]
    public void A_question_with_actions_is_refused()
    {
        var result = Notification.Publish(Guid.NewGuid(), Owner, Question() with { Actions = [OpenRun] }, Now);

        Assert.Equal("notification.action_invalid", result.Error.Code);
    }

    [Fact]
    public void More_than_three_actions_are_refused()
    {
        var result = Notification.Publish(Guid.NewGuid(), Owner, Alert() with { Actions = [OpenRun, OpenRun, OpenRun, OpenRun] }, Now);

        Assert.Equal("notification.action_invalid", result.Error.Code);
    }

    [Theory]
    [InlineData("https://example.test/run/1")]
    [InlineData("backlog://devbook/backlog/.devbook/arc42/01-introduction.md")]
    [InlineData("javascript:alert(1)")]
    [InlineData("/window/sessions")]
    public void A_link_that_is_no_backlog_window_link_is_refused(string link)
    {
        var result = Notification.Publish(Guid.NewGuid(), Owner, Alert() with { Actions = [OpenRun with { Link = link }] }, Now);

        Assert.Equal("notification.action_invalid", result.Error.Code);
    }

    [Fact]
    public void A_link_past_2048_characters_is_refused()
    {
        var link = "backlog://window/sessions?session=" + new string('x', NotificationAction.MaxLinkLength);

        var result = Notification.Publish(Guid.NewGuid(), Owner, Alert() with { Actions = [OpenRun with { Link = link }] }, Now);

        Assert.Equal("notification.action_invalid", result.Error.Code);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Open\nrun")]
    public void A_blank_or_multi_line_label_is_refused(string label)
    {
        var result = Notification.Publish(Guid.NewGuid(), Owner, Alert() with { Actions = [OpenRun with { Label = label }] }, Now);

        Assert.Equal("notification.action_invalid", result.Error.Code);
    }

    [Fact]
    public void A_label_past_forty_characters_is_refused()
    {
        var result = Notification.Publish(Guid.NewGuid(), Owner, Alert() with { Actions = [OpenRun with { Label = new string('x', 41) }] }, Now);

        Assert.Equal("notification.content_too_long", result.Error.Code);
    }

    [Fact]
    public async Task The_centre_hands_an_alerts_actions_to_its_screens()
    {
        var time = new FakeTimeProvider(Now);
        var store = new ObservedNotificationRepository(new InMemoryNotificationRepository());
        using var owner = new OwnerNotifications(store, new FixedDevice(Owner), new AnswerDelivery([], store, time), time);
        using var centre = new NotificationCentre(store, owner, new FixedDevice(Owner), time);

        await owner.PublishAsync(Alert() with { Actions = [OpenRun, OpenSchedules] }, Ct);

        Assert.Equal([OpenRun, OpenSchedules], Assert.Single(await centre.ListAsync(Ct)).Actions!);
    }
}
