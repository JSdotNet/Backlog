using System.Text.Json.Nodes;

namespace Backlog.Desktop.UI.UnitTests;

public class CopilotToolTests
{
    [Theory]
    [InlineData("1.2.3", "1.2.4", true)]
    [InlineData("v1.2.3", "1.2.3", false)]
    [InlineData("unknown", "1.2.3", false)]
    [InlineData("not installed", "1.2.3", false)]
    public void Update_available_only_compares_known_versions(string installed, string available, bool expected)
    {
        var tool = new CopilotToolInfo(
            "plugin:test",
            CopilotToolKind.Plugin,
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
        var tool = new CopilotToolInfo(
            "plugin:test",
            CopilotToolKind.Plugin,
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

    private static CopilotToolInfo Tool(bool enabled, bool installed, string installedVersion, string availableVersion) =>
        new(
            "plugin:test",
            CopilotToolKind.Plugin,
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
        var catalogPath = Path.Combine(root, ".tools", "copilot-tools.json");
        var pcConfigPath = Path.Combine(root, ".tools", "dev-pc", "copilot-tools.json");
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

        var config = await CopilotToolConfiguration.ReadAsync(CopilotToolConfigurationPaths.FromRepositoryRoot(root, "dev-pc"));

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
        var catalogPath = Path.Combine(root, ".tools", "copilot-tools.json");
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
        var paths = CopilotToolConfigurationPaths.FromRepositoryRoot(root, "dev-pc");

        await CopilotToolConfiguration.WriteEnabledOverrideAsync(paths, "plugin:architecture", false);
        await CopilotToolConfiguration.WriteEnabledOverrideAsync(paths, "mcp:JSdotNet.MCP.Guidelines", false);

        var config = await CopilotToolConfiguration.ReadAsync(paths);
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

        var paths = CopilotToolConfigurationPaths.CreateDefault("dev-pc", nestedStartPath);

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

        var paths = CopilotToolConfigurationPaths.CreateDefault("dev-pc", nestedStartPath, storageRoot);

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

        var paths = CopilotToolConfigurationPaths.FromRepositoryRoot(root, "dev-pc");

        Assert.Equal(legacyCatalog, paths.CatalogPath);
        Assert.Equal(legacyPcConfig, paths.PcConfigPath);
        Assert.True(CopilotToolConfiguration.CatalogExists(paths));
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

        var paths = CopilotToolConfigurationPaths.FromRepositoryRoot(root, "dev-pc");

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

        var paths = CopilotToolConfigurationPaths.CreateDefault("dev-pc", nestedStartPath);

        Assert.Equal(legacyCatalog, paths.CatalogPath);
    }

    /// <summary>Creating one always writes the new name: a machine with no catalog
    /// has nothing to stay compatible with.</summary>
    [Fact]
    public async Task Creating_a_catalog_writes_the_new_name()
    {
        var root = Path.Combine(Path.GetTempPath(), "backlog-tool-tests", Guid.NewGuid().ToString("N"));
        var paths = CopilotToolConfigurationPaths.FromRepositoryRoot(root, "dev-pc");

        await CopilotToolConfiguration.CreateCatalogAsync(paths);

        Assert.Equal("ai-tools.json", Path.GetFileName(paths.CatalogPath));
        Assert.True(File.Exists(Path.Combine(root, ".tools", "ai-tools.json")));
    }

    [Fact]
    public async Task Creating_a_catalog_makes_the_folder_and_both_arrays()
    {
        // Deliberately not CreateTempToolConfigRoot: a machine that has never had
        // a catalog has no .tools folder either, and creating one has to make it.
        var root = Path.Combine(Path.GetTempPath(), "backlog-tool-tests", Guid.NewGuid().ToString("N"));
        var paths = CopilotToolConfigurationPaths.FromRepositoryRoot(root, "dev-pc");

        Assert.False(CopilotToolConfiguration.CatalogExists(paths));

        await CopilotToolConfiguration.CreateCatalogAsync(paths);

        Assert.True(CopilotToolConfiguration.CatalogExists(paths));

        var config = await CopilotToolConfiguration.ReadAsync(paths);
        Assert.Empty(config.Root["plugins"]!.AsArray());
        Assert.Empty(config.Root["mcpServers"]!.AsArray());
    }

    [Fact]
    public async Task Creating_a_catalog_over_one_that_exists_is_refused_and_changes_nothing()
    {
        var root = CreateTempToolConfigRoot();
        var paths = CopilotToolConfigurationPaths.FromRepositoryRoot(root, "dev-pc");
        await File.WriteAllTextAsync(paths.CatalogPath, """
            { "plugins": [ { "name": "architecture", "source": "JSdotNet/Copilot:plugins/architecture", "enabled": true } ], "mcpServers": [] }
            """);
        var before = await File.ReadAllBytesAsync(paths.CatalogPath);

        await Assert.ThrowsAsync<InvalidOperationException>(() => CopilotToolConfiguration.CreateCatalogAsync(paths));

        Assert.Equal(before, await File.ReadAllBytesAsync(paths.CatalogPath));
    }

    [Fact]
    public async Task Adding_a_tool_lands_in_the_catalog_and_never_in_the_pc_config()
    {
        var paths = await CreateCatalogWithAsync("""{ "plugins": [], "mcpServers": [] }""");

        await CopilotToolConfiguration.AddToCatalogAsync(
            paths,
            new CopilotToolDraft(CopilotToolKind.Plugin, "architecture", "JSdotNet/Copilot:plugins/architecture", PluginKind: "repository-skills"));
        await CopilotToolConfiguration.AddToCatalogAsync(
            paths,
            new CopilotToolDraft(CopilotToolKind.McpServer, "JSdotNet.MCP.Guidelines", DisplayName: "guidelines"));

        var config = await CopilotToolConfiguration.ReadAsync(paths);
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
            CopilotToolConfiguration.AddToCatalogAsync(paths, new CopilotToolDraft(CopilotToolKind.Plugin, "Architecture", "b")));

        Assert.Contains("already", refused.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Single((await CopilotToolConfiguration.ReadAsync(paths)).Root["plugins"]!.AsArray());
    }

    [Fact]
    public async Task A_plugin_without_a_source_is_refused()
    {
        var paths = await CreateCatalogWithAsync("""{ "plugins": [], "mcpServers": [] }""");

        var refused = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CopilotToolConfiguration.AddToCatalogAsync(paths, new CopilotToolDraft(CopilotToolKind.Plugin, "architecture", "   ")));

        Assert.Contains("source", refused.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty((await CopilotToolConfiguration.ReadAsync(paths)).Root["plugins"]!.AsArray());
    }

    [Fact]
    public async Task Adding_before_there_is_a_catalog_says_to_create_one()
    {
        var root = CreateTempToolConfigRoot();
        var paths = CopilotToolConfigurationPaths.FromRepositoryRoot(root, "dev-pc");

        var refused = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CopilotToolConfiguration.AddToCatalogAsync(paths, new CopilotToolDraft(CopilotToolKind.Plugin, "architecture", "a")));

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
        await CopilotToolConfiguration.WriteEnabledOverrideAsync(paths, "plugin:architecture", false);

        await CopilotToolConfiguration.RemoveFromCatalogAsync(paths, "plugin:architecture");
        await CopilotToolConfiguration.RemoveEnabledOverrideAsync(paths, "plugin:architecture");

        var config = await CopilotToolConfiguration.ReadAsync(paths);
        var remaining = Assert.Single(config.Root["plugins"]!.AsArray())!;
        Assert.Equal("qa", remaining["name"]!.GetValue<string>());

        // The point of pruning the override: add it back and it comes back
        // enabled rather than carrying a disable nobody remembers making.
        await CopilotToolConfiguration.AddToCatalogAsync(paths, new CopilotToolDraft(CopilotToolKind.Plugin, "architecture", "a"));
        var reread = await CopilotToolConfiguration.ReadAsync(paths);
        var readded = reread.Root["plugins"]!.AsArray()
            .Single(node => node!["name"]!.GetValue<string>() == "architecture")!;
        Assert.True(readded["enabled"]!.GetValue<bool>());
    }

    [Fact]
    public async Task Removing_a_pc_override_when_there_is_no_pc_config_does_nothing()
    {
        var paths = await CreateCatalogWithAsync("""{ "plugins": [], "mcpServers": [] }""");

        await CopilotToolConfiguration.RemoveEnabledOverrideAsync(paths, "plugin:architecture");

        Assert.False(File.Exists(paths.PcConfigPath));
    }

    [Fact]
    public async Task Removing_a_tool_that_is_not_there_fails_without_touching_the_catalog()
    {
        var paths = await CreateCatalogWithAsync("""{ "plugins": [], "mcpServers": [] }""");
        var before = await File.ReadAllBytesAsync(paths.CatalogPath);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CopilotToolConfiguration.RemoveFromCatalogAsync(paths, "plugin:architecture"));

        Assert.Equal(before, await File.ReadAllBytesAsync(paths.CatalogPath));
    }

    [Fact]
    public async Task Importing_replaces_the_catalog_and_keeps_the_previous_one_beside_it()
    {
        var paths = await CreateCatalogWithAsync("""
            { "plugins": [ { "name": "architecture", "source": "a", "enabled": true } ], "mcpServers": [] }
            """);
        var before = await File.ReadAllTextAsync(paths.CatalogPath);

        await CopilotToolConfiguration.ImportCatalogAsync(paths, """
            { "plugins": [ { "name": "qa", "source": "b", "enabled": false } ], "mcpServers": [] }
            """);

        var config = await CopilotToolConfiguration.ReadAsync(paths);
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
            CopilotToolConfiguration.ImportCatalogAsync(paths, "{ not json at all"));

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
        Assert.False(CopilotToolConfiguration.TryReadCatalog(json, out _, out var error));
        Assert.Contains(expected, error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_catalog_with_only_one_of_the_two_arrays_is_accepted()
    {
        // The bar is deliberately low. A hand-edited catalog that has only
        // grown plugins so far is a real file, not a malformed one.
        Assert.True(CopilotToolConfiguration.TryReadCatalog(
            """{ "plugins": [ { "name": "architecture", "source": "a" } ] }""",
            out var root,
            out _));

        Assert.Single(root["plugins"]!.AsArray());
    }

    private static async Task<CopilotToolConfigurationPaths> CreateCatalogWithAsync(string json)
    {
        var root = CreateTempToolConfigRoot();
        var paths = CopilotToolConfigurationPaths.FromRepositoryRoot(root, "dev-pc");
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
        var plugins = CopilotToolOutput.ParseClaudePluginList(PluginListJson);

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
        var plugins = CopilotToolOutput.ParseClaudePluginList(PluginListJson);

        Assert.False(plugins["qa@jsdotnet-copilot"].Enabled);
        Assert.Equal("0.9.0", plugins["qa@jsdotnet-copilot"].Version);
    }

    /// <summary>"Here but unversioned" is not "not here". A plugin installed from
    /// a marketplace need not declare a version, and reporting it absent would
    /// offer an install for something already on the machine.</summary>
    [Fact]
    public void A_plugin_with_no_version_reads_as_installed()
    {
        var plugins = CopilotToolOutput.ParseClaudePluginList(PluginListJson);

        Assert.Equal(CopilotToolOutput.Installed, plugins["documentation@jsdotnet-copilot"].Version);
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
        Assert.Empty(CopilotToolOutput.ParseClaudePluginList(body));
    }

    /// <summary>The same list, wrapped in an object. Both shapes have come out of
    /// the CLI, and reading both costs less than a release that empties every
    /// Claude row on the pane.</summary>
    [Fact]
    public void A_plugin_list_wrapped_in_an_object_is_read_the_same_way()
    {
        var plugins = CopilotToolOutput.ParseClaudePluginList("""
            { "plugins": [ { "id": "qa@jsdotnet-copilot", "version": "1.0.0" } ] }
            """);

        Assert.Equal("1.0.0", Assert.Single(plugins).Value.Version);
    }

    [Fact]
    public void Configured_marketplaces_are_read_by_name()
    {
        var names = CopilotToolOutput.ParseClaudeMarketplaceList("""
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
        Assert.Empty(CopilotToolOutput.ParseClaudeMarketplaceList(body));
    }

    [Fact]
    public void A_registered_mcp_server_reports_its_scope_and_command()
    {
        var details = CopilotToolOutput.ParseClaudeMcpServer("""
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
        Assert.Null(CopilotToolOutput.ParseClaudeMcpServer(output));
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
        var details = CopilotToolOutput.ParseClaudeMcpServer($"a-server:{Environment.NewLine}{scopeLine}{Environment.NewLine}  Command: a-command");

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
        Assert.Equal(expected, CopilotToolOutput.ClaudePluginId(name, claudeName, claudeMarketplace, defaultMarketplace));
    }

    /// <summary>A catalog with no marketplaces at all can resolve no Claude plugin
    /// id. Nothing rather than a throw, so the row says so beside the rows that
    /// resolved fine.</summary>
    [Fact]
    public void A_plugin_with_no_marketplace_anywhere_has_no_claude_id()
    {
        Assert.Null(CopilotToolOutput.ClaudePluginId("architecture", null, null, null));
        Assert.Null(CopilotToolOutput.ClaudePluginId("architecture", null, "   ", "  "));
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
public class CopilotToolHostTests
{
    [Fact]
    public void An_entry_with_no_hosts_property_is_for_both()
    {
        var entry = JsonNode.Parse("""{ "name": "architecture" }""");

        Assert.Equal(CopilotToolHosts.Both, CopilotToolConfiguration.ParseHosts(entry));
    }

    [Theory]
    [InlineData("""{ "hosts": [] }""", CopilotToolHosts.Both)]
    [InlineData("""{ "hosts": [ "  " ] }""", CopilotToolHosts.Both)]
    [InlineData("""{ "hosts": [ "copilot" ] }""", CopilotToolHosts.Copilot)]
    [InlineData("""{ "hosts": [ "claude" ] }""", CopilotToolHosts.Claude)]
    [InlineData("""{ "hosts": [ "Copilot", "CLAUDE" ] }""", CopilotToolHosts.Both)]
    public void The_hosts_array_is_read_case_insensitively(string json, CopilotToolHosts expected)
    {
        Assert.Equal(expected, CopilotToolConfiguration.ParseHosts(JsonNode.Parse(json)));
    }

    /// <summary>A host name this version has not met is ignored rather than
    /// rejected. A catalog written for a third host still installs its Copilot and
    /// Claude entries here.</summary>
    [Fact]
    public void An_unknown_host_name_is_ignored()
    {
        Assert.Equal(
            CopilotToolHosts.Claude,
            CopilotToolConfiguration.ParseHosts(JsonNode.Parse("""{ "hosts": [ "claude", "cursor" ] }""")));
    }

    /// <summary>One press acts on every host the entry targets, so an update
    /// waiting on either one is an update worth a button.</summary>
    [Fact]
    public void An_update_on_one_host_makes_the_row_updateable()
    {
        var tool = Tool(
            new CopilotToolHostState(CopilotToolHosts.Copilot, true, "1.0.0", "1.0.0", "Enabled plugin"),
            new CopilotToolHostState(CopilotToolHosts.Claude, true, "1.0.0", "1.1.0", "Update available"));

        Assert.True(tool.CanUpdate);
        Assert.True(tool.UpdateAvailable);
    }

    /// <summary>A plugin Copilot already has and Claude has never heard of is
    /// still a plugin this machine is short of.</summary>
    [Fact]
    public void A_tool_missing_on_one_host_can_still_be_installed()
    {
        var tool = Tool(
            new CopilotToolHostState(CopilotToolHosts.Copilot, true, "1.0.0", "1.0.0", "Enabled plugin"),
            new CopilotToolHostState(CopilotToolHosts.Claude, false, "not installed", "1.0.0", "Not installed"));

        Assert.True(tool.CanInstall);
        Assert.False(tool.CanUpdate);
    }

    [Fact]
    public void A_tool_both_hosts_have_at_the_published_version_offers_nothing()
    {
        var tool = Tool(
            new CopilotToolHostState(CopilotToolHosts.Copilot, true, "1.0.0", "1.0.0", "Enabled plugin"),
            new CopilotToolHostState(CopilotToolHosts.Claude, true, "1.0.0", "1.0.0", "Enabled plugin"));

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
        var tool = new CopilotToolInfo(
            "plugin:architecture",
            CopilotToolKind.Plugin,
            "architecture",
            "a",
            ConfiguredEnabled: true,
            Installed: true,
            "1.0.0",
            "1.1.0",
            "Update available");

        Assert.Equal(CopilotToolHosts.Both, tool.Hosts);
        Assert.Empty(tool.HostStates);
        Assert.True(tool.CanUpdate);
        Assert.False(tool.CanInstall);
    }

    private static CopilotToolInfo Tool(params CopilotToolHostState[] states) => new(
        "plugin:architecture",
        CopilotToolKind.Plugin,
        "architecture",
        "JSdotNet/Copilot:plugins/architecture",
        ConfiguredEnabled: true,
        Installed: states.All(state => state.Installed),
        "1.0.0",
        "1.0.0",
        "Enabled plugin")
    {
        Hosts = CopilotToolHosts.Both,
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
        var key = CopilotToolConfiguration.KeyFor(CopilotToolKind.Marketplace, "jsdotnet-copilot");

        Assert.Equal("marketplace:jsdotnet-copilot", key);

        var (arrayName, idName, idValue) = CopilotToolConfiguration.ParseKey(key);

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

        Assert.Equal("jsdotnet-copilot", CopilotToolConfiguration.DefaultMarketplaceName(root));
        Assert.Equal(2, CopilotToolConfiguration.MarketplaceEntries(root).Count());
    }

    [Fact]
    public void A_catalog_with_no_marketplaces_has_no_default()
    {
        Assert.Null(CopilotToolConfiguration.DefaultMarketplaceName(JsonNode.Parse("""{ "plugins": [] }""")));
        Assert.Empty(CopilotToolConfiguration.MarketplaceEntries(JsonNode.Parse("""{ "plugins": [] }""")));
    }

    /// <summary>Marketplaces are content on their own: adding one is how a machine
    /// that installs only Claude plugins is bootstrapped, and refusing that
    /// catalog would leave the import unable to do it.</summary>
    [Fact]
    public void A_catalog_that_is_only_marketplaces_is_accepted()
    {
        Assert.True(CopilotToolConfiguration.TryReadCatalog(
            """{ "claude": { "marketplaces": [ { "name": "jsdotnet-copilot", "source": "JSdotNet/Copilot" } ] } }""",
            out var root,
            out _));

        Assert.Equal("jsdotnet-copilot", CopilotToolConfiguration.DefaultMarketplaceName(root));
    }

    [Fact]
    public void A_marketplace_without_a_name_is_refused_with_a_reason()
    {
        Assert.False(CopilotToolConfiguration.TryReadCatalog(
            """{ "claude": { "marketplaces": [ { "source": "JSdotNet/Copilot" } ] } }""",
            out _,
            out var error));

        Assert.Contains("name", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Adding_a_marketplace_lands_under_the_claude_section()
    {
        var paths = await CreateCatalogWithAsync("""{ "plugins": [], "mcpServers": [] }""");

        await CopilotToolConfiguration.AddToCatalogAsync(
            paths,
            new CopilotToolDraft(CopilotToolKind.Marketplace, "jsdotnet-copilot", "JSdotNet/Copilot"));

        var config = await CopilotToolConfiguration.ReadAsync(paths);
        var marketplace = Assert.Single(CopilotToolConfiguration.MarketplaceEntries(config.Root));

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
            CopilotToolConfiguration.AddToCatalogAsync(paths, new CopilotToolDraft(CopilotToolKind.Marketplace, "jsdotnet-copilot", "  ")));

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

        await CopilotToolConfiguration.RemoveFromCatalogAsync(paths, "marketplace:jsdotnet-copilot");

        var config = await CopilotToolConfiguration.ReadAsync(paths);
        Assert.Empty(CopilotToolConfiguration.MarketplaceEntries(config.Root));
    }

    /// <summary>Both hosts is what the format means by silence, so writing the
    /// property out would add a line that changes nothing and invite the reader to
    /// wonder why the entry beside it lacks one.</summary>
    [Fact]
    public async Task A_plugin_for_both_hosts_is_written_without_a_hosts_property()
    {
        var paths = await CreateCatalogWithAsync("""{ "plugins": [] }""");

        await CopilotToolConfiguration.AddToCatalogAsync(paths, new CopilotToolDraft(CopilotToolKind.Plugin, "architecture", "a"));

        var config = await CopilotToolConfiguration.ReadAsync(paths);
        Assert.Null(Assert.Single(config.Root["plugins"]!.AsArray())!["hosts"]);
    }

    [Fact]
    public async Task A_claude_only_plugin_carries_its_hosts_and_its_claude_names()
    {
        var paths = await CreateCatalogWithAsync("""{ "plugins": [] }""");

        await CopilotToolConfiguration.AddToCatalogAsync(
            paths,
            new CopilotToolDraft(CopilotToolKind.Plugin, "jsdotnet-project-guidelines", "JSdotNet/Copilot:plugins/guidelines")
            {
                Hosts = CopilotToolHosts.Claude,
                ClaudeName = "guidelines",
                ClaudeMarketplace = "anthropic-skills"
            });

        var config = await CopilotToolConfiguration.ReadAsync(paths);
        var plugin = Assert.Single(config.Root["plugins"]!.AsArray())!;

        Assert.Equal(["claude"], plugin["hosts"]!.AsArray().Select(node => node!.GetValue<string>()));
        Assert.Equal("guidelines", plugin["claudeName"]!.GetValue<string>());
        Assert.Equal("anthropic-skills", plugin["claudeMarketplace"]!.GetValue<string>());
        Assert.Equal(CopilotToolHosts.Claude, CopilotToolConfiguration.ParseHosts(plugin));
    }

    [Fact]
    public async Task An_mcp_server_with_a_claude_command_carries_its_registration()
    {
        var paths = await CreateCatalogWithAsync("""{ "mcpServers": [] }""");

        await CopilotToolConfiguration.AddToCatalogAsync(
            paths,
            new CopilotToolDraft(CopilotToolKind.McpServer, "JSdotNet.MCP.Guidelines", DisplayName: "jsdotnet-project-guidelines")
            {
                ClaudeServerName = "jsdotnet-coding-guidelines",
                ClaudeCommand = "jsdotnet-guidelines-mcpserver",
                ClaudeArgs = ["--stdio"]
            });

        var config = await CopilotToolConfiguration.ReadAsync(paths);
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

        await CopilotToolConfiguration.AddToCatalogAsync(
            paths,
            new CopilotToolDraft(CopilotToolKind.McpServer, "JSdotNet.MCP.Guidelines") { ClaudeServerName = "guidelines" });

        var config = await CopilotToolConfiguration.ReadAsync(paths);
        Assert.Null(Assert.Single(config.Root["mcpServers"]!.AsArray())!["claude"]);
    }

    private static async Task<CopilotToolConfigurationPaths> CreateCatalogWithAsync(string json)
    {
        var root = Path.Combine(Path.GetTempPath(), "backlog-tool-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, ".tools"));
        var paths = CopilotToolConfigurationPaths.FromRepositoryRoot(root, "dev-pc");
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
public class CopilotToolCommandTests
{
    [Fact]
    public void A_catalog_built_without_commands_has_an_empty_log()
    {
        var catalog = new CopilotToolCatalog([], "Nothing was checked.");

        Assert.Empty(catalog.Commands);
    }

    [Fact]
    public void A_result_built_without_commands_has_an_empty_log()
    {
        Assert.Empty(CopilotToolActionResult.Ok("Done.").Commands);
        Assert.Empty(CopilotToolActionResult.Failed("Not done.").Commands);
        Assert.Empty(new CopilotToolActionResult(true, "Done.").Commands);
    }

    [Fact]
    public void A_result_carries_the_commands_it_is_handed()
    {
        CopilotToolCommand[] commands =
        [
            new("dotnet tool list --global", 0, "Package Id      Version"),
            new("dotnet tool search missing --exact-match", 1, "No packages found.")
        ];

        Assert.Equal(commands, CopilotToolActionResult.Ok("Done.", commands).Commands);
        Assert.Equal(commands, CopilotToolActionResult.Failed("Not done.", commands).Commands);
    }
}

public class UnsupportedCopilotToolServiceTests
{
    [Fact]
    public async Task Listing_returns_a_clear_unsupported_message()
    {
        var service = new UnsupportedCopilotToolService();

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
        var result = await new UnsupportedCopilotToolService().CreateCatalogAsync();

        Assert.False(result.Succeeded);
        Assert.Contains("desktop app", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Adding_a_tool_is_refused()
    {
        var result = await new UnsupportedCopilotToolService()
            .AddAsync(new CopilotToolDraft(CopilotToolKind.Plugin, "architecture", "a"));

        Assert.False(result.Succeeded);
        Assert.Contains("desktop app", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Removing_a_tool_is_refused()
    {
        var result = await new UnsupportedCopilotToolService().RemoveAsync("plugin:architecture");

        Assert.False(result.Succeeded);
        Assert.Contains("desktop app", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Importing_a_catalog_is_refused()
    {
        var result = await new UnsupportedCopilotToolService().ImportAsync("""{ "plugins": [] }""");

        Assert.False(result.Succeeded);
        Assert.Contains("desktop app", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(CopilotToolAction.Update)]
    [InlineData(CopilotToolAction.Enable)]
    [InlineData(CopilotToolAction.Disable)]
    public async Task Actions_fail_without_throwing(CopilotToolAction action)
    {
        var service = new UnsupportedCopilotToolService();

        var result = action switch
        {
            CopilotToolAction.Update => await service.UpdateAsync("plugin:test"),
            CopilotToolAction.Enable => await service.EnableAsync("plugin:test"),
            CopilotToolAction.Disable => await service.DisableAsync("plugin:test"),
            _ => throw new ArgumentOutOfRangeException(nameof(action))
        };

        Assert.False(result.Succeeded);
        Assert.False(string.IsNullOrWhiteSpace(result.Message));
    }
}
