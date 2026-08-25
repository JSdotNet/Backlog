using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The half of the application providers that can be checked without a machine to
/// check it on: how a declared command is launched, what arguments the package
/// managers are handed, which array a key routes to, and what a write to the
/// Claude desktop app's own config leaves behind.
///
/// <para>The process launching itself has no seam and is deliberately not mocked
/// — the same call this class makes about the parsers. What is here instead is
/// every fact that a mocked process would have agreed with just as happily as the
/// real one: that <c>--silent</c> is not <c>-s</c>, that <c>code</c> is not an
/// executable, that <c>flags</c> is 402, and that a settings file must come back
/// with the settings still in it.</para>
/// </summary>
public class DevToolApplicationTests
{
    // ---------------------------------------------------------------- launching

    /// <summary><c>code</c> resolves to <c>bin\code.cmd</c>, and a redirected
    /// launch cannot use the shell — so starting it directly throws before there
    /// is any output to explain why.</summary>
    [Fact]
    public void A_shell_command_is_launched_through_cmd_rather_than_directly()
    {
        var spec = DevToolCommands.VsCodeExtensionList();

        Assert.Equal("cmd.exe", spec.FileName);
        Assert.Equal(["/c", "code", "--list-extensions", "--show-versions"], spec.LaunchArguments);
    }

    /// <summary>The command and its arguments stay separate items, so a path with
    /// a space in it survives. Hand-quoting is what loses one of those.</summary>
    [Fact]
    public void A_shell_command_keeps_every_argument_as_its_own_item()
    {
        var spec = new DevToolCommandSpec("my tool", ["--path", @"C:\Program Files\thing"], Shell: true);

        Assert.Equal(["/c", "my tool", "--path", @"C:\Program Files\thing"], spec.LaunchArguments);
    }

    [Fact]
    public void A_plain_command_is_launched_as_itself()
    {
        var spec = new DevToolCommandSpec("aspire", ["--version"]);

        Assert.Equal("aspire", spec.FileName);
        Assert.Equal(["--version"], spec.LaunchArguments);
    }

    /// <summary>winget writes UTF-8 to a redirected pipe whatever the console code
    /// page is, so the default has to stay UTF-8 — a code-page reader turns its
    /// © into ┬®.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("utf-8")]
    [InlineData("UTF8")]
    public void An_unspecified_encoding_is_utf8(string? declared)
    {
        var spec = new DevToolCommandSpec("winget", ["list"], Encoding: declared);

        Assert.Equal(Encoding.UTF8, spec.OutputEncoding);
    }

    /// <summary><c>wsl.exe</c> writes UTF-16LE with no BOM, and a UTF-8 reader
    /// hands the parser <c>W\0S\0L\0</c> — output that reads as nothing at all
    /// rather than as an error anyone would see.</summary>
    [Theory]
    [InlineData("utf-16le")]
    [InlineData("UTF-16")]
    [InlineData("unicode")]
    public void A_utf16_command_is_read_as_utf16(string declared)
    {
        var spec = new DevToolCommandSpec("wsl", ["--version"], Encoding: declared);

        Assert.Equal(Encoding.Unicode, spec.OutputEncoding);
    }

    /// <summary>The value comes out of a hand-edited catalog, so a typo costs one
    /// command its reader rather than throwing out of the middle of a probe.</summary>
    [Fact]
    public void An_encoding_nobody_recognises_falls_back_to_utf8()
    {
        var spec = new DevToolCommandSpec("wsl", ["--version"], Encoding: "utf-27");

        Assert.Equal(Encoding.UTF8, spec.OutputEncoding);
    }

    // ------------------------------------------------------------------- winget

