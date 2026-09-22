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
    }
}
