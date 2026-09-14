using System.Text.Json.Nodes;

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

    /// <summary>
    /// AC2. The row's key has to survive the round trip through the per-PC file,
    /// which is the one boundary an identity can silently fail at: the merge in
    /// <see cref="DevToolConfiguration.ReadAsync"/> drops a per-PC entry with no
    /// catalog entry of the same id property behind it, so an override written
    /// under a property the catalog entry does not carry is a saved setting that
    /// reads back as never saved.
    /// </summary>
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
    /// that names neither an identity nor a command is an entry no override could
    /// reach and no button could act on — the same bar the import validator holds
    /// this array to.</summary>
    [Fact]
    public async Task A_server_that_names_neither_a_package_id_nor_a_command_is_refused()
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

        Assert.Contains("package id", noPackageId.Message, StringComparison.Ordinal);
        Assert.Contains("name", noName.Message, StringComparison.Ordinal);
        Assert.Contains("command", noCommand.Message, StringComparison.Ordinal);
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
