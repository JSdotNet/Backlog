using Backlog.Modules.DevPc.UI;
using Bunit;
using static Backlog.Modules.DevPc.UnitTests.ToolsPaneTests;

namespace Backlog.Modules.DevPc.UnitTests;

/// <summary>
/// The pane's Overview tab (plan +system-tools-redesign, step 3): the four headline
/// tiles, Needs you with its order and its one control per card, the empty
/// "Everything matches" state, By kind, the host filter over every figure, and the
/// Last run card with and without a record.
/// </summary>
public sealed class ToolsPaneOverviewTests
{
    // ------------------------------------------------------------ tiles

    /// <summary>Matches the catalog is n of m over the rows the machine wants, with
    /// the stacked bar's parts said in words. A switched-off row is neither.</summary>
    [Fact]
    public void The_matches_tile_counts_matches_of_the_rows_the_machine_wants()
    {
        using var context = Context(Mixed());
        var pane = context.Render<ToolsPane>();

        Assert.Equal("3 of 7", Text(pane, "tools-overview-tile-matches-value"));
        Assert.Equal("3 match, 2 updates, 1 missing, 1 unconfirmed", Text(pane, "tools-overview-stack-text"));
        Assert.Equal("true", pane.Find("[data-testid='tools-overview-stack']").GetAttribute("aria-hidden"));
    }

    /// <summary>Updates counts the rows behind and splits them by host: Copilot,
    /// Claude, and the machine's own software.</summary>
    [Fact]
    public void The_updates_tile_counts_updates_and_splits_them_by_host()
    {
        using var context = Context(Mixed());
        var pane = context.Render<ToolsPane>();

        Assert.Equal("2", Text(pane, "tools-overview-tile-updates-value"));
        Assert.Equal(
            ["Copilot 0", "Claude 1", "Machine 1"],
            pane.FindAll("[data-testid='tools-overview-updates-split'] li").Select(line => Squash(line.TextContent)));
    }

    /// <summary>Every row behind counts on exactly one line, so the split adds up to
    /// the tile: a row for both hosts goes to the one host that reports it, and to
    /// Both when both or neither do.</summary>
    [Fact]
    public void The_updates_split_counts_each_row_once_and_adds_up_to_the_tile()
    {
        using var context = Context(BehindOnEveryHost());
        var pane = context.Render<ToolsPane>();

        Assert.Equal("7", Text(pane, "tools-overview-tile-updates-value"));
        Assert.Equal(
            ["Copilot 2", "Claude 2", "Both 2", "Machine 1"],
            pane.FindAll("[data-testid='tools-overview-updates-split'] li").Select(line => Squash(line.TextContent)));
        Assert.Equal(7, SplitSum(pane));
    }

    /// <summary>Under a host filter the filter's host always shows, and a line it
    /// would not show appears when it has a row — a both-hosts row only Copilot
    /// reports, under Claude — so the split still adds up to the tile.</summary>
    [Fact]
    public void The_updates_split_under_a_host_filter_still_adds_up_to_the_tile()
    {
        using var context = Context(BehindOnEveryHost());
        var pane = context.Render<ToolsPane>();

        pane.Find("[data-testid='tools-host-claude']").Click();

        Assert.Equal("5", Text(pane, "tools-overview-tile-updates-value"));
        Assert.Equal(
            ["Copilot 1", "Claude 2", "Both 2"],
            pane.FindAll("[data-testid='tools-overview-updates-split'] li").Select(line => Squash(line.TextContent)));
        Assert.Equal(5, SplitSum(pane));

        pane.Find("[data-testid='tools-host-copilot']").Click();

        Assert.Equal("5", Text(pane, "tools-overview-tile-updates-value"));
        Assert.Equal(
            ["Copilot 2", "Claude 1", "Both 2"],
            pane.FindAll("[data-testid='tools-overview-updates-split'] li").Select(line => Squash(line.TextContent)));
        Assert.Equal(5, SplitSum(pane));
    }

