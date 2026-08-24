using System.Text.Json;
using System.Text.Json.Nodes;
using Backlog.Desktop.UI.BacklogManagement;
using Backlog.Desktop.UI.Knowledge;
using Backlog.Modules.DevPc.Abstractions;
using Backlog.Modules.Backlog.Abstractions.Services;

namespace Backlog.Desktop.WebHarness;

internal sealed class LocalDevelopmentCopilotToolService : ICopilotToolService
{
    /// <summary>
    /// Stand-ins for the dozen processes the desktop head runs to answer a
    /// check. This fake starts none, so nothing real could be reported here —
    /// but the pane's command log is a browser-testable surface and this harness
    /// is the only place a browser can reach it, so it gets two entries that say
    /// out loud what they are. One of them failed, because the failing one is
    /// the entire reason the log exists.
    /// </summary>
    private static readonly CopilotToolCommand[] SampleCommands =
    [
        new("copilot --version", 0, "Sample output: this harness reads the catalog and starts no processes."),
        new("dotnet tool search JSdotNet.MCP.Guidelines", 1, "Sample failure: nothing was searched.")
    ];

    private readonly IBacklogStore _store;

    public LocalDevelopmentCopilotToolService(IBacklogStore store)
    {
        _store = store;
    }

    public async Task<CopilotToolCatalog> ListAsync(CancellationToken ct = default)
    {
        var paths = Paths;
        if (!CopilotToolConfiguration.CatalogExists(paths))
        {
            // The path travels with the "not found" answer: it is what the pane
            // names in its empty state, and what the create button is offering to
            // write. An empty list on its own cannot say either.
            return new CopilotToolCatalog(
                [],
                $"Tool catalog was not found at {paths.CatalogPath}.",
                CatalogExists: false,
                CatalogPath: paths.CatalogPath,
                CanEditCatalog: true);
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
        return new CopilotToolCatalog(tools, message, CatalogExists: true, CatalogPath: config.CatalogPath, CanEditCatalog: true)
        {
            Commands = SampleCommands
        };
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

    // Editing the catalog is a file write and nothing else, so the harness does it
    // for real rather than refusing the way it refuses an install: a browser
    // session is where the pane's create, add, remove and import are driven, and a
    // stubbed answer there would be a surface nobody has actually operated.

    public Task<CopilotToolActionResult> CreateCatalogAsync(CancellationToken ct = default) =>
        EditCatalogAsync(
            paths => CopilotToolConfiguration.CreateCatalogAsync(paths, ct),
            paths => $"Created a tool catalog at {paths.CatalogPath}.");

    public Task<CopilotToolActionResult> AddAsync(CopilotToolDraft draft, CancellationToken ct = default) =>
        EditCatalogAsync(
            paths => CopilotToolConfiguration.AddToCatalogAsync(paths, draft, ct),
            _ => $"{draft.Id} was added to the catalog.");

    public Task<CopilotToolActionResult> RemoveAsync(string key, CancellationToken ct = default) =>
        EditCatalogAsync(
            async paths =>
            {
                await CopilotToolConfiguration.RemoveFromCatalogAsync(paths, key, ct).ConfigureAwait(false);

                // The per-PC override outlives the catalog entry unless it goes
                // with it, and the same tool added again would then arrive
                // already disabled by a decision nobody remembers making.
                await CopilotToolConfiguration.RemoveEnabledOverrideAsync(paths, key, ct).ConfigureAwait(false);
            },
            _ => $"{CopilotToolConfiguration.ParseKey(key).IdValue} was removed from the catalog.");

    public Task<CopilotToolActionResult> ImportAsync(string json, CancellationToken ct = default) =>
        EditCatalogAsync(
            paths => CopilotToolConfiguration.ImportCatalogAsync(paths, json, ct),
            paths => $"The catalog at {paths.CatalogPath} was replaced. The previous one is beside it as .bak.");

    /// <summary>The same wrapper the desktop host uses, for the same reason:
    /// <c>.tools</c> is a folder on somebody's disk, so a refused write is an
    /// ordinary outcome and has to reach the pane as a message rather than as an
    /// exception that takes the tools surface down.</summary>
    private async Task<CopilotToolActionResult> EditCatalogAsync(
        Func<CopilotToolConfigurationPaths, Task> edit,
        Func<CopilotToolConfigurationPaths, string> describe)
    {
        var paths = Paths;

        try
        {
            await edit(paths).ConfigureAwait(false);
            return CopilotToolActionResult.Ok(describe(paths));
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or IOException or UnauthorizedAccessException or JsonException)
        {
            return CopilotToolActionResult.Failed(ex.Message);
        }
    }

    private CopilotToolConfigurationPaths Paths => CopilotToolConfigurationPaths.FromStorageRoot(_store.RootDirectory);

    private static IEnumerable<JsonNode> GetArray(JsonNode root, string name) =>
        root[name]?.AsArray().Where(node => node is not null).Cast<JsonNode>() ?? [];

    private static string GetString(JsonNode node, string name) => node[name]?.GetValue<string>() ?? string.Empty;

    private static bool GetBool(JsonNode node, string name) => node[name]?.GetValue<bool>() ?? false;

    private static string VersionOr(JsonNode node, string name, string fallback) =>
        GetString(node, name) is { Length: > 0 } value ? value : fallback;
}