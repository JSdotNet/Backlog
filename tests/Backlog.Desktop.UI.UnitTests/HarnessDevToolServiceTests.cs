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

        var disabled = await tools.DisableAsync(key);

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

        var enabled = await tools.EnableAsync(key);

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

        await tools.DisableAsync(key);

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
    [InlineData("app:Microsoft.PowerToys")]
    public async Task A_disabled_catalog_row_reads_back_disabled(string key)
    {
        var tools = CreateService("""
            {
              "plugins": [
                { "name": "architecture", "source": "JSdotNet/Copilot:plugins/architecture", "enabled": true }
              ],
              "mcpServers": [
                { "name": "guidelines", "packageId": "JSdotNet.MCP.Guidelines", "enabled": true }
              ],
              "applications": [
                { "id": "Microsoft.PowerToys", "name": "PowerToys", "provider": "winget", "enabled": true }
              ]
            }
            """);

        Assert.True(await IsEnabledAsync(tools, key));

        await tools.DisableAsync(key);

        Assert.False(await IsEnabledAsync(tools, key));
    }

    /// <summary>Switching one row off is not an opinion about the row beside it.
    /// A dictionary keyed on the wrong thing would take the whole sample spread
    /// down with the one row that was clicked.</summary>
    [Fact]
    public async Task Disabling_one_row_leaves_the_others_alone()
    {
        var tools = CreateService(EmptyCatalog);

        await tools.DisableAsync("app:office-signed-in");

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

        await tools.AcknowledgeAsync(key, acknowledged: true);
        await tools.DisableAsync(key);

        var row = await FindAsync(tools, key);
        Assert.False(row.ConfiguredEnabled);
        Assert.True(row.Acknowledged);
    }

    /// <summary>What the desktop head calls a row this machine does not want, and
    /// what the harness has to call it too for the pane to read the same.</summary>
    private const string DisabledStatus = "Disabled in config";

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
    private static LocalDevelopmentDevToolService CreateService(string catalog)
    {
        var root = Path.Combine(Path.GetTempPath(), "backlog-harness-tool-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, ".tools"));
        File.WriteAllText(
            Path.Combine(root, ".tools", DevToolConfigurationPaths.CatalogFileName),
            catalog);

        return new LocalDevelopmentDevToolService(TaskStoreFor(root));
    }

    private static ITaskStore TaskStoreFor(string root) =>
        TasksTestHost.TaskStoreFor(
            new WorkspaceSettingsStore(root, Path.Combine(root, "settings.json")));
}
