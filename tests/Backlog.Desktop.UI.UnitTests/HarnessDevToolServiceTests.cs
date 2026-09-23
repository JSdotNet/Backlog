using Backlog.Desktop.WebHarness;
using Backlog.Modules.Tasks.Abstractions.Services;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The tools pane's Disable and Enable, through the adapter a browser session
/// actually talks to.
///
/// <para>The desktop head is a MAUI window no driver can reach, so the harness is
/// the only place these two buttons are ever pressed — and its adapter answers
/// from JSON alone, which is why they are pinned here rather than only in a
/// Playwright pass. A row that keeps its old label after being switched off is
/// invisible to a build and to every test that stops at the write.</para>
/// </summary>
public sealed class HarnessDevToolServiceTests
{
    /// <summary>
    /// The regression this file was written for.
    ///
    /// <para>A sample row is in nobody's catalog, so the merge that carries a real
    /// row's override back out of the per-PC file has nothing to carry — and the
    /// row was re-seeded from its literal on every read. The write succeeded, the
    /// action reported success, and the row came back exactly as it went in.</para>
    /// </summary>
    [Fact]
    public async Task A_disabled_sample_application_reads_back_disabled()
    {
        var tools = CreateService(EmptyCatalog);
        const string key = "app:office-signed-in";

        Assert.True(await IsEnabledAsync(tools, key));

        var disabled = await tools.DisableAsync(key, TestContext.Current.CancellationToken);

        Assert.True(disabled.Succeeded);

        // Both halves of the row the user reads: the state the button is labelled
        // from, and the status line beside it.
        var row = await FindAsync(tools, key);
        Assert.False(row.ConfiguredEnabled);
        Assert.StartsWith(DisabledStatus, row.Status, StringComparison.Ordinal);
    }

    /// <summary>The other direction, on the sample row that starts switched off.
    /// Disable alone would pass against an adapter that had simply stopped
    /// reporting anything as enabled.</summary>
    [Fact]
    public async Task An_enabled_sample_application_reads_back_enabled()
    {
        var tools = CreateService(EmptyCatalog);
        const string key = "app:JetBrains.ReSharper";

        Assert.False(await IsEnabledAsync(tools, key));

        var enabled = await tools.EnableAsync(key, TestContext.Current.CancellationToken);

        Assert.True(enabled.Succeeded);

        // This is the row seeded switched off, so it is the one whose status was
        // seeded saying so. Re-enabled, it must stop.
        var row = await FindAsync(tools, key);
        Assert.True(row.ConfiguredEnabled);
        Assert.DoesNotContain(DisabledStatus, row.Status, StringComparison.Ordinal);
    }

    /// <summary>What switching a row off is actually for. Git.Git is the sample
    /// row that is behind, so it is the one where an override that never arrived
    /// leaves the pane still offering to update something this machine has said
    /// it does not want.</summary>
    [Fact]
    public async Task A_disabled_row_is_no_longer_offered_an_update()
    {
        var tools = CreateService(EmptyCatalog);
        const string key = "app:Git.Git";

        Assert.True((await FindAsync(tools, key)).CanUpdate);

        await tools.DisableAsync(key, TestContext.Current.CancellationToken);

        var row = await FindAsync(tools, key);
        Assert.False(row.ConfiguredEnabled);
        Assert.False(row.CanUpdate);
    }

