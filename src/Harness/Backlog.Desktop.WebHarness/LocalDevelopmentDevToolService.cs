using System.Text.Json;
using System.Text.Json.Nodes;
using Backlog.Desktop.UI.Tasks;
using Backlog.Desktop.UI.Knowledge;
using Backlog.Modules.DevPc.Abstractions;
using Backlog.Modules.Tasks.Abstractions.Services;

namespace Backlog.Desktop.WebHarness;

/// <summary>
/// The harness's own answer to the tools port, read out of the catalog JSON and
/// nothing else.
/// <para>Public rather than internal, like <c>LocalAzureFoundryCompletion</c> in
/// the harness beside it, so the unit tests can drive it directly. Everything it
/// answers comes from files, so the half of the pane a browser is slowest to
/// reach is the half a test can pin in milliseconds.</para>
/// </summary>
public sealed class LocalDevelopmentDevToolService : IDevToolService
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

    /// <summary>What a row this machine does not want reports, in the one
    /// spelling the desktop head uses for it.</summary>
    private const string DisabledStatus = "Disabled in config";

    private readonly ITaskStore _store;

    /// <summary>What has been ticked this session, for the sample rows below.
    /// They are not in anybody's catalog, so the merge that carries a real row's
    /// acknowledgement back out of the per-PC file has nothing to carry — and a
    /// checkbox that forgot the moment the pane re-read would be the one shape
    /// this harness exists to let somebody actually operate.</summary>
    private readonly Dictionary<string, bool> _acknowledged = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>What has been switched off — or back on — this session, for the
    /// same rows and the same reason as <see cref="_acknowledged"/> above.
    ///
    /// <para>A catalog row needs nothing here: its override goes into the per-PC
    /// file and <see cref="DevToolConfiguration.ReadAsync"/> merges it back over
    /// the catalog on the next read, which is how the desktop head resolves one
    /// too. The sample rows are in no catalog, so that merge finds nothing to
    /// merge into and drops the write — leaving Disable as a button that reported
    /// success and changed nothing, on the only rows a browser can reach.</para>
    /// </summary>
    private readonly Dictionary<string, bool> _enabled = new(StringComparer.OrdinalIgnoreCase);

    public LocalDevelopmentDevToolService(ITaskStore store)
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
                enabled ? "Configured from local JSON" : DisabledStatus)
            {
                Hosts = hosts,
                HostStates = HostStates(hosts, enabled, installedVersion, availableVersion)
            });
        }

        foreach (var node in GetArray(config.Root, DevToolConfiguration.McpServersArrayName))
        {
            // Through the abstraction's own reader, which is what the desktop head
            // enumerates with too. Which property identifies an entry and which
            // mechanism is behind it are decisions this harness has to answer
            // identically — the pane is one component over two implementations of
            // one port, and an array read twice, two ways, is how this half came to
            // lie about a catalog it was reading correctly.
            if (DevToolConfiguration.ReadMcpServer(node) is { } server)
            {
                tools.Add(McpServer(node, server));
            }
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
    /// changed.
    ///
    /// <para>Through the same wrapper the catalog edits go through, because the
    /// abstraction refuses a marketplace key by throwing and a caller of
    /// <c>IDevToolService</c> reads failure off the result it is handed. The
    /// desktop head guards the kind before it ever gets here; this adapter called
    /// straight through, so that refusal escaped as an exception.</para></summary>
    public Task<DevToolActionResult> AcknowledgeAsync(string key, bool acknowledged, CancellationToken ct = default) =>
        EditCatalogAsync(
            async paths =>
            {
                await DevToolConfiguration.WriteAcknowledgementAsync(paths, key, acknowledged, ct).ConfigureAwait(false);
                _acknowledged[key] = acknowledged;
            },
            _ => acknowledged
                ? $"{key} is marked as done on this machine."
                : $"{key} is no longer marked as done on this machine.");

    /// <inheritdoc cref="AcknowledgeAsync" />
    private Task<DevToolActionResult> SetEnabledAsync(string key, bool enabled, CancellationToken ct) =>
        EditCatalogAsync(
            async paths =>
            {
                await DevToolConfiguration.WriteEnabledOverrideAsync(paths, key, enabled, ct).ConfigureAwait(false);
                _enabled[key] = enabled;
            },
            _ => $"{key} was {(enabled ? "enabled" : "disabled")} in the local PC config.");

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
            status: DisabledStatus);

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

    /// <summary>
    /// One MCP server row, of either mechanism the array holds.
    ///
    /// <para>The versions still come out of the catalog — nothing here starts a
    /// process — but only for a server that ships as a .NET tool, because that is
    /// the only kind those two properties are about. A server registered by the
    /// command it declares reports the command line where the installed version
    /// goes, the way the desktop head lets a command stand in for a registration's
    /// version, and keeps the no-version dash opposite it: there is nothing
    /// published for it to be behind, and a second copy of its own command line
    /// there would make the row read as up to date about a lookup nobody
    /// performed.</para>
    ///
    /// <para>An entry with no .NET tool and no command either — <c>manual</c>, or a
    /// <c>packageId</c> entry naming a mechanism this build does not know — has no
    /// command line to stand in, so it keeps the no-version dash on both sides.
    /// That is the marker the pane already reads as "there is deliberately nothing
    /// here", where an empty string is a cell that looks like it broke; the desktop
    /// head answers the same shape the same way, and the two have to agree or the
    /// browser copy of this pane stops being worth looking at.</para>
    /// </summary>
    private static DevToolInfo McpServer(JsonNode node, DevToolMcpServer server)
    {
        var enabled = server.Enabled;
        var hosts = server.Hosts;
        var installedVersion = server.Installable
            ? VersionOr(node, "installedVersion", enabled ? "configured" : "disabled")
            : server.CommandLine is { Length: > 0 } commandLine ? commandLine : DevToolOutput.NoVersion;
        var availableVersion = server.Installable
            ? VersionOr(node, "availableVersion", "catalog")
            : DevToolOutput.NoVersion;

        var states = new List<DevToolHostState>
        {
            new(
                hosts,
                enabled,
                installedVersion,
                availableVersion,
                server.Mechanism switch
                {
                    DevToolMcpMechanism.DotNetTool => "The .NET tool, shared by both hosts",
                    DevToolMcpMechanism.Command => "Registered by the command it declares",
                    _ => "Nothing here installs or registers this server"
                })
        };

        // The registration Claude needs on top of the shared tool. Read out of
        // the catalog rather than probed, because nothing here starts a
        // process — but drawn, because the pane's per-host detail is one of the
        // shapes a browser session is here to look at.
        //
        // Only for a .NET tool. For a command-registered server the command *is*
        // the registration, so a second state repeating it would be the same fact
        // twice — and its version columns would be the invented version the row
        // above is careful not to have.
        if (server.Installable && hosts.HasFlag(DevToolHosts.Claude) && node["claude"] is { } claude)
        {
            var claudeName = GetString(claude, "name") is { Length: > 0 } registered ? registered : server.Name;
            states.Add(new DevToolHostState(
                DevToolHosts.Claude,
                enabled,
                GetString(claude, "command"),
                GetString(claude, "command"),
                $"Registered with Claude as '{claudeName}'"));
        }

        // A mechanism this build does not know is drawn action-less and says so.
        // The note travels appended to the status behind a middle dot, which is
        // the shape the pane splits on and the shape the application rows below
        // already use for what an entry wanted said about itself.
        var status = enabled ? "Configured from local JSON" : DisabledStatus;
        var note = server.MechanismRecognised
            ? null
            : $"\"{server.DeclaredMechanism}\" is not a mechanism this build knows, so nothing runs for this row";

        return new DevToolInfo(
            server.Key,
            DevToolKind.McpServer,
            server.DisplayName,
            server.Source,
            enabled,
            enabled,
            installedVersion,
            availableVersion,
            note is null ? status : $"{status} · {note}")
        {
            Hosts = hosts,
            HostStates = states,
            Installable = server.Installable
        };
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

        // And the same for the switch beside it. A catalog row arrives here with
        // the override already merged in, so this agrees with what it was passed;
        // a sample row would otherwise be re-seeded from its literal on every
        // read, which is the whole of what Disable used to do.
        var wanted = _enabled.TryGetValue(key, out var chosen) ? chosen : enabled;

        // The seeded status says what was found, and that stays true when the
        // machine switches the row off — but the desktop head reports a row it
        // does not want as disabled and nothing else, so this does too, and so do
        // the plugin and MCP branches above. The other direction matters for the
        // one sample row seeded switched off: re-enabled, its status must stop
        // saying it is disabled.
        var reported = wanted
            ? status == DisabledStatus ? "Not installed" : status
            : DisabledStatus;

        return new DevToolInfo(
            key,
            DevToolKind.Application,
            name,
            // The provider stands in for the source column, matching the desktop
            // head: for an application, that is what "where does this come from"
            // means.
            DevToolConfiguration.ProviderName(provider),
            wanted,
            provider is DevToolProvider.Manual ? confirmed : installed,
            installedVersion,
            availableVersion,
            // The note travels appended to the status behind a middle dot, which
            // is the shape the pane splits on. It is the port's one string for
            // "what was found, and what the entry wanted said about it".
            string.IsNullOrWhiteSpace(note) ? reported : $"{reported} · {note}")
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