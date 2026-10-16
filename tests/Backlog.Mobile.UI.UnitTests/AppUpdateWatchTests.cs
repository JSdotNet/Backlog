using Backlog.Mobile.UI.AppUpdate;
using Backlog.UI.Components.AppUpdate;

using Microsoft.Extensions.Time.Testing;

namespace Backlog.Mobile.UI.UnitTests;

/// <summary>
/// The quiet check the app makes on its own once it is on screen: at most once every
/// eight hours, the desktop's launch cadence, and silent when it fails. What it finds
/// only marks the version control; installing is always the person's call.
/// </summary>
public sealed class AppUpdateWatchTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 15, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task The_first_launch_checks_and_marks_a_newer_version()
    {
        var updates = new ScriptedAppUpdateService { CheckAnswer = AppUpdateCheckResult.Available(availableVersion: "0.1.60") };
        var watch = new AppUpdateWatch(updates, new InMemoryAppUpdateCheckStamp(), new FakeTimeProvider(Now));
        var changes = 0;
        watch.Changed += () => changes++;

        await watch.CheckQuietlyAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, updates.Checks);
        Assert.True(watch.UpdateReady);
        Assert.Equal("0.1.60", watch.Latest?.AvailableVersion);
        Assert.Equal(1, changes);
        Assert.Equal(0, updates.Installs);
    }

    [Fact]
    public async Task A_launch_within_eight_hours_of_the_last_check_asks_nothing()
    {
        var updates = new ScriptedAppUpdateService();
        var stamp = new InMemoryAppUpdateCheckStamp { LastChecked = Now - TimeSpan.FromHours(7.9) };
        var watch = new AppUpdateWatch(updates, stamp, new FakeTimeProvider(Now));

        await watch.CheckQuietlyAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0, updates.Checks);
    }

    [Fact]
    public async Task A_launch_eight_hours_after_the_last_check_checks_again_and_stamps_it()
    {
        var updates = new ScriptedAppUpdateService();
        var stamp = new InMemoryAppUpdateCheckStamp { LastChecked = Now - TimeSpan.FromHours(8) };
        var watch = new AppUpdateWatch(updates, stamp, new FakeTimeProvider(Now));

        await watch.CheckQuietlyAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, updates.Checks);
        Assert.Equal(Now, stamp.LastChecked);
        Assert.False(watch.UpdateReady);
    }

    [Fact]
    public async Task A_build_that_cannot_update_itself_never_checks_quietly()
    {
        var updates = new ScriptedAppUpdateService { IsSupported = false };
        var watch = new AppUpdateWatch(updates, new InMemoryAppUpdateCheckStamp(), new FakeTimeProvider(Now));

        await watch.CheckQuietlyAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0, updates.Checks);
    }

    [Fact]
    public async Task A_failed_quiet_check_is_silent_and_is_tried_again_next_launch()
    {
        var updates = new ScriptedAppUpdateService { CheckAnswer = AppUpdateCheckResult.Failed("Offline.") };
        var stamp = new InMemoryAppUpdateCheckStamp();
        var watch = new AppUpdateWatch(updates, stamp, new FakeTimeProvider(Now));

        await watch.CheckQuietlyAsync(TestContext.Current.CancellationToken);

        Assert.Null(stamp.LastChecked);
        Assert.Null(watch.Latest);
        Assert.False(watch.UpdateReady);
    }

    [Fact]
    public async Task A_quiet_check_that_throws_is_swallowed()
    {
        var updates = new ScriptedAppUpdateService { CheckThrows = true };
        var watch = new AppUpdateWatch(updates, new InMemoryAppUpdateCheckStamp(), new FakeTimeProvider(Now));

        await watch.CheckQuietlyAsync(TestContext.Current.CancellationToken);

        Assert.Null(watch.Latest);
    }

    [Fact]
    public async Task Checking_by_hand_always_asks_and_replaces_what_the_quiet_check_found()
    {
        var updates = new ScriptedAppUpdateService { CheckAnswer = AppUpdateCheckResult.Available(availableVersion: "0.1.60") };
        var stamp = new InMemoryAppUpdateCheckStamp();
        var time = new FakeTimeProvider(Now);
        var watch = new AppUpdateWatch(updates, stamp, time);
        await watch.CheckQuietlyAsync(TestContext.Current.CancellationToken);

        updates.CheckAnswer = AppUpdateCheckResult.UpToDate();
        time.Advance(TimeSpan.FromMinutes(5));
        var result = await watch.CheckAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, updates.Checks);
        Assert.Equal(AppUpdateAvailability.UpToDate, result.Availability);
        Assert.False(watch.UpdateReady);
        Assert.Equal(Now + TimeSpan.FromMinutes(5), stamp.LastChecked);
    }
}
