using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Backlog.Desktop.UI.BacklogManagement;
using Backlog.Desktop.UI.Knowledge;
using Backlog.Modules.DevPc.Abstractions;
using Backlog.Modules.Backlog.Abstractions.Services;
using Microsoft.Extensions.Logging;

namespace Backlog.Desktop.Services;

public sealed class DevToolService : IDevToolService
{
    /// <summary>What a row and the status line say when the host a catalog entry
    /// targets is not installed on this machine.
    ///
    /// <para>Said plainly and in full, because the alternative shape — a Claude row
    /// reading "not installed" — is indistinguishable from a plugin that genuinely
    /// needs installing, and offers an Install that has nothing to run.</para></summary>
    private const string ClaudeCliMissing = "The Claude CLI was not found on this machine.";

    /// <inheritdoc cref="ClaudeCliMissing" />
    private const string CopilotCliMissing = "Neither the copilot nor the gh CLI was found on this machine.";

    /// <summary>A Claude plugin id is <c>&lt;name&gt;@&lt;marketplace&gt;</c>, so a
    /// catalog with no marketplaces at all can resolve none of them. Reported on
    /// the row rather than thrown, because it is the catalog's own gap and every
    /// other row is unaffected by it.</summary>
    private const string NoMarketplace = "No Claude marketplace is configured in the catalog.";

    /// <summary>The exit code stood in for a command that could not be started at
    /// all. Any non-zero value would do; a distinct one keeps "the CLI is absent"
    /// legible in the command log next to a CLI that ran and refused.</summary>
    private const int CommandNotFound = -1;

    private readonly IBacklogStore? _store;
    private readonly string? _configPath;
    private readonly ILogger<DevToolService>? _logger;

    public DevToolService(ILogger<DevToolService>? logger = null, string? configPath = null)
        : this(null, logger, configPath)
    {
    }

    public DevToolService(IBacklogStore store, ILogger<DevToolService>? logger = null)
        : this(store, logger, null)
    {
    }

    private DevToolService(IBacklogStore? store, ILogger<DevToolService>? logger, string? configPath)
    {
        _store = store;
        _logger = logger;
        _configPath = configPath;
    }