    /// <summary>Missing counts the rows the machine is short of, and how many of them
    /// install from here against by hand.</summary>
    [Fact]
    public void The_missing_tile_splits_installs_from_here_and_by_hand()
    {
        using var context = Context(FakeDevToolService.With(
            Tool("plugin:absent", "absent") with { Installed = false, InstalledVersion = "not installed" },
            Application("Docker.DockerDesktop", "Docker Desktop", installed: false, installable: false, installedVersion: "not installed")));
        var pane = context.Render<ToolsPane>();

        Assert.Equal("2", Text(pane, "tools-overview-tile-missing-value"));
        Assert.Equal("1 install from here · 1 by hand", Text(pane, "tools-overview-missing-split"));
    }

    /// <summary>Stale cache counts the cached versions nothing loads.</summary>
    [Fact]
    public void The_stale_cache_tile_counts_cached_versions_nothing_loads()
    {
        using var context = Context(FakeDevToolService.With(Cached(stale: 2, inUse: 1)));
        var pane = context.Render<ToolsPane>();

        Assert.Equal("2", Text(pane, "tools-overview-tile-stale-value"));
    }

    // ------------------------------------------------------------ needs you

    /// <summary>One card per row needing action, ordered updates, missing,
    /// unconfirmed, stale cache — whatever order the catalog lists them in.</summary>
    [Fact]
    public void Needs_you_orders_updates_missing_unconfirmed_and_the_stale_cache()
    {
        using var context = Context(FakeDevToolService.With(
            ManualApplication(acknowledged: false),
            Application("Docker.DockerDesktop", "Docker Desktop", installed: false, installable: false, installedVersion: "not installed"),
            Tool("plugin:absent", "absent") with { Installed = false, InstalledVersion = "not installed" },
            Behind("plugin:delivery", "delivery", DevToolHosts.Default),
            Cached(stale: 1, inUse: 1)));
        var pane = context.Render<ToolsPane>();

        Assert.Equal(
            [
                "Update delivery",
                "Install Docker Desktop by hand",
                "Install absent",
                "Confirm Office apps activated and signed in",
                "Clear the stale cache"
            ],
            pane.FindAll("[data-testid='tools-overview-need-title']").Select(title => title.TextContent.Trim()));
    }

    /// <summary>Each card carries exactly one control, the one its row needs.</summary>
    [Fact]
    public void Each_card_carries_the_one_control_its_row_needs()
    {
        using var context = Context(FakeDevToolService.With(
            Behind("plugin:delivery", "delivery", DevToolHosts.Default),
            Tool("plugin:absent", "absent") with { Installed = false, InstalledVersion = "not installed" },
            Application("Docker.DockerDesktop", "Docker Desktop", installed: false, installable: false, installedVersion: "not installed"),
            ManualApplication(acknowledged: false),
            Cached(stale: 1, inUse: 1)));
        var pane = context.Render<ToolsPane>();

        var cards = pane.FindAll("[data-testid='tools-overview-need']");
        Assert.All(cards, card => Assert.Single(card.QuerySelectorAll(".tools-overview-need__control > *")));

        Assert.Equal("Update", Card(pane, "plugin:delivery").QuerySelector("[data-testid='tools-overview-need-update']")!.TextContent.Trim());
        Assert.Equal("Install", Card(pane, "plugin:absent").QuerySelector("[data-testid='tools-overview-need-install']")!.TextContent.Trim());

        var download = Card(pane, "app:Docker.DockerDesktop").QuerySelector("[data-testid='tools-overview-need-download']")!;
        Assert.Equal("a", download.TagName, ignoreCase: true);
        Assert.Equal("https://winstall.app/apps/Docker.DockerDesktop", download.GetAttribute("href"));
        Assert.Equal("_blank", download.GetAttribute("target"));

        Assert.Contains("Done here", Card(pane, "app:office-signed-in").QuerySelector("[data-testid='tools-overview-need-done']")!.TextContent, StringComparison.Ordinal);
        Assert.Equal("Clear", Card(pane, "__clear-cache__").QuerySelector("[data-testid='tools-overview-need-clear']")!.TextContent.Trim());
        Assert.Empty(pane.FindAll("[data-testid='tools-cache-clear']"));
    }

