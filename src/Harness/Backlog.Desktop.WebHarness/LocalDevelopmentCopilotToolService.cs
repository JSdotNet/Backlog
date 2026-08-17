using System.Text.Json.Nodes;
using Backlog.Desktop.UI.BacklogManagement;
using Backlog.Desktop.UI.Knowledge;
using Backlog.Desktop.UI.Shell;
using Backlog.Desktop.UI.Workspace;

namespace Backlog.Desktop.WebHarness;

internal sealed class LocalDevelopmentCopilotToolService : ICopilotToolService
{
    private readonly BacklogStore _store;

    public LocalDevelopmentCopilotToolService(BacklogStore store)
    {
        _store = store;
    }

    public async Task<CopilotToolCatalog> ListAsync(CancellationToken ct = default)
    {
        var paths = Paths;
        if (!File.Exists(paths.CatalogPath))
        {
            return new CopilotToolCatalog([], $"Tool catalog was not found at {paths.CatalogPath}.");
        }

        var config = await CopilotToolConfiguration.ReadAsync(paths, ct).ConfigureAwait(false);
        var tools = new List<CopilotToolInfo>();

        foreach (var plugin in GetArray(config.Root, "plugins"))
        {
            var name = GetString(plugin, "name");
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var enabled = GetBool(plugin, "enabled");
            var installedVersion = VersionOr(plugin, "installedVersion", enabled ? "configured" : "disabled");
            var availableVersion = VersionOr(plugin, "availableVersion", "catalog");
            tools.Add(new CopilotToolInfo(
                $"plugin:{name}",
                CopilotToolKind.Plugin,
                name,
                GetString(plugin, "source"),
                enabled,
                enabled,
                installedVersion,
                availableVersion,
                enabled ? "Configured from local JSON" : "Disabled in config"));
        }

        foreach (var server in GetArray(config.Root, "mcpServers"))
        {
            var packageId = GetString(server, "packageId");
            if (string.IsNullOrWhiteSpace(packageId))
            {
                continue;
            }

            var name = GetString(server, "name");
            var enabled = GetBool(server, "enabled");
            var displayName = string.IsNullOrWhiteSpace(name) ? packageId : $"{name} ({packageId})";
            var installedVersion = VersionOr(server, "installedVersion", enabled ? "configured" : "disabled");
            var availableVersion = VersionOr(server, "availableVersion", "catalog");
            tools.Add(new CopilotToolInfo(
                $"mcp:{packageId}",
                CopilotToolKind.McpServer,
                displayName,
                packageId,
                enabled,
                enabled,
                installedVersion,
                availableVersion,
                enabled ? "Configured from local JSON" : "Disabled in config"));
        }

        var message = config.PcConfigExists
            ? $"Showing tools from {config.CatalogPath} with PC config {config.PcConfigPath}."
            : $"Showing tools from {config.CatalogPath}. PC config will be created at {config.PcConfigPath}.";
        return new CopilotToolCatalog(tools, message);
    }

    public Task<CopilotToolActionResult> UpdateAsync(string key, CancellationToken ct = default) =>
        Task.FromResult(CopilotToolActionResult.Failed("Tool updates are only available in the desktop app."));

    public async Task<CopilotToolActionResult> UpdateAllAsync(CancellationToken ct = default)
    {
        var catalog = await ListAsync(ct).ConfigureAwait(false);
        if (!catalog.Tools.Any(tool => tool.CanUpdate))
        {
            return CopilotToolActionResult.Ok("No enabled tools have updates available.");
        }

        return CopilotToolActionResult.Failed("Tool updates are only available in the desktop app.");
    }

    public Task<CopilotToolActionResult> EnableAsync(string key, CancellationToken ct = default) =>
        SetEnabledAsync(key, enabled: true, ct);

    public Task<CopilotToolActionResult> DisableAsync(string key, CancellationToken ct = default) =>
        SetEnabledAsync(key, enabled: false, ct);

    private async Task<CopilotToolActionResult> SetEnabledAsync(string key, bool enabled, CancellationToken ct)
    {
        await CopilotToolConfiguration.WriteEnabledOverrideAsync(Paths, key, enabled, ct).ConfigureAwait(false);
        return CopilotToolActionResult.Ok($"{key} was {(enabled ? "enabled" : "disabled")} in the local PC config.");
    }

    private CopilotToolConfigurationPaths Paths => CopilotToolConfigurationPaths.FromStorageRoot(_store.RootDirectory);

    private static IEnumerable<JsonNode> GetArray(JsonNode root, string name) =>
        root[name]?.AsArray().Where(node => node is not null).Cast<JsonNode>() ?? [];

    private static string GetString(JsonNode node, string name) => node[name]?.GetValue<string>() ?? string.Empty;

    private static bool GetBool(JsonNode node, string name) => node[name]?.GetValue<bool>() ?? false;

    private static string VersionOr(JsonNode node, string name, string fallback) =>
        GetString(node, name) is { Length: > 0 } value ? value : fallback;
}