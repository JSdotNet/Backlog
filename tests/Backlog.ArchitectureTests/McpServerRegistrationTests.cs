namespace Backlog.ArchitectureTests;

/// <summary>
/// The MCP server's own version of the two rules
/// <see cref="TaskSyncClientRegistrationTests"/> holds, plus the one that is
/// specific to it: where the <c>Microsoft.AspNetCore.App</c> framework reference
/// may appear.
/// <para>
/// <c>Backlog.HostComposition.UnitTests</c> starts the web harness for real and
/// proves the tools can be constructed out of it. It cannot do that for
/// <c>src/App/Backlog.Desktop/MauiProgram.cs</c> — a MAUI head cannot be composed
/// in a test process — so these are text scans over source, deliberately narrow,
/// and they are the only guard that head has.
/// </para>
/// </summary>
public class McpServerRegistrationTests
{
    private static readonly string[] Roots = ["src/App", "src/Harness"];

    /// <summary>
    /// <c>McpServerWorker</c> is a singleton whose constructor binds the port, so
    /// nothing starts it except the first resolve — the same arrangement, and the
    /// same silent failure, as the four workers beside it. A head that registers
    /// it and never asks for it builds, starts, opens every screen, and listens
    /// on nothing at all.
    /// </summary>
    [Fact]
    public void The_head_that_registers_the_mcp_worker_also_resolves_it()
    {
        var files = CompositionRoots().ToList();

        Assert.NotEmpty(files);

        var offenders = files
            // The angle brackets rather than a bare name, the way the sync rules
            // match: every comment about this worker names the type too, and a
            // bare substring would read an explanation as the call it explains.
            .Where(file => file.Text.Contains("TryAddSingleton<McpServerWorker>", StringComparison.Ordinal))
            .Where(file => !file.Text.Contains("GetRequiredService<McpServerWorker>()", StringComparison.Ordinal))
            .Select(file => file.RelativePath)
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "These composition roots register the MCP listener and never resolve it, so the port is never "
            + "bound - the head reports the feature as on and no session can reach it. Ask for it once "
            + "after Build():\n" + string.Join('\n', offenders));
    }

    /// <summary>
    /// A host that maps the endpoint has to register the server behind it.
    /// <c>MapMcp</c> with no <c>AddMcpServer</c> under it is a route that
    /// resolves nothing, and it fails inside <c>Build()</c> rather than at the
    /// call.
    /// </summary>
    [Fact]
    public void Every_host_that_maps_the_endpoint_also_registers_the_server()
    {
        var files = CompositionRoots().ToList();

        Assert.NotEmpty(files);

        var offenders = files
            .Where(file => file.Text.Contains("MapMcp(", StringComparison.Ordinal))
            .Where(file => !file.Text.Contains("AddBacklogMcpServer()", StringComparison.Ordinal))
            .Select(file => file.RelativePath)
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "These hosts map the MCP endpoint without registering the server behind it. Call "
            + "AddBacklogMcpServer() - the registration both hosts share, so the tools and their feature "
            + "gates cannot differ between them:\n" + string.Join('\n', offenders));
    }

    /// <summary>
    /// <b>The rule this suite exists for.</b> Local ADR 0012 §2 puts
    /// <c>Microsoft.AspNetCore.App</c> in <c>Backlog.Desktop.csproj</c> "and
    /// nowhere else", because eleven projects compile into the
    /// <c>net10.0-android</c> head and that head cannot take it — it fails
    /// NETSDK1082, which is exactly how an AspNetCore instrumentation package
    /// once broke it through <c>ServiceDefaults</c>.
    /// <para>
    /// Worth a rule rather than a comment because nothing else catches it:
    /// <c>dotnet test</c> does not compile the mobile head, so a green build and
    /// a green suite sit happily on top of a broken one. A <c>Microsoft.NET.Sdk.Web</c>
    /// project has the reference implicitly and does not declare it, which is why
    /// this reads declarations rather than effective references — the harnesses
    /// are exempt by being web SDK projects, not by being listed here.
    /// </para>
    /// </summary>
    [Fact]
    public void The_aspnetcore_framework_reference_appears_in_one_project_only()
    {
        var offenders = ProjectFiles()
            .Where(file => file.Text.Contains("\"Microsoft.AspNetCore.App\"", StringComparison.Ordinal))
            .Where(file => !file.RelativePath.EndsWith("Backlog.Desktop.csproj", StringComparison.Ordinal))
            .Select(file => file.RelativePath)
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "These projects declare the Microsoft.AspNetCore.App framework reference. Local ADR 0012 puts "
            + "it in src/App/Backlog.Desktop/Backlog.Desktop.csproj and nowhere else: every other project "
            + "under src/ is, or may become, one the net10.0-android head compiles, and that head fails "
            + "NETSDK1082 on this reference. `dotnet test` does not build that head, so nothing else in "
            + "this suite would tell you:\n" + string.Join('\n', offenders));
    }

    /// <summary>
    /// The package that carries the framework reference, held to the same rule
    /// one level up. <c>ModelContextProtocol.AspNetCore</c> declares
    /// <c>Microsoft.AspNetCore.App</c> in its own nuspec, so referencing it from
    /// a project on the mobile path breaks that head exactly as declaring the
    /// reference would — and would do it without the word "FrameworkReference"
    /// appearing anywhere.
    /// <para>
    /// The transport-neutral <c>ModelContextProtocol.Core</c> and the
    /// dependency-injection <c>ModelContextProtocol</c> carry no framework
    /// reference and are deliberately not covered: they are what let the tool
    /// library and the shared registration live outside the two hosts.
    /// </para>
    /// </summary>
    [Fact]
    public void The_aspnetcore_half_of_the_mcp_sdk_is_referenced_only_by_the_two_hosts()
    {
        string[] allowed =
        [
            "src/App/Backlog.Desktop/Backlog.Desktop.csproj",
            "src/Harness/Backlog.Desktop.WebHarness/Backlog.Desktop.WebHarness.csproj"
        ];

        var offenders = ProjectFiles()
            .Where(file => file.Text.Contains("\"ModelContextProtocol.AspNetCore\"", StringComparison.Ordinal))
            .Where(file => !allowed.Contains(file.RelativePath, StringComparer.Ordinal))
            .Select(file => file.RelativePath)
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "These projects reference ModelContextProtocol.AspNetCore, which carries the "
            + "Microsoft.AspNetCore.App framework reference in its nuspec. Only the two hosts that "
            + "actually listen may take it. For tools use ModelContextProtocol.Core, and for the shared "
            + "registration ModelContextProtocol - neither declares a framework reference:\n"
            + string.Join('\n', offenders));
    }

    /// <summary>
    /// The listener is never released while the worker's own lock is held.
    /// <para>
    /// <c>Release</c> is <c>Host.Dispose()</c>, which is
    /// <c>StopAsync().GetAwaiter().GetResult()</c> — it blocks until Kestrel has
    /// closed the socket, and with a session holding the Streamable-HTTP stream
    /// open that is the whole shutdown timeout. <c>ApplyGates</c> runs on the
    /// thread that raised the event, which for <c>IAppFeatureSettings.Changed</c>
    /// is the Settings screen's. Releasing there froze the screen somebody had
    /// just clicked, and did it holding <c>_gate</c>, so a concurrent
    /// <c>McpChanged</c> queued behind the freeze.
    /// </para>
    /// <para>
    /// <c>Dispose</c> is the one place a synchronous release is right — the port
    /// has to be free before the process goes, and there is no screen left to
    /// freeze — and it releases outside the lock. So the rule is about the lock
    /// rather than about the method: no <c>Release</c> inside any
    /// <c>lock (_gate)</c> block.
    /// </para>
    /// <para>
    /// A source scan because there is no alternative: the worker lives in the
    /// MAUI head, which no test project in this repository can reference. The same
    /// limit, and the same answer, as the two rules above.
    /// </para>
    /// </summary>
    [Fact]
    public void The_listener_is_never_released_while_the_gate_is_held()
    {
        var text = WorkerSource();

        var blocks = LockedBlocks(text, "lock (_gate)").ToList();

        // Against the scan quietly finding nothing — a renamed gate would make
        // every assertion below true of an empty list.
        Assert.True(
            blocks.Count >= 3,
            $"Only {blocks.Count} `lock (_gate)` blocks were found in McpServerWorker.cs. If the gate was "
            + "renamed, rename it here too; this rule is not worth having if it reads nothing.");

        var offenders = blocks
            .Where(block => block.Contains("Release(", StringComparison.Ordinal))
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "McpServerWorker releases a listener inside lock (_gate). Release is Host.Dispose(), which blocks "
            + "for the shutdown timeout while a session holds the stream open - on the thread that raised the "
            + "event, which for a feature flip is the one drawing the Settings screen, and with the gate held so "
            + "the next change queues behind it. Decide under the lock and hand the work to the transition "
            + "chain:\n" + string.Join("\n---\n", offenders));
    }

    /// <summary>
    /// The listener binds both spellings of loopback.
    /// <para>
    /// <c>IPAddress.Loopback</c> is <c>127.0.0.1</c> and nothing else, and on
    /// Windows <c>localhost</c> resolves to <c>::1</c> first — so a registration
    /// written by hand against <c>http://localhost:5757/mcp</c> is refused while
    /// the app is running, listening, and reporting itself as on. Local ADR 0012
    /// §1 forbids binding <c>0.0.0.0</c> or <c>::</c>; it asks for loopback, and
    /// loopback has two addresses.
    /// </para>
    /// </summary>
    [Fact]
    public void The_listener_binds_both_loopback_addresses_and_neither_wildcard()
    {
        var text = WorkerSource();

        Assert.Contains("options.Listen(IPAddress.Loopback", text, StringComparison.Ordinal);

        Assert.True(
            text.Contains("options.Listen(IPAddress.IPv6Loopback", StringComparison.Ordinal),
            "McpServerWorker listens on 127.0.0.1 only. On Windows localhost resolves to ::1 first, so a "
            + "registration naming localhost cannot reach it. Listen on IPAddress.IPv6Loopback too, guarded by "
            + "Socket.OSSupportsIPv6 so a machine with IPv6 switched off still starts.");

        Assert.True(
            text.Contains("Socket.OSSupportsIPv6", StringComparison.Ordinal),
            "The IPv6 endpoint is registered unguarded. Listen only records an endpoint - the failure lands at "
            + "the bind, and takes the working IPv4 listener down with it.");

        Assert.DoesNotContain("IPAddress.Any", text, StringComparison.Ordinal);
        Assert.DoesNotContain("IPAddress.IPv6Any", text, StringComparison.Ordinal);
    }

    /// <summary><c>McpServerWorker.cs</c>, which is the subject of the two rules
    /// above and the one file in the MAUI head this suite reads in
    /// full.</summary>
    private static string WorkerSource()
    {
        var file = new FileInfo(Path.Combine(
            Repository.Root.FullName,
            "src", "App", "Backlog.Desktop", "Mcp", "McpServerWorker.cs"));

        Assert.True(file.Exists, $"{file.FullName} should exist - the rules here are about that file.");

        return File.ReadAllText(file.FullName);
    }

    /// <summary>The body of every block opening with <paramref name="opening"/>,
    /// by brace matching. Braces inside a string literal would fool it; there are
    /// none in the file it reads, and a scan that tried to tokenize C# would be a
    /// compiler.</summary>
    private static IEnumerable<string> LockedBlocks(string text, string opening)
    {
        var at = text.IndexOf(opening, StringComparison.Ordinal);

        while (at >= 0)
        {
            var open = text.IndexOf('{', at);

            if (open < 0) yield break;

            var depth = 0;
            var index = open;

            for (; index < text.Length; index++)
            {
                if (text[index] == '{') depth++;
                else if (text[index] == '}' && --depth == 0) break;
            }

            yield return text[open..Math.Min(index + 1, text.Length)];

            at = text.IndexOf(opening, index, StringComparison.Ordinal);
        }
    }

    /// <summary>Every <c>MauiProgram.cs</c> or <c>Program.cs</c> under the app
    /// heads and the harnesses — the files that assemble a service collection for
    /// a runnable host. The same set
    /// <see cref="TaskSyncClientRegistrationTests"/> reads, and read the same
    /// way.</summary>
    private static IEnumerable<(string RelativePath, string Text)> CompositionRoots()
    {
        foreach (var root in Roots)
        {
            var folder = new DirectoryInfo(Path.Combine([Repository.Root.FullName, .. root.Split('/')]));
            if (!folder.Exists) continue;

            foreach (var file in folder.EnumerateFiles("*.cs", SearchOption.AllDirectories))
            {
                if (IsBuildOutput(file)) continue;
                if (file.Name is not ("MauiProgram.cs" or "Program.cs")) continue;

                yield return (Relative(file), File.ReadAllText(file.FullName));
            }
        }
    }

    /// <summary>Every project file under <c>src/</c>. Not only the heads: the
    /// whole point of the two rules above is that the reference must not appear
    /// somewhere nobody was looking.</summary>
    private static IEnumerable<(string RelativePath, string Text)> ProjectFiles()
    {
        var source = new DirectoryInfo(Path.Combine(Repository.Root.FullName, "src"));

        Assert.True(source.Exists, "src/ should exist.");

        foreach (var file in source.EnumerateFiles("*.csproj", SearchOption.AllDirectories))
        {
            if (IsBuildOutput(file)) continue;

            yield return (Relative(file), File.ReadAllText(file.FullName));
        }
    }

    private static bool IsBuildOutput(FileInfo file) =>
        file.FullName.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
        || file.FullName.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}");

    private static string Relative(FileInfo file) =>
        Path.GetRelativePath(Repository.Root.FullName, file.FullName).Replace('\\', '/');
}
