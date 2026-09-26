using Backlog.Infrastructure.Sync;
using Backlog.Mobile.UI.Outbox;
using Backlog.UI.Components.Shell;

using Microsoft.Extensions.Time.Testing;

namespace Backlog.Mobile.UI.UnitTests;

/// <summary>
/// A paired phone: three tabs, a status line, and a draft that is still in the
/// Inbox's field when the person comes back to it.
/// </summary>
public sealed class ShellNavigationTests
{
    [Fact]
    public void A_paired_phone_shows_three_tabs_with_the_current_one_lit()
    {
        using var host = ShellHost.Paired();

        var app = host.Open();

        app.WaitForAssertion(() => Assert.NotNull(app.Find("[data-testid='capture-field'] input")));

        var tabs = app.FindAll("[data-testid='tab-bar'] a");
        Assert.Equal(["Inbox", "Note", "Tasks"], tabs.Select(tab => tab.TextContent));
        Assert.Equal(["", "note", "tasks"], tabs.Select(tab => tab.GetAttribute("href")));
        Assert.Contains("tab-bar__tab--active", app.Find("[data-testid='tab-bar-inbox']").ClassList);

        Assert.Empty(app.FindAll("[data-testid='pairing-code-field']"));
    }

    [Theory]
    [InlineData("note", "Note", "note-placeholder")]
    [InlineData("tasks", "Tasks", "tasks-placeholder")]
    public void Each_tab_resolves_to_its_own_page(string route, string heading, string placeholder)
    {
        using var host = ShellHost.Paired();
        var app = host.Open();

        app.WaitForAssertion(() => Assert.NotNull(app.Find("[data-testid='capture-field'] input")));

        host.Navigation.NavigateTo(route);

        app.WaitForAssertion(() => Assert.Equal(heading, app.Find("h1").TextContent));
        Assert.NotNull(app.Find($"[data-testid='{placeholder}']"));
        Assert.Contains("tab-bar__tab--active", app.Find($"[data-testid='tab-bar-{route}']").ClassList);
        Assert.DoesNotContain("tab-bar__tab--active", app.Find("[data-testid='tab-bar-inbox']").ClassList);
    }

    /// <summary>
    /// The Router remounts a page on every navigation, so text held in the
    /// Inbox's own fields would be gone after a look at another tab. This is the
    /// round trip that used to lose it.
    /// </summary>
    [Fact]
    public void A_draft_typed_in_the_inbox_is_still_there_after_a_trip_to_tasks_and_back()
    {
        using var host = ShellHost.Paired();
        var app = host.Open();

        app.WaitForAssertion(() => Assert.NotNull(app.Find("[data-testid='capture-field'] input")));
        app.Find("[data-testid='capture-field'] input").Input("Call the plumber about the boiler");

        host.Navigation.NavigateTo("tasks");
        app.WaitForAssertion(() => Assert.NotNull(app.Find("[data-testid='tasks-placeholder']")));
        Assert.Empty(app.FindAll("[data-testid='capture-field']"));

        host.Navigation.NavigateTo("");
        app.WaitForAssertion(() => Assert.Equal(
            "Call the plumber about the boiler",
            app.Find("[data-testid='capture-field'] input").GetAttribute("value")));
        Assert.False(app.Find("[data-testid='capture-submit']").HasAttribute("disabled"));
    }

    [Fact]
    public void The_status_line_says_synced_once_the_inbox_has_heard_back()
    {
        using var host = ShellHost.Paired();
        var app = host.Open();

        app.WaitForAssertion(() => Assert.Equal(
            "Synced just now",
            app.Find("[data-testid='sync-status'] .sync-status__text").TextContent));
    }

    [Fact]
    public async Task The_tracker_reads_not_paired_then_synced_then_offline_with_what_is_waiting()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 9, 25, 9, 30, 0, TimeSpan.Zero));
        var credentials = TestDevices.Unpaired();
        var store = new InMemoryDeviceStore();
        using var outbox = new DeviceOutbox(store, [new AlwaysOfflineKind()], clock);
        using var tracker = new SyncStatusTracker(credentials, outbox, store, clock);
        var raised = 0;
        tracker.Changed += () => raised++;

        Assert.Equal(SyncStatusReading.NotPaired, tracker.Current);

        credentials.Save(new DeviceCredential(Guid.NewGuid(), Guid.NewGuid(), "Phone", "a-registration-credential"));
        Assert.Equal(SyncStatusReading.Synced(null), tracker.Current);

        tracker.RecordSynced();
        Assert.Equal(SyncStatusReading.Synced(clock.GetUtcNow()), tracker.Current);

        // A failed pull is offline, with nothing waiting yet.
        tracker.RecordUnreachable();
        Assert.Equal(SyncStatusReading.Offline(0, clock.GetUtcNow()), tracker.Current);

        // Three captures the network would not take: the number is the outbox's.
        tracker.RecordSynced();
        for (var i = 0; i < 3; i++)
        {
            await outbox.EnqueueAsync("offline", Guid.CreateVersion7(), "{}", TestContext.Current.CancellationToken);
            await outbox.WhenIdleAsync();
        }

        Assert.Equal(SyncStatusReading.Offline(3, clock.GetUtcNow()), tracker.Current);

        credentials.Clear();
        Assert.Equal(SyncStatusReading.NotPaired, tracker.Current);

        Assert.True(raised >= 5);
    }

    private sealed class AlwaysOfflineKind : IOutboxKind
    {
        public string Kind => "offline";

        public Task<OutboxDelivery> SendAsync(OutboxEntry entry, CancellationToken cancellationToken) =>
            Task.FromResult(OutboxDelivery.Transient("No network."));
    }
}
