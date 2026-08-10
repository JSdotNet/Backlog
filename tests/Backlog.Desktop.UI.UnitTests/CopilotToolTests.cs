using Backlog.Desktop.UI.Services;

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
    public async Task Pc_config_overrides_matching_catalog_tools_only()
    {
        var root = CreateTempToolConfigRoot();
        var catalogPath = Path.Combine(root, "tools", "copilot-tools.json");
        var pcConfigPath = Path.Combine(root, "tools", "dev-pc", "copilot-tools.json");
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
        var catalogPath = Path.Combine(root, "tools", "copilot-tools.json");
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
        var catalogPath = Path.Combine(root, "tools", "copilot-tools.json");
        await File.WriteAllTextAsync(catalogPath, """{ "plugins": [], "mcpServers": [] }""");
        var nestedStartPath = Path.Combine(root, "src", "App", "Backlog.Desktop", "bin", "Debug");
        Directory.CreateDirectory(nestedStartPath);

        var paths = CopilotToolConfigurationPaths.CreateDefault("dev-pc", nestedStartPath);

        Assert.Equal(catalogPath, paths.CatalogPath);
        Assert.Equal(Path.Combine(root, "tools", "dev-pc", "copilot-tools.json"), paths.PcConfigPath);
    }

    private static string CreateTempToolConfigRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), "backlog-tool-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(path, "tools"));
        return path;
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
