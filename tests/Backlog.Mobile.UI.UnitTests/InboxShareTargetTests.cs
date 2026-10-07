using Backlog.Mobile.UI.Outbox;

namespace Backlog.Mobile.UI.UnitTests;

/// <summary>
/// Sharing into the app from another one.
///
/// <para>A share is a capture, made the moment it arrives: it goes into the
/// device outbox exactly as a pressed Capture does, the app lands on the Inbox
/// whichever tab it was on, and a line there says where the new row came from.
/// Whatever the person was typing in the quick-capture field is left alone.
/// These tests drive the whole shell — Router, layout, page — through the same
/// abstraction both hosts register, so they hold for the Android share target
/// and the browser harness alike.</para>
/// </summary>
public sealed class InboxShareTargetTests
{
    [Fact]
    public void A_share_that_arrived_before_the_app_was_drawn_is_captured_once_and_explained()
    {
        using var host = ShellHost.Paired();

        // The order a real Android share happens in: the intent is handled while
        // the WebView is still starting, so the payload predates every component.
        host.Share.Share("https://example.test/article");

        var app = host.Open();

        app.WaitForAssertion(() =>
        {
            Assert.Equal(1, host.Inbox.Created);
            Assert.Contains("captured", app.Find("[data-testid='share-status']").TextContent, StringComparison.OrdinalIgnoreCase);
        });

        Assert.Equal("https://example.test/article", Assert.Single(host.Inbox.Received).Title);
        app.WaitForAssertion(() => Assert.Contains(
            app.FindAll("[data-testid='inbox-row-title']"),
            title => title.TextContent == "https://example.test/article"));
    }

    [Fact]
    public void A_share_into_the_running_app_is_captured()
    {
        using var host = ShellHost.Paired();
        var app = host.Open();
        app.WaitForAssertion(() => Assert.Equal(1, host.Inbox.Pulls));

        // Sharing into an app that is already running: OnNewIntent on Android, a
        // second navigation in the harness.
        host.Share.Share("https://example.test/second");

        app.WaitForAssertion(() =>
        {
            Assert.Equal(1, host.Inbox.Created);
            Assert.NotNull(app.Find("[data-testid='share-status']"));
        });

        Assert.Equal("https://example.test/second", Assert.Single(host.Inbox.Received).Title);
    }

    [Fact]
    public void A_shared_video_is_captured_as_its_title_followed_by_its_link()
    {
        using var host = ShellHost.Paired();
        var app = host.Open();

        host.Share.Share("https://youtu.be/abc123", "How to fold a fitted sheet");

        app.WaitForAssertion(() => Assert.Equal(1, host.Inbox.Created));
        Assert.Equal("How to fold a fitted sheet https://youtu.be/abc123", Assert.Single(host.Inbox.Received).Title);
    }

    [Fact]
    public void A_share_made_while_on_another_tab_is_captured_and_lands_on_the_inbox()
    {
        using var host = ShellHost.Paired();
        var app = host.Open("");
        app.WaitForAssertion(() => Assert.Equal(1, host.Tasks.Pulls));

        host.Share.Share("https://youtu.be/abc123", "How to fold a fitted sheet");

        app.WaitForAssertion(() =>
        {
            Assert.Equal(1, host.Inbox.Created);
            Assert.Equal("inbox", host.Navigation.ToBaseRelativePath(host.Navigation.Uri));
            Assert.NotNull(app.Find("[data-testid='share-status']"));
        });

        Assert.Equal("How to fold a fitted sheet https://youtu.be/abc123", Assert.Single(host.Inbox.Received).Title);
    }

    [Fact]
    public void A_draft_being_typed_is_left_as_it_was()
    {
        using var host = ShellHost.Paired();
        var app = host.Open();

        app.Find("[data-testid='capture-field'] input").Input("watch later");

        host.Share.Share("https://youtu.be/abc123");

        app.WaitForAssertion(() => Assert.Equal(1, host.Inbox.Created));

        Assert.Equal("https://youtu.be/abc123", Assert.Single(host.Inbox.Received).Title);
        Assert.Equal("watch later", host.Service<CaptureDraft>().Text);
        Assert.Equal("watch later", app.Find("[data-testid='capture-field'] input").GetAttribute("value"));
    }

