using System.Text.Json.Nodes;

namespace Backlog.Infrastructure.DevPc.UnitTests;

/// <summary>"Create from this machine": the catalog it writes is the hosts'
/// own manifests, read back as entries.</summary>
public class DevToolCatalogSeedTests
{
    private const string CopilotConfig = """
        // User settings belong in settings.json.
        {
          "installedPlugins": [
            { "name": "architecture", "marketplace": "", "enabled": true, "version": "0.5.0",
              "source": { "source": "github", "repo": "JSdotNet/Copilot", "path": "plugins/architecture" } },
            { "name": "winui", "marketplace": "win-dev-skills", "enabled": false, "version": "0.4.0" },
            { "name": "orphan", "marketplace": "", "enabled": true }
          ]
        }
        """;

    private const string ClaudeInstalledPlugins = """
        {
          "version": 2,
          "plugins": {
            "architecture@jsdotnet-ai-plugins": [ { "scope": "user", "version": "0.6.0" } ],
            "delivery@jsdotnet-devbook": [ { "scope": "project", "projectPath": "D:\\Repos\\x" }, { "scope": "user", "version": "1.26.0" } ],
            "finance-only@jsdotnet-ai-plugins": [ { "scope": "project", "projectPath": "D:\\Repos\\finance" } ]
          }
        }
        """;

    private const string ClaudeKnownMarketplaces = """
        {
          "jsdotnet-ai-plugins": { "source": { "source": "github", "repo": "JSdotNet/ai-plugins" } },
          "jsdotnet-devbook": { "source": { "source": "github", "repo": "JSdotNet/devbook" } }
        }
        """;

    private const string ClaudeSettings = """
        { "enabledPlugins": { "delivery@jsdotnet-devbook": false } }
        """;

    private const string ClaudeUserConfig = """
        {
          "numStartups": 12,
          "mcpServers": {
            "aspire": { "type": "stdio", "command": "aspire", "args": [ "agent", "mcp" ] },
            "backlog": { "type": "http", "url": "http://127.0.0.1:5000/mcp", "headersHelper": "backlog-token" }
          }
        }
        """;

    private const string ClaudeDesktopConfig = """
        {
          "mcpServers": {
            "aspire": { "command": "aspire", "args": [ "agent", "mcp" ] },
            "files": { "command": "npx", "args": [ "-y", "files-server" ], "env": { "TOKEN": "secret-value" } }
          }
        }
        """;

    private static DevToolCatalogSeedResult Seed() => DevToolCatalogSeed.Build(new DevToolHostManifests(
        CopilotConfig, ClaudeInstalledPlugins, ClaudeKnownMarketplaces, ClaudeSettings, ClaudeUserConfig, ClaudeDesktopConfig));

    private static JsonObject Plugin(DevToolCatalogSeedResult seed, string name) =>
        seed.Catalog["plugins"]!.AsArray().Single(p => (string?)p!["name"] == name)!.AsObject();

    private static JsonObject Server(DevToolCatalogSeedResult seed, string name) =>
        seed.Catalog["mcpServers"]!.AsArray().Single(p => (string?)p!["name"] == name)!.AsObject();

    [Fact]
    public void Claude_marketplaces_are_seeded_with_their_repositories()
    {
        var marketplaces = Seed().Catalog["claude"]!["marketplaces"]!.AsArray();

        Assert.Equal(
            ["jsdotnet-ai-plugins=JSdotNet/ai-plugins", "jsdotnet-devbook=JSdotNet/devbook"],
            marketplaces.Select(m => $"{m!["name"]}={m["source"]}"));
    }

    [Fact]
    public void A_plugin_both_hosts_have_is_one_entry_for_both()
    {
        var architecture = Plugin(Seed(), "architecture");

        Assert.Equal("JSdotNet/Copilot:plugins/architecture", (string?)architecture["source"]);
        Assert.Null(architecture["hosts"]);
        Assert.Equal("jsdotnet-ai-plugins", (string?)architecture["claudeMarketplace"]);
        Assert.True((bool)architecture["enabled"]!);
    }