    /// <summary>
    /// <c>--silent</c> is <c>-h</c> and <c>-s</c> is <c>--source</c>. The two are
    /// one keystroke apart in a line where the wrong one installs from the
    /// Microsoft Store, which needs a signed-in Store account and fails without
    /// one.
    /// </summary>
    [Fact]
    public void A_winget_install_is_silent_exact_and_from_the_winget_source()
    {
        var args = DevToolCommands.WingetInstall("Microsoft.VisualStudioCode").Args;

        Assert.Equal("install", args[0]);
        Assert.Contains("--silent", args);
        Assert.Contains("--exact", args);
        Assert.Contains("--disable-interactivity", args);
        Assert.Contains("--accept-source-agreements", args);
        Assert.Contains("--accept-package-agreements", args);
        Assert.DoesNotContain("-s", args);
        Assert.DoesNotContain("-h", args);

        Assert.Equal("winget", args[Array.IndexOf([.. args], "--source") + 1]);
        Assert.Equal("Microsoft.VisualStudioCode", args[Array.IndexOf([.. args], "--id") + 1]);
    }

    /// <summary>A real catalog id carries a literal <c>+</c>. It reaches the
    /// process through <c>ArgumentList</c> as one item, so nothing has to escape
    /// it.</summary>
    [Fact]
    public void A_winget_id_with_a_plus_in_it_stays_one_argument()
    {
        var args = DevToolCommands.WingetInstall("Microsoft.VCRedist.2015+.x64").LaunchArguments;

        Assert.Contains("Microsoft.VCRedist.2015+.x64", args);
    }

    /// <summary>Both listings are unfiltered, which is what makes the whole
    /// catalog cost two process launches instead of one per row.</summary>
    [Fact]
    public void The_two_batched_winget_listings_carry_no_package_filter()
    {
        var list = DevToolCommands.WingetList();
        var upgrade = DevToolCommands.WingetUpgrade();

        Assert.DoesNotContain("--id", list.Args);
        Assert.DoesNotContain("--id", upgrade.Args);
        Assert.Contains("--disable-interactivity", list.Args);
        Assert.Contains("--accept-source-agreements", list.Args);

        // Without it, a package whose installed version winget cannot read drops
        // out of the upgrade listing entirely — and those are exactly the MSIX and
        // click-to-run entries that most often have one.
        Assert.Contains("--include-unknown", upgrade.Args);
    }

    [Fact]
    public void A_winget_show_asks_for_one_exact_package()
    {
        var args = DevToolCommands.WingetShow("Anthropic.Claude").Args;

        Assert.Equal("show", args[0]);
        Assert.Contains("--exact", args);
        Assert.Equal("Anthropic.Claude", args[Array.IndexOf([.. args], "--id") + 1]);
    }

    // -------------------------------------------------------------- marketplace

    /// <summary>
    /// 402 and not 914. The extra bit in 914 is <c>IncludeLatestVersionOnly</c>,
    /// which answers with the newest build of any channel — a pre-release for
    /// <c>ms-vscode.PowerShell</c>, while the stable channel the CLI installs sits
    /// several versions below. That is a permanent update offer for a version the
    /// install can never deliver.
    /// </summary>
    [Fact]
    public void The_marketplace_query_asks_for_every_version_rather_than_the_latest_one()
    {
        var query = JsonNode.Parse(DevToolCommands.MarketplaceExtensionQuery(["ms-dotnettools.csharp"]))!;

        Assert.Equal(402, (int)query["flags"]!);
    }

    /// <summary>One call for the whole column: several criteria go in one filter,
    /// so the Available version of thirteen extensions is one round trip.</summary>
    [Fact]
    public void Every_extension_goes_into_one_query()
    {
        var query = JsonNode.Parse(DevToolCommands.MarketplaceExtensionQuery(
        [
            "ms-dotnettools.csharp",
            "ms-vscode.PowerShell",
            "microsoft-aspire.aspire-vscode"
        ]))!;

        var filters = query["filters"]!.AsArray();
        Assert.Single(filters);

        var filter = filters[0]!;
        var criteria = filter["criteria"]!.AsArray();

        Assert.Equal(3, criteria.Count);
        Assert.Equal(3, (int)filter["pageSize"]!);
        Assert.Equal(1, (int)filter["pageNumber"]!);

        // filterType 7 is an exact publisher.name match. Anything else matches by
        // text and brings back the wrong extension.
        Assert.All(criteria, criterion => Assert.Equal(7, (int)criterion!["filterType"]!));
        Assert.Equal(
            ["ms-dotnettools.csharp", "ms-vscode.PowerShell", "microsoft-aspire.aspire-vscode"],
            criteria.Select(criterion => (string)criterion!["value"]!));
    }

