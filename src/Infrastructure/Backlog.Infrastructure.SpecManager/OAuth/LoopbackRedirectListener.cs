using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Web;

namespace Backlog.Infrastructure.SpecManager.OAuth;

/// <summary>
/// The other end of the browser's redirect (RFC 8252 §7.3): a socket on
/// <c>127.0.0.1</c>, on whatever port the OS hands out, that answers the one
/// <c>GET /callback</c> carrying this sign-in's <c>state</c> and then stops.
/// <para>
/// A <see cref="TcpListener"/> rather than an <see cref="HttpListener"/>: the latter
/// goes through http.sys, which refuses a prefix the user holds no URL reservation
/// for, and asking for one is an administrator's prompt for a sign-in. Reading one
/// request line is all the HTTP this needs.
/// </para>
/// <para>
/// <b>One bad connection never ends the wait.</b> Connections are taken one at a
/// time, each with <see cref="DefaultConnectionTimeout"/> to say what it wants: a
/// browser's speculative pre-connect that never writes, a connection reset halfway,
/// a request line longer than <see cref="MaxLineLength"/> — each is dropped and the
/// next one accepted. Only the caller's token ends the wait without an answer.
/// </para>
/// </summary>
internal sealed class LoopbackRedirectListener : IDisposable
{
    public const string CallbackPath = "/callback";

    /// <summary>The longest request or header line read. A callback's is a few
    /// hundred characters; anything far past that is not one.</summary>
    public const int MaxLineLength = 8 * 1024;

    /// <summary>How many header lines are read before the connection is given
    /// up on.</summary>
    private const int MaxHeaderLines = 100;

    /// <summary>How long one connection is given to send its request.</summary>
    public static readonly TimeSpan DefaultConnectionTimeout = TimeSpan.FromSeconds(10);

    private readonly TcpListener _listener;
    private readonly TimeSpan _connectionTimeout;

    private LoopbackRedirectListener(TcpListener listener, TimeSpan connectionTimeout)
    {
        _listener = listener;
        _connectionTimeout = connectionTimeout;
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        RedirectUri = new Uri(string.Create(CultureInfo.InvariantCulture, $"http://127.0.0.1:{port}{CallbackPath}"));
    }

    /// <summary>Where the authorization server is told to send the browser back
    /// to.</summary>
    public Uri RedirectUri { get; }