    [Fact]
    public void A_copilot_marketplace_plugin_installs_from_name_at_marketplace_and_keeps_its_switch()
    {
        var winui = Plugin(Seed(), "winui");

        Assert.Equal("winui@win-dev-skills", (string?)winui["source"]);
        Assert.Equal(["copilot"], winui["hosts"]!.AsArray().Select(h => (string?)h));
        Assert.False((bool)winui["enabled"]!);
    }

    [Fact]
    public void A_claude_only_plugin_names_its_marketplace_and_takes_its_switch_from_settings()
    {
        var delivery = Plugin(Seed(), "delivery");

        // No source: the marketplace is where Claude resolves it, and a source
        // would be read as the plugin's own repository.
        Assert.Null(delivery["source"]);
        Assert.Equal(["claude"], delivery["hosts"]!.AsArray().Select(h => (string?)h));
        Assert.Equal("jsdotnet-devbook", (string?)delivery["claudeMarketplace"]);
        Assert.False((bool)delivery["enabled"]!);
    }

    [Fact]
    public void Project_only_installs_and_sourceless_plugins_are_left_out_and_said()
    {
        var seed = Seed();

        Assert.Equal(["architecture", "winui", "delivery"], seed.Catalog["plugins"]!.AsArray().Select(p => (string?)p!["name"]));
        Assert.Contains(seed.Notes, note => note.Contains("orphan", StringComparison.Ordinal));
        Assert.Contains(seed.Notes, note => note.Contains("1 Claude plugin installed only for a project", StringComparison.Ordinal));
    }

    [Fact]
    public void Registered_mcp_servers_are_seeded_per_host_and_merged_when_they_match()
    {
        var seed = Seed();

        var aspire = Server(seed, "aspire");
        Assert.Equal("aspire", (string?)aspire["command"]);
        Assert.Equal(["agent", "mcp"], aspire["args"]!.AsArray().Select(a => (string?)a));
        Assert.Equal(["claude", "claude-desktop"], aspire["hosts"]!.AsArray().Select(h => (string?)h));

        var backlog = Server(seed, "backlog");
        Assert.Equal("http", (string?)backlog["type"]);
        Assert.Equal("http://127.0.0.1:5000/mcp", (string?)backlog["url"]);
        Assert.Equal(["claude"], backlog["hosts"]!.AsArray().Select(h => (string?)h));

        Assert.Equal(["claude-desktop"], Server(seed, "files")["hosts"]!.AsArray().Select(h => (string?)h));
        Assert.Equal(3, seed.McpServers);
    }

    [Fact]
    public void No_header_or_environment_value_reaches_the_catalog()
    {
        var seed = Seed();
        var written = seed.Catalog.ToJsonString();

        Assert.DoesNotContain("secret-value", written, StringComparison.Ordinal);
        Assert.DoesNotContain("backlog-token", written, StringComparison.Ordinal);
        Assert.Contains(seed.Notes, note => note.Contains("backlog, files", StringComparison.Ordinal));
    }

    [Fact]
    public void A_url_or_argument_that_may_carry_a_credential_is_not_copied()
    {
        var seed = DevToolCatalogSeed.Build(new DevToolHostManifests(ClaudeUserConfig: """
            {
              "mcpServers": {
                "keyed": { "type": "http", "url": "https://example.test/mcp?apiKey=abc123" },
                "userinfo": { "type": "http", "url": "https://me:pw@example.test/mcp" },
                "placeholder": { "type": "http", "url": "http://127.0.0.1:${PORT}/mcp" },
                "cli": { "command": "tool", "args": [ "serve", "--api-key", "sk-live-1", "--token=ghp_abc", "ghp_bare", "--port", "5" ] }
              }
            }
            """));
        var written = seed.Catalog.ToJsonString();

        Assert.DoesNotContain("abc123", written, StringComparison.Ordinal);
        Assert.DoesNotContain("pw@", written, StringComparison.Ordinal);
        Assert.DoesNotContain("sk-live-1", written, StringComparison.Ordinal);
        Assert.DoesNotContain("ghp_", written, StringComparison.Ordinal);
        Assert.Equal(["placeholder", "cli"], seed.Catalog["mcpServers"]!.AsArray().Select(s => (string?)s!["name"]));
        Assert.Equal(["serve", "--port", "5"], Server(seed, "cli")["args"]!.AsArray().Select(a => (string?)a));
        Assert.Contains(seed.Notes, note => note.Contains("keyed was left out", StringComparison.Ordinal));
        Assert.Contains(seed.Notes, note => note.Contains("not copied for cli", StringComparison.Ordinal));
    }

