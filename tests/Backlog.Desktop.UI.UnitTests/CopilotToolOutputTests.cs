namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The text the Copilot CLI, the dotnet CLI and the GitHub API actually print,
/// read back as data.
///
/// <para>Every input below was captured from a real run rather than written to
/// suit the parser. The pane reported every configured plugin as "not installed"
/// because the shipped regex had been written for a whitespace table the CLI has
/// never printed, and a hand-made fixture would have agreed with that regex just
/// as happily as with the fix.</para>
/// </summary>
public class CopilotToolOutputTests
{
    /// <summary>GitHub Copilot CLI 1.0.80, verbatim.</summary>
    private const string PluginListOutput = """
        Installed plugins:
          • winui@win-dev-skills (v0.4.0) [disabled]
          • github (v0.1.0)
          • copilot-spec-builder (v0.1.0)
          • architecture (v0.4.0)
          • domain-design (v0.2.0)
          • csharp-coding (v0.1.0)
        """;

    /// <summary><c>dotnet tool list --global</c>, verbatim.</summary>
    private const string DotNetToolListOutput = """
        Package Id                                 Version      Commands
        ---------------------------------------------------------------------
        jsdotnet.mcp.guidelines                    1.0.12       jsdotnet-mcp-guidelines
        jsdotnet.mcp.design                        1.0.12       jsdotnet-mcp-design
        """;

    /// <summary><c>dotnet tool search jsdotnet.mcp.guidelines</c>, verbatim —
    /// including the row whose id merely starts the same way.</summary>
    private const string DotNetToolSearchOutput = """
        Package ID                                 Latest Version      Authors              Downloads      Verified
        -----------------------------------------------------------------------------------------------------------
        jsdotnet.mcp.guidelines                    1.0.12              Eduard Keilholz      577
        jsdotnet.project.guidelines.mcpserver      1.0.6               Eduard Keilholz      813
        jsdotnet.mcp.design                        1.0.12              Eduard Keilholz      583
        """;

    [Fact]
    public void The_plugin_list_reads_the_cli_bullet_form()
    {
        var plugins = CopilotToolOutput.ParsePluginList(PluginListOutput);

        Assert.Equal("0.4.0", plugins["architecture"]);
        Assert.Equal("0.1.0", plugins["github"]);
        Assert.Equal("0.2.0", plugins["domain-design"]);
    }

    /// <summary>The marketplace a plugin came from is not part of its name: the
    /// catalog keys on <c>winui</c> and would never match <c>winui@win-dev-skills</c>.</summary>
    [Fact]
    public void A_marketplace_suffix_is_not_part_of_the_plugin_name()
    {
        var plugins = CopilotToolOutput.ParsePluginList(PluginListOutput);

        Assert.Equal("0.4.0", plugins["winui"]);
        Assert.DoesNotContain("winui@win-dev-skills", plugins.Keys);
    }

    /// <summary>The regression the pane reported: the shipped regex made keys out
    /// of the header and the bullet, so every catalog lookup missed and every
    /// plugin read as "not installed".</summary>
    [Fact]
    public void Neither_the_header_nor_the_bullet_becomes_a_plugin()
    {
        var plugins = CopilotToolOutput.ParsePluginList(PluginListOutput);

        Assert.DoesNotContain("Installed", plugins.Keys);
        Assert.DoesNotContain("•", plugins.Keys);
        Assert.Equal(6, plugins.Count);
    }

