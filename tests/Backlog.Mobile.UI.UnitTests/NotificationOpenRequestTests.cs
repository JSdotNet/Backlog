using Backlog.Mobile.UI.Notifications;

namespace Backlog.Mobile.UI.UnitTests;

/// <summary>
/// A tap on a notification in the tray opens it in the app (local ADR 0039 §4): the head hands
/// the id over, the layout navigates to its detail — also when the tap launched the app and
/// arrived before the layout was drawn.
/// </summary>
public sealed class NotificationOpenRequestTests
{
    private static readonly Guid Id = Guid.Parse("01920000-0000-7000-8000-0000000000d1");

    [Fact]
    public void A_tap_that_launched_the_app_opens_the_notification_once_the_layout_is_drawn()
    {
        using var host = ShellHost.Paired();
        host.Service<NotificationOpenRequests>().Request(Id);

        host.Open("");

        Assert.EndsWith($"/notifications/{Id:D}", host.Navigation.Uri, StringComparison.Ordinal);
    }

    [Fact]
    public void A_tap_while_the_app_is_open_opens_the_notification()
    {
        using var host = ShellHost.Paired();
        var app = host.Open("");

        host.Service<NotificationOpenRequests>().Request(Id);

        app.WaitForAssertion(() => Assert.EndsWith($"/notifications/{Id:D}", host.Navigation.Uri, StringComparison.Ordinal));
    }

    [Fact]
    public void A_request_is_followed_once()
    {
        var requests = new NotificationOpenRequests();
        requests.Request(Id);

        List<Guid> first = [];
        List<Guid> second = [];
        using (requests.Subscribe(first.Add)) { }
        using (requests.Subscribe(second.Add)) { }

        Assert.Equal([Id], first);
        Assert.Empty(second);
    }
}
