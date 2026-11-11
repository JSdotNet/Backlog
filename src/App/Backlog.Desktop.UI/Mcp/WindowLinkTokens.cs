using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace Backlog.Desktop.UI.Mcp;

/// <summary>
/// The link token that guards the pop-out pages the desktop app serves on its
/// loopback MCP listener (local ADR 0043 §7). A web link to <c>/window/{surfaceId}</c>
/// carries one, minted for that link alone; the page's first load redeems it, once,
/// within <see cref="Lifetime"/>, for a cookie value the listener sets and every
/// later request of that page — its files and its circuit — carries instead.
/// <para>
/// A singleton of the desktop head, in memory only: a restart forgets every token
/// and every cookie value, so a link minted before it is dead and a page opened
/// before it asks for a new one. Nothing a link carries survives into a file, which
/// is what keeps a token out of a digest read the next morning (ADR 0043 rejects
/// tokens in <c>backlog://</c> links for the same reason).
/// </para>
/// <para>
/// <b>A cookie value expires after <see cref="IdleTimeout"/> idle</b> (local ADR 0052
/// §4): every request it admits slides it, and a page whose circuit is still connected
/// is never idle, so a window in use stays open and one left alone for a working day
/// asks for a new link.
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

    /// <summary>How long a cookie value stays valid with no request carrying it and no
    /// connection held open on it (local ADR 0052 §4).</summary>
    public static readonly TimeSpan IdleTimeout = TimeSpan.FromHours(8);

    /// <summary>The query key a web link carries its token under.</summary>
    public const string QueryKey = "t";

    /// <summary>256 bits, as the MCP bearer token has.</summary>
    private const int SecretBytes = 32;

    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    /// <summary>Each unredeemed token and the moment it stops redeeming.</summary>
    private readonly ConcurrentDictionary<string, DateTimeOffset> _tokens = new(StringComparer.Ordinal);

    /// <summary>Each cookie value a redemption issued, and how recently it was used.
    /// One goes when it has been idle for <see cref="IdleTimeout"/>; the browser keeps
    /// it as a session cookie, so only this side can tell it has gone.</summary>
    private readonly ConcurrentDictionary<string, CookieUse> _cookies = new(StringComparer.Ordinal);

    /// <summary>A new token, for one link: URL-safe, so it goes into a query string
    /// as it is.</summary>
    public string Mint() => Mint(out _);

    /// <summary>A new token, for one link, and the moment it stops redeeming — what the
    /// log of minted links records in place of the token.</summary>
    public string Mint(out DateTimeOffset expires)
    {
        var now = _clock.GetUtcNow();

        // Tokens nobody redeemed and cookies nobody uses would otherwise stay for the
        // life of the app.
        foreach (var (stale, until) in _tokens)
        {
            if (until < now) _tokens.TryRemove(stale, out _);
        }

        foreach (var (stale, use) in _cookies)
        {
            if (use.IsIdle(now)) _cookies.TryRemove(stale, out _);
        }

        var token = NewSecret();
        expires = now + Lifetime;
        _tokens[token] = expires;
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
        _cookies[cookie] = new CookieUse(_clock.GetUtcNow());
        return cookie;
    }

    /// <summary>
    /// Whether <paramref name="cookie"/> is a value a redemption of this instance
    /// issued that has not gone idle for longer than <see cref="IdleTimeout"/>. A valid
    /// one is used by the asking, so the idle time starts over; an idle one is
    /// forgotten, and never valid again.
    /// </summary>
    public bool IsValidCookie(string? cookie)
    {
        if (string.IsNullOrEmpty(cookie) || !_cookies.TryGetValue(cookie, out var use)) return false;

        if (use.TryTouch(_clock.GetUtcNow())) return true;

        _cookies.TryRemove(cookie, out _);
        return false;
    }

    /// <summary>
    /// Holds <paramref name="cookie"/> in use for as long as one request it admitted
    /// runs — a page's circuit, which is one connection for as long as the page is
    /// open — so it is not idle while that request is; disposing the hold ends it, and
    /// the idle time starts from then. A cookie value this instance does not hold is
    /// held by nothing.
    /// </summary>
    public IDisposable Hold(string? cookie) =>
        !string.IsNullOrEmpty(cookie) && _cookies.TryGetValue(cookie, out var use)
            ? use.Hold(_clock)
            : NoHold.Instance;

    /// <summary>When one cookie value was last used, and how many requests it admitted
    /// are running now.</summary>
    private sealed class CookieUse(DateTimeOffset issued)
    {
        private readonly Lock _gate = new();
        private DateTimeOffset _lastUsed = issued;
        private int _running;

        public bool IsIdle(DateTimeOffset now)
        {
            lock (_gate) return _running == 0 && now - _lastUsed > IdleTimeout;
        }

        /// <summary>Uses the value now, unless it has already gone idle.</summary>
        public bool TryTouch(DateTimeOffset now)
        {
            lock (_gate)
            {
                if (_running == 0 && now - _lastUsed > IdleTimeout) return false;

                _lastUsed = now;
                return true;
            }
        }

        public IDisposable Hold(TimeProvider clock)
        {
            lock (_gate)
            {
                _running++;
                _lastUsed = clock.GetUtcNow();
            }

            return new Release(this, clock);
        }

        private void End(DateTimeOffset now)
        {
            lock (_gate)
            {
                _running--;
                _lastUsed = now;
            }
        }

        private sealed class Release(CookieUse use, TimeProvider clock) : IDisposable
        {
            private int _released;

            public void Dispose()
            {
                if (Interlocked.Exchange(ref _released, 1) == 0) use.End(clock.GetUtcNow());
            }
        }
    }

    private sealed class NoHold : IDisposable
    {
        public static readonly NoHold Instance = new();

        public void Dispose()
        {
        }
    }

    private static string NewSecret() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(SecretBytes))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
}
