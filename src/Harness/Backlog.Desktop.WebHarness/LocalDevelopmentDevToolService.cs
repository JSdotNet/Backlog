using System.Text.Json;
using System.Text.Json.Nodes;
using Backlog.Desktop.UI.BacklogManagement;
using Backlog.Desktop.UI.Knowledge;
using Backlog.Modules.DevPc.Abstractions;
using Backlog.Modules.Backlog.Abstractions.Services;

namespace Backlog.Desktop.WebHarness;

internal sealed class LocalDevelopmentDevToolService : IDevToolService
{
    /// <summary>
    /// Stand-ins for the dozen processes the desktop head runs to answer a
    /// check. This fake starts none, so nothing real could be reported here —
    /// but the pane's command log is a browser-testable surface and this harness
    /// is the only place a browser can reach it, so it gets two entries that say
    /// out loud what they are. One of them failed, because the failing one is
    /// the entire reason the log exists.
    /// </summary>
    private static readonly DevToolCommand[] SampleCommands =
    [
        new("copilot --version", 0, "Sample output: this harness reads the catalog and starts no processes."),
        new("dotnet tool search JSdotNet.MCP.Guidelines", 1, "Sample failure: nothing was searched.")
    ];

    private readonly IBacklogStore _store;

    public LocalDevelopmentDevToolService(IBacklogStore store)
    {
        _store = store;
    }

    public async Task<DevToolCatalog> ListAsync(CancellationToken ct = default)
    {
        var paths = Paths;
        if (!DevToolConfiguration.CatalogExists(paths))
        {
            // The path travels with the "not found" answer: it is what the pane
            // names in its empty state, and what the create button is offering to
            // write. An empty list on its own cannot say either.
            return new DevToolCatalog(
                [],
                $"Tool catalog was not found at {paths.CatalogPath}.",
                CatalogExists: false,
                CatalogPath: paths.CatalogPath,
                CanEditCatalog: true);
        }

        var config = await DevToolConfiguration.ReadAsync(paths, ct).ConfigureAwait(false);
        var tools = new List<DevToolInfo>();

        // The marketplaces lead, the way they do in the desktop head, because the
        // pane's marketplace row is one of the surfaces this harness exists to make
        // reachable from a browser at all.
        foreach (var marketplace in DevToolConfiguration.MarketplaceEntries(config.Root))
        {
            var name = GetString(marketplace, "name");
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            tools.Add(new DevToolInfo(
                DevToolConfiguration.KeyFor(DevToolKind.Marketplace, name),
                DevToolKind.Marketplace,
                name,
                GetString(marketplace, "source"),
                ConfiguredEnabled: true,
                Installed: true,
                "configured",
                DevToolOutput.NoVersion,
                "Configured from local JSON")
            {
                Hosts = DevToolHosts.Claude
            });
        }

        foreach (var plugin in GetArray(config.Root, "plugins"))
        {
            var name = GetString(plugin, "name");
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var enabled = GetBool(plugin, "enabled");
            var hosts = DevToolConfiguration.ParseHosts(plugin);
            var installedVersion = VersionOr(plugin, "installedVersion", enabled ? "configured" : "disabled");
            var availableVersion = VersionOr(plugin, "availableVersion", "catalog");
            tools.Add(new DevToolInfo(
                DevToolConfiguration.KeyFor(DevToolKind.Plugin, name),
                DevToolKind.Plugin,
                name,
                GetString(plugin, "source"),
                enabled,
                enabled,
                installedVersion,
                availableVersion,
                enabled ? "Configured from local JSON" : "Disabled in config")
            {
                Hosts = hosts,
                HostStates = HostStates(hosts, enabled, installedVersion, availableVersion)
            });
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
            var hosts = DevToolConfiguration.ParseHosts(server);
            var displayName = string.IsNullOrWhiteSpace(name) ? packageId : $"{name} ({packageId})";
            var installedVersion = VersionOr(server, "installedVersion", enabled ? "configured" : "disabled");
            var availableVersion = VersionOr(server, "availableVersion", "catalog");
            var states = new List<DevToolHostState>
            {
                new(hosts, enabled, installedVersion, availableVersion, "The .NET tool, shared by both hosts")
            };

            // The registration Claude needs on top of the shared tool. Read out of
            // the catalog rather than probed, because nothing here starts a
            // process — but drawn, because the pane's per-host detail is one of the
            // shapes a browser session is here to look at.
            if (hosts.HasFlag(DevToolHosts.Claude) && server["claude"] is { } claude)
            {
                var claudeName = GetString(claude, "name") is { Length: > 0 } registered ? registered : name;
                states.Add(new DevToolHostState(
                    DevToolHosts.Claude,
                    enabled,
                    GetString(claude, "command"),
                    GetString(claude, "command"),
                    $"Registered with Claude as '{claudeName}'"));
            }

            tools.Add(new DevToolInfo(
                DevToolConfiguration.KeyFor(DevToolKind.McpServer, packageId),
                DevToolKind.McpServer,
                displayName,
                packageId,
                enabled,
                enabled,
                installedVersion,
                availableVersion,
                enabled ? "Configured from local JSON" : "Disabled in config")
            {
                Hosts = hosts,
                HostStates = states
            });
        }

        var message = config.PcConfigExists
            ? $"Showing tools from {config.CatalogPath} with PC config {config.PcConfigPath}."
            : $"Showing tools from {config.CatalogPath}. PC config will be created at {config.PcConfigPath}.";
        return new DevToolCatalog(tools, message, CatalogExists: true, CatalogPath: config.CatalogPath, CanEditCatalog: true)
        {
            Commands = SampleCommands
        };
    }