    /// <summary>The shape the parser beside it expects, proven end to end rather
    /// than by two files agreeing about a field name.</summary>
    [Fact]
    public void The_query_and_the_response_parser_agree_on_the_extension_id()
    {
        var response = """
            {
              "results": [
                {
                  "extensions": [
                    {
                      "publisher": { "publisherName": "ms-vscode" },
                      "extensionName": "PowerShell",
                      "versions": [
                        { "version": "2026.1.2", "properties": [ { "key": "Microsoft.VisualStudio.Code.PreRelease", "value": "true" } ] },
                        { "version": "2025.4.0", "properties": [] }
                      ]
                    }
                  ]
                }
              ]
            }
            """;

        var versions = DevToolOutput.ParseMarketplaceExtensionVersions(response);

        // The CLI lowercases what it prints, so the lookup has to be blind to case
        // in both directions.
        Assert.Equal("2025.4.0", versions["ms-vscode.powershell"]);
    }

    // ---------------------------------------------------------------- routing

    [Theory]
    [InlineData("plugin:architecture", DevToolKind.Plugin)]
    [InlineData("mcp:jsdotnet.mcp.design", DevToolKind.McpServer)]
    [InlineData("marketplace:jsdotnet-copilot", DevToolKind.Marketplace)]
    [InlineData("app:Microsoft.VisualStudioCode", DevToolKind.Application)]
    public void A_key_routes_to_the_kind_that_minted_it(string key, DevToolKind expected) =>
        Assert.Equal(expected, DevToolConfiguration.KindOf(key));

    /// <summary>The routing this replaced was a ternary whose else was "MCP
    /// server", so an app key was not rejected by it — it was run as an MCP server
    /// against an entry that has no packageId.</summary>
    [Fact]
    public void An_application_key_does_not_route_to_the_mcp_servers()
    {
        var key = DevToolConfiguration.KeyFor(DevToolKind.Application, "Microsoft.VisualStudioCode");

        Assert.NotEqual(DevToolKind.McpServer, DevToolConfiguration.KindOf(key));
        Assert.Equal(("applications", "id", "Microsoft.VisualStudioCode"), DevToolConfiguration.ParseKey(key));
    }

    [Fact]
    public void A_key_with_no_known_prefix_is_refused() =>
        Assert.Throws<ArgumentException>(() => DevToolConfiguration.KindOf("widget:thing"));

    // ------------------------------------------------- Claude Desktop config

    /// <summary>The config the app really writes: a settings store that happens to
    /// hold a server list, with the server list absent until something registers
    /// one.</summary>
    private const string ClaudeDesktopConfig = """
        {
          "preferences": { "theme": "dark", "sendAnalytics": false },
          "coworkUserFilesPath": "D:\\Cowork",
          "globalShortcut": "Ctrl+Alt+Space",
          "features": { "betaThinking": true }
        }
        """;

