using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Backlog.Infrastructure.Sync;

/// <summary>
/// The loopback redirect a native app signs in through (RFC 8252 §7.3): a port on
/// <c>127.0.0.1</c> chosen by the OS, and one path, waiting for the browser to arrive
/// with the answer to an owner sign-in (local ADR 0058 §1).
/// <para>
/// A bare socket rather than <c>HttpListener</c>: http.sys wants a URL reservation for
/// anything but <c>localhost</c>, and the browser only ever sends one GET here. Bound
/// to the loopback address alone, so nothing off this machine can reach it.
/// </para>
/// </summary>
internal sealed class LoopbackRedirectListener : IDisposable
{
    private const int MaxRequestBytes = 16 * 1024;

    private readonly TcpListener _listener;
    private readonly string _path;

    private LoopbackRedirectListener(TcpListener listener, string path)
    {
        _listener = listener;
        _path = path;
        RedirectUri = new Uri($"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}{path}");
    }

    /// <summary>Where the sign-in sends the browser back to.</summary>
    public Uri RedirectUri { get; }

    /// <summary>Starts listening on a free loopback port, under a path no other sign-in uses.</summary>
    public static LoopbackRedirectListener Start()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return new LoopbackRedirectListener(listener, $"/backlog-enrolment/{Guid.NewGuid():N}/");
    }

    /// <summary>
    /// Waits for the browser to arrive at <see cref="RedirectUri"/> and answers the
    /// address it arrived at, query included. Any other request — a favicon, a stray
    /// probe — is answered 404 and the wait goes on.
    /// </summary>
    public async Task<Uri> WaitAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            using var client = await _listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
            using var stream = client.GetStream();

            // Each connection gets seconds, not the sign-in's minutes: a browser's
            // speculative preconnect, or any local process, can open a socket and send
            // nothing, and the real redirect would wait behind it.
            using var connection = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            connection.CancelAfter(ConnectionDeadline);

            try
            {
                var target = await ReadRequestTargetAsync(stream, connection.Token).ConfigureAwait(false);
                if (target is not null && target.StartsWith(_path, StringComparison.Ordinal))
                {
                    await RespondAsync(stream, "200 OK", Page, connection.Token).ConfigureAwait(false);
                    return new Uri(RedirectUri, target);
                }

                await RespondAsync(stream, "404 Not Found", string.Empty, connection.Token).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is OperationCanceledException or IOException && !cancellationToken.IsCancellationRequested)
            {
                // That connection, not the sign-in: drop it and take the next.
            }
        }
    }

    /// <summary>How long one connection may take to say what it wants.</summary>
    internal static TimeSpan ConnectionDeadline { get; set; } = TimeSpan.FromSeconds(5);

    public void Dispose() => _listener.Stop();

    private const string Page =
        "<!doctype html><html><head><meta charset=\"utf-8\"><title>Backlog</title></head>"
        + "<body style=\"font-family:system-ui,sans-serif;padding:2rem\"><p>Backlog has the sign-in. You can close this tab.</p></body></html>";

    /// <summary>The request target of a <c>GET</c>, or null for anything else.</summary>
    private static async Task<string?> ReadRequestTargetAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        var buffer = new byte[MaxRequestBytes];
        var read = 0;

        while (read < buffer.Length)
        {
            var count = await stream.ReadAsync(buffer.AsMemory(read), cancellationToken).ConfigureAwait(false);
            if (count == 0) break;
            read += count;

            if (buffer.AsSpan(0, read).IndexOf("\r\n"u8) >= 0) break;
        }

        var text = Encoding.ASCII.GetString(buffer, 0, read);
        var line = text.Split("\r\n", 2)[0].Split(' ');

        return line is ["GET", var target, ..] && target.StartsWith('/') ? target : null;
    }

    private static async Task RespondAsync(NetworkStream stream, string status, string body, CancellationToken cancellationToken)
    {
        var content = Encoding.UTF8.GetBytes(body);
        var head = Encoding.ASCII.GetBytes(
            $"HTTP/1.1 {status}\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {content.Length}\r\nConnection: close\r\nCache-Control: no-store\r\n\r\n");

        await stream.WriteAsync(head, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(content, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }
}
