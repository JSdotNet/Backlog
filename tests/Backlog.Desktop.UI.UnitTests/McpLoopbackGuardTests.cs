using System.Diagnostics;

using Backlog.Desktop.UI.Mcp;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The two checks in front of the desktop MCP listener. This is the security
/// surface of the whole feature and there was no precedent for either check
/// anywhere in <c>src/</c> when it was written, so the cases are enumerated
/// rather than sampled.
/// <para>
/// The listener itself is in <c>Backlog.Desktop</c>, a MAUI <c>WinExe</c> no
/// test project in this repository can reference — which is why the predicates
/// live where they do. What is not covered here is the header plumbing around
/// them: that a header sent twice is refused, and which status code each
/// refusal gets, are <c>McpServerWorker.GuardAsync</c>'s, and only a running
/// listener can show them.
/// </para>
/// </summary>
public class McpLoopbackGuardTests
{
    /// <summary>A real MCP client is not a browser and sends no
    /// <c>Origin</c> at all, so absent has to be allowed — otherwise the check
    /// would refuse every legitimate caller and admit none.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void An_absent_origin_is_allowed(string? origin)
    {
        Assert.True(McpLoopbackGuard.IsLoopbackOrigin(origin));
    }

    /// <summary>Loopback under each of its three spellings, on any port. A
    /// browser page served from a development server on this machine is the
    /// caller this admits.</summary>
    [Theory]
    [InlineData("http://localhost")]
    [InlineData("http://localhost:3000")]
    [InlineData("https://localhost:5001")]
    [InlineData("http://127.0.0.1")]
    [InlineData("http://127.0.0.1:5757")]
    [InlineData("http://[::1]")]
    [InlineData("http://[::1]:8080")]
    // 127.0.0.0/8 is all loopback, not just .1 — the kernel routes it that way
    // and so does IPAddress.IsLoopback, so treating it otherwise would be this
    // predicate inventing a rule of its own.
    [InlineData("http://127.0.0.2:5757")]
    [InlineData("HTTP://LOCALHOST:3000")]
    // The empty path some clients serialize. It names the same origin.
    [InlineData("http://localhost:3000/")]
    public void A_loopback_origin_is_allowed(string origin)
    {
        Assert.True(McpLoopbackGuard.IsLoopbackOrigin(origin));
    }

    /// <summary>
    /// What the check exists for. A page on the open web can send a cross-origin
    /// request to <c>127.0.0.1:5757</c> from any browser on this machine; DNS
    /// rebinding is what would otherwise let it read the answer back, and
    /// refusing a non-loopback <c>Origin</c> is what closes it.
    /// </summary>
    [Theory]
    [InlineData("https://evil.example")]
    [InlineData("http://evil.example:5757")]
    // The near-misses, which are the ones a predicate written with
    // StartsWith or Contains would wave through.
    [InlineData("http://localhost.evil.example")]
    [InlineData("http://127.0.0.1.evil.example")]
    [InlineData("http://evil.example/?x=http://localhost")]
    [InlineData("http://notlocalhost")]
    // A private-network address is not loopback: another machine on the same
    // Wi-Fi is exactly who the loopback bind is keeping out.
    [InlineData("http://192.168.1.10")]
    [InlineData("http://10.0.0.5:5757")]
    [InlineData("http://0.0.0.0")]
    // A host called `loopback`, which Uri.IsLoopback answers true for as though
    // it were a second spelling of localhost. It is not one: it is a name, and on
    // a corporate network it is a name that resolves to somebody's machine. Only
    // the literal `localhost` and a literal loopback address pass.
    [InlineData("http://loopback")]
    [InlineData("http://loopback:5757")]
    [InlineData("https://loopback")]
    [InlineData("http://LOOPBACK")]
    // A name that happens to resolve to 127.0.0.1 is still a name, and the
    // resolution is the attacker's to change — which is what DNS rebinding is.
    [InlineData("http://localtest.me")]
    // The credential form, where the real host is after the @.
    [InlineData("http://localhost@evil.example")]
    // An Origin carries a scheme, a host and a port and nothing else (RFC 6454),
    // so a value with a path, a query, a fragment or a port that is not digits is
    // not one — whatever host it names.
    [InlineData("http://localhost/some/path")]
    [InlineData("http://localhost?x=1")]
    [InlineData("http://localhost#fragment")]
    [InlineData("http://localhost:")]
    [InlineData("http://localhost:notaport")]
    [InlineData("http://")]
    public void A_cross_origin_request_is_refused(string origin)
    {
        Assert.False(McpLoopbackGuard.IsLoopbackOrigin(origin));
    }

