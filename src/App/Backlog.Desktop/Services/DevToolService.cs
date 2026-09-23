using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Backlog.Desktop.UI.Tasks;
using Backlog.Desktop.UI.Devbook;
using Backlog.Modules.DevPc.Abstractions;
using Backlog.Modules.Tasks.Abstractions.Services;
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

    /// <inheritdoc cref="CommandNotFound" />
    /// <summary>The exit code stood in for a command that was still running when
    /// its time ran out. Distinct from <see cref="CommandNotFound"/> for the same
    /// reason that one is distinct from a real failure: "winget never answered" and
    /// "winget answered no" are different problems and the log is where somebody
    /// tells them apart.</summary>
    private const int CommandTimedOut = -2;

    /// <inheritdoc cref="ClaudeCliMissing" />
    private const string WingetCliMissing = "The winget CLI was not found on this machine.";

    /// <inheritdoc cref="ClaudeCliMissing" />
    private const string VsCodeCliMissing = "The VS Code CLI ('code') was not found on this machine.";

    /// <inheritdoc cref="ClaudeCliMissing" />
    private const string ClaudeDesktopMissing = "The Claude desktop app was not found on this machine.";

    /// <summary>
    /// How long a listing waits for one command before giving up on it.
    ///
    /// <para>There was no timeout at all, and the pane calls the listing with no
    /// token — survivable while everything it ran was <c>claude plugin list</c>,
    /// which answers instantly or not at all. <c>winget list</c> against a cold
    /// source is minutes of nothing, and a source that is unreachable is forever:
    /// one of those hung the tools tab with no way back.</para>
    ///
    /// <para>Two minutes rather than a few seconds because the slow case here is
    /// legitimate — winget really does take that long the first time after a
    /// reboot — and a probe killed early reports "not installed" about a machine
    /// that has it.</para>
    /// </summary>
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromMinutes(2);

    /// <summary>How long an install gets. A Docker Desktop or a Visual Studio
    /// workload is genuinely tens of minutes of downloading, and the probe budget
    /// would kill both of them part-way through — which is worse than waiting,
    /// because a half-installed package is a machine somebody has to repair by
    /// hand.</summary>
    private static readonly TimeSpan InstallTimeout = TimeSpan.FromMinutes(45);

    /// <summary>How many per-package <c>winget show</c> calls one refresh may
    /// spend.
    ///
    /// <para>The two batched listings answer for every package that is installed.
    /// What is left is the packages this machine does not have, and each of those
    /// costs a process launch and a source round-trip to learn a version the row
    /// does not act on — so a catalog listing twenty missing apps would spend a
    /// minute of the refresh on the Available column alone. What is skipped is
    /// named on the status line rather than silently truncated.</para></summary>
    private const int WingetShowBudget = 6;

    /// <summary>
    /// The one HTTP client in this class, for the one thing no CLI can answer:
    /// what a VS Code extension's latest published version is.
    ///
    /// <para>Static because the alternative in a class of static methods is a
    /// client per refresh, and a socket per refresh after that. Its own timeout is
    /// short: this is one column of a listing, and the listing is honest about not
    /// knowing.</para></summary>
    private static readonly HttpClient Marketplace = new() { Timeout = TimeSpan.FromSeconds(20) };

    private static readonly IReadOnlyDictionary<string, string> NoVersions =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    private readonly ITaskStore? _store;
    private readonly string? _configPath;
    private readonly ILogger<DevToolService>? _logger;

    /// <summary>
    /// This machine's own MCP server, or nothing when this host cannot resolve
    /// one.
    ///
    /// <para>Nullable on purpose rather than by omission. A row that declares a
    /// <c>${BACKLOG_MCP_PORT}</c> is still a row: it lists, it shows the URL
    /// exactly as the catalog spells it, and what it cannot do is register —
    /// which it says. Refusing to list it instead would hide a catalog entry
    /// from the one screen that shows the catalog.</para>
    /// </summary>
    private readonly IMcpEndpointSource? _mcp;

    public DevToolService(ILogger<DevToolService>? logger = null, string? configPath = null)
        : this(null, null, logger, configPath)
    {
    }

    public DevToolService(ITaskStore store, ILogger<DevToolService>? logger = null)
        : this(store, null, logger, null)
    {
    }

    /// <summary>
    /// The composition the desktop head uses: a store, and the endpoint a row
    /// reached over HTTP is written from.
    /// </summary>
    /// <param name="mcp">This machine's MCP server, or <see langword="null"/> for
    /// a host that has none — see <see cref="_mcp"/>.</param>
    /// <remarks><paramref name="mcp"/> is required rather than optional, unlike
    /// the logger beside it. Defaulted, a one-argument <c>new DevToolService(store)</c>
    /// would be applicable to this overload and to the one above it with nothing
    /// to choose between them, which is a CS0121 waiting for whoever writes that
    /// line next. Required, every arity resolves to exactly one of the
    /// three.</remarks>
    public DevToolService(ITaskStore store, IMcpEndpointSource? mcp, ILogger<DevToolService>? logger = null)
        : this(store, mcp, logger, null)
    {
    }

    private DevToolService(ITaskStore? store, IMcpEndpointSource? mcp, ILogger<DevToolService>? logger, string? configPath)
    {
        _store = store;
        _mcp = mcp;
        _logger = logger;
        _configPath = configPath;
    }

    /// <summary>
    /// Forwarded from <see cref="IMcpEndpointSource.Changed"/>, and raised by
    /// nothing else.
    ///
    /// <para>Everything else a listing reads is a file this pane's own buttons
    /// write, and the pane already re-lists after each of them. The MCP endpoint
    /// is the one input that moves without anybody pressing anything here: the
    /// port is edited on the Settings screen and the listener binds, fails and
    /// stops on its own.</para>
    ///
    /// <para>Subscribed through, not stored and replayed: with no source there is
    /// nothing to subscribe to and the accessors are the empty pair that
    /// <c>UnsupportedDevToolService</c> uses, so a subscriber holds nothing.</para>
    /// </summary>
    public event Action? Changed
    {
        add
        {
            if (_mcp is not null) _mcp.Changed += value;
        }
        remove
        {
            if (_mcp is not null) _mcp.Changed -= value;
        }
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

        // Only when some server in the catalog is actually a .NET tool. This is
        // the inventory every packageId is looked up in, and a catalog whose MCP
        // servers are all registered by command has nothing to look up in it — so
        // running it anyway would be a launch on behalf of rows it cannot say
        // anything about.
        if (DevToolConfiguration.ReadMcpServers(root).Any(server => server.Mechanism is DevToolMcpMechanism.DotNetTool))
        {
            try
            {
                installedTools = await GetInstalledDotNetToolsAsync(log, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to list .NET tools.");
                messages.Add(".NET tools could not be checked.");
            }
        }

        var claudeCli = await ResolveClaudeCliAsync(log, ct).ConfigureAwait(false);
        var refreshed = new RefreshState();
        var claudeMarketplaces = (IReadOnlySet<string>)new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var claudePlugins = (IReadOnlyDictionary<string, DevToolOutput.ClaudePluginState>)
            new Dictionary<string, DevToolOutput.ClaudePluginState>(StringComparer.OrdinalIgnoreCase);
        var claudeInstallPaths = (IReadOnlyDictionary<string, int>)new Dictionary<string, int>();
        var claudeInstallsKnown = false;

        if (claudeCli is null)
        {
            messages.Add(ClaudeCliMissing);
        }
        else
        {
            claudeMarketplaces = await GetClaudeMarketplacesAsync(claudeCli, log, ct).ConfigureAwait(false);

            // Every configured marketplace pulled before Claude is asked what it
            // has. Claude answers that question out of its own clone of a
            // marketplace and installs out of that same clone, so a listing that
            // read first was a listing about whichever refresh somebody last
            // happened to run - and the update it then offered was one this pane
            // could not deliver, however many times it was pressed.
            foreach (var name in DevToolConfiguration.MarketplaceEntries(root)
                .Select(marketplace => GetString(marketplace, "name"))
                .Where(name => !string.IsNullOrWhiteSpace(name) && claudeMarketplaces.Contains(name))
                .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var pull = await RunAsync(DevToolCommands.ClaudeMarketplaceUpdate(claudeCli, name), log, ct).ConfigureAwait(false);
                if (pull.ExitCode == 0)
                {
                    refreshed.Marketplaces.Add(name);
                }
                else
                {
                    messages.Add($"The {name} marketplace could not be refreshed, so its plugins read as version unknown.");
                }
            }

            (claudePlugins, claudeInstallPaths, claudeInstallsKnown) = await GetInstalledClaudePluginsAsync(claudeCli, log, ct).ConfigureAwait(false);
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
                claudeInstallPaths,
                claudeInstallsKnown,
                defaultMarketplace,
                refreshed,
                log,
                ct).ConfigureAwait(false));
        }

        // Resolved once, before the loop, and only when some entry asks for it.
        // The Claude desktop app is a file rather than a CLI: the answer is one
        // config document that every row targeting it reads, and re-reading it per
        // row would be one more chance for two rows to disagree about the same
        // file.
        var claudeDesktop = GetArray(root, "mcpServers").Any(server => DevToolConfiguration.ParseHosts(server).HasFlag(DevToolHosts.ClaudeDesktop))
            ? await ResolveClaudeDesktopAsync(log, ct).ConfigureAwait(false)
            : null;

        if (claudeDesktop?.Error is { Length: > 0 } desktopError)
        {
            messages.Add(desktopError);
        }

        foreach (var server in GetArray(root, "mcpServers"))
        {
            // Read through the abstraction rather than by reaching for a
            // packageId. That reach is what dropped every command-registered
            // server before a row was ever built — this repository's own catalog
            // ships one — and the reader is where the two decisions a row needs
            // live: which property identifies the entry, and which mechanism is
            // behind it. It is the same reader the harness enumerates with, so the
            // two implementations of this port cannot disagree about one catalog.
            //
            // An entry that cannot be addressed at all is still skipped: there is
            // nothing to write an override against and nothing for a button to
            // remove, so a row for it would be a row nothing can act on.
            if (DevToolConfiguration.ReadMcpServer(server) is not { } declared)
            {
                continue;
            }

            tools.Add(await DescribeMcpServerAsync(server, declared, installedTools, claudeCli, claudeDesktop, _mcp, log, ct).ConfigureAwait(false));
        }

        // Applications last, and read as one batch. Every other kind above asks
        // one CLI a question per entry; this one asks two CLIs and an HTTP
        // endpoint for the whole array at once, because a machine's software
        // inventory is thirty-odd rows and a launch per row is a refresh nobody
        // waits out.
        var applications = DevToolConfiguration.ReadApplications(root);
        if (applications.Count > 0)
        {
            var inventory = await ReadApplicationInventoryAsync(applications, log, messages, ct).ConfigureAwait(false);

            foreach (var application in applications)
            {
                tools.Add(DescribeApplication(application, inventory));
            }
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
        IReadOnlyDictionary<string, int> claudeInstallPaths,
        bool claudeInstallsKnown,
        string? defaultMarketplace,
        RefreshState refreshed,
        CommandLog log,
        CancellationToken ct)
    {
        var kind = GetString(plugin, "kind");
        var enabled = GetBool(plugin, "enabled");
        var hosts = DevToolConfiguration.ParseHosts(plugin);
        var repositoryBacked = IsRepositoryBacked(plugin);
        var states = new List<DevToolHostState>();
        var notes = new List<string>();
        IReadOnlyList<DevToolCachedVersion> cachedVersions = [];

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
            else if (repositoryBacked)
            {
                // Its own branch rather than the CLI listing's, because it is its
                // own authority: both of this row's versions are commits in a
                // mirror this host manages. Read through the listing first - as it
                // was - the installed side could come from Copilot's plugin list
                // and the available side from a sha, which is two units and a
                // permanent update.
                var repository = await DescribeRepositoryStateAsync(plugin, enabled, kind, refreshed, log, ct).ConfigureAwait(false);
                states.Add(repository.State);
                notes.AddRange(repository.Notes);
            }
            else
            {
                var installedVersion = GetPluginInstalledVersion(plugin, installedPlugins);
                var installed = !installedVersion.Equals(DevToolOutput.NotInstalled, StringComparison.OrdinalIgnoreCase);
                var available = await AvailableAsync().ConfigureAwait(false);

                states.Add(new DevToolHostState(
                    DevToolHosts.Copilot,
                    installed,
                    installedVersion,
                    available,
                    DescribeStatus(enabled, installed, DevToolInfo.VersionDiffers(installedVersion, available), string.IsNullOrWhiteSpace(kind) ? "plugin" : kind))
                {
                    // Both sides are the source repository at its default branch,
                    // which is what "copilot plugin install" and "plugin update"
                    // pull - so the two columns are one authority and comparing
                    // them means something.
                    InstalledAuthority = DevToolVersionAuthority.CopilotSource,
                    AvailableAuthority = DevToolVersionAuthority.CopilotSource
                });
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

                // Only a marketplace this refresh actually pulled may answer the
                // Available column. Claude's installed version and Claude's update
                // both come out of that clone, so a published version read while
                // the clone was behind is a number about a ref Claude cannot
                // install from - which is the reported defect: claude-desktop at
                // 0.7.0 against 0.8.0, permanently, with an Update that changed
                // nothing.
                var marketplace = ClaudeMarketplaceOf(pluginId);
                var pulled = refreshed.Marketplaces.Contains(marketplace);
                var available = DevToolRefresh.ClaudeAvailable(
                    pulled,
                    pulled ? KnownOrNull(await AvailableAsync().ConfigureAwait(false)) : null);

                if (!pulled)
                {
                    notes.Add($"The {marketplace} marketplace was not refreshed, so Claude's published version is unknown.");
                }

                // Read whether or not Claude says the plugin is installed. A plugin
                // that was uninstalled leaves every version it ever had in the
                // cache, and those are the stalest folders of all. Not read at all
                // when the listing failed: with no install paths to read against,
                // every folder would show as stale, and the pane would offer to
                // clear the ones every install points at.
                if (claudeInstallsKnown)
                {
                    cachedVersions = ReadCachedVersions(pluginId, claudeInstallPaths);
                }
                else
                {
                    notes.Add("Claude's plugin listing could not be read, so cached versions are not shown.");
                }

                states.Add(new DevToolHostState(
                    DevToolHosts.Claude,
                    state is not null,
                    installedVersion,
                    available,
                    state is { Enabled: false }
                        ? "Installed but switched off in Claude"
                        : DescribeStatus(enabled, state is not null, DevToolInfo.VersionDiffers(installedVersion, available), "Claude plugin"))
                {
                    InstalledAuthority = DevToolVersionAuthority.ClaudeMarketplace,
                    AvailableAuthority = DevToolVersionAuthority.ClaudeMarketplace
                });
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
            HostStates = states,
            CachedVersions = cachedVersions
        };
    }

    /// <summary>Where Claude keeps its config — and under it, its plugin cache.
    /// The same override the CLI honours, so a machine that moved its config
    /// folder is read where it actually is rather than where the default
    /// would be.</summary>
    private static string ClaudeConfigDirectory =>
        Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR") is { Length: > 0 } configured
            ? configured
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude");

    private static string ClaudeCacheRoot => DevToolCache.CacheRoot(ClaudeConfigDirectory);

    /// <summary>The versions of one plugin sitting in Claude's cache, read
    /// against what the listing said is installed. Empty for a plugin with no
    /// cache folder, and empty — rather than a failed listing — when the folder
    /// cannot be read: the cache is a detail of the row, not the row.</summary>
    private static IReadOnlyList<DevToolCachedVersion> ReadCachedVersions(string pluginId, IReadOnlyDictionary<string, int> installPaths)
    {
        if (DevToolCache.PluginDirectory(ClaudeCacheRoot, pluginId) is not { } directory || !Directory.Exists(directory))
        {
            return [];
        }

        try
        {
            return DevToolCache.Describe(Directory.EnumerateDirectories(directory), installPaths);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
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
    ///
    /// <para>All of which is about a server that ships as a .NET tool, and the
    /// array holds one other kind: a server registered by the command it declares,
    /// which has no package, no published version and no install. That row reports
    /// its command line where a version would go — the way
    /// <see cref="DescribeClaudeRegistration" /> already lets a command stand in
    /// for a registration's version — and keeps the no-version dash in the
    /// available column, because there is nothing published to be behind. Nothing
    /// is looked up for it and nothing is invented: a number here would be a claim
    /// about a search nobody ran.</para>
    ///
    /// <para>And a row with no .NET tool and no command either — an entry that
    /// declares itself <c>manual</c>, or a <c>packageId</c> entry whose mechanism
    /// this build has never heard of — gets the no-version dash in both columns.
    /// The dash is the marker the pane's own <c>ReportsDetection</c> and
    /// <see cref="DevToolInfo.AvailableVersionKnown" /> already read as "there is
    /// deliberately nothing here"; the empty string that used to arrive instead is
    /// read by nothing, and drew a blank Installed cell opposite a dashed Available
    /// one, which reads as a column that failed rather than one with nothing to
    /// say.</para>
    /// </summary>
    private static async Task<DevToolInfo> DescribeMcpServerAsync(
        JsonNode server,
        DevToolMcpServer declared,
        IReadOnlyDictionary<string, string> installedTools,
        string? claudeCli,
        ClaudeDesktopState? claudeDesktop,
        IMcpEndpointSource? mcp,
        CommandLog log,
        CancellationToken ct)
    {
        var enabled = declared.Enabled;
        var hosts = declared.Hosts;
        var toolInstalled = declared.Installable && installedTools.ContainsKey(declared.PackageId);

        // The URL with this machine's port filled in — and its token deliberately
        // left literal. Describing is what every refresh does for every row, so
        // the resolver used here is the one that refuses to mint: a table nobody
        // pressed anything on must not be what writes a secret to this machine.
        var endpoint = declared.Mechanism is DevToolMcpMechanism.Http
            ? McpPlaceholders.Expand(declared.Url, DescribingWith(mcp))
            : null;

        var installedVersion = declared.Installable
            ? installedTools.TryGetValue(declared.PackageId, out var version) ? version : DevToolOutput.NotInstalled
            : declared.CommandLine is { Length: > 0 } commandLine ? commandLine
            : endpoint is { Text.Length: > 0 } reached ? reached.Text
            : DevToolOutput.NoVersion;
        var availableVersion = await GetDotNetToolAvailableVersionAsync(declared, log, ct).ConfigureAwait(false);

        var states = new List<DevToolHostState>
        {
            new(
                hosts,
                // A row with no mechanism behind it is reported as present, for the
                // reason DescribePluginAsync reports an uninspectable host as
                // present: "not installed" is a claim, and it is the claim that
                // puts an Install button on a row with nothing to run.
                !declared.Installable || toolInstalled,
                installedVersion,
                availableVersion,
                declared.Installable
                    ? DescribeStatus(enabled, toolInstalled, DevToolInfo.VersionDiffers(installedVersion, availableVersion), "mcp-server")
                    : MechanismStatus(declared))
        };
        var notes = new List<string>();

        // A mechanism this build has never heard of runs nothing at all — not the
        // .NET tool half above, and not either registration below. The fact
        // travels in the row's own notes, because an entry silently drawn as
        // action-less is a typo in a hand-edited file with nothing pointing at it.
        if (!declared.MechanismRecognised)
        {
            notes.Add($"\"{declared.DeclaredMechanism}\" is not a mechanism this build knows, so nothing runs for this row");
        }

        var registration = declared.Mechanism is DevToolMcpMechanism.Manual
            ? null
            : DevToolConfiguration.McpRegistrationSection(server);

        // What a row reached over HTTP has to say about the endpoint behind it,
        // which is not the same question as whether its registration is right.
        // A registration can be perfect and answer nothing, because the feature
        // is off or because somebody else holds the port — so these are notes on
        // the row rather than findings about the registration.
        if (endpoint is not null)
        {
            if (mcp is null)
            {
                notes.Add(EndpointUnresolvable);
            }
            else if (!mcp.Enabled)
            {
                notes.Add(EndpointDisabled);
            }
            else if (mcp.Unavailable is { Length: > 0 } unavailable)
            {
                // The worker's own sentence, verbatim. It already ends by saying
                // to change the port in every registration too, which is exactly
                // what this row is for.
                notes.Add(unavailable);
            }

            if (endpoint.Problem is { } problem)
            {
                notes.Add(problem);
            }
        }

        if (hosts.HasFlag(DevToolHosts.Claude) && registration is { } claude)
        {
            var serverName = ClaudeServerName(server, claude);
            var http = IsHttpRegistration(claude);

            // Where this registration is supposed to point: the expanded URL for
            // a server that is reached, and the command for one that is started.
            var target = http
                ? McpPlaceholders.Expand(GetString(claude, "url"), DescribingWith(mcp)).Text
                : GetString(claude, "command");

            if (claudeCli is null)
            {
                notes.Add(ClaudeCliMissing);
            }
            else if (string.IsNullOrWhiteSpace(serverName) || string.IsNullOrWhiteSpace(target))
            {
                notes.Add(http ? "Claude registration needs a name and a URL" : "Claude registration needs a name and a command");
            }
            else
            {
                var details = await GetClaudeMcpServerAsync(claudeCli, serverName, log, ct).ConfigureAwait(false);
                var state = DescribeClaudeRegistration(serverName, DeclaredTransport(claude), target, details);

                // The finding stays; the offer goes. Re-registering calls
                // EnsureToken, and minting a secret for a server nobody switched
                // on — or for an endpoint this host cannot resolve at all — is
                // the one thing a button here must not do. The row still says it
                // is pointing elsewhere, and the note above says why nothing can
                // be done about it yet.
                if (http && mcp is not { Enabled: true })
                {
                    state = state with { RegistrationDrifted = false };
                }

                states.Add(WithoutAnInventedVersion(state, declared));
            }
        }

        // The desktop app is a third host and not a second reading of the Claude
        // CLI beside it: its own config file, its own server list, and a change to
        // either that the other never sees.
        if (hosts.HasFlag(DevToolHosts.ClaudeDesktop))
        {
            // A manual entry and an entry with no section are two different facts,
            // and folding them into one condition reported the wrong one: an entry
            // that declares "mechanism": "manual" and carries a perfectly good
            // claudeDesktop section was told there was "No claudeDesktop or claude
            // section to register". What is true of it is that nothing here is
            // going to act on it, which is what MechanismStatus already says for
            // the row's own status — and an unrecognised mechanism, which lands on
            // Manual as well, has said so in its own note above and is not told
            // twice.
            if (declared.Mechanism is DevToolMcpMechanism.Manual)
            {
                if (declared.MechanismRecognised)
                {
                    notes.Add(MechanismStatus(declared));
                }
            }
            else if (ClaudeDesktopSection(server) is not { } desktopSection)
            {
                notes.Add("No claudeDesktop or claude section to register");
            }
            else if (claudeDesktop is null)
            {
                notes.Add(ClaudeDesktopMissing);
            }
            else
            {
                states.Add(WithoutAnInventedVersion(
                    DescribeClaudeDesktopRegistration(
                        ClaudeServerName(server, desktopSection),
                        ClaudeDesktopCommandLine(desktopSection),
                        claudeDesktop),
                    declared));
            }
        }

        return new DevToolInfo(
            declared.Key,
            DevToolKind.McpServer,
            declared.DisplayName,
            declared.Source,
            enabled,
            states.All(state => state.Installed),
            installedVersion,
            availableVersion,
            AggregateStatus(enabled, states, notes, "mcp-server"))
        {
            Hosts = hosts,
            HostStates = states,
            Installable = declared.Installable
        };
    }

    /// <summary>What a row with no .NET tool behind it says where a version would
    /// go.
    ///
    /// <para>A command-registered server is on the machine or it is not, and
    /// nothing here can tell which — so the status says what it is rather than
    /// what was found. A mechanism this build does not recognise says exactly
    /// that, in the row's notes; here it only says that nothing was run.</para></summary>
    private static string MechanismStatus(DevToolMcpServer server) => server.Mechanism switch
    {
        DevToolMcpMechanism.Command => "Registered by the command it declares",
        // Not "nothing installs this", which is the arm it used to fall into and
        // which is false about it: there is nothing to install, and there is very
        // much something to register.
        DevToolMcpMechanism.Http => "Registered by the URL it declares",
        _ => "Nothing here installs or registers this server"
    };

    /// <summary>What a row says when this host has no Backlog endpoint to write a
    /// registration from. Said plainly for the reason <see cref="ClaudeCliMissing"/>
    /// is: "could not expand" reads as a failure, and this is a host that was
    /// never going to be able to.</summary>
    private const string EndpointUnresolvable =
        "The Backlog MCP endpoint is resolved in the desktop app, so nothing here can expand or register this row.";

    /// <summary>What a row says when the machine has the server switched off.
    ///
    /// <para>The row is still drawn and the registration is still left exactly
    /// where it is: a registration made while the feature was on is on this
    /// machine either way, and it still names the port. What is true is only that
    /// nothing answers there.</para></summary>
    private const string EndpointDisabled =
        "The MCP server is switched off, so nothing answers at the registered address.";

    /// <summary>
    /// The resolver a listing may use: the port, and never the token.
    ///
    /// <para>A host with no endpoint gets one that answers nothing, which is not
    /// the same as one that invents a value. Every placeholder stays literal and
    /// <see cref="McpExpansion.Problem"/> says which one could not be filled in,
    /// so the row shows the URL exactly as the catalog spells it and says why.</para>
    /// </summary>
    private static McpPlaceholderResolver DescribingWith(IMcpEndpointSource? mcp) =>
        mcp is null ? NoEndpoint : McpPlaceholders.Describing(mcp);

    /// <inheritdoc cref="DescribingWith" />
    private static readonly McpPlaceholderResolver NoEndpoint = _ => null;

    /// <summary>Whether a registration section registers a URL rather than a
    /// command.
    ///
    /// <para>Read off the section and not off the entry's mechanism, because the
    /// section is what the registration is made from: a nested <c>claude</c> block
    /// wins over the synthesised one, and it is allowed to say something the
    /// entry's own shape does not. The <c>type</c> answers where there is one, and
    /// a bare <c>url</c> with no <c>command</c> beside it answers where there is
    /// not.</para></summary>
    private static bool IsHttpRegistration(JsonNode section) =>
        DeclaredTransport(section).Equals(DevToolConfiguration.HttpTypeName, StringComparison.OrdinalIgnoreCase)
        || (GetString(section, "url").Trim().Length > 0 && GetString(section, "command").Trim().Length == 0);

    /// <summary>The transport a section declares, or empty when it declares
    /// none — which is every registration written before there was one to
    /// declare, and is read as "no opinion" rather than as stdio.</summary>
    private static string DeclaredTransport(JsonNode section) => GetString(section, "type").Trim();

    /// <summary>
    /// One host state's available column, blanked for a row that has no published
    /// version to be behind.
    ///
    /// <para>Both registration describers put the catalog's own command in that
    /// column, and for a .NET-tool server that is the right answer: a registration
    /// pointing at a renamed executable is fixed by a re-add, and the two columns
    /// differing is how the row offers it. A command-registered server has no
    /// second thing to compare against — the command <em>is</em> the entry — so
    /// repeating it there would make <see cref="DevToolInfo.AvailableVersionKnown"/>
    /// true about a lookup nobody performed, and the row would read as up to date
    /// on the strength of its own command line.</para>
    /// </summary>
    private static DevToolHostState WithoutAnInventedVersion(DevToolHostState state, DevToolMcpServer server) =>
        server.Installable ? state : state with { AvailableVersion = DevToolOutput.NoVersion };

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
    private static DevToolHostState DescribeClaudeRegistration(
        string serverName,
        string declaredType,
        string expectedTarget,
        DevToolOutput.ClaudeMcpServerDetails? details)
    {
        if (details is null)
        {
            return new DevToolHostState(
                DevToolHosts.Claude,
                Installed: false,
                DevToolOutput.NotInstalled,
                expectedTarget,
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

        // Whichever of the two the host reported, chosen by what it said the
        // transport was rather than by which string happens to be non-empty: a
        // stdio registration of a server the catalog now reaches over HTTP has a
        // command, and showing it opposite a URL is how somebody sees what
        // actually needs repairing.
        var registered = details.Type.Equals(DevToolConfiguration.HttpTypeName, StringComparison.OrdinalIgnoreCase)
            ? details.Url
            : details.Command;

        // Asked of the abstraction rather than compared here, because "the same
        // registration" is not string equality: a trailing slash and the case of
        // a scheme are not drift, and a transport that disagrees is drift whatever
        // the two targets say.
        var drifted = DevToolConfiguration.RegistrationDrifted(declaredType, expectedTarget, details);

        return new DevToolHostState(
            DevToolHosts.Claude,
            Installed: true,
            string.IsNullOrWhiteSpace(registered) ? DevToolOutput.Unknown : registered,
            expectedTarget,
            drifted
                ? $"Registered with Claude as '{serverName}', pointing elsewhere"
                : $"Registered with Claude as '{serverName}'")
        {
            // Carried rather than left for the row to re-derive from the two
            // columns above, which are prose as often as they are targets.
            RegistrationDrifted = drifted
        };
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
        // An application is included when it is genuinely behind *or* absent,
        // which is one more case than the other kinds get. "Update all" for a
        // software inventory is read as "make this machine match the catalog", and
        // an app the catalog asks for and the machine does not have is the most
        // obvious thing that sweep should fix.
        //
        // Installable is what keeps it honest: a checklist row, a detect-only
        // package and a manual acknowledgement are all permanently "not
        // installed", and each of them would otherwise be attempted — and reported
        // as a failure — on every sweep forever.
        var candidates = catalog.Tools
            .Where(tool => tool.Kind switch
            {
                DevToolKind.Marketplace => tool.CanUpdate || !tool.Installed,
                DevToolKind.Application => tool.CanUpdate || tool.CanInstall,
                _ => tool.CanUpdate
            })
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

    /// <summary>
    /// Ticks — or unticks — the box on a row nothing can check.
    ///
    /// <para>Per machine, in the per-PC file, because "this laptop is signed in"
    /// is not a fact to sync to the next one.</para>
    ///
    /// <para>Its own port method rather than a meaning hidden inside
    /// <see cref="UpdateAsync"/>, which is where it lived first and could not
    /// stay: Update is what a row offers while there is something left to do, so
    /// ticking the box removed the only control the row had and the tick became
    /// permanent. The state is passed in rather than toggled here for the same
    /// reason — the checkbox already knows which way it went.</para>
    /// </summary>
    public async Task<DevToolActionResult> AcknowledgeAsync(string key, bool acknowledged, CancellationToken ct = default)
    {
        var log = new CommandLog();
        var result = await AcknowledgeCoreAsync(key, acknowledged, log, ct).ConfigureAwait(false);

        return result with { Commands = log.Commands };
    }

    private async Task<DevToolActionResult> AcknowledgeCoreAsync(string key, bool acknowledged, CommandLog log, CancellationToken ct)
    {
        var configPaths = ConfigurationPaths;
        if (!DevToolConfiguration.CatalogExists(configPaths))
        {
            return DevToolActionResult.Failed($"Tool catalog was not found at {configPaths.CatalogPath}.");
        }

        if (DevToolConfiguration.KindOf(key) is not DevToolKind.Application)
        {
            return DevToolActionResult.Failed("Only an application row is confirmed by hand.");
        }

        try
        {
            var config = await DevToolConfiguration.ReadAsync(configPaths, ct).ConfigureAwait(false);
            if (FindToolNode(config.Root, key) is not JsonObject entry
                || DevToolConfiguration.ReadApplication(entry) is not { } application)
            {
                return DevToolActionResult.Failed("That tool is no longer in the config.");
            }

            // A row with a mechanism behind it has an answer somebody can go and
            // find, and a hand-written tick over the top of one is how a probe
            // gets overruled by a habit.
            if (application.Provider is not DevToolProvider.Manual)
            {
                return DevToolActionResult.Failed($"{application.Name} is checked by running something, so there is nothing to confirm by hand.");
            }

            await DevToolConfiguration.WriteAcknowledgementAsync(configPaths, application.Key, acknowledged, ct).ConfigureAwait(false);

            log.RecordStep(
                $"{configPaths.PcConfigPath}: {application.Key} acknowledged = {acknowledged.ToString().ToLowerInvariant()}",
                0,
                "Nothing was run; this row records what a person confirmed rather than what a machine found.");

            return DevToolActionResult.Ok(acknowledged
                ? $"{application.Name} is marked as done on this machine."
                : $"{application.Name} is no longer marked as done on this machine.");
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or IOException or UnauthorizedAccessException or JsonException)
        {
            _logger?.LogWarning(ex, "Acknowledgement failed for {Key}.", key);
            return DevToolActionResult.Failed($"That could not be recorded: {ex.Message}");
        }
    }

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

    public async Task<DevToolActionResult> RemoveCachedVersionAsync(string key, string version, CancellationToken ct = default)
    {
        var log = new CommandLog();
        var result = await RemoveCachedVersionCoreAsync(key, version, log, ct).ConfigureAwait(false);

        return result with { Commands = log.Commands };
    }

    private async Task<DevToolActionResult> RemoveCachedVersionCoreAsync(string key, string version, CommandLog log, CancellationToken ct)
    {
        if (DevToolConfiguration.KindOf(key) is not DevToolKind.Plugin)
        {
            return DevToolActionResult.Failed("Only a plugin has a cache to clear.");
        }

        // The version arrives from a row that read it off a folder name, but the
        // port cannot know that: anything with a separator in it would name a
        // folder outside the plugin's own, and the delete below is recursive.
        if (!DevToolCache.IsPlainFolderName(version))
        {
            return DevToolActionResult.Failed($"'{version}' is not a cached version.");
        }

        var configPaths = ConfigurationPaths;
        if (!DevToolConfiguration.CatalogExists(configPaths))
        {
            return DevToolActionResult.Failed($"Tool catalog was not found at {configPaths.CatalogPath}.");
        }

        var root = (await DevToolConfiguration.ReadAsync(configPaths, ct).ConfigureAwait(false)).Root;
        if (FindToolNode(root, key) is not { } plugin)
        {
            return DevToolActionResult.Failed("That tool is no longer in the config.");
        }

        var name = GetRequiredString(plugin, "name");
        if (ClaudePluginIdFor(plugin, name, DevToolConfiguration.DefaultMarketplaceName(root)) is not { } pluginId)
        {
            return DevToolActionResult.Failed(NoMarketplace);
        }

        if (DevToolCache.PluginDirectory(ClaudeCacheRoot, pluginId) is not { } pluginDirectory)
        {
            return DevToolActionResult.Failed($"{pluginId} does not name a cache folder.");
        }

        var directory = Path.Combine(pluginDirectory, version);
        if (!Directory.Exists(directory))
        {
            return DevToolActionResult.Failed($"{pluginId} {version} is not in Claude's plugin cache.");
        }

        // Asked again rather than trusted from the row. The row's "in use" is as
        // old as the last check, and an install made since — a project scope
        // registered in another window — is exactly the one a stale answer would
        // delete out from under.
        if (await ResolveClaudeCliAsync(log, ct).ConfigureAwait(false) is not { } claudeCli)
        {
            return DevToolActionResult.Failed(ClaudeCliMissing);
        }

        var listing = await GetInstalledClaudePluginsAsync(claudeCli, log, ct).ConfigureAwait(false);
        if (!listing.Known)
        {
            return DevToolActionResult.Failed(ClaudeListingUnreadable);
        }

        var installs = listing.InstallPaths.GetValueOrDefault(DevToolCache.NormalizePath(directory));
        if (installs > 0)
        {
            return DevToolActionResult.Failed(
                $"{pluginId} {version} is still in use by {Installs(installs)}. Update those installs first; deleting the folder would break them.");
        }

        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger?.LogWarning(ex, "Could not delete cached plugin version {Directory}.", directory);
            return DevToolActionResult.Failed($"{pluginId} {version} could not be removed from the cache: {ex.Message}");
        }

        return DevToolActionResult.Ok($"Removed {pluginId} {version} from Claude's plugin cache.");
    }

    public async Task<DevToolActionResult> RemoveStaleCacheAsync(CancellationToken ct = default)
    {
        var log = new CommandLog();
        var result = await RemoveStaleCacheCoreAsync(log, ct).ConfigureAwait(false);

        return result with { Commands = log.Commands };
    }

    /// <summary>
    /// The whole cache walked — every marketplace, every plugin, every version —
    /// and every version folder no install points at deleted.
    ///
    /// <para>Not driven by the catalog, deliberately. The cache on a machine that
    /// has been through a marketplace rename holds a whole tree under the old
    /// marketplace's name that no row will ever describe, and those folders are
    /// the ones nothing else can reach.</para>
    /// </summary>
    private async Task<DevToolActionResult> RemoveStaleCacheCoreAsync(CommandLog log, CancellationToken ct)
    {
        var cacheRoot = ClaudeCacheRoot;
        if (!Directory.Exists(cacheRoot))
        {
            return DevToolActionResult.Ok("Claude's plugin cache is empty.");
        }

        if (await ResolveClaudeCliAsync(log, ct).ConfigureAwait(false) is not { } claudeCli)
        {
            return DevToolActionResult.Failed(ClaudeCliMissing);
        }

        var listing = await GetInstalledClaudePluginsAsync(claudeCli, log, ct).ConfigureAwait(false);
        if (!listing.Known)
        {
            // Refused, not read as "no install anywhere": with an empty map every
            // folder in the cache is stale, and the walk below would delete the
            // versions every install points at along with the rest.
            return DevToolActionResult.Failed(ClaudeListingUnreadable);
        }

        var removed = 0;
        var failed = new List<string>();

        try
        {
            foreach (var marketplace in Directory.EnumerateDirectories(cacheRoot))
            {
                foreach (var plugin in Directory.EnumerateDirectories(marketplace))
                {
                    foreach (var cached in DevToolCache.Describe(Directory.EnumerateDirectories(plugin), listing.InstallPaths))
                    {
                        ct.ThrowIfCancellationRequested();

                        if (cached.InUse)
                        {
                            continue;
                        }

                        try
                        {
                            Directory.Delete(cached.Path, recursive: true);
                            removed++;
                        }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                        {
                            _logger?.LogWarning(ex, "Could not delete cached plugin version {Directory}.", cached.Path);
                            failed.Add($"{Path.GetFileName(plugin)}@{Path.GetFileName(marketplace)} {cached.Version}");
                        }
                    }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger?.LogWarning(ex, "Could not walk Claude's plugin cache at {Directory}.", cacheRoot);
            return DevToolActionResult.Failed($"Claude's plugin cache could not be read: {ex.Message}");
        }

        var summary = removed switch
        {
            0 => "Nothing in Claude's plugin cache was stale.",
            1 => "Removed 1 stale version from Claude's plugin cache.",
            _ => $"Removed {removed} stale versions from Claude's plugin cache."
        };

        return failed.Count == 0
            ? DevToolActionResult.Ok(summary)
            : DevToolActionResult.Failed($"{summary} Could not remove: {string.Join(", ", failed)}.");
    }

    private static string Installs(int count) => count == 1 ? "1 install" : $"{count} installs";

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
        var kind = DevToolConfiguration.KindOf(key);
        var node = FindToolNode(root, key);
        if (node is null)
        {
            return DevToolActionResult.Failed("That tool is no longer in the config.");
        }

        if (kind is DevToolKind.Marketplace)
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
            // Spelled out per kind rather than as the two-way ternary this
            // replaced, whose else was "MCP server": an app: key was not rejected
            // by it, it was run as an MCP server against an entry that has no
            // packageId — an "is missing 'packageId'" on a row about Visual Studio
            // Code.
            return kind switch
            {
                DevToolKind.Plugin => await ApplyPluginAsync(node, DevToolConfiguration.DefaultMarketplaceName(root), log, ct).ConfigureAwait(false),
                DevToolKind.McpServer => await ApplyMcpServerAsync(node, log, ct).ConfigureAwait(false),
                DevToolKind.Application => await ApplyApplicationAsync(node, log, ct).ConfigureAwait(false),
                DevToolKind.Marketplace => throw new InvalidOperationException("A marketplace is handled above.")
            };
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
                : DevToolActionResult.Failed(log.Failure(name, uninstall));
        }

        var args = isInstalled ? new[] { "plugin", "update", name } : ["plugin", "install", GetRequiredString(plugin, "source")];
        var result = await RunAsync(cli.Command, [.. cli.Prefix, .. args], log, ct).ConfigureAwait(false);
        return result.ExitCode == 0
            ? DevToolActionResult.Ok(isInstalled ? "updated." : "installed.")
            : DevToolActionResult.Failed(log.Failure(name, result));
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

        var installed = (await GetInstalledClaudePluginsAsync(cli, log, ct).ConfigureAwait(false)).Plugins;
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
                : DevToolActionResult.Failed(log.Failure(pluginId, uninstall));
        }

        // The marketplace before either install or update. Claude installs out of
        // its own clone of one, so an update against a clone nobody pulled runs,
        // exits 0 and changes nothing — which is how a row came to offer the same
        // update forever. Failing here rather than reporting success names the one
        // reason the press could not have worked.
        var marketplace = ClaudeMarketplaceOf(pluginId);
        var pull = await RunAsync(DevToolCommands.ClaudeMarketplaceUpdate(cli, marketplace), log, ct).ConfigureAwait(false);
        if (pull.ExitCode != 0)
        {
            return DevToolActionResult.Failed(
                $"the {marketplace} marketplace could not be refreshed, so {pluginId} can only reach what Claude already has. {log.Failure(marketplace, pull)}");
        }

        if (state is null)
        {
            var install = await RunAsync(cli, ["plugin", "install", pluginId, "--scope", "user"], log, ct).ConfigureAwait(false);
            return install.ExitCode == 0
                ? DevToolActionResult.Ok($"{pluginId} installed.")
                : DevToolActionResult.Failed(log.Failure(pluginId, install));
        }

        var update = await RunAsync(cli, ["plugin", "update", pluginId], log, ct).ConfigureAwait(false);
        if (update.ExitCode != 0)
        {
            return DevToolActionResult.Failed(log.Failure(pluginId, update));
        }

        if (state.Enabled)
        {
            return DevToolActionResult.Ok($"{pluginId} updated.");
        }

        var enable = await RunAsync(cli, ["plugin", "enable", pluginId], log, ct).ConfigureAwait(false);
        return enable.ExitCode == 0
            ? DevToolActionResult.Ok($"{pluginId} updated and enabled.")
            : DevToolActionResult.Failed(log.Failure(pluginId, enable));
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
                : DevToolActionResult.Failed(log.Failure(name, update));
        }

        var source = GetRequiredString(marketplace, "source");
        var add = await RunAsync(cli, ["plugin", "marketplace", "add", source], log, ct).ConfigureAwait(false);
        return add.ExitCode == 0
            ? DevToolActionResult.Ok($"The {name} marketplace was added.")
            : DevToolActionResult.Failed(log.Failure(name, add));
    }

    /// <summary>
    /// One MCP server row's action: the shared .NET tool, and then Claude's
    /// registration of it.
    ///
    /// <para>Both halves, in that order, because the registration points at the
    /// tool: registering before installing would name an executable that is not
    /// there yet, and disabling in the other order would leave Claude holding a
    /// registration for something that has just been uninstalled.</para>
    ///
    /// <para>Which halves run at all is the entry's mechanism. A server registered
    /// by the command it declares has no .NET tool half: there is no package to
    /// install, none to uninstall on a disable, and a <c>dotnet tool</c> launch
    /// with a blank id behind it would be reported as this row's failure. So that
    /// half is omitted rather than attempted, and a mechanism this build does not
    /// recognise omits both — the safe reading of a mechanism nobody here knows is
    /// the one that runs nothing.</para>
    /// </summary>
    private async Task<DevToolActionResult> ApplyMcpServerAsync(JsonNode server, CommandLog log, CancellationToken ct)
    {
        var declared = DevToolConfiguration.ReadMcpServer(server)
            ?? throw new InvalidOperationException("The tool config entry has no 'packageId', 'name' or 'command' to address it by.");
        var enabled = declared.Enabled;
        var outcomes = new List<string>();
        var failed = false;

        if (declared.Mechanism is DevToolMcpMechanism.DotNetTool)
        {
            var tool = await ApplyDotNetToolAsync(declared, enabled, log, ct).ConfigureAwait(false);
            outcomes.Add($".NET tool: {tool.Message}");
            failed |= !tool.Succeeded;
        }
        else if (!declared.MechanismRecognised)
        {
            outcomes.Add($"nothing was run: \"{declared.DeclaredMechanism}\" is not a mechanism this build knows.");
        }
        else if (declared.Mechanism is DevToolMcpMechanism.Command)
        {
            outcomes.Add("nothing to install: this server is registered by the command it declares.");
        }
        else if (declared.Mechanism is DevToolMcpMechanism.Http)
        {
            // Same shape, different reason. There is nothing to install because
            // the server is reached rather than started — and unlike the manual
            // arm below, the registration half underneath this very much does
            // run for it.
            outcomes.Add("nothing to install: this server is reached at the URL it declares.");
        }
        else
        {
            // Which leaves Manual, and it gets its own sentence rather than the
            // one above. Keying this off MechanismRecognised told an entry that
            // says "mechanism": "manual" out loud — recognised, so it falls past
            // the typo branch — that it "is registered by the command it
            // declares", which is a claim about a command such an entry declares
            // nowhere. The wording is MechanismStatus's, so the status the row
            // shows and the sentence the action reports cannot describe the same
            // entry two different ways.
            outcomes.Add("nothing was run: nothing here installs or registers this server.");
        }

        var hosts = declared.Hosts;
        var registration = declared.Mechanism is DevToolMcpMechanism.Manual
            ? null
            : DevToolConfiguration.McpRegistrationSection(server);

        if (hosts.HasFlag(DevToolHosts.Claude) && registration is { } claude)
        {
            var claudeResult = await ApplyClaudeMcpRegistrationAsync(server, claude, enabled, log, ct).ConfigureAwait(false);
            outcomes.Add($"Claude: {claudeResult.Message}");
            failed |= !claudeResult.Succeeded;
        }

        // The desktop app after the CLI, and separately from it. The two keep
        // their own server lists in their own places, and a registration made
        // through one is invisible to the other.
        if (hosts.HasFlag(DevToolHosts.ClaudeDesktop) && declared.Mechanism is not DevToolMcpMechanism.Manual)
        {
            var desktop = await ApplyClaudeDesktopRegistrationAsync(server, enabled, log, ct).ConfigureAwait(false);
            outcomes.Add($"Claude Desktop: {desktop.Message}");
            failed |= !desktop.Succeeded;
        }

        // Named by whichever identity the entry actually has, rather than by a
        // package id that a command-registered server does not carry: a message
        // that opened with " — " told the operator nothing about which row it was
        // reporting on.
        var message = $"{declared.Id} — {string.Join(" ", outcomes)}";

        return failed ? DevToolActionResult.Failed(message) : DevToolActionResult.Ok(message);
    }

    /// <summary>
    /// The shared .NET tool half of one MCP server row: installed when it is
    /// wanted and absent, updated when it is wanted and there, and uninstalled
    /// when the machine has said it does not want it.
    ///
    /// <para>Its own method so that the one thing it must never do is structural
    /// rather than remembered: it takes a <see cref="DevToolMcpServer" /> and
    /// refuses anything but a <see cref="DevToolMcpMechanism.DotNetTool" />, so
    /// there is no path from a command-registered entry to a <c>dotnet tool</c>
    /// launch with a blank package id.</para>
    /// </summary>
    private async Task<DevToolActionResult> ApplyDotNetToolAsync(DevToolMcpServer server, bool enabled, CommandLog log, CancellationToken ct)
    {
        if (server.Mechanism is not DevToolMcpMechanism.DotNetTool)
        {
            return DevToolActionResult.Ok("nothing to install: this server ships as no .NET tool.");
        }

        var packageId = server.PackageId;
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

        if (!enabled && !isInstalled)
        {
            return DevToolActionResult.Ok("already absent.");
        }

        var result = await RunAsync("dotnet", args, log, ct).ConfigureAwait(false);

        return result.ExitCode == 0
            ? DevToolActionResult.Ok(success)
            : DevToolActionResult.Failed(log.Failure(packageId, result));
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

        // Which transport this registration is made over. Read here rather than
        // where it is used, because the line below needs it: the answer decides
        // whether this apply is one that can mint a token.
        var http = IsHttpRegistration(claude);

        // Before anything is run, and that ordering is the whole point. The log
        // masks at record time, so a secret it is told about afterwards is a
        // secret already written into a line the pane will render — and the very
        // first command of this method, `claude mcp get`, echoes back the
        // Headers: block of whatever is registered today.
        RedactRegistrationSecrets(claude, http && enabled, log);

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

            var remove = await RunClaudeMcpAsync(cli, ["mcp", "remove", name, "--scope", "user"], log, ct).ConfigureAwait(false);
            return remove.ExitCode == 0
                ? DevToolActionResult.Ok($"'{name}' was unregistered.")
                : DevToolActionResult.Failed(log.Failure(name, remove));
        }

        // The transport read at the top of this method decides both what the
        // target is and which `claude mcp add` vector builds it. The two are
        // genuinely different command lines — the stdio one separates its flags
        // from the command with `--`, and putting one before a URL registers a
        // server that can never answer — so they are built apart rather than
        // patched into one.
        string target;
        IReadOnlyList<string> addArguments;

        if (http)
        {
            var resolved = RegisterableEndpoint(name, claude, log);
            if (resolved.Failure is { } failed)
            {
                return failed;
            }

            target = resolved.Url;
            addArguments = DevToolCommands.ClaudeMcpAddHttp(cli, name, target, resolved.Headers).LaunchArguments;
        }
        else
        {
            target = GetString(claude, "command");
            if (string.IsNullOrWhiteSpace(target))
            {
                return DevToolActionResult.Failed($"'{name}' has no claude.command to register.");
            }

            addArguments = ["mcp", "add", "--scope", "user", name, "--", target, .. ClaudeServerArgs(claude)];
        }

        // Against the target rather than against the command alone, and through
        // the abstraction: a URL that differs only by a trailing slash is the
        // same registration, and a stdio registration of an HTTP server is a
        // different one however its command reads.
        if (details is not null && !DevToolConfiguration.RegistrationDrifted(DeclaredTransport(claude), target, details))
        {
            return DevToolActionResult.Ok($"'{name}' was already registered.");
        }

        if (details is not null)
        {
            var remove = await RunClaudeMcpAsync(cli, ["mcp", "remove", name, "--scope", "user"], log, ct).ConfigureAwait(false);
            if (remove.ExitCode != 0)
            {
                return DevToolActionResult.Failed(log.Failure(name, remove));
            }
        }

        var add = await RunClaudeMcpAsync(cli, addArguments, log, ct).ConfigureAwait(false);
        return add.ExitCode == 0
            ? DevToolActionResult.Ok(details is null ? $"'{name}' was registered." : $"'{name}' was re-registered.")
            : DevToolActionResult.Failed(log.Failure(name, add));
    }

    /// <summary>
    /// Everything about one registration that is a credential, told to the log
    /// before the apply runs anything at all.
    /// </summary>
    /// <remarks>
    /// <para><b>Every header value, not only Backlog's own token.</b> The Add-a-
    /// tool form takes free-text headers, so <c>X-Api-Key: sk-live-…</c> is a
    /// shape a person can put into the catalog — and it reaches the pane twice
    /// over, joined verbatim into the paste-ready <c>--header</c> line and echoed
    /// back in the <c>Headers:</c> block <c>claude mcp get</c> prints. A header
    /// value is a credential by default here: this code cannot tell an API key
    /// from a client tag, and of the two ways to be wrong, masking a tag costs a
    /// word of legibility and showing a key costs a rotation. The URL's query
    /// string goes in for the same reason — <c>?token=…</c> is the other place
    /// people put one.</para>
    ///
    /// <para><b>And before the first recorded command, which is the half that was
    /// wrong.</b> <see cref="CommandLog.Redact"/> takes effect from the moment it
    /// is called, because the log masks as it records rather than when it is
    /// read — so a value handed over after <c>claude mcp get</c> has run is a
    /// value already written into a line the pane will render. The mint used to
    /// happen inside <see cref="RegisterableEndpoint"/>, two commands later,
    /// under a remark claiming it came first.</para>
    ///
    /// <para>A declared value still carrying <c>${</c> is not yet a secret and is
    /// not masked as one; it is a placeholder, and masking it would blank the
    /// very text a person needs to see to understand what their catalog says. Its
    /// expansion is the secret, and that is seeded here when it is Backlog's own
    /// token — which is known without expanding anything — and again in
    /// <see cref="RegisterableEndpoint"/> for anything else the expansion
    /// produces.</para>
    /// </remarks>
    /// <param name="mayMint">Whether this apply is one that is allowed to create
    /// a token: an HTTP registration somebody asked to be made. Unregistering
    /// mints nothing, and neither does a stdio row — writing a secret to a
    /// machine to take a registration <em>away</em> would be the plainest version
    /// of the thing every guard in this file is about.</param>
    private void RedactRegistrationSecrets(JsonNode claude, bool mayMint, CommandLog log)
    {
        var declaredUrl = GetString(claude, "url");
        var declaredValues = DevToolConfiguration.ReadHeaders(claude, "headers")
            .Select(header => header.Value)
            .Concat(QueryStringValues(declaredUrl))
            .ToArray();

        foreach (var value in declaredValues.Where(value => !value.Contains("${", StringComparison.Ordinal)))
        {
            log.Redact(value);
        }

        if (mayMint
            && _mcp is { Enabled: true } mcp
            && declaredValues.Append(declaredUrl).Any(value => value.Contains(McpPlaceholders.TokenName, StringComparison.Ordinal)))
        {
            log.Redact(mcp.EnsureToken());
        }
    }

    /// <summary>What a URL carries after its <c>?</c>, value by value.
    ///
    /// <para>Split by hand rather than through <see cref="Uri"/>, because the
    /// string this is handed is routinely not a URL yet: it is the catalog's
    /// spelling, <c>${BACKLOG_MCP_PORT}</c> and all, which no parser accepts.
    /// Nothing here needs it to be well formed — what is wanted is the text after
    /// each <c>=</c>, and a string with no <c>?</c> in it yields nothing.</para></summary>
    private static IEnumerable<string> QueryStringValues(string url)
    {
        var query = url.IndexOf('?', StringComparison.Ordinal);

        if (query < 0)
        {
            yield break;
        }

        foreach (var pair in url[(query + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = pair.IndexOf('=', StringComparison.Ordinal);

            if (separator >= 0 && separator + 1 < pair.Length)
            {
                yield return pair[(separator + 1)..];
            }
        }
    }

    /// <summary>
    /// The URL and the headers one HTTP registration is made with, expanded for
    /// this machine — or the sentence that says why it cannot be.
    /// </summary>
    /// <remarks>
    /// <para>This is the one call site in the class that expands a token, and it
    /// refuses outright for a host with no endpoint and for a machine that has
    /// the server switched off: registering either would write a secret to a
    /// machine for a server nobody asked to run.</para>
    ///
    /// <para>The mint itself is not here. It happens in
    /// <see cref="RedactRegistrationSecrets"/>, at the top of the apply, because
    /// the log masks at record time and by the time this runs two commands have
    /// already gone into it. What this adds is the other direction: whatever the
    /// expansion <em>produced</em> is handed to the log too, so a header whose
    /// value came out of a placeholder this file does not special-case is masked
    /// on the <c>claude mcp add</c> line that carries it.</para>
    ///
    /// <para>An unresolved placeholder fails the row rather than being registered
    /// around it. A URL still carrying <c>${</c> is not a URL, and the add would
    /// succeed: what fails is every later call, on a machine, with nothing
    /// pointing at why.</para>
    /// </remarks>
    private (DevToolActionResult? Failure, string Url, IReadOnlyList<KeyValuePair<string, string>> Headers) RegisterableEndpoint(
        string name,
        JsonNode claude,
        CommandLog log)
    {
        var none = Array.Empty<KeyValuePair<string, string>>();

        if (_mcp is not { } mcp)
        {
            return (DevToolActionResult.Failed($"'{name}' was not registered. {EndpointUnresolvable}"), string.Empty, none);
        }

        if (!mcp.Enabled)
        {
            return (
                DevToolActionResult.Failed(
                    $"'{name}' was not registered: the MCP server is switched off on this machine, and registering it would "
                    + "create a token for a server nobody has asked to run. Switch it on first."),
                string.Empty,
                none);
        }

        var declaredUrl = GetString(claude, "url");
        var declaredHeaders = DevToolConfiguration.ReadHeaders(claude, "headers");

        var resolve = McpPlaceholders.Applying(mcp);
        var url = McpPlaceholders.Expand(declaredUrl, resolve);

        if (!url.Resolved)
        {
            return (DevToolActionResult.Failed($"'{name}' was not registered. {url.Problem}"), string.Empty, none);
        }

        if (string.IsNullOrWhiteSpace(url.Text))
        {
            return (DevToolActionResult.Failed($"'{name}' has no claude.url to register."), string.Empty, none);
        }

        // Whatever the query string expanded into is a credential on the same
        // terms the declared one was — ?token=${…} is the shape that reaches
        // here still spelled as a placeholder.
        foreach (var value in QueryStringValues(url.Text))
        {
            log.Redact(value);
        }

        var headers = new List<KeyValuePair<string, string>>(declaredHeaders.Count);
        foreach (var (header, value) in declaredHeaders)
        {
            var expanded = McpPlaceholders.Expand(value, resolve);

            if (!expanded.Resolved)
            {
                return (DevToolActionResult.Failed($"'{name}' was not registered. {expanded.Problem}"), string.Empty, none);
            }

            // The expansion, told to the log before the line that carries it is
            // recorded. The declared spelling was seeded at the top of the apply;
            // this is what that spelling turned into on this machine, which is
            // the string that actually goes onto the command line.
            log.Redact(expanded.Text);

            headers.Add(new KeyValuePair<string, string>(header, expanded.Text));
        }

        return (null, url.Text, headers);
    }

    /// <summary>
    /// Everything the automated providers know about this machine, read once per
    /// listing.
    ///
    /// <para>Two winget calls and one <c>code</c> call answer for every row of
    /// their provider, which is the whole reason this exists as a batch: a
    /// <c>--id</c> per row would be one process launch per row, and thirty
    /// launches is a refresh people stop pressing.</para>
    ///
    /// <para>Absent is a normal value throughout. A machine with no winget, no VS
    /// Code, or no network for the marketplace still lists every row it can and
    /// says on the status line what it could not check.</para>
    /// </summary>
    private sealed record ApplicationInventory(
        bool WingetPresent,
        IReadOnlyDictionary<string, DevToolOutput.WingetPackage> WingetPackages,
        IReadOnlyDictionary<string, string> WingetUpgrades,
        IReadOnlyDictionary<string, string> WingetManifests,
        bool VsCodePresent,
        IReadOnlyDictionary<string, string> Extensions,
        IReadOnlyDictionary<string, string> ExtensionVersions,
        IReadOnlyDictionary<string, CommandProbe> Probes);

    /// <summary>What one declared command answered.
    ///
    /// <para><paramref name="Detected"/> is separate from the version because
    /// several probes have no version to give: <c>fsutil devdrv query</c> prints
    /// prose and <c>git config --global pull.rebase</c> prints <c>true</c>. A row
    /// that had to invent a number to say "yes" would then be comparing that
    /// invention against itself and reporting itself up to date.</para></summary>
    private sealed record CommandProbe(bool Detected, string InstalledVersion, string AvailableVersion);

    private static readonly IReadOnlyDictionary<string, DevToolOutput.WingetPackage> NoPackages =
        new Dictionary<string, DevToolOutput.WingetPackage>(StringComparer.OrdinalIgnoreCase);

    private static readonly IReadOnlyDictionary<string, CommandProbe> NoProbes =
        new Dictionary<string, CommandProbe>(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc cref="ApplicationInventory" />
    private static async Task<ApplicationInventory> ReadApplicationInventoryAsync(
        IReadOnlyList<DevToolApplication> applications,
        CommandLog log,
        List<string> messages,
        CancellationToken ct)
    {
        var wingetPresent = false;
        var wingetPackages = NoPackages;
        var wingetUpgrades = NoVersions;
        var wingetManifests = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var wingetRows = applications.Where(application => application.Provider is DevToolProvider.Winget).ToArray();
        if (wingetRows.Length > 0)
        {
            wingetPresent = await ResolveWingetCliAsync(log, ct).ConfigureAwait(false) is not null;

            if (!wingetPresent)
            {
                messages.Add(WingetCliMissing);
            }
            else
            {
                // The local source index, before either listing reads it. Both of
                // them answer out of that index and nothing was refreshing it, so
                // a machine whose index predated a package's new manifest reported
                // the old version in the Available column and dropped the package
                // out of the upgrade listing entirely.
                var sourceUpdate = await RunAsync(DevToolCommands.WingetSourceUpdate(), log, ct).ConfigureAwait(false);
                if (sourceUpdate.ExitCode != 0)
                {
                    messages.Add("The winget source could not be refreshed, so package versions may be stale.");
                }

                var list = await RunAsync(DevToolCommands.WingetList(), log, ct).ConfigureAwait(false);
                if (list.ExitCode == 0)
                {
                    wingetPackages = DevToolOutput.ParseWingetList(list.Output);
                }
                else
                {
                    messages.Add("The winget package list could not be read.");
                }

                // The second call is what makes the Available column real. A bare
                // list only prints an Available cell for a package that has an
                // upgrade, so a package missing from *this* listing is a package
                // that is current — which is the answer for almost every row and
                // costs no further calls.
                var upgrade = await RunAsync(DevToolCommands.WingetUpgrade(), log, ct).ConfigureAwait(false);
                if (upgrade.ExitCode == 0)
                {
                    wingetUpgrades = DevToolOutput.ParseWingetUpgrade(upgrade.Output);
                }

                // What is left is the packages this machine does not have, and the
                // only way to learn what version it would get is one call each.
                var pending = wingetRows
                    .Where(application => application.Enabled && !wingetPackages.ContainsKey(application.Id))
                    .Select(application => application.Id)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                foreach (var id in pending.Take(WingetShowBudget))
                {
                    var show = await RunAsync(DevToolCommands.WingetShow(id), log, ct).ConfigureAwait(false);
                    if (show.ExitCode == 0 && DevToolOutput.ParseWingetShowVersion(show.Output) is { } manifestVersion)
                    {
                        wingetManifests[id] = manifestVersion;
                    }
                }

                if (pending.Length > WingetShowBudget)
                {
                    // Named rather than silently dropped. A truncated lookup that
                    // says nothing is indistinguishable from a lookup that failed,
                    // and both leave the same "unknown" in the column.
                    messages.Add(
                        $"Only the first {WingetShowBudget} missing winget packages were looked up this refresh; "
                        + $"{string.Join(", ", pending.Skip(WingetShowBudget))} still read as version unknown.");
                }
            }
        }

        var vsCodePresent = false;
        var extensions = NoVersions;
        var extensionVersions = NoVersions;

        var extensionRows = applications.Where(application => application.Provider is DevToolProvider.VsCodeExtension).ToArray();
        if (extensionRows.Length > 0)
        {
            vsCodePresent = await ResolveVsCodeCliAsync(log, ct).ConfigureAwait(false) is not null;

            if (!vsCodePresent)
            {
                messages.Add(VsCodeCliMissing);
            }
            else
            {
                var list = await RunAsync(DevToolCommands.VsCodeExtensionList(), log, ct).ConfigureAwait(false);
                if (list.ExitCode == 0)
                {
                    extensions = DevToolOutput.ParseVsCodeExtensionList(list.Output);
                }
                else
                {
                    messages.Add("The VS Code extension list could not be read.");
                }
            }

            // Asked for even with no CLI here, because the two answer different
            // questions and only one of them needs VS Code on this machine.
            extensionVersions = await ReadMarketplaceVersionsAsync(
                extensionRows.Where(application => application.Enabled).Select(application => application.Id),
                log,
                ct).ConfigureAwait(false);
        }

        var probes = new Dictionary<string, CommandProbe>(StringComparer.OrdinalIgnoreCase);
        foreach (var application in applications)
        {
            // A disabled row is a row this machine has said it does not want, and
            // running its probe would be spending a process to answer a question
            // nobody asked.
            if (!application.Enabled)
            {
                continue;
            }

            // A command row's detect always runs — it is the only thing that can
            // answer for that row at all.
            //
            // The optional cross-check on the other providers runs only when the
            // batched listing came up empty for that row, and that gate is the
            // difference between four process launches per refresh and twenty:
            // most of this catalog declares a probe, and re-asking PATH about a
            // package winget has just reported by version buys nothing. What it
            // does buy is the case it was added for — a portable install that
            // answers on PATH and is registered nowhere, which the package manager
            // reports as simply missing.
            var spec = application.Provider switch
            {
                DevToolProvider.Command => application.Detect ?? application.Probe,
                DevToolProvider.Winget => wingetPackages.ContainsKey(application.Id) ? null : application.Probe,
                DevToolProvider.VsCodeExtension => extensions.ContainsKey(application.Id) ? null : application.Probe,
                DevToolProvider.Manual => null
            };

            if (spec is null)
            {
                continue;
            }

            // Only a command row can declare its own available side, and only
            // its own detect is read against it — a winget row's cross-check
            // probe has a package manager answering its Available column already.
            var available = application.Provider is DevToolProvider.Command ? application.Available : null;

            probes[application.Id] = await ProbeCommandAsync(spec, available, log, ct).ConfigureAwait(false);
        }

        return new ApplicationInventory(
            wingetPresent,
            wingetPackages,
            wingetUpgrades,
            wingetManifests,
            vsCodePresent,
            extensions,
            extensionVersions,
            probes.Count == 0 ? NoProbes : probes);
    }

    /// <summary>Whether this machine has winget, asked the cheapest way there is.
    /// Null rather than an assumption, for the same reason the Copilot CLI is
    /// resolved before anything is asked of it: every winget row would otherwise
    /// report Windows' "file not found" as though it were that package's own
    /// problem.</summary>
    private static async Task<string?> ResolveWingetCliAsync(CommandLog log, CancellationToken ct)
    {
        var probe = await RunAsync(DevToolCommands.WingetVersion(), log, ct).ConfigureAwait(false);

        return probe.ExitCode == 0 ? DevToolCommands.WingetVersion().Command : null;
    }

    /// <inheritdoc cref="ResolveWingetCliAsync" />
    /// <remarks><c>code</c> is not an executable — it is a <c>.cmd</c> shim, which
    /// is why this goes through the shell like every other <c>code</c> call. Run
    /// directly it does not report "not installed", it throws.</remarks>
    private static async Task<string?> ResolveVsCodeCliAsync(CommandLog log, CancellationToken ct)
    {
        var probe = await RunAsync(DevToolCommands.VsCodeVersion(), log, ct).ConfigureAwait(false);

        return probe.ExitCode == 0 ? DevToolCommands.VsCodeVersion().Command : null;
    }

    /// <summary>
    /// The latest published version of every catalog extension, in one call.
    ///
    /// <para>Best effort by construction. There is no CLI that can answer this —
    /// no <c>--list-outdated</c>, no <c>--check-updates</c>, no JSON — so it is
    /// this endpoint or nothing, and "nothing" has to be an ordinary outcome
    /// rather than a listing that failed. An empty result leaves the column
    /// reading "unknown", which the pane renders as "Version unknown" instead of
    /// a "Up to date" nobody checked.</para>
    /// </summary>
    private static async Task<IReadOnlyDictionary<string, string>> ReadMarketplaceVersionsAsync(
        IEnumerable<string> ids,
        CommandLog log,
        CancellationToken ct)
    {
        var wanted = ids
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (wanted.Length == 0)
        {
            return NoVersions;
        }

        var description = $"POST {DevToolCommands.MarketplaceQueryUrl} ({wanted.Length} extension(s))";

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, DevToolCommands.MarketplaceQueryUrl)
            {
                Content = new StringContent(DevToolCommands.MarketplaceExtensionQuery(wanted), Encoding.UTF8, "application/json")
            };

            // Added without validation because the gallery's api-version lives in
            // the Accept header itself, and the parsed header type rejects the
            // parameter form it needs.
            request.Headers.TryAddWithoutValidation("Accept", DevToolCommands.MarketplaceQueryAccept);

            using var response = await Marketplace.SendAsync(request, ct).ConfigureAwait(false);
            var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                log.RecordStep(description, (int)response.StatusCode, json);
                return NoVersions;
            }

            var versions = DevToolOutput.ParseMarketplaceExtensionVersions(json);
            log.RecordStep(description, 0, $"{versions.Count} of {wanted.Length} extension(s) answered with a stable version.");

            return versions;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException or InvalidOperationException or UriFormatException)
        {
            log.RecordStep(description, CommandNotFound, exception.Message);

            return NoVersions;
        }
    }

    /// <summary>
    /// What one declared command says about this machine.
    ///
    /// <para>A non-zero exit is not a failure here, it is the answer "no".
    /// <c>git config --global pull.rebase</c> for a key nobody has set exits 1
    /// with an empty stdout, and surfacing that as a broken row would put an error
    /// on the screen for a setting that is simply not configured — which is
    /// exactly what the row exists to report.</para>
    ///
    /// <para>An <c>expect</c> answers from the text instead, because the probes
    /// that need it print prose rather than a version. Both version columns stay
    /// the no-version sentinel: there is no number, and inventing one would make
    /// the row equal to itself and read as up to date whether or not the machine
    /// had done the thing.</para>
    /// </summary>
    private static async Task<CommandProbe> ProbeCommandAsync(
        DevToolCommandSpec spec,
        DevToolCommandSpec? available,
        CommandLog log,
        CancellationToken ct)
    {
        var result = await RunAsync(spec, log, ct).ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(spec.Expect))
        {
            return new CommandProbe(
                result.Output.Contains(spec.Expect, StringComparison.OrdinalIgnoreCase),
                DevToolOutput.NoVersion,
                DevToolOutput.NoVersion);
        }

        if (available is not null)
        {
            return await ProbeDeclaredAvailableAsync(result, available, log, ct).ConfigureAwait(false);
        }

        if (result.ExitCode != 0)
        {
            return new CommandProbe(false, DevToolOutput.NotInstalled, DevToolOutput.Unknown);
        }

        // Unknown and not the installed version: a declared command has no
        // published version anywhere to compare against, and copying the installed
        // one into this column would announce "up to date" about a lookup that
        // never happened.
        return new CommandProbe(
            true,
            DevToolOutput.ParseVersionProbe(result.Output) ?? DevToolOutput.Installed,
            DevToolOutput.Unknown);
    }

    /// <summary>
    /// The row that answers its own Available column
    /// (<see cref="DevToolApplication.Available"/>).
    ///
    /// <para>Both sides are read as printed rather than through the version
    /// reader, and the available command runs even when detect failed: a service
    /// that is not reachable is "not installed" opposite the build it should be
    /// running, which is exactly the row that earns an Install. An available
    /// command that fails or prints nothing leaves the column "unknown", which
    /// the pane renders as a lookup that did not happen rather than as a row
    /// that is current.</para>
    /// </summary>
    private static async Task<CommandProbe> ProbeDeclaredAvailableAsync(
        CommandResult detect,
        DevToolCommandSpec available,
        CommandLog log,
        CancellationToken ct)
    {
        var wanted = await RunAsync(available, log, ct).ConfigureAwait(false);
        var availableVersion = wanted.ExitCode == 0
            ? DevToolOutput.ParseVerbatimProbe(wanted.Output) ?? DevToolOutput.Unknown
            : DevToolOutput.Unknown;

        if (detect.ExitCode != 0)
        {
            return new CommandProbe(false, DevToolOutput.NotInstalled, availableVersion);
        }

        return new CommandProbe(
            true,
            DevToolOutput.ParseVerbatimProbe(detect.Output) ?? DevToolOutput.Installed,
            availableVersion);
    }

    /// <summary>One application entry as one row, through whichever mechanism it
    /// declared. Exhaustive on purpose: a provider added to the enum and not
    /// handled here should fail to compile rather than fall into whichever arm
    /// happened to be last.</summary>
    private static DevToolInfo DescribeApplication(DevToolApplication application, ApplicationInventory inventory) =>
        application.Provider switch
        {
            DevToolProvider.Winget => DescribeWingetApplication(application, inventory),
            DevToolProvider.VsCodeExtension => DescribeExtensionApplication(application, inventory),
            DevToolProvider.Command => DescribeCommandApplication(application, inventory),
            DevToolProvider.Manual => DescribeManualApplication(application)
        };

    private static DevToolInfo DescribeWingetApplication(DevToolApplication application, ApplicationInventory inventory)
    {
        var notes = ApplicationNotes(application);

        if (!inventory.WingetPresent)
        {
            notes.Add(WingetCliMissing);

            // The PATH cross-check is the only answer left, and on a machine with
            // no package manager it is a real one: a row whose command answers is
            // a row this machine has, however it got there. Without this the probe
            // ran and its answer was thrown away.
            if (inventory.Probes.GetValueOrDefault(application.Id) is { } fallback)
            {
                return ApplicationRow(
                    application,
                    fallback.Detected,
                    fallback.InstalledVersion,
                    DevToolOutput.Unknown,
                    Installable: false,
                    notes,
                    fallback.Detected ? $"Answers on PATH as '{application.Probe?.Command}'" : "Not installed");
            }

            return ApplicationRow(application, false, DevToolOutput.Unknown, DevToolOutput.Unknown, Installable: false, notes, "Nothing could be checked");
        }

        var package = inventory.WingetPackages.GetValueOrDefault(application.Id);
        var installed = package is not null;

        // winget prints a literal "Unknown" in the Version column for packages
        // whose installed version it cannot read — MSIX and click-to-run entries
        // mostly. The package is there; only its number is not.
        var installedVersion = package?.InstalledVersion is { Length: > 0 } version
            && !version.Equals(DevToolOutput.Unknown, StringComparison.OrdinalIgnoreCase)
                ? version
                : installed ? DevToolOutput.Installed : DevToolOutput.NotInstalled;

        var availableVersion =
            inventory.WingetUpgrades.GetValueOrDefault(application.Id)
            ?? package?.AvailableVersion
            ?? inventory.WingetManifests.GetValueOrDefault(application.Id)
            // Installed and absent from the upgrade listing is winget saying it is
            // current. Leaving this unknown instead would put "Version unknown" on
            // every up-to-date row on the machine.
            ?? (installed ? installedVersion : DevToolOutput.Unknown);

        if (application.DetectOnly)
        {
            notes.Add("Install this one by hand: winget knows it and cannot install it unattended.");
        }

        AddProbeDisagreement(application, inventory, installed, notes);

        return ApplicationRow(
            application,
            installed,
            installedVersion,
            availableVersion,
            !application.DetectOnly,
            notes,
            DescribeStatus(application.Enabled, installed, DevToolInfo.VersionDiffers(installedVersion, availableVersion), "application"));
    }

    private static DevToolInfo DescribeExtensionApplication(DevToolApplication application, ApplicationInventory inventory)
    {
        var notes = ApplicationNotes(application);

        if (!inventory.VsCodePresent)
        {
            notes.Add(VsCodeCliMissing);

            return ApplicationRow(application, false, DevToolOutput.Unknown, DevToolOutput.Unknown, Installable: false, notes, "Nothing could be checked");
        }

        // Both lookups are case-insensitive dictionaries, which is the whole trick:
        // the marketplace and the catalog spell it ms-vscode.PowerShell and the CLI
        // answers ms-vscode.powershell. An ordinal match finds neither in the other
        // and every extension reads as missing.
        var installedVersion = inventory.Extensions.GetValueOrDefault(application.Id);
        var installed = installedVersion is not null;
        var availableVersion = inventory.ExtensionVersions.GetValueOrDefault(application.Id) ?? DevToolOutput.Unknown;

        if (application.DetectOnly)
        {
            notes.Add("Install this one by hand.");
        }

        AddProbeDisagreement(application, inventory, installed, notes);

        return ApplicationRow(
            application,
            installed,
            installedVersion ?? DevToolOutput.NotInstalled,
            availableVersion,
            !application.DetectOnly,
            notes,
            DescribeStatus(application.Enabled, installed, DevToolInfo.VersionDiffers(installedVersion ?? DevToolOutput.NotInstalled, availableVersion), "extension"));
    }

    /// <summary>
    /// One declared-command row, which is two rows wearing one provider: a tool
    /// that says how to install itself, and a checklist item that declined to.
    ///
    /// <para>The checklist item is the reason the absence of an <c>install</c> is
    /// meaningful rather than incomplete. "Dev Drive configured" and "the NuGet
    /// cache is redirected" are worth reporting and are not ours to press a button
    /// about, and a row that offered one would run nothing and call the nothing a
    /// failure.</para>
    /// </summary>
    private static DevToolInfo DescribeCommandApplication(DevToolApplication application, ApplicationInventory inventory)
    {
        var notes = ApplicationNotes(application);
        var checklist = application.Install is null;

        if (application.Detect is null)
        {
            notes.Add("This entry declares no detect command, so nothing was checked.");

            return ApplicationRow(application, false, DevToolOutput.Unknown, DevToolOutput.Unknown, Installable: false, notes, "Nothing could be checked");
        }

        if (inventory.Probes.GetValueOrDefault(application.Id) is not { } probe)
        {
            // Only reachable for a row this machine has switched off, which is
            // why the headline is the switch rather than the probe.
            return ApplicationRow(application, false, DevToolOutput.NotInstalled, DevToolOutput.Unknown, Installable: false, notes, "Disabled in config");
        }

        if (checklist)
        {
            notes.Add("No install command: this one is a checklist item.");
        }

        var headline = checklist
            ? probe.Detected ? "Checklist item: done" : "Checklist item: not done yet"
            : DescribeStatus(application.Enabled, probe.Detected, DevToolInfo.VersionDiffers(probe.InstalledVersion, probe.AvailableVersion), "application");

        return ApplicationRow(
            application,
            probe.Detected,
            probe.InstalledVersion,
            probe.AvailableVersion,
            !checklist && !application.DetectOnly,
            notes,
            headline);
    }

    /// <summary>
    /// A row nothing can honestly answer: a sign-in, a menu entry that renders, a
    /// first run without errors.
    ///
    /// <para>Nothing runs for it, ever, and its detected state is the
    /// acknowledgement and nothing else. That is the distinction the whole manual
    /// provider exists to hold: not "this machine has it" but "somebody said so on
    /// this machine", and drawing the two the same way is what would make the
    /// other thirty rows worth less.</para>
    /// </summary>
    private static DevToolInfo DescribeManualApplication(DevToolApplication application)
    {
        var notes = ApplicationNotes(application);

        return ApplicationRow(
            application,
            application.Acknowledged,
            application.Acknowledged ? "confirmed by hand" : DevToolOutput.NotInstalled,
            // No version on either side. There is nothing to look up, and a number
            // here would invite a comparison that means nothing.
            DevToolOutput.NoVersion,
            // Never, in either direction. There is nothing to install and nothing
            // that could report having installed it; the row's one act is the tick,
            // which goes through AcknowledgeAsync and not through an Install button
            // that would have to mean two things at once.
            Installable: false,
            notes,
            application.Acknowledged ? "Confirmed by hand" : "Nothing can check this — confirm it by hand");
    }

    /// <summary>What a row has to say beyond its status: the catalog's own note,
    /// and a provider this version has never heard of.</summary>
    private static List<string> ApplicationNotes(DevToolApplication application)
    {
        var notes = new List<string>();

        if (!string.IsNullOrWhiteSpace(application.Note))
        {
            notes.Add(application.Note.Trim());
        }

        if (!application.ProviderRecognised)
        {
            // Reported rather than dropped. The entry named a mechanism nobody
            // here knows, and a row that simply was not there would hide a typo in
            // a hand-edited file behind an inventory that looked complete.
            notes.Add($"The catalog asks for a '{application.DeclaredProvider}' provider, which this version does not know, so nothing was run for it.");
        }

        return notes;
    }

    /// <summary>The optional PATH cross-check, which is only interesting when it
    /// disagrees. The package manager reports what is registered and the command
    /// reports what answers, which are not the same question — and the two
    /// disagreeing is worth seeing rather than picking a winner between.</summary>
    private static void AddProbeDisagreement(DevToolApplication application, ApplicationInventory inventory, bool installed, List<string> notes)
    {
        // Only asked of a row the package manager could not find, so the only
        // disagreement reachable here is the one worth reporting: something that
        // answers on PATH and is registered nowhere.
        if (installed || application.Probe is not { } probeSpec || inventory.Probes.GetValueOrDefault(application.Id) is not { Detected: true })
        {
            return;
        }

        notes.Add($"'{probeSpec.Command}' answers on PATH but the package manager does not list it");
    }

    /// <summary>The shape every application row shares, so the four describers
    /// above differ only in the answers they found and not in how a row is
    /// built.</summary>
    private static DevToolInfo ApplicationRow(
        DevToolApplication application,
        bool installed,
        string installedVersion,
        string availableVersion,
        bool Installable,
        IReadOnlyList<string> notes,
        string headline) =>
        new(
            application.Key,
            DevToolKind.Application,
            application.Name,
            // The provider stands in for the source column, because for an
            // application that is what "where does this come from" means.
            DevToolConfiguration.ProviderName(application.Provider),
            application.Enabled,
            installed,
            installedVersion,
            availableVersion,
            notes.Count == 0 ? headline : $"{headline} · {string.Join(" · ", notes)}")
        {
            // Not a host's tool. Copilot and Claude are the two AI hosts a plugin
            // is installed into, and an application is installed into the machine —
            // claiming either would put an Install for it on a host that has never
            // heard of it.
            Hosts = DevToolHosts.None,
            Installable = Installable,
            Acknowledged = application.Acknowledged,
            ConfirmedByHand = application.Provider is DevToolProvider.Manual,

            // Whatever the entry filed itself under, unchanged. The pane groups on
            // it and does not interpret it: a heading this version has never seen
            // is a heading somebody added to the catalog, not an error.
            Group = application.Group
        };

    /// <summary>
    /// One application row's action, through whichever mechanism it declared.
    /// </summary>
    private static async Task<DevToolActionResult> ApplyApplicationAsync(
        JsonNode node,
        CommandLog log,
        CancellationToken ct)
    {
        if (node is not JsonObject entry || DevToolConfiguration.ReadApplication(entry) is not { } application)
        {
            return DevToolActionResult.Failed("That application entry has no id.");
        }

        if (!application.Enabled)
        {
            // Disabling is a statement about this machine, not an uninstall. There
            // is no safe general "remove" here — a winget package may be something
            // three other things depend on — so the row stops asking for it and
            // leaves what is there alone.
            return DevToolActionResult.Ok($"{application.Name} is switched off on this machine; nothing was installed or removed.");
        }

        return application.Provider switch
        {
            DevToolProvider.Winget => await ApplyWingetApplicationAsync(application, log, ct).ConfigureAwait(false),
            DevToolProvider.VsCodeExtension => await ApplyExtensionApplicationAsync(application, log, ct).ConfigureAwait(false),
            DevToolProvider.Command => await ApplyCommandApplicationAsync(application, log, ct).ConfigureAwait(false),
            // Nothing to run and nothing to record. Enabling a manual row says
            // this machine wants the thing done; saying it has been done is a
            // separate act with a port method of its own
            // (<see cref="AcknowledgeAsync"/>), because a tick that arrived
            // through Update could never be taken back — the row had no action
            // left once it was ticked.
            DevToolProvider.Manual => DevToolActionResult.Ok(
                $"{application.Name} is confirmed by hand; tick it on the row when it is done.")
        };
    }

    private static async Task<DevToolActionResult> ApplyWingetApplicationAsync(DevToolApplication application, CommandLog log, CancellationToken ct)
    {
        if (application.DetectOnly)
        {
            return DevToolActionResult.Failed($"{application.Name} cannot be installed unattended; install it by hand.");
        }

        if (await ResolveWingetCliAsync(log, ct).ConfigureAwait(false) is null)
        {
            return DevToolActionResult.Failed(WingetCliMissing);
        }

        var spec = DevToolCommands.WingetInstall(application.Id, application.InstallerType);
        var result = await RunAsync(spec, log, ct, InstallTimeout).ConfigureAwait(false);

        return result.ExitCode == 0
            ? DevToolActionResult.Ok($"{application.Id} installed.")
            : DevToolActionResult.Failed(DescribeInstallFailure(application.Id, spec, result, log));
    }

    private static async Task<DevToolActionResult> ApplyExtensionApplicationAsync(DevToolApplication application, CommandLog log, CancellationToken ct)
    {
        if (application.DetectOnly)
        {
            return DevToolActionResult.Failed($"{application.Name} is marked as installed by hand.");
        }

        if (await ResolveVsCodeCliAsync(log, ct).ConfigureAwait(false) is null)
        {
            return DevToolActionResult.Failed(VsCodeCliMissing);
        }

        // One verb for install and update both. An extension that is already there
        // exits 0 with a sentence saying so, so there is nothing for the caller to
        // decide between; a missing one exits 1 with "not found".
        var spec = DevToolCommands.VsCodeInstallExtension(application.Id);
        var result = await RunAsync(spec, log, ct, InstallTimeout).ConfigureAwait(false);

        return result.ExitCode == 0
            ? DevToolActionResult.Ok($"{application.Id} installed or already current.")
            : DevToolActionResult.Failed(log.Failure(application.Id, result));
    }

    private static async Task<DevToolActionResult> ApplyCommandApplicationAsync(DevToolApplication application, CommandLog log, CancellationToken ct)
    {
        if (application.Install is not { } install)
        {
            return DevToolActionResult.Ok($"{application.Name} is a checklist item, so there is nothing to install.");
        }

        if (application.DetectOnly)
        {
            return DevToolActionResult.Failed($"{application.Name} is marked as installed by hand.");
        }

        var result = await RunAsync(install, log, ct, InstallTimeout).ConfigureAwait(false);

        return result.ExitCode == 0
            ? DevToolActionResult.Ok($"{application.Name} installed.")
            : DevToolActionResult.Failed(DescribeInstallFailure(application.Name, install, result, log));
    }

    /// <summary>
    /// A failed install, said in a form somebody can act on.
    ///
    /// <para>An elevation failure gets the command spelled out beside it. There is
    /// no <c>runas</c> path here and there will not be:
    /// <see cref="ProcessStartInfo.UseShellExecute"/> is false, which forbids a
    /// verb, and an elevated second process would lose the captured stdout that is
    /// the entire reason the pane shows a command log. So the honest answer is the
    /// exact line to paste into an admin shell.</para>
    /// </summary>
    /// <param name="log">The action's own log, because the sentence is built out
    /// of what the process printed and only the log knows what may not be
    /// repeated out of it.</param>
    private static string DescribeInstallFailure(string name, DevToolCommandSpec spec, CommandResult result, CommandLog log)
    {
        var failure = log.Failure(name, result);

        return NeedsElevation(result)
            // Through the log's own describer rather than joined here, for the
            // reason the failure sentence above goes through it: this is a
            // command line, and a command line is one of the two places a
            // credential is written down.
            ? $"{failure} This one needs an elevated shell: {log.Describe(spec.FileName, spec.LaunchArguments)}"
            : failure;
    }

    /// <summary>Whether a failure was really about privileges. Read from the text
    /// rather than the exit code because winget hands through the installer's own
    /// code, and every installer numbers this differently.</summary>
    private static bool NeedsElevation(CommandResult result)
    {
        var text = $"{result.Output} {result.Error}";

        return text.Contains("elevat", StringComparison.OrdinalIgnoreCase)
            || text.Contains("administrator", StringComparison.OrdinalIgnoreCase)
            || text.Contains("access is denied", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// What this machine's Claude desktop app is and where its config lives.
    /// </summary>
    /// <param name="Installed">Whether the app is here at all. A config file being
    /// present proves it; when there is none the MSIX package is asked directly,
    /// because the classic uninstall registry returns nothing for a packaged
    /// app.</param>
    /// <param name="ConfigPath">The file to read and write — the one that exists,
    /// or the one that would be created.</param>
    /// <param name="Config">The document as it is on disk, or nothing when there
    /// is no file. Nothing is also what an unreadable file gives, and
    /// <paramref name="Error"/> is how the two are told apart: a write over a file
    /// that could not be parsed would discard settings this app never owned.</param>
    private sealed record ClaudeDesktopState(bool Installed, string ConfigPath, JsonObject? Config, string? Error);

    /// <inheritdoc cref="ClaudeDesktopState" />
    private static async Task<ClaudeDesktopState> ResolveClaudeDesktopAsync(CommandLog log, CancellationToken ct)
    {
        var candidates = DevToolClaudeDesktopConfig.ConfigPathCandidates(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));

        if (candidates.FirstOrDefault(File.Exists) is not { } path)
        {
            // No config is not the same as no app: a freshly installed Claude
            // Desktop that has never registered a server has no such file, and
            // reading that as "not installed" would refuse a registration the
            // machine is perfectly able to take.
            var installed = await ProbeClaudeDesktopPackageAsync(log, ct).ConfigureAwait(false);

            return new ClaudeDesktopState(installed, candidates[0], null, installed ? null : ClaudeDesktopMissing);
        }

        try
        {
            var text = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);

            return new ClaudeDesktopState(true, path, JsonNode.Parse(text) as JsonObject, null);
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            return new ClaudeDesktopState(true, path, null, $"The Claude Desktop config at {path} could not be read: {exception.Message}");
        }
    }

    /// <summary>Whether the MSIX package is installed.
    ///
    /// <para>Asked of the package manager rather than of the disk. Claude Desktop
    /// ships as MSIX: it writes no classic uninstall registry key, declares no
    /// execution alias so nothing of it is on PATH, and its files live under
    /// <c>C:\Program Files\WindowsApps</c>, which is ACL-locked — a walk of that
    /// folder does not fail, it hangs.</para></summary>
    private static async Task<bool> ProbeClaudeDesktopPackageAsync(CommandLog log, CancellationToken ct)
    {
        var probe = await RunAsync(
            "powershell",
            ["-NoProfile", "-NonInteractive", "-Command", "(Get-AppxPackage -Name Claude).Version"],
            log,
            ct).ConfigureAwait(false);

        return probe.ExitCode == 0 && !string.IsNullOrWhiteSpace(probe.Output);
    }

    /// <summary>Where an entry says what the desktop app should register.
    /// <c>claudeDesktop</c> when it has one, and whatever registers it with every
    /// other host otherwise — the command is usually the same one the CLI is
    /// given, and making every entry say it twice would be two places for it to
    /// drift. Which section that is, and which wins for an entry carrying both, is
    /// <see cref="DevToolConfiguration.McpRegistrationSection"/>'s one
    /// answer.</summary>
    private static JsonNode? ClaudeDesktopSection(JsonNode server) =>
        server["claudeDesktop"] ?? DevToolConfiguration.McpRegistrationSection(server);

    /// <summary>The registration as one line, which is what the row compares. The
    /// arguments are part of it: a registration pointing at the right executable
    /// with the wrong arguments is a registration that does not work, and the
    /// command alone cannot say so.</summary>
    private static string ClaudeDesktopCommandLine(JsonNode section) =>
        string.Join(' ', new[] { GetString(section, "command") }.Concat(ClaudeServerArgs(section))).Trim();

    /// <summary>
    /// What the desktop app's config says about one server.
    ///
    /// <para>An absent <c>mcpServers</c> is zero servers and not a broken file —
    /// the app omits the property entirely until something registers — so every
    /// machine that has never used one reads as "not registered" rather than as an
    /// error.</para>
    /// </summary>
    private static DevToolHostState DescribeClaudeDesktopRegistration(string serverName, string commandLine, ClaudeDesktopState desktop)
    {
        if (!desktop.Installed)
        {
            return new DevToolHostState(DevToolHosts.ClaudeDesktop, Installed: false, DevToolOutput.NotInstalled, commandLine, ClaudeDesktopMissing);
        }

        if (desktop.Config is null && desktop.Error is { Length: > 0 })
        {
            return new DevToolHostState(DevToolHosts.ClaudeDesktop, Installed: false, DevToolOutput.Unknown, commandLine, desktop.Error);
        }

        if (DevToolClaudeDesktopConfig.ReadServer(desktop.Config, serverName) is not { } registered)
        {
            return new DevToolHostState(
                DevToolHosts.ClaudeDesktop,
                Installed: false,
                DevToolOutput.NotInstalled,
                commandLine,
                $"Not registered with Claude Desktop as '{serverName}'");
        }

        return new DevToolHostState(
            DevToolHosts.ClaudeDesktop,
            Installed: true,
            string.IsNullOrWhiteSpace(registered.CommandLine) ? DevToolOutput.Unknown : registered.CommandLine,
            commandLine,
            string.Equals(registered.CommandLine, commandLine, StringComparison.Ordinal)
                ? $"Registered with Claude Desktop as '{serverName}'"
                : $"Registered with Claude Desktop as '{serverName}', pointing elsewhere");
    }

    /// <summary>
    /// The Claude desktop app's half of one MCP server row, which is a file edit
    /// and not a command.
    ///
    /// <para>The MSIX package declares no execution alias, so there is no CLI to
    /// call, and the app's own update channel can only change a server that is
    /// already registered. Editing the config is the entire mechanism — which
    /// makes this the one action here whose failure mode is destroying something,
    /// because the same file holds the person's preferences, shortcut and feature
    /// switches. The document is read, one property is put back, and the whole of
    /// it is written through a temp file.</para>
    /// </summary>
    private static async Task<DevToolActionResult> ApplyClaudeDesktopRegistrationAsync(JsonNode server, bool enabled, CommandLog log, CancellationToken ct)
    {
        if (ClaudeDesktopSection(server) is not { } section)
        {
            return DevToolActionResult.Failed("the entry has no claudeDesktop or claude section to register.");
        }

        var name = ClaudeServerName(server, section);
        if (string.IsNullOrWhiteSpace(name))
        {
            return DevToolActionResult.Failed("the entry has no server name.");
        }

        var desktop = await ResolveClaudeDesktopAsync(log, ct).ConfigureAwait(false);

        if (!desktop.Installed)
        {
            return DevToolActionResult.Failed(ClaudeDesktopMissing);
        }

        if (desktop.Error is { Length: > 0 } error)
        {
            // Refused rather than overwritten. Whatever is in that file is the
            // person's settings, and replacing an unreadable document with a clean
            // one would be the one failure here that cannot be undone.
            return DevToolActionResult.Failed(error);
        }

        var root = desktop.Config ?? [];
        var registered = DevToolClaudeDesktopConfig.ReadServer(root, name);

        if (!enabled)
        {
            if (registered is null)
            {
                return DevToolActionResult.Ok($"'{name}' was already absent from Claude Desktop.");
            }

            DevToolClaudeDesktopConfig.RemoveServer(root, name);
            await WriteClaudeDesktopConfigAsync(desktop.ConfigPath, root, name, "removed", log, ct).ConfigureAwait(false);

            return DevToolActionResult.Ok($"'{name}' was removed from Claude Desktop. {DevToolClaudeDesktopConfig.RestartRequired}");
        }

        var command = GetString(section, "command");
        if (string.IsNullOrWhiteSpace(command))
        {
            return DevToolActionResult.Failed($"'{name}' has no command to register.");
        }

        var commandLine = ClaudeDesktopCommandLine(section);
        if (registered is not null && string.Equals(registered.CommandLine, commandLine, StringComparison.Ordinal))
        {
            return DevToolActionResult.Ok($"'{name}' was already registered with Claude Desktop.");
        }

        DevToolClaudeDesktopConfig.MergeServer(root, name, command, ClaudeServerArgs(section));
        await WriteClaudeDesktopConfigAsync(desktop.ConfigPath, root, name, registered is null ? "registered" : "re-registered", log, ct).ConfigureAwait(false);

        return DevToolActionResult.Ok(
            $"'{name}' was {(registered is null ? "registered with" : "re-registered with")} Claude Desktop. {DevToolClaudeDesktopConfig.RestartRequired}");
    }

    /// <summary>
    /// Writes the whole config through a temp file and moves it into place, the
    /// way the catalog writer does — and for a sharper version of the same reason.
    /// This file is not ours: the app may be reading it, and a serialise straight
    /// into it truncates first, so a failure part-way through leaves a document
    /// that is neither the old settings nor the new ones and that the app cannot
    /// parse at startup.
    /// </summary>
    private static async Task WriteClaudeDesktopConfigAsync(string path, JsonObject root, string name, string verb, CommandLog log, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? Environment.CurrentDirectory);

        var tempPath = path + ".tmp";

        await using (var stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(stream, root, ClaudeDesktopJsonOptions, ct).ConfigureAwait(false);
        }

        File.Move(tempPath, path, overwrite: true);

        // Nothing reached the command log for this, because nothing ran. Without a
        // line here the pane's account of the action would be silent about the only
        // thing the action did.
        log.RecordStep($"{path}: {verb} '{name}'", 0, DevToolClaudeDesktopConfig.RestartRequired);
    }

    /// <summary>Indented, because this is a file people open and edit by hand and
    /// arriving as one line would be a change to it that nothing asked for.</summary>
    private static readonly JsonSerializerOptions ClaudeDesktopJsonOptions = new() { WriteIndented = true };

    private static async Task<Dictionary<string, string>> GetInstalledPluginsAsync((string Command, string[] Prefix) cli, CommandLog log, CancellationToken ct)
    {
        var result = await RunAsync(cli.Command, [.. cli.Prefix, "plugin", "list"], log, ct).ConfigureAwait(false);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(log.Failure("Copilot plugin list", result));
        }

        return new Dictionary<string, string>(DevToolOutput.ParsePluginList(result.Output), StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>What Claude has installed at any scope, or nothing when the listing
    /// failed. A failure is not thrown here the way the Copilot listing throws:
    /// every Claude row can still say something useful without it, and the command
    /// log already carries whatever the CLI printed. It is said, though — a
    /// listing that failed and a listing of nothing are opposite answers to the
    /// one question the cache delete asks.</summary>
    private static async Task<ClaudeInstallListing> GetInstalledClaudePluginsAsync(
        string cli,
        CommandLog log,
        CancellationToken ct)
    {
        var result = await RunAsync(DevToolCommands.ClaudePluginList(cli), log, ct).ConfigureAwait(false);

        if (result.ExitCode == 0 && DevToolOutput.TryParseClaudePluginInstallPaths(result.Output, out var installPaths))
        {
            return new ClaudeInstallListing(DevToolOutput.ParseClaudePluginList(result.Output), installPaths, Known: true);
        }

        return new ClaudeInstallListing(
            new Dictionary<string, DevToolOutput.ClaudePluginState>(StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, int>(),
            Known: false);
    }

    /// <summary>One <c>plugin list</c> run, read twice: what is installed per
    /// plugin id, and which cache folders every install at every scope points
    /// at. Both come out of the same body, and running it twice for the two
    /// would be two chances for the answers to disagree.
    /// <para><paramref name="Known"/> is whether the run answered at all. An
    /// empty <paramref name="InstallPaths"/> from a listing that ran is "no
    /// install points anywhere"; the same empty map from one that did not is
    /// "nothing is known", and a cache delete must not read the second as the
    /// first — that is every version of every plugin gone, the ones in use
    /// included.</para></summary>
    private sealed record ClaudeInstallListing(
        IReadOnlyDictionary<string, DevToolOutput.ClaudePluginState> Plugins,
        IReadOnlyDictionary<string, int> InstallPaths,
        bool Known);

    private const string ClaudeListingUnreadable =
        "Claude's plugin listing could not be read, so nothing can be told stale from in use. Nothing was removed; see the command log.";

    /// <inheritdoc cref="GetInstalledClaudePluginsAsync" />
    private static async Task<IReadOnlySet<string>> GetClaudeMarketplacesAsync(string cli, CommandLog log, CancellationToken ct)
    {
        var result = await RunAsync(DevToolCommands.ClaudeMarketplaceList(cli), log, ct).ConfigureAwait(false);

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
        var result = await RunClaudeMcpAsync(cli, ["mcp", "get", name], log, ct).ConfigureAwait(false);

        return DevToolOutput.ParseClaudeMcpServer(string.IsNullOrWhiteSpace(result.Output) ? result.Error : result.Output);
    }

    /// <summary>
    /// A <c>claude mcp</c> call, asked from the user's profile folder.
    /// </summary>
    /// <remarks>
    /// <para>The working directory is the whole of this method. <c>claude mcp get</c>
    /// has no scope flag: it answers about whichever scope holds the name, and
    /// project scope is found by walking up from the current directory. This
    /// repository's own committed <c>.mcp.json</c> declares <c>backlog</c>, so
    /// asked from inside a worktree the CLI answers <c>Scope: Project config</c> —
    /// and the apply, correctly, leaves a non-user-scope registration alone. The
    /// row could then never be managed at all, on the one machine it is being
    /// developed on.</para>
    ///
    /// <para>The leave-alone rule is not what changes, and must not: local ADR
    /// 0012 §3 depends on a project's own registration being somebody's decision
    /// rather than ours to sweep. What changes is only where the question is
    /// asked from, so that the answer is about user scope. Every <c>claude mcp</c>
    /// launch goes through here — <c>get</c>, <c>add</c> and <c>remove</c> — so
    /// the read and the write cannot disagree about which scope they are talking
    /// about.</para>
    ///
    /// <para>The existing <c>aspire</c> row is affected in exactly the same way,
    /// and that is intended rather than collateral: it was reading a project-scope
    /// answer for the same reason.</para>
    /// </remarks>
    private static Task<CommandResult> RunClaudeMcpAsync(string cli, IReadOnlyList<string> arguments, CommandLog log, CancellationToken ct) =>
        RunAsync(cli, arguments, log, ct, workingDirectory: Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));

    private static async Task<Dictionary<string, string>> GetInstalledDotNetToolsAsync(CommandLog log, CancellationToken ct)
    {
        var result = await RunAsync("dotnet", ["tool", "list", "--global"], log, ct).ConfigureAwait(false);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(log.Failure("dotnet tool list", result));
        }

        return new Dictionary<string, string>(DevToolOutput.ParseDotNetToolList(result.Output), StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>What nuget.org publishes for one MCP server package.
    ///
    /// <para>Searched without <c>--exact-match</c>, because the installed SDK has
    /// no such flag: every lookup exited non-zero on an unrecognised argument and
    /// every available version came back unknown. The exactness the flag was
    /// there for now happens in the parser, over the whole result.</para>
    ///
    /// <para>A server that is not a .NET tool is answered with the no-version dash
    /// and nothing is run. This is the one place a package id reaches
    /// <c>dotnet tool search</c>, so it is the place that has to refuse a blank
    /// one: searching for nothing returns the whole feed, and whatever the parser
    /// found in it would be a version this row invented for itself.</para></summary>
    private static async Task<string> GetDotNetToolAvailableVersionAsync(DevToolMcpServer server, CommandLog log, CancellationToken ct)
    {
        if (server.Mechanism is not DevToolMcpMechanism.DotNetTool)
        {
            return DevToolOutput.NoVersion;
        }

        var packageId = server.PackageId;
        var result = await RunAsync("dotnet", ["tool", "search", packageId], log, ct).ConfigureAwait(false);

        return result.ExitCode == 0
            ? DevToolOutput.ParseDotNetToolSearchVersion(result.Output, packageId)
            : DevToolOutput.Unknown;
    }

    /// <summary>What the plugin's source publishes today, read from the plugin's
    /// manifest through the GitHub API.
    ///
    /// <para>The manifest and not a release: the repository these plugins ship
    /// from cuts no releases, so the release lookup this replaces answered
    /// "release not found" for all of them.</para>
    ///
    /// <para>Repository-backed entries never reach here. They are versioned by
    /// commit in a mirror this host manages, and a sha compared against a semver
    /// reported an update on every check forever — see
    /// <see cref="DescribeRepositoryStateAsync" />.</para></summary>
    private static async Task<string> GetPluginAvailableVersionAsync(JsonNode plugin, CommandLog log, CancellationToken ct)
    {
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

    /// <summary>What the host CLI says it has. Repository-backed entries do not
    /// come here: their installed side is a commit in the mirror, and answering
    /// one row's two columns from two mechanisms is what
    /// <see cref="DevToolVersionAuthority" /> exists to stop.</summary>
    private static string GetPluginInstalledVersion(JsonNode plugin, IReadOnlyDictionary<string, string> installedPlugins) =>
        installedPlugins.TryGetValue(GetRequiredString(plugin, "name"), out var version)
            ? version
            : DevToolOutput.NotInstalled;

    /// <summary>
    /// One repository-backed row, both of whose versions are commits in the local
    /// mirror.
    ///
    /// <para>Fetched first, then read twice: the local tip and the tip that fetch
    /// just wrote. Neither was happening. The listing asked git for the remote
    /// HEAD without fetching and for the clone's HEAD beside it, unscoped, so the
    /// row moved every time anything at all changed in a repository carrying
    /// twenty plugins — which is the reported <c>copilot-app-canvases</c>
    /// case.</para>
    ///
    /// <para>The scope comes from the entry's own <c>extensionsPath</c> or
    /// <c>skillsPath</c>, the two catalog fields nothing used to read. Each names
    /// the folder this row's artifacts are copied out of, which is exactly the
    /// scope its version question has.</para>
    /// </summary>
    private static async Task<(DevToolHostState State, IReadOnlyList<string> Notes)> DescribeRepositoryStateAsync(
        JsonNode plugin,
        bool enabled,
        string kind,
        RefreshState refreshed,
        CommandLog log,
        CancellationToken ct)
    {
        var label = string.IsNullOrWhiteSpace(kind) ? "plugin" : kind;
        var artifactPath = DevToolRefresh.ArtifactPath(GetString(plugin, "extensionsPath"), GetString(plugin, "skillsPath"));
        var notes = new List<string> { DevToolRefresh.MirrorNote(artifactPath) };

        if (ExistingRepositoryPath(plugin) is not { } repoPath)
        {
            return (
                new DevToolHostState(
                    DevToolHosts.Copilot,
                    Installed: false,
                    DevToolOutput.NotInstalled,
                    DevToolOutput.Unknown,
                    DescribeStatus(enabled, installed: false, updateAvailable: false, label))
                {
                    InstalledAuthority = DevToolVersionAuthority.RepositoryMirror,
                    AvailableAuthority = DevToolVersionAuthority.RepositoryMirror
                },
                notes);
        }

        var fetched = await FetchRepositoryAsync(repoPath, refreshed, log, ct).ConfigureAwait(false);
        if (!fetched)
        {
            notes.Add("The source mirror could not be fetched, so the published commit is unknown.");
        }

        var local = await RunAsync(DevToolRefresh.InstalledCommit(repoPath, artifactPath), log, ct).ConfigureAwait(false);
        var installedVersion = DevToolRefresh.RepositoryCommit(local.ExitCode == 0, local.Output);

        // Only read when the fetch wrote it. FETCH_HEAD left over from some
        // earlier run is exactly the stale answer this whole path is about.
        var remote = fetched
            ? await RunAsync(DevToolRefresh.AvailableCommit(repoPath, artifactPath), log, ct).ConfigureAwait(false)
            : null;
        var availableVersion = DevToolRefresh.RepositoryCommit(remote is { ExitCode: 0 }, remote?.Output ?? string.Empty);

        var installed = !installedVersion.Equals(DevToolOutput.Unknown, StringComparison.OrdinalIgnoreCase);

        return (
            new DevToolHostState(
                DevToolHosts.Copilot,
                installed,
                installedVersion,
                availableVersion,
                DescribeStatus(enabled, installed, DevToolInfo.VersionDiffers(installedVersion, availableVersion), label))
            {
                InstalledAuthority = DevToolVersionAuthority.RepositoryMirror,
                AvailableAuthority = DevToolVersionAuthority.RepositoryMirror
            },
            notes);
    }

    /// <summary>One mirror fetched once per listing, whatever the outcome.
    ///
    /// <para>Several catalog entries share one <c>repoPath</c> — the guidelines
    /// repository carries two of them — and fetching per row would be the same
    /// network round trip twice, with the second one able to answer about a
    /// different tip than the first.</para></summary>
    private static async Task<bool> FetchRepositoryAsync(string repoPath, RefreshState refreshed, CommandLog log, CancellationToken ct)
    {
        if (refreshed.Repositories.TryGetValue(repoPath, out var already))
        {
            return already;
        }

        var fetch = await RunAsync(DevToolCommands.GitFetch(repoPath), log, ct).ConfigureAwait(false);
        var fetched = fetch.ExitCode == 0;
        refreshed.Repositories[repoPath] = fetched;

        return fetched;
    }

    /// <summary>
    /// What one listing has already pulled.
    ///
    /// <para>Passed by hand like <see cref="CommandLog" /> beside it, and for the
    /// same reason: this class is static, several of its callers run concurrently,
    /// and an ambient collector would be a shared mutable no call site mentions.
    /// A method that refreshes a source says so in its signature.</para>
    /// </summary>
    private sealed class RefreshState
    {
        /// <summary>The marketplaces <c>plugin marketplace update</c> actually
        /// pulled. Only those may answer a Claude row's Available column.</summary>
        public HashSet<string> Marketplaces { get; } = new(StringComparer.OrdinalIgnoreCase);

        /// <inheritdoc cref="FetchRepositoryAsync" />
        public Dictionary<string, bool> Repositories { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>The marketplace half of a <c>&lt;name&gt;@&lt;marketplace&gt;</c>
    /// plugin id. Split at the last separator, because a plugin name may carry
    /// one and a marketplace name may not.</summary>
    private static string ClaudeMarketplaceOf(string pluginId)
    {
        var separator = pluginId.LastIndexOf('@');

        return separator >= 0 ? pluginId[(separator + 1)..] : pluginId;
    }

    /// <summary>A version, or nothing when the lookup came back with the word for
    /// "nobody answered". Passing that word on as a version is how it ends up in
    /// a column being compared against a real one.</summary>
    private static string? KnownOrNull(string version) =>
        version.Equals(DevToolOutput.Unknown, StringComparison.OrdinalIgnoreCase) ? null : version;

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

    private static async Task<string> RefreshRepositorySourceAsync(JsonNode plugin, CommandLog log, CancellationToken ct)
    {
        var source = GetRequiredString(plugin, "source");
        var repoPath = ResolveConfiguredPath(GetRequiredString(plugin, "repoPath"));
        if (Directory.Exists(repoPath))
        {
            var pull = await RunAsync("git", ["-C", repoPath, "pull", "--ff-only"], log, ct).ConfigureAwait(false);
            return pull.ExitCode == 0 ? "source refreshed" : log.Failure(ToolDisplayName(plugin), pull);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(repoPath) ?? Environment.CurrentDirectory);
        var clone = await RunAsync("git", ["clone", source, repoPath], log, ct).ConfigureAwait(false);
        return clone.ExitCode == 0 ? "source cloned" : log.Failure(ToolDisplayName(plugin), clone);
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

        // Resolved through the abstraction's reader rather than against a
        // packageId, because that is where the rule about which property
        // identifies an entry lives: a command-registered server is addressed by
        // its name, and a lookup that knew only about package ids told the
        // operator its row was "no longer in the config" while the entry sat right
        // there in the file.
        if (key.StartsWith("mcp:", StringComparison.OrdinalIgnoreCase))
        {
            var id = key["mcp:".Length..];
            return GetArray(root, DevToolConfiguration.McpServersArrayName)
                .FirstOrDefault(node => DevToolConfiguration.ReadMcpServer(node) is { } server
                    && server.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        }

        if (key.StartsWith("marketplace:", StringComparison.OrdinalIgnoreCase))
        {
            var name = key["marketplace:".Length..];
            return DevToolConfiguration.MarketplaceEntries(root)
                .FirstOrDefault(node => GetString(node, "name").Equals(name, StringComparison.OrdinalIgnoreCase));
        }

        // Ids are matched case-insensitively for the same reason the arrays above
        // are: a winget id and an extension id are both written by hand into a
        // catalog, and the marketplace's own casing of an extension id is not what
        // the CLI prints back.
        if (key.StartsWith("app:", StringComparison.OrdinalIgnoreCase))
        {
            var id = key["app:".Length..];
            return GetArray(root, DevToolConfiguration.ApplicationsArrayName)
                .FirstOrDefault(node => GetString(node, DevToolConfiguration.ApplicationIdName).Equals(id, StringComparison.OrdinalIgnoreCase));
        }

        return null;
    }

    // Read with the abstraction's tolerance rather than an indexer that throws
    // on a value node and a GetValue that throws on a number: the entries are
    // hand-written, and one odd value is one row's finding, not the list's.
    private static string GetString(JsonNode node, string name) => DevToolConfiguration.ReadString(node, name);

    private static string GetRequiredString(JsonNode node, string name) =>
        string.IsNullOrWhiteSpace(GetString(node, name))
            ? throw new InvalidOperationException($"The tool config entry is missing '{name}'.")
            : GetString(node, name);

    private static bool GetBool(JsonNode node, string name) => node[name]?.GetValue<bool>() ?? false;

    private static string PluginKey(string name) => DevToolConfiguration.KeyFor(DevToolKind.Plugin, name);

    private static string MarketplaceKey(string name) => DevToolConfiguration.KeyFor(DevToolKind.Marketplace, name);

    /// <summary>What a failure message calls the row. The command is the last
    /// fallback rather than nothing at all: an MCP server that ships as no package
    /// and was never named still has to be named in the sentence reporting on
    /// it.</summary>
    private static string ToolDisplayName(JsonNode node) => GetString(node, "name") is { Length: > 0 } name
        ? name
        : GetString(node, "packageId") is { Length: > 0 } packageId ? packageId : GetString(node, "command");

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
        [.. DevToolConfiguration.ReadStrings(claude, "args").Where(value => !string.IsNullOrWhiteSpace(value))];

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
                states.Any(state => state.ReportsUpdate),
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
        DevToolHosts.ClaudeDesktop => "claude desktop",
        DevToolHosts.Default => "shared",
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


    /// <summary>A command the catalog declared, run the way it declared it — the
    /// shell wrapper and the reader both come from the spec, so a caller never has
    /// to remember that <c>code</c> is a <c>.cmd</c> or that <c>wsl</c> answers in
    /// UTF-16.</summary>
    private static Task<CommandResult> RunAsync(DevToolCommandSpec spec, CommandLog log, CancellationToken ct, TimeSpan? timeout = null) =>
        RunAsync(spec.FileName, spec.LaunchArguments, log, ct, timeout, spec.OutputEncoding);

    /// <summary>
    /// One child process, its output, and every way that can fail turned into a
    /// <see cref="CommandResult"/> rather than an exception.
    /// </summary>
    /// <param name="timeout">How long to wait, defaulting to
    /// <see cref="ProbeTimeout"/>. An install passes
    /// <see cref="InstallTimeout"/>.</param>
    /// <param name="encoding">How to read the redirected streams, defaulting to
    /// UTF-8 — which is what winget writes to a pipe whatever the console code
    /// page is, and is not what <c>wsl.exe</c> writes.</param>
    /// <param name="workingDirectory">Where to ask from, or nothing to inherit
    /// this process's own — which is what every launch but the <c>claude mcp</c>
    /// ones does. See <see cref="RunClaudeMcpAsync"/> for the one question whose
    /// answer depends on where it was asked.</param>
    private static async Task<CommandResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        CommandLog log,
        CancellationToken ct,
        TimeSpan? timeout = null,
        Encoding? encoding = null,
        string? workingDirectory = null)
    {
        var reader = encoding ?? Encoding.UTF8;

        using var process = new Process();
        process.StartInfo = new ProcessStartInfo(fileName)
        {
            // Empty means "inherit", which is what ProcessStartInfo already does
            // with it, so the ordinary launch is unchanged.
            WorkingDirectory = workingDirectory ?? string.Empty,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            // Redirecting the streams is not what keeps the console away — only
            // this flag is. The desktop head is a GUI-subsystem process with no
            // console of its own, so Windows hands every child it starts a brand
            // new one, and opening the tools tab meant a dozen of them blinking
            // across the screen while the pane quietly read their output.
            CreateNoWindow = true,
            StandardOutputEncoding = reader,
            StandardErrorEncoding = reader
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
            //
            // It is also how a .cmd shim launched directly fails — "not a valid
            // application for this OS platform" — which is why a spec that names
            // one asks for the shell instead of relying on this.
            var missing = new CommandResult(CommandNotFound, string.Empty, ex.Message);
            log.Record(fileName, arguments, missing);

            return missing;
        }

        var budget = timeout ?? ProbeTimeout;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(budget);

        CommandResult result;
        try
        {
            var outputTask = process.StandardOutput.ReadToEndAsync(deadline.Token);
            var errorTask = process.StandardError.ReadToEndAsync(deadline.Token);
            await process.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
            result = new CommandResult(process.ExitCode, await outputTask.ConfigureAwait(false), await errorTask.ConfigureAwait(false));
        }
        catch (OperationCanceledException)
        {
            // The whole tree, not the process: winget's own installer runs as a
            // child of it, and killing only the parent leaves that installer
            // holding the package lock with nothing left to report on it.
            //
            // Killed on both paths, and only one of them is reported. A caller
            // that cancelled gets its exception back — abandoning the child there
            // would leave a winget running against a pane that has gone — while a
            // deadline that fired becomes a result, because a hung CLI is a row
            // that could not be checked rather than a tools pane that has stopped
            // existing.
            KillProcessTree(process);

            if (ct.IsCancellationRequested)
            {
                throw;
            }

            result = new CommandResult(
                CommandTimedOut,
                string.Empty,
                $"Timed out after {budget.TotalSeconds:0} seconds and the process was stopped.");
        }

        log.Record(fileName, arguments, result);

        return result;
    }

    /// <summary>Stops a command that ran out of time, and says nothing when it
    /// cannot. Between the timeout firing and this line the process may already
    /// have exited on its own, which is a race rather than a problem — and a
    /// failure to kill is not something the row can act on either way.</summary>
    private static void KillProcessTree(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception or NotSupportedException or AggregateException)
        {
        }
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
    ///
    /// <para><b>And the one exit.</b> Everything that turns what a process
    /// printed into text a person sees is a method on this type — the recorded
    /// output, the paste-ready command line, a synthesised step's description,
    /// and <see cref="Failure"/>, which is the sentence a failed action reports.
    /// That is structural rather than tidy: <see cref="Failure"/> used to be a
    /// static beside them and returned <c>result.Error</c> verbatim into the
    /// message the pane renders above the log, so a registration that failed
    /// showed the token in the message and masked it in the transcript
    /// underneath. A secret can only be kept out of all of them by one type
    /// knowing all of them, and this is the type that owns the secrets.</para>
    /// </summary>
    private sealed class CommandLog
    {
        /// <summary>How much of one command's output survives. A <c>git clone</c>
        /// prints progress by the screenful and this text goes straight into a
        /// pane, so the tail is dropped rather than handed to the renderer.</summary>
        private const int OutputLimit = 4000;

        private readonly List<DevToolCommand> _commands = [];

        /// <summary>The values nothing this log produces may carry — a token this
        /// app minted for the action being logged, and every header value the
        /// entry being applied declares.
        ///
        /// <para>Seeded before the first command of an action is recorded, which
        /// is the only moment that works: <see cref="Captured"/> masks at record
        /// time, so a value told to this log afterwards is a value that was
        /// already written down. A <see cref="HashSet{T}"/> rather than a list
        /// because the same secret is offered from more than one place — the
        /// declared header and its expansion — and masking it twice would only
        /// cost passes over every line.</para></summary>
        private readonly HashSet<string> _secrets = new(StringComparer.Ordinal);

        public IReadOnlyList<DevToolCommand> Commands => _commands;

        /// <summary>Keeps one value out of everything this log produces from here
        /// on — the command line, whatever the command printed, and the sentence
        /// a failure is reported with.
        ///
        /// <para>By value rather than by pattern, which is <see cref="DevToolOutput.Redact"/>'s
        /// own reasoning: the token arrives on the command line joined into a
        /// <c>--header</c> argument and comes back in the <c>Headers:</c> block
        /// the CLI echoes, and a rule written against either of those spellings
        /// stops working — silently — the day the CLI changes one.</para></summary>
        public void Redact(string? secret)
        {
            if (!string.IsNullOrWhiteSpace(secret))
            {
                _secrets.Add(secret);
            }
        }

        public void Record(string fileName, IReadOnlyList<string> arguments, CommandResult result) =>
            _commands.Add(new DevToolCommand(Describe(fileName, arguments), result.ExitCode, Captured(result)));

        /// <summary>Something that happened without a process behind it: an HTTP
        /// call, a config file rewritten in place.
        ///
        /// <para>Synthesised rather than left out. The pane's command log is the
        /// only account of what a refresh or an action did, and the two steps that
        /// are not commands — the marketplace lookup and the Claude Desktop config
        /// merge — are precisely the two whose failure is otherwise invisible: a
        /// column that reads "unknown" and a file that did not change.</para>
        ///
        /// <para>The description is masked as well as the output. It is composed
        /// by its caller out of a path and a name, and "composed by its caller"
        /// is exactly the property that stops being true one refactor from
        /// now.</para></summary>
        public void RecordStep(string description, int exitCode, string output) =>
            _commands.Add(new DevToolCommand(
                DevToolOutput.Redact(description, _secrets),
                exitCode,
                Captured(new CommandResult(exitCode, output, string.Empty))));

        /// <summary>
        /// Why an action failed, in the words the process used, and safe to show.
        /// </summary>
        /// <remarks>
        /// <para>On this type and not beside the other statics, because what it
        /// returns is process output and process output is where a credential
        /// comes back. This became a <c>DevToolActionResult.Message</c>, which
        /// the pane renders in the status line directly above the command
        /// log — so a failing <c>claude mcp add … --header "Authorization: Bearer
        /// …"</c> printed the redacted line in the transcript and the unredacted
        /// one in the sentence over it.</para>
        ///
        /// <para>Standard error where there is any, and standard output where
        /// there is not: a CLI that failed quietly on stdout is still a CLI that
        /// said why. An exit code alone is the last resort and names the thing
        /// that failed, because "exit code 1" on its own is not a sentence
        /// anybody can act on.</para>
        /// </remarks>
        public string Failure(string name, CommandResult result)
        {
            var details = string.IsNullOrWhiteSpace(result.Error) ? result.Output : result.Error;

            return string.IsNullOrWhiteSpace(details)
                ? $"{name} failed with exit code {result.ExitCode}."
                : DevToolOutput.Redact(details.Trim(), _secrets);
        }

        /// <summary>The command as something a reader could paste into a shell,
        /// which is the form they will want it in when they go to reproduce
        /// whatever just failed. Public because a failure sentence quotes one
        /// too — see <see cref="DescribeInstallFailure"/> — and a second join of
        /// the same strings somewhere else is a second place to forget the
        /// masking.</summary>
        public string Describe(string fileName, IReadOnlyList<string> arguments) =>
            DevToolOutput.Redact(
                string.Join(' ', new[] { Quoted(fileName) }.Concat(arguments.Select(Quoted))),
                _secrets);

        private static string Quoted(string value) =>
            value.Any(char.IsWhiteSpace) ? $"\"{value}\"" : value;

        private string Captured(CommandResult result)
        {
            // Standard error after standard output rather than interleaved: the
            // two streams were read separately and their real ordering was lost
            // at that point, so pretending to restore it would be a fiction.
            // Redacted before the truncation rather than after it, so a token in
            // the last hundred characters of a long output is masked whether or
            // not the tail survives.
            var combined = DevToolOutput.Redact(
                string.Join(
                    Environment.NewLine,
                    new[] { result.Output, result.Error }
                        .Select(stream => stream.TrimEnd())
                        .Where(stream => stream.Length > 0)),
                _secrets);

            return combined.Length <= OutputLimit
                ? combined
                : combined[..OutputLimit] + $"{Environment.NewLine}... output truncated at {OutputLimit} characters.";
        }
    }
}