    public async Task<DevToolCatalog> ListAsync(CancellationToken ct = default)
    {
        var configPaths = ConfigurationPaths;
        if (!DevToolConfiguration.CatalogExists(configPaths))
        {
            // The path travels with the "not found" answer: it is what the pane
            // names in its empty state, and what the create button is offering to
            // write. An empty list on its own cannot say either.
            return new DevToolCatalog(
                [],
                $"Tool catalog was not found at {configPaths.CatalogPath}.",
                CatalogExists: false,
                CatalogPath: configPaths.CatalogPath,
                CanEditCatalog: true);
        }

        var config = await DevToolConfiguration.ReadAsync(configPaths, ct).ConfigureAwait(false);
        var root = config.Root;
        var messages = new List<string>();
        var installedPlugins = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var installedTools = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var log = new CommandLog();

        // Both CLIs are resolved before anything is asked of either, and neither
        // being there is an ordinary answer rather than a failure. This machine may
        // run only Copilot, only Claude, or a Claude that ships inside the desktop
        // app and never reaches PATH — and in every one of those the rows for the
        // host that *is* installed still have to work.
        var copilotCli = await ResolveCopilotCliAsync(log, ct).ConfigureAwait(false);
        if (copilotCli is null)
        {
            messages.Add(CopilotCliMissing);
        }
        else
        {
            try
            {
                installedPlugins = await GetInstalledPluginsAsync(copilotCli.Value, log, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to list Copilot plugins.");
                messages.Add("Copilot plugins could not be checked.");
            }
        }

        try
        {
            installedTools = await GetInstalledDotNetToolsAsync(log, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to list .NET tools.");
            messages.Add(".NET tools could not be checked.");
        }

        var claudeCli = await ResolveClaudeCliAsync(log, ct).ConfigureAwait(false);
        var claudeMarketplaces = (IReadOnlySet<string>)new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var claudePlugins = (IReadOnlyDictionary<string, DevToolOutput.ClaudePluginState>)
            new Dictionary<string, DevToolOutput.ClaudePluginState>(StringComparer.OrdinalIgnoreCase);

        if (claudeCli is null)
        {
            messages.Add(ClaudeCliMissing);
        }
        else
        {
            claudeMarketplaces = await GetClaudeMarketplacesAsync(claudeCli, log, ct).ConfigureAwait(false);
            claudePlugins = await GetInstalledClaudePluginsAsync(claudeCli, log, ct).ConfigureAwait(false);
        }

        var defaultMarketplace = DevToolConfiguration.DefaultMarketplaceName(root);
        var tools = new List<DevToolInfo>();

        // Marketplaces lead the table because they lead the install order: every
        // Claude plugin id resolves against one, so a marketplace that was never
        // added is the single reason a whole block of rows below it will fail.
        foreach (var marketplace in DevToolConfiguration.MarketplaceEntries(root))
        {
            var name = GetString(marketplace, "name");
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var known = claudeMarketplaces.Contains(name);
            tools.Add(new DevToolInfo(
                MarketplaceKey(name),
                DevToolKind.Marketplace,
                name,
                GetString(marketplace, "source"),
                ConfiguredEnabled: true,
                Installed: known,
                known ? "configured" : DevToolOutput.NotInstalled,
                // A marketplace has no version to compare. It is added or it is
                // not, and printing a number here would invite a comparison that
                // means nothing.
                DevToolOutput.NoVersion,
                claudeCli is null ? ClaudeCliMissing : known ? "Configured marketplace" : "Not added to Claude yet")
            {
                Hosts = DevToolHosts.Claude
            });
        }

        foreach (var plugin in GetArray(root, "plugins"))
        {
            var name = GetString(plugin, "name");
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            tools.Add(await DescribePluginAsync(
                plugin,
                name,
                copilotCli,
                installedPlugins,
                claudeCli,
                claudePlugins,
                defaultMarketplace,
                log,
                ct).ConfigureAwait(false));
        }

        foreach (var server in GetArray(root, "mcpServers"))
        {
            var packageId = GetString(server, "packageId");
            if (string.IsNullOrWhiteSpace(packageId))
            {
                continue;
            }

            tools.Add(await DescribeMcpServerAsync(server, packageId, installedTools, claudeCli, log, ct).ConfigureAwait(false));
        }

        var sourceMessage = config.PcConfigExists
            ? $"Showing tools from {config.CatalogPath} with PC config {config.PcConfigPath}."
            : $"Showing tools from {config.CatalogPath}. PC config will be created at {config.PcConfigPath}.";
        var message = messages.Count == 0 ? sourceMessage : $"{sourceMessage} {string.Join(" ", messages)}";
        return new DevToolCatalog(tools, message, CatalogExists: true, CatalogPath: config.CatalogPath, CanEditCatalog: true)
        {
            Commands = log.Commands
        };
    }

    /// <summary>
    /// One catalog plugin as one row, with an answer per host it targets.
    ///
    /// <para>One row and not two, because the catalog entry is one entry: two rows
    /// would mean two Remove buttons for something the catalog can only lose
    /// once. The per-host detail lives in <see cref="DevToolInfo.HostStates" />
    /// instead, and the row's own columns summarise it.</para>
    ///
    /// <para>A host that cannot be inspected contributes no state at all rather
    /// than a state that guesses. "The Claude CLI is not on this machine" and "this
    /// plugin is not installed in Claude" look identical once flattened to a
    /// boolean, and only one of them is worth offering an Install for.</para>
    /// </summary>
    private static async Task<DevToolInfo> DescribePluginAsync(
        JsonNode plugin,
        string name,
        (string Command, string[] Prefix)? copilotCli,
        IReadOnlyDictionary<string, string> installedPlugins,
        string? claudeCli,
        IReadOnlyDictionary<string, DevToolOutput.ClaudePluginState> claudePlugins,
        string? defaultMarketplace,
        CommandLog log,
        CancellationToken ct)
    {
        var kind = GetString(plugin, "kind");
        var enabled = GetBool(plugin, "enabled");
        var hosts = DevToolConfiguration.ParseHosts(plugin);
        var repositoryBacked = IsRepositoryBacked(plugin);
        var states = new List<DevToolHostState>();
        var notes = new List<string>();

        // Looked up once and shared. A Claude plugin's source is the same
        // owner/repo:path shorthand the Copilot entry carries, so the published
        // version behind them is one manifest and asking GitHub for it twice would
        // double the calls to say the same thing.
        string? availableVersion = null;
        async Task<string> AvailableAsync() =>
            availableVersion ??= await GetPluginAvailableVersionAsync(plugin, log, ct).ConfigureAwait(false);

        if (hosts.HasFlag(DevToolHosts.Copilot))
        {
            // A repository-backed plugin is a git clone this host manages itself,
            // so it stays knowable with no Copilot CLI at all. Everything else is
            // only knowable through the CLI, and with none there is nothing to
            // report but that.
            if (copilotCli is null && !repositoryBacked)
            {
                notes.Add(CopilotCliMissing);
            }
            else
            {
                var installedVersion = await GetPluginInstalledVersionAsync(plugin, installedPlugins, log, ct).ConfigureAwait(false);
                var installed = !installedVersion.Equals(DevToolOutput.NotInstalled, StringComparison.OrdinalIgnoreCase);
                var available = await AvailableAsync().ConfigureAwait(false);

                states.Add(new DevToolHostState(
                    DevToolHosts.Copilot,
                    installed,
                    installedVersion,
                    available,
                    DescribeStatus(enabled, installed, DevToolInfo.VersionDiffers(installedVersion, available), string.IsNullOrWhiteSpace(kind) ? "plugin" : kind)));
            }
        }

        if (hosts.HasFlag(DevToolHosts.Claude))
        {
            if (IsCopilotOnlyKind(kind))
            {
                // repository-skills copies flat files into ~/.copilot/skills and
                // repository-canvases copies extension folders beside them. Both
                // are Copilot mechanisms with no Claude counterpart, so the Claude
                // half of such an entry is not a failure to report — it is a thing
                // that does not exist.
                notes.Add($"Claude skips '{kind}'");
            }
            else if (claudeCli is null)
            {
                notes.Add(ClaudeCliMissing);
            }
            else if (ClaudePluginIdFor(plugin, name, defaultMarketplace) is not { } pluginId)
            {
                notes.Add(NoMarketplace);
            }
            else
            {
                var state = claudePlugins.GetValueOrDefault(pluginId);
                var installedVersion = state?.Version ?? DevToolOutput.NotInstalled;
                var available = await AvailableAsync().ConfigureAwait(false);

                states.Add(new DevToolHostState(
                    DevToolHosts.Claude,
                    state is not null,
                    installedVersion,
                    available,
                    state is { Enabled: false }
                        ? "Installed but switched off in Claude"
                        : DescribeStatus(enabled, state is not null, DevToolInfo.VersionDiffers(installedVersion, available), "Claude plugin")));
            }
        }

        return new DevToolInfo(
            PluginKey(name),
            DevToolKind.Plugin,
            name,
            GetString(plugin, "source"),
            enabled,
            // No inspectable host means nothing is known, and "not installed" would
            // be a claim. Reporting it as present is what keeps the row from
            // offering an Install that has no CLI to run.
            states.Count == 0 || states.All(state => state.Installed),
            Summarize(states, state => state.InstalledVersion),
            Summarize(states, state => state.AvailableVersion),
            AggregateStatus(enabled, states, notes, string.IsNullOrWhiteSpace(kind) ? "plugin" : kind))
        {
            Hosts = hosts,
            HostStates = states
        };
    }

    /// <summary>
    /// One catalog MCP server as one row.
    ///
    /// <para>The <c>packageId</c> install is a global .NET tool and both hosts run
    /// the same copy of it, so it is one state attributed to whichever hosts the
    /// entry targets rather than one per host. What Claude adds on top is a
    /// user-scope registration pointing at that tool, and that is the second
    /// state.</para>
    ///
    /// <para>The version columns stay the .NET tool's, because that is what a
    /// version means for an MCP server. The registration's own answer — absent,
    /// pointing somewhere else, or owned by another scope — travels in its host
    /// state, where it decides what the row offers without displacing the number
    /// the columns are for.</para>
    /// </summary>
    private static async Task<DevToolInfo> DescribeMcpServerAsync(
        JsonNode server,
        string packageId,
        IReadOnlyDictionary<string, string> installedTools,
        string? claudeCli,
        CommandLog log,
        CancellationToken ct)
    {
        var name = GetString(server, "name");
        var displayName = string.IsNullOrWhiteSpace(name) ? packageId : $"{name} ({packageId})";
        var enabled = GetBool(server, "enabled");
        var hosts = DevToolConfiguration.ParseHosts(server);
        var toolInstalled = installedTools.ContainsKey(packageId);
        var installedVersion = installedTools.TryGetValue(packageId, out var version) ? version : DevToolOutput.NotInstalled;
        var availableVersion = await GetDotNetToolAvailableVersionAsync(packageId, log, ct).ConfigureAwait(false);

        var states = new List<DevToolHostState>
        {
            new(
                hosts,
                toolInstalled,
                installedVersion,
                availableVersion,
                DescribeStatus(enabled, toolInstalled, DevToolInfo.VersionDiffers(installedVersion, availableVersion), "mcp-server"))
        };
        var notes = new List<string>();

        if (hosts.HasFlag(DevToolHosts.Claude) && server["claude"] is { } claude)
        {
            var serverName = ClaudeServerName(server, claude);
            var command = GetString(claude, "command");

            if (claudeCli is null)
            {
                notes.Add(ClaudeCliMissing);
            }
            else if (string.IsNullOrWhiteSpace(serverName) || string.IsNullOrWhiteSpace(command))
            {
                notes.Add("Claude registration needs a name and a command");
            }
            else
            {
                var details = await GetClaudeMcpServerAsync(claudeCli, serverName, log, ct).ConfigureAwait(false);
                states.Add(DescribeClaudeRegistration(serverName, command, details));
            }
        }

        return new DevToolInfo(
            McpServerKey(packageId),
            DevToolKind.McpServer,
            displayName,
            packageId,
            enabled,
            states.All(state => state.Installed),
            installedVersion,
            availableVersion,
            AggregateStatus(enabled, states, notes, "mcp-server"))
        {
            Hosts = hosts,
            HostStates = states
        };
    }

    /// <summary>
    /// What one Claude MCP registration is, said in the terms the row acts on.
    ///
    /// <para>The command stands in for the version, because for a registration it
    /// is the thing that can be out of date: a tool renamed in the catalog leaves a
    /// registration pointing at an executable that no longer exists, and the fix is
    /// a remove and a re-add rather than an upgrade.</para>
    ///
    /// <para>A registration in any scope but <c>user</c> is reported and left
    /// exactly alone — it belongs to a project or to a deliberate local override,
    /// and reaching into either from here would undo somebody's decision on a
    /// machine-wide sweep. Saying so with matching versions is what stops the row
    /// offering to "fix" it.</para>
    /// </summary>
    private static DevToolHostState DescribeClaudeRegistration(string serverName, string command, DevToolOutput.ClaudeMcpServerDetails? details)
    {
        if (details is null)
        {
            return new DevToolHostState(
                DevToolHosts.Claude,
                Installed: false,
                DevToolOutput.NotInstalled,
                command,
                $"Not registered with Claude as '{serverName}'");
        }

        if (!details.IsUserScope)
        {
            var scope = string.IsNullOrWhiteSpace(details.Scope) ? "another" : details.Scope;
            return new DevToolHostState(
                DevToolHosts.Claude,
                Installed: true,
                $"{scope} scope",
                $"{scope} scope",
                $"Left alone: '{serverName}' is registered at {scope} scope");
        }

        return new DevToolHostState(
            DevToolHosts.Claude,
            Installed: true,
            string.IsNullOrWhiteSpace(details.Command) ? DevToolOutput.Unknown : details.Command,
            command,
            string.Equals(details.Command, command, StringComparison.Ordinal)
                ? $"Registered with Claude as '{serverName}'"
                : $"Registered with Claude as '{serverName}', pointing elsewhere");
    }

    public Task<DevToolActionResult> UpdateAsync(string key, CancellationToken ct = default) => ApplyAsync(key, null, ct);

    public async Task<DevToolActionResult> UpdateAllAsync(CancellationToken ct = default)
    {
        var catalog = await ListAsync(ct).ConfigureAwait(false);

        // "Update all" is several runs stitched together, so its log is too: the
        // scan that decided what was out of date, then each tool's own commands
        // in the order they ran. Keeping only the last tool's would hide the
        // failure that mattered behind whichever one happened to go last.
        var commands = new List<DevToolCommand>(catalog.Commands);

        // A marketplace Claude has never been told about is included even though it
        // has no version to be behind. It is the one gap that fails a whole block
        // of rows for a single reason — every Claude plugin id resolves against a
        // marketplace — and an "Update all" that skipped it would leave the
        // operator pressing Update on plugin after plugin that cannot resolve.
        var candidates = catalog.Tools
            .Where(tool => tool.CanUpdate || (tool.Kind is DevToolKind.Marketplace && !tool.Installed))
            .ToArray();
        if (candidates.Length == 0)
        {
            return DevToolActionResult.Ok("No enabled tools have updates available.", commands);
        }

        var updated = 0;
        var failures = new List<string>();
        foreach (var tool in candidates)
        {
            var result = await ApplyAsync(tool.Key, null, ct).ConfigureAwait(false);
            commands.AddRange(result.Commands);
            if (result.Succeeded)
            {
                updated++;
            }
            else
            {
                failures.Add($"{tool.Name}: {result.Message}");
            }
        }

        if (failures.Count > 0)
        {
            return DevToolActionResult.Failed($"Updated {updated} tool(s); {failures.Count} failed. {string.Join(" ", failures)}", commands);
        }

        return DevToolActionResult.Ok($"Updated {updated} tool(s).", commands);
    }

    public Task<DevToolActionResult> EnableAsync(string key, CancellationToken ct = default) => ApplyAsync(key, true, ct);

    public Task<DevToolActionResult> DisableAsync(string key, CancellationToken ct = default) => ApplyAsync(key, false, ct);

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

    /// <summary>
    /// The four catalog edits share one shape: delegate to the shared writer, and
    /// turn everything it can refuse into a message the pane can put on its status
    /// line.
    ///
    /// <para>Nothing here is allowed to throw at the pane. <c>.tools</c> is a
    /// folder on somebody's disk — read-only, OneDrive-synced, open in an editor —
    /// so a failed write is an ordinary outcome, and a pane that fell over on one
    /// would take the tools surface down with it.</para>
    /// </summary>
    private async Task<DevToolActionResult> EditCatalogAsync(
        Func<DevToolConfigurationPaths, Task> edit,
        Func<DevToolConfigurationPaths, string> describe)
    {
        var paths = ConfigurationPaths;

        try
        {
            await edit(paths).ConfigureAwait(false);
            return DevToolActionResult.Ok(describe(paths));
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or IOException or UnauthorizedAccessException or JsonException)
        {
            _logger?.LogWarning(ex, "Tool catalog edit failed.");
            return DevToolActionResult.Failed(ex.Message);
        }
    }

    /// <summary>One action, and every command it ran. The log is attached here
    /// rather than at each return inside <see cref="ApplyCoreAsync" />, because
    /// the exits that most need it are the ones that leave early: a refusal
    /// carries the output explaining it, and the catch below would otherwise
    /// report an exception message with nothing behind it.</summary>
    private async Task<DevToolActionResult> ApplyAsync(string key, bool? enabled, CancellationToken ct)
    {
        var log = new CommandLog();
        var result = await ApplyCoreAsync(key, enabled, log, ct).ConfigureAwait(false);

        return result with { Commands = log.Commands };
    }

    private async Task<DevToolActionResult> ApplyCoreAsync(string key, bool? enabled, CommandLog log, CancellationToken ct)
    {
        var configPaths = ConfigurationPaths;
        if (!DevToolConfiguration.CatalogExists(configPaths))
        {
            return DevToolActionResult.Failed($"Tool catalog was not found at {configPaths.CatalogPath}.");
        }

        var config = await DevToolConfiguration.ReadAsync(configPaths, ct).ConfigureAwait(false);
        var root = config.Root;
        var node = FindToolNode(root, key);
        if (node is null)
        {
            return DevToolActionResult.Failed("That tool is no longer in the config.");
        }

        if (key.StartsWith("marketplace:", StringComparison.OrdinalIgnoreCase))
        {
            // A marketplace has no enabled flag to override. It is where Claude
            // plugins come from, not a tool this machine opts into, so the pane
            // offers it add and update and nothing else.
            if (enabled is not null)
            {
                return DevToolActionResult.Failed("A marketplace is not enabled or disabled; remove it from the catalog instead.");
            }

            try
            {
                return await ApplyMarketplaceAsync(node, log, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Claude marketplace action failed for {Key}.", key);
                return DevToolActionResult.Failed($"{GetString(node, "name")} could not be changed: {ex.Message}");
            }
        }

        if (enabled is not null)
        {
            await DevToolConfiguration.WriteEnabledOverrideAsync(configPaths, key, enabled.Value, ct).ConfigureAwait(false);
            root = (await DevToolConfiguration.ReadAsync(configPaths, ct).ConfigureAwait(false)).Root;
            node = FindToolNode(root, key) ?? throw new InvalidOperationException("That tool is no longer in the config.");
        }

        try
        {
            return key.StartsWith("plugin:", StringComparison.OrdinalIgnoreCase)
                ? await ApplyPluginAsync(node, DevToolConfiguration.DefaultMarketplaceName(root), log, ct).ConfigureAwait(false)
                : await ApplyMcpServerAsync(node, log, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Tool action failed for {Key}.", key);
            return DevToolActionResult.Failed($"{ToolDisplayName(node)} could not be changed: {ex.Message}");
        }
    }

    private DevToolConfigurationPaths ConfigurationPaths => _configPath is null
        ? DevToolConfigurationPaths.CreateDefault(storageRootDirectory: _store?.RootDirectory)
        : DevToolConfigurationPaths.FromCatalogPath(_configPath);

    /// <summary>
    /// One row's Update, applied to every host the entry targets.
    ///
    /// <para>Copilot first and Claude second, matching the order the PowerShell
    /// script this mirrors uses, so an operator watching the command log sees the
    /// same sequence in both places. Both outcomes are reported: a plugin that
    /// updated in Copilot and failed in Claude has to say both, because "failed" on
    /// its own would send somebody looking for a Copilot problem that is not
    /// there.</para>
    ///
    /// <para>A host the entry does not target contributes nothing at all — not a
    /// skipped line, not a caveat. The catalog said which hosts it is for, and
    /// narrating the ones it is not would bury the ones it is.</para>
    /// </summary>
    private async Task<DevToolActionResult> ApplyPluginAsync(JsonNode plugin, string? defaultMarketplace, CommandLog log, CancellationToken ct)
    {
        var name = GetRequiredString(plugin, "name");
        var hosts = DevToolConfiguration.ParseHosts(plugin);
        var outcomes = new List<string>();
        var failed = false;

        if (hosts.HasFlag(DevToolHosts.Copilot))
        {
            var copilot = await ApplyCopilotPluginAsync(plugin, name, log, ct).ConfigureAwait(false);
            outcomes.Add($"Copilot: {copilot.Message}");
            failed |= !copilot.Succeeded;
        }

        if (hosts.HasFlag(DevToolHosts.Claude))
        {
            var claude = await ApplyClaudePluginAsync(plugin, name, defaultMarketplace, log, ct).ConfigureAwait(false);
            outcomes.Add($"Claude: {claude.Message}");
            failed |= !claude.Succeeded;
        }

        if (outcomes.Count == 0)
        {
            return DevToolActionResult.Failed($"{name} targets no host, so there was nothing to do.");
        }

        var message = $"{name} — {string.Join(" ", outcomes)}";

        return failed ? DevToolActionResult.Failed(message) : DevToolActionResult.Ok(message);
    }

    private async Task<DevToolActionResult> ApplyCopilotPluginAsync(JsonNode plugin, string name, CommandLog log, CancellationToken ct)
    {
        var kind = GetString(plugin, "kind");
        var enabled = GetBool(plugin, "enabled");

        if (IsCopilotOnlyKind(kind))
        {
            var status = enabled
                ? await RefreshRepositorySourceAsync(plugin, log, ct).ConfigureAwait(false)
                : "disabled in config";
            return DevToolActionResult.Ok($"{status}.");
        }

        if (await ResolveCopilotCliAsync(log, ct).ConfigureAwait(false) is not { } cli)
        {
            return DevToolActionResult.Failed(CopilotCliMissing);
        }

        var installed = await GetInstalledPluginsAsync(cli, log, ct).ConfigureAwait(false);
        var isInstalled = installed.ContainsKey(name);

        if (!enabled)
        {
            if (!isInstalled)
            {
                return DevToolActionResult.Ok("already disabled.");
            }

            var uninstall = await RunAsync(cli.Command, [.. cli.Prefix, "plugin", "uninstall", name], log, ct).ConfigureAwait(false);
            return uninstall.ExitCode == 0
                ? DevToolActionResult.Ok("disabled.")
                : DevToolActionResult.Failed(CommandFailure(name, uninstall));
        }

        var args = isInstalled ? new[] { "plugin", "update", name } : ["plugin", "install", GetRequiredString(plugin, "source")];
        var result = await RunAsync(cli.Command, [.. cli.Prefix, .. args], log, ct).ConfigureAwait(false);
        return result.ExitCode == 0
            ? DevToolActionResult.Ok(isInstalled ? "updated." : "installed.")
            : DevToolActionResult.Failed(CommandFailure(name, result));
    }

    /// <summary>
    /// The Claude half of one plugin row.
    ///
    /// <para>An update that finds the plugin switched off in Claude enables it
    /// afterwards. Claude keeps its own on/off switch beside the install, and a
    /// plugin the catalog says this machine wants that stays switched off is an
    /// update nobody can see the result of.</para>
    /// </summary>
    private async Task<DevToolActionResult> ApplyClaudePluginAsync(JsonNode plugin, string name, string? defaultMarketplace, CommandLog log, CancellationToken ct)
    {
        var kind = GetString(plugin, "kind");
        if (IsCopilotOnlyKind(kind))
        {
            return DevToolActionResult.Ok($"skipped, '{kind}' is a Copilot-only mechanism.");
        }

        if (await ResolveClaudeCliAsync(log, ct).ConfigureAwait(false) is not { } cli)
        {
            return DevToolActionResult.Failed(ClaudeCliMissing);
        }

        if (ClaudePluginIdFor(plugin, name, defaultMarketplace) is not { } pluginId)
        {
            return DevToolActionResult.Failed(NoMarketplace);
        }

        var installed = await GetInstalledClaudePluginsAsync(cli, log, ct).ConfigureAwait(false);
        var state = installed.GetValueOrDefault(pluginId);
        var enabled = GetBool(plugin, "enabled");

        if (!enabled)
        {
            if (state is null)
            {
                return DevToolActionResult.Ok("already absent.");
            }

            var uninstall = await RunAsync(cli, ["plugin", "uninstall", pluginId, "--scope", "user"], log, ct).ConfigureAwait(false);
            return uninstall.ExitCode == 0
                ? DevToolActionResult.Ok($"{pluginId} uninstalled.")
                : DevToolActionResult.Failed(CommandFailure(pluginId, uninstall));
        }

        if (state is null)
        {
            var install = await RunAsync(cli, ["plugin", "install", pluginId, "--scope", "user"], log, ct).ConfigureAwait(false);
            return install.ExitCode == 0
                ? DevToolActionResult.Ok($"{pluginId} installed.")
                : DevToolActionResult.Failed(CommandFailure(pluginId, install));
        }

        var update = await RunAsync(cli, ["plugin", "update", pluginId], log, ct).ConfigureAwait(false);
        if (update.ExitCode != 0)
        {
            return DevToolActionResult.Failed(CommandFailure(pluginId, update));
        }

        if (state.Enabled)
        {
            return DevToolActionResult.Ok($"{pluginId} updated.");
        }

        var enable = await RunAsync(cli, ["plugin", "enable", pluginId], log, ct).ConfigureAwait(false);
        return enable.ExitCode == 0
            ? DevToolActionResult.Ok($"{pluginId} updated and enabled.")
            : DevToolActionResult.Failed(CommandFailure(pluginId, enable));
    }

    /// <summary>
    /// One marketplace row's Add or Update.
    ///
    /// <para>Add takes the source and update takes the name, which is the CLI's
    /// own asymmetry: adding is "fetch this repository", updating is "refresh the
    /// one you already know by that name". Which of the two applies is decided by
    /// asking Claude what it already has rather than by what the catalog hopes.</para>
    /// </summary>
    private async Task<DevToolActionResult> ApplyMarketplaceAsync(JsonNode marketplace, CommandLog log, CancellationToken ct)
    {
        var name = GetRequiredString(marketplace, "name");

        if (await ResolveClaudeCliAsync(log, ct).ConfigureAwait(false) is not { } cli)
        {
            return DevToolActionResult.Failed(ClaudeCliMissing);
        }

        var known = await GetClaudeMarketplacesAsync(cli, log, ct).ConfigureAwait(false);
        if (known.Contains(name))
        {
            var update = await RunAsync(cli, ["plugin", "marketplace", "update", name], log, ct).ConfigureAwait(false);
            return update.ExitCode == 0
                ? DevToolActionResult.Ok($"The {name} marketplace was refreshed.")
                : DevToolActionResult.Failed(CommandFailure(name, update));
        }

        var source = GetRequiredString(marketplace, "source");
        var add = await RunAsync(cli, ["plugin", "marketplace", "add", source], log, ct).ConfigureAwait(false);
        return add.ExitCode == 0
            ? DevToolActionResult.Ok($"The {name} marketplace was added.")
            : DevToolActionResult.Failed(CommandFailure(name, add));
    }

    /// <summary>
    /// One MCP server row's action: the shared .NET tool, and then Claude's
    /// registration of it.
    ///
    /// <para>Both halves, in that order, because the registration points at the
    /// tool: registering before installing would name an executable that is not
    /// there yet, and disabling in the other order would leave Claude holding a
    /// registration for something that has just been uninstalled.</para>
    /// </summary>
    private async Task<DevToolActionResult> ApplyMcpServerAsync(JsonNode server, CommandLog log, CancellationToken ct)
    {
        var packageId = GetRequiredString(server, "packageId");
        var enabled = GetBool(server, "enabled");
        var installed = await GetInstalledDotNetToolsAsync(log, ct).ConfigureAwait(false);
        var isInstalled = installed.ContainsKey(packageId);

        string[] args;
        string success;
        if (!enabled)
        {
            args = ["tool", "uninstall", "--global", packageId];
            success = "disabled.";
        }
        else if (isInstalled)
        {
            args = ["tool", "update", "--global", packageId];
            success = "updated.";
        }
        else
        {
            args = ["tool", "install", "--global", packageId];
            success = "installed.";
        }

        var outcomes = new List<string>();
        var failed = false;

        if (enabled || isInstalled)
        {
            var result = await RunAsync("dotnet", args, log, ct).ConfigureAwait(false);
            outcomes.Add($".NET tool: {(result.ExitCode == 0 ? success : CommandFailure(packageId, result))}");
            failed |= result.ExitCode != 0;
        }
        else
        {
            outcomes.Add(".NET tool: already absent.");
        }

        if (DevToolConfiguration.ParseHosts(server).HasFlag(DevToolHosts.Claude) && server["claude"] is { } claude)
        {
            var registration = await ApplyClaudeMcpRegistrationAsync(server, claude, enabled, log, ct).ConfigureAwait(false);
            outcomes.Add($"Claude: {registration.Message}");
            failed |= !registration.Succeeded;
        }

        var message = $"{packageId} — {string.Join(" ", outcomes)}";

        return failed ? DevToolActionResult.Failed(message) : DevToolActionResult.Ok(message);
    }

    /// <summary>
    /// The user-scope <c>claude mcp</c> registration for one server entry.
    ///
    /// <para>A registration in another scope is reported and left exactly as it is,
    /// in both directions. Somebody put a project- or local-scope server there
    /// deliberately, and a machine-wide "make this match the catalog" quietly
    /// replacing it would be the kind of change that is only noticed later, in a
    /// project that has stopped working.</para>
    ///
    /// <para>A registration whose command no longer matches is removed and re-added
    /// rather than edited, because the CLI offers no edit — and a stale command is
    /// how a renamed tool leaves Claude pointing at an executable that is gone.</para>
    /// </summary>
    private async Task<DevToolActionResult> ApplyClaudeMcpRegistrationAsync(JsonNode server, JsonNode claude, bool enabled, CommandLog log, CancellationToken ct)
    {
        var name = ClaudeServerName(server, claude);
        if (string.IsNullOrWhiteSpace(name))
        {
            return DevToolActionResult.Failed("the entry has no Claude server name.");
        }

        if (await ResolveClaudeCliAsync(log, ct).ConfigureAwait(false) is not { } cli)
        {
            return DevToolActionResult.Failed(ClaudeCliMissing);
        }

        var details = await GetClaudeMcpServerAsync(cli, name, log, ct).ConfigureAwait(false);

        if (details is not null && !details.IsUserScope)
        {
            return DevToolActionResult.Ok($"left alone, '{name}' is registered at {details.Scope} scope.");
        }

        if (!enabled)
        {
            if (details is null)
            {
                return DevToolActionResult.Ok($"'{name}' was already unregistered.");
            }

            var remove = await RunAsync(cli, ["mcp", "remove", name, "--scope", "user"], log, ct).ConfigureAwait(false);
            return remove.ExitCode == 0
                ? DevToolActionResult.Ok($"'{name}' was unregistered.")
                : DevToolActionResult.Failed(CommandFailure(name, remove));
        }

        var command = GetString(claude, "command");
        if (string.IsNullOrWhiteSpace(command))
        {
            return DevToolActionResult.Failed($"'{name}' has no claude.command to register.");
        }

        if (details is not null && string.Equals(details.Command, command, StringComparison.Ordinal))
        {
            return DevToolActionResult.Ok($"'{name}' was already registered.");
        }

        if (details is not null)
        {
            var remove = await RunAsync(cli, ["mcp", "remove", name, "--scope", "user"], log, ct).ConfigureAwait(false);
            if (remove.ExitCode != 0)
            {
                return DevToolActionResult.Failed(CommandFailure(name, remove));
            }
        }

        var add = await RunAsync(cli, ["mcp", "add", "--scope", "user", name, "--", command, .. ClaudeServerArgs(claude)], log, ct).ConfigureAwait(false);
        return add.ExitCode == 0
            ? DevToolActionResult.Ok(details is null ? $"'{name}' was registered." : $"'{name}' was re-registered.")
            : DevToolActionResult.Failed(CommandFailure(name, add));
    }

    private static async Task<Dictionary<string, string>> GetInstalledPluginsAsync((string Command, string[] Prefix) cli, CommandLog log, CancellationToken ct)
    {
        var result = await RunAsync(cli.Command, [.. cli.Prefix, "plugin", "list"], log, ct).ConfigureAwait(false);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(CommandFailure("Copilot plugin list", result));
        }

        return new Dictionary<string, string>(DevToolOutput.ParsePluginList(result.Output), StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>What Claude has installed at any scope, or nothing when the listing
    /// failed. A failure is not thrown here the way the Copilot listing throws:
    /// every Claude row can still say something useful without it, and the command
    /// log already carries whatever the CLI printed.</summary>
    private static async Task<IReadOnlyDictionary<string, DevToolOutput.ClaudePluginState>> GetInstalledClaudePluginsAsync(
        string cli,
        CommandLog log,
        CancellationToken ct)
    {
        var result = await RunAsync(cli, ["plugin", "list", "--json"], log, ct).ConfigureAwait(false);

        return result.ExitCode == 0
            ? DevToolOutput.ParseClaudePluginList(result.Output)
            : new Dictionary<string, DevToolOutput.ClaudePluginState>(StringComparer.OrdinalIgnoreCase);
    }

    /// <inheritdoc cref="GetInstalledClaudePluginsAsync" />
    private static async Task<IReadOnlySet<string>> GetClaudeMarketplacesAsync(string cli, CommandLog log, CancellationToken ct)
    {
        var result = await RunAsync(cli, ["plugin", "marketplace", "list", "--json"], log, ct).ConfigureAwait(false);

        return result.ExitCode == 0
            ? DevToolOutput.ParseClaudeMarketplaceList(result.Output)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// What Claude knows about one MCP registration.
    ///
    /// <para>A non-zero exit is not read as a failure here: the CLI answers an
    /// unknown name with "No MCP server named ..." and a non-zero code, and that is
    /// the ordinary "needs registering" case rather than something going wrong. The
    /// parser decides which of the two it is from the text, so both streams are
    /// handed to it.</para>
    /// </summary>
    private static async Task<DevToolOutput.ClaudeMcpServerDetails?> GetClaudeMcpServerAsync(string cli, string name, CommandLog log, CancellationToken ct)
    {
        var result = await RunAsync(cli, ["mcp", "get", name], log, ct).ConfigureAwait(false);

        return DevToolOutput.ParseClaudeMcpServer(string.IsNullOrWhiteSpace(result.Output) ? result.Error : result.Output);
    }

    private static async Task<Dictionary<string, string>> GetInstalledDotNetToolsAsync(CommandLog log, CancellationToken ct)
    {
        var result = await RunAsync("dotnet", ["tool", "list", "--global"], log, ct).ConfigureAwait(false);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(CommandFailure("dotnet tool list", result));
        }

        return new Dictionary<string, string>(DevToolOutput.ParseDotNetToolList(result.Output), StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>What nuget.org publishes for one MCP server package.
    ///
    /// <para>Searched without <c>--exact-match</c>, because the installed SDK has
    /// no such flag: every lookup exited non-zero on an unrecognised argument and
    /// every available version came back unknown. The exactness the flag was
    /// there for now happens in the parser, over the whole result.</para></summary>
    private static async Task<string> GetDotNetToolAvailableVersionAsync(string packageId, CommandLog log, CancellationToken ct)
    {
        var result = await RunAsync("dotnet", ["tool", "search", packageId], log, ct).ConfigureAwait(false);

        return result.ExitCode == 0
            ? DevToolOutput.ParseDotNetToolSearchVersion(result.Output, packageId)
            : DevToolOutput.Unknown;
    }

    /// <summary>What the plugin's source publishes today.
    ///
    /// <para>A repository-backed tool is asked what its remote's HEAD is, not what
    /// its clone's is: the clone is the installed side, and asking git the same
    /// question twice made every such tool equal to itself and hid real pending
    /// updates.</para>
    ///
    /// <para>Everything else reads the plugin's manifest through the GitHub API.
    /// The repository these plugins ship from cuts no releases, so the release
    /// lookup this replaces answered "release not found" for all of them.</para></summary>
    private static async Task<string> GetPluginAvailableVersionAsync(JsonNode plugin, CommandLog log, CancellationToken ct)
    {
        // A repository-backed tool is versioned by commit and never falls back to
        // the manifest: the two are not the same unit, and a sha compared against
        // a semver would report an update on every check forever.
        if (IsRepositoryBacked(plugin))
        {
            if (ExistingRepositoryPath(plugin) is not { } repoPath)
            {
                return DevToolOutput.Unknown;
            }

            var head = await RunAsync("git", ["-C", repoPath, "ls-remote", "origin", "HEAD"], log, ct).ConfigureAwait(false);

            return head.ExitCode == 0 && FirstField(head.Output) is { Length: > 0 } sha
                ? DevToolOutput.ShortCommit(sha)
                : DevToolOutput.Unknown;
        }

        if (DevToolOutput.ParsePluginSource(GetString(plugin, "source")) is not { } source)
        {
            return DevToolOutput.Unknown;
        }

        var manifest = await RunAsync(
            "gh",
            ["api", $"repos/{source.Owner}/{source.Repository}/contents/{source.ManifestPath}", "-H", "Accept: application/vnd.github.raw"],
            log,
            ct).ConfigureAwait(false);

        return manifest.ExitCode == 0
            ? DevToolOutput.ParsePluginManifestVersion(manifest.Output) ?? DevToolOutput.Unknown
            : DevToolOutput.Unknown;
    }

    private static async Task<string> GetPluginInstalledVersionAsync(JsonNode plugin, IReadOnlyDictionary<string, string> installedPlugins, CommandLog log, CancellationToken ct)
    {
        var name = GetRequiredString(plugin, "name");
        if (installedPlugins.TryGetValue(name, out var version))
        {
            return version;
        }

        if (!IsRepositoryBacked(plugin) || ExistingRepositoryPath(plugin) is not { } repoPath)
        {
            return DevToolOutput.NotInstalled;
        }

        // The full sha, cut to the same width as the remote one it will be
        // compared against. Asking git for the short form here and taking the
        // remote's full one would make the two differ on length alone.
        var result = await RunAsync("git", ["-C", repoPath, "rev-parse", "HEAD"], log, ct).ConfigureAwait(false);

        return result.ExitCode == 0 && FirstField(result.Output) is { Length: > 0 } sha
            ? DevToolOutput.ShortCommit(sha)
            : "source";
    }

    private static bool IsRepositoryBacked(JsonNode plugin)
    {
        var kind = GetString(plugin, "kind");

        return kind.Equals("repository-skills", StringComparison.OrdinalIgnoreCase)
            || kind.Equals("repository-canvases", StringComparison.OrdinalIgnoreCase);
    }

    private static string? ExistingRepositoryPath(JsonNode plugin)
    {
        var repoPath = GetString(plugin, "repoPath");
        if (string.IsNullOrWhiteSpace(repoPath))
        {
            return null;
        }

        var expanded = ResolveConfiguredPath(repoPath);

        return Directory.Exists(expanded) ? expanded : null;
    }

    /// <summary>The first whitespace-separated field of a command's output.
    /// <c>ls-remote</c> answers "&lt;sha&gt;\tHEAD" and <c>rev-parse</c> answers a
    /// bare sha, so one reader covers both.</summary>
    private static string FirstField(string output) =>
        output.Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries) is [var first, ..] ? first : string.Empty;

    private static async Task<string> RefreshRepositorySourceAsync(JsonNode plugin, CommandLog log, CancellationToken ct)
    {
        var source = GetRequiredString(plugin, "source");
        var repoPath = ResolveConfiguredPath(GetRequiredString(plugin, "repoPath"));
        if (Directory.Exists(repoPath))
        {
            var pull = await RunAsync("git", ["-C", repoPath, "pull", "--ff-only"], log, ct).ConfigureAwait(false);
            return pull.ExitCode == 0 ? "source refreshed" : CommandFailure(ToolDisplayName(plugin), pull);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(repoPath) ?? Environment.CurrentDirectory);
        var clone = await RunAsync("git", ["clone", source, repoPath], log, ct).ConfigureAwait(false);
        return clone.ExitCode == 0 ? "source cloned" : CommandFailure(ToolDisplayName(plugin), clone);
    }

    private static IEnumerable<JsonNode> GetArray(JsonNode root, string name) =>
        root[name]?.AsArray().Where(node => node is not null).Cast<JsonNode>() ?? [];

    private static JsonNode? FindToolNode(JsonNode root, string key)
    {
        if (key.StartsWith("plugin:", StringComparison.OrdinalIgnoreCase))
        {
            var name = key["plugin:".Length..];
            return GetArray(root, "plugins").FirstOrDefault(node => GetString(node, "name").Equals(name, StringComparison.OrdinalIgnoreCase));
        }

        if (key.StartsWith("mcp:", StringComparison.OrdinalIgnoreCase))
        {
            var packageId = key["mcp:".Length..];
            return GetArray(root, "mcpServers").FirstOrDefault(node => GetString(node, "packageId").Equals(packageId, StringComparison.OrdinalIgnoreCase));
        }

        if (key.StartsWith("marketplace:", StringComparison.OrdinalIgnoreCase))
        {
            var name = key["marketplace:".Length..];
            return DevToolConfiguration.MarketplaceEntries(root)
                .FirstOrDefault(node => GetString(node, "name").Equals(name, StringComparison.OrdinalIgnoreCase));
        }

        return null;
    }

    private static string GetString(JsonNode node, string name) => node[name]?.GetValue<string>() ?? string.Empty;

    private static string GetRequiredString(JsonNode node, string name) =>
        string.IsNullOrWhiteSpace(GetString(node, name))
            ? throw new InvalidOperationException($"The tool config entry is missing '{name}'.")
            : GetString(node, name);

    private static bool GetBool(JsonNode node, string name) => node[name]?.GetValue<bool>() ?? false;

    private static string PluginKey(string name) => DevToolConfiguration.KeyFor(DevToolKind.Plugin, name);

    private static string McpServerKey(string packageId) => DevToolConfiguration.KeyFor(DevToolKind.McpServer, packageId);

    private static string MarketplaceKey(string name) => DevToolConfiguration.KeyFor(DevToolKind.Marketplace, name);

    private static string ToolDisplayName(JsonNode node) => GetString(node, "name") is { Length: > 0 } name ? name : GetString(node, "packageId");

    /// <summary>The two plugin kinds that are Copilot mechanisms and nothing else:
    /// one copies flat skill files into <c>~/.copilot/skills</c>, the other copies
    /// canvas extensions into <c>~/.copilot/extensions</c>. Neither has a Claude
    /// counterpart, so the Claude half of such an entry is skipped rather than
    /// attempted and failed.</summary>
    private static bool IsCopilotOnlyKind(string kind) =>
        kind.Equals("repository-skills", StringComparison.OrdinalIgnoreCase)
        || kind.Equals("repository-canvases", StringComparison.OrdinalIgnoreCase);

    private static string? ClaudePluginIdFor(JsonNode plugin, string name, string? defaultMarketplace) =>
        DevToolOutput.ClaudePluginId(
            name,
            GetString(plugin, "claudeName"),
            GetString(plugin, "claudeMarketplace"),
            defaultMarketplace);

    /// <summary>What Claude registers the MCP server under. The <c>claude</c>
    /// section's own name when it has one, and the entry's otherwise — the two
    /// differ often enough (Copilot's <c>jsdotnet-project-guidelines</c> is
    /// Claude's <c>jsdotnet-coding-guidelines</c>) that the fallback is the
    /// exception rather than the rule.</summary>
    private static string ClaudeServerName(JsonNode server, JsonNode claude) =>
        GetString(claude, "name") is { Length: > 0 } name ? name : GetString(server, "name");

    private static string[] ClaudeServerArgs(JsonNode claude) =>
        claude["args"] is JsonArray args
            ? [.. args.Select(node => node?.GetValue<string>()).Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value!)]
            : [];

    private static string DescribeStatus(bool enabled, bool installed, bool updateAvailable, string kind)
    {
        if (!enabled)
        {
            return "Disabled in config";
        }

        if (!installed)
        {
            return "Not installed";
        }

        return updateAvailable ? "Update available" : $"Enabled {kind}";
    }

    /// <summary>
    /// One row's status line, from what every host it could inspect answered plus
    /// whatever it could not inspect at all.
    ///
    /// <para>The notes are the half that matters most and the half a boolean cannot
    /// hold: "the Claude CLI is not on this machine" and "no marketplace is
    /// configured" are both reasons a row is quieter than it looks, and a row that
    /// simply read "Enabled plugin" while silently doing half its job is what this
    /// exists to prevent.</para>
    /// </summary>
    private static string AggregateStatus(bool enabled, IReadOnlyList<DevToolHostState> states, IReadOnlyList<string> notes, string kind)
    {
        var headline = states.Count == 0
            ? enabled ? "Nothing could be checked" : "Disabled in config"
            : DescribeStatus(
                enabled,
                states.All(state => state.Installed),
                states.Any(state => DevToolInfo.VersionDiffers(state.InstalledVersion, state.AvailableVersion)),
                kind);

        return notes.Count == 0 ? headline : $"{headline} · {string.Join(" · ", notes)}";
    }

    /// <summary>
    /// Several hosts' versions as one column.
    ///
    /// <para>The hosts agree most of the time, and when they do the column reads
    /// exactly as it did when there was only one of them. When they disagree, that
    /// disagreement <em>is</em> the answer — a plugin at 1.2.0 in Copilot and
    /// absent from Claude is not summarised by either number alone — so both are
    /// named rather than one being picked.</para>
    /// </summary>
    private static string Summarize(IReadOnlyList<DevToolHostState> states, Func<DevToolHostState, string> select)
    {
        if (states.Count == 0)
        {
            return DevToolOutput.Unknown;
        }

        var values = states.Select(select).ToArray();

        return values.Distinct(StringComparer.OrdinalIgnoreCase).Count() == 1
            ? values[0]
            : string.Join(" · ", states.Select(state => $"{HostLabel(state.Host)} {select(state)}"));
    }

    private static string HostLabel(DevToolHosts hosts) => hosts switch
    {
        DevToolHosts.Copilot => "copilot",
        DevToolHosts.Claude => "claude",
        DevToolHosts.Both => "shared",
        _ => "none"
    };

    /// <summary>
    /// Which Copilot CLI this machine has, or nothing when it has neither.
    ///
    /// <para>Null rather than an optimistic fall back to <c>gh</c>: a machine with
    /// no Copilot at all would otherwise run every Copilot command against a
    /// <c>gh</c> that is not there either, and each row would report the resulting
    /// "file not found" as though it were that plugin's own problem.</para>
    /// </summary>
    private static async Task<(string Command, string[] Prefix)?> ResolveCopilotCliAsync(CommandLog log, CancellationToken ct)
    {
        var copilot = await RunAsync("copilot", ["--version"], log, ct).ConfigureAwait(false);
        if (copilot.ExitCode == 0)
        {
            return ("copilot", []);
        }

        var gh = await RunAsync("gh", ["--version"], log, ct).ConfigureAwait(false);

        return gh.ExitCode == 0 ? ("gh", ["copilot"]) : null;
    }

    /// <summary>
    /// Which Claude CLI this machine has, or nothing when it has none.
    ///
    /// <para>PATH first, in the order <c>claude.cmd</c>, <c>claude.exe</c>,
    /// <c>claude</c> — the shim before the executable before the bare name, because
    /// that is the order a shell would resolve them in and running a different one
    /// than the operator does would be a difference nobody could see.</para>
    ///
    /// <para>Then the copy the Claude desktop app ships, which it installs under
    /// <c>%APPDATA%\Claude\claude-code\&lt;version&gt;</c> and never puts on PATH.
    /// The newest version folder wins, and a folder whose name is not a version
    /// sorts below every one that is rather than being skipped: it is still a
    /// candidate, just the last one.</para>
    /// </summary>
    private static async Task<string?> ResolveClaudeCliAsync(CommandLog log, CancellationToken ct)
    {
        foreach (var candidate in new[] { "claude.cmd", "claude.exe", "claude" })
        {
            var probe = await RunAsync(candidate, ["--version"], log, ct).ConfigureAwait(false);
            if (probe.ExitCode == 0)
            {
                return candidate;
            }
        }

        var bundledRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Claude", "claude-code");
        if (!Directory.Exists(bundledRoot))
        {
            return null;
        }

        return new DirectoryInfo(bundledRoot)
            .GetDirectories()
            .OrderByDescending(directory => Version.TryParse(directory.Name, out var version) ? version : new Version(0, 0, 0))
            .Select(directory => Path.Combine(directory.FullName, "claude.exe"))
            .FirstOrDefault(File.Exists);
    }


    private static async Task<CommandResult> RunAsync(string fileName, IReadOnlyList<string> arguments, CommandLog log, CancellationToken ct)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo(fileName)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            // Redirecting the streams is not what keeps the console away — only
            // this flag is. The desktop head is a GUI-subsystem process with no
            // console of its own, so Windows hands every child it starts a brand
            // new one, and opening the tools tab meant a dozen of them blinking
            // across the screen while the pane quietly read their output.
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        try
        {
            process.Start();
        }
        catch (Win32Exception ex)
        {
            // A CLI that is not on this machine is an ordinary answer, not a
            // crash. Probing for one is how both CLIs are discovered, and letting
            // Windows' "file not found" escape would take the whole listing down
            // over a host the operator may simply not use. It is recorded like any
            // other command so the log still shows what was looked for.
            var missing = new CommandResult(CommandNotFound, string.Empty, ex.Message);
            log.Record(fileName, arguments, missing);

            return missing;
        }

        var outputTask = process.StandardOutput.ReadToEndAsync(ct);
        var errorTask = process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct).ConfigureAwait(false);
        var result = new CommandResult(process.ExitCode, await outputTask.ConfigureAwait(false), await errorTask.ConfigureAwait(false));

        log.Record(fileName, arguments, result);

        return result;
    }

    private static string CommandFailure(string name, CommandResult result)
    {
        var details = string.IsNullOrWhiteSpace(result.Error) ? result.Output : result.Error;
        return string.IsNullOrWhiteSpace(details) ? $"{name} failed with exit code {result.ExitCode}." : details.Trim();
    }

    private static string ResolveConfiguredPath(string path) => Environment.ExpandEnvironmentVariables(path);

    private sealed record CommandResult(int ExitCode, string Output, string Error);

    /// <summary>
    /// What one call to <see cref="ListAsync" /> or one action ran, collected so
    /// the pane can show it.
    ///
    /// <para>Passed from method to method by hand rather than parked in a field
    /// or an <c>AsyncLocal</c>. Nearly everything in this class is static and
    /// several of the callers run concurrently with each other, so an ambient
    /// collector would be a shared mutable that no call site mentions — the
    /// parameter is the point: a method that runs a process says so in its
    /// signature.</para>
    /// </summary>
    private sealed class CommandLog
    {
        /// <summary>How much of one command's output survives. A <c>git clone</c>
        /// prints progress by the screenful and this text goes straight into a
        /// pane, so the tail is dropped rather than handed to the renderer.</summary>
        private const int OutputLimit = 4000;

        private readonly List<DevToolCommand> _commands = [];

        public IReadOnlyList<DevToolCommand> Commands => _commands;

        public void Record(string fileName, IReadOnlyList<string> arguments, CommandResult result) =>
            _commands.Add(new DevToolCommand(Describe(fileName, arguments), result.ExitCode, Captured(result)));

        /// <summary>The command as something a reader could paste into a shell,
        /// which is the form they will want it in when they go to reproduce
        /// whatever just failed.</summary>
        private static string Describe(string fileName, IReadOnlyList<string> arguments) =>
            string.Join(' ', new[] { Quoted(fileName) }.Concat(arguments.Select(Quoted)));

        private static string Quoted(string value) =>
            value.Any(char.IsWhiteSpace) ? $"\"{value}\"" : value;

        private static string Captured(CommandResult result)
        {
            // Standard error after standard output rather than interleaved: the
            // two streams were read separately and their real ordering was lost
            // at that point, so pretending to restore it would be a fiction.
            var combined = string.Join(
                Environment.NewLine,
                new[] { result.Output, result.Error }
                    .Select(stream => stream.TrimEnd())
                    .Where(stream => stream.Length > 0));

            return combined.Length <= OutputLimit
                ? combined
                : combined[..OutputLimit] + $"{Environment.NewLine}... output truncated at {OutputLimit} characters.";
        }
    }
}