    /// <summary>The controls do what their row's own controls do.</summary>
    [Fact]
    public void The_cards_controls_act_on_their_row()
    {
        var service = FakeDevToolService.With(
            Behind("plugin:delivery", "delivery", DevToolHosts.Default),
            Tool("plugin:absent", "absent") with { Installed = false, InstalledVersion = "not installed" },
            ManualApplication(acknowledged: false),
            Cached(stale: 1, inUse: 1));
        using var context = Context(service);
        var pane = context.Render<ToolsPane>();

        Card(pane, "plugin:delivery").QuerySelector("[data-testid='tools-overview-need-update']")!.Click();
        Card(pane, "plugin:absent").QuerySelector("[data-testid='tools-overview-need-install']")!.Click();
        Card(pane, "app:office-signed-in").QuerySelector("[data-testid='tools-overview-need-done'] input")!.Change(true);
        Card(pane, "__clear-cache__").QuerySelector("[data-testid='tools-overview-need-clear']")!.Click();

        Assert.Equal(["plugin:delivery", "plugin:absent"], service.Updated);
        Assert.Equal(("app:office-signed-in", true), Assert.Single(service.Acknowledgements));
        Assert.Contains("1 cached versions", pane.Find("[data-testid='tools-cache-remove-dialog']").TextContent, StringComparison.Ordinal);
    }

    /// <summary>A card's detail line names the versions, the kind and the catalog's
    /// note; its chips name the hosts the row is for.</summary>
    [Fact]
    public void A_card_names_its_hosts_and_its_versions_kind_and_note()
    {
        using var context = Context(FakeDevToolService.With(
            Behind("plugin:delivery", "delivery", DevToolHosts.Default) with { Status = "Configured plugin · Pinned by the team" }));
        var pane = context.Render<ToolsPane>();

        var card = Card(pane, "plugin:delivery");
        Assert.Equal("1.0.0 → 1.1.0 · Plugin · Pinned by the team", card.QuerySelector("[data-testid='tools-overview-need-detail']")!.TextContent.Trim());
        Assert.Equal(
            ["Copilot", "Claude"],
            card.QuerySelectorAll("[data-testid='tools-overview-need-hosts'] .tools-inventory__hosts").Select(chip => chip.TextContent.Trim()));
        Assert.Equal("alert", card.ClassList.Single(name => name.StartsWith("tools-overview-need--", StringComparison.Ordinal))["tools-overview-need--".Length..]);
    }

    /// <summary>A drifted registration is a Re-register, in its title and on its
    /// button, with no version pair.</summary>
    [Fact]
    public void A_drifted_registration_is_a_re_register_card()
    {
        var state = new DevToolHostState(DevToolHosts.Claude, Installed: true, "http://localhost:1/mcp", "http://localhost:2/mcp", "Registered elsewhere")
        {
            RegistrationDrifted = true
        };
        using var context = Context(FakeDevToolService.With(
            Tool("mcp:backlog", "backlog") with { Hosts = DevToolHosts.Claude, HostStates = [state], Installable = false }));
        var pane = context.Render<ToolsPane>();

        var card = Card(pane, "mcp:backlog");
        Assert.Equal("Re-register backlog", card.QuerySelector("[data-testid='tools-overview-need-title']")!.TextContent.Trim());
        var button = card.QuerySelector("[data-testid='tools-overview-need-update']")!;
        Assert.Equal("Re-register", button.TextContent.Trim());
        Assert.Equal("Re-register backlog", button.GetAttribute("aria-label"));
        Assert.DoesNotContain("→", card.QuerySelector("[data-testid='tools-overview-need-detail']")!.TextContent, StringComparison.Ordinal);
    }

    /// <summary>A row whose version lookup did not answer is neither a match nor
    /// outstanding, so it is left out of the figures.</summary>
    [Fact]
    public void A_row_whose_version_is_unknown_is_not_counted()
    {
        using var context = Context(FakeDevToolService.With(
            Tool("plugin:architecture", "architecture"),
            Tool("plugin:unknown", "unknown") with { AvailableVersion = "unknown" }));
        var pane = context.Render<ToolsPane>();

        Assert.Equal("1 of 1", Text(pane, "tools-overview-tile-matches-value"));
        Assert.Equal("1/1", Text(pane, "tools-overview-kind-plugins-count"));
    }