    /// <summary>Starts listening before the browser is opened, so the redirect can
    /// never arrive at a closed port.</summary>
    /// <param name="connectionTimeout">How long one connection is given;
    /// <see cref="DefaultConnectionTimeout"/> when null.</param>
    public static LoopbackRedirectListener Start(TimeSpan? connectionTimeout = null)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return new LoopbackRedirectListener(listener, connectionTimeout ?? DefaultConnectionTimeout);
    }

    /// <summary>
    /// Waits for the browser to arrive at <see cref="CallbackPath"/> with
    /// <paramref name="expectedState"/>, answers it with a page that says whether to
    /// go back to the app, and returns what it carried.
    /// <para>
    /// Anything else is answered and waited past: another path (a favicon) with a
    /// 404, and a callback with another <c>state</c> with an error page. The latter
    /// belongs to some other request — exchanging its code is exactly the attack
    /// <c>state</c> is there to stop — and ending the sign-in on it would let
    /// whoever sent it cancel this one.
    /// </para>
    /// </summary>
    public async Task<LoopbackCallback> WaitForCallbackAsync(string expectedState, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedState);

        while (true)
        {
            using var client = await _listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
            using var connection = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            connection.CancelAfter(_connectionTimeout);

            try
            {
                var callback = await AnswerAsync(client, expectedState, connection.Token).ConfigureAwait(false);
                if (callback is not null) return callback;
            }
            catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException
                || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
            {
                // This connection is the one that failed, not the sign-in.
            }
        }
    }

    public void Dispose() => _listener.Stop();

    /// <summary>Reads one request and answers it: the callback when it is this
    /// sign-in's, null when the wait goes on.</summary>
    private static async Task<LoopbackCallback?> AnswerAsync(TcpClient client, string expectedState, CancellationToken cancellationToken)
    {
        var stream = client.GetStream();
        await using (stream.ConfigureAwait(false))
        {
            var path = await ReadRequestTargetAsync(stream, cancellationToken).ConfigureAwait(false);

            // Closed, overlong or not a GET: there is nobody to answer.
            if (path is null) return null;

            if (!IsCallback(path))
            {
                await WriteAsync(stream, HttpStatusCode.NotFound, "Not found.", cancellationToken).ConfigureAwait(false);
                return null;
            }

            var query = HttpUtility.ParseQueryString(path.Length > CallbackPath.Length ? path[(CallbackPath.Length + 1)..] : string.Empty);
            if (!string.Equals(query["state"], expectedState, StringComparison.Ordinal))
            {
                await WriteAsync(
                    stream,
                    HttpStatusCode.BadRequest,
                    "This answer from spec-manager did not belong to this sign-in, so it was not used. Backlog is still waiting for the sign-in it started.",
                    cancellationToken).ConfigureAwait(false);
                return null;
            }

            var callback = Interpret(query["code"], query["error"], query["error_description"]);
            await WriteAsync(
                stream,
                HttpStatusCode.OK,
                callback.Error is null
                    ? "You are signed in to spec-manager. You can close this tab and go back to Backlog."
                    : "Signing in to spec-manager did not work. You can close this tab; Backlog says what went wrong.",
                cancellationToken).ConfigureAwait(false);
            return callback;
        }
    }

    private static bool IsCallback(string path) =>
        path.StartsWith(CallbackPath, StringComparison.Ordinal)
        && (path.Length == CallbackPath.Length || path[CallbackPath.Length] == '?');

    private static LoopbackCallback Interpret(string? code, string? error, string? description)
    {
        if (!string.IsNullOrEmpty(error))
        {
            return new LoopbackCallback(null, error == "access_denied"
                ? "The sign-in was turned down in spec-manager."
                : $"spec-manager refused the sign-in ({error}{(string.IsNullOrWhiteSpace(description) ? string.Empty : ": " + description)}).");
        }

        return string.IsNullOrEmpty(code)
            ? new LoopbackCallback(null, "spec-manager sent the browser back without a sign-in code. Try signing in again.")
            : new LoopbackCallback(code, null);
    }

    /// <summary>The request target of the request line — <c>GET /callback?… HTTP/1.1</c>
    /// — after reading the headers through; null for a connection that closed, a
    /// line past <see cref="MaxLineLength"/>, or anything but a GET.</summary>
    private static async Task<string?> ReadRequestTargetAsync(Stream stream, CancellationToken cancellationToken)
    {
        var requestLine = await ReadLineAsync(stream, cancellationToken).ConfigureAwait(false);
        if (requestLine is null) return null;

        // The headers are read through so the browser is not answered mid-request;
        // nothing in them is needed.
        for (var lines = 0; ; lines++)
        {
            if (lines >= MaxHeaderLines) return null;

            var header = await ReadLineAsync(stream, cancellationToken).ConfigureAwait(false);
            if (header is null) return null;
            if (header.Length == 0) break;
        }

        var parts = requestLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts is { Length: >= 2 } && parts[0] == "GET" ? parts[1] : null;
    }

    /// <summary>One CRLF- or LF-terminated ASCII line, or null when the connection
    /// closed first or the line ran past <see cref="MaxLineLength"/>.</summary>
    private static async Task<string?> ReadLineAsync(Stream stream, CancellationToken cancellationToken)
    {
        var line = new StringBuilder();
        var buffer = new byte[1];

        while (line.Length <= MaxLineLength)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0) return null;

            var c = (char)buffer[0];
            if (c == '\n') return line.ToString().TrimEnd('\r');
            line.Append(c);
        }

        return null;
    }

    private static async Task WriteAsync(Stream stream, HttpStatusCode status, string message, CancellationToken cancellationToken)
    {
        var body = Encoding.UTF8.GetBytes(
            "<!DOCTYPE html><html lang=\"en\"><head><meta charset=\"utf-8\"><title>Backlog</title></head>"
            + "<body style=\"font-family:sans-serif;margin:3rem\"><p>" + WebUtility.HtmlEncode(message) + "</p></body></html>");
        var head = Encoding.ASCII.GetBytes(string.Create(
            CultureInfo.InvariantCulture,
            $"HTTP/1.1 {(int)status} {status}\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {body.Length}\r\nCache-Control: no-store\r\nConnection: close\r\n\r\n"));

        await stream.WriteAsync(head, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(body, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>What the browser brought back: a code to exchange, or why there is
/// none, written for the person.</summary>
internal sealed record LoopbackCallback(string? Code, string? Error);