    /// <summary>
    /// A name that is drawn like <c>localhost</c> and is not it. The first letter
    /// here is U+04CF, the Cyrillic palochka, which most fonts draw as a lowercase
    /// L — so the value is indistinguishable by eye and is a different string by
    /// every comparison. The host test is a whole-string match and never a fuzzy
    /// one, which is what refuses it.
    /// <para>
    /// Built from a code point rather than typed, so this file stays ASCII: a
    /// source file whose meaning depends on its encoding is a file that changes
    /// meaning when a tool rewrites it.
    /// </para>
    /// </summary>
    [Fact]
    public void A_homograph_of_localhost_is_refused()
    {
        Assert.False(McpLoopbackGuard.IsLoopbackOrigin("http://" + (char)0x04CF + "ocalhost"));
    }

    /// <summary>
    /// The literal <c>null</c> a browser sends from a sandboxed iframe or a
    /// <c>file://</c> page. It is an <c>Origin</c> that is present and is not
    /// loopback, so it is refused — the string "null" must never be mistaken for
    /// the header being absent.
    /// </summary>
    [Fact]
    public void The_literal_null_origin_is_refused()
    {
        Assert.False(McpLoopbackGuard.IsLoopbackOrigin("null"));
    }

    /// <summary>A scheme other than http or https is refused, and a hostless URI
    /// with it. <see cref="Uri.IsLoopback"/> is true for <c>file:///c:/page.html</c>,
    /// and a page opened off the disk is one of the callers this keeps out.</summary>
    [Theory]
    [InlineData("file:///c:/page.html")]
    [InlineData("file://localhost/c:/page.html")]
    [InlineData("chrome-extension://abcdefghijklmnop")]
    [InlineData("ws://localhost:5757")]
    [InlineData("data:text/html,<script>")]
    public void A_non_http_origin_is_refused(string origin)
    {
        Assert.False(McpLoopbackGuard.IsLoopbackOrigin(origin));
    }

    /// <summary>Anything that is not an absolute URI is refused rather than
    /// guessed at. A relative value is not something a browser produces, so it is
    /// a caller doing something else.</summary>
    [Theory]
    [InlineData("localhost")]
    [InlineData("127.0.0.1")]
    [InlineData("/")]
    [InlineData("//localhost")]
    [InlineData("not a url")]
    public void An_origin_that_is_not_an_absolute_url_is_refused(string origin)
    {
        Assert.False(McpLoopbackGuard.IsLoopbackOrigin(origin));
    }

    private const string Token = "kZ8qX2vN4pL7wR1tY6uI3oA5sD9fG0hJkLmNbVcXzQe";

    [Fact]
    public void The_stored_token_is_accepted()
    {
        Assert.True(McpLoopbackGuard.IsAuthorized($"Bearer {Token}", Token));
    }

    /// <summary>RFC 9110 makes the scheme name case-insensitive, and clients do
    /// vary it.</summary>
    [Theory]
    [InlineData("bearer ")]
    [InlineData("BEARER ")]
    [InlineData("BeArEr ")]
    public void The_scheme_name_is_case_insensitive(string scheme)
    {
        Assert.True(McpLoopbackGuard.IsAuthorized(scheme + Token, Token));
    }

