using System.Net;
using System.Text;

using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Mobile.UI.UnitTests;

/// <summary>
/// Speech capture on the Inbox screen.
///
/// <para>The point of the increment is that the recognised words are
/// <em>visible</em>: on screen in a status line, and in the quick-capture field
/// where they can still be edited before anything syncs. These tests drive the
/// screen through the same abstraction both hosts register, so they hold for the
/// browser harness and the Android head alike.</para>
/// </summary>
public sealed class InboxSpeechCaptureTests
{
    [Fact]
    public void The_mic_control_is_offered_when_the_device_can_recognise_speech()
    {
        using var host = InboxHost.WithSpeech(supported: true);

        var page = host.Render();

        page.WaitForAssertion(() =>
        {
            var mic = page.Find("[data-testid='speech-toggle']");

            Assert.False(mic.HasAttribute("disabled"));
            Assert.Equal("false", mic.GetAttribute("aria-pressed"));
        });

        Assert.Empty(page.FindAll("[data-testid='speech-unavailable']"));
    }

    [Fact]
    public void A_device_without_a_recogniser_gets_a_disabled_control_and_is_told_why()
    {
        using var host = InboxHost.WithSpeech(supported: false);

        var page = host.Render();

        page.WaitForAssertion(() =>
        {
            Assert.True(page.Find("[data-testid='speech-toggle']").HasAttribute("disabled"));
            Assert.Contains(
                "not available",
                page.Find("[data-testid='speech-unavailable']").TextContent,
                StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public void Starting_makes_the_listening_state_obvious()
    {
        using var host = InboxHost.WithSpeech(supported: true);
        var page = host.Render();

        host.StartListening(page);

        page.WaitForAssertion(() =>
        {
            Assert.Equal("true", page.Find("[data-testid='speech-toggle']").GetAttribute("aria-pressed"));
            Assert.Contains("Listening", page.Find("[data-testid='speech-status']").TextContent);
        });
    }

    [Fact]
    public void Pressing_the_control_again_asks_the_recogniser_to_finish()
    {
        using var host = InboxHost.WithSpeech(supported: true);
        var page = host.Render();

        host.StartListening(page);
        page.WaitForAssertion(() =>
            Assert.Equal("true", page.Find("[data-testid='speech-toggle']").GetAttribute("aria-pressed")));

        page.Find("[data-testid='speech-toggle']").Click();

        page.WaitForAssertion(() => Assert.Equal(1, host.Speech.StopCount));
    }

    [Fact]
    public void What_was_heard_is_shown_on_screen_and_lands_in_the_capture_field()
    {
        using var host = InboxHost.WithSpeech(supported: true);
        var page = host.Render();

        host.StartListening(page);
        host.Speech.Complete(SpeechTranscript.Heard("call the bank"));

        page.WaitForAssertion(() =>
        {
            Assert.Contains("call the bank", page.Find("[data-testid='speech-status']").TextContent);
            Assert.Equal("call the bank", CaptureFieldValue(page));

            // The turn is over, so the control has to look idle again.
            Assert.Equal("false", page.Find("[data-testid='speech-toggle']").GetAttribute("aria-pressed"));
        });
    }

    [Fact]
    public void Dictation_extends_a_field_that_already_has_text_after_a_single_space()
    {
        using var host = InboxHost.WithSpeech(supported: true);
        var page = host.Render();

        page.Find("[data-testid='capture-field'] input").Input("buy milk");

        host.StartListening(page);
        host.Speech.Complete(SpeechTranscript.Heard("and eggs"));

        page.WaitForAssertion(() => Assert.Equal("buy milk and eggs", CaptureFieldValue(page)));
    }

    [Fact]
    public void A_recogniser_that_fails_says_so_and_leaves_the_draft_alone()
    {
        using var host = InboxHost.WithSpeech(supported: true);
        var page = host.Render();

        page.Find("[data-testid='capture-field'] input").Input("buy milk");

        host.StartListening(page);
        host.Speech.Complete(SpeechTranscript.Failed("Microphone access was denied."));

        page.WaitForAssertion(() =>
        {
            Assert.Contains("denied", page.Find("[data-testid='speech-error']").TextContent);
            Assert.Equal("buy milk", CaptureFieldValue(page));
            Assert.Equal("false", page.Find("[data-testid='speech-toggle']").GetAttribute("aria-pressed"));
        });

        // Nothing was heard, so there is nothing for the transcript line to say.
        Assert.Empty(page.FindAll("[data-testid='speech-status']"));
    }

    private static string? CaptureFieldValue(IRenderedComponent<Inbox> page) =>
        page.Find("[data-testid='capture-field'] input").GetAttribute("value");

    /// <summary>
    /// The screen plus the two things it is injected with: a recogniser the test
    /// drives by hand, and a cloud sync client whose inbox is always empty —
    /// this feature never touches sync, and a real request would only add a way
    /// for these tests to fail for an unrelated reason.
    /// </summary>
    private sealed class InboxHost : IDisposable
    {
        private readonly BunitContext _context = new();

        private InboxHost(bool supported)
        {
            Speech = new FakeSpeechTranscriber(supported);

            _context.JSInterop.Mode = JSRuntimeMode.Loose;
            _context.Services.AddSingleton<ISpeechTranscriber>(Speech);

            // The screen also takes a share source. Dictation has nothing to do
            // with sharing, so this one is never triggered — it is here because
            // the screen is injected with it, and a share that never arrives is
            // the state these tests want anyway.
            _context.Services.AddSingleton<ISharedContentReceiver>(new TestSharedContentReceiver());
            _context.Services.AddSingleton(new CloudSyncClient(
                new HttpClient(new EmptyInboxHandler()) { BaseAddress = new Uri("https://sync.test") }));
        }

        public FakeSpeechTranscriber Speech { get; }

        public static InboxHost WithSpeech(bool supported) => new(supported);

        public IRenderedComponent<Inbox> Render() => _context.Render<Inbox>();

        /// <summary>Presses the mic and waits until the screen has taken the
        /// listening turn, so a test never completes a turn that has not
        /// started.</summary>
        public void StartListening(IRenderedComponent<Inbox> page)
        {
            page.WaitForAssertion(() =>
                Assert.False(page.Find("[data-testid='speech-toggle']").HasAttribute("disabled")));

            page.Find("[data-testid='speech-toggle']").Click();

            page.WaitForAssertion(() => Assert.True(Speech.IsListening));
        }

        public void Dispose() => _context.Dispose();
    }

    /// <summary>
    /// A recogniser the test finishes by hand. <see cref="ListenAsync"/> stays
    /// pending exactly as the real ones do, which is what makes the listening
    /// state observable at all.
    /// </summary>
    private sealed class FakeSpeechTranscriber(bool supported) : ISpeechTranscriber
    {
        private TaskCompletionSource<SpeechTranscript>? _pending;

        public int StopCount { get; private set; }

        public bool IsListening => _pending is not null;

        public ValueTask<bool> IsSupportedAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(supported);

        public Task<SpeechTranscript> ListenAsync(CancellationToken cancellationToken = default)
        {
            var pending = new TaskCompletionSource<SpeechTranscript>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pending = pending;
            return pending.Task;
        }

        public ValueTask StopAsync()
        {
            StopCount++;
            return ValueTask.CompletedTask;
        }

        public void Complete(SpeechTranscript result) =>
            Interlocked.Exchange(ref _pending, null)?.TrySetResult(result);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    /// <summary>An inbox with nothing in it, so the screen renders its empty
    /// state and no test depends on a running sync service.</summary>
    private sealed class EmptyInboxHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("[]", Encoding.UTF8, "application/json")
            });
    }
}