    /// <summary>An install by hand with no address to derive links to its own tab.</summary>
    [Fact]
    public void An_install_by_hand_with_no_download_page_links_to_its_tab()
    {
        using var context = Context(FakeDevToolService.With(
            Application("dev-drive", "Dev Drive configured", installed: false, installable: false, installedVersion: "not installed") with { Source = "command" }));
        var pane = context.Render<ToolsPane>();

        Card(pane, "app:dev-drive").QuerySelector("[data-testid='tools-overview-need-open-tab']")!.Click();

        Assert.Equal("true", pane.Find("[data-testid='tools-tab-applications']").GetAttribute("aria-selected"));
    }

    /// <summary>The link went with the Overview panel it was in, so focus goes to the
    /// tab it opened rather than falling to the document.</summary>
    [Fact]
    public void The_see_how_link_takes_focus_to_the_tab_it_opens()
    {
        using var context = Context(FakeDevToolService.With(
            Application("dev-drive", "Dev Drive configured", installed: false, installable: false, installedVersion: "not installed") with { Source = "command" }));
        context.JSInterop.SetupVoid(FocusIdentifier, _ => true);
        var pane = context.Render<ToolsPane>();

        Card(pane, "app:dev-drive").QuerySelector("[data-testid='tools-overview-need-open-tab']")!.Click();

        AssertFocused(context, pane, "tools-tab-applications");
    }

    /// <summary>A catalog that names a download page is linked to it, for a row
    /// that had no page to derive and for one whose derived page it replaces.</summary>
    [Fact]
    public void An_install_by_hand_links_to_the_catalogs_download_page_when_it_names_one()
    {
        using var context = Context(FakeDevToolService.With(
            Application("dev-drive", "Dev Drive configured", installed: false, installable: false, installedVersion: "not installed") with
            {
                Source = "command",
                Download = "https://learn.microsoft.com/windows/dev-drive/"
            },
            Application("Docker.DockerDesktop", "Docker Desktop", installed: false, installable: false, installedVersion: "not installed") with
            {
                Download = "https://docs.docker.com/desktop/setup/install/windows-install/"
            },
            Tool("mcp:aspire", "aspire") with
            {
                Installed = false,
                InstalledVersion = "not installed",
                Installable = false,
                Download = "https://aspire.dev/get-started/install-cli/"
            }));
        var pane = context.Render<ToolsPane>();

        Assert.Equal("https://learn.microsoft.com/windows/dev-drive/", DownloadHref(pane, "app:dev-drive"));
        Assert.Equal("https://docs.docker.com/desktop/setup/install/windows-install/", DownloadHref(pane, "app:Docker.DockerDesktop"));
        Assert.Equal("https://aspire.dev/get-started/install-cli/", DownloadHref(pane, "mcp:aspire"));
        Assert.Empty(pane.FindAll("[data-testid='tools-overview-need-open-tab']"));
    }

    /// <summary>Without a catalog page the card keeps the link it derives; with
    /// one that is not http or https it does the same, and never links the value.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("javascript:alert(1)")]
    [InlineData("file:///C:/Windows/System32")]
    public void An_install_by_hand_without_a_linkable_download_page_falls_back_to_the_derived_link(string? download)
    {
        using var context = Context(FakeDevToolService.With(
            Application("Docker.DockerDesktop", "Docker Desktop", installed: false, installable: false, installedVersion: "not installed") with { Download = download },
            Application("dev-drive", "Dev Drive configured", installed: false, installable: false, installedVersion: "not installed") with
            {
                Source = "command",
                Download = download
            }));
        var pane = context.Render<ToolsPane>();

        Assert.Equal("https://winstall.app/apps/Docker.DockerDesktop", DownloadHref(pane, "app:Docker.DockerDesktop"));
        var devDrive = Card(pane, "app:dev-drive");
        Assert.Null(devDrive.QuerySelector("[data-testid='tools-overview-need-download']"));
        Assert.NotNull(devDrive.QuerySelector("[data-testid='tools-overview-need-open-tab']"));
        if (download is not null)
        {
            Assert.DoesNotContain(download, pane.Markup, StringComparison.Ordinal);
        }
    }

