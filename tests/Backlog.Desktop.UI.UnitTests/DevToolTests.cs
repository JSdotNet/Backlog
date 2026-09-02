using System.Text.Json.Nodes;

namespace Backlog.Desktop.UI.UnitTests;

public class DevToolTests
{
    [Theory]
    [InlineData("1.2.3", "1.2.4", true)]
    [InlineData("v1.2.3", "1.2.3", false)]
    [InlineData("unknown", "1.2.3", false)]
    [InlineData("not installed", "1.2.3", false)]
    public void Update_available_only_compares_known_versions(string installed, string available, bool expected)
    {
        var tool = new DevToolInfo(
            "plugin:test",
            DevToolKind.Plugin,
            "test",
            "https://github.com/example/test",
            ConfiguredEnabled: true,
            Installed: true,
            installed,
            available,
            "Enabled plugin");

        Assert.Equal(expected, tool.UpdateAvailable);
    }

    [Fact]
    public void Disabled_tools_are_not_updateable()
    {
        var tool = new DevToolInfo(
            "plugin:test",
            DevToolKind.Plugin,
            "test",
            "https://github.com/example/test",
            ConfiguredEnabled: false,
            Installed: true,
            "1.0.0",
            "1.1.0",
            "Disabled plugin");

        Assert.True(tool.UpdateAvailable);
        Assert.False(tool.CanUpdate);
    }

    /// <summary>An enabled tool that is not on the machine is the one case the
    /// pane had no word for: it cannot be updated, so it was labelled "up to
    /// date" while the row beside it said "not installed".</summary>
    [Theory]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    public void Only_an_enabled_tool_that_is_absent_can_be_installed(bool enabled, bool installed, bool expected)
    {
        var tool = Tool(enabled, installed, "1.0.0", "1.0.0");

        Assert.Equal(expected, tool.CanInstall);
    }

    /// <summary>"Up to date" is a claim about a version somebody looked up. When
    /// the lookup failed there is no version to have been up to date with.</summary>
    [Theory]
    [InlineData("1.0.0", true)]
    [InlineData("unknown", false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    public void An_available_version_is_known_only_when_a_lookup_answered(string available, bool expected)
    {
        var tool = Tool(enabled: true, installed: true, "1.0.0", available);

        Assert.Equal(expected, tool.AvailableVersionKnown);
    }

    private static DevToolInfo Tool(bool enabled, bool installed, string installedVersion, string availableVersion) =>
        new(
            "plugin:test",
            DevToolKind.Plugin,
            "test",
            "https://github.com/example/test",
            enabled,
            installed,
            installedVersion,
            availableVersion,
            "Enabled plugin");

    [Fact]
    public async Task Pc_config_overrides_matching_catalog_tools_only()
    {
        var root = CreateTempToolConfigRoot();
        var catalogPath = Path.Combine(root, ".tools", "ai-tools.json");
        var pcConfigPath = Path.Combine(root, ".tools", "dev-pc", "ai-tools.json");
        Directory.CreateDirectory(Path.GetDirectoryName(pcConfigPath)!);
        await File.WriteAllTextAsync(catalogPath, """
            {
              "plugins": [
                { "name": "architecture", "source": "JSdotNet/Copilot:plugins/architecture", "enabled": true },
                { "name": "qa", "source": "JSdotNet/Copilot:plugins/qa", "enabled": false }
              ],
              "mcpServers": [
                { "name": "guidelines", "packageId": "JSdotNet.MCP.Guidelines", "enabled": true }
              ]
            }
            """);
        await File.WriteAllTextAsync(pcConfigPath, """
            {
              "plugins": [
                { "name": "architecture", "enabled": false },
                { "name": "unknown", "enabled": true }
              ],
              "mcpServers": [
                { "packageId": "JSdotNet.MCP.Guidelines", "enabled": false }
              ]
            }
            """);

        var config = await DevToolConfiguration.ReadAsync(DevToolConfigurationPaths.FromRepositoryRoot(root, "dev-pc"));

        var plugins = config.Root["plugins"]!.AsArray();
        Assert.False(plugins[0]!["enabled"]!.GetValue<bool>());
        Assert.False(plugins[1]!["enabled"]!.GetValue<bool>());
        Assert.DoesNotContain(plugins, node => node?["name"]?.GetValue<string>() == "unknown");
        Assert.False(config.Root["mcpServers"]![0]!["enabled"]!.GetValue<bool>());
    }

    [Fact]
    public async Task Enabled_override_writes_minimal_pc_config()
    {
        var root = CreateTempToolConfigRoot();
        var catalogPath = Path.Combine(root, ".tools", "ai-tools.json");
        await File.WriteAllTextAsync(catalogPath, """
            {
              "plugins": [
                { "name": "architecture", "source": "JSdotNet/Copilot:plugins/architecture", "enabled": true }
              ],
              "mcpServers": [
                { "name": "guidelines", "packageId": "JSdotNet.MCP.Guidelines", "enabled": true }
              ]
            }
            """);
        var paths = DevToolConfigurationPaths.FromRepositoryRoot(root, "dev-pc");

        await DevToolConfiguration.WriteEnabledOverrideAsync(paths, "plugin:architecture", false);
        await DevToolConfiguration.WriteEnabledOverrideAsync(paths, "mcp:JSdotNet.MCP.Guidelines", false);

        var config = await DevToolConfiguration.ReadAsync(paths);
        Assert.True(config.PcConfigExists);
        Assert.False(config.Root["plugins"]![0]!["enabled"]!.GetValue<bool>());
        Assert.False(config.Root["mcpServers"]![0]!["enabled"]!.GetValue<bool>());

        var pcConfig = await File.ReadAllTextAsync(paths.PcConfigPath);
        Assert.Contains("\"name\": \"architecture\"", pcConfig);
        Assert.Contains("\"packageId\": \"JSdotNet.MCP.Guidelines\"", pcConfig);
        Assert.DoesNotContain("source", pcConfig, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Default_paths_prefer_local_repo_tools_catalog()
    {
        var root = CreateTempToolConfigRoot();
        var catalogPath = Path.Combine(root, ".tools", "copilot-tools.json");
        await File.WriteAllTextAsync(catalogPath, """{ "plugins": [], "mcpServers": [] }""");
        var nestedStartPath = Path.Combine(root, "src", "App", "Backlog.Desktop", "bin", "Debug");
        Directory.CreateDirectory(nestedStartPath);

        var paths = DevToolConfigurationPaths.CreateDefault("dev-pc", nestedStartPath);

        Assert.Equal(catalogPath, paths.CatalogPath);

        // The per-PC file is resolved on its own, not by copying the catalog's
        // name: a machine can carry a catalog under the old name and have never
        // written an override, and the override it writes next belongs under the
        // name this version uses.
        Assert.Equal(Path.Combine(root, ".tools", "dev-pc", "ai-tools.json"), paths.PcConfigPath);
    }

    [Fact]
    public void Default_paths_use_the_configured_storage_root_for_tools()
    {
        var storageRoot = Path.Combine(Path.GetTempPath(), "backlog-tool-tests", Guid.NewGuid().ToString("N"));
        var repositoryRoot = CreateTempToolConfigRoot();
        var nestedStartPath = Path.Combine(repositoryRoot, "src", "App", "Backlog.Desktop", "bin", "Debug");
        Directory.CreateDirectory(nestedStartPath);

        var paths = DevToolConfigurationPaths.CreateDefault("dev-pc", nestedStartPath, storageRoot);

        Assert.Equal(Path.Combine(storageRoot, ".tools", "ai-tools.json"), paths.CatalogPath);
        Assert.Equal(Path.Combine(storageRoot, ".tools", "dev-pc", "ai-tools.json"), paths.PcConfigPath);
    }

    /// <summary>
    /// The catalog was renamed when it stopped being only about Copilot. The
    /// machines that have one keep it in a synced folder, so an upgrade that only
    /// looked for the new name would show every one of them the "no catalog yet"
    /// empty state beside the catalog they already had.
    /// </summary>
    [Fact]
    public async Task A_catalog_still_under_the_old_name_is_found()
    {
        var root = CreateTempToolConfigRoot();
        var legacyCatalog = Path.Combine(root, ".tools", "copilot-tools.json");
        var legacyPcConfig = Path.Combine(root, ".tools", "dev-pc", "copilot-tools.json");
        Directory.CreateDirectory(Path.GetDirectoryName(legacyPcConfig)!);
        await File.WriteAllTextAsync(legacyCatalog, """{ "plugins": [], "mcpServers": [] }""");
        await File.WriteAllTextAsync(legacyPcConfig, """{ "plugins": [] }""");

        var paths = DevToolConfigurationPaths.FromRepositoryRoot(root, "dev-pc");

        Assert.Equal(legacyCatalog, paths.CatalogPath);
        Assert.Equal(legacyPcConfig, paths.PcConfigPath);
        Assert.True(DevToolConfiguration.CatalogExists(paths));
    }

    /// <summary>A machine mid-rename has both files. The one the rename produced
    /// is the one that is read — otherwise the rename would appear to have done
    /// nothing.</summary>
    [Fact]
    public async Task The_new_name_wins_when_both_are_on_disk()
    {
        var root = CreateTempToolConfigRoot();
        await File.WriteAllTextAsync(Path.Combine(root, ".tools", "copilot-tools.json"), """{ "plugins": [] }""");
        await File.WriteAllTextAsync(Path.Combine(root, ".tools", "ai-tools.json"), """{ "plugins": [] }""");

        var paths = DevToolConfigurationPaths.FromRepositoryRoot(root, "dev-pc");

        Assert.Equal(Path.Combine(root, ".tools", "ai-tools.json"), paths.CatalogPath);
    }

    /// <summary>The walk that finds a repository's catalog stops on either name.
    /// A repository nobody has renamed yet is still a repository with a catalog in
    /// it, and walking past it would land on whichever ancestor happened to have
    /// one.</summary>
    [Fact]
    public async Task The_root_walk_stops_on_a_catalog_under_the_old_name()
    {
        var root = CreateTempToolConfigRoot();
        var legacyCatalog = Path.Combine(root, ".tools", "copilot-tools.json");
        await File.WriteAllTextAsync(legacyCatalog, """{ "plugins": [], "mcpServers": [] }""");
        var nestedStartPath = Path.Combine(root, "src", "App", "Backlog.Desktop", "bin", "Debug");
        Directory.CreateDirectory(nestedStartPath);

        var paths = DevToolConfigurationPaths.CreateDefault("dev-pc", nestedStartPath);

        Assert.Equal(legacyCatalog, paths.CatalogPath);
    }

    /// <summary>Creating one always writes the new name: a machine with no catalog
    /// has nothing to stay compatible with.</summary>
    [Fact]
    public async Task Creating_a_catalog_writes_the_new_name()
    {
        var root = Path.Combine(Path.GetTempPath(), "backlog-tool-tests", Guid.NewGuid().ToString("N"));
        var paths = DevToolConfigurationPaths.FromRepositoryRoot(root, "dev-pc");

        await DevToolConfiguration.CreateCatalogAsync(paths);

        Assert.Equal("ai-tools.json", Path.GetFileName(paths.CatalogPath));
        Assert.True(File.Exists(Path.Combine(root, ".tools", "ai-tools.json")));
    }

    [Fact]
    public async Task Creating_a_catalog_makes_the_folder_and_both_arrays()
    {
        // Deliberately not CreateTempToolConfigRoot: a machine that has never had
        // a catalog has no .tools folder either, and creating one has to make it.
        var root = Path.Combine(Path.GetTempPath(), "backlog-tool-tests", Guid.NewGuid().ToString("N"));
        var paths = DevToolConfigurationPaths.FromRepositoryRoot(root, "dev-pc");

        Assert.False(DevToolConfiguration.CatalogExists(paths));

        await DevToolConfiguration.CreateCatalogAsync(paths);

        Assert.True(DevToolConfiguration.CatalogExists(paths));

        var config = await DevToolConfiguration.ReadAsync(paths);
        Assert.Empty(config.Root["plugins"]!.AsArray());
        Assert.Empty(config.Root["mcpServers"]!.AsArray());
    }

    [Fact]
    public async Task Creating_a_catalog_over_one_that_exists_is_refused_and_changes_nothing()
    {
        var root = CreateTempToolConfigRoot();
        var paths = DevToolConfigurationPaths.FromRepositoryRoot(root, "dev-pc");
        await File.WriteAllTextAsync(paths.CatalogPath, """
            { "plugins": [ { "name": "architecture", "source": "JSdotNet/Copilot:plugins/architecture", "enabled": true } ], "mcpServers": [] }
            """);
        var before = await File.ReadAllBytesAsync(paths.CatalogPath);

        await Assert.ThrowsAsync<InvalidOperationException>(() => DevToolConfiguration.CreateCatalogAsync(paths));

        Assert.Equal(before, await File.ReadAllBytesAsync(paths.CatalogPath));
    }

    [Fact]
    public async Task Adding_a_tool_lands_in_the_catalog_and_never_in_the_pc_config()
    {
        var paths = await CreateCatalogWithAsync("""{ "plugins": [], "mcpServers": [] }""");

        await DevToolConfiguration.AddToCatalogAsync(
            paths,
            new DevToolDraft(DevToolKind.Plugin, "architecture", "JSdotNet/Copilot:plugins/architecture", PluginKind: "repository-skills"));
        await DevToolConfiguration.AddToCatalogAsync(
            paths,
            new DevToolDraft(DevToolKind.McpServer, "JSdotNet.MCP.Guidelines", DisplayName: "guidelines"));

        var config = await DevToolConfiguration.ReadAsync(paths);
        var plugin = Assert.Single(config.Root["plugins"]!.AsArray())!;
        Assert.Equal("architecture", plugin["name"]!.GetValue<string>());
        Assert.Equal("JSdotNet/Copilot:plugins/architecture", plugin["source"]!.GetValue<string>());
        Assert.Equal("repository-skills", plugin["kind"]!.GetValue<string>());

        // New entries arrive enabled: adding a tool is the act of asking for it.
        Assert.True(plugin["enabled"]!.GetValue<bool>());

        var server = Assert.Single(config.Root["mcpServers"]!.AsArray())!;
        Assert.Equal("JSdotNet.MCP.Guidelines", server["packageId"]!.GetValue<string>());
        Assert.Equal("guidelines", server["name"]!.GetValue<string>());

        // The merge drops a PC entry with no catalog match, so a tool written
        // there would be a tool that never appears.
        Assert.False(File.Exists(paths.PcConfigPath));
    }

    [Fact]
    public async Task Adding_an_id_that_is_already_in_the_catalog_is_refused()
    {
        var paths = await CreateCatalogWithAsync("""
            { "plugins": [ { "name": "architecture", "source": "a", "enabled": true } ], "mcpServers": [] }
            """);

        // Case-insensitively, matching every lookup in the catalog: two entries
        // differing only in case would be one tool with two rows.
        var refused = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            DevToolConfiguration.AddToCatalogAsync(paths, new DevToolDraft(DevToolKind.Plugin, "Architecture", "b")));

        Assert.Contains("already", refused.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Single((await DevToolConfiguration.ReadAsync(paths)).Root["plugins"]!.AsArray());
    }

    [Fact]
    public async Task A_plugin_without_a_source_is_refused()
    {
        var paths = await CreateCatalogWithAsync("""{ "plugins": [], "mcpServers": [] }""");

        var refused = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            DevToolConfiguration.AddToCatalogAsync(paths, new DevToolDraft(DevToolKind.Plugin, "architecture", "   ")));

        Assert.Contains("source", refused.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty((await DevToolConfiguration.ReadAsync(paths)).Root["plugins"]!.AsArray());
    }

    [Fact]
    public async Task Adding_before_there_is_a_catalog_says_to_create_one()
    {
        var root = CreateTempToolConfigRoot();
        var paths = DevToolConfigurationPaths.FromRepositoryRoot(root, "dev-pc");

        var refused = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            DevToolConfiguration.AddToCatalogAsync(paths, new DevToolDraft(DevToolKind.Plugin, "architecture", "a")));

        Assert.Contains("Create it first", refused.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(paths.CatalogPath));
    }

    [Fact]
    public async Task Removing_a_tool_drops_its_catalog_entry_and_its_pc_override()
    {
        var paths = await CreateCatalogWithAsync("""
            {
              "plugins": [
                { "name": "architecture", "source": "a", "enabled": true },
                { "name": "qa", "source": "b", "enabled": true }
              ],
              "mcpServers": []
            }
            """);
        await DevToolConfiguration.WriteEnabledOverrideAsync(paths, "plugin:architecture", false);

        await DevToolConfiguration.RemoveFromCatalogAsync(paths, "plugin:architecture");
        await DevToolConfiguration.RemoveEnabledOverrideAsync(paths, "plugin:architecture");

        var config = await DevToolConfiguration.ReadAsync(paths);
        var remaining = Assert.Single(config.Root["plugins"]!.AsArray())!;
        Assert.Equal("qa", remaining["name"]!.GetValue<string>());

        // The point of pruning the override: add it back and it comes back
        // enabled rather than carrying a disable nobody remembers making.
        await DevToolConfiguration.AddToCatalogAsync(paths, new DevToolDraft(DevToolKind.Plugin, "architecture", "a"));
        var reread = await DevToolConfiguration.ReadAsync(paths);
        var readded = reread.Root["plugins"]!.AsArray()
            .Single(node => node!["name"]!.GetValue<string>() == "architecture")!;
        Assert.True(readded["enabled"]!.GetValue<bool>());
    }

    [Fact]
    public async Task Removing_a_pc_override_when_there_is_no_pc_config_does_nothing()
    {
        var paths = await CreateCatalogWithAsync("""{ "plugins": [], "mcpServers": [] }""");

        await DevToolConfiguration.RemoveEnabledOverrideAsync(paths, "plugin:architecture");

        Assert.False(File.Exists(paths.PcConfigPath));
    }

    [Fact]
    public async Task Removing_a_tool_that_is_not_there_fails_without_touching_the_catalog()
    {
        var paths = await CreateCatalogWithAsync("""{ "plugins": [], "mcpServers": [] }""");
        var before = await File.ReadAllBytesAsync(paths.CatalogPath);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            DevToolConfiguration.RemoveFromCatalogAsync(paths, "plugin:architecture"));

        Assert.Equal(before, await File.ReadAllBytesAsync(paths.CatalogPath));
    }

    [Fact]
    public async Task Importing_replaces_the_catalog_and_keeps_the_previous_one_beside_it()
    {
        var paths = await CreateCatalogWithAsync("""
            { "plugins": [ { "name": "architecture", "source": "a", "enabled": true } ], "mcpServers": [] }
            """);
        var before = await File.ReadAllTextAsync(paths.CatalogPath);

        await DevToolConfiguration.ImportCatalogAsync(paths, """
            { "plugins": [ { "name": "qa", "source": "b", "enabled": false } ], "mcpServers": [] }
            """);

        var config = await DevToolConfiguration.ReadAsync(paths);
        var plugin = Assert.Single(config.Root["plugins"]!.AsArray())!;

        // A replace and not a merge: the entry the imported file does not carry
        // is gone rather than kept.
        Assert.Equal("qa", plugin["name"]!.GetValue<string>());
        Assert.Equal(before, await File.ReadAllTextAsync(paths.CatalogPath + ".bak"));
    }

    [Fact]
    public async Task Importing_invalid_json_leaves_the_previous_catalog_byte_identical()
    {
        var paths = await CreateCatalogWithAsync("""
            { "plugins": [ { "name": "architecture", "source": "a", "enabled": true } ], "mcpServers": [] }
            """);
        var before = await File.ReadAllBytesAsync(paths.CatalogPath);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            DevToolConfiguration.ImportCatalogAsync(paths, "{ not json at all"));

        Assert.Equal(before, await File.ReadAllBytesAsync(paths.CatalogPath));

        // Nothing was replaced, so nothing was backed up either.
        Assert.False(File.Exists(paths.CatalogPath + ".bak"));
    }

    [Theory]
    [InlineData("""{ "tools": [] }""", "mcpServers")]
    [InlineData("""[ { "name": "architecture" } ]""", "object")]
    [InlineData("""{ "plugins": [ { "source": "a" } ] }""", "name")]
    [InlineData("""{ "mcpServers": [ { "name": "guidelines" } ] }""", "packageId")]
    public void A_document_that_is_not_a_catalog_is_refused_with_a_reason(string json, string expected)
    {
        Assert.False(DevToolConfiguration.TryReadCatalog(json, out _, out var error));
        Assert.Contains(expected, error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_catalog_with_only_one_of_the_four_arrays_is_accepted()
    {
        // The bar is deliberately low. A hand-edited catalog that has only
        // grown plugins so far is a real file, not a malformed one.
        Assert.True(DevToolConfiguration.TryReadCatalog(
            """{ "plugins": [ { "name": "architecture", "source": "a" } ] }""",
            out var root,
            out _));

        Assert.Single(root["plugins"]!.AsArray());
    }

    private static async Task<DevToolConfigurationPaths> CreateCatalogWithAsync(string json)
    {
        var root = CreateTempToolConfigRoot();
        var paths = DevToolConfigurationPaths.FromRepositoryRoot(root, "dev-pc");
        await File.WriteAllTextAsync(paths.CatalogPath, json);
        return paths;
    }

    private static string CreateTempToolConfigRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), "backlog-tool-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(path, ".tools"));
        return path;
    }
}

/// <summary>
/// What the Claude CLI prints, read back as the facts the pane branches on.
///
/// <para>Every fixture here is real CLI output rather than something a fake
/// process was told to say, which is the whole reason these parsers live in the
/// abstractions beside the catalog format instead of in the desktop adapter: a
/// fixture can be wrong in the same way the CLI is, and a fake cannot.</para>
/// </summary>
public class ClaudeToolOutputTests
{
    private const string PluginListJson = """
        [
          { "id": "architecture@jsdotnet-copilot", "name": "architecture", "version": "1.2.0", "enabled": true },
          { "id": "qa@jsdotnet-copilot", "name": "qa", "version": "0.9.0", "enabled": false },
          { "id": "documentation@jsdotnet-copilot", "name": "documentation" }
        ]
        """;