    [Fact]
    public void Hosts_that_disagree_about_a_shared_plugin_are_written_enabled_and_said()
    {
        var seed = DevToolCatalogSeed.Build(new DevToolHostManifests(
            CopilotConfig: """{ "installedPlugins": [ { "name": "qa", "enabled": false, "source": { "repo": "o/r", "path": "plugins/qa" } } ] }""",
            ClaudeInstalledPlugins: """{ "plugins": { "qa@m": [ { "scope": "user" } ] } }"""));

        Assert.True((bool)Plugin(seed, "qa")["enabled"]!);
        Assert.Contains(seed.Notes, note => note.Contains("qa is on in Claude and off in Copilot", StringComparison.Ordinal));
    }

    [Fact]
    public void A_local_folder_marketplace_and_a_scopeless_install_are_handled()
    {
        var seed = DevToolCatalogSeed.Build(new DevToolHostManifests(
            ClaudeKnownMarketplaces: """{ "mine": { "source": { "source": "directory", "path": "C:\\Users\\me\\market" } } }""",
            ClaudeInstalledPlugins: """{ "plugins": { "old@mine": { "version": "1.0.0" } } }"""));

        Assert.Null(seed.Catalog["claude"]);
        Assert.DoesNotContain("Users", seed.Catalog.ToJsonString(), StringComparison.Ordinal);
        Assert.Contains(seed.Notes, note => note.Contains("mine marketplace was left out", StringComparison.Ordinal));

        // An install written before scopes existed is the user's.
        Assert.Equal("mine", (string?)Plugin(seed, "old")["claudeMarketplace"]);
    }

    [Theory]
    [InlineData("""[ "not", "an", "object" ]""")]
    [InlineData("""{ "installedPlugins": "nope", "plugins": [ 1 ], "mcpServers": [ 2 ] }""")]
    [InlineData("""{ "installedPlugins": [ "name", { "name": "x", "source": "o/r" } ], "plugins": { "a@b": "user" }, "enabledPlugins": [ true ], "mcpServers": { "s": "cmd" } }""")]
    [InlineData("""{ "m": "github", "n": { "source": [ "repo" ] } }""")]
    public void A_manifest_of_the_wrong_shape_contributes_nothing_and_never_throws(string json)
    {
        var seed = DevToolCatalogSeed.Build(new DevToolHostManifests(json, json, json, json, json, json));

        Assert.NotNull(seed.Catalog["plugins"]);
        Assert.NotNull(seed.Catalog["mcpServers"]);
    }

    [Fact]
    public void Applications_are_never_seeded()
    {
        Assert.Null(Seed().Catalog["applications"]);
    }

    [Fact]
    public void A_machine_with_no_manifests_seeds_the_empty_catalog_and_says_so()
    {
        var seed = DevToolCatalogSeed.Build(new DevToolHostManifests());

        Assert.Empty(seed.Catalog["plugins"]!.AsArray());
        Assert.Empty(seed.Catalog["mcpServers"]!.AsArray());
        Assert.Null(seed.Catalog["claude"]);
        Assert.StartsWith("Created an empty tool catalog at x", seed.Describe("x"), StringComparison.Ordinal);
    }

