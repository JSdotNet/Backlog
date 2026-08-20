using System.Net;
using System.Text;

using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Mobile.UI.UnitTests;

/// <summary>
/// Sharing into the Inbox screen.
///
/// <para>A share is a draft, not a capture: the payload lands in the
/// quick-capture field where it can still be read and edited, the screen says
/// where it came from, and nothing leaves the device until Capture is pressed.
/// These tests drive the screen through the same abstraction both hosts
/// register, so they hold for the Android share target and the browser harness
/// alike.</para>
/// </summary>
public sealed class InboxShareTargetTests
{
    [Fact]
    public void A_share_that_arrived_before_the_screen_existed_is_prefilled_and_explained()
    {
        using var host = InboxHost.Create();

        // The order a real Android share happens in: the intent is handled while
        // the WebView is still starting, so the payload predates the component.
        host.Share.Share("https://example.test/article");

        var page = host.Render();

        page.WaitForAssertion(() =>
        {
            Assert.Equal("https://example.test/article", CaptureFieldValue(page));
            Assert.Contains("shared", page.Find("[data-testid='share-status']").TextContent, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public void A_share_that_arrives_after_the_first_render_still_reaches_the_field()
    {
        using var host = InboxHost.Create();
        var page = host.Render();

        // Sharing into an app that is already running: OnNewIntent on Android, a
        // second navigation in the harness.
        host.Share.Share("https://example.test/second");

        page.WaitForAssertion(() =>
        {
            Assert.Equal("https://example.test/second", CaptureFieldValue(page));
            Assert.NotNull(page.Find("[data-testid='share-status']"));
        });
    }

    [Fact]
    public void A_shared_video_reads_as_its_title_followed_by_its_link()
    {
        using var host = InboxHost.Create();
        var page = host.Render();

        host.Share.Share("https://youtu.be/abc123", "How to fold a fitted sheet");

        page.WaitForAssertion(() => Assert.Equal(
            "How to fold a fitted sheet https://youtu.be/abc123", CaptureFieldValue(page)));
    }

    [Fact]
    public void With_nothing_shared_the_screen_is_the_screen_it_always_was()
    {
        using var host = InboxHost.Create();

        var page = host.Render();

        page.WaitForAssertion(() => Assert.NotNull(page.Find("[data-testid='capture-field'] input")));

        Assert.Empty(page.FindAll("[data-testid='share-status']"));
        Assert.True(string.IsNullOrEmpty(CaptureFieldValue(page)));
    }

    [Fact]
    public void A_share_extends_a_draft_that_is_already_being_written()
    {
        using var host = InboxHost.Create();
        var page = host.Render();

        page.Find("[data-testid='capture-field'] input").Input("watch later");

        host.Share.Share("https://youtu.be/abc123");

        page.WaitForAssertion(() =>
            Assert.Equal("watch later https://youtu.be/abc123", CaptureFieldValue(page)));
    }

    [Fact]
    public void Nothing_is_sent_to_the_cloud_until_capture_is_pressed()
    {
        using var host = InboxHost.Create();
        var page = host.Render();

        host.Share.Share("https://example.test/article");
        page.WaitForAssertion(() => Assert.Equal("https://example.test/article", CaptureFieldValue(page)));

        // The share alone: the point of the whole feature is that this is still
        // a draft the person has not agreed to yet.
        Assert.Equal(0, host.Sync.CaptureCount);

        page.Find("[data-testid='capture-submit']").Click();

        page.WaitForAssertion(() => Assert.Equal(1, host.Sync.CaptureCount));
    }

    [Fact]
    public void The_status_line_is_a_status_rather_than_an_interruption()
    {
        using var host = InboxHost.Create();
        var page = host.Render();

        host.Share.Share("https://example.test/article");

        page.WaitForAssertion(() =>
            Assert.Equal("status", page.Find("[data-testid='share-status']").GetAttribute("role")));
    }

    private static string? CaptureFieldValue(IRenderedComponent<Inbox> page) =>
        page.Find("[data-testid='capture-field'] input").GetAttribute("value");

    /// <summary>
    /// The screen plus the three things it is injected with: a share source the
    /// test triggers by hand, a recogniser that reports no speech support (this
    /// feature has nothing to do with dictation, and an idle mic keeps the
    /// markup these tests read predictable), and a sync client that counts what
    /// it was asked to send.
    /// </summary>
    private sealed class InboxHost : IDisposable
    {
        private readonly BunitContext _context = new();

        private InboxHost()
        {
            Share = new TestSharedContentReceiver();
            Sync = new CountingSyncHandler();

            _context.JSInterop.Mode = JSRuntimeMode.Loose;
            _context.Services.AddSingleton<ISharedContentReceiver>(Share);
            _context.Services.AddSingleton<ISpeechTranscriber>(new SilentSpeechTranscriber());
            _context.Services.AddSingleton(new CloudSyncClient(
                new HttpClient(Sync) { BaseAddress = new Uri("https://sync.test") }));
        }

        public TestSharedContentReceiver Share { get; }

        public CountingSyncHandler Sync { get; }

        public static InboxHost Create() => new();

        public IRenderedComponent<Inbox> Render() => _context.Render<Inbox>();

        public void Dispose() => _context.Dispose();
    }

    /// <summary>A device with no recogniser: the mic stays disabled and the
    /// screen never waits on a listening turn.</summary>
    private sealed class SilentSpeechTranscriber : ISpeechTranscriber
    {
        public ValueTask<bool> IsSupportedAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(false);

        public Task<SpeechTranscript> ListenAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(SpeechTranscript.Failed("No recogniser."));

        public ValueTask StopAsync() => ValueTask.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    /// <summary>An empty inbox that also remembers whether a capture was ever
    /// posted, which is how "nothing syncs until Capture" is observed.</summary>
    private sealed class CountingSyncHandler : HttpMessageHandler
    {
        public int CaptureCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Post) CaptureCount++;

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("[]", Encoding.UTF8, "application/json")
            });
        }
    }
}
