using Backlog.Desktop.WebHarness;
using Backlog.Infrastructure.FileSystem;
using Backlog.Modules.DevPc.Abstractions;
using Backlog.Modules.DevPc.UI;
using Backlog.SharedKernel.Ai;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// Dev PC Management's Ask AI content: the tool catalog, one line per tool,
/// with the catalog document named first.
/// </summary>
public class ToolsAiContentSourceTests
{
    [Fact]
    public async Task A_record_is_the_tool_name_kind_versions_and_state_after_the_catalog_document()
    {
        var catalog = new DevToolCatalog(
            [
                new DevToolInfo("plugin:claude-desktop", DevToolKind.Plugin, "claude-desktop", "JSdotNet/Copilot", ConfiguredEnabled: true, Installed: true, "1.4.0", "1.5.0", "Update available") { Group = "Agents" },
                new DevToolInfo("mcp:playwright", DevToolKind.McpServer, "playwright", null, ConfiguredEnabled: false, Installed: false, "", "", "")
            ],
            "2 tools.",
            CatalogExists: true,
            CatalogPath: @"C:\Users\me\.copilot\tools.json",
            CanEditCatalog: true);

        var content = await new ToolsAiContentSource(new FixedTools(catalog)).ComposeAsync(new AiContentRequest("nothing shared", 6000), TestContext.Current.CancellationToken);

        Assert.Equal("tools", content.AreaKey);
        Assert.Equal(
            "Tools: 3 entries.\n"
            + @"Catalog document: C:\Users\me\.copilot\tools.json" + "\n"
            + "---\n"
            + "Tool: claude-desktop — plugin, Agents, installed 1.4.0, available 1.5.0, enabled, Update available\n"
            + "---\n"
            + "Tool: playwright — MCP server, not installed, disabled",
            content.Body);
    }

    [Fact]
    public async Task A_host_without_a_catalog_says_so_in_the_body()
    {
        var content = await new ToolsAiContentSource(new UnsupportedDevToolService()).ComposeAsync(new AiContentRequest("anything", 6000), TestContext.Current.CancellationToken);

        Assert.Equal("Tools: 1 entry.\nCatalog: Tool management is only available in the desktop app.", content.Body);
    }

    /// <summary>
    /// The catalog a machine shares with an assistant, for a server reached over
    /// HTTP.
    ///
    /// <para>This is the shared content the Ask AI port composes, which is to say
    /// text that leaves the machine. A row that carried an expanded bearer token
    /// would put this machine's MCP credential into a prompt — so the rule is that
    /// nothing in a row is ever the expansion of one, and this is where that is
    /// held against a real read of a real catalog rather than against a row built
    /// by hand to pass.</para>
    ///
    /// <para>Through the harness adapter deliberately: it composes no endpoint
    /// source at all, so what it produces is the unexpanded truth of the file.
    /// The desktop head cannot be reached from a test project, which is why the
    /// expansion itself is pinned in <c>McpEndpointExpansionTests</c>
    /// instead.</para>
    ///
    /// <para><b>The positive half is what makes the negative half mean
    /// anything.</b> An assertion that a header value is absent proves nothing on
    /// its own — no row type has ever carried one, so it would pass with this
    /// whole feature reverted and with the catalog unread. So the entry's
    /// <em>other</em> unexpanded text is asserted present first: the port
    /// placeholder, verbatim out of the file. That is this row reaching the
    /// shared body with its catalog spelling intact, which is exactly the path a
    /// header value would take the day somebody adds one — and the day they do,
    /// the three assertions under it fail.</para>
    /// </summary>
    [Fact]
    public async Task An_http_row_shares_its_url_and_none_of_its_headers()
    {
        var root = Path.Combine(Path.GetTempPath(), "backlog-tools-ai-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, ".tools"));
        File.WriteAllText(
            Path.Combine(root, ".tools", DevToolConfigurationPaths.CatalogFileName),
            """
            {
              "plugins": [],
              "mcpServers": [
                {
                  "name": "backlog",
                  "type": "http",
                  "url": "http://127.0.0.1:${BACKLOG_MCP_PORT}/mcp",
                  "headers": {
                    "Authorization": "Bearer ${BACKLOG_MCP_TOKEN}",
                    "X-Api-Key": "sk-live-abc123"
                  },
                  "hosts": [ "claude" ],
                  "enabled": true
                }
              ]
            }
            """);

        var tools = new LocalDevelopmentDevToolService(
            TasksTestHost.TaskStoreFor(new WorkspaceSettingsStore(root, Path.Combine(root, "settings.json"))));

        var content = await new ToolsAiContentSource(tools)
            .ComposeAsync(new AiContentRequest("nothing shared", 6000), TestContext.Current.CancellationToken);

        // The row is there, and the catalog's own unexpanded text reaches the
        // body — so what follows is about what was left out rather than about a
        // catalog nothing read.
        Assert.Contains("backlog", content.Body, StringComparison.Ordinal);
        Assert.Contains("${BACKLOG_MCP_PORT}", content.Body, StringComparison.Ordinal);

        // A literal credential somebody typed into the Add-a-tool form, and the
        // placeholder that becomes this machine's own. Neither is this row's to
        // share.
        Assert.DoesNotContain("sk-live-abc123", content.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("BACKLOG_MCP_TOKEN", content.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("Bearer", content.Body, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class FixedTools(DevToolCatalog catalog) : IDevToolService
    {
        public Task<DevToolCatalog> ListAsync(CancellationToken ct = default) => Task.FromResult(catalog);
        public Task<DevToolActionResult> UpdateAsync(string key, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<DevToolActionResult> UpdateAllAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<DevToolActionResult> EnableAsync(string key, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<DevToolActionResult> DisableAsync(string key, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<DevToolActionResult> AcknowledgeAsync(string key, bool acknowledged, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<DevToolActionResult> CreateCatalogAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<DevToolActionResult> AddAsync(DevToolDraft draft, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<DevToolActionResult> RemoveAsync(string key, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<DevToolActionResult> ImportAsync(string json, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<DevToolActionResult> RemoveCachedVersionAsync(string key, string version, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<DevToolActionResult> RemoveStaleCacheAsync(CancellationToken ct = default) => throw new NotSupportedException();

        /// <summary>A fixed catalog never changes, so nothing is ever raised.
        /// Empty accessors rather than an auto-event, which would be a field
        /// nothing assigns.</summary>
        public event Action? Changed
        {
            add { }
            remove { }
        }
    }
}