    /// <summary>Nothing needing action shows the one sentence instead of a list.</summary>
    [Fact]
    public void Nothing_needing_action_says_everything_matches_the_catalog()
    {
        using var context = Context(FakeDevToolService.With(
            Tool("plugin:architecture", "architecture"),
            Application("Git.Git", "Git"),
            ManualApplication(acknowledged: true)));
        var pane = context.Render<ToolsPane>();

        Assert.Contains("Everything matches the catalog", pane.Find("[data-testid='tools-overview-needs-empty']").TextContent, StringComparison.Ordinal);
        Assert.Empty(pane.FindAll("[data-testid='tools-overview-need']"));
    }

    // ------------------------------------------------------------ by kind

    /// <summary>One bar per kind tab, with matches of rows, each a link to its tab.</summary>
    [Fact]
    public void By_kind_draws_one_bar_per_kind_tab_and_each_opens_its_tab()
    {
        using var context = Context(Mixed());
        var pane = context.Render<ToolsPane>();

        Assert.Equal(
            ["Plugins 2/3", "Marketplaces 0/0", "MCP servers 0/0", "Applications 1/3", "Checklist 0/1"],
            pane.FindAll(".tools-overview-kinds__link").Select(link => Squash(
                link.QuerySelector(".tools-overview-kinds__name")!.TextContent + " " + link.QuerySelector(".tools-overview-kinds__ratio")!.TextContent)));
        Assert.NotNull(pane.Find("[data-testid='tools-overview-legend']"));

        pane.Find("[data-testid='tools-overview-kind-checklist']").Click();

        Assert.Equal("true", pane.Find("[data-testid='tools-tab-checklist']").GetAttribute("aria-selected"));
    }

    /// <summary>A By kind link is in the Overview panel it hides, so focus goes to
    /// the tab it opened.</summary>
    [Fact]
    public void A_by_kind_link_takes_focus_to_the_tab_it_opens()
    {
        using var context = Context(Mixed());
        context.JSInterop.SetupVoid(FocusIdentifier, _ => true);
        var pane = context.Render<ToolsPane>();

        pane.Find("[data-testid='tools-overview-kind-checklist']").Click();

        AssertFocused(context, pane, "tools-tab-checklist");
    }

    // ------------------------------------------------------------ host filter

    /// <summary>The host filter scopes every figure on the Overview: the tiles, Needs
    /// you and By kind.</summary>
    [Fact]
    public void The_host_filter_scopes_every_figure_on_the_overview()
    {
        using var context = Context(Mixed());
        var pane = context.Render<ToolsPane>();

        pane.Find("[data-testid='tools-host-copilot']").Click();

        Assert.Equal("1 of 1", Text(pane, "tools-overview-tile-matches-value"));
        Assert.Equal("0", Text(pane, "tools-overview-tile-updates-value"));
        Assert.Equal(["Copilot 0"], pane.FindAll("[data-testid='tools-overview-updates-split'] li").Select(line => Squash(line.TextContent)));
        Assert.Equal("0", Text(pane, "tools-overview-tile-missing-value"));
        Assert.Equal("0", Text(pane, "tools-overview-tile-stale-value"));
        Assert.NotNull(pane.Find("[data-testid='tools-overview-needs-empty']"));
        Assert.Equal("1/1", Text(pane, "tools-overview-kind-plugins-count"));
        Assert.Equal("0/0", Text(pane, "tools-overview-kind-applications-count"));

        pane.Find("[data-testid='tools-host-claude']").Click();

        Assert.Equal("1 of 2", Text(pane, "tools-overview-tile-matches-value"));
        Assert.Equal(["Update delivery", "Clear the stale cache"], pane.FindAll("[data-testid='tools-overview-need-title']").Select(title => title.TextContent.Trim()));
    }

    // ------------------------------------------------------------ last run

    [Fact]
    public void There_is_no_last_run_card_without_a_record()
    {
        using var context = Context(Mixed());
        var pane = context.Render<ToolsPane>();

        Assert.Empty(pane.FindAll("[data-testid='tools-overview-last-run']"));
    }

    /// <summary>A record that cannot be read is no record: the card is hidden and
    /// the inventory still loads.</summary>
    [Fact]
    public void A_last_run_that_cannot_be_read_hides_the_card_and_the_inventory_loads()
    {
        using var context = Context(Mixed() with { LastRunReadFails = true });
        var pane = context.Render<ToolsPane>();

        Assert.Empty(pane.FindAll("[data-testid='tools-overview-last-run']"));
        Assert.Equal("3 of 7", Text(pane, "tools-overview-tile-matches-value"));
    }

