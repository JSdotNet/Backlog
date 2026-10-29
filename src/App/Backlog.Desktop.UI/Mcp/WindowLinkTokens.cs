using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace Backlog.Desktop.UI.Mcp;

/// <summary>
/// The link token that guards the pop-out pages the desktop app serves on its
/// loopback MCP listener (local ADR 0042 §7). A web link to <c>/window/{surfaceId}</c>
/// carries one, minted for that link alone; the page's first load redeems it, once,
/// within <see cref="Lifetime"/>, for a cookie value the listener sets and every
/// later request of that page — its files and its circuit — carries instead.
/// <para>
/// A singleton of the desktop head, in memory only: a restart forgets every token
/// and every cookie value, so a link minted before it is dead and a page opened
/// before it asks for a new one. Nothing a link carries survives into a file, which
/// is what keeps a token out of a digest read the next morning (ADR 0042 rejects
/// tokens in <c>backlog://</c> links for the same reason).
/// </para>
/// <para>
/// In <c>Backlog.Desktop.UI</c> beside <see cref="McpLoopbackGuard"/>, and for its
/// reason: the listener lives in a MAUI head no test project can reference, and this
/// is a security surface to be asserted directly. <c>open_window</c> mints the links
/// through <see cref="DesktopSurfaceWindowHost"/>, beside it.
/// </para>
/// </summary>
public sealed class WindowLinkTokens(TimeProvider? clock = null)
{
    /// <summary>How long a minted token can be redeemed, from the moment it is
    /// minted: long enough to read a reply and click its link, short enough that a
    /// link pasted somewhere is dead before anyone else finds it.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    /// <summary>The query key a web link carries its token under.</summary>
    public const string QueryKey = "t";

    /// <summary>256 bits, as the MCP bearer token has.</summary>
    private const int SecretBytes = 32;

    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    /// <summary>Each unredeemed token and the moment it stops redeeming.</summary>
    private readonly ConcurrentDictionary<string, DateTimeOffset> _tokens = new(StringComparer.Ordinal);

    /// <summary>Each cookie value a redemption issued. They live as long as the app
    /// does, as the browser's session cookie they are held in does.</summary>
    private readonly ConcurrentDictionary<string, byte> _cookies = new(StringComparer.Ordinal);

    /// <summary>A new token, for one link: URL-safe, so it goes into a query string
    /// as it is.</summary>
    public string Mint()
    {
        var now = _clock.GetUtcNow();

        // Tokens nobody redeemed would otherwise stay for the life of the app.
        foreach (var (stale, expires) in _tokens)
        {
            if (expires < now) _tokens.TryRemove(stale, out _);
        }

        var token = NewSecret();
        _tokens[token] = now + Lifetime;
        return token;
    }

    /// <summary>
    /// Redeems <paramref name="token"/>: the cookie value its page is to carry from
    /// now on, or null when it was never minted, was redeemed already, or is older
    /// than <see cref="Lifetime"/>. A token is spent by the attempt either way, so an
    /// expired one is never worth presenting twice.
    /// </summary>
    public string? Redeem(string? token)
    {
        if (string.IsNullOrWhiteSpace(token) || !_tokens.TryRemove(token, out var expires)) return null;

        if (_clock.GetUtcNow() > expires) return null;

        var cookie = NewSecret();
        _cookies[cookie] = 0;
        return cookie;
    }

    /// <summary>Whether <paramref name="cookie"/> is a value a redemption of this
    /// instance issued.</summary>
    public bool IsValidCookie(string? cookie) =>
        !string.IsNullOrEmpty(cookie) && _cookies.ContainsKey(cookie);

    private static string NewSecret() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(SecretBytes))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
}