    /// <summary>
    /// The one that matters. This file is the person's entire Claude Desktop
    /// settings store, and a writer that emitted only <c>mcpServers</c> would be
    /// indistinguishable from a correct one until somebody noticed their
    /// preferences, their shortcut and their Cowork path had gone.
    /// </summary>
    [Fact]
    public void Registering_a_server_leaves_every_other_setting_exactly_as_it_was()
    {
        var root = JsonNode.Parse(ClaudeDesktopConfig)!.AsObject();

        var merged = DevToolClaudeDesktopConfig.MergeServer(root, "guidelines", "jsdotnet-mcp-guidelines", ["--stdio"]);

        Assert.Equal("dark", (string)merged["preferences"]!["theme"]!);
        Assert.False((bool)merged["preferences"]!["sendAnalytics"]!);
        Assert.Equal(@"D:\Cowork", (string)merged["coworkUserFilesPath"]!);
        Assert.Equal("Ctrl+Alt+Space", (string)merged["globalShortcut"]!);
        Assert.True((bool)merged["features"]!["betaThinking"]!);
    }

    /// <summary>An absent <c>mcpServers</c> is zero servers, not a broken file:
    /// the app omits the property entirely until the first registration.</summary>
    [Fact]
    public void A_config_with_no_server_list_gets_one()
    {
        var root = JsonNode.Parse(ClaudeDesktopConfig)!.AsObject();

        Assert.Null(DevToolClaudeDesktopConfig.ReadServer(root, "guidelines"));

        DevToolClaudeDesktopConfig.MergeServer(root, "guidelines", "jsdotnet-mcp-guidelines");

        Assert.Equal("jsdotnet-mcp-guidelines", DevToolClaudeDesktopConfig.ReadServer(root, "guidelines")!.Command);
    }

    /// <summary>A second server is added beside the first rather than in place of
    /// it. The list is an object keyed by name, and rewriting it whole is the
    /// other way to lose somebody's registrations.</summary>
    [Fact]
    public void A_second_server_joins_the_first()
    {
        var root = JsonNode.Parse(ClaudeDesktopConfig)!.AsObject();

        DevToolClaudeDesktopConfig.MergeServer(root, "design", "jsdotnet-mcp-design");
        DevToolClaudeDesktopConfig.MergeServer(root, "guidelines", "jsdotnet-mcp-guidelines", ["--stdio"]);

        Assert.Equal("jsdotnet-mcp-design", DevToolClaudeDesktopConfig.ReadServer(root, "design")!.Command);
        Assert.Equal("jsdotnet-mcp-guidelines --stdio", DevToolClaudeDesktopConfig.ReadServer(root, "guidelines")!.CommandLine);
    }

    /// <summary>The app's own validator takes stdio servers and nothing else, and
    /// is not strict — so a <c>type</c> or a <c>url</c> written here would be
    /// stripped silently rather than reported.</summary>
    [Fact]
    public void A_registration_carries_only_what_the_app_accepts()
    {
        var root = new JsonObject();

        DevToolClaudeDesktopConfig.MergeServer(
            root,
            "guidelines",
            "jsdotnet-mcp-guidelines",
            ["--stdio"],
            new Dictionary<string, string> { ["JSDOTNET_TOKEN"] = "abc" });

        var entry = root["mcpServers"]!["guidelines"]!.AsObject();

        Assert.Equal(["command", "args", "env"], entry.Select(property => property.Key));
        Assert.Equal("abc", (string)entry["env"]!["JSDOTNET_TOKEN"]!);
    }

    /// <summary>No arguments means no <c>args</c> property, rather than an empty
    /// array the app would have to ignore.</summary>
    [Fact]
    public void A_server_with_no_arguments_writes_only_a_command()
    {
        var root = new JsonObject();

        DevToolClaudeDesktopConfig.MergeServer(root, "guidelines", "jsdotnet-mcp-guidelines");

        Assert.Equal(["command"], root["mcpServers"]!["guidelines"]!.AsObject().Select(property => property.Key));
    }

