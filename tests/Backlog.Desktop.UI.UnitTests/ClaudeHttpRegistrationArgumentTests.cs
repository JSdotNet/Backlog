using Backlog.Modules.DevPc.Abstractions;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The argument vector <c>claude mcp add</c> is handed for an HTTP server.
///
/// <para>An argument list is the half of a command surface that can be checked
/// without a machine to run it on, and this one carries a trap that shows up in a
/// diff and never in a suite that mocked the process away: the <c>--</c> in the
/// stdio vector beside it belongs to stdio. It is what separates the flags from
/// the command and its arguments. Put one before a URL and the CLI reads the URL
/// as the command to start, so the registration succeeds and registers a server
/// that can never answer.</para>
///
/// <para>Checked against <c>claude mcp add --help</c>: <c>-t/--transport</c> takes
/// <c>stdio|sse|http</c>, <c>-s/--scope</c> takes <c>local|user|project</c>, and
/// <c>-H/--header</c> repeats.</para>
/// </summary>
public class ClaudeHttpRegistrationArgumentTests
{
    [Fact]
    public void An_http_registration_names_the_transport_the_scope_the_name_and_the_url()
    {
        var spec = DevToolCommands.ClaudeMcpAddHttp(
            "claude",
            "backlog",
            "http://127.0.0.1:5757/mcp",
            [new("Authorization", "Bearer t0ken")]);

        Assert.Equal("claude", spec.Command);
        Assert.Equal(
            [
                "mcp", "add",
                "--transport", "http",
                "--scope", "user",
                "backlog",
                "http://127.0.0.1:5757/mcp",
                "--header", "Authorization: Bearer t0ken"
            ],
            spec.Args);
    }

    /// <inheritdoc cref="ClaudeHttpRegistrationArgumentTests" />
    [Fact]
    public void An_http_registration_carries_no_argument_separator()
    {
        var spec = DevToolCommands.ClaudeMcpAddHttp("claude", "backlog", "http://127.0.0.1:5757/mcp", []);

        Assert.DoesNotContain("--", spec.Args);
    }

    /// <summary>One <c>--header</c> per header, in the order the catalog lists
    /// them. The order is the reason the catalog reads them as a list rather than
    /// a dictionary.</summary>
    [Fact]
    public void Every_header_is_its_own_repeated_flag_in_catalog_order()
    {
        var spec = DevToolCommands.ClaudeMcpAddHttp(
            "claude",
            "backlog",
            "http://127.0.0.1:5757/mcp",
            [
                new("Authorization", "Bearer t0ken"),
                new("X-Backlog-Client", "dev-pc")
            ]);

        Assert.Equal(
            ["--header", "Authorization: Bearer t0ken", "--header", "X-Backlog-Client: dev-pc"],
            spec.Args.TakeLast(4));

        Assert.Equal(2, spec.Args.Count(argument => argument == "--header"));
    }
}