    [Fact]
    public void An_unreadable_manifest_contributes_nothing_and_is_named()
    {
        var seed = DevToolCatalogSeed.Build(new DevToolHostManifests(ClaudeKnownMarketplaces: "{ not json"));

        Assert.Null(seed.Catalog["claude"]);
        Assert.Contains(seed.Notes, note => note.Contains("Claude's marketplace list could not be read", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Creating_from_this_machine_writes_the_seed_the_reader_accepts()
    {
        var (service, paths, _) = ServiceOverManifests();

        var result = await service.CreateCatalogFromMachineAsync(TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded, result.Message);
        Assert.Contains("from this machine: 3 plugins, 2 marketplaces and 3 MCP servers", result.Message, StringComparison.Ordinal);

        var config = await DevToolConfiguration.ReadAsync(paths, TestContext.Current.CancellationToken);
        Assert.Equal(3, config.Root["plugins"]!.AsArray().Count);
        Assert.Equal(3, config.Root["mcpServers"]!.AsArray().Count);
        Assert.True(DevToolConfiguration.TryReadCatalog(await File.ReadAllTextAsync(paths.CatalogPath, TestContext.Current.CancellationToken), out _, out var error), error);

        // Read back the way a check reads them: the mechanism and hosts each
        // registration was seeded as.
        var servers = DevToolConfiguration.ReadMcpServers(config.Root).ToDictionary(s => s.Id);
        Assert.Equal(DevToolMcpMechanism.Command, servers["aspire"].Mechanism);
        Assert.Equal(DevToolHosts.Claude | DevToolHosts.ClaudeDesktop, servers["aspire"].Hosts);
        Assert.Equal(DevToolMcpMechanism.Http, servers["backlog"].Mechanism);
        Assert.Equal(DevToolHosts.Claude, servers["backlog"].Hosts);
        Assert.Equal(DevToolHosts.ClaudeDesktop, servers["files"].Hosts);

        var architecture = config.Root["plugins"]!.AsArray().Single(p => (string?)p!["name"] == "architecture");
        Assert.Equal(DevToolHosts.Default, DevToolConfiguration.ParseHosts(architecture));
    }

    [Fact]
    public async Task Creating_from_this_machine_over_an_existing_catalog_is_refused_and_changes_nothing()
    {
        var (service, paths, _) = ServiceOverManifests();
        Directory.CreateDirectory(Path.GetDirectoryName(paths.CatalogPath)!);
        await File.WriteAllTextAsync(paths.CatalogPath, """{ "plugins": [], "mcpServers": [] }""", TestContext.Current.CancellationToken);
        var before = await File.ReadAllBytesAsync(paths.CatalogPath, TestContext.Current.CancellationToken);

        var result = await service.CreateCatalogFromMachineAsync(TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.Contains("already exists", result.Message, StringComparison.Ordinal);
        Assert.Equal(before, await File.ReadAllBytesAsync(paths.CatalogPath, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task The_seeded_write_refuses_an_existing_catalog_too()
    {
        var root = Path.Combine(Path.GetTempPath(), "backlog-tool-tests", Guid.NewGuid().ToString("N"));
        var paths = DevToolConfigurationPaths.FromRepositoryRoot(root, "dev-pc");
        Directory.CreateDirectory(Path.GetDirectoryName(paths.CatalogPath)!);
        await File.WriteAllTextAsync(paths.CatalogPath, "{}", TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            DevToolConfiguration.CreateCatalogAsync(paths, Seed().Catalog, TestContext.Current.CancellationToken));

        Assert.Equal("{}", await File.ReadAllTextAsync(paths.CatalogPath, TestContext.Current.CancellationToken));
    }

    /// <summary>A service whose catalog and host manifests are all under one
    /// temporary folder, so nothing reads the machine the test runs on.</summary>
    private static (DevToolService Service, DevToolConfigurationPaths Paths, string Home) ServiceOverManifests()
    {
        var home = Path.Combine(Path.GetTempPath(), "backlog-tool-tests", Guid.NewGuid().ToString("N"));
        var manifests = DevToolHostManifestPaths.ForUser(home, Path.Combine(home, "roaming"), Path.Combine(home, "local"));

        Write(manifests.CopilotConfig, CopilotConfig);
        Write(manifests.ClaudeInstalledPlugins, ClaudeInstalledPlugins);
        Write(manifests.ClaudeKnownMarketplaces, ClaudeKnownMarketplaces);
        Write(manifests.ClaudeSettings, ClaudeSettings);
        Write(manifests.ClaudeUserConfig, ClaudeUserConfig);
        Write(manifests.ClaudeDesktopConfigCandidates[0], ClaudeDesktopConfig);

        var paths = DevToolConfigurationPaths.FromRepositoryRoot(Path.Combine(home, "repo"), "dev-pc");
        var service = new DevToolService(configPath: paths.CatalogPath) { HostManifestPaths = manifests };

        return (service, paths, home);
    }

    private static void Write(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }
}