    [Fact]
    public void Removing_a_server_leaves_the_other_settings_and_the_other_servers()
    {
        var root = JsonNode.Parse(ClaudeDesktopConfig)!.AsObject();
        DevToolClaudeDesktopConfig.MergeServer(root, "design", "jsdotnet-mcp-design");
        DevToolClaudeDesktopConfig.MergeServer(root, "guidelines", "jsdotnet-mcp-guidelines");

        DevToolClaudeDesktopConfig.RemoveServer(root, "guidelines");

        Assert.Null(DevToolClaudeDesktopConfig.ReadServer(root, "guidelines"));
        Assert.Equal("jsdotnet-mcp-design", DevToolClaudeDesktopConfig.ReadServer(root, "design")!.Command);
        Assert.Equal("Ctrl+Alt+Space", (string)root["globalShortcut"]!);
    }

    /// <summary>Round-tripped through the serializer the writer uses, because a
    /// merge that only survives in memory is not the property being claimed.</summary>
    [Fact]
    public void The_merged_document_survives_being_written_out()
    {
        var root = JsonNode.Parse(ClaudeDesktopConfig)!.AsObject();
        DevToolClaudeDesktopConfig.MergeServer(root, "guidelines", "jsdotnet-mcp-guidelines", ["--stdio"]);

        var written = JsonSerializer.Serialize(root, new JsonSerializerOptions { WriteIndented = true });
        var reread = JsonNode.Parse(written)!.AsObject();

        Assert.Equal("dark", (string)reread["preferences"]!["theme"]!);
        Assert.Equal("jsdotnet-mcp-guidelines --stdio", DevToolClaudeDesktopConfig.ReadServer(reread, "guidelines")!.CommandLine);
    }

    /// <summary>Roaming first, then the MSIX redirection of that same folder, then
    /// the older layout. A machine that has been upgraded can carry more than one
    /// of them, so the order is the answer rather than a preference.</summary>
    [Fact]
    public void The_config_is_looked_for_in_the_roaming_folder_first()
    {
        var candidates = DevToolClaudeDesktopConfig.ConfigPathCandidates(@"C:\Users\x\AppData\Roaming", @"C:\Users\x\AppData\Local");

        Assert.Equal(
        [
            @"C:\Users\x\AppData\Roaming\Claude\claude_desktop_config.json",
            @"C:\Users\x\AppData\Local\Packages\Claude_pzs8sxrjxfjjc\LocalCache\Roaming\Claude\claude_desktop_config.json",
            @"C:\Users\x\AppData\Local\Claude-Data\claude_desktop_config.json"
        ],
            candidates);
    }

    /// <summary>Silence means both AI hosts and deliberately not the desktop app:
    /// every entry on every machine is silent, and folding it in would make each
    /// of them claim a registration that was never made.</summary>
    [Fact]
    public void The_desktop_host_has_to_be_asked_for()
    {
        Assert.False(DevToolHosts.Both.HasFlag(DevToolHosts.ClaudeDesktop));
        Assert.True(DevToolOutput.ParseHosts(["claude", "claude-desktop"]).HasFlag(DevToolHosts.ClaudeDesktop));
        Assert.False(DevToolOutput.ParseHosts(["claude"]).HasFlag(DevToolHosts.ClaudeDesktop));
    }

    // -------------------------------------------------------- checklist rows

    /// <summary>
    /// A checklist row and a detect-only package are both permanently "not
    /// installed", and without <see cref="DevToolInfo.Installable"/> both offered
    /// an Install that had nothing to run — and reported the nothing as a failure,
    /// on every sweep, forever.
    /// </summary>
    [Fact]
    public void A_row_with_no_mechanism_offers_nothing()
    {
        var row = ChecklistRow() with { Installable = false };

        Assert.False(row.CanInstall);
        Assert.False(row.CanUpdate);
    }

    [Fact]
    public void A_row_with_a_mechanism_still_offers_an_install_when_it_is_missing()
    {
        Assert.True(ChecklistRow().CanInstall);
    }