    /// <summary>The card reads the record: its outcome, kind, when, steps succeeded
    /// and duration, the transcript's last lines, and opens the whole transcript.</summary>
    [Fact]
    public void The_last_run_card_shows_the_record()
    {
        var service = Mixed();
        var finished = DateTimeOffset.Now.AddMinutes(-5);
        service.SavedRuns.Add(new DevToolLastRun(
            DevToolRunKind.UpdateAll,
            finished.AddSeconds(-72),
            finished,
            DevToolRunOutcome.PartlyFailed,
            [
                new DevToolRunStep("delivery", "1.0.0", "1.1.0", DevToolRunOutcome.Succeeded, TimeSpan.FromSeconds(30)),
                new DevToolRunStep("devbook", "1.0.0", "1.2.0", DevToolRunOutcome.Failed, TimeSpan.FromSeconds(40))
            ],
            "$ claude plugin update delivery\nupdated\n\n$ claude plugin update devbook\nrefused\nexit code 1\n\nOne of two tools could not be updated."));
        using var context = Context(service);
        var pane = context.Render<ToolsPane>();

        var card = pane.Find("[data-testid='tools-overview-last-run']");
        Assert.Equal("Partly failed", card.QuerySelector("[data-testid='tools-overview-run-outcome']")!.TextContent.Trim());
        Assert.StartsWith("Update all · ", Squash(card.QuerySelector("[data-testid='tools-overview-run-summary']")!.TextContent), StringComparison.Ordinal);
        Assert.Equal("1 of 2 steps succeeded · 1 min 12 s", Squash(card.QuerySelector("[data-testid='tools-overview-run-figures']")!.TextContent));
        Assert.Equal(
            "$ claude plugin update devbook\nrefused\nexit code 1\nOne of two tools could not be updated.",
            card.QuerySelector("[data-testid='tools-overview-run-tail']")!.TextContent);

        Assert.Empty(pane.FindAll("[data-testid='tools-last-run-transcript-dialog']"));
        pane.Find("[data-testid='tools-overview-run-open']").Click();
        Assert.Contains("claude plugin update delivery", pane.Find("[data-testid='tools-last-run-transcript-dialog']").TextContent, StringComparison.Ordinal);
    }

    /// <summary>A run the pane finishes becomes the card, without a reopen.</summary>
    [Fact]
    public void A_finished_run_becomes_the_last_run_card()
    {
        var service = FakeDevToolService.With(Behind("plugin:delivery", "delivery", DevToolHosts.Default));
        using var context = Context(service);
        var pane = context.Render<ToolsPane>();

        Assert.Empty(pane.FindAll("[data-testid='tools-overview-last-run']"));

        pane.Find("[data-testid='tools-overview-need-update']").Click();

        var card = pane.Find("[data-testid='tools-overview-last-run']");
        Assert.Equal("Succeeded", card.QuerySelector("[data-testid='tools-overview-run-outcome']")!.TextContent.Trim());
        Assert.StartsWith("Update · Today", Squash(card.QuerySelector("[data-testid='tools-overview-run-summary']")!.TextContent), StringComparison.Ordinal);
        Assert.StartsWith("1 of 1 step succeeded", Squash(card.QuerySelector("[data-testid='tools-overview-run-figures']")!.TextContent), StringComparison.Ordinal);
    }

    // ------------------------------------------------------------ fixtures

    /// <summary>Four plugins — current on Copilot, behind on Claude, one switched
    /// off, and a current Claude plugin with a stale cached version — three
    /// applications, current, behind and missing by hand, and an unconfirmed
    /// checklist row.</summary>
    private static FakeDevToolService Mixed() => FakeDevToolService.With(
        Tool("plugin:architecture", "architecture") with { Hosts = DevToolHosts.Copilot },
        Behind("plugin:delivery", "delivery", DevToolHosts.Claude),
        Tool("plugin:off", "off") with { ConfiguredEnabled = false, Installed = false, InstalledVersion = "not installed" },
        Cached(stale: 1, inUse: 1) with { Hosts = DevToolHosts.Claude },
        Application("Git.Git", "Git"),
        Application("Microsoft.PowerShell", "PowerShell", installedVersion: "7.4.0", availableVersion: "7.5.0"),
        Application("Docker.DockerDesktop", "Docker Desktop", installed: false, installable: false, installedVersion: "not installed", enabled: true),
        ManualApplication(acknowledged: false));