    /// <summary>
    /// The rows that were never broken, guarded so a fix aimed at the sample
    /// spread cannot quietly take them with it.
    ///
    /// <para>These three come from the catalog, so their override arrives through
    /// <see cref="DevToolConfiguration.ReadAsync"/>'s merge — the same route the
    /// desktop head resolves them by.</para>
    /// </summary>
    [Theory]
    [InlineData("plugin:architecture")]
    [InlineData("mcp:JSdotNet.MCP.Guidelines")]
    [InlineData("mcp:aspire")]
    [InlineData("app:Microsoft.PowerToys")]
    public async Task A_disabled_catalog_row_reads_back_disabled(string key)
    {
        var tools = CreateService("""
            {
              "plugins": [
                { "name": "architecture", "source": "JSdotNet/Copilot:plugins/architecture", "enabled": true }
              ],
              "mcpServers": [
                { "name": "guidelines", "packageId": "JSdotNet.MCP.Guidelines", "enabled": true },
                { "name": "aspire", "command": "aspire", "args": [ "agent", "mcp" ], "enabled": true }
              ],
              "applications": [
                { "id": "Microsoft.PowerToys", "name": "PowerToys", "provider": "winget", "enabled": true }
              ]
            }
            """);

        Assert.True(await IsEnabledAsync(tools, key));

        await tools.DisableAsync(key, TestContext.Current.CancellationToken);

        Assert.False(await IsEnabledAsync(tools, key));
    }

    /// <summary>Switching one row off is not an opinion about the row beside it.
    /// A dictionary keyed on the wrong thing would take the whole sample spread
    /// down with the one row that was clicked.</summary>
    [Fact]
    public async Task Disabling_one_row_leaves_the_others_alone()
    {
        var tools = CreateService(EmptyCatalog);

        await tools.DisableAsync("app:office-signed-in", TestContext.Current.CancellationToken);

        Assert.True(await IsEnabledAsync(tools, "app:onenote-available"));
        Assert.True(await IsEnabledAsync(tools, "app:Git.Git"));
    }

    /// <summary>The tick and the switch are two different facts about a row —
    /// "this machine has done it" and "this machine wants it" — and the class
    /// keeps them apart on purpose. Disabling a manual row must not untick it.
    /// </summary>
    [Fact]
    public async Task Disabling_a_manual_row_does_not_untick_it()
    {
        var tools = CreateService(EmptyCatalog);
        const string key = "app:onenote-available";

        await tools.AcknowledgeAsync(key, acknowledged: true, TestContext.Current.CancellationToken);
        await tools.DisableAsync(key, TestContext.Current.CancellationToken);

        var row = await FindAsync(tools, key);
        Assert.False(row.ConfiguredEnabled);
        Assert.True(row.Acknowledged);
    }

    /// <summary>
    /// The three per-machine writes on the one kind that has no per-machine state
    /// to write.
    ///
    /// <para>The pane draws a marketplace neither a switch nor a tick box, so this
    /// is the port's contract rather than a button — and it has to come back as a
    /// result and not as an exception out of the abstraction, because a caller of
    /// <see cref="IDevToolService"/> reads failure off the result it is handed.
    /// The desktop head already refuses an enable here; the harness is the half
    /// that called straight through.</para>
    /// </summary>
    [Fact]
    public async Task A_marketplace_is_refused_a_per_machine_override()
    {
        var (tools, paths) = CreateServiceWith("""
            {
              "claude": { "marketplaces": [ { "name": "jsdotnet-copilot", "source": "JSdotNet/Copilot" } ] },
              "plugins": []
            }
            """);
        const string key = "marketplace:jsdotnet-copilot";

        var disabled = await tools.DisableAsync(key, TestContext.Current.CancellationToken);
        var enabled = await tools.EnableAsync(key, TestContext.Current.CancellationToken);
        var acknowledged = await tools.AcknowledgeAsync(key, acknowledged: true, TestContext.Current.CancellationToken);

        Assert.All(
            new[] { disabled, enabled, acknowledged },
            result =>
            {
                Assert.False(result.Succeeded);
                Assert.Contains("marketplace", result.Message, StringComparison.OrdinalIgnoreCase);
                Assert.Contains("catalog", result.Message, StringComparison.OrdinalIgnoreCase);

                // The abstraction's own sentence, forwarded rather than
                // reworded here — one rule, one wording.
                Assert.Contains("jsdotnet-copilot", result.Message, StringComparison.Ordinal);
            });

        // What a refusal actually has to leave alone. Asserting the row instead
        // would assert nothing: ListAsync builds a marketplace row with
        // ConfiguredEnabled: true and never sets Acknowledged on one, so both
        // read the same whether the write was refused or went through.
        Assert.False(File.Exists(paths.PcConfigPath));

        // And that this is the path the service actually writes to, so the line
        // above is a fact about the refusal and not about a path nobody uses.
        Assert.True((await tools.DisableAsync("plugin:architecture", TestContext.Current.CancellationToken)).Succeeded);
        Assert.True(File.Exists(paths.PcConfigPath));
    }

    /// <summary>
    /// The pair the fix rests on, asserted where it is observable: the merge is
    /// live for the one property a marketplace actually owns, and inert for the
    /// two the write half refuses.
    ///
    /// <para>This is what "the two halves cannot disagree" means in the end. The
    /// per-PC <c>source</c> reaches the row, so a hand-edited entry is honoured
    /// rather than discarded; the per-PC <c>enabled</c> reaches the document and
    /// changes nothing, because a marketplace row is built
    /// <c>ConfiguredEnabled: true</c> and is never asked. That second half is why
    /// refusing the write costs nobody anything — and it is pinned here rather
    /// than assumed, because it is the hardcode the refusal's reasoning
    /// depends on.</para>
    /// </summary>
    [Fact]
    public async Task A_per_pc_marketplace_entry_moves_the_source_and_not_the_switch()
    {
        var (tools, paths) = CreateServiceWith("""
            {
              "claude": { "marketplaces": [ { "name": "jsdotnet-copilot", "source": "JSdotNet/Copilot" } ] },
              "plugins": []
            }
            """);
        Directory.CreateDirectory(Path.GetDirectoryName(paths.PcConfigPath)!);
        await File.WriteAllTextAsync(paths.PcConfigPath, """
            {
              "claude": {
                "marketplaces": [
                  { "name": "jsdotnet-copilot", "source": "D:/Repos/Copilot", "enabled": false, "acknowledged": true }
                ]
              }
            }
            """, TestContext.Current.CancellationToken);

        var row = await FindAsync(tools, "marketplace:jsdotnet-copilot");

        Assert.Equal("D:/Repos/Copilot", row.Source);
        Assert.True(row.ConfiguredEnabled);
        Assert.False(row.Acknowledged);
    }

    /// <summary>
    /// AC1, AC4 and AC6 on the half a browser can reach.
    ///
    /// <para>An <c>mcpServers</c> entry with no <c>packageId</c> was dropped by
    /// both enumerations before a row was ever built, so the one server in this
    /// repository's own catalog that is registered by a command instead of
    /// installed as a .NET tool was invisible in the only copy of this pane a
    /// driver can open.</para>
    ///
    /// <para>The version columns are the second half of the same fix. There is no
    /// published version of a command to be behind, so nothing is looked up and
    /// nothing is invented: the command line stands in for the installed version —
    /// the way <c>DescribeClaudeRegistration</c> already lets a command stand in
    /// for a registration's — and the available column keeps the no-version dash,
    /// which is what stops the row reading as up to date about nothing.</para>
    /// </summary>
    [Fact]
    public async Task A_command_registered_server_becomes_one_row()
    {
        var tools = CreateService(CommandRegisteredCatalog);

        var row = await FindAsync(tools, "mcp:aspire");

        Assert.Equal(DevToolKind.McpServer, row.Kind);
        Assert.Equal("aspire", row.Name);
        Assert.Equal("aspire agent mcp", row.InstalledVersion);
        Assert.Equal(DevToolOutput.NoVersion, row.AvailableVersion);
        Assert.False(row.AvailableVersionKnown);

        // Nothing this pane can press: there is no .NET tool to install and no
        // version to update to.
        Assert.False(row.Installable);
        Assert.False(row.CanInstall);
        Assert.False(row.CanUpdate);
    }

    /// <summary>
    /// The third mechanism the array holds, in the only copy of this pane a
    /// driver can open.
    ///
    /// <para>The entry is addressable by its <c>name</c> and reachable at its
    /// <c>url</c>, so it is a row — and it is a row with nothing to install,
    /// because the thing at the other end of a URL is listening or it is
    /// not.</para>
    /// </summary>
    [Fact]
    public async Task An_http_server_becomes_one_row()
    {
        var tools = CreateService(HttpCatalog);

        var row = await FindAsync(tools, "mcp:backlog");

        Assert.Equal(DevToolKind.McpServer, row.Kind);
        Assert.Equal("backlog", row.Name);
        Assert.Equal(Endpoint, row.Source);
        Assert.Equal(Endpoint, row.InstalledVersion);
        Assert.Equal(DevToolOutput.NoVersion, row.AvailableVersion);

        Assert.False(row.Installable);
        Assert.False(row.CanInstall);
        Assert.False(row.CanUpdate);

        // Nothing here can repair a registration either: this harness has no
        // endpoint to write one from, so the row reports and offers nothing.
        Assert.False(row.CanReRegister);
    }

    /// <summary>
    /// The per-host line, which the guard in front of it used to swallow.
    ///
    /// <para>It was drawn only for a .NET tool, on the reasoning that a
    /// command-registered server's command <em>is</em> its registration. That
    /// reasoning does not carry to a server reached over HTTP: the URL is where it
    /// answers and the registration is a separate thing that can point somewhere
    /// else entirely — which is the whole subject of this row.</para>
    /// </summary>
    [Fact]
    public async Task An_http_server_draws_its_claude_registration()
    {
        var tools = CreateService(HttpCatalog);

        var row = await FindAsync(tools, "mcp:backlog");

        var claude = Assert.Single(row.HostStates, state => state.Status.Contains("Registered with Claude", StringComparison.Ordinal));
        Assert.Equal("Registered with Claude as 'backlog'", claude.Status);
        Assert.Equal(Endpoint, claude.InstalledVersion);
    }

    /// <summary>
    /// The rule this harness is held to whatever else changes: it expands
    /// nothing.
    ///
    /// <para>There is no <c>IMcpEndpointSource</c> here and there deliberately
    /// never will be — no settings, no listener, no port, and an implementation
    /// that resolved <c>${BACKLOG_MCP_TOKEN}</c> would have to mint one, which is
    /// to say write a credential to a machine from a development host. So the
    /// placeholders stay exactly as the file spells them, everywhere the row
    /// carries them, and the row says which host can answer instead.</para>
    /// </summary>
    [Fact]
    public async Task An_http_server_is_reported_unexpanded()
    {
        var tools = CreateService(HttpCatalog);

        var row = await FindAsync(tools, "mcp:backlog");

        Assert.Contains("${BACKLOG_MCP_PORT}", row.Source, StringComparison.Ordinal);
        Assert.Contains("${BACKLOG_MCP_PORT}", row.InstalledVersion, StringComparison.Ordinal);
        Assert.All(row.HostStates, state => Assert.DoesNotContain("Bearer", state.InstalledVersion, StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain("Bearer", row.Status, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("resolved in the desktop app", row.Status, StringComparison.Ordinal);
    }

    /// <summary>AC8. A mechanism nobody here knows is read the way an unknown
    /// application <c>provider</c> is: the row is drawn, nothing runs for it, and
    /// the fact travels in the status rather than in a row that simply is not
    /// there.</summary>
    [Fact]
    public async Task A_server_with_an_unrecognised_mechanism_is_still_a_row()
    {
        var tools = CreateService("""
            {
              "mcpServers": [
                { "name": "aspire", "mechanism": "docker-compose", "command": "aspire", "enabled": true }
              ]
            }
            """);

        var row = await FindAsync(tools, "mcp:aspire");

        Assert.False(row.Installable);
        Assert.False(row.CanInstall);
        Assert.Contains("docker-compose", row.Status, StringComparison.Ordinal);
    }

    /// <summary>
    /// The same shape without a command in it, which is where the version columns
    /// ran out of anything honest to say.
    ///
    /// <para>A row with no .NET tool reports its command line where the installed
    /// version goes — but an entry can reach that state carrying no top-level
    /// command at all, either by naming a mechanism this build has never heard of
    /// or by declaring itself <c>manual</c>. What arrived in the cell then was the
    /// empty string, which the pane draws as a blank Installed cell opposite a
    /// dashed Available one: a column that reads as though the lookup broke, on a
    /// row where no lookup was ever meant to happen. The no-version dash is the
    /// marker for that, and it is the one both columns now carry.</para>
    /// </summary>
    [Fact]
    public async Task A_server_with_no_command_to_stand_in_keeps_the_no_version_dash()
    {
        var tools = CreateService("""
            {
              "mcpServers": [
                { "name": "guidelines", "packageId": "JSdotNet.MCP.Guidelines", "mechanism": "docker-compose", "enabled": true }
              ]
            }
            """);

        var row = await FindAsync(tools, "mcp:JSdotNet.MCP.Guidelines");

        Assert.Equal(DevToolOutput.NoVersion, row.InstalledVersion);
        Assert.Equal(DevToolOutput.NoVersion, row.AvailableVersion);
        Assert.False(row.Installable);
        Assert.False(row.CanInstall);
    }

    /// <summary>What the desktop head calls a row this machine does not want, and
    /// what the harness has to call it too for the pane to read the same.</summary>
    private const string DisabledStatus = "Disabled in config";

    /// <summary>The sample cache a plugin entry can carry, read as the pane's
    /// chips: which versions, and which of them something still loads.</summary>
    [Fact]
    public async Task A_plugin_entry_reads_its_sample_cached_versions()
    {
        var tools = CreateService(CachedPluginCatalog);

        var row = await FindAsync(tools, "plugin:devbook");

        Assert.Equal(["1.0.0", "1.0.1", "1.1.0"], row.CachedVersions.Select(cached => cached.Version));
        Assert.Equal([17, 1, 0], row.CachedVersions.Select(cached => cached.Installs));
        Assert.Equal(["1.1.0"], row.StaleCachedVersions.Select(cached => cached.Version));
    }

    /// <summary>The browser's Remove has to change what the next read says, or
    /// it is the same shape as a Remove that did nothing.</summary>
    [Fact]
    public async Task Removing_a_stale_cached_version_drops_it_from_the_next_read()
    {
        var tools = CreateService(CachedPluginCatalog);

        var removed = await tools.RemoveCachedVersionAsync("plugin:devbook", "1.1.0", TestContext.Current.CancellationToken);

        Assert.True(removed.Succeeded);
        var row = await FindAsync(tools, "plugin:devbook");
        Assert.Equal(["1.0.0", "1.0.1"], row.CachedVersions.Select(cached => cached.Version));
        Assert.Empty(row.StaleCachedVersions);
    }

    /// <summary>The port's one promise about the cache, kept by the fake too: a
    /// folder some install still points at is not deleted from here.</summary>
    [Fact]
    public async Task A_cached_version_in_use_is_refused()
    {
        var tools = CreateService(CachedPluginCatalog);

        var refused = await tools.RemoveCachedVersionAsync("plugin:devbook", "1.0.0", TestContext.Current.CancellationToken);

        Assert.False(refused.Succeeded);
        Assert.Contains("in use", refused.Message, StringComparison.Ordinal);
        Assert.Equal(3, (await FindAsync(tools, "plugin:devbook")).CachedVersions.Count);
    }

    [Fact]
    public async Task Clearing_the_stale_cache_removes_every_stale_version_and_nothing_in_use()
    {
        var tools = CreateService(CachedPluginCatalog);

        var cleared = await tools.RemoveStaleCacheAsync(TestContext.Current.CancellationToken);

        Assert.True(cleared.Succeeded);
        Assert.Contains("2 stale versions", cleared.Message, StringComparison.Ordinal);
        var catalog = await tools.ListAsync();
        Assert.Empty(catalog.Tools.SelectMany(tool => tool.StaleCachedVersions));
        Assert.Equal(["1.0.0", "1.0.1"], (await FindAsync(tools, "plugin:devbook")).CachedVersions.Select(cached => cached.Version));
        Assert.Equal(["0.5.0"], (await FindAsync(tools, "plugin:architecture")).CachedVersions.Select(cached => cached.Version));
    }

    /// <summary>Two plugins with sample caches: one carrying the object shape and
    /// one carrying bare version strings, which read as versions nothing uses.</summary>
    private const string CachedPluginCatalog = """
        {
          "plugins": [
            {
              "name": "devbook",
              "source": "JSdotNet/Devbook:plugins/devbook",
              "enabled": true,
              "cachedVersions": [
                { "version": "1.0.0", "installs": 17 },
                { "version": "1.0.1", "installs": 1 },
                { "version": "1.1.0", "installs": 0 }
              ]
            },
            {
              "name": "architecture",
              "source": "JSdotNet/Copilot:plugins/architecture",
              "enabled": true,
              "cachedVersions": [ "0.4.0", { "version": "0.5.0", "installs": 1 } ]
            }
          ],
          "mcpServers": []
        }
        """;

    /// <summary>The entry this repository's own catalog ships: an MCP server that
    /// is registered by the command that starts it rather than installed as a
    /// .NET tool.</summary>
    private const string CommandRegisteredCatalog = """
        {
          "plugins": [],
          "mcpServers": [
            { "name": "aspire", "command": "aspire", "args": [ "agent", "mcp" ], "enabled": true }
          ]
        }
        """;

    /// <summary>The address this repository's own MCP server is reached at, with
    /// the two placeholders it carries. Spelled once so the assertions and the
    /// catalog below cannot drift apart.</summary>
    private const string Endpoint = "http://127.0.0.1:${BACKLOG_MCP_PORT}/mcp";

    /// <summary>An MCP server that is reached over HTTP rather than installed or
    /// started — carrying, on purpose, an Authorization header with a token
    /// placeholder in it. A catalog without one would prove nothing about the
    /// harness not expanding it.</summary>
    private const string HttpCatalog = $$"""
        {
          "plugins": [],
          "mcpServers": [
            {
              "name": "backlog",
              "type": "http",
              "url": "{{Endpoint}}",
              "headers": { "Authorization": "Bearer ${BACKLOG_MCP_TOKEN}" },
              "hosts": [ "claude" ],
              "enabled": true
            }
          ]
        }
        """;

    private const string EmptyCatalog = """{ "plugins": [], "mcpServers": [] }""";

    private static async Task<bool> IsEnabledAsync(IDevToolService tools, string key) =>
        (await FindAsync(tools, key)).ConfiguredEnabled;

    private static async Task<DevToolInfo> FindAsync(IDevToolService tools, string key)
    {
        var catalog = await tools.ListAsync();

        return Assert.Single(catalog.Tools, tool => tool.Key == key);
    }

    /// <summary>
    /// The adapter over a catalog of its own, in a folder of its own.
    ///
    /// <para>Composed the way the harness composes it — a workspace settings file,
    /// the backlog store over it, the adapter over that — because the storage root
    /// is what decides where <c>.tools</c> is, and a test that pointed the adapter
    /// at the real one would write per-PC overrides into somebody's synced
    /// folder.</para>
    /// </summary>
    private static LocalDevelopmentDevToolService CreateService(string catalog) =>
        CreateServiceWith(catalog).Tools;

    /// <summary>The service and the paths it reads, for the tests that have to
    /// assert on the files themselves rather than on a row.</summary>
    private static (LocalDevelopmentDevToolService Tools, DevToolConfigurationPaths Paths) CreateServiceWith(string catalog)
    {
        var root = Path.Combine(Path.GetTempPath(), "backlog-harness-tool-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, ".tools"));
        File.WriteAllText(
            Path.Combine(root, ".tools", DevToolConfigurationPaths.CatalogFileName),
            catalog);

        return (new LocalDevelopmentDevToolService(TaskStoreFor(root)), DevToolConfigurationPaths.FromRepositoryRoot(root));
    }

    private static ITaskStore TaskStoreFor(string root) =>
        TasksTestHost.TaskStoreFor(
            new WorkspaceSettingsStore(root, Path.Combine(root, "settings.json")));
}
