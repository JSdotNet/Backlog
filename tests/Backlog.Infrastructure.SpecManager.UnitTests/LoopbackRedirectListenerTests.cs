using System.Net;

using Backlog.Infrastructure.SpecManager.OAuth;

namespace Backlog.Infrastructure.SpecManager.UnitTests;

/// <summary>
/// The loopback end of the redirect, driven with an HTTP client where a browser
/// would arrive.
/// </summary>
public sealed class LoopbackRedirectListenerTests : IDisposable
{
    private readonly HttpClient _browser = new(new SocketsHttpHandler { UseProxy = false });

    public void Dispose() => _browser.Dispose();

    [Fact]
    public void The_redirect_uri_is_on_the_loopback_address_with_a_port_of_its_own()
    {
        using var first = LoopbackRedirectListener.Start();
        using var second = LoopbackRedirectListener.Start();

        Assert.Equal("127.0.0.1", first.RedirectUri.Host);
        Assert.Equal("/callback", first.RedirectUri.AbsolutePath);
        Assert.NotEqual(first.RedirectUri.Port, second.RedirectUri.Port);
    }

    [Fact]
    public async Task Another_path_is_answered_not_found_and_the_callback_still_awaited()
    {
        using var listener = LoopbackRedirectListener.Start();
        var waiting = listener.WaitForCallbackAsync("expected", TestContext.Current.CancellationToken);

        using (var favicon = await _browser.GetAsync(new Uri(listener.RedirectUri, "/favicon.ico"), TestContext.Current.CancellationToken))
        {
            Assert.Equal(HttpStatusCode.NotFound, favicon.StatusCode);
        }

        Assert.False(waiting.IsCompleted);

        using var page = await _browser.GetAsync(new Uri($"{listener.RedirectUri}?code=abc&state=expected"), TestContext.Current.CancellationToken);
        var callback = await waiting;

        Assert.Equal(new LoopbackCallback("abc", null), callback);
        Assert.Equal("text/html", page.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task A_callback_without_a_code_is_an_error_for_the_person()
    {
        using var listener = LoopbackRedirectListener.Start();
        var waiting = listener.WaitForCallbackAsync("expected", TestContext.Current.CancellationToken);

        using var page = await _browser.GetAsync(new Uri($"{listener.RedirectUri}?state=expected"), TestContext.Current.CancellationToken);
        var callback = await waiting;

        Assert.Null(callback.Code);
        Assert.NotNull(callback.Error);
    }

    [Fact]
    public async Task An_answer_with_another_state_gets_an_error_page_and_the_right_one_is_still_awaited()
    {
        using var listener = LoopbackRedirectListener.Start();
        var waiting = listener.WaitForCallbackAsync("expected", TestContext.Current.CancellationToken);

        using (var forged = await _browser.GetAsync(new Uri($"{listener.RedirectUri}?code=stolen&state=forged"), TestContext.Current.CancellationToken))
        {
            Assert.Equal(HttpStatusCode.BadRequest, forged.StatusCode);
            Assert.Contains("did not belong to this sign-in", await forged.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), StringComparison.Ordinal);
        }

        Assert.False(waiting.IsCompleted);

        using var page = await _browser.GetAsync(new Uri($"{listener.RedirectUri}?code=abc&state=expected"), TestContext.Current.CancellationToken);
        Assert.Equal(new LoopbackCallback("abc", null), await waiting);
    }

    [Fact]
    public async Task A_connection_that_sends_nothing_is_dropped_and_the_callback_still_answered()
    {
        using var listener = LoopbackRedirectListener.Start(connectionTimeout: TimeSpan.FromMilliseconds(200));
        var waiting = listener.WaitForCallbackAsync("expected", TestContext.Current.CancellationToken);

        // A browser's speculative pre-connect: opened, and never written to.
        using var idle = new System.Net.Sockets.TcpClient();
        await idle.ConnectAsync(IPAddress.Loopback, listener.RedirectUri.Port, TestContext.Current.CancellationToken);

        using var page = await _browser.GetAsync(new Uri($"{listener.RedirectUri}?code=abc&state=expected"), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Equal(new LoopbackCallback("abc", null), await waiting);
    }

    [Fact]
    public async Task A_connection_reset_mid_request_is_dropped_and_the_callback_still_answered()
    {
        using var listener = LoopbackRedirectListener.Start(connectionTimeout: TimeSpan.FromSeconds(5));
        var waiting = listener.WaitForCallbackAsync("expected", TestContext.Current.CancellationToken);

        using (var reset = new System.Net.Sockets.TcpClient())
        {
            await reset.ConnectAsync(IPAddress.Loopback, listener.RedirectUri.Port, TestContext.Current.CancellationToken);
            await reset.GetStream().WriteAsync("GET /callback?co"u8.ToArray(), TestContext.Current.CancellationToken);
            reset.LingerState = new System.Net.Sockets.LingerOption(true, 0);
        }

        using var page = await _browser.GetAsync(new Uri($"{listener.RedirectUri}?code=abc&state=expected"), TestContext.Current.CancellationToken);

        Assert.Equal(new LoopbackCallback("abc", null), await waiting);
    }

    [Fact]
    public async Task An_overlong_request_line_is_not_read_to_its_end()
    {
        using var listener = LoopbackRedirectListener.Start(connectionTimeout: TimeSpan.FromSeconds(5));
        var waiting = listener.WaitForCallbackAsync("expected", TestContext.Current.CancellationToken);

        using (var flood = new System.Net.Sockets.TcpClient())
        {
            await flood.ConnectAsync(IPAddress.Loopback, listener.RedirectUri.Port, TestContext.Current.CancellationToken);
            var line = System.Text.Encoding.ASCII.GetBytes("GET /callback?state=" + new string('x', LoopbackRedirectListener.MaxLineLength * 2));
            try
            {
                await flood.GetStream().WriteAsync(line, TestContext.Current.CancellationToken);
            }
            catch (IOException)
            {
                // The listener may close on it before it is all written.
            }
        }

        using var page = await _browser.GetAsync(new Uri($"{listener.RedirectUri}?code=abc&state=expected"), TestContext.Current.CancellationToken);

        Assert.Equal(new LoopbackCallback("abc", null), await waiting);
    }

    [Fact]
    public async Task Waiting_ends_when_cancelled()
    {
        using var listener = LoopbackRedirectListener.Start();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

        var waiting = listener.WaitForCallbackAsync("expected", cancellation.Token);
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
    }
}
