using System.Text;

using Backlog.Infrastructure.SpecManager.Api;

namespace Backlog.Infrastructure.SpecManager.Questions;

/// <summary>One open notification stream: the events it carries, one at a time.</summary>
internal interface ISpecManagerEventStream : IAsyncDisposable
{
    /// <summary>The next event's name — <c>changed</c> — or null once the stream has
    /// ended. Throws when the connection fails mid-read.</summary>
    Task<string?> ReadEventAsync(CancellationToken cancellationToken);
}

/// <summary>Opens the notification stream. The seam the question source's reconnect and
/// fallback are proved through, with a fake stream in place of a server.</summary>
internal interface ISpecManagerEventStreamConnector
{
    /// <summary>Opens the stream, or throws when it cannot: nobody signed in, a refusal,
    /// a network that did not answer.</summary>
    Task<ISpecManagerEventStream> ConnectAsync(CancellationToken cancellationToken);
}

/// <summary><see cref="ISpecManagerEventStreamConnector"/> over the client's
/// <c>GET /api/notifications/stream</c>.</summary>
internal sealed class SpecManagerEventStreamConnector(SpecManagerClient client) : ISpecManagerEventStreamConnector
{
    public async Task<ISpecManagerEventStream> ConnectAsync(CancellationToken cancellationToken)
    {
        var response = await client.OpenNotificationStreamAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var body = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            return new ServerSentEvents(response, body);
        }
        catch
        {
            response.Dispose();
            throw;
        }
    }
}

/// <summary>
/// The Server-Sent Events wire format, read for event names only: <c>event:</c> names the
/// event a blank line then dispatches; <c>data:</c>, <c>id:</c> and <c>retry:</c> are read
/// past, and a line starting with a colon — the heartbeat — is a comment. An event with no
/// name is <c>message</c>, as the format says. spec-manager sends <c>changed</c> and
/// carries nothing in its data, so nothing else is kept.
/// </summary>
internal sealed class ServerSentEvents : ISpecManagerEventStream
{
    /// <summary>The name of an event that names none.</summary>
    public const string DefaultEvent = "message";

    private readonly IDisposable? _owner;
    private readonly StreamReader _reader;

    public ServerSentEvents(IDisposable? owner, Stream body)
    {
        ArgumentNullException.ThrowIfNull(body);

        _owner = owner;
        _reader = new StreamReader(body, Encoding.UTF8);
    }

    public async Task<string?> ReadEventAsync(CancellationToken cancellationToken)
    {
        string? name = null;
        var hasField = false;

        while (true)
        {
            var line = await _reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line is null) return null;

            if (line.Length == 0)
            {
                if (hasField) return string.IsNullOrEmpty(name) ? DefaultEvent : name;

                name = null;
                continue;
            }

            if (line[0] == ':') continue;

            var colon = line.IndexOf(':', StringComparison.Ordinal);
            var field = colon < 0 ? line : line[..colon];
            var value = colon < 0 ? string.Empty : line[(colon + 1)..].TrimStart(' ');

            switch (field)
            {
                case "event":
                    name = value;
                    hasField = true;
                    break;

                case "data":
                    hasField = true;
                    break;
            }
        }
    }

    public ValueTask DisposeAsync()
    {
        _reader.Dispose();
        _owner?.Dispose();
        return ValueTask.CompletedTask;
    }
}
