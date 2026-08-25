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
public class DevToolOutputTests
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
        var plugins = DevToolOutput.ParsePluginList(PluginListOutput);

        Assert.Equal("0.4.0", plugins["architecture"]);
        Assert.Equal("0.1.0", plugins["github"]);
        Assert.Equal("0.2.0", plugins["domain-design"]);
    }

    /// <summary>The marketplace a plugin came from is not part of its name: the
    /// catalog keys on <c>winui</c> and would never match <c>winui@win-dev-skills</c>.</summary>
    [Fact]
    public void A_marketplace_suffix_is_not_part_of_the_plugin_name()
    {
        var plugins = DevToolOutput.ParsePluginList(PluginListOutput);

        Assert.Equal("0.4.0", plugins["winui"]);
        Assert.DoesNotContain("winui@win-dev-skills", plugins.Keys);
    }

    /// <summary>The regression the pane reported: the shipped regex made keys out
    /// of the header and the bullet, so every catalog lookup missed and every
    /// plugin read as "not installed".</summary>
    [Fact]
    public void Neither_the_header_nor_the_bullet_becomes_a_plugin()
    {
        var plugins = DevToolOutput.ParsePluginList(PluginListOutput);

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
        var plugins = DevToolOutput.ParsePluginList(PluginListOutput);

        Assert.True(plugins.ContainsKey("winui"));
        Assert.DoesNotContain("disabled", plugins["winui"], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_plugin_without_a_version_still_counts_as_installed()
    {
        var plugins = DevToolOutput.ParsePluginList("""
            Installed plugins:
              • mystery
            """);

        Assert.Equal(DevToolOutput.Installed, plugins["mystery"]);
    }

    [Fact]
    public void The_global_tool_list_reads_package_ids_case_insensitively()
    {
        var tools = DevToolOutput.ParseDotNetToolList(DotNetToolListOutput);

        Assert.Equal("1.0.12", tools["jsdotnet.mcp.guidelines"]);
        Assert.Equal("1.0.12", tools["JSdotNet.MCP.Guidelines"]);
    }

    /// <summary>A prefix match would have taken 1.0.6 from
    /// <c>jsdotnet.project.guidelines.mcpserver</c> and reported an update that
    /// does not exist, so the first column has to match whole.</summary>
    [Fact]
    public void The_tool_search_takes_the_row_whose_id_matches_whole()
    {
        var version = DevToolOutput.ParseDotNetToolSearchVersion(DotNetToolSearchOutput, "JSdotNet.MCP.Guidelines");

        Assert.Equal("1.0.12", version);
    }

    [Fact]
    public void The_tool_search_reports_unknown_for_a_package_it_did_not_list()
    {
        var version = DevToolOutput.ParseDotNetToolSearchVersion(DotNetToolSearchOutput, "JSdotNet.MCP.Missing");

        Assert.Equal(DevToolOutput.Unknown, version);
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
        var parsed = DevToolOutput.ParsePluginSource(source);

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
        Assert.Null(DevToolOutput.ParsePluginSource(source));

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

        Assert.Equal("0.4.0", DevToolOutput.ParsePluginManifestVersion(manifest));
    }

    [Theory]
    [InlineData("")]
    [InlineData("release not found")]
    [InlineData("{ \"name\": \"architecture\" }")]
    [InlineData("{ \"version\": 4 }")]
    public void A_manifest_that_does_not_parse_reports_nothing_rather_than_throwing(string json) =>
        Assert.Null(DevToolOutput.ParsePluginManifestVersion(json));

    /// <summary>The local HEAD and the remote HEAD arrive at different widths —
    /// <c>rev-parse</c> can be asked for a short one, <c>ls-remote</c> cannot —
    /// so both are cut to the same length before anything compares them. Cutting
    /// only one made every repo-backed tool look up to date.</summary>
    [Fact]
    public void A_local_and_a_remote_head_compare_at_the_same_width()
    {
        var local = DevToolOutput.ShortCommit("92f9b2bc987cb1b1db2c32741774ba5e43ddffac");
        var remote = DevToolOutput.ShortCommit("add2b0e0f9351d080b10ca2447f241bd8e87be17");

        Assert.Equal("92f9b2b", local);
        Assert.Equal("add2b0e", remote);
        Assert.True(DevToolInfo.VersionDiffers(local, remote));
    }

    [Fact]
    public void A_head_that_is_already_short_survives_untouched() =>
        Assert.Equal("92f9b2b", DevToolOutput.ShortCommit("  92f9b2b\n"));

    /// <summary>Bare <c>winget list</c> on this machine, verbatim — ten rows kept
    /// out of 145 and nothing else changed.
    ///
    /// <para>Every trap in the format is in here: an <c>Available</c> column that
    /// only some rows fill, synthetic <c>MSIX</c> and <c>ARP</c> ids, an id with
    /// spaces in it (<c>Steam App 1086940</c>), an empty Source, a literal
    /// <c>Unknown</c> where a version should be, a version with a <c>v</c> prefix,
    /// and <c>Microsoft.PowerShell</c> listed twice.</para></summary>
    private const string WingetListOutput = """
        Name                                                         Id                                                                                    Version              Available           Source
        ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------
        App Installer                                                Microsoft.AppInstaller                                                                1.29.290.0                               winget
        Backlog                                                      MSIX\JSdotNet.Backlog.Desktop_0.1.27.0_x64__yfqkkwj0ckctp                             0.1.27.0
        Baldur's Gate 3                                              ARP\Machine\X64\Steam App 1086940                                                     Unknown
        Claude                                                       Anthropic.Claude                                                                      1.34493.1.0                              winget
        Copilot                                                      XP9CXNGPPJ97XX                                                                        152.0.4191.42                            msstore
        Copilot CLI                                                  GitHub.Copilot                                                                        v1.0.65              v1.0.80             winget
        Git                                                          Git.Git                                                                               2.54.0               2.55.0.3            winget
        Microsoft Visual Studio Code (User)                          Microsoft.VisualStudioCode                                                            1.134.0                                  winget
        PowerShell                                                   Microsoft.PowerShell                                                                  7.6.5.0                                  winget
        PowerShell 7.6.5.0-x64                                       Microsoft.PowerShell                                                                  7.6.5.0                                  winget
        """;

    /// <summary><c>winget list --id Microsoft.VisualStudioCode --exact</c>, verbatim.
    /// Nothing here can be upgraded, so winget prints no <c>Available</c> column at
    /// all — the reason a fixed-offset parser cannot work.</summary>
    private const string WingetListWithoutAvailableOutput = """
        Name                                Id                         Version Source
        ------------------------------------------------------------------------------
        Microsoft Visual Studio Code (User) Microsoft.VisualStudioCode 1.134.0 winget
        """;

    /// <summary><c>winget list --id Git.Git --exact</c>, verbatim. Same command as
    /// above, one row, and every column has moved: winget sizes each column to the
    /// widest cell of this invocation, so <c>Name</c> is four characters wide here
    /// and sixty-one wide in the full listing.</summary>
    private const string WingetListNarrowOutput = """
        Name Id      Version Available Source
        -------------------------------------
        Git  Git.Git 2.54.0  2.55.0.3  winget
        """;

    /// <summary><c>winget upgrade</c> on this machine, verbatim, summary line
    /// included.</summary>
    private const string WingetUpgradeOutput = """
        Name                                             Id                                         Version              Available           Source
        -------------------------------------------------------------------------------------------------------------------------------------------
        Copilot CLI                                      GitHub.Copilot                             v1.0.65              v1.0.80             winget
        Docker Desktop                                   Docker.DockerDesktop                       4.79.0               4.88.0              winget
        Git                                              Git.Git                                    2.54.0               2.55.0.3            winget
        GitHub CLI                                       GitHub.cli                                 2.95.0               2.98.0              winget
        Microsoft ODBC Driver 17 for SQL Server          Microsoft.msodbcsql.17                     17.10.6.1            17.11.1.1           winget
        Microsoft Teams                                  Microsoft.Teams                            26149.1205.4798.6437 26198.304.4946.9672 winget
        Microsoft Windows Desktop Runtime - 6.0.11 (x64) Microsoft.DotNet.DesktopRuntime.6          6.0.11               6.0.36              winget
        Node.js                                          OpenJS.NodeJS.LTS                          24.18.0              24.19.0             winget
        Oh My Posh                                       JanDeDobbeleer.OhMyPosh                    29.18.0.0            30.7.0              winget
        Outlook for Windows                              Microsoft.Outlook                          1.2026.609.400       1.2026.812.100      winget
        PowerToys (Preview) x64                          Microsoft.PowerToys                        0.100.1              0.101.2362.0        winget
        Python 3.14.6 (64-bit)                           Python.Python.3.14                         3.14.6               3.14.7              winget
        uv                                               astral-sh.uv                               0.11.25              0.12.5              winget
        Visual Studio Enterprise 2026 Insiders           Microsoft.VisualStudio.Enterprise.Insiders 18.8.11918.235       18.10.12113.136     winget
        Windows App Development CLI                      Microsoft.WinAppCli                        0.4.0.0              0.6.1               winget
        Windows Subsystem for Linux                      Microsoft.WSL                              2.7.10.0             2.7.12              winget
        16 upgrades available.
        """;

    /// <summary><c>winget show --id Microsoft.PowerShell --exact</c>, verbatim as
    /// far as the installer block — which is the half that matters, because it is
    /// the only other place the word Version appears.</summary>
    private const string WingetShowOutput = """
        Found PowerShell [Microsoft.PowerShell]
        Version: 7.6.5.0
        Publisher: Microsoft Corporation
        Publisher Url: https://github.com/PowerShell/PowerShell/
        Publisher Support Url: https://github.com/PowerShell/PowerShell/issues
        Author: Microsoft Corporation
        Moniker: pwsh
        Description:
          PowerShell is a cross-platform (Windows, Linux, and macOS) automation and configuration tool/framework that works well with your existing tools and is optimized for dealing with structured data (e.g. JSON, CSV, XML, etc.), REST APIs, and object models.
          It includes a command-line shell, an associated scripting language and a framework for processing cmdlets.
        Homepage: https://microsoft.com/PowerShell
        License: MIT
        License Url: https://github.com/PowerShell/PowerShell/blob/master/LICENSE.txt
        Copyright: Copyright (c) Microsoft Corporation
        Copyright Url: https://github.com/PowerShell/PowerShell/blob/master/LICENSE.txt
        Release Notes Url: https://github.com/PowerShell/PowerShell/releases/tag/v7.6.5
        Documentation:
          Product Documentation: https://learn.microsoft.com/powershell
          FAQ: https://github.com/PowerShell/PowerShell/blob/master/docs/FAQ.md
        Tags:
          command-line
          cross-platform
          open-source
          powershell
          pwsh
          shell
        Installer:
          Installer Type: msix
        """;

    /// <summary><c>code --list-extensions --show-versions</c> on this machine,
    /// verbatim. The marketplace spells the second one
    /// <c>ms-vscode.PowerShell</c>.</summary>
    private const string VsCodeExtensionListOutput = """
        ms-vscode-remote.remote-containers@0.466.0
        ms-vscode.powershell@2025.4.0
        """;

    /// <summary>A live <c>extensionquery</c> response, pruned to the properties
    /// this reads and to three extensions.
    ///
    /// <para>Captured with the flags the caller sends (914, which includes
    /// <c>IncludeLatestVersionOnly</c>), so each extension carries only its single
    /// newest version — and for two of the three that newest version is a
    /// pre-release.</para></summary>
    private const string MarketplaceLatestOnlyJson = """
        {
          "results": [
            {
              "extensions": [
                {
                  "publisher": {
                    "publisherName": "ms-vscode"
                  },
                  "extensionName": "PowerShell",
                  "versions": [
                    {
                      "version": "2026.1.2",
                      "properties": [
                        {
                          "key": "Microsoft.VisualStudio.Code.PreRelease",
                          "value": "true"
                        }
                      ]
                    }
                  ]
                },
                {
                  "publisher": {
                    "publisherName": "ms-dotnettools"
                  },
                  "extensionName": "csdevkit",
                  "versions": [
                    {
                      "version": "3.29.195",
                      "targetPlatform": "darwin-arm64",
                      "properties": [
                        {
                          "key": "Microsoft.VisualStudio.Code.PreRelease",
                          "value": "true"
                        }
                      ]
                    },
                    {
                      "version": "3.29.195",
                      "targetPlatform": "win32-x64",
                      "properties": [
                        {
                          "key": "Microsoft.VisualStudio.Code.PreRelease",
                          "value": "true"
                        }
                      ]
                    }
                  ]
                },
                {
                  "publisher": {
                    "publisherName": "microsoft-aspire"
                  },
                  "extensionName": "aspire-vscode",
                  "versions": [
                    {
                      "version": "1.19.0"
                    }
                  ]
                }
              ]
            }
          ]
        }
        """;

    /// <summary>The same query without <c>IncludeLatestVersionOnly</c>, pruned the
    /// same way: the full version history, newest first.
    ///
    /// <para>PowerShell's four newest builds are all pre-release and the stable
    /// channel is four releases back at 2025.4.0 — which is exactly what
    /// <c>code --install-extension</c> installed here.</para></summary>
    private const string MarketplaceAllVersionsJson = """
        {
          "results": [
            {
              "extensions": [
                {
                  "publisher": {
                    "publisherName": "ms-vscode"
                  },
                  "extensionName": "PowerShell",
                  "versions": [
                    {
                      "version": "2026.1.2",
                      "properties": [
                        {
                          "key": "Microsoft.VisualStudio.Code.PreRelease",
                          "value": "true"
                        }
                      ]
                    },
                    {
                      "version": "2026.1.1",
                      "properties": [
                        {
                          "key": "Microsoft.VisualStudio.Code.PreRelease",
                          "value": "true"
                        }
                      ]
                    },
                    {
                      "version": "2026.1.0",
                      "properties": [
                        {
                          "key": "Microsoft.VisualStudio.Code.PreRelease",
                          "value": "true"
                        }
                      ]
                    },
                    {
                      "version": "2025.5.0",
                      "properties": [
                        {
                          "key": "Microsoft.VisualStudio.Code.PreRelease",
                          "value": "true"
                        }
                      ]
                    },
                    {
                      "version": "2025.4.0"
                    }
                  ]
                },
                {
                  "publisher": {
                    "publisherName": "microsoft-aspire"
                  },
                  "extensionName": "aspire-vscode",
                  "versions": [
                    {
                      "version": "1.19.0"
                    },
                    {
                      "version": "1.18.0"
                    }
                  ]
                }
              ]
            }
          ]
        }
        """;

    [Fact]
    public void The_winget_list_reads_a_row_into_its_columns()
    {
        var packages = DevToolOutput.ParseWingetList(WingetListOutput);

        var git = packages["Git.Git"];
        Assert.Equal("Git", git.Name);
        Assert.Equal("2.54.0", git.InstalledVersion);
        Assert.Equal("2.55.0.3", git.AvailableVersion);
        Assert.Equal("winget", git.Source);
    }

    /// <summary>The column winget omits is the one that says an update exists, so
    /// reading a missing Available as anything but "no update" would offer an
    /// install of a version nobody published.</summary>
    [Fact]
    public void A_package_with_nothing_to_upgrade_to_has_no_available_version()
    {
        var packages = DevToolOutput.ParseWingetList(WingetListOutput);

        Assert.Null(packages["Microsoft.VisualStudioCode"].AvailableVersion);
        Assert.Equal("1.134.0", packages["Microsoft.VisualStudioCode"].InstalledVersion);
    }

    /// <summary>The Available column is not merely empty for a machine with no
    /// upgrades — the header does not carry it at all, and Source sits where
    /// Available would have started.</summary>
    [Fact]
    public void A_listing_that_prints_no_available_column_still_reads()
    {
        var packages = DevToolOutput.ParseWingetList(WingetListWithoutAvailableOutput);

        var code = packages["Microsoft.VisualStudioCode"];
        Assert.Equal("1.134.0", code.InstalledVersion);
        Assert.Null(code.AvailableVersion);
        Assert.Equal("winget", code.Source);
    }

    /// <summary>The same command against a different package puts every column at a
    /// different offset, because winget sizes them from the widest row it is about
    /// to print. Offsets have to come from this invocation's header.</summary>
    [Fact]
    public void Column_offsets_come_from_the_header_of_this_invocation()
    {
        var packages = DevToolOutput.ParseWingetList(WingetListNarrowOutput);

        var git = packages["Git.Git"];
        Assert.Equal("Git", git.Name);
        Assert.Equal("2.54.0", git.InstalledVersion);
        Assert.Equal("2.55.0.3", git.AvailableVersion);
    }

    /// <summary>A Steam app registered through ARP has spaces in the middle of its
    /// id and no source at all. Splitting a row on whitespace would take
    /// <c>1086940</c> for a version and shift everything after it.</summary>
    [Fact]
    public void An_id_with_spaces_in_it_does_not_disturb_the_rows_around_it()
    {
        var packages = DevToolOutput.ParseWingetList(WingetListOutput);

        var steam = packages[@"ARP\Machine\X64\Steam App 1086940"];
        Assert.Equal("Baldur's Gate 3", steam.Name);
        Assert.Equal("Unknown", steam.InstalledVersion);
        Assert.Equal(string.Empty, steam.Source);
        Assert.Equal("1.34493.1.0", packages["Anthropic.Claude"].InstalledVersion);
    }

    /// <summary>Versions come back as winget printed them. The Copilot CLI's carry
    /// a <c>v</c> and Git's has four components; normalising either would make the
    /// two ends of a comparison disagree about a version they both mean.</summary>
    [Fact]
    public void A_version_is_whatever_winget_printed()
    {
        var packages = DevToolOutput.ParseWingetList(WingetListOutput);

        Assert.Equal("v1.0.65", packages["GitHub.Copilot"].InstalledVersion);
        Assert.Equal("v1.0.80", packages["GitHub.Copilot"].AvailableVersion);
    }

    /// <summary>PowerShell is registered twice on this machine — once as the app
    /// and once as its versioned entry — and both rows carry the same id. Keyed by
    /// id that is a duplicate key, and adding rather than assigning would take the
    /// whole listing down.</summary>
    [Fact]
    public void A_package_listed_twice_is_one_package()
    {
        var packages = DevToolOutput.ParseWingetList(WingetListOutput);

        Assert.Equal("PowerShell", packages["Microsoft.PowerShell"].Name);
        Assert.Equal(9, packages.Count);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("No installed package found matching input criteria.")]
    [InlineData("Name\nId\nVersion")]
    public void A_listing_that_is_not_a_table_reports_nothing_rather_than_throwing(string output) =>
        Assert.Empty(DevToolOutput.ParseWingetList(output));

    [Fact]
    public void The_upgrade_list_reads_every_row_and_no_summary()
    {
        var upgrades = DevToolOutput.ParseWingetUpgrade(WingetUpgradeOutput);

        Assert.Equal(16, upgrades.Count);
        Assert.Equal("2.55.0.3", upgrades["Git.Git"]);
        Assert.Equal("v1.0.80", upgrades["GitHub.Copilot"]);
        Assert.DoesNotContain(upgrades.Keys, key => key.Contains("upgrade", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Teams' version is exactly as wide as the column winget sized for
    /// it, so the cell touches its neighbour with a single space between them.
    /// Slicing one character short here loses a digit off both versions.</summary>
    [Fact]
    public void A_version_that_fills_its_column_is_not_clipped()
    {
        var upgrades = DevToolOutput.ParseWingetUpgrade(WingetUpgradeOutput);

        Assert.Equal("26198.304.4946.9672", upgrades["Microsoft.Teams"]);
    }

    /// <summary>winget prints prose in the middle of a table — a sentence about
    /// packages that need explicit targeting sits between its two upgrade tables —
    /// and a long enough sentence reaches every column offset. This one is a real
    /// line of winget output (the description <c>winget show</c> prints for
    /// PowerShell) standing in for it, because this machine has no second table to
    /// capture. A row has to be shaped like a row, not merely be long.</summary>
    [Fact]
    public void A_sentence_long_enough_to_reach_the_columns_is_not_a_package()
    {
        const string prose = "  PowerShell is a cross-platform (Windows, Linux, and macOS) automation and configuration tool/framework that works well with your existing tools and is optimized for dealing with structured data (e.g. JSON, CSV, XML, etc.), REST APIs, and object models.";

        var packages = DevToolOutput.ParseWingetList(WingetListOutput + "\r\n" + prose);

        Assert.Equal(9, packages.Count);
        Assert.DoesNotContain(packages.Keys, key => key.Contains("automation", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("")]
    [InlineData("No installed package found matching input criteria.")]
    public void An_upgrade_list_with_no_table_reports_nothing_rather_than_throwing(string output) =>
        Assert.Empty(DevToolOutput.ParseWingetUpgrade(output));

    /// <summary>The available version is the one at the top. The installer block
    /// further down describes the same version and never starts a line with
    /// <c>Version:</c>, which is what makes anchoring to the line start
    /// enough.</summary>
    [Fact]
    public void The_show_output_carries_the_available_version() =>
        Assert.Equal("7.6.5.0", DevToolOutput.ParseWingetShowVersion(WingetShowOutput));

    [Theory]
    [InlineData("")]
    [InlineData("No package found matching input criteria.")]
    [InlineData("Found PowerShell [Microsoft.PowerShell]")]
    public void A_show_output_without_a_version_reports_nothing(string output) =>
        Assert.Null(DevToolOutput.ParseWingetShowVersion(output));

    /// <summary>"Not installed" arrives as an exit code and nothing else — the
    /// sentence goes to stdout and stderr stays empty, so the code is the only
    /// signal there is.</summary>
    [Fact]
    public void The_winget_not_found_code_is_recognised()
    {
        Assert.True(DevToolOutput.IsWingetNotFound(-1978335212));
        Assert.False(DevToolOutput.IsWingetNotFound(0));
        Assert.False(DevToolOutput.IsWingetNotFound(1));
    }

    /// <summary>The CLI lowercases what it prints. The catalog spells the id the
    /// way the marketplace does, so a case-sensitive lookup finds nothing and
    /// every extension reads as missing.</summary>
    [Fact]
    public void An_extension_is_found_under_the_casing_the_marketplace_uses()
    {
        var extensions = DevToolOutput.ParseVsCodeExtensionList(VsCodeExtensionListOutput);

        Assert.Equal("2025.4.0", extensions["ms-vscode.PowerShell"]);
        Assert.Equal("0.466.0", extensions["ms-vscode-remote.remote-containers"]);
    }

    /// <summary>Without <c>--show-versions</c> the CLI prints bare ids. The
    /// extension is still installed; only its version is unknown.</summary>
    [Fact]
    public void An_extension_line_without_a_version_still_counts_as_installed()
    {
        var extensions = DevToolOutput.ParseVsCodeExtensionList("ms-dotnettools.csdevkit");

        Assert.Equal(DevToolOutput.Installed, extensions["ms-dotnettools.csdevkit"]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Extensions installed on this machine:")]
    public void An_extension_listing_that_is_not_one_reports_nothing(string output) =>
        Assert.Empty(DevToolOutput.ParseVsCodeExtensionList(output));

    [Fact]
    public void The_marketplace_response_gives_a_latest_version_per_extension()
    {
        var versions = DevToolOutput.ParseMarketplaceExtensionVersions(MarketplaceAllVersionsJson);

        Assert.Equal("1.19.0", versions["microsoft-aspire.aspire-vscode"]);
        Assert.Equal("1.19.0", versions["MICROSOFT-ASPIRE.ASPIRE-VSCODE"]);
    }

    /// <summary>PowerShell's four newest builds are pre-releases nobody gets from
    /// <c>code --install-extension</c>; the stable channel is at 2025.4.0, which is
    /// what is installed here. Taking the top of the list would report an update
    /// that can never be applied, on every refresh, forever.</summary>
    [Fact]
    public void A_pre_release_is_not_the_available_version()
    {
        var versions = DevToolOutput.ParseMarketplaceExtensionVersions(MarketplaceAllVersionsJson);

        Assert.Equal("2025.4.0", versions["ms-vscode.PowerShell"]);
    }

    /// <summary>Asked for the latest version only, the marketplace answers with a
    /// pre-release and nothing else — there is no stable version in the response to
    /// report. Saying nothing lets the row read "version unknown"; saying 2026.1.2
    /// would be a permanent false update.</summary>
    [Fact]
    public void An_extension_whose_only_answer_is_a_pre_release_reports_nothing()
    {
        var versions = DevToolOutput.ParseMarketplaceExtensionVersions(MarketplaceLatestOnlyJson);

        Assert.Equal("1.19.0", versions["microsoft-aspire.aspire-vscode"]);
        Assert.DoesNotContain("ms-vscode.PowerShell", versions.Keys);
        Assert.DoesNotContain("ms-dotnettools.csdevkit", versions.Keys);
    }

    [Theory]
    [InlineData("")]
    [InlineData("<html>Bad Gateway</html>")]
    [InlineData("""{ "results": [] }""")]
    [InlineData("""{ "results": [ { "extensions": [ { "extensionName": "orphan" } ] } ] }""")]
    public void A_marketplace_body_that_does_not_parse_reports_nothing_rather_than_throwing(string json) =>
        Assert.Empty(DevToolOutput.ParseMarketplaceExtensionVersions(json));

    /// <summary>Eight tools, eight ways of saying the same thing, every one of them
    /// captured from this machine.</summary>
    [Theory]
    [InlineData("13.5.2+a22cec24d76e764b3681977e314ab4a0aeed0240", "13.5.2")]
    [InlineData("PowerShell 7.6.5", "7.6.5")]
    [InlineData("gh version 2.95.0 (2026-06-17)\nhttps://github.com/cli/cli/releases/tag/v2.95.0", "2.95.0")]
    [InlineData("git version 2.54.0.windows.1", "2.54.0.windows.1")]
    [InlineData("Docker version 29.5.3, build d1c06ef", "29.5.3")]
    [InlineData("10.0.400", "10.0.400")]
    [InlineData("1.134.0\n110a328ea54b42367b803ec53ee0bf52ef26b419\nx64", "1.134.0")]
    [InlineData("WSL version: 2.7.10.0\r\nKernel version: 6.18.33.2-2", "2.7.10.0")]
    public void A_version_probe_reports_the_version_its_tool_printed(string output, string expected) =>
        Assert.Equal(expected, DevToolOutput.ParseVersionProbe(output));

    /// <summary>Git's version has four components and a word in it. Cutting it to
    /// three would be a version this machine does not have.</summary>
    [Fact]
    public void A_version_that_is_not_three_numbers_survives_whole() =>
        Assert.Equal("2.54.0.windows.1", DevToolOutput.ParseVersionProbe("git version 2.54.0.windows.1"));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("'aspire' is not recognized as an internal or external command,\noperable program or batch file.")]
    public void A_probe_that_printed_no_version_reports_nothing(string output) =>
        Assert.Null(DevToolOutput.ParseVersionProbe(output));

    /// <summary>The Claude desktop app is a host of its own, and its name starts
    /// with the name of the host beside it. Anything matching on a prefix or a
    /// substring would read <c>claude-desktop</c> as the CLI and register a server
    /// with the wrong one of the two.</summary>
    [Fact]
    public void The_desktop_app_is_not_the_cli_beside_it()
    {
        var hosts = DevToolOutput.ParseHosts(["claude-desktop"]);

        Assert.Equal(DevToolHosts.ClaudeDesktop, hosts);
        Assert.False(hosts.HasFlag(DevToolHosts.Claude));
    }

    [Fact]
    public void An_entry_can_name_both_claude_hosts()
    {
        var hosts = DevToolOutput.ParseHosts(["claude", "claude-desktop"]);

        Assert.True(hosts.HasFlag(DevToolHosts.Claude));
        Assert.True(hosts.HasFlag(DevToolHosts.ClaudeDesktop));
        Assert.False(hosts.HasFlag(DevToolHosts.Copilot));
    }

    /// <summary>Silence still means the two hosts every existing entry is silent
    /// about. The desktop app is not one of them: folding it in would make every
    /// entry on every machine claim a registration nobody made.</summary>
    [Fact]
    public void Saying_nothing_still_means_the_two_hosts_it_always_meant()
    {
        Assert.Equal(DevToolHosts.Both, DevToolOutput.ParseHosts(null));
        Assert.Equal(DevToolHosts.Both, DevToolOutput.ParseHosts([]));
        Assert.Equal(DevToolHosts.Both, DevToolOutput.ParseHosts([null, "", "   "]));
    }

    /// <summary>A catalog written for a host this version has not met still
    /// installs the entries it can.</summary>
    [Fact]
    public void A_host_this_version_has_not_met_is_ignored_rather_than_rejected() =>
        Assert.Equal(DevToolHosts.ClaudeDesktop, DevToolOutput.ParseHosts(["cursor", "claude-desktop"]));
}
