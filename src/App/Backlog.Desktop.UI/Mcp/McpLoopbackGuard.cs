using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace Backlog.Desktop.UI.Mcp;

/// <summary>
/// The two checks local ADR 0012 §1 puts in front of the desktop MCP listener:
/// an <c>Origin</c> that must be absent or loopback, and a bearer token that
/// must match the one the app generated.
/// <para>
/// Pure predicates over header values, deliberately. The listener itself lives
/// in <c>Backlog.Desktop</c>, which is a MAUI <c>WinExe</c> on a platform TFM
/// that no test project in this repository can reference — a MAUI head cannot be
/// composed in a test process at all, which is the same limit
/// <c>TaskSyncClientRegistrationTests</c> works around with a source scan. This
/// is the security surface of the whole feature and there is no precedent for
/// either check anywhere in <c>src/</c>, so it goes where it can be asserted
/// directly rather than only through a running listener.
/// </para>
/// <para>
/// Header plumbing is not here: whether a header arrived twice is the
/// listener's question, and these take the single value it resolved.
/// </para>
/// </summary>
public static class McpLoopbackGuard
{
    /// <summary>The scheme, with its trailing space, exactly as RFC 6750 writes
    /// it.</summary>
    private const string BearerScheme = "Bearer ";

    /// <summary>
    /// Whether a request's <c>Origin</c> is one the server will answer.
    /// <para>
    /// The rule, exactly: <b>absent, empty or whitespace is allowed</b> —
    /// that is what a real MCP client sends, because it is not a browser.
    /// Anything else must parse as an <em>absolute</em> URI whose scheme is
    /// <c>http</c> or <c>https</c> and whose host is a loopback address; the
    /// port is not looked at.
    /// </para>
    /// <para>
    /// So <c>http://localhost</c>, <c>http://localhost:3000</c>,
    /// <c>http://127.0.0.1:5757</c> and <c>http://[::1]:8080</c> pass, and
    /// <c>https://evil.example</c>, <c>http://localhost.evil.example</c>,
    /// <c>http://192.168.1.10</c> and the literal <c>null</c> that a sandboxed
    /// iframe or a <c>file://</c> page sends do not.
    /// </para>
    /// <para>
    /// <b>What it is for.</b> Loopback is why a bearer token is enough, and it
    /// is also why this check is necessary: any page in any browser on this
    /// machine can send a cross-origin request to <c>127.0.0.1:5757</c>, and DNS
    /// rebinding is what would let it read the answer back. Refusing a
    /// cross-origin <c>Origin</c> is what closes that, and it is the reason
    /// §1 pairs the two checks rather than choosing one.
    /// </para>
    /// <para>
    /// The scheme is checked as well as the host, and not only for tidiness:
    /// <c>file://localhost/c:/page.html</c> has a loopback host, and a page opened
    /// off the disk is one of the callers this is keeping out.
    /// </para>
    /// <para>
    /// <b>The header is read, not handed to <see cref="Uri"/>.</b> Two reasons,
    /// and the first is a real hole: <c>Uri</c> canonicalizes the host
    /// <c>loopback</c> <em>into</em> <c>localhost</c> — <c>new Uri("http://loopback").Host</c>
    /// is the string <c>"localhost"</c> — so no test written against <c>Uri.Host</c>
    /// or <see cref="Uri.IsLoopback"/> can tell the two apart, and a page on an
    /// intranet machine reachable as <c>http://loopback</c> would pass. The
    /// second is that an <c>Origin</c> is not a URL: RFC 6454 serializes it as
    /// scheme, host and optional port and nothing else, so a value carrying a
    /// path, a query, a fragment or a userinfo is not an origin any browser
    /// produced and is refused on that alone — <c>http://localhost@evil.example</c>
    /// among them.
    /// </para>
    /// <para>
    /// What is left is an exact rule: the literal name <c>localhost</c>, or a host
    /// that parses as an IP address the platform calls loopback. A name is never
    /// resolved, which is the point — a name that resolves to <c>127.0.0.1</c>
    /// today resolves wherever its owner says tomorrow, and that is what DNS
    /// rebinding <em>is</em>.
    /// </para>
    /// </summary>
    public static bool IsLoopbackOrigin(string? origin)
    {
        if (string.IsNullOrWhiteSpace(origin)) return true;

        var value = origin.Trim();

        // Whitespace inside it is not something a serialization produces, and
        // neither is an empty scheme or an empty authority.
        if (value.Any(char.IsWhiteSpace)) return false;

        var separator = value.IndexOf("://", StringComparison.Ordinal);
        if (separator <= 0) return false;

        var scheme = value[..separator];

        if (!scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
            && !scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var authority = value[(separator + 3)..];

        if (authority.Length == 0) return false;

        // Everything an origin does not have. A trailing slash is the one
        // forgiving case: some clients send `http://localhost:3000/`, and the
        // empty path it names is the same origin.
        if (authority.EndsWith('/')) authority = authority[..^1];

        if (authority.AsSpan().IndexOfAny("/\\?#@") >= 0) return false;

        return IsLoopbackHost(authority);
    }

    /// <summary>
    /// Whether an origin's authority — its host and optional port — is loopback.
    /// <para>
    /// <c>localhost</c> is compared as a whole string and without regard to case,
    /// which is what refuses <c>localhost.evil.example</c>, <c>notlocalhost</c>
    /// and the Unicode homographs that look like it: a letter from another
    /// alphabet is not this letter, and nothing here folds one into the other.
    /// Everything else has to parse as a literal address.
    /// </para>
    /// <para>
    /// The brackets an IPv6 host carries (<c>[::1]:8080</c>) are taken off first,
    /// because <see cref="IPAddress.TryParse(string, out IPAddress)"/> reads an
    /// address rather than an authority's spelling of one — and they are also
    /// what says where the host ends, since the address itself is full of colons.
    /// </para>
    /// </summary>
    private static bool IsLoopbackHost(string authority)
    {
        string host;

        if (authority[0] == '[')
        {
            var close = authority.IndexOf(']', StringComparison.Ordinal);
            if (close < 0) return false;

            host = authority[1..close];

            if (!HasValidPort(authority[(close + 1)..])) return false;
        }
        else
        {
            var colon = authority.LastIndexOf(':');

            host = colon < 0 ? authority : authority[..colon];

            if (colon >= 0 && !HasValidPort(authority[colon..])) return false;
        }

        if (host.Length == 0) return false;

        if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)) return true;

