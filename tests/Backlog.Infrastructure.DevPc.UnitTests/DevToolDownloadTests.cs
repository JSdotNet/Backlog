using System.Text.Json.Nodes;

namespace Backlog.Infrastructure.DevPc.UnitTests;

/// <summary>
/// The catalog's optional <c>download</c> page: read off an application or an MCP
/// server, written by the Add tool path, kept by an import, and absent — not
/// invented — on every catalog written before the property existed.
/// </summary>
public class DevToolDownloadTests
{
    [Fact]
    public void An_application_entry_reads_its_download_page()
    {
        var root = JsonNode.Parse("""
            {
              "applications": [
                { "id": "Dev.Drive", "provider": "manual", "download": " https://learn.microsoft.com/windows/dev-drive/ ", "enabled": true }
              ]
            }
            """)!;

        var application = Assert.Single(DevToolConfiguration.ReadApplications(root));

        Assert.Equal("https://learn.microsoft.com/windows/dev-drive/", application.Download);
    }

    [Fact]
    public void An_mcp_server_entry_reads_its_download_page()
    {
        var root = JsonNode.Parse("""
            {
              "mcpServers": [
                { "name": "aspire", "command": "aspire", "args": ["mcp", "start"], "download": "https://aspire.dev/get-started/install-cli/", "enabled": true }
              ]
            }
            """)!;

        var server = Assert.Single(DevToolConfiguration.ReadMcpServers(root));

        Assert.Equal("https://aspire.dev/get-started/install-cli/", server.Download);
    }

    /// <summary>Every catalog in existence before this field: the entries read
    /// exactly as they did, with no download page rather than an empty one.</summary>
    [Fact]
    public void An_old_catalog_without_the_field_reads_no_download_page()
    {
        var root = JsonNode.Parse("""
            {
              "mcpServers": [ { "name": "aspire", "command": "aspire", "enabled": true } ],
              "applications": [
                { "id": "Git.Git", "provider": "winget", "enabled": true },
                { "id": "Blank.Download", "provider": "manual", "download": "   ", "enabled": true },
                { "id": "Odd.Download", "provider": "manual", "download": 42, "enabled": true }
              ]
            }
            """)!;

        Assert.All(DevToolConfiguration.ReadApplications(root), application => Assert.Null(application.Download));
        Assert.Null(Assert.Single(DevToolConfiguration.ReadMcpServers(root)).Download);
    }

    [Fact]
    public async Task Adding_an_application_with_a_download_page_round_trips_it()
    {
        var paths = await CreateCatalogWithAsync("""{ "plugins": [], "mcpServers": [] }""");

        await DevToolConfiguration.AddToCatalogAsync(
            paths,
            new DevToolDraft(DevToolKind.Application, "Dev.Drive", DisplayName: "Dev Drive")
            {
                Provider = DevToolProvider.Manual,
                Download = " https://learn.microsoft.com/windows/dev-drive/ "
            }, TestContext.Current.CancellationToken);

        var config = await DevToolConfiguration.ReadAsync(paths, TestContext.Current.CancellationToken);
        var entry = Assert.Single(config.Root["applications"]!.AsArray())!.AsObject();

        Assert.Equal("https://learn.microsoft.com/windows/dev-drive/", entry["download"]!.GetValue<string>());
        Assert.Equal("https://learn.microsoft.com/windows/dev-drive/", Assert.Single(DevToolConfiguration.ReadApplications(config.Root)).Download);
    }

    [Fact]
    public async Task Adding_an_mcp_server_with_a_download_page_round_trips_it()
    {
        var paths = await CreateCatalogWithAsync("""{ "plugins": [], "mcpServers": [] }""");

        await DevToolConfiguration.AddToCatalogAsync(
            paths,
            new DevToolDraft(DevToolKind.McpServer, "aspire")
            {
                McpMechanism = DevToolMcpMechanism.Command,
                ServerCommand = "aspire",
                ServerArgs = ["mcp", "start"],
                Download = "https://aspire.dev/get-started/install-cli/"
            }, TestContext.Current.CancellationToken);

        var config = await DevToolConfiguration.ReadAsync(paths, TestContext.Current.CancellationToken);
        var entry = Assert.Single(config.Root["mcpServers"]!.AsArray())!.AsObject();

        Assert.Equal("https://aspire.dev/get-started/install-cli/", entry["download"]!.GetValue<string>());
        Assert.Equal("https://aspire.dev/get-started/install-cli/", Assert.Single(DevToolConfiguration.ReadMcpServers(config.Root)).Download);
    }

    /// <summary>The format's silence is meaningful: a draft that named no page
    /// writes no property for the next reader to explain.</summary>
    [Fact]
    public async Task Adding_without_a_download_page_writes_no_property()
    {
        var paths = await CreateCatalogWithAsync("""{ "plugins": [], "mcpServers": [] }""");

        await DevToolConfiguration.AddToCatalogAsync(
            paths,
            new DevToolDraft(DevToolKind.Application, "Git.Git") { Provider = DevToolProvider.Winget, Download = "  " },
            TestContext.Current.CancellationToken);

        var config = await DevToolConfiguration.ReadAsync(paths, TestContext.Current.CancellationToken);
        var entry = Assert.Single(config.Root["applications"]!.AsArray())!.AsObject();

        Assert.False(entry.ContainsKey("download"));
        Assert.Null(Assert.Single(DevToolConfiguration.ReadApplications(config.Root)).Download);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("file:///C:/Windows")]
    [InlineData("not a url")]
    [InlineData("/relative/page")]
    public async Task Adding_a_download_page_that_is_not_http_is_refused(string download)
    {
        var paths = await CreateCatalogWithAsync("""{ "plugins": [], "mcpServers": [] }""");

        var refused = await Assert.ThrowsAsync<InvalidOperationException>(() => DevToolConfiguration.AddToCatalogAsync(
            paths,
            new DevToolDraft(DevToolKind.Application, "Dev.Drive") { Provider = DevToolProvider.Manual, Download = download },
            TestContext.Current.CancellationToken));

        Assert.Equal(DevToolConfiguration.DownloadNotLinkableMessage, refused.Message);
    }

    [Fact]
    public async Task An_import_keeps_the_download_page()
    {
        var paths = await CreateCatalogWithAsync("""{ "plugins": [], "mcpServers": [] }""");

        await DevToolConfiguration.ImportCatalogAsync(paths, """
            {
              "applications": [
                { "id": "Dev.Drive", "provider": "manual", "download": "https://learn.microsoft.com/windows/dev-drive/", "enabled": true }
              ]
            }
            """, TestContext.Current.CancellationToken);

        var config = await DevToolConfiguration.ReadAsync(paths, TestContext.Current.CancellationToken);

        Assert.Equal("https://learn.microsoft.com/windows/dev-drive/", Assert.Single(DevToolConfiguration.ReadApplications(config.Root)).Download);
    }

    [Theory]
    [InlineData("https://example.com/download", true)]
    [InlineData("http://example.com/", true)]
    [InlineData("HTTPS://EXAMPLE.COM/", true)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("file:///C:/Windows", false)]
    [InlineData("ftp://example.com/file", false)]
    [InlineData("example.com", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Only_an_http_or_https_address_is_a_linkable_download_page(string? download, bool expected)
    {
        Assert.Equal(expected, DevToolConfiguration.IsLinkableDownload(download));
    }

    private static async Task<DevToolConfigurationPaths> CreateCatalogWithAsync(string json)
    {
        var root = Path.Combine(Path.GetTempPath(), "backlog-tool-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, ".tools"));
        var paths = DevToolConfigurationPaths.FromRepositoryRoot(root, "dev-pc");
        await File.WriteAllTextAsync(paths.CatalogPath, json);
        return paths;
    }
}