    public Task<DevToolActionResult> UpdateAsync(string key, CancellationToken ct = default) =>
        Task.FromResult(DevToolActionResult.Failed("Tool updates are only available in the desktop app."));

    public async Task<DevToolActionResult> UpdateAllAsync(CancellationToken ct = default)
    {
        var catalog = await ListAsync(ct).ConfigureAwait(false);
        if (!catalog.Tools.Any(tool => tool.CanUpdate))
        {
            return DevToolActionResult.Ok("No enabled tools have updates available.");
        }

        return DevToolActionResult.Failed("Tool updates are only available in the desktop app.");
    }

    public Task<DevToolActionResult> EnableAsync(string key, CancellationToken ct = default) =>
        SetEnabledAsync(key, enabled: true, ct);

    public Task<DevToolActionResult> DisableAsync(string key, CancellationToken ct = default) =>
        SetEnabledAsync(key, enabled: false, ct);

    private async Task<DevToolActionResult> SetEnabledAsync(string key, bool enabled, CancellationToken ct)
    {
        await DevToolConfiguration.WriteEnabledOverrideAsync(Paths, key, enabled, ct).ConfigureAwait(false);
        return DevToolActionResult.Ok($"{key} was {(enabled ? "enabled" : "disabled")} in the local PC config.");
    }

    // Editing the catalog is a file write and nothing else, so the harness does it
    // for real rather than refusing the way it refuses an install: a browser
    // session is where the pane's create, add, remove and import are driven, and a
    // stubbed answer there would be a surface nobody has actually operated.

    public Task<DevToolActionResult> CreateCatalogAsync(CancellationToken ct = default) =>
        EditCatalogAsync(
            paths => DevToolConfiguration.CreateCatalogAsync(paths, ct),
            paths => $"Created a tool catalog at {paths.CatalogPath}.");

    public Task<DevToolActionResult> AddAsync(DevToolDraft draft, CancellationToken ct = default) =>
        EditCatalogAsync(
            paths => DevToolConfiguration.AddToCatalogAsync(paths, draft, ct),
            _ => $"{draft.Id} was added to the catalog.");

    public Task<DevToolActionResult> RemoveAsync(string key, CancellationToken ct = default) =>
        EditCatalogAsync(
            async paths =>
            {
                await DevToolConfiguration.RemoveFromCatalogAsync(paths, key, ct).ConfigureAwait(false);

                // The per-PC override outlives the catalog entry unless it goes
                // with it, and the same tool added again would then arrive
                // already disabled by a decision nobody remembers making.
                await DevToolConfiguration.RemoveEnabledOverrideAsync(paths, key, ct).ConfigureAwait(false);
            },
            _ => $"{DevToolConfiguration.ParseKey(key).IdValue} was removed from the catalog.");

    public Task<DevToolActionResult> ImportAsync(string json, CancellationToken ct = default) =>
        EditCatalogAsync(
            paths => DevToolConfiguration.ImportCatalogAsync(paths, json, ct),
            paths => $"The catalog at {paths.CatalogPath} was replaced. The previous one is beside it as .bak.");

    /// <summary>The same wrapper the desktop host uses, for the same reason:
    /// <c>.tools</c> is a folder on somebody's disk, so a refused write is an
    /// ordinary outcome and has to reach the pane as a message rather than as an
    /// exception that takes the tools surface down.</summary>
    private async Task<DevToolActionResult> EditCatalogAsync(
        Func<DevToolConfigurationPaths, Task> edit,
        Func<DevToolConfigurationPaths, string> describe)
    {
        var paths = Paths;

        try
        {
            await edit(paths).ConfigureAwait(false);
            return DevToolActionResult.Ok(describe(paths));
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or IOException or UnauthorizedAccessException or JsonException)
        {
            return DevToolActionResult.Failed(ex.Message);
        }
    }

    private DevToolConfigurationPaths Paths => DevToolConfigurationPaths.FromStorageRoot(_store.RootDirectory);

    private static IEnumerable<JsonNode> GetArray(JsonNode root, string name) =>
        root[name]?.AsArray().Where(node => node is not null).Cast<JsonNode>() ?? [];

    private static string GetString(JsonNode node, string name) => node[name]?.GetValue<string>() ?? string.Empty;

    private static bool GetBool(JsonNode node, string name) => node[name]?.GetValue<bool>() ?? false;

    /// <summary>One state per host the entry targets, all saying the same thing.
    /// Nothing was probed here, so the hosts cannot honestly disagree — what this
    /// gives the browser is the <em>shape</em>: a row with two host states, so the
    /// pane's per-host rendering is reachable without a machine that has both CLIs
    /// on it.</summary>
    private static IReadOnlyList<DevToolHostState> HostStates(DevToolHosts hosts, bool enabled, string installedVersion, string availableVersion)
    {
        var states = new List<DevToolHostState>();

        if (hosts.HasFlag(DevToolHosts.Copilot))
        {
            states.Add(new DevToolHostState(DevToolHosts.Copilot, enabled, installedVersion, availableVersion, "Configured from local JSON"));
        }

        if (hosts.HasFlag(DevToolHosts.Claude))
        {
            states.Add(new DevToolHostState(DevToolHosts.Claude, enabled, installedVersion, availableVersion, "Configured from local JSON"));
        }

        return states;
    }

    private static string VersionOr(JsonNode node, string name, string fallback) =>
        GetString(node, name) is { Length: > 0 } value ? value : fallback;
}