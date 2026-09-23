using Backlog.Modules.DevPc.Abstractions;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The catalog spells an HTTP entry the way a person pastes it out of a
/// <c>.mcp.json</c> — <c>${BACKLOG_MCP_PORT}</c> in the URL and
/// <c>${BACKLOG_MCP_TOKEN}</c> in the header — and something has to turn that
/// into the two strings a registration is made of.
///
/// <para>These are the facts that expander rests on. The load-bearing one is the
/// last: the vocabulary is closed. The catalog is a hand-edited file in a synced
/// folder, so a general <c>${VAR}</c> expander would be a way to put any
/// environment variable of the machine that syncs it onto a command line, and
/// this file asserts that it is not one.</para>
/// </summary>
public class McpEndpointExpansionTests
{
    private const string EndpointUrl = "http://127.0.0.1:${BACKLOG_MCP_PORT}/mcp";
    private const string AuthorizationHeader = "Bearer ${BACKLOG_MCP_TOKEN}";

    /// <summary>The port is the machine's, not the file's: whatever this PC's
    /// MCP server is configured to listen on is what the registration is
    /// written with.</summary>
    [Theory]
    [InlineData(5757)]
    [InlineData(6000)]
    public void The_port_comes_from_this_machines_server(int port)
    {
        var source = new FakeEndpointSource(port);

        var expanded = McpPlaceholders.Expand(EndpointUrl, McpPlaceholders.Describing(source));

        Assert.True(expanded.Resolved);
        Assert.Null(expanded.Problem);
        Assert.Equal($"http://127.0.0.1:{port}/mcp", expanded.Text);
    }

    /// <summary>
    /// <c>${NAME:-default}</c> is accepted so that a line copied out of a
    /// <c>.mcp.json</c> works unaltered — and the source wins wherever it
    /// answers, because the default in the file is what that file guessed and the
    /// source is what this machine is actually doing.
    /// </summary>
    [Fact]
    public void A_default_in_the_file_loses_to_the_machine_that_answers()
    {
        var expanded = McpPlaceholders.Expand(
            "http://127.0.0.1:${BACKLOG_MCP_PORT:-5757}/mcp",
            McpPlaceholders.Describing(new FakeEndpointSource(6000)));

        Assert.Equal("http://127.0.0.1:6000/mcp", expanded.Text);
        Assert.True(expanded.Resolved);
    }

    /// <summary>And is what is left when nothing answers, which is the whole
    /// point of writing one: the literal, never the placeholder.</summary>
    [Fact]
    public void A_default_is_used_when_nothing_answers()
    {
        var expanded = McpPlaceholders.Expand("http://127.0.0.1:${BACKLOG_MCP_PORT:-5757}/mcp", _ => null);

        Assert.Equal("http://127.0.0.1:5757/mcp", expanded.Text);
        Assert.True(expanded.Resolved);
    }

    /// <summary>The token is the fake's, and it is minted exactly once — the
    /// resolver is the only thing that may ask for one, and asking twice would be
    /// two writes of a file for one registration.</summary>
    [Fact]
    public void The_apply_resolver_mints_the_token_once()
    {
        var source = new FakeEndpointSource(5757, "t0ken");

        var expanded = McpPlaceholders.Expand(AuthorizationHeader, McpPlaceholders.Applying(source));

        Assert.Equal("Bearer t0ken", expanded.Text);
        Assert.True(expanded.Resolved);
        Assert.Equal(1, source.TokensMinted);
    }

    /// <summary>
    /// And the describing resolver mints none. Describing a row is something the
    /// pane does on every refresh, for every host, whether or not anybody ever
    /// presses Re-register — minting a secret to draw a table with would persist
    /// a token nobody asked for.
    ///
    /// <para>The placeholder is left exactly as it was, and the expansion says so,
    /// so nothing downstream can mistake the literal for a URL it may
    /// register.</para>
    /// </summary>
    [Fact]
    public void The_describing_resolver_mints_no_token_at_all()
    {
        var source = new FakeEndpointSource(5757, "t0ken");

        var expanded = McpPlaceholders.Expand(AuthorizationHeader, McpPlaceholders.Describing(source));

        Assert.Equal(0, source.TokensMinted);
        Assert.False(expanded.Resolved);
        Assert.Equal(AuthorizationHeader, expanded.Text);
        Assert.DoesNotContain("t0ken", expanded.Text, StringComparison.Ordinal);
        Assert.NotNull(expanded.Problem);
    }

    /// <summary>
    /// The security fact this expander exists in this shape for.
    ///
    /// <para>The catalog is a hand-edited JSON file in a folder that syncs between
    /// machines, and a registration built from it becomes a command line. An
    /// expander that read <c>Environment.GetEnvironmentVariable</c> would let
    /// anything that file names — a key, a connection string, a password sitting
    /// in the user's environment — be lifted onto that command line and into the
    /// host's own config. So the vocabulary is closed to two names, and a real
    /// environment variable by the placeholder's name is set here to prove the
    /// difference is not accidental.</para>
    ///
    /// <para>The unrecognised placeholder is left literal and the row is failed by
    /// name. Never half-expanded and registered anyway: a URL still carrying
    /// <c>${</c> is not a URL.</para>
    /// </summary>
    [Fact]
    public void An_unknown_placeholder_is_never_read_from_the_environment()
    {
        const string Name = "SOMETHING_ELSE";
        const string Secret = "a-value-from-the-environment";

        var before = Environment.GetEnvironmentVariable(Name);
        Environment.SetEnvironmentVariable(Name, Secret);

        try
        {
            var expanded = McpPlaceholders.Expand(
                "http://127.0.0.1:5757/${SOMETHING_ELSE}",
                McpPlaceholders.Applying(new FakeEndpointSource(5757)));

            Assert.DoesNotContain(Secret, expanded.Text, StringComparison.Ordinal);
            Assert.Equal("http://127.0.0.1:5757/${SOMETHING_ELSE}", expanded.Text);

            Assert.False(expanded.Resolved);
            Assert.Equal(Name, expanded.Unresolved);
            Assert.NotNull(expanded.Problem);
            Assert.Contains(Name, expanded.Problem, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable(Name, before);
        }
    }

    /// <summary>
    /// A name this build does not fill in, with a default written beside it, is
    /// that default.
    ///
    /// <para>Which is the one thing the paragraph above does <em>not</em> give
    /// away: the default is a literal out of the same file, and using it reads
    /// nothing off this machine. Refusing it made
    /// <c>${SOMETHING:-5757}</c> — a line out of somebody else's
    /// <c>.mcp.json</c>, which is the premise the whole syntax is accepted
    /// for — fail a row that had its own answer written into it.</para>
    /// </summary>
    [Fact]
    public void An_unknown_name_with_a_default_beside_it_is_that_default()
    {
        const string Name = "SOMETHING_ELSE";
        const string Secret = "a-value-from-the-environment";

        var before = Environment.GetEnvironmentVariable(Name);
        Environment.SetEnvironmentVariable(Name, Secret);

        try
        {
            var expanded = McpPlaceholders.Expand(
                "http://127.0.0.1:${SOMETHING_ELSE:-5757}/mcp",
                McpPlaceholders.Applying(new FakeEndpointSource(6000)));

            Assert.Equal("http://127.0.0.1:5757/mcp", expanded.Text);
            Assert.True(expanded.Resolved);

            // And emphatically not the environment's, which is what a general
            // expander would have reached for the moment the name went unmatched.
            Assert.DoesNotContain(Secret, expanded.Text, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable(Name, before);
        }
    }

    /// <summary>
    /// An empty default is not a resolution.
    ///
    /// <para><c>${BACKLOG_MCP_PORT:-}</c> against a source that does not answer
    /// produced <c>""</c> and called the expansion resolved — which describes the
    /// row as <c>http://127.0.0.1:/mcp</c>, reports no problem against it, and
    /// offers to register it. The pattern matches an empty default because
    /// <c>[^}]*</c> matches empty; what makes it not a value is read here rather
    /// than left to the caller, because every caller only looks at
    /// <see cref="McpExpansion.Resolved"/>.</para>
    /// </summary>
    [Theory]
    [InlineData("http://127.0.0.1:${BACKLOG_MCP_PORT:-}/mcp")]
    [InlineData("http://127.0.0.1:${BACKLOG_MCP_PORT:-   }/mcp")]
    public void An_empty_default_is_not_a_resolution(string url)
    {
        var expanded = McpPlaceholders.Expand(url, _ => null);

        Assert.False(expanded.Resolved);
        Assert.Equal(McpPlaceholders.PortName, expanded.Unresolved);
        Assert.NotNull(expanded.Problem);

        // Left exactly as the file spelled it, so nothing downstream can mistake
        // it for an address.
        Assert.Equal(url, expanded.Text);
    }

    /// <summary>The same rule one level up: a resolver that answered with blank
    /// has not answered. A port is a number or it is nothing.</summary>
    [Fact]
    public void A_blank_answer_from_the_source_is_no_answer()
    {
        var expanded = McpPlaceholders.Expand(EndpointUrl, _ => string.Empty);

        Assert.False(expanded.Resolved);
        Assert.Equal(McpPlaceholders.PortName, expanded.Unresolved);
        Assert.Equal(EndpointUrl, expanded.Text);
    }

    /// <summary>Text with nothing to expand in it comes back as itself, which is
    /// every catalog entry written before any of this existed.</summary>
    [Fact]
    public void Text_with_no_placeholder_in_it_is_left_alone()
    {
        var expanded = McpPlaceholders.Expand("http://127.0.0.1:5757/mcp", _ => null);

        Assert.Equal("http://127.0.0.1:5757/mcp", expanded.Text);
        Assert.True(expanded.Resolved);
    }

    /// <summary>A stand-in for the worker, which lives in the desktop head no
    /// test project references. It counts the mints, because "how many times was
    /// a secret written" is the fact two of these tests are about.</summary>
    private sealed class FakeEndpointSource(int port, string token = "t0ken") : IMcpEndpointSource
    {
        public int TokensMinted { get; private set; }

        public bool Enabled => true;

        public int Port => port;

        public string? Unavailable => null;

        public string EnsureToken()
        {
            TokensMinted++;
            return token;
        }

        public event Action? Changed
        {
            add { }
            remove { }
        }
    }
}