    /// <summary>The CLI's own <c>[disabled]</c> is not the catalog's answer to
    /// "should this machine have it". Whether a plugin is wanted comes from the
    /// config's <c>enabled</c> flag; the list only says what is on disk.</summary>
    [Fact]
    public void A_disabled_plugin_is_still_installed()
    {
        var plugins = CopilotToolOutput.ParsePluginList(PluginListOutput);

        Assert.True(plugins.ContainsKey("winui"));
        Assert.DoesNotContain("disabled", plugins["winui"], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_plugin_without_a_version_still_counts_as_installed()
    {
        var plugins = CopilotToolOutput.ParsePluginList("""
            Installed plugins:
              • mystery
            """);

        Assert.Equal(CopilotToolOutput.Installed, plugins["mystery"]);
    }

    [Fact]
    public void The_global_tool_list_reads_package_ids_case_insensitively()
    {
        var tools = CopilotToolOutput.ParseDotNetToolList(DotNetToolListOutput);

        Assert.Equal("1.0.12", tools["jsdotnet.mcp.guidelines"]);
        Assert.Equal("1.0.12", tools["JSdotNet.MCP.Guidelines"]);
    }

    /// <summary>A prefix match would have taken 1.0.6 from
    /// <c>jsdotnet.project.guidelines.mcpserver</c> and reported an update that
    /// does not exist, so the first column has to match whole.</summary>
    [Fact]
    public void The_tool_search_takes_the_row_whose_id_matches_whole()
    {
        var version = CopilotToolOutput.ParseDotNetToolSearchVersion(DotNetToolSearchOutput, "JSdotNet.MCP.Guidelines");

        Assert.Equal("1.0.12", version);
    }

    [Fact]
    public void The_tool_search_reports_unknown_for_a_package_it_did_not_list()
    {
        var version = CopilotToolOutput.ParseDotNetToolSearchVersion(DotNetToolSearchOutput, "JSdotNet.MCP.Missing");

        Assert.Equal(CopilotToolOutput.Unknown, version);
    }

    [Theory]
    [InlineData("JSdotNet/Copilot:plugins/architecture", "JSdotNet", "Copilot", "plugins/architecture/.claude-plugin/plugin.json")]
    [InlineData("JSdotNet/Copilot", "JSdotNet", "Copilot", ".claude-plugin/plugin.json")]
    [InlineData("https://github.com/example/test", "example", "test", ".claude-plugin/plugin.json")]
    [InlineData("https://github.com/JSdotNet/Copilot/tree/main/plugins/architecture", "JSdotNet", "Copilot", "plugins/architecture/.claude-plugin/plugin.json")]
    public void A_catalog_source_resolves_to_the_manifest_that_carries_the_version(
        string source,
        string owner,
        string repository,
        string manifestPath)
    {
        var parsed = CopilotToolOutput.ParsePluginSource(source);

        Assert.NotNull(parsed);
        Assert.Equal(owner, parsed.Owner);
        Assert.Equal(repository, parsed.Repository);
        Assert.Equal(manifestPath, parsed.ManifestPath);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-source")]
    [InlineData("https://example.com/owner/repo")]
    public void An_unrecognised_source_resolves_to_nothing(string source) =>
        Assert.Null(CopilotToolOutput.ParsePluginSource(source));

    [Fact]
    public void The_plugin_manifest_carries_the_published_version()
    {
        const string manifest = """
            {
              "name": "architecture",
              "description": "Architecture plugin",
              "version": "0.4.0"
            }
            """;

        Assert.Equal("0.4.0", CopilotToolOutput.ParsePluginManifestVersion(manifest));
    }

    [Theory]
    [InlineData("")]
    [InlineData("release not found")]
    [InlineData("{ \"name\": \"architecture\" }")]
    [InlineData("{ \"version\": 4 }")]
    public void A_manifest_that_does_not_parse_reports_nothing_rather_than_throwing(string json) =>
        Assert.Null(CopilotToolOutput.ParsePluginManifestVersion(json));

    /// <summary>The local HEAD and the remote HEAD arrive at different widths —
    /// <c>rev-parse</c> can be asked for a short one, <c>ls-remote</c> cannot —
    /// so both are cut to the same length before anything compares them. Cutting
    /// only one made every repo-backed tool look up to date.</summary>
    [Fact]
    public void A_local_and_a_remote_head_compare_at_the_same_width()
    {
        var local = CopilotToolOutput.ShortCommit("92f9b2bc987cb1b1db2c32741774ba5e43ddffac");
        var remote = CopilotToolOutput.ShortCommit("add2b0e0f9351d080b10ca2447f241bd8e87be17");

        Assert.Equal("92f9b2b", local);
        Assert.Equal("add2b0e", remote);
        Assert.True(CopilotToolInfo.VersionDiffers(local, remote));
    }

    [Fact]
    public void A_head_that_is_already_short_survives_untouched() =>
        Assert.Equal("92f9b2b", CopilotToolOutput.ShortCommit("  92f9b2b\n"));
}
