using Backlog.Modules.Notifications.UI;

namespace Backlog.Modules.Notifications.UnitTests;

/// <summary>
/// The requests to open the notification centre from outside the shell — a Windows
/// notification pressed. Every window that listens answers (local ADR 0041 §5): the main
/// window's shell, and a centre on screen in a window of its own. A press nobody heard is
/// kept for the next listener that takes it.
/// </summary>
public sealed class NotificationCentreRequestsTests
{
    [Fact]
    public void Every_listener_answers_a_request()
    {
        var requests = new NotificationCentreRequests();
        var id = Guid.NewGuid();
        var main = new List<Guid?>();
        var poppedOut = new List<Guid?>();

        using var mainListener = requests.Listen(main.Add);
        using var poppedOutListener = requests.Listen(poppedOut.Add, takeMissed: false);

        requests.Open(id);

        Assert.Equal([id], main);
        Assert.Equal([id], poppedOut);
    }

    [Fact]
    public void A_listener_that_stopped_is_not_asked_and_the_others_still_are()
    {
        var requests = new NotificationCentreRequests();
        var first = new List<Guid?>();
        var second = new List<Guid?>();

        var firstListener = requests.Listen(first.Add);
        using var secondListener = requests.Listen(second.Add);
        firstListener.Dispose();
        firstListener.Dispose();

        requests.Open(null);

        Assert.Empty(first);
        Assert.Equal([(Guid?)null], second);
    }

    [Fact]
    public void A_request_nobody_heard_goes_to_the_next_listener_that_takes_it_and_only_once()
    {
        var requests = new NotificationCentreRequests();
        var id = Guid.NewGuid();
        requests.Open(id);

        var declined = new List<Guid?>();
        using var surface = requests.Listen(declined.Add, takeMissed: false);
        var shell = new List<Guid?>();
        using var shellListener = requests.Listen(shell.Add);
        var later = new List<Guid?>();
        using var laterListener = requests.Listen(later.Add);

        Assert.Empty(declined);
        Assert.Equal([id], shell);
        Assert.Empty(later);
    }

    [Fact]
    public void A_request_only_a_centre_on_screen_heard_is_still_kept_for_the_shell()
    {
        var requests = new NotificationCentreRequests();
        var id = Guid.NewGuid();
        var poppedOut = new List<Guid?>();
        using var centre = requests.Listen(poppedOut.Add, takeMissed: false);

        // The main window is away on another page: no shell listens.
        requests.Open(id);

        var shell = new List<Guid?>();
        using var shellListener = requests.Listen(shell.Add);

        Assert.Equal([id], poppedOut);
        Assert.Equal([id], shell);
    }

    [Fact]
    public void A_request_with_listeners_is_not_kept_for_a_later_one()
    {
        var requests = new NotificationCentreRequests();
        using var listener = requests.Listen(_ => { });

        requests.Open(Guid.NewGuid());

        var later = new List<Guid?>();
        using var laterListener = requests.Listen(later.Add);
        Assert.Empty(later);
    }
}
