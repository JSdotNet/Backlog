using Backlog.Desktop.UI.Shell;
using Backlog.Modules.Sessions.Abstractions;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The registry behind <c>open_dashboard</c>: which windows are asked, what is made of
/// their answers, and what happens to one that has gone.
/// </summary>
public sealed class SessionsSurfaceActivatorTests
{
    [Fact]
    public async Task With_no_window_attached_there_is_nothing_to_bring_forward()
    {
        var activator = new SessionsSurfaceActivator();

        Assert.Equal(DeliverySurfaceActivation.Unattached, await activator.ActivateAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task The_attached_window_is_asked()
    {
        var activator = new SessionsSurfaceActivator();
        var asked = 0;

        using (activator.Attach(_ =>
        {
            asked++;

            return Task.FromResult(DeliverySurfaceActivation.Shown);
        }))
        {
            Assert.Equal(DeliverySurfaceActivation.Shown, await activator.ActivateAsync(TestContext.Current.CancellationToken));
        }

        Assert.Equal(1, asked);
    }

    [Fact]
    public async Task Asking_twice_shows_twice_rather_than_toggling_back()
    {
        var activator = new SessionsSurfaceActivator();

        using var _ = activator.Attach(_ => Task.FromResult(DeliverySurfaceActivation.Shown));

        Assert.Equal(DeliverySurfaceActivation.Shown, await activator.ActivateAsync(TestContext.Current.CancellationToken));

        // A caller asking to be shown the pane twice means it twice. The shell's own
        // click is a toggle and would have put the reader back on the workspace here.
        Assert.Equal(DeliverySurfaceActivation.Shown, await activator.ActivateAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task One_window_showing_the_pane_is_the_pane_being_shown()
    {
        var activator = new SessionsSurfaceActivator();

        using var gated = activator.Attach(_ => Task.FromResult(DeliverySurfaceActivation.Disabled));
        using var showing = activator.Attach(_ => Task.FromResult(DeliverySurfaceActivation.Shown));

        // The harness runs a circuit per browser tab, so a person may have two open
        // with the area switched off in neither, one, or both. The best answer wins.
        Assert.Equal(DeliverySurfaceActivation.Shown, await activator.ActivateAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_detached_window_is_not_asked_again()
    {
        var activator = new SessionsSurfaceActivator();
        var asked = 0;

        var attachment = activator.Attach(_ =>
        {
            asked++;

            return Task.FromResult(DeliverySurfaceActivation.Shown);
        });

        await activator.ActivateAsync(TestContext.Current.CancellationToken);
        attachment.Dispose();
        await activator.ActivateAsync(TestContext.Current.CancellationToken);

        // A window that stayed registered after it was gone is one the next
        // open_dashboard tries to render into.
        Assert.Equal(1, asked);

        // Disposing twice is not a second removal — the handle is a window's own, and
        // a component's disposal can run more than once.
        attachment.Dispose();

        Assert.Equal(DeliverySurfaceActivation.Unattached, await activator.ActivateAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_window_that_has_torn_down_costs_its_own_answer_and_no_more()
    {
        var activator = new SessionsSurfaceActivator();

        using var dead = activator.Attach(_ => throw new ObjectDisposedException("circuit"));
        using var alive = activator.Attach(_ => Task.FromResult(DeliverySurfaceActivation.Shown));

        // Disposal races this by construction: a window can go away between the
        // snapshot and the call, and that is one fewer window that could have
        // answered rather than a failure of the caller's request.
        Assert.Equal(DeliverySurfaceActivation.Shown, await activator.ActivateAsync(TestContext.Current.CancellationToken));
    }
}