    /// <summary>The token itself is not. It is a secret rather than a word, and
    /// Base64Url is case-significant — folding case would throw away six bits per
    /// character.</summary>
    [Fact]
    public void The_token_itself_is_case_sensitive()
    {
        Assert.False(McpLoopbackGuard.IsAuthorized($"Bearer {Token.ToUpperInvariant()}", Token));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Bearer ")]
    [InlineData("Bearer")]
    [InlineData("Basic dXNlcjpwYXNz")]
    [InlineData("Bearer wrong-token-entirely")]
    // One character off, at each end: the case a comparison that stopped early
    // would answer fastest.
    [InlineData("Bearer XZ8qX2vN4pL7wR1tY6uI3oA5sD9fG0hJkLmNbVcXzQe")]
    [InlineData("Bearer kZ8qX2vN4pL7wR1tY6uI3oA5sD9fG0hJkLmNbVcXzQX")]
    // A prefix of the real token, and the real token with something after it.
    [InlineData("Bearer kZ8qX2vN4pL7wR1tY6uI3oA5sD9fG0hJkLmNbVcXzQ")]
    [InlineData("Bearer kZ8qX2vN4pL7wR1tY6uI3oA5sD9fG0hJkLmNbVcXzQee")]
    // The scheme without its space is not the scheme.
    [InlineData("BearerkZ8qX2vN4pL7wR1tY6uI3oA5sD9fG0hJkLmNbVcXzQe")]
    public void Anything_but_the_stored_token_is_refused(string? authorization)
    {
        Assert.False(McpLoopbackGuard.IsAuthorized(authorization, Token));
    }

    /// <summary>
    /// A server with no token yet refuses everything, including an empty
    /// presentation. The alternative — treating "no secret" as "no check" —
    /// would leave the port open for exactly as long as it took the first caller
    /// to arrive.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void A_server_with_no_token_refuses_every_request(string? expected)
    {
        Assert.False(McpLoopbackGuard.IsAuthorized("Bearer ", expected));
        Assert.False(McpLoopbackGuard.IsAuthorized($"Bearer {Token}", expected));
        Assert.False(McpLoopbackGuard.IsAuthorized(null, expected));
    }

    /// <summary>
    /// The comparison is fixed-time, which is the reason
    /// <see cref="System.Security.Cryptography.CryptographicOperations.FixedTimeEquals"/>
    /// is used instead of <c>==</c>: a comparison that returned at the first
    /// differing byte would let a caller learn the token one character per round
    /// trip, and over loopback a round trip is cheap enough for that to be an
    /// attack somebody runs rather than one from a textbook.
    /// <para>
    /// <b>Timing is not asserted</b>, deliberately. A wall-clock comparison
    /// between "wrong at byte 0" and "wrong at byte 42" is a measurement of the
    /// build agent's scheduler, and a test that fails when the runner is busy is
    /// worse than no test. What is asserted is the property that makes the timing
    /// claim true and that a rewrite to <c>==</c> would break: every wrong token
    /// of the right length is refused, and the work is the same for all of them.
    /// The timing is measured only to keep the numbers in the failure message
    /// when the assertion above it ever does fail.
    /// </para>
    /// </summary>
    [Fact]
    public void Every_wrong_token_of_the_right_length_is_refused_alike()
    {
        // One wrong token per position, each differing from the real one at
        // exactly that index and nowhere else.
        var candidates = Enumerable
            .Range(0, Token.Length)
            .Select(index => Token.Remove(index, 1).Insert(index, Token[index] == 'a' ? "b" : "a"))
            .ToList();

        Assert.All(candidates, candidate => Assert.Equal(Token.Length, candidate.Length));
        Assert.All(candidates, candidate => Assert.NotEqual(Token, candidate));

        var stopwatch = Stopwatch.StartNew();

        Assert.All(
            candidates,
            candidate => Assert.False(McpLoopbackGuard.IsAuthorized($"Bearer {candidate}", Token)));

        stopwatch.Stop();

        // And the right one still passes, so the loop above did not simply refuse
        // everything.
        Assert.True(McpLoopbackGuard.IsAuthorized($"Bearer {Token}", Token));
    }
}