    [Fact]
    public void An_installed_claude_plugin_is_keyed_by_its_marketplace_qualified_id()
    {
        var plugins = DevToolOutput.ParseClaudePluginList(PluginListJson);

        // Case-insensitively, matching every other lookup against the catalog: the
        // catalog spells names the way a person typed them and the CLI does not
        // promise to agree.
        Assert.Equal("1.2.0", plugins["ARCHITECTURE@JSDOTNET-COPILOT"].Version);
        Assert.True(plugins["architecture@jsdotnet-copilot"].Enabled);
    }

    /// <summary>Claude keeps its own on/off switch beside the install. An update
    /// that leaves a switched-off plugin switched off is an update nobody can see
    /// the result of, so the flag travels with the version.</summary>
    [Fact]
    public void A_plugin_claude_has_switched_off_is_still_installed()
    {
        var plugins = DevToolOutput.ParseClaudePluginList(PluginListJson);

        Assert.False(plugins["qa@jsdotnet-copilot"].Enabled);
        Assert.Equal("0.9.0", plugins["qa@jsdotnet-copilot"].Version);
    }

    /// <summary>"Here but unversioned" is not "not here". A plugin installed from
    /// a marketplace need not declare a version, and reporting it absent would
    /// offer an install for something already on the machine.</summary>
    [Fact]
    public void A_plugin_with_no_version_reads_as_installed()
    {
        var plugins = DevToolOutput.ParseClaudePluginList(PluginListJson);

        Assert.Equal(DevToolOutput.Installed, plugins["documentation@jsdotnet-copilot"].Version);
    }

    /// <summary>The CLI ships on its own cadence and prints warnings and login
    /// prompts down the same stream. None of that is a reason to take the tools
    /// tab down.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Warning: not logged in.")]
    [InlineData("{ \"plugins\": [] }")]
    public void A_body_that_is_not_a_plugin_list_reads_as_nothing_installed(string body)
    {
        Assert.Empty(DevToolOutput.ParseClaudePluginList(body));
    }

    /// <summary>The same list, wrapped in an object. Both shapes have come out of
    /// the CLI, and reading both costs less than a release that empties every
    /// Claude row on the pane.</summary>
    [Fact]
    public void A_plugin_list_wrapped_in_an_object_is_read_the_same_way()
    {
        var plugins = DevToolOutput.ParseClaudePluginList("""
            { "plugins": [ { "id": "qa@jsdotnet-copilot", "version": "1.0.0" } ] }
            """);

        Assert.Equal("1.0.0", Assert.Single(plugins).Value.Version);
    }

