using System.Globalization;

using Backlog.Desktop.UI.Shell;
using Backlog.Infrastructure.Mcp;
using Backlog.SharedKernel;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Backlog.Desktop.UI.Mcp;

/// <summary>
/// <see cref="ISurfaceWindowHost"/> for the desktop app (local ADR 0043 §7): a native
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
/// <param name="log">Where every minted link is recorded (local ADR 0052 §4); none
/// records nothing.</param>
public sealed class DesktopSurfaceWindowHost(
    IShellWindows windows,
    WindowLinkTokens tokens,
    Func<int?> listenerPort,
    ILogger<DesktopSurfaceWindowHost>? log = null)
    : ISurfaceWindowHost
{
    private readonly ILogger _log = (ILogger?)log ?? NullLogger.Instance;

    /// <inheritdoc />
    /// <remarks>Awaits the head's answer, so a window that failed to build, or an app
    /// with nothing to open it in, reads as not opened.</remarks>
    public Task<bool> TryOpenWindowAsync(string surfaceId, IReadOnlyDictionary<string, string> query) =>
        windows.OpenAsync(surfaceId, query);

    /// <inheritdoc />
    /// <remarks>No token is minted while the listener is not listening: a token for a
    /// page nothing serves would wait out its 10 minutes unredeemed. Each link minted is
    /// logged with its surface, its caller and its token's expiry; the token itself, and
    /// so the link, never reaches the log, which is a file read later.</remarks>
    public Uri? WebLink(string surfaceId, IReadOnlyDictionary<string, string> query, string caller)
    {
        if (listenerPort() is not { } port) return null;

        var token = tokens.Mint(out var expires);
        var tokenized = query
            .Where(pair => !string.Equals(pair.Key, WindowLinkTokens.QueryKey, StringComparison.Ordinal))
            .Append(new KeyValuePair<string, string>(WindowLinkTokens.QueryKey, token));

        _log.LogInformation(
            "open_window minted a web link to {SurfaceId} for {Caller}; its token redeems until {Expires:o}.",
            surfaceId,
            caller,
            expires);

        return new Uri(
            "http://localhost:" + port.ToString(CultureInfo.InvariantCulture) + SurfaceLink.Path(surfaceId, tokenized));
    }
}