    [Fact]
    public void An_empty_share_captures_nothing()
    {
        using var host = ShellHost.Paired();
        var app = host.Open();
        app.WaitForAssertion(() => Assert.Equal(1, host.Inbox.Pulls));

        host.Share.Share("   ", "  ");

        Assert.Empty(host.Outbox.Entries);
        Assert.Empty(host.Inbox.Received);
        Assert.Empty(app.FindAll("[data-testid='share-status']"));
    }

    [Fact]
    public void With_nothing_shared_the_inbox_is_the_screen_it_always_was()
    {
        using var host = ShellHost.Paired();

        var app = host.Open();
        app.WaitForAssertion(() => Assert.Equal(1, host.Inbox.Pulls));

        Assert.Empty(app.FindAll("[data-testid='share-status']"));
        Assert.Empty(host.Outbox.Entries);
        Assert.True(string.IsNullOrEmpty(app.Find("[data-testid='capture-field'] input").GetAttribute("value")));
    }

    [Fact]
    public void The_status_line_is_a_status_rather_than_an_interruption()
    {
        using var host = ShellHost.Paired();
        var app = host.Open();

        host.Share.Share("https://example.test/article");

        app.WaitForAssertion(() =>
            Assert.Equal("status", app.Find("[data-testid='share-status']").GetAttribute("role")));
    }

    [Fact]
    public void The_status_line_goes_once_the_person_captures_something_of_their_own()
    {
        using var host = ShellHost.Paired();
        var app = host.Open();

        host.Share.Share("https://example.test/article");
        app.WaitForAssertion(() => Assert.NotNull(app.Find("[data-testid='share-status']")));

        app.Find("[data-testid='capture-field'] input").Input("Call the plumber");
        app.Find("[data-testid='capture-submit']").Click();

        app.WaitForAssertion(() =>
        {
            Assert.Equal(2, host.Inbox.Created);
            Assert.Empty(app.FindAll("[data-testid='share-status']"));
        });
    }

    [Fact]
    public void A_share_the_device_could_not_keep_says_so()
    {
        using var host = ShellHost.Paired(store: new FullDeviceStore());
        var app = host.Open();

        host.Share.Share("https://example.test/article");

        app.WaitForAssertion(() => Assert.Contains(
            "Couldn't capture what was shared",
            app.Find("[data-testid='share-status']").TextContent));

        Assert.Empty(host.Inbox.Received);
    }

    [Fact]
    public void A_share_made_before_pairing_waits_in_the_outbox()
    {
        // Out of reach as well as unpaired, so the capture stays on the phone —
        // the scripted service would otherwise take it without asking for a
        // credential.
        using var host = ShellHost.Unpaired(inbox: new ScriptedInboxService { State = InboxServiceState.Unreachable });

        host.Share.Share("https://example.test/article");

        var app = host.Open();

        app.WaitForAssertion(() => Assert.Contains(
            "1 item is waiting",
            app.Find("[data-testid='pairing-waiting']").TextContent));
        Assert.Single(host.Outbox.Entries);
    }

    /// <summary>A device with no room left: every write to the outbox fails, the
    /// way a full disk or a locked database would.</summary>
    private sealed class FullDeviceStore : IDeviceStore
    {
        private readonly InMemoryDeviceStore _inner = new();

        public IReadOnlyList<OutboxEntry> ReadOutbox() => _inner.ReadOutbox();

        public Task AddAsync(OutboxEntry entry, CancellationToken cancellationToken = default) =>
            throw new IOException("There is not enough space on the device.");

        public Task UpdateAsync(OutboxEntry entry, CancellationToken cancellationToken = default) =>
            _inner.UpdateAsync(entry, cancellationToken);

        public Task RemoveAsync(Guid id, CancellationToken cancellationToken = default) =>
            _inner.RemoveAsync(id, cancellationToken);

        public CachedInbox? ReadInbox() => _inner.ReadInbox();

        public Task SaveInboxAsync(CachedInbox inbox, CancellationToken cancellationToken = default) =>
            _inner.SaveInboxAsync(inbox, cancellationToken);
    }
}