    [Fact]
    public void Configured_marketplaces_are_read_by_name()
    {
        var names = DevToolOutput.ParseClaudeMarketplaceList("""
            [
              { "name": "jsdotnet-copilot", "source": { "source": "github", "repo": "JSdotNet/Copilot" } },
              { "name": "anthropic-skills", "source": { "source": "github", "repo": "anthropics/skills" } }
            ]
            """);

        Assert.Contains("JSdotNet-Copilot", names);
        Assert.Contains("anthropic-skills", names);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    public void A_body_that_is_not_a_marketplace_list_reads_as_none_configured(string body)
    {
        Assert.Empty(DevToolOutput.ParseClaudeMarketplaceList(body));
    }

    [Fact]
    public void A_registered_mcp_server_reports_its_scope_and_command()
    {
        var details = DevToolOutput.ParseClaudeMcpServer("""
            jsdotnet-coding-guidelines:
              Scope: User config (available in all your projects)
              Status: ✓ Connected
              Type: stdio
              Command: jsdotnet-guidelines-mcpserver
              Args:
              Environment:
            """);

        Assert.NotNull(details);
        Assert.Equal("jsdotnet-guidelines-mcpserver", details.Command);
        Assert.True(details.IsUserScope);
    }

    /// <summary>The CLI answers an unknown name with a sentence and a non-zero
    /// exit. That is the "needs registering" case — the one this whole feature
    /// exists for — and reading it as a failure would break the row that most
    /// needs to work.</summary>
    [Theory]
    [InlineData("No MCP server named \"jsdotnet-publish-results\" found. Configured servers: aspire")]
    [InlineData("Error: no mcp server named 'x' found")]
    public void An_absent_mcp_server_is_reported_as_absent_rather_than_as_a_failure(string output)
    {
        Assert.Null(DevToolOutput.ParseClaudeMcpServer(output));
    }

    /// <summary>A registration somebody made at project or local scope is theirs.
    /// A machine-wide sweep that quietly replaced it would be noticed later, in a
    /// project that had stopped working.</summary>
    [Theory]
    [InlineData("  Scope: Local config (private to you in this project)", false)]
    [InlineData("  Scope: Project config (shared via .mcp.json)", false)]
    [InlineData("  Scope: User config (available in all your projects)", true)]
    public void Only_a_user_scope_registration_is_ours_to_change(string scopeLine, bool expected)
    {
        var details = DevToolOutput.ParseClaudeMcpServer($"a-server:{Environment.NewLine}{scopeLine}{Environment.NewLine}  Command: a-command");

        Assert.NotNull(details);
        Assert.Equal(expected, details.IsUserScope);
    }

    [Theory]
    [InlineData("architecture", null, null, "jsdotnet-copilot", "architecture@jsdotnet-copilot")]
    [InlineData("jsdotnet-project-guidelines", "guidelines", null, "jsdotnet-copilot", "guidelines@jsdotnet-copilot")]
    [InlineData("architecture", null, "anthropic-skills", "jsdotnet-copilot", "architecture@anthropic-skills")]
    [InlineData("architecture", "  ", "  ", "jsdotnet-copilot", "architecture@jsdotnet-copilot")]
    public void A_claude_plugin_id_falls_back_to_the_name_and_the_first_marketplace(
        string name,
        string? claudeName,
        string? claudeMarketplace,
        string? defaultMarketplace,
        string expected)
    {
        Assert.Equal(expected, DevToolOutput.ClaudePluginId(name, claudeName, claudeMarketplace, defaultMarketplace));
    }

    /// <summary>A catalog with no marketplaces at all can resolve no Claude plugin
    /// id. Nothing rather than a throw, so the row says so beside the rows that
    /// resolved fine.</summary>
    [Fact]
    public void A_plugin_with_no_marketplace_anywhere_has_no_claude_id()
    {
        Assert.Null(DevToolOutput.ClaudePluginId("architecture", null, null, null));
        Assert.Null(DevToolOutput.ClaudePluginId("architecture", null, "   ", "  "));
    }
}

/// <summary>
/// Which hosts an entry is for, and what the row makes of two hosts disagreeing.
///
/// <para>Silence meaning "both" is the load-bearing rule: every catalog written
/// before Claude support says nothing about hosts, and reading that as
/// "Copilot only" would have dropped all of them out of the Claude half without a
/// single file changing.</para>
/// </summary>
public class DevToolHostTests
{
    [Fact]
    public void An_entry_with_no_hosts_property_is_for_both()
    {
        var entry = JsonNode.Parse("""{ "name": "architecture" }""");

        Assert.Equal(DevToolHosts.Default, DevToolConfiguration.ParseHosts(entry));
    }

    [Theory]
    [InlineData("""{ "hosts": [] }""", DevToolHosts.Default)]
    [InlineData("""{ "hosts": [ "  " ] }""", DevToolHosts.Default)]
    [InlineData("""{ "hosts": [ "copilot" ] }""", DevToolHosts.Copilot)]
    [InlineData("""{ "hosts": [ "claude" ] }""", DevToolHosts.Claude)]
    [InlineData("""{ "hosts": [ "Copilot", "CLAUDE" ] }""", DevToolHosts.Default)]
    public void The_hosts_array_is_read_case_insensitively(string json, DevToolHosts expected)
    {
        Assert.Equal(expected, DevToolConfiguration.ParseHosts(JsonNode.Parse(json)));
    }

    /// <summary>A host name this version has not met is ignored rather than
    /// rejected. A catalog written for a third host still installs its Copilot and
    /// Claude entries here.</summary>
    [Fact]
    public void An_unknown_host_name_is_ignored()
    {
        Assert.Equal(
            DevToolHosts.Claude,
            DevToolConfiguration.ParseHosts(JsonNode.Parse("""{ "hosts": [ "claude", "cursor" ] }""")));
    }

    /// <summary>One press acts on every host the entry targets, so an update
    /// waiting on either one is an update worth a button.</summary>
    [Fact]
    public void An_update_on_one_host_makes_the_row_updateable()
    {
        var tool = Tool(
            new DevToolHostState(DevToolHosts.Copilot, true, "1.0.0", "1.0.0", "Enabled plugin"),
            new DevToolHostState(DevToolHosts.Claude, true, "1.0.0", "1.1.0", "Update available"));

        Assert.True(tool.CanUpdate);
        Assert.True(tool.UpdateAvailable);
    }

    /// <summary>A plugin Copilot already has and Claude has never heard of is
    /// still a plugin this machine is short of.</summary>
    [Fact]
    public void A_tool_missing_on_one_host_can_still_be_installed()
    {
        var tool = Tool(
            new DevToolHostState(DevToolHosts.Copilot, true, "1.0.0", "1.0.0", "Enabled plugin"),
            new DevToolHostState(DevToolHosts.Claude, false, "not installed", "1.0.0", "Not installed"));

        Assert.True(tool.CanInstall);
        Assert.False(tool.CanUpdate);
    }

    [Fact]
    public void A_tool_both_hosts_have_at_the_published_version_offers_nothing()
    {
        var tool = Tool(
            new DevToolHostState(DevToolHosts.Copilot, true, "1.0.0", "1.0.0", "Enabled plugin"),
            new DevToolHostState(DevToolHosts.Claude, true, "1.0.0", "1.0.0", "Enabled plugin"));

        Assert.False(tool.CanUpdate);
        Assert.False(tool.CanInstall);
        Assert.True(tool.AvailableVersionKnown);
    }

    /// <summary>The old shape still behaves the way it always did. Every existing
    /// caller builds one of these positionally with no host states at all, and a
    /// derived property that only read the new field would have quietly stopped
    /// answering for all of them.</summary>
    [Fact]
    public void A_row_with_no_host_states_falls_back_to_its_single_values()
    {
        var tool = new DevToolInfo(
            "plugin:architecture",
            DevToolKind.Plugin,
            "architecture",
            "a",
            ConfiguredEnabled: true,
            Installed: true,
            "1.0.0",
            "1.1.0",
            "Update available");

        Assert.Equal(DevToolHosts.Default, tool.Hosts);
        Assert.Empty(tool.HostStates);
        Assert.True(tool.CanUpdate);
        Assert.False(tool.CanInstall);
    }

    private static DevToolInfo Tool(params DevToolHostState[] states) => new(
        "plugin:architecture",
        DevToolKind.Plugin,
        "architecture",
        "JSdotNet/Copilot:plugins/architecture",
        ConfiguredEnabled: true,
        Installed: states.All(state => state.Installed),
        "1.0.0",
        "1.0.0",
        "Enabled plugin")
    {
        Hosts = DevToolHosts.Default,
        HostStates = states
    };
}

/// <summary>
/// The catalog shapes Claude support added: the marketplace array, the per-entry
/// hosts filter, and the <c>claude</c> section on an MCP server.
/// </summary>
public class ClaudeCatalogTests
{
    [Fact]
    public void A_marketplace_key_round_trips_through_the_catalog_reader()
    {
        var key = DevToolConfiguration.KeyFor(DevToolKind.Marketplace, "jsdotnet-copilot");

        Assert.Equal("marketplace:jsdotnet-copilot", key);

        var (arrayName, idName, idValue) = DevToolConfiguration.ParseKey(key);

        Assert.Equal("claude.marketplaces", arrayName);
        Assert.Equal("name", idName);
        Assert.Equal("jsdotnet-copilot", idValue);
    }

    /// <summary>The first one in the array, because that is the only thing that
    /// makes it the default. Read in one place rather than every caller deciding
    /// that <c>[0]</c> is meaningful.</summary>
    [Fact]
    public void The_first_marketplace_is_the_default_one()
    {
        var root = JsonNode.Parse("""
            {
              "claude": {
                "marketplaces": [
                  { "name": "jsdotnet-copilot", "source": "JSdotNet/Copilot" },
                  { "name": "anthropic-skills", "source": "anthropics/skills" }
                ]
              }
            }
            """);

        Assert.Equal("jsdotnet-copilot", DevToolConfiguration.DefaultMarketplaceName(root));
        Assert.Equal(2, DevToolConfiguration.MarketplaceEntries(root).Count());
    }