        return IPAddress.TryParse(host, out var address) && IPAddress.IsLoopback(address);
    }

    /// <summary>Whether what follows the host is nothing at all or a colon and
    /// digits. A bare colon, or anything else after one, is not a port and so not
    /// an origin.</summary>
    private static bool HasValidPort(string remainder) =>
        remainder.Length == 0
        || (remainder[0] == ':' && remainder.Length > 1 && remainder[1..].All(char.IsAsciiDigit));

    /// <summary>
    /// Whether the request presented the stored bearer token.
    /// <para>
    /// The scheme name is matched case-insensitively, which is what RFC 9110
    /// requires of every auth scheme; the token itself is matched byte for byte,
    /// because it is a secret rather than a word.
    /// </para>
    /// <para>
    /// <see cref="CryptographicOperations.FixedTimeEquals"/> rather than
    /// <c>==</c>, and that is the point of the method: string equality returns
    /// at the first byte that differs, so how long it took to say no is which
    /// byte it reached. Over loopback a round trip is cheap enough for that to
    /// be an attack somebody actually runs rather than one from a textbook — a
    /// few thousand requests per guessed character.
    /// </para>
    /// <para>
    /// It still reveals whether the two lengths matched, which
    /// <see cref="CryptographicOperations.FixedTimeEquals"/> cannot hide and no
    /// comparison could. The token is a fixed 43 characters, so there is nothing
    /// there to learn.
    /// </para>
    /// <para>
    /// An empty expected token is refused rather than matched. It means the app
    /// has not generated one, and a server that answered every unauthenticated
    /// request while it had no secret would be open for exactly as long as it
    /// took the first caller to arrive.
    /// </para>
    /// </summary>
    public static bool IsAuthorized(string? authorization, string? expected)
    {
        if (string.IsNullOrEmpty(expected)) return false;
        if (authorization is null) return false;
        if (!authorization.StartsWith(BearerScheme, StringComparison.OrdinalIgnoreCase)) return false;

        var presented = authorization[BearerScheme.Length..].Trim();

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(presented),
            Encoding.UTF8.GetBytes(expected));
    }
}
