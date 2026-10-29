using System.Globalization;

using Backlog.Desktop.UI.Shell;
using Backlog.Infrastructure.Mcp;
using Backlog.SharedKernel;

namespace Backlog.Desktop.UI.Mcp;

/// <summary>
/// <see cref="ISurfaceWindowHost"/> for the desktop app (local ADR 0040 §7): a native
/// window is one more of the head's own windows, and a web link is the loopback
/// listener's page with a token minted for that link alone.
/// <para>
/// A singleton of the head, built over the head's <see cref="IShellWindows"/> — its
/// OS windows. The listener serves its pages with a browser's <see cref="IShellWindows"/>
/// of its own, so the tool, which runs on that listener, reaches the head's windows only
/// through this instance, which the listener forwards from the head's container.
/// </para>
/// <para>
/// The link names <c>localhost</c>, as the entry that asked for the tool spelled it,
/// with the listener's port: the listener binds both loopback addresses, and the page's
/// cookie is set under whichever name the link carries.
/// </para>
/// </summary>
/// <param name="windows">The head's windows.</param>
/// <param name="tokens">The link tokens the listener redeems.</param>
/// <param name="listenerPort">The port the loopback listener is listening on now, or null
/// while it is not.</param>
public sealed class DesktopSurfaceWindowHost(IShellWindows windows, WindowLinkTokens tokens, Func<int?> listenerPort)
    : ISurfaceWindowHost
{
    /// <inheritdoc />
    public bool TryOpenWindow(string surfaceId, IReadOnlyDictionary<string, string> query)
    {
        windows.Open(surfaceId, query);
        return true;
    }

    /// <inheritdoc />
    /// <remarks>No token is minted while the listener is not listening: a token for a
    /// page nothing serves would wait out its 10 minutes unredeemed.</remarks>
    public Uri? WebLink(string surfaceId, IReadOnlyDictionary<string, string> query)
    {
        if (listenerPort() is not { } port) return null;

        var tokenized = query
            .Where(pair => !string.Equals(pair.Key, WindowLinkTokens.QueryKey, StringComparison.Ordinal))
            .Append(new KeyValuePair<string, string>(WindowLinkTokens.QueryKey, tokens.Mint()));

        return new Uri(
            "http://localhost:" + port.ToString(CultureInfo.InvariantCulture) + SurfaceLink.Path(surfaceId, tokenized));
    }
}