    [Fact]
    public void A_catalog_with_no_marketplaces_has_no_default()
    {
        Assert.Null(DevToolConfiguration.DefaultMarketplaceName(JsonNode.Parse("""{ "plugins": [] }""")));
        Assert.Empty(DevToolConfiguration.MarketplaceEntries(JsonNode.Parse("""{ "plugins": [] }""")));
    }

    /// <summary>Marketplaces are content on their own: adding one is how a machine
    /// that installs only Claude plugins is bootstrapped, and refusing that
    /// catalog would leave the import unable to do it.</summary>
    [Fact]
    public void A_catalog_that_is_only_marketplaces_is_accepted()
    {
        Assert.True(DevToolConfiguration.TryReadCatalog(
            """{ "claude": { "marketplaces": [ { "name": "jsdotnet-copilot", "source": "JSdotNet/Copilot" } ] } }""",
            out var root,
            out _));

        Assert.Equal("jsdotnet-copilot", DevToolConfiguration.DefaultMarketplaceName(root));
    }

    [Fact]
    public void A_marketplace_without_a_name_is_refused_with_a_reason()
    {
        Assert.False(DevToolConfiguration.TryReadCatalog(
            """{ "claude": { "marketplaces": [ { "source": "JSdotNet/Copilot" } ] } }""",
            out _,
            out var error));

        Assert.Contains("name", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Adding_a_marketplace_lands_under_the_claude_section()
    {
        var paths = await CreateCatalogWithAsync("""{ "plugins": [], "mcpServers": [] }""");

        await DevToolConfiguration.AddToCatalogAsync(
            paths,
            new DevToolDraft(DevToolKind.Marketplace, "jsdotnet-copilot", "JSdotNet/Copilot"));

        var config = await DevToolConfiguration.ReadAsync(paths);
        var marketplace = Assert.Single(DevToolConfiguration.MarketplaceEntries(config.Root));

        Assert.Equal("jsdotnet-copilot", marketplace["name"]!.GetValue<string>());
        Assert.Equal("JSdotNet/Copilot", marketplace["source"]!.GetValue<string>());

        // No enabled flag: a marketplace is not a tool this machine opts into, it
        // is where the Claude plugins that do come from.
        Assert.Null(marketplace["enabled"]);
    }

    [Fact]
    public async Task A_marketplace_without_a_source_is_refused()
    {
        var paths = await CreateCatalogWithAsync("""{ "plugins": [] }""");

        var refused = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            DevToolConfiguration.AddToCatalogAsync(paths, new DevToolDraft(DevToolKind.Marketplace, "jsdotnet-copilot", "  ")));

        Assert.Contains("source", refused.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Removing_a_marketplace_drops_it_from_the_claude_section()
    {
        var paths = await CreateCatalogWithAsync("""
            {
              "claude": { "marketplaces": [ { "name": "jsdotnet-copilot", "source": "JSdotNet/Copilot" } ] },
              "plugins": []
            }
            """);

        await DevToolConfiguration.RemoveFromCatalogAsync(paths, "marketplace:jsdotnet-copilot");

        var config = await DevToolConfiguration.ReadAsync(paths);
        Assert.Empty(DevToolConfiguration.MarketplaceEntries(config.Root));
    }

    /// <summary>Both hosts is what the format means by silence, so writing the
    /// property out would add a line that changes nothing and invite the reader to
    /// wonder why the entry beside it lacks one.</summary>
    [Fact]
    public async Task A_plugin_for_both_hosts_is_written_without_a_hosts_property()
    {
        var paths = await CreateCatalogWithAsync("""{ "plugins": [] }""");

        await DevToolConfiguration.AddToCatalogAsync(paths, new DevToolDraft(DevToolKind.Plugin, "architecture", "a"));

        var config = await DevToolConfiguration.ReadAsync(paths);
        Assert.Null(Assert.Single(config.Root["plugins"]!.AsArray())!["hosts"]);
    }

    [Fact]
    public async Task A_claude_only_plugin_carries_its_hosts_and_its_claude_names()
    {
        var paths = await CreateCatalogWithAsync("""{ "plugins": [] }""");

        await DevToolConfiguration.AddToCatalogAsync(
            paths,
            new DevToolDraft(DevToolKind.Plugin, "jsdotnet-project-guidelines", "JSdotNet/Copilot:plugins/guidelines")
            {
                Hosts = DevToolHosts.Claude,
                ClaudeName = "guidelines",
                ClaudeMarketplace = "anthropic-skills"
            });

        var config = await DevToolConfiguration.ReadAsync(paths);
        var plugin = Assert.Single(config.Root["plugins"]!.AsArray())!;

        Assert.Equal(["claude"], plugin["hosts"]!.AsArray().Select(node => node!.GetValue<string>()));
        Assert.Equal("guidelines", plugin["claudeName"]!.GetValue<string>());
        Assert.Equal("anthropic-skills", plugin["claudeMarketplace"]!.GetValue<string>());
        Assert.Equal(DevToolHosts.Claude, DevToolConfiguration.ParseHosts(plugin));
    }

    [Fact]
    public async Task An_mcp_server_with_a_claude_command_carries_its_registration()
    {
        var paths = await CreateCatalogWithAsync("""{ "mcpServers": [] }""");

        await DevToolConfiguration.AddToCatalogAsync(
            paths,
            new DevToolDraft(DevToolKind.McpServer, "JSdotNet.MCP.Guidelines", DisplayName: "jsdotnet-project-guidelines")
            {
                ClaudeServerName = "jsdotnet-coding-guidelines",
                ClaudeCommand = "jsdotnet-guidelines-mcpserver",
                ClaudeArgs = ["--stdio"]
            });

        var config = await DevToolConfiguration.ReadAsync(paths);
        var claude = Assert.Single(config.Root["mcpServers"]!.AsArray())!["claude"]!;

        Assert.Equal("jsdotnet-coding-guidelines", claude["name"]!.GetValue<string>());
        Assert.Equal("jsdotnet-guidelines-mcpserver", claude["command"]!.GetValue<string>());
        Assert.Equal(["--stdio"], claude["args"]!.AsArray().Select(node => node!.GetValue<string>()));
    }

    /// <summary>The shared .NET tool install is still the whole point of an MCP
    /// server entry. One with no Claude command installs the tool and registers
    /// nothing, rather than being refused.</summary>
    [Fact]
    public async Task An_mcp_server_without_a_claude_command_gets_no_claude_section()
    {
        var paths = await CreateCatalogWithAsync("""{ "mcpServers": [] }""");

        await DevToolConfiguration.AddToCatalogAsync(
            paths,
            new DevToolDraft(DevToolKind.McpServer, "JSdotNet.MCP.Guidelines") { ClaudeServerName = "guidelines" });

        var config = await DevToolConfiguration.ReadAsync(paths);
        Assert.Null(Assert.Single(config.Root["mcpServers"]!.AsArray())!["claude"]);
    }

    private static async Task<DevToolConfigurationPaths> CreateCatalogWithAsync(string json)
    {
        var root = Path.Combine(Path.GetTempPath(), "backlog-tool-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, ".tools"));
        var paths = DevToolConfigurationPaths.FromRepositoryRoot(root, "dev-pc");
        await File.WriteAllTextAsync(paths.CatalogPath, json);
        return paths;
    }
}

/// <summary>
/// The command log a host may attach to what it answers with. It is additive on
/// purpose — a host that runs no processes still constructs these results the
/// way it always did — so what is asserted here is that "no commands" is an
/// empty list rather than a null nobody remembered to guard.
/// </summary>
public class DevToolCommandTests
{
    [Fact]
    public void A_catalog_built_without_commands_has_an_empty_log()
    {
        var catalog = new DevToolCatalog([], "Nothing was checked.");

        Assert.Empty(catalog.Commands);
    }

    [Fact]
    public void A_result_built_without_commands_has_an_empty_log()
    {
        Assert.Empty(DevToolActionResult.Ok("Done.").Commands);
        Assert.Empty(DevToolActionResult.Failed("Not done.").Commands);
        Assert.Empty(new DevToolActionResult(true, "Done.").Commands);
    }

    [Fact]
    public void A_result_carries_the_commands_it_is_handed()
    {
        DevToolCommand[] commands =
        [
            new("dotnet tool list --global", 0, "Package Id      Version"),
            new("dotnet tool search missing --exact-match", 1, "No packages found.")
        ];

        Assert.Equal(commands, DevToolActionResult.Ok("Done.", commands).Commands);
        Assert.Equal(commands, DevToolActionResult.Failed("Not done.", commands).Commands);
    }
}

public class UnsupportedDevToolServiceTests
{
    [Fact]
    public async Task Listing_returns_a_clear_unsupported_message()
    {
        var service = new UnsupportedDevToolService();

        var catalog = await service.ListAsync();

        Assert.Empty(catalog.Tools);
        Assert.Contains("desktop app", catalog.Message, StringComparison.OrdinalIgnoreCase);

        // The pane draws every editing affordance behind this flag, so a host that
        // cannot write the catalog draws none of them rather than four that refuse.
        Assert.False(catalog.CanEditCatalog);
        Assert.False(catalog.CatalogExists);
    }

    [Fact]
    public async Task Creating_a_catalog_is_refused()
    {
        var result = await new UnsupportedDevToolService().CreateCatalogAsync();

        Assert.False(result.Succeeded);
        Assert.Contains("desktop app", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Adding_a_tool_is_refused()
    {
        var result = await new UnsupportedDevToolService()
            .AddAsync(new DevToolDraft(DevToolKind.Plugin, "architecture", "a"));

        Assert.False(result.Succeeded);
        Assert.Contains("desktop app", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Removing_a_tool_is_refused()
    {
        var result = await new UnsupportedDevToolService().RemoveAsync("plugin:architecture");

        Assert.False(result.Succeeded);
        Assert.Contains("desktop app", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Importing_a_catalog_is_refused()
    {
        var result = await new UnsupportedDevToolService().ImportAsync("""{ "plugins": [] }""");

        Assert.False(result.Succeeded);
        Assert.Contains("desktop app", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(DevToolAction.Update)]
    [InlineData(DevToolAction.Enable)]
    [InlineData(DevToolAction.Disable)]
    public async Task Actions_fail_without_throwing(DevToolAction action)
    {
        var service = new UnsupportedDevToolService();

        var result = action switch
        {
            DevToolAction.Update => await service.UpdateAsync("plugin:test"),
            DevToolAction.Enable => await service.EnableAsync("plugin:test"),
            DevToolAction.Disable => await service.DisableAsync("plugin:test"),
            _ => throw new ArgumentOutOfRangeException(nameof(action))
        };

        Assert.False(result.Succeeded);
        Assert.False(string.IsNullOrWhiteSpace(result.Message));
    }
}

/// <summary>
/// When a difference between two versions is an update to offer.
///
/// <para>Every fixture here is a version string this machine really printed.
/// Two of them are the reason the comparison stopped being a string inequality:
/// an MSIX app and a click-to-run suite both self-update on their own channel,
/// so their installed version routinely reads <em>ahead</em> of the winget
/// manifest, and "differs" announced an update for both of them on every check
/// forever.</para>
/// </summary>
public class DevToolVersionComparisonTests
{
    /// <summary>The two real inversions, by name. Neither is an error and
    /// neither is an update — the installed side is simply further along.</summary>
    [Theory]
    [InlineData("1.34493.1.0", "1.30096.1")]            // Anthropic.Claude: MSIX ahead of the winget exe manifest
    [InlineData("16.0.20326.20100", "16.0.20228.20124")] // Microsoft.Office: C2R ahead of the manifest
    public void An_installed_version_ahead_of_the_available_one_is_not_an_update(string installed, string available) =>
        Assert.False(DevToolInfo.VersionDiffers(installed, available));

    [Theory]
    [InlineData("1.30096.1", "1.34493.1.0")]
    [InlineData("1.2.3", "1.2.4")]
    [InlineData("v1.0.65", "v1.0.80")]
    [InlineData("1.0.65", "v1.0.80")]
    public void A_newer_available_version_is_an_update(string installed, string available) =>
        Assert.True(DevToolInfo.VersionDiffers(installed, available));

    /// <summary>Absent components are zero, so the same version written to two
    /// widths is one version. winget prints both forms for the same package.</summary>
    [Theory]
    [InlineData("1.2", "1.2.0")]
    [InlineData("1.2.0.0", "1.2")]
    public void The_same_number_at_two_widths_is_the_same_version(string installed, string available) =>
        Assert.False(DevToolInfo.VersionDiffers(installed, available));

    /// <summary>Real version strings that are not dotted numbers at all. None of
    /// them may throw, and none of them can be ordered — so the answer falls back
    /// to the inequality this comparison used to be.</summary>
    [Theory]
    [InlineData("2.54.0.windows.1", "2.55.0.windows.1", true)]
    [InlineData("2.54.0.windows.1", "2.54.0.windows.1", false)]
    [InlineData("13.5.2+a22cec24", "13.5.2+a22cec24", false)]
    [InlineData("13.5.2+a22cec24", "13.6.0+b1c2d3e4", true)]
    [InlineData("2.55.0.windows.1", "2.54.0.windows.1", true)]
    public void A_version_that_is_not_a_dotted_number_compares_as_text(string installed, string available, bool expected) =>
        Assert.Equal(expected, DevToolInfo.VersionDiffers(installed, available));

    /// <summary>A repository-backed row puts two short commits in the version
    /// columns, and a commit that happens to be all digits is not a number: the
    /// remote one sorting lower than the local one is a pending update, not a
    /// machine that is ahead.</summary>
    [Fact]
    public void Two_commits_that_are_all_digits_still_differ() =>
        Assert.True(DevToolInfo.VersionDiffers("9921470", "1234560"));

    [Theory]
    [InlineData("unknown", "1.2.3")]
    [InlineData("1.2.3", "unknown")]
    [InlineData("not installed", "1.2.3")]
    [InlineData("1.2.3", "—")]
    [InlineData("configured", "source")]
    [InlineData("", "1.2.3")]
    public void A_column_that_holds_no_version_is_never_an_update(string installed, string available) =>
        Assert.False(DevToolInfo.VersionDiffers(installed, available));
}

/// <summary>
/// The <c>applications</c> array: the machine's own software inventory, beside
/// the two arrays that were only ever about AI tooling.
/// </summary>
public class ApplicationCatalogTests
{
    /// <summary>Every kind, because the catch-all this replaced minted an
    /// <c>mcp:</c> key for anything it had not been told about — which
    /// <see cref="DevToolConfiguration.ParseKey"/> then resolved to the wrong
    /// array, quietly, for a kind nobody had thought about yet.</summary>
    [Theory]
    [InlineData(DevToolKind.Plugin, "plugin:architecture", "plugins", "name")]
    [InlineData(DevToolKind.McpServer, "mcp:JSdotNet.MCP.Guidelines", "mcpServers", "packageId")]
    [InlineData(DevToolKind.Marketplace, "marketplace:jsdotnet-copilot", "claude.marketplaces", "name")]
    [InlineData(DevToolKind.Application, "app:Microsoft.VisualStudioCode", "applications", "id")]
    public void Every_kind_round_trips_through_its_key(DevToolKind kind, string expectedKey, string expectedArray, string expectedIdName)
    {
        var id = expectedKey[(expectedKey.IndexOf(':') + 1)..];

        var key = DevToolConfiguration.KeyFor(kind, id);
        Assert.Equal(expectedKey, key);

        var (arrayName, idName, idValue) = DevToolConfiguration.ParseKey(key);
        Assert.Equal(expectedArray, arrayName);
        Assert.Equal(expectedIdName, idName);
        Assert.Equal(id, idValue);
    }

    /// <summary>AC1. Every catalog on every machine predates this array, and a
    /// machine that never grows one has to keep behaving exactly as it did.</summary>
    [Fact]
    public async Task A_catalog_with_no_applications_array_reads_exactly_as_before()
    {
        var paths = await CreateCatalogWithAsync("""
            {
              "plugins": [ { "name": "architecture", "source": "JSdotNet/Copilot:plugins/architecture", "enabled": true } ],
              "mcpServers": [ { "name": "guidelines", "packageId": "JSdotNet.MCP.Guidelines", "enabled": true } ]
            }
            """);

        var config = await DevToolConfiguration.ReadAsync(paths);

        Assert.Single(config.Root["plugins"]!.AsArray());
        Assert.Single(config.Root["mcpServers"]!.AsArray());
        Assert.Empty(DevToolConfiguration.ReadApplications(config.Root));

        // Reading must not invent the array either: the catalog is hand-edited and
        // a property nobody asked for appearing after a read is a diff to explain.
        Assert.Null(config.Root["applications"]);
    }

    [Fact]
    public void An_application_entry_is_read_into_its_provider_and_its_commands()
    {
        var root = JsonNode.Parse("""
            {
              "applications": [
                {
                  "id": "Microsoft.VisualStudioCode",
                  "name": "Visual Studio Code",
                  "provider": "winget",
                  "group": "Team developer tools",
                  "note": "Also on PATH as code.cmd.",
                  "detectOnly": true,
                  "probe": { "command": "code", "args": ["--version"], "shell": true, "encoding": "utf-16le" },
                  "enabled": true
                }
              ]
            }
            """)!;

        var application = Assert.Single(DevToolConfiguration.ReadApplications(root));

        Assert.Equal("Microsoft.VisualStudioCode", application.Id);
        Assert.Equal("Visual Studio Code", application.Name);
        Assert.Equal(DevToolProvider.Winget, application.Provider);
        Assert.Equal("Team developer tools", application.Group);
        Assert.Equal("Also on PATH as code.cmd.", application.Note);
        Assert.True(application.DetectOnly);
        Assert.True(application.Enabled);
        Assert.Equal("app:Microsoft.VisualStudioCode", application.Key);

        var probe = application.Probe!;
        Assert.Equal("code", probe.Command);
        Assert.Equal(["--version"], probe.Args);
        Assert.True(probe.Shell);
        Assert.Equal("utf-16le", probe.Encoding);
        Assert.Null(application.Detect);
        Assert.Null(application.Install);
    }

    /// <summary>An entry with no <c>install</c> is the checklist row: something to
    /// look for and nothing to press.</summary>
    [Fact]
    public void A_command_entry_carries_what_to_run_and_what_to_expect()
    {
        var root = JsonNode.Parse("""
            {
              "applications": [
                {
                  "id": "git-pull-rebase",
                  "name": "git pull.rebase = true",
                  "provider": "command",
                  "detect": { "command": "git", "args": ["config","--global","pull.rebase"], "expect": "true" },
                  "install": { "command": "git", "args": ["config","--global","pull.rebase","true"] },
                  "enabled": true
                },
                {
                  "id": "dev-drive",
                  "name": "Dev Drive configured",
                  "provider": "command",
                  "detect": { "command": "fsutil", "args": ["devdrv","query","D:"], "expect": "trusted Dev Drive" },
                  "enabled": true
                }
              ]
            }
            """)!;

        var applications = DevToolConfiguration.ReadApplications(root);

        Assert.Equal(DevToolProvider.Command, applications[0].Provider);
        Assert.Equal("true", applications[0].Detect!.Expect);
        Assert.Equal("git", applications[0].Install!.Command);
        Assert.False(applications[0].Detect!.Shell);

        Assert.Null(applications[1].Install);
        Assert.Equal("trusted Dev Drive", applications[1].Detect!.Expect);
    }

    /// <summary>A provider this version has not met is a row that must not be
    /// acted on and must not disappear. It lands on the one provider that runs
    /// nothing — and it says so, rather than being silently reclassified.</summary>
    [Theory]
    [InlineData("\"chocolatey\"", "chocolatey")]
    [InlineData("42", "")]
    [InlineData("null", "")]
    public void An_unreadable_provider_degrades_to_a_manual_row(string providerJson, string expectedDeclared)
    {
        var root = JsonNode.Parse($$"""
            {
              "applications": [
                { "id": "mystery", "provider": {{providerJson}}, "enabled": true },
                { "id": "Git.Git", "name": "Git", "provider": "winget", "enabled": true }
              ]
            }
            """)!;

        var applications = DevToolConfiguration.ReadApplications(root);

        Assert.Equal(2, applications.Count);
        Assert.Equal(DevToolProvider.Manual, applications[0].Provider);
        Assert.False(applications[0].ProviderRecognised);
        Assert.Equal(expectedDeclared, applications[0].DeclaredProvider);

        // The name falls back to the id, so an entry with neither still has
        // something to draw.
        Assert.Equal("mystery", applications[0].Name);

        // And the entry beside it is untouched: one bad row is one bad row.
        Assert.Equal(DevToolProvider.Winget, applications[1].Provider);
        Assert.True(applications[1].ProviderRecognised);
    }

    [Fact]
    public void A_provider_that_says_manual_is_recognised_as_one()
    {
        var root = JsonNode.Parse("""
            { "applications": [ { "id": "office-signed-in", "provider": "MANUAL", "enabled": true } ] }
            """)!;

        var application = Assert.Single(DevToolConfiguration.ReadApplications(root));

        Assert.Equal(DevToolProvider.Manual, application.Provider);
        Assert.True(application.ProviderRecognised);
    }

    /// <summary>The array is read for the pane, so one malformed entry cannot be
    /// allowed to take the other thirty rows down with it.</summary>
    [Fact]
    public void An_entry_that_is_not_a_row_is_skipped_rather_than_thrown_on()
    {
        var root = JsonNode.Parse("""
            {
              "applications": [
                { "provider": "winget", "enabled": true },
                "Git.Git",
                null,
                { "id": "   ", "provider": "winget" },
                { "id": "Git.Git", "provider": "winget", "enabled": true }
              ]
            }
            """)!;

        var application = Assert.Single(DevToolConfiguration.ReadApplications(root));

        Assert.Equal("Git.Git", application.Id);
    }

    [Fact]
    public void A_root_with_no_applications_at_all_reads_as_none()
    {
        Assert.Empty(DevToolConfiguration.ReadApplications(null));
        Assert.Empty(DevToolConfiguration.ReadApplications(JsonNode.Parse("""{ "applications": {} }""")));
    }

    /// <summary>The import bar is the same for the new array as for the two old
    /// ones — which is why a grouping marker in the catalog is a <c>group</c>
    /// property on a real entry and never an object of its own.</summary>
    [Fact]
    public void An_application_without_an_id_is_refused_by_the_import()
    {
        Assert.False(DevToolConfiguration.TryReadCatalog(
            """{ "plugins": [], "applications": [ { "name": "Step 7 - Team developer tools" } ] }""",
            out _,
            out var error));

        Assert.Contains("applications", error, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id", error, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A catalog that is only applications is a real file: a machine
    /// being set up from the HowTo has software to check before it has a single
    /// plugin.</summary>
    [Fact]
    public void A_catalog_that_is_only_applications_is_accepted()
    {
        Assert.True(DevToolConfiguration.TryReadCatalog(
            """{ "applications": [ { "id": "Git.Git", "provider": "winget", "enabled": true } ] }""",
            out var root,
            out _));

        Assert.Single(root["applications"]!.AsArray());
    }

    /// <summary>Without this the per-machine file merges for plugins and MCP
    /// servers and silently does nothing for applications — the same gap
    /// <c>claude.marketplaces</c> has, and one that reads as "the override did not
    /// save" rather than as a missing merge.</summary>
    [Fact]
    public async Task A_pc_override_wins_for_an_application()
    {
        var root = CreateTempToolConfigRoot();
        var paths = DevToolConfigurationPaths.FromRepositoryRoot(root, "dev-pc");
        await File.WriteAllTextAsync(paths.CatalogPath, """
            {
              "plugins": [],
              "applications": [
                { "id": "Docker.DockerDesktop", "name": "Docker Desktop", "provider": "winget", "enabled": true },
                { "id": "Git.Git", "name": "Git", "provider": "winget", "enabled": true }
              ]
            }
            """);
        Directory.CreateDirectory(Path.GetDirectoryName(paths.PcConfigPath)!);
        await File.WriteAllTextAsync(paths.PcConfigPath, """
            {
              "applications": [
                { "id": "Docker.DockerDesktop", "enabled": false },
                { "id": "Never.Heard.Of.It", "enabled": true }
              ]
            }
            """);

        var config = await DevToolConfiguration.ReadAsync(paths);
        var applications = DevToolConfiguration.ReadApplications(config.Root);

        Assert.False(applications[0].Enabled);
        Assert.True(applications[1].Enabled);
        Assert.DoesNotContain(applications, application => application.Id == "Never.Heard.Of.It");
    }

    /// <summary>A manual row has nothing to probe, so the only thing that can
    /// change its state is the person saying they did it — per machine, in the
    /// same file and the same shape the enabled override already uses.</summary>
    [Fact]
    public async Task An_acknowledgement_is_written_to_the_pc_config()
    {
        var root = CreateTempToolConfigRoot();
        var paths = DevToolConfigurationPaths.FromRepositoryRoot(root, "dev-pc");
        await File.WriteAllTextAsync(paths.CatalogPath, """
            {
              "plugins": [],
              "applications": [
                { "id": "office-signed-in", "name": "Signed in to Microsoft 365", "provider": "manual", "enabled": true }
              ]
            }
            """);

        await DevToolConfiguration.WriteAcknowledgementAsync(paths, "app:office-signed-in", true);

        var config = await DevToolConfiguration.ReadAsync(paths);
        var application = Assert.Single(DevToolConfiguration.ReadApplications(config.Root));

        Assert.True(application.Acknowledged);
        Assert.True(application.Enabled);

        var pcConfig = await File.ReadAllTextAsync(paths.PcConfigPath);
        Assert.Contains("\"id\": \"office-signed-in\"", pcConfig);
        Assert.Contains("\"acknowledged\": true", pcConfig);

        // Acknowledging says nothing about whether the machine wants the row.
        Assert.DoesNotContain("\"enabled\"", pcConfig, StringComparison.Ordinal);

        await DevToolConfiguration.WriteAcknowledgementAsync(paths, "app:office-signed-in", false);

        var reread = await DevToolConfiguration.ReadAsync(paths);
        Assert.False(DevToolConfiguration.ReadApplications(reread.Root)[0].Acknowledged);
    }

    [Fact]
    public void An_acknowledgement_reaches_the_row_it_is_drawn_from()
    {
        Assert.True(ManualRow() with { Acknowledged = true } is { Acknowledged: true });
        Assert.False(ManualRow().Acknowledged);
    }

    private static DevToolInfo ManualRow() => new(
        "app:office-signed-in",
        DevToolKind.Application,
        "Signed in to Microsoft 365",
        null,
        ConfiguredEnabled: true,
        Installed: false,
        DevToolOutput.NoVersion,
        DevToolOutput.NoVersion,
        "Manual check");

    [Fact]
    public async Task Adding_a_winget_application_writes_its_provider_and_nothing_it_did_not_say()
    {
        var paths = await CreateCatalogWithAsync("""{ "plugins": [], "mcpServers": [] }""");

        await DevToolConfiguration.AddToCatalogAsync(
            paths,
            new DevToolDraft(DevToolKind.Application, "Microsoft.VisualStudioCode", DisplayName: "Visual Studio Code")
            {
                Provider = DevToolProvider.Winget
            });

        var config = await DevToolConfiguration.ReadAsync(paths);
        var entry = Assert.Single(config.Root["applications"]!.AsArray())!.AsObject();

        Assert.Equal("Microsoft.VisualStudioCode", entry["id"]!.GetValue<string>());
        Assert.Equal("Visual Studio Code", entry["name"]!.GetValue<string>());
        Assert.Equal("winget", entry["provider"]!.GetValue<string>());
        Assert.True(entry["enabled"]!.GetValue<bool>());

        // Nothing the draft did not say: the format's silence is meaningful and a
        // property that only restates a default is a line to explain later.
        Assert.False(entry.ContainsKey("detect"));
        Assert.False(entry.ContainsKey("install"));
        Assert.False(entry.ContainsKey("group"));
        Assert.False(entry.ContainsKey("detectOnly"));
        Assert.False(entry.ContainsKey("hosts"));

        var application = Assert.Single(DevToolConfiguration.ReadApplications(config.Root));
        Assert.Equal(DevToolProvider.Winget, application.Provider);
        Assert.True(application.ProviderRecognised);
    }

    [Fact]
    public async Task Adding_a_command_application_writes_what_it_runs()
    {
        var paths = await CreateCatalogWithAsync("""{ "plugins": [], "mcpServers": [] }""");

        await DevToolConfiguration.AddToCatalogAsync(
            paths,
            new DevToolDraft(DevToolKind.Application, "git-pull-rebase", DisplayName: "git pull.rebase = true")
            {
                Provider = DevToolProvider.Command,
                DetectCommand = "git",
                DetectArgs = ["config", "--global", "pull.rebase"],
                DetectExpect = "true",
                InstallCommand = "git",
                InstallArgs = ["config", "--global", "pull.rebase", "true"]
            });

        var config = await DevToolConfiguration.ReadAsync(paths);
        var application = Assert.Single(DevToolConfiguration.ReadApplications(config.Root));

        Assert.Equal(DevToolProvider.Command, application.Provider);
        Assert.Equal("git", application.Detect!.Command);
        Assert.Equal(["config", "--global", "pull.rebase"], application.Detect.Args);
        Assert.Equal("true", application.Detect.Expect);
        Assert.Equal(["config", "--global", "pull.rebase", "true"], application.Install!.Args);
        Assert.Null(application.Install.Expect);
    }

    /// <summary>An install nobody declared is the checklist row, and it is the
    /// absence of the property that says so.</summary>
    [Fact]
    public async Task A_command_application_with_no_install_is_written_as_a_checklist_row()
    {
        var paths = await CreateCatalogWithAsync("""{ "plugins": [], "mcpServers": [] }""");

        await DevToolConfiguration.AddToCatalogAsync(
            paths,
            new DevToolDraft(DevToolKind.Application, "dev-drive", DisplayName: "Dev Drive configured")
            {
                Provider = DevToolProvider.Command,
                DetectCommand = "fsutil",
                DetectArgs = ["devdrv", "query", "D:"],
                DetectExpect = "trusted Dev Drive"
            });

        var config = await DevToolConfiguration.ReadAsync(paths);
        var entry = Assert.Single(config.Root["applications"]!.AsArray())!.AsObject();

        Assert.False(entry.ContainsKey("install"));
        Assert.Null(DevToolConfiguration.ReadApplications(config.Root)[0].Install);
    }

    [Fact]
    public async Task An_application_without_an_id_says_what_an_application_needs()
    {
        var paths = await CreateCatalogWithAsync("""{ "plugins": [], "mcpServers": [] }""");

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => DevToolConfiguration.AddToCatalogAsync(
            paths,
            new DevToolDraft(DevToolKind.Application, "  ")));

        Assert.Contains("application", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A command entry with nothing to run can never answer whether it is
    /// there, so it is refused at the form rather than written and drawn as a row
    /// that is permanently unknown.</summary>
    [Fact]
    public async Task A_command_application_without_a_detect_command_is_refused()
    {
        var paths = await CreateCatalogWithAsync("""{ "plugins": [], "mcpServers": [] }""");

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => DevToolConfiguration.AddToCatalogAsync(
            paths,
            new DevToolDraft(DevToolKind.Application, "dev-drive") { Provider = DevToolProvider.Command }));

        Assert.Contains("detect", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task An_application_id_that_is_already_in_the_catalog_is_refused()
    {
        var paths = await CreateCatalogWithAsync("""
            { "plugins": [], "applications": [ { "id": "Git.Git", "provider": "winget", "enabled": true } ] }
            """);

        await Assert.ThrowsAsync<InvalidOperationException>(() => DevToolConfiguration.AddToCatalogAsync(
            paths,
            new DevToolDraft(DevToolKind.Application, "git.git") { Provider = DevToolProvider.Winget }));
    }

    private static async Task<DevToolConfigurationPaths> CreateCatalogWithAsync(string json)
    {
        var paths = DevToolConfigurationPaths.FromRepositoryRoot(CreateTempToolConfigRoot(), "dev-pc");
        await File.WriteAllTextAsync(paths.CatalogPath, json);
        return paths;
    }

    private static string CreateTempToolConfigRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), "backlog-tool-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(path, ".tools"));
        return path;
    }
}

/// <summary>
/// Claude Desktop as a third host — and the one thing that must not follow from
/// adding it.
/// </summary>
public class ClaudeDesktopHostTests
{
    /// <summary>The load-bearing assertion of the whole host change.
    /// <see cref="DevToolHosts.Default"/> is what a catalog entry means by saying
    /// nothing, and every entry on every machine says nothing. Folding the new
    /// host into it would make each of them claim a registration that was never
    /// made and offer an Install for it.</summary>
    [Fact]
    public void The_silent_default_does_not_include_claude_desktop()
    {
        Assert.False(DevToolHosts.Default.HasFlag(DevToolHosts.ClaudeDesktop));
        Assert.Equal(DevToolHosts.Copilot | DevToolHosts.Claude, DevToolHosts.Default);
        Assert.Equal(DevToolHosts.Default, DevToolOutput.ParseHosts(null));
    }

    [Fact]
    public void Claude_desktop_is_its_own_flag()
    {
        Assert.Equal(4, (int)DevToolHosts.ClaudeDesktop);
        Assert.False(DevToolHosts.ClaudeDesktop.HasFlag(DevToolHosts.Claude));
    }

    /// <summary>Opt-in only, so an entry that wants it has to write it — which
    /// means the writer has to be able to.</summary>
    [Fact]
    public async Task An_entry_for_claude_desktop_carries_it_in_its_hosts()
    {
        var root = Path.Combine(Path.GetTempPath(), "backlog-tool-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, ".tools"));
        var paths = DevToolConfigurationPaths.FromRepositoryRoot(root, "dev-pc");
        await File.WriteAllTextAsync(paths.CatalogPath, """{ "plugins": [], "mcpServers": [] }""");

        await DevToolConfiguration.AddToCatalogAsync(
            paths,
            new DevToolDraft(DevToolKind.McpServer, "JSdotNet.MCP.Guidelines")
            {
                Hosts = DevToolHosts.Claude | DevToolHosts.ClaudeDesktop,
                ClaudeCommand = "guidelines"
            });

        var hosts = (await DevToolConfiguration.ReadAsync(paths)).Root["mcpServers"]![0]!["hosts"]!.AsArray()
            .Select(node => node!.GetValue<string>())
            .ToArray();

        Assert.Equal(["claude", "claude-desktop"], hosts);
    }
}
