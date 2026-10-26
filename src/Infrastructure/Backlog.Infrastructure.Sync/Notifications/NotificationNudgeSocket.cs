using System.Net.WebSockets;
using System.Text;

namespace Backlog.Infrastructure.Sync.Notifications;

/// <summary>
/// One open nudge socket, receive-only: the desktop never sends on it, and the client token it
/// was opened with would let it do nothing if it did.
/// </summary>
public interface INotificationNudgeSocket : IAsyncDisposable
{
    /// <summary>The next text message, or <see langword="null"/> once the socket has closed.
    /// Throws when the connection drops underneath it.</summary>
    Task<string?> ReceiveAsync(CancellationToken cancellationToken);
}

/// <summary>Opens a <see cref="INotificationNudgeSocket"/> on the URL negotiate handed out —
/// the seam the listener's reconnect and fallback are tested through.</summary>
public interface INotificationNudgeSocketConnector
{
    Task<INotificationNudgeSocket> ConnectAsync(Uri url, CancellationToken cancellationToken);
}

/// <summary>
/// The real socket: a <see cref="ClientWebSocket"/> with no subprotocol, so Web PubSub
/// delivers each message the service publishes to the group as it was sent — one small JSON
/// text frame.
/// </summary>
public sealed class ClientWebSocketNudgeConnector : INotificationNudgeSocketConnector
{
    /// <summary>How often the client pings, so an idle connection through a proxy or NAT is not
    /// dropped silently and a dead one is noticed.</summary>
    internal static readonly TimeSpan KeepAlive = TimeSpan.FromSeconds(30);

    /// <summary>How long opening the socket may take before the attempt counts as failed.</summary>
    internal static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(30);

    public async Task<INotificationNudgeSocket> ConnectAsync(Uri url, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(url);

        var socket = new ClientWebSocket();
        socket.Options.KeepAliveInterval = KeepAlive;

        // Ping and wait for the pong, so a half-open connection — the laptop slept, the network
        // changed — is noticed and the poll takes over, rather than waiting on a dead socket.
        socket.Options.KeepAliveTimeout = KeepAlive;

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ConnectTimeout);

        try
        {
            await socket.ConnectAsync(url, timeout.Token).ConfigureAwait(false);
        }
        catch
        {
            socket.Dispose();
            throw;
        }

        return new Socket(socket);
    }

    private sealed class Socket(ClientWebSocket socket) : INotificationNudgeSocket
    {
        /// <summary>A nudge is a few dozen bytes; anything past this is not one.</summary>
        private const int MaxMessageBytes = 4 * 1024;

        public async Task<string?> ReceiveAsync(CancellationToken cancellationToken)
        {
            var buffer = new byte[MaxMessageBytes];
            var length = 0;

            while (true)
            {
                if (length == buffer.Length)
                {
                    throw new WebSocketException(WebSocketError.Faulted, "A nudge message was larger than any nudge is.");
                }

                var result = await socket.ReceiveAsync(buffer.AsMemory(length), cancellationToken).ConfigureAwait(false);

                if (result.MessageType == WebSocketMessageType.Close) return null;

                length += result.Count;

                if (!result.EndOfMessage) continue;

                if (result.MessageType != WebSocketMessageType.Text)
                {
                    // Not something this desktop asked for; skip it and keep listening.
                    length = 0;
                    continue;
                }

                return Encoding.UTF8.GetString(buffer, 0, length);
            }
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                if (socket.State == WebSocketState.Open)
                {
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                    await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, timeout.Token).ConfigureAwait(false);
                }
            }
            catch (Exception ex) when (ex is WebSocketException or OperationCanceledException or ObjectDisposedException)
            {
                // Closing politely is a courtesy; the socket goes either way.
            }
            finally
            {
                socket.Dispose();
            }
        }
    }
}
