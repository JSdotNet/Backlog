using System.Text.RegularExpressions;

namespace Backlog.ArchitectureTests;

/// <summary>
/// The desktop head's half of the two-mechanism <c>mcpServers</c> array, checked
/// by reading its source.
///
/// <para>A source scan for the reason <see cref="ProcessLaunchTests"/> is one: the
/// file these rules are about lives in <c>src/App/Backlog.Desktop</c>, the MAUI
/// head, which no test project references or can reference. The decisions
/// themselves were pushed into <c>Backlog.Modules.DevPc.Abstractions</c> so that
/// one set of unit tests covers both services — what is left here is that this
/// service actually goes through them.</para>
///
/// <para>Two facts, and both of them were bugs. The enumeration reached for a
/// <c>packageId</c> and skipped any entry without one, so a server registered by
/// the command it declares — which this repository's own catalog ships — was
/// dropped before a row existed. And the action shelled out to <c>dotnet tool</c>
/// with whatever that reach returned, which for such an entry would be a blank
/// package id: an install of nothing, reported as the row's own failure.</para>
/// </summary>
public class DevToolMcpMechanismTests
{
    private const string ServiceFile = "src/App/Backlog.Desktop/Services/DevToolService.cs";

    /// <summary>The argument lists that ask <c>dotnet</c> about one package.
    ///
    /// <para><c>tool list</c> is deliberately not among them: it is one inventory
    /// read for the whole catalog rather than a launch on any row's behalf, and
    /// the listing already declines to run it when no server in the catalog is a
    /// .NET tool at all.</para></summary>
    private static readonly string[] PackageLaunches =
    [
        "\"tool\", \"install\"",
        "\"tool\", \"update\"",
        "\"tool\", \"uninstall\"",
        "\"tool\", \"search\""
    ];

    /// <summary>What a member has to name for a package launch inside it to be
    /// one that cannot happen with a blank id.</summary>
    private const string MechanismCheck = "DevToolMcpMechanism.DotNetTool";

    [Fact]
    public void The_desktop_head_reads_its_mcp_rows_through_the_shared_reader()
    {
        var source = Source();

        Assert.Contains("DevToolConfiguration.ReadMcpServer(", source, StringComparison.Ordinal);

        // The two reaches that dropped the row and then acted with what they
        // found. Both are the abstraction's job now, and the reader answers for an
        // entry of either mechanism.
        Assert.DoesNotContain("GetString(server, \"packageId\")", source, StringComparison.Ordinal);
        Assert.DoesNotContain("GetRequiredString(server, \"packageId\")", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_dotnet_tool_launch_sits_behind_a_mechanism_check()
    {
        var members = Members().ToList();
        var launching = members
            .Where(member => PackageLaunches.Any(launch => member.Body.Contains(launch, StringComparison.Ordinal)))
            .ToList();

        // Otherwise the rule below passes by finding nothing — which is exactly
        // what it would do if the strings it looks for were ever reworded.
        Assert.NotEmpty(launching);

        var offenders = launching
            .Where(member => !member.Body.Contains(MechanismCheck, StringComparison.Ordinal))
            .Select(member => member.Declaration)
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "These members run `dotnet tool` against one package without naming the mechanism that says the "
            + $"entry has one, so a command-registered MCP server reaches them with a blank package id. Take a "
            + $"DevToolMcpServer and check `{MechanismCheck}` before the launch:\n"
            + string.Join('\n', offenders));
    }

    private static string Source() => File.ReadAllText(Path.Combine(Repository.Root.FullName, ServiceFile));

    /// <summary>The file's members, each with the text of its own body — split on
    /// a declaration at class indentation, which is how every member in this file
    /// is written.</summary>
    private static IEnumerable<Member> Members()
    {
        var declaration = new Regex(@"^    (private|public|internal|protected)\b.*$");
        var lines = Source().Split('\n');
        var current = "class DevToolService";
        var body = new List<string>();

        foreach (var line in lines)
        {
            if (declaration.IsMatch(line.TrimEnd('\r')))
            {
                yield return new Member(current, string.Join('\n', body));

                current = line.Trim();
                body.Clear();
            }

            body.Add(line);
        }

        yield return new Member(current, string.Join('\n', body));
    }

    private sealed record Member(string Declaration, string Body);
}