    private static DevToolInfo Behind(string key, string name, DevToolHosts hosts) =>
        Tool(key, name) with { Hosts = hosts, AvailableVersion = "1.1.0" };

    /// <summary>Seven rows behind: one each on Copilot and on Claude Desktop alone;
    /// four for both hosts — reported by Copilot, by Claude, by both, and by
    /// neither (no host states) — and one machine application.</summary>
    private static FakeDevToolService BehindOnEveryHost() => FakeDevToolService.With(
        Behind("plugin:copilot-only", "copilot-only", DevToolHosts.Copilot),
        Behind("plugin:claude-only", "claude-only", DevToolHosts.ClaudeDesktop),
        BehindOnBoth("plugin:copilot-reports", copilotBehind: true, claudeBehind: false),
        BehindOnBoth("plugin:claude-reports", copilotBehind: false, claudeBehind: true),
        BehindOnBoth("plugin:both-report", copilotBehind: true, claudeBehind: true),
        Behind("plugin:silent", "silent", DevToolHosts.Default),
        Application("Microsoft.PowerShell", "PowerShell", installedVersion: "7.4.0", availableVersion: "7.5.0"));

    private static DevToolInfo BehindOnBoth(string key, bool copilotBehind, bool claudeBehind) =>
        Behind(key, key["plugin:".Length..], DevToolHosts.Default) with
        {
            HostStates =
            [
                new DevToolHostState(DevToolHosts.Copilot, Installed: true, copilotBehind ? "1.0.0" : "1.1.0", "1.1.0", "Installed"),
                new DevToolHostState(DevToolHosts.Claude, Installed: true, claudeBehind ? "1.0.0" : "1.1.0", "1.1.0", "Installed")
            ]
        };

    private static int SplitSum(IRenderedComponent<ToolsPane> pane) =>
        pane.FindAll("[data-testid='tools-overview-updates-split'] .tools-overview__line-figure")
            .Sum(figure => int.Parse(figure.TextContent.Trim(), System.Globalization.CultureInfo.InvariantCulture));

    private const string FocusIdentifier = "Blazor._internal.domWrapper.focus";

    /// <summary>The tab is on show and focus moved exactly once. Which element it
    /// went to is the strip's own test: bUnit leaves the reference attribute empty on
    /// a tab the strip captures into its array, so it cannot be matched here.</summary>
    private static void AssertFocused(BunitContext context, IRenderedComponent<ToolsPane> pane, string tabTestId)
    {
        pane.WaitForAssertion(() =>
            Assert.Equal("true", pane.Find($"[data-testid='{tabTestId}']").GetAttribute("aria-selected")));

        Assert.Single(context.JSInterop.Invocations[FocusIdentifier]);
    }

    private static DevToolInfo Cached(int stale, int inUse) =>
        Tool("plugin:devbook", "devbook") with
        {
            CachedVersions =
            [
                .. Enumerable.Range(0, inUse).Select(index => new DevToolCachedVersion($"2.0.{index}", $@"C:\cache\devbook\2.0.{index}", Installs: 1)),
                .. Enumerable.Range(0, stale).Select(index => new DevToolCachedVersion($"1.0.{index}", $@"C:\cache\devbook\1.0.{index}", Installs: 0))
            ]
        };

    private static AngleSharp.Dom.IElement Card(IRenderedComponent<ToolsPane> pane, string key) =>
        pane.Find($"[data-testid='tools-overview-need'][data-need-key='{key}']");

    private static string? DownloadHref(IRenderedComponent<ToolsPane> pane, string key) =>
        Card(pane, key).QuerySelector("[data-testid='tools-overview-need-download']")?.GetAttribute("href");

    private static string Text(IRenderedComponent<ToolsPane> pane, string testId) =>
        Squash(pane.Find($"[data-testid='{testId}']").TextContent);

    private static string Squash(string text) =>
        string.Join(' ', text.Split((char[])[' ', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries));
}