    /// <summary>"Up to date" is a claim about something somebody found. A probe
    /// answered by a substring in prose has no version on either side, and a dash
    /// compared against a dash would report the row current whether or not the
    /// machine had done the thing.</summary>
    [Fact]
    public void A_row_with_no_version_at_all_is_not_a_row_that_was_checked()
    {
        var row = ChecklistRow() with
        {
            Installed = true,
            InstalledVersion = DevToolOutput.NoVersion,
            AvailableVersion = DevToolOutput.NoVersion
        };

        Assert.False(row.AvailableVersionKnown);
        Assert.False(row.UpdateAvailable);
    }

    /// <summary>Nothing checked this machine; somebody ticked a box on it. The two
    /// stay separate properties because collapsing them is the lie the whole
    /// manual provider exists to avoid.</summary>
    [Fact]
    public void An_acknowledgement_is_not_an_installation()
    {
        var row = ChecklistRow() with { Acknowledged = true, Installed = false };

        Assert.True(row.Acknowledged);
        Assert.False(row.Installed);
    }

    private static DevToolInfo ChecklistRow() => new(
        DevToolConfiguration.KeyFor(DevToolKind.Application, "dev-drive"),
        DevToolKind.Application,
        "Dev Drive configured",
        "command",
        ConfiguredEnabled: true,
        Installed: false,
        DevToolOutput.NotInstalled,
        DevToolOutput.Unknown,
        "Checklist item: not done yet")
    {
        Hosts = DevToolHosts.None
    };

    // ------------------------------------------------------- catalog entries

    /// <summary>A checklist row is a command entry that declined to say how to
    /// install itself, rather than a fourth provider every switch would have to
    /// know about.</summary>
    [Fact]
    public void An_entry_with_no_install_block_reads_as_a_checklist_row()
    {
        var entry = JsonNode.Parse("""
            {
              "id": "dev-drive",
              "name": "Dev Drive configured",
              "provider": "command",
              "enabled": true,
              "detect": { "command": "fsutil", "args": ["devdrv", "query", "D:"], "expect": "trusted Dev Drive" }
            }
            """)!.AsObject();

        var application = DevToolConfiguration.ReadApplication(entry)!;

        Assert.Equal(DevToolProvider.Command, application.Provider);
        Assert.Null(application.Install);
        Assert.Equal("trusted Dev Drive", application.Detect!.Expect);
        Assert.True(application.ProviderRecognised);
    }

    /// <summary>A provider nobody here knows parks on manual, so nothing is run on
    /// its behalf — and the row still appears, because a dropped entry would hide
    /// a typo in a hand-edited file behind an inventory that looked complete.</summary>
    [Fact]
    public void An_unknown_provider_runs_nothing_and_says_so()
    {
        var entry = JsonNode.Parse("""
            { "id": "thing", "name": "Thing", "provider": "chocolatey", "enabled": true }
            """)!.AsObject();

        var application = DevToolConfiguration.ReadApplication(entry)!;

        Assert.Equal(DevToolProvider.Manual, application.Provider);
        Assert.False(application.ProviderRecognised);
        Assert.Equal("chocolatey", application.DeclaredProvider);
    }

    /// <summary>The two facts about running a process that only the entry knows,
    /// read straight off the catalog rather than guessed per command.</summary>
    [Fact]
    public void A_declared_command_carries_its_shell_and_its_encoding()
    {
        var entry = JsonNode.Parse("""
            {
              "id": "wsl",
              "name": "WSL",
              "provider": "command",
              "enabled": true,
              "detect": { "command": "wsl", "args": ["--version"], "encoding": "utf-16le" },
              "install": { "command": "code", "args": ["--install-extension", "x"], "shell": true }
            }
            """)!.AsObject();

        var application = DevToolConfiguration.ReadApplication(entry)!;

        Assert.Equal(Encoding.Unicode, application.Detect!.OutputEncoding);
        Assert.Equal("wsl", application.Detect.FileName);
        Assert.Equal("cmd.exe", application.Install!.FileName);
        Assert.Equal(["/c", "code", "--install-extension", "x"], application.Install.LaunchArguments);
    }
}
