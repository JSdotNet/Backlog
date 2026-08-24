
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
        Assert.Equal(Path.Combine(root, ".tools", "dev-pc", "copilot-tools.json"), paths.PcConfigPath);
    }

    [Fact]
    public void Default_paths_use_the_configured_storage_root_for_tools()
    {
        var storageRoot = Path.Combine(Path.GetTempPath(), "backlog-tool-tests", Guid.NewGuid().ToString("N"));
        var repositoryRoot = CreateTempToolConfigRoot();
        var nestedStartPath = Path.Combine(repositoryRoot, "src", "App", "Backlog.Desktop", "bin", "Debug");
        Directory.CreateDirectory(nestedStartPath);

        var paths = CopilotToolConfigurationPaths.CreateDefault("dev-pc", nestedStartPath, storageRoot);

        Assert.Equal(Path.Combine(storageRoot, ".tools", "copilot-tools.json"), paths.CatalogPath);
        Assert.Equal(Path.Combine(storageRoot, ".tools", "dev-pc", "copilot-tools.json"), paths.PcConfigPath);
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
