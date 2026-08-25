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

    /// <summary>What has been ticked this session, for the sample rows below.
    /// They are not in anybody's catalog, so the merge that carries a real row's
    /// acknowledgement back out of the per-PC file has nothing to carry — and a
    /// checkbox that forgot the moment the pane re-read would be the one shape
    /// this harness exists to let somebody actually operate.</summary>
    private readonly Dictionary<string, bool> _acknowledged = new(StringComparer.OrdinalIgnoreCase);

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

        // The applications: whatever the catalog declares, and then the sample
        // spread for every shape it does not. The harness is the only place a
        // browser can reach this pane at all — the desktop head cannot be driven —
        // so every branch the pane grew for applications has to be reachable here
        // or it is a branch nobody has ever looked at.
        foreach (var application in DevToolConfiguration.ReadApplications(config.Root))
        {
            tools.Add(CatalogApplication(application));
        }

        var declared = tools.Select(tool => tool.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var sample in SampleApplications())
        {
            // A sample is skipped rather than renamed when the catalog already has
            // that id: two rows with one key is a duplicate @key, which is a
            // render-time throw rather than a cosmetic problem.
            if (declared.Add(sample.Key))
            {
                tools.Add(sample);
            }
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

    /// <summary>Done for real, like the enable override beside it: it is a small
    /// write to the per-PC file and nothing else, and the harness is the only
    /// place a browser can tick one of these boxes and see the row come back
    /// changed.</summary>
    public async Task<DevToolActionResult> AcknowledgeAsync(string key, bool acknowledged, CancellationToken ct = default)
    {
        await DevToolConfiguration.WriteAcknowledgementAsync(Paths, key, acknowledged, ct).ConfigureAwait(false);
        _acknowledged[key] = acknowledged;

        return DevToolActionResult.Ok(acknowledged
            ? $"{key} is marked as done on this machine."
            : $"{key} is no longer marked as done on this machine.");
    }

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

    /// <summary>
    /// One application row of every shape the pane can draw.
    ///
    /// <para>This is the harness earning its keep. The desktop head is a MAUI
    /// window that no browser driver can reach, so every application branch the
    /// pane grew — an update, an install, a package the machine has to be given by
    /// hand, a checklist item that answers yes, one that answers no, one that can
    /// fix itself, and a tick nothing can check — is validated here or nowhere.
    /// A shorter list would be a shorter QA pass, not a smaller harness.</para>
    ///
    /// <para>The versions are real ones, and the notes are the catalog's real
    /// notes, because a sample that reads as sample data teaches a reader nothing
    /// about how the row will look on the day it matters.</para>
    /// </summary>
    private IEnumerable<DevToolInfo> SampleApplications()
    {
        // Installed and current: the row that should be quiet.
        yield return Application(
            "Microsoft.VisualStudioCode", "Visual Studio Code", DevToolProvider.Winget,
            "Developer Configurations baseline", "1.104.2", "1.104.2", installed: true,
            status: "Application installed");

        // Installed and behind: the Update button.
        yield return Application(
            "Git.Git", "Git", DevToolProvider.Winget,
            "Developer Configurations baseline", "2.51.0", "2.52.0", installed: true,
            status: "Update available for application");

        // Configured and absent: the Install button.
        yield return Application(
            "Microsoft.PowerToys", "PowerToys", DevToolProvider.Winget,
            "Team developer tools", DevToolOutput.NotInstalled, "0.96.1", installed: false,
            status: "Not installed");

        // Known to the package manager and not installable by it: no button, and a
        // note that says why rather than a row that looks broken.
        yield return Application(
            "Microsoft.Office", "Microsoft 365 apps", DevToolProvider.Winget,
            "Microsoft 365 and productivity", "16.0.19231.20044", "16.0.18827.20164", installed: true,
            installable: false,
            note: "Click-to-Run updates on its own channel, so the winget manifest version routinely reads lower than what is installed. Sign-in and activation are interactive.",
            status: "Application installed");

        // A row this machine has switched off. It wants nothing, whatever it says.
        yield return Application(
            "JetBrains.ReSharper", "ReSharper", DevToolProvider.Winget,
            "Visual Studio family", DevToolOutput.NotInstalled, DevToolOutput.Unknown, installed: false,
            enabled: false, installable: false,
            note: "Marked \"if your team uses it\" in the HowTo.",
            status: "Disabled in config");

        yield return Application(
            "ms-dotnettools.csdevkit", "C# Dev Kit", DevToolProvider.VsCodeExtension,
            "VS Code extensions", "1.31.9", "1.31.9", installed: true,
            status: "Extension installed");

        yield return Application(
            "ms-azuretools.vscode-docker", "Docker", DevToolProvider.VsCodeExtension,
            "VS Code extensions", "2.0.0", "2.1.0", installed: true,
            status: "Update available for extension");

        // An extension the marketplace could not be asked about. Not "up to date":
        // there is no version to have matched.
        yield return Application(
            "ms-vscode.PowerShell", "PowerShell", DevToolProvider.VsCodeExtension,
            "VS Code extensions", "2025.2.0", DevToolOutput.Unknown, installed: true,
            status: "Extension installed");

        // The checklist rows: no version on either side, so the column says
        // whether it was detected rather than pretending to a number.
        yield return Application(
            "dev-drive", "Dev Drive configured", DevToolProvider.Command,
            "Dev Drive and package caches", DevToolOutput.NoVersion, DevToolOutput.NoVersion, installed: true,
            installable: false,
            note: "A heuristic. The authoritative per-volume flag, fsutil devdrv query D:, needs elevation and returns Access Denied without it, so this checks for ReFS on D: instead.",
            status: "Checklist item: done");

        yield return Application(
            "npm-cache-on-dev-drive", "npm cache on Dev Drive", DevToolProvider.Command,
            "Dev Drive and package caches", DevToolOutput.NoVersion, DevToolOutput.NoVersion, installed: false,
            installable: false,
            note: "The HowTo scopes this to \"when npm is installed\", so an absent npm is not a failure.",
            status: "Checklist item: not done yet");

        // The two checklist rows that can fix themselves. They keep the button the
        // ones above do not have, which is the distinction the Installable flag is
        // there to carry.
        yield return Application(
            "git-pull-rebase", "git pull.rebase is true", DevToolProvider.Command,
            "Git configuration", DevToolOutput.NoVersion, DevToolOutput.NoVersion, installed: false,
            note: "An unset key exits 1 with empty output. That is \"not configured\", not an error.",
            status: "Not configured");

        yield return Application(
            "git-rebase-autostash", "git rebase.autoStash is true", DevToolProvider.Command,
            "Git configuration", DevToolOutput.NoVersion, DevToolOutput.NoVersion, installed: true,
            status: "Configured");

        // The two manual rows, one ticked and one not. Both are only ever what
        // somebody said; neither is ever drawn as a thing that was found.
        yield return Application(
            "office-signed-in", "Office apps activated and signed in", DevToolProvider.Manual,
            "Manual verification", DevToolOutput.NotInstalled, DevToolOutput.NoVersion, installed: false,
            installable: false, acknowledged: true,
            status: "Nothing can check this — confirm it by hand");

        yield return Application(
            "onenote-available", "OneNote available", DevToolProvider.Manual,
            "Manual verification", DevToolOutput.NotInstalled, DevToolOutput.NoVersion, installed: false,
            installable: false,
            note: "No standalone winget package exists — OneNote ships inside Microsoft.Office. The only standalone is the Store package XPFFZHVGQWWLHB, which needs a signed-in Store account.",
            status: "Nothing can check this — confirm it by hand");
    }

    /// <summary>
    /// A real catalog entry as a row, without running anything for it.
    ///
    /// <para>Read rather than probed, and the columns say so: this harness starts
    /// no processes, and a fake that answered "installed" for an application
    /// nobody looked for would be worse than one that says it does not know. What
    /// it does carry honestly is the entry's shape — its group, its note, whether
    /// there is a mechanism behind it — which is what decides how the pane draws
    /// it, and is the half a browser session is here to look at.</para>
    /// </summary>
    private DevToolInfo CatalogApplication(DevToolApplication application)
    {
        var manual = application.Provider is DevToolProvider.Manual;
        var checklist = application.Provider is DevToolProvider.Command && !string.IsNullOrWhiteSpace(application.Detect?.Expect);

        // A checklist row has no version anywhere, a manual row has nothing to
        // look up at all, and everything else has a version this harness did not
        // go and find.
        var installedVersion = manual || checklist ? DevToolOutput.NoVersion : DevToolOutput.Unknown;

        return Application(
            application.Id,
            application.Name,
            application.Provider,
            application.Group ?? string.Empty,
            manual ? DevToolOutput.NotInstalled : installedVersion,
            manual || checklist ? DevToolOutput.NoVersion : DevToolOutput.Unknown,
            installed: false,
            enabled: application.Enabled,
            installable: !manual && !application.DetectOnly && (application.Provider is not DevToolProvider.Command || application.Install is not null),
            acknowledged: application.Acknowledged,
            note: application.Note,
            status: manual
                ? "Nothing can check this — confirm it by hand"
                : "Read from the catalog; this harness starts no processes");
    }

    /// <summary>The shape every application row shares, so the sample spread and
    /// the catalog rows differ only in the answers they carry.</summary>
    private DevToolInfo Application(
        string id,
        string name,
        DevToolProvider provider,
        string group,
        string installedVersion,
        string availableVersion,
        bool installed,
        bool enabled = true,
        bool installable = true,
        bool acknowledged = false,
        string? note = null,
        string status = "")
    {
        var key = DevToolConfiguration.KeyFor(DevToolKind.Application, id);

        // A tick made this session wins over whatever the row was seeded with.
        // The sample rows are in nobody's catalog, so there is no merge to carry
        // one back out of the per-PC file for them.
        var confirmed = _acknowledged.TryGetValue(key, out var recorded) ? recorded : acknowledged;

        return new DevToolInfo(
            key,
            DevToolKind.Application,
            name,
            // The provider stands in for the source column, matching the desktop
            // head: for an application, that is what "where does this come from"
            // means.
            DevToolConfiguration.ProviderName(provider),
            enabled,
            provider is DevToolProvider.Manual ? confirmed : installed,
            installedVersion,
            availableVersion,
            // The note travels appended to the status behind a middle dot, which
            // is the shape the pane splits on. It is the port's one string for
            // "what was found, and what the entry wanted said about it".
            string.IsNullOrWhiteSpace(note) ? status : $"{status} · {note}")
        {
            // Not a host's tool: an application is installed into the machine, and
            // claiming Copilot or Claude would put an Install for it on a host that
            // has never heard of it.
            Hosts = DevToolHosts.None,
            Group = string.IsNullOrWhiteSpace(group) ? null : group,
            Installable = installable,
            Acknowledged = confirmed,
            ConfirmedByHand = provider is DevToolProvider.Manual
        };
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