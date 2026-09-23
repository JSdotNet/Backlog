using System.Net;
using System.Text.Json.Nodes;

using Backlog.Desktop.UI.Extensions;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The <c>mcpServers</c> array holds two kinds of server, and only one of them
/// ships as a NuGet package.
///
/// <para>A server installed as a global .NET tool is identified by its
/// <c>packageId</c>, and that was taken for the array's identity everywhere: the
/// <c>mcp:</c> key's property, the per-PC override's property, the display name,
/// and both version lookups. The Aspire CLI server is the other kind — a
/// <c>command</c> and its arguments, registered rather than installed — and this
/// repository's own catalog ships one, so an array that could only be addressed
/// by package id was an array that dropped it silently.</para>
///
/// <para>These are the facts a row of the second kind rests on, pinned in the
/// abstraction both the desktop head and the harness read them through: the
/// desktop head lives in <c>src/App/Backlog.Desktop</c>, which no test project
/// references, so a decision that has to be proven by a test has to be made
/// here.</para>
/// </summary>
public class DevToolMcpServerTests
{
    /// <summary>
    /// The URL the repository's own catalog has to carry, <em>derived</em> from
    /// the two things that decide it rather than written out a second time.
    ///
    /// <para>This is the whole of the fix to two sources of truth. The hosts
    /// serve the endpoint at <see cref="BacklogMcpServerRegistration.EndpointPath"/>
    /// and the catalog names a path in a string; with both spelled out, moving
    /// the constant left the catalog pointing at a 404 and nothing said so — not
    /// the build, not the drift check (both sides agreed on the stale string),
    /// and not a test, because the test held its own copy of the literal.
    /// Composed here, moving the constant without moving the catalog fails this
    /// file.</para>
    /// </summary>
    private static readonly string BacklogServerUrl =
        $"http://{IPAddress.Loopback}:${{{McpPlaceholders.PortName}}}{BacklogMcpServerRegistration.EndpointPath}";

    /// <summary>The entry this whole file is about, exactly as
    /// <c>.tools/ai-tools.json</c> ships it.</summary>
    private const string CommandRegisteredCatalog = """
        {
          "plugins": [],
          "mcpServers": [
            { "name": "aspire", "command": "aspire", "args": [ "agent", "mcp" ], "enabled": true }
          ]
        }
        """;

    /// <summary>The third mechanism, spelled the way a person pastes it out of a
    /// <c>.mcp.json</c>: a transport, a URL with a placeholder and a default in
    /// it, and headers in the order the file lists them.</summary>
    private const string HttpCatalog = """
        {
          "mcpServers": [
            {
              "name": "backlog",
              "type": "http",
              "url": "http://127.0.0.1:${BACKLOG_MCP_PORT:-5757}/mcp",
              "headers": {
                "Authorization": "Bearer ${BACKLOG_MCP_TOKEN}",
                "X-Backlog-Client": "dev-pc"
              },
              "enabled": true
            }
          ]
        }
        """;

    /// <summary>
    /// AC2. The row's key has to survive the round trip through the per-PC file,
    /// which is the one boundary an identity can silently fail at: the merge in
    /// <see cref="DevToolConfiguration.ReadAsync"/> drops a per-PC entry with no
    /// catalog entry of the same id property behind it, so an override written
    /// under a property the catalog entry does not carry is a saved setting that
    /// reads back as never saved.
    /// </summary>
    /// <summary>
    /// One odd value in a hand-written entry is that row's finding, not the
    /// list's. Read with an indexer that throws on a value node and a
    /// <c>GetValue</c> that throws on a number, a <c>"claude": "name"</c> or an
    /// <c>args</c> holding a port number took every row off the Tools pane
    /// behind "could not be checked".
    /// </summary>
    [Fact]
    public void A_registration_with_odd_values_reads_as_blank_rather_than_throwing()
    {
        var server = JsonNode.Parse("""
            { "name": "odd", "claude": { "command": "npx", "args": [ "-y", 8080, null, "serve" ] } }
            """)!;
        var claude = DevToolConfiguration.McpRegistrationSection(server)!;

        Assert.Equal("npx", DevToolConfiguration.ReadString(claude, "command"));
        Assert.Equal(["-y", "serve"], DevToolConfiguration.ReadStrings(claude, "args"));
        Assert.Equal(string.Empty, DevToolConfiguration.ReadString(claude, "name"));

        // A section that is a bare string instead of an object, and a name that
        // is a number: both nothing, neither an exception.
        var stringSection = JsonNode.Parse("""{ "name": 3, "claude": "guidelines" }""")!;
        Assert.Equal(string.Empty, DevToolConfiguration.ReadString(stringSection, "name"));
        Assert.Equal(string.Empty, DevToolConfiguration.ReadString(stringSection["claude"], "name"));
        Assert.Empty(DevToolConfiguration.ReadStrings(stringSection["claude"], "args"));
        Assert.Equal(string.Empty, DevToolConfiguration.ReadString(null, "name"));
        Assert.Null(DevToolConfiguration.McpRegistrationSection(JsonNode.Parse("\"aspire\"")));
    }

    [Fact]
    public async Task A_command_registered_server_reads_back_the_overrides_it_was_given()
    {
        var paths = await CreateCatalogWithAsync(CommandRegisteredCatalog);

        await DevToolConfiguration.WriteEnabledOverrideAsync(paths, "mcp:aspire", false, TestContext.Current.CancellationToken);
        await DevToolConfiguration.WriteAcknowledgementAsync(paths, "mcp:aspire", true, TestContext.Current.CancellationToken);

        // Written under the property the catalog entry is actually identified by.
        // A "packageId": "aspire" here would be a per-PC entry nothing matches.
        var pcConfig = await File.ReadAllTextAsync(paths.PcConfigPath, TestContext.Current.CancellationToken);
        Assert.Contains("\"name\": \"aspire\"", pcConfig, StringComparison.Ordinal);
        Assert.DoesNotContain("packageId", pcConfig, StringComparison.OrdinalIgnoreCase);

        // And read back as saved, which is the half the write alone cannot prove.
        var config = await DevToolConfiguration.ReadAsync(paths, TestContext.Current.CancellationToken);
        var server = config.Root["mcpServers"]![0]!;

        Assert.False(server["enabled"]!.GetValue<bool>());
        Assert.True(server["acknowledged"]!.GetValue<bool>());
    }

    /// <summary>
    /// AC3. Every per-PC file on every machine predates this, and each of them
    /// holds <c>{"packageId": …}</c> entries for the servers that do ship as .NET
    /// tools. Nothing is renamed out from under those: both identities have to
    /// merge, out of the same file, in the same read.
    /// </summary>
    [Fact]
    public async Task A_per_pc_file_holding_both_identities_merges_both()
    {
        var paths = await CreateCatalogWithAsync("""
            {
              "mcpServers": [
                { "name": "guidelines", "packageId": "JSdotNet.MCP.Guidelines", "enabled": true },
                { "name": "aspire", "command": "aspire", "args": [ "agent", "mcp" ], "enabled": true }
              ]
            }
            """);

        // Hand-written rather than produced by the writer above, because the file
        // this has to keep working with is the one already on somebody's disk.
        Directory.CreateDirectory(Path.GetDirectoryName(paths.PcConfigPath)!);
        await File.WriteAllTextAsync(paths.PcConfigPath, """
            {
              "mcpServers": [
                { "packageId": "JSdotNet.MCP.Guidelines", "enabled": false },
                { "name": "aspire", "enabled": false }
              ]
            }
            """, TestContext.Current.CancellationToken);

        var servers = (await DevToolConfiguration.ReadAsync(paths, TestContext.Current.CancellationToken)).Root["mcpServers"]!.AsArray();

        Assert.False(servers[0]!["enabled"]!.GetValue<bool>());
        Assert.False(servers[1]!["enabled"]!.GetValue<bool>());
    }

    /// <summary>AC1 and AC4, at the one place both services now read the array
    /// through. The key is the name, the row reads as the name, the command line
    /// is what stands in for a version, and there is nothing to install.</summary>
    [Fact]
    public void A_command_registered_entry_is_identified_by_its_name()
    {
        var server = ReadOnly(CommandRegisteredCatalog);

        Assert.Equal("aspire", server.Id);
        Assert.Equal("name", server.IdName);
        Assert.Equal("mcp:aspire", server.Key);
        Assert.Equal("aspire", server.DisplayName);
        Assert.Equal(DevToolMcpMechanism.Command, server.Mechanism);
        Assert.Equal("aspire agent mcp", server.CommandLine);
        Assert.Equal("aspire agent mcp", server.Source);
        Assert.False(server.Installable);
        Assert.True(server.MechanismRecognised);
    }

    /// <summary>AC3, said about the reader rather than about the file: a server
    /// that ships as a .NET tool is still identified by its package id and still
    /// reads out exactly as it did, which is what every key already stored in a
    /// per-PC file depends on.</summary>
    [Fact]
    public void A_tool_backed_entry_is_still_identified_by_its_package_id()
    {
        var server = ReadOnly("""
            {
              "mcpServers": [
                { "name": "guidelines", "packageId": "JSdotNet.MCP.Guidelines", "enabled": true }
              ]
            }
            """);

        Assert.Equal("JSdotNet.MCP.Guidelines", server.Id);
        Assert.Equal("packageId", server.IdName);
        Assert.Equal("mcp:JSdotNet.MCP.Guidelines", server.Key);
        Assert.Equal("guidelines (JSdotNet.MCP.Guidelines)", server.DisplayName);
        Assert.Equal(DevToolMcpMechanism.DotNetTool, server.Mechanism);
        Assert.True(server.Installable);
    }

    /// <summary>The third shape the array accepts, and the one with the least in
    /// it: a command and nothing else. The import validator takes a
    /// <c>command</c> as proof enough on its own, so the reader has to answer for
    /// such an entry too — with the command as its identity, and the command line
    /// as what the row is called, because an entry that never said what to call
    /// itself still has to be called something. A blank display name here was a
    /// nameless line under "MCP servers" offering to "Remove  from the
    /// catalog".</summary>
    [Fact]
    public void An_entry_with_only_a_command_is_named_by_that_command()
    {
        var server = ReadOnly("""
            {
              "mcpServers": [
                { "command": "aspire", "args": [ "agent", "mcp" ], "enabled": true }
              ]
            }
            """);

        Assert.Equal("aspire", server.Id);
        Assert.Equal("command", server.IdName);
        Assert.Equal("mcp:aspire", server.Key);
        Assert.Equal("aspire agent mcp", server.DisplayName);
        Assert.Equal("aspire agent mcp", server.Source);
        Assert.Equal(DevToolMcpMechanism.Command, server.Mechanism);
        Assert.False(server.Installable);
    }

    /// <summary>An entry nothing can address is skipped rather than thrown on,
    /// the way an application with no id is: this array is read on every load of
    /// the pane, and one mistyped entry is not allowed to take the rest of the
    /// catalog off the screen.</summary>
    [Fact]
    public void An_entry_with_no_identity_at_all_is_skipped()
    {
        Assert.Empty(DevToolConfiguration.ReadMcpServers(JsonNode.Parse("""
            { "mcpServers": [ { "enabled": true }, "not even an object" ] }
            """)));
    }

    /// <summary>
    /// A name is what a row is called, not a way to reach one — and this is the
    /// one shape where the difference showed. The reader used to accept a
    /// name-only entry and build a row whose command line was empty, whose status
    /// announced it was "registered by the command it declares" though it declared
    /// none, and whose registration section was null, so no button could ever act
    /// on it.
    ///
    /// <para>Both halves are asserted together because the bug was the two of them
    /// disagreeing: the file the reader was happy with is a file the import
    /// refuses, so exporting a catalog and reading it back was not a round trip.
    /// The reader is the half that moved.</para>
    /// </summary>
    [Fact]
    public void An_entry_with_a_name_and_nothing_to_reach_it_by_is_skipped()
    {
        const string NameOnly = """
            { "mcpServers": [ { "name": "foo", "enabled": true } ] }
            """;

        Assert.Empty(DevToolConfiguration.ReadMcpServers(JsonNode.Parse(NameOnly)));

        Assert.False(DevToolConfiguration.TryReadCatalog(NameOnly, out _, out var error));
        Assert.Contains("\"packageId\" or a \"command\"", error, StringComparison.Ordinal);
    }

    /// <summary>AC8. Mirrors <c>ParseProvider</c> degrading an unknown
    /// <c>provider</c> to Manual with <c>ProviderRecognised = false</c>: the safe
    /// reading of a mechanism nobody here knows is the one that runs nothing, and
    /// the entry is still read so that the row can say so.</summary>
    [Fact]
    public void An_unrecognised_mechanism_lands_on_the_one_that_runs_nothing()
    {
        var server = ReadOnly("""
            {
              "mcpServers": [
                { "name": "aspire", "mechanism": "docker-compose", "command": "aspire", "enabled": true }
              ]
            }
            """);

        Assert.Equal(DevToolMcpMechanism.Manual, server.Mechanism);
        Assert.Equal("docker-compose", server.DeclaredMechanism);
        Assert.False(server.MechanismRecognised);
        Assert.False(server.Installable);

        // Still addressable, which is the difference between a row that reports a
        // typo and a row that is simply not there.
        Assert.Equal("mcp:aspire", server.Key);
    }

    /// <summary>Every mechanism, because the writer and the reader are two halves
    /// of one vocabulary: one spelling that only exists in the writer is a value
    /// that would read back as the mechanism that runs nothing.</summary>
    [Theory]
    [InlineData(DevToolMcpMechanism.DotNetTool, "dotnet-tool")]
    [InlineData(DevToolMcpMechanism.Command, "command")]
    [InlineData(DevToolMcpMechanism.Http, "http")]
    [InlineData(DevToolMcpMechanism.Manual, "manual")]
    public void Every_mechanism_round_trips_through_its_name(DevToolMcpMechanism mechanism, string expected)
    {
        Assert.Equal(expected, DevToolConfiguration.McpMechanismName(mechanism));
        Assert.Equal(mechanism, DevToolConfiguration.ParseMcpMechanism(expected));
    }

    /// <summary>A declared mechanism overrules the shape, which is the whole
    /// reason it can be declared: an entry that ships a package id and is
    /// nonetheless registered by hand has no other way to say so.</summary>
    [Fact]
    public void A_declared_mechanism_overrules_the_shape_of_the_entry()
    {
        var server = ReadOnly("""
            {
              "mcpServers": [
                { "name": "guidelines", "packageId": "JSdotNet.MCP.Guidelines", "mechanism": "manual", "enabled": true }
              ]
            }
            """);

        Assert.Equal(DevToolMcpMechanism.Manual, server.Mechanism);
        Assert.True(server.MechanismRecognised);
        Assert.False(server.Installable);

        // And the identity is still the package id it carries: a mechanism is not
        // a rename.
        Assert.Equal("packageId", server.IdName);
    }

    /// <summary>
    /// AC7. The top-level command is read, and where an entry carries both a
    /// top-level command and a nested <c>claude</c> section the section wins —
    /// which is what lets <c>jsdotnet-project-guidelines</c> be registered with
    /// Claude under a different name than the catalog calls it.
    /// </summary>
    [Fact]
    public void The_nested_claude_section_wins_over_the_top_level_command()
    {
        var registration = DevToolConfiguration.McpRegistrationSection(JsonNode.Parse("""
            {
              "name": "aspire",
              "command": "aspire",
              "args": [ "agent", "mcp" ],
              "claude": { "name": "aspire-cli", "command": "aspire-preview", "args": [ "mcp" ] }
            }
            """));

        Assert.NotNull(registration);
        Assert.Equal("aspire-cli", registration["name"]!.GetValue<string>());
        Assert.Equal("aspire-preview", registration["command"]!.GetValue<string>());
    }

    /// <inheritdoc cref="The_nested_claude_section_wins_over_the_top_level_command" />
    [Fact]
    public void An_entry_with_only_a_top_level_command_registers_from_it()
    {
        var registration = DevToolConfiguration.McpRegistrationSection(JsonNode.Parse("""
            { "name": "aspire", "command": "aspire", "args": [ "agent", "mcp" ], "enabled": true }
            """));

        Assert.NotNull(registration);
        Assert.Equal("aspire", registration["name"]!.GetValue<string>());
        Assert.Equal("aspire", registration["command"]!.GetValue<string>());
        Assert.Equal(["agent", "mcp"], registration["args"]!.AsArray().Select(node => node!.GetValue<string>()));
    }

    /// <summary>An entry with neither has nothing to register, and says so with a
    /// null rather than with a section whose command is blank.</summary>
    [Fact]
    public void An_entry_with_no_command_anywhere_registers_nowhere()
    {
        Assert.Null(DevToolConfiguration.McpRegistrationSection(JsonNode.Parse("""
            { "name": "guidelines", "packageId": "JSdotNet.MCP.Guidelines", "enabled": true }
            """)));
    }

    /// <summary>
    /// AC9. The Add dialog's second mechanism, written the way the live catalog
    /// spells it — a name, a command, its arguments, and no package id anywhere.
    /// </summary>
    [Fact]
    public async Task Adding_a_command_registered_server_writes_the_command_and_no_package_id()
    {
        var paths = await CreateCatalogWithAsync("""{ "mcpServers": [] }""");

        await DevToolConfiguration.AddToCatalogAsync(
            paths,
            new DevToolDraft(DevToolKind.McpServer, "aspire")
            {
                McpMechanism = DevToolMcpMechanism.Command,
                ServerCommand = "aspire",
                ServerArgs = ["agent", "mcp"]
            },
            TestContext.Current.CancellationToken);

        var entry = Assert.Single((await DevToolConfiguration.ReadAsync(paths, TestContext.Current.CancellationToken)).Root["mcpServers"]!.AsArray())!;

        Assert.Equal("aspire", entry["name"]!.GetValue<string>());
        Assert.Equal("aspire", entry["command"]!.GetValue<string>());
        Assert.Equal(["agent", "mcp"], entry["args"]!.AsArray().Select(node => node!.GetValue<string>()));
        Assert.True(entry["enabled"]!.GetValue<bool>());
        Assert.Null(entry["packageId"]);

        // And it reads back as the row it was written to be, through the same
        // reader both services enumerate with.
        var server = DevToolConfiguration.ReadMcpServer(entry)!;
        Assert.Equal("mcp:aspire", server.Key);
        Assert.Equal(DevToolMcpMechanism.Command, server.Mechanism);
    }

    /// <summary>The other half of AC9: an entry that ships as a .NET tool is
    /// written exactly as it was before there was a second mechanism.</summary>
    [Fact]
    public async Task Adding_a_tool_backed_server_still_writes_a_package_id_entry()
    {
        var paths = await CreateCatalogWithAsync("""{ "mcpServers": [] }""");

        await DevToolConfiguration.AddToCatalogAsync(
            paths,
            new DevToolDraft(DevToolKind.McpServer, "JSdotNet.MCP.Guidelines", DisplayName: "guidelines"),
            TestContext.Current.CancellationToken);

        var entry = Assert.Single((await DevToolConfiguration.ReadAsync(paths, TestContext.Current.CancellationToken)).Root["mcpServers"]!.AsArray())!;

        Assert.Equal("JSdotNet.MCP.Guidelines", entry["packageId"]!.GetValue<string>());
        Assert.Equal("guidelines", entry["name"]!.GetValue<string>());
        Assert.Null(entry["command"]);
    }

    /// <summary>AC9's refusals, each naming the thing that is missing. A draft
    /// that names neither an identity nor a command nor a URL is an entry no
    /// override could reach and no button could act on — the same bar the import
    /// validator holds this array to.</summary>
    [Fact]
    public async Task A_server_that_names_neither_a_package_id_nor_a_command_nor_a_url_is_refused()
    {
        var paths = await CreateCatalogWithAsync("""{ "mcpServers": [] }""");

        var noPackageId = await Assert.ThrowsAsync<InvalidOperationException>(() => DevToolConfiguration.AddToCatalogAsync(
            paths,
            new DevToolDraft(DevToolKind.McpServer, "  "),
            TestContext.Current.CancellationToken));

        var noName = await Assert.ThrowsAsync<InvalidOperationException>(() => DevToolConfiguration.AddToCatalogAsync(
            paths,
            new DevToolDraft(DevToolKind.McpServer, "  ") { McpMechanism = DevToolMcpMechanism.Command, ServerCommand = "aspire" },
            TestContext.Current.CancellationToken));

        var noCommand = await Assert.ThrowsAsync<InvalidOperationException>(() => DevToolConfiguration.AddToCatalogAsync(
            paths,
            new DevToolDraft(DevToolKind.McpServer, "aspire") { McpMechanism = DevToolMcpMechanism.Command },
            TestContext.Current.CancellationToken));

        var noUrl = await Assert.ThrowsAsync<InvalidOperationException>(() => DevToolConfiguration.AddToCatalogAsync(
            paths,
            new DevToolDraft(DevToolKind.McpServer, "backlog") { McpMechanism = DevToolMcpMechanism.Http },
            TestContext.Current.CancellationToken));

        Assert.Contains("package id", noPackageId.Message, StringComparison.Ordinal);
        Assert.Contains("name", noName.Message, StringComparison.Ordinal);
        Assert.Contains("command", noCommand.Message, StringComparison.Ordinal);
        Assert.Contains("URL", noUrl.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The counterpart to each of those refusals: a draft that carries a URL and
    /// no command at all is accepted, because a URL is a way to reach a server.
    /// This is the half the addressability rule had to grow — the reader and the
    /// validator both took a package id or a command as the only proof there was
    /// anything there, and an HTTP entry has neither.
    /// </summary>
    [Fact]
    public async Task Adding_an_http_server_writes_the_transport_the_url_and_its_headers()
    {
        var paths = await CreateCatalogWithAsync("""{ "mcpServers": [] }""");

        await DevToolConfiguration.AddToCatalogAsync(
            paths,
            new DevToolDraft(DevToolKind.McpServer, "backlog")
            {
                McpMechanism = DevToolMcpMechanism.Http,
                ServerUrl = "http://127.0.0.1:${BACKLOG_MCP_PORT}/mcp",
                ServerHeaders = [new("Authorization", "Bearer ${BACKLOG_MCP_TOKEN}")]
            },
            TestContext.Current.CancellationToken);

        var entry = Assert.Single((await DevToolConfiguration.ReadAsync(paths, TestContext.Current.CancellationToken)).Root["mcpServers"]!.AsArray())!;

        Assert.Equal("backlog", entry["name"]!.GetValue<string>());
        Assert.Equal("http", entry["type"]!.GetValue<string>());
        Assert.Equal("http://127.0.0.1:${BACKLOG_MCP_PORT}/mcp", entry["url"]!.GetValue<string>());
        Assert.Equal("Bearer ${BACKLOG_MCP_TOKEN}", entry["headers"]!["Authorization"]!.GetValue<string>());
        Assert.Null(entry["packageId"]);
        Assert.Null(entry["command"]);

        // No "mechanism": "http" line. The transport already says it, and a line
        // that only restates the shape is one the next reader has to go and check
        // against it — only Manual, which no shape implies, is spelled out.
        Assert.Null(entry["mechanism"]);

        var server = DevToolConfiguration.ReadMcpServer(entry)!;
        Assert.Equal("mcp:backlog", server.Key);
        Assert.Equal(DevToolMcpMechanism.Http, server.Mechanism);
    }

    /// <summary>The registration section an HTTP entry with no nested
    /// <c>claude</c> object synthesises: what the host is to be told, in the same
    /// shape one code path can register either kind from — and with the
    /// placeholders still unexpanded, because this also feeds the listing.</summary>
    [Fact]
    public void An_http_entry_registers_from_its_url_and_headers()
    {
        var registration = DevToolConfiguration.McpRegistrationSection(
            JsonNode.Parse(HttpCatalog)!["mcpServers"]![0]);

        Assert.NotNull(registration);
        Assert.Equal("backlog", registration["name"]!.GetValue<string>());
        Assert.Equal("http", registration["type"]!.GetValue<string>());
        Assert.Equal("http://127.0.0.1:${BACKLOG_MCP_PORT:-5757}/mcp", registration["url"]!.GetValue<string>());
        Assert.Equal("Bearer ${BACKLOG_MCP_TOKEN}", registration["headers"]!["Authorization"]!.GetValue<string>());

        // And the nested section still wins, exactly as it does for a command.
        var overridden = DevToolConfiguration.McpRegistrationSection(JsonNode.Parse("""
            {
              "name": "backlog",
              "type": "http",
              "url": "http://127.0.0.1:5757/mcp",
              "claude": { "name": "backlog-dev", "type": "http", "url": "http://127.0.0.1:6000/mcp" }
            }
            """));

        Assert.Equal("backlog-dev", overridden!["name"]!.GetValue<string>());
        Assert.Equal("http://127.0.0.1:6000/mcp", overridden["url"]!.GetValue<string>());
    }

    /// <summary>What a stored identity has to survive besides the merge: the two
    /// buttons that resolve a key back to an entry. A remove that could only match
    /// a package id told the operator the row was already gone while the entry sat
    /// in the file.</summary>
    [Fact]
    public async Task A_command_registered_server_can_be_removed_by_its_key()
    {
        var paths = await CreateCatalogWithAsync(CommandRegisteredCatalog);

        await DevToolConfiguration.WriteEnabledOverrideAsync(paths, "mcp:aspire", false, TestContext.Current.CancellationToken);
        await DevToolConfiguration.RemoveFromCatalogAsync(paths, "mcp:aspire", TestContext.Current.CancellationToken);
        await DevToolConfiguration.RemoveEnabledOverrideAsync(paths, "mcp:aspire", TestContext.Current.CancellationToken);

        Assert.Empty((await DevToolConfiguration.ReadAsync(paths, TestContext.Current.CancellationToken)).Root["mcpServers"]!.AsArray());

        // The override goes with the entry, or the same server added again would
        // arrive already switched off by a decision nobody remembers making.
        var pcConfig = await File.ReadAllTextAsync(paths.PcConfigPath, TestContext.Current.CancellationToken);
        Assert.DoesNotContain("aspire", pcConfig, StringComparison.Ordinal);
    }

    /// <summary>
    /// AC10, against the file this whole change is judged by: the repository's own
    /// catalog, still importable and now carrying a row for the entry it has held
    /// all along.
    ///
    /// <para>The import validator PR #382 landed already accepted this file. What
    /// this adds is the half that came after it: the reader that turns the file
    /// into rows finds the Aspire server in it, with no version invented for it and
    /// nothing to install.</para>
    /// </summary>
    [Fact]
    public void The_repositorys_own_catalog_imports_and_carries_its_command_registered_row()
    {
        var json = File.ReadAllText(RepositoryRoot.File(".tools", "ai-tools.json"));

        Assert.True(DevToolConfiguration.TryReadCatalog(json, out var root, out var error), error);

        var server = Assert.Single(DevToolConfiguration.ReadMcpServers(root), candidate => candidate.Key == "mcp:aspire");

        Assert.Equal(DevToolMcpMechanism.Command, server.Mechanism);
        Assert.Equal("aspire agent mcp", server.CommandLine);
        Assert.False(server.Installable);

        // And the .NET-tool servers beside it are read exactly as they were.
        Assert.All(
            DevToolConfiguration.ReadMcpServers(root).Where(candidate => candidate.PackageId.Length > 0),
            candidate =>
            {
                Assert.Equal(DevToolMcpMechanism.DotNetTool, candidate.Mechanism);
                Assert.Equal("packageId", candidate.IdName);
            });
    }

    /// <summary>
    /// The row this change exists for, read out of the file the machine actually
    /// ships: Backlog's own server, reached over HTTP at the port this machine
    /// chose, registered with Claude and with Claude alone.
    ///
    /// <para><c>hosts</c> is not decoration. Silence means
    /// <see cref="DevToolHosts.Default"/> — Copilot and Claude both — and Copilot
    /// has no HTTP registration path in this code, so the row would grow a half
    /// nothing answers. Local ADR 0012 rules Claude Desktop out from the other
    /// side, for the matching reason: its configuration file takes stdio
    /// servers only.</para>
    /// </summary>
    [Fact]
    public void The_repositorys_own_catalog_carries_the_backlog_server_for_claude_alone()
    {
        var json = File.ReadAllText(RepositoryRoot.File(".tools", "ai-tools.json"));

        Assert.True(DevToolConfiguration.TryReadCatalog(json, out var root, out var error), error);

        var server = Assert.Single(DevToolConfiguration.ReadMcpServers(root), candidate => candidate.Key == "mcp:backlog");

        Assert.Equal(DevToolMcpMechanism.Http, server.Mechanism);
        Assert.Equal("name", server.IdName);
        Assert.Equal("backlog", server.Id);
        Assert.False(server.Installable);
        Assert.Equal(DevToolHosts.Claude, server.Hosts);
        Assert.Equal(BacklogServerUrl, server.Url);
        Assert.Equal("Bearer ${BACKLOG_MCP_TOKEN}", Assert.Single(server.Headers).Value);
    }

    /// <summary>
    /// The path in the catalog is the path the hosts serve, and this is the only
    /// thing that says so.
    ///
    /// <para>Change <see cref="BacklogMcpServerRegistration.EndpointPath"/> and
    /// everything else stays green: the worker listens at the new path, the
    /// catalog still names the old one, the registration points at a 404, and
    /// <c>RegistrationDrifted</c> reports no drift because the registration and
    /// the catalog agree with each other on the stale string. Nothing in the
    /// build connects the two — the port crosses the endpoint port and the path
    /// never did — so what connects them is this assertion, read off the
    /// constant rather than restated beside it.</para>
    /// </summary>
    [Fact]
    public void The_repositorys_own_catalog_points_at_the_path_the_hosts_serve()
    {
        var json = File.ReadAllText(RepositoryRoot.File(".tools", "ai-tools.json"));

        var server = Assert.Single(
            DevToolConfiguration.ReadMcpServers(JsonNode.Parse(json)),
            candidate => candidate.Key == "mcp:backlog");

        // The port placeholder stood in with a number so that this is a URL to
        // parse; the path and the host are what is being read off it.
        var url = new Uri(server.Url.Replace($"${{{McpPlaceholders.PortName}}}", "0", StringComparison.Ordinal));

        Assert.Equal(BacklogMcpServerRegistration.EndpointPath, url.AbsolutePath);
        Assert.Equal(IPAddress.Loopback.ToString(), url.Host);
    }

    /// <summary>
    /// The catalog is a committed file, and on this machine it resolves out of a
    /// synced folder — so neither half of the endpoint may be written into it
    /// literally. The token because it is a secret; the port because it is one
    /// machine's setting, and a literal there is that machine imposing it on
    /// every other.
    ///
    /// <para>The placeholders are the whole mechanism rather than a convenience:
    /// they are expanded at apply time, by a resolver that knows these two names
    /// and refuses to read the environment.</para>
    /// </summary>
    [Fact]
    public void The_repositorys_own_catalog_writes_no_literal_port_and_no_literal_token()
    {
        var json = File.ReadAllText(RepositoryRoot.File(".tools", "ai-tools.json"));

        Assert.Contains("${BACKLOG_MCP_PORT}", json, StringComparison.Ordinal);
        Assert.Contains("${BACKLOG_MCP_TOKEN}", json, StringComparison.Ordinal);

        // Nothing bearer-shaped that is not the placeholder, and no loopback URL
        // that has had a port baked into it.
        Assert.DoesNotMatch(@"Bearer\s+(?!\$\{BACKLOG_MCP_TOKEN\})\S", json);
        Assert.DoesNotMatch(@"127\.0\.0\.1:\d", json);
    }

    /// <summary>
    /// The third mechanism, and the one this repository's own MCP server needs:
    /// a server nothing installs and no command starts, reached over HTTP at a
    /// URL the entry carries.
    ///
    /// <para>Identified by its <c>name</c>, which is the property
    /// <c>claude mcp add --transport http</c> registers it under — and never by
    /// its URL, which carries a port that moves. The <c>${…}</c> in the URL and in
    /// the header is left exactly as the catalog spells it: this reader is the
    /// half that reads the file, and expanding a placeholder is a decision about
    /// one machine.</para>
    /// </summary>
    [Fact]
    public void An_http_entry_is_read_and_identified_by_its_name()
    {
        var server = ReadOnly(HttpCatalog);

        Assert.Equal(DevToolMcpMechanism.Http, server.Mechanism);
        Assert.Equal("name", server.IdName);
        Assert.Equal("backlog", server.Id);
        Assert.Equal("mcp:backlog", server.Key);

        // Nothing to install: there is no package, and a URL is not something
        // `dotnet tool install` has ever been able to put on a machine.
        Assert.False(server.Installable);

        Assert.Equal("http", server.Type);
        Assert.Equal("http://127.0.0.1:${BACKLOG_MCP_PORT:-5757}/mcp", server.Url);
        Assert.True(server.MechanismRecognised);

        // In catalog order, because the order is what the repeated --header flags
        // are built from.
        Assert.Equal(
            ["Authorization", "X-Backlog-Client"],
            server.Headers.Select(header => header.Key));
        Assert.Equal(
            ["Bearer ${BACKLOG_MCP_TOKEN}", "dev-pc"],
            server.Headers.Select(header => header.Value));

        // And the row reads as something: the URL is what it comes from, where a
        // .NET tool's package id and a command entry's command line are.
        Assert.Equal("backlog", server.DisplayName);
        Assert.Equal("http://127.0.0.1:${BACKLOG_MCP_PORT:-5757}/mcp", server.Source);
    }

    /// <summary>A <c>type</c> is not a way to reach a server. An entry that names
    /// one and no URL has nothing behind it at all, and is skipped exactly as a
    /// name-only entry is.</summary>
    [Fact]
    public void An_http_entry_with_no_url_is_skipped()
    {
        Assert.Empty(DevToolConfiguration.ReadMcpServers(JsonNode.Parse("""
            { "mcpServers": [ { "name": "backlog", "type": "http", "enabled": true } ] }
            """)));
    }

    /// <summary>
    /// The URL says where to reach the server and the name says what to call it,
    /// and an HTTP registration needs both: <c>claude mcp add --transport http</c>
    /// takes the name as its first positional argument.
    ///
    /// <para>Without the refusal the identity precedence falls through the absent
    /// name to the absent command, and the entry becomes a row with an empty id
    /// under the key <c>mcp:command=</c> — nameless, unremovable and
    /// unregisterable. Both halves are asserted, for the reason
    /// <see cref="An_entry_with_a_name_and_nothing_to_reach_it_by_is_skipped"/>
    /// asserts both: a file the reader accepts and the import refuses is not a
    /// round trip.</para>
    /// </summary>
    [Fact]
    public void An_http_entry_with_no_name_is_refused()
    {
        const string NoName = """
            { "mcpServers": [ { "type": "http", "url": "http://127.0.0.1:5757/mcp", "enabled": true } ] }
            """;

        Assert.Empty(DevToolConfiguration.ReadMcpServers(JsonNode.Parse(NoName)));

        Assert.False(DevToolConfiguration.TryReadCatalog(NoName, out _, out var error));
        Assert.Contains("name", error, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("url", error, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A transport this build cannot speak degrades to the mechanism that runs
    /// nothing, the way an unrecognised <c>mechanism</c> and an unrecognised
    /// <c>provider</c> do — and emphatically does not get registered over HTTP
    /// because it happens to be reached at a URL. <c>sse</c> is the live case: a
    /// real MCP transport, spelled in real <c>.mcp.json</c> files, that nothing
    /// here knows how to add.
    /// </summary>
    [Fact]
    public void A_declared_type_this_build_does_not_know_lands_on_the_one_that_runs_nothing()
    {
        var server = ReadOnly("""
            {
              "mcpServers": [
                { "name": "events", "type": "sse", "url": "http://127.0.0.1:5757/sse", "enabled": true }
              ]
            }
            """);

        Assert.Equal(DevToolMcpMechanism.Manual, server.Mechanism);
        Assert.Equal("sse", server.DeclaredMechanism);
        Assert.False(server.MechanismRecognised);
        Assert.False(server.Installable);

        // Still a row, so the typo — or the transport this build has yet to
        // learn — is reported rather than invisible.
        Assert.Equal("mcp:events", server.Key);
    }

    /// <summary>
    /// The other side of that degrade, and the one it had swallowed: a transport
    /// this build does not know, on an entry that still names a command.
    ///
    /// <para>This is not a hypothetical shape — it is exactly what a person
    /// pastes out of a <c>.mcp.json</c>, which is the premise the whole HTTP
    /// entry rests on. Read as <see cref="DevToolMcpMechanism.Manual"/>, the
    /// entry stopped registering the moment somebody added the <c>type</c> line
    /// that describes what it was already doing, and the row said the transport
    /// was "not a mechanism this build knows" — which is true of the word and
    /// false of the entry. A transport is not a mechanism, so the shape beside it
    /// answers and the redundant label is dropped.</para>
    /// </summary>
    [Fact]
    public void A_transport_word_this_build_does_not_know_still_registers_the_command_beside_it()
    {
        var server = ReadOnly("""
            {
              "mcpServers": [
                {
                  "name": "jsdotnet",
                  "type": "stdio",
                  "command": "jsdotnet-guidelines-mcpserver",
                  "enabled": true
                }
              ]
            }
            """);

        Assert.Equal(DevToolMcpMechanism.Command, server.Mechanism);

        // No note: the entry declared no mechanism, and "stdio" is not one it got
        // wrong. Manual plus MechanismRecognised false is the pair that put
        // "not a mechanism this build knows" on a row that was fine.
        Assert.True(server.MechanismRecognised);
        Assert.Equal(string.Empty, server.DeclaredMechanism);

        // And the command survives as the thing the registration is made from —
        // Manual is the arm that hands the describer a null section and leaves
        // the row with no button at all.
        Assert.Equal("jsdotnet-guidelines-mcpserver", server.Command);
        Assert.Equal("jsdotnet-guidelines-mcpserver", server.CommandLine);
    }

    /// <summary>A <c>packageId</c> is the same fallback one step further up the
    /// order: a .NET tool wearing a transport word is still a .NET tool.</summary>
    [Fact]
    public void A_transport_word_beside_a_package_id_still_reads_as_the_dotnet_tool()
    {
        var server = ReadOnly("""
            {
              "mcpServers": [
                { "name": "guidelines", "type": "stdio", "packageId": "JSdotNet.Guidelines", "enabled": true }
              ]
            }
            """);

        Assert.Equal(DevToolMcpMechanism.DotNetTool, server.Mechanism);
        Assert.True(server.MechanismRecognised);
        Assert.True(server.Installable);
    }

    /// <summary>The one entry of a single-server catalog, read the way both
    /// services read it.</summary>
    private static DevToolMcpServer ReadOnly(string catalog) =>
        Assert.Single(DevToolConfiguration.ReadMcpServers(JsonNode.Parse(catalog)));

    private static async Task<DevToolConfigurationPaths> CreateCatalogWithAsync(string json)
    {
        var root = Path.Combine(Path.GetTempPath(), "backlog-tool-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, ".tools"));
        var paths = DevToolConfigurationPaths.FromRepositoryRoot(root, "dev-pc");
        await File.WriteAllTextAsync(paths.CatalogPath, json);
        return paths;
    }
}
