using Backlog.Infrastructure.FileSystem;
using Backlog.Modules.DevPc.UI;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using static Backlog.Modules.DevPc.UnitTests.ToolsPaneTests;

namespace Backlog.Modules.DevPc.UnitTests;

/// <summary>
/// The pane's header and kind tabs (plan +system-tools-redesign, step 2): the host
/// filter that scopes every tab and every count, the tabs' count badges, the Checklist
/// split off the applications, the folding of groups with nothing outstanding, the
/// Needs action toggle, the headline's template, and the tab and filter remembered per
/// machine.
/// </summary>
public sealed class ToolsPaneTabsTests
{
    [Fact]
    public void The_claude_filter_shows_claude_code_and_claude_desktop_rows_and_no_hostless_ones()
    {
        using var context = Context(HostMix());
        var pane = context.Render<ToolsPane>();

        pane.Find("[data-testid='tools-host-claude']").Click();

        Assert.NotNull(pane.Find("[data-tool-key='plugin:both']"));
        Assert.NotNull(pane.Find("[data-tool-key='plugin:claude']"));
        Assert.NotNull(pane.Find("[data-tool-key='mcp:desktop']"));
        Assert.Empty(pane.FindAll("[data-tool-key='plugin:copilot']"));
        Assert.Empty(pane.FindAll("[data-tool-key='app:Git.Git']"));
        Assert.Empty(pane.FindAll("[data-tool-key='app:office-signed-in']"));
    }

    [Fact]
    public void The_copilot_filter_shows_only_copilot_rows()
    {
        using var context = Context(HostMix());
        var pane = context.Render<ToolsPane>();

        pane.Find("[data-testid='tools-host-copilot']").Click();

        Assert.Equal(
            ["plugin:both", "plugin:copilot"],
            pane.FindAll("[data-tool-key]").Select(row => row.GetAttribute("data-tool-key")).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void All_hosts_is_the_default_and_shows_every_row()
    {
        using var context = Context(HostMix());
        var pane = context.Render<ToolsPane>();

        Assert.Equal("true", pane.Find("[data-testid='tools-host-all']").GetAttribute("aria-checked"));
        Assert.Equal(6, pane.FindAll("[data-tool-key]").Count);
    }

    /// <summary>The filter scopes the counts as well as the rows: a tab's badge and
    /// the toolbar's All (n) both shrink with it.</summary>
    [Fact]
    public void The_host_filter_scopes_every_count()
    {
        using var context = Context(FakeDevToolService.With(
            Plugin("plugin:copilot-behind", DevToolHosts.Copilot, behind: true),
            Plugin("plugin:claude-behind", DevToolHosts.Claude, behind: true),
            Plugin("plugin:claude-current", DevToolHosts.Claude)));
        var pane = context.Render<ToolsPane>();
        OpenPlugins(pane);

        Assert.Equal("2", pane.Find("[data-testid='tools-tab-plugins-count']").TextContent.Trim());
        Assert.Equal("All (3)", pane.Find("[data-testid='tools-needs-all']").TextContent.Trim());

        pane.Find("[data-testid='tools-host-copilot']").Click();

        Assert.Equal("1", pane.Find("[data-testid='tools-tab-plugins-count']").TextContent.Trim());
        Assert.Equal("All (1)", pane.Find("[data-testid='tools-needs-all']").TextContent.Trim());
        Assert.Equal("Needs action (1)", pane.Find("[data-testid='tools-needs-action']").TextContent.Trim());
    }

    /// <summary>Each kind tab counts its rows needing action, toned by the worst of
    /// them: error for missing, warning for behind, neutral for unconfirmed. A tab with
    /// nothing outstanding carries no badge rather than a zero.</summary>
    [Fact]
    public void Each_kind_tab_counts_its_rows_needing_action_in_the_tone_of_the_worst()
    {
        using var context = Context(FakeDevToolService.With(
            Plugin("plugin:behind", DevToolHosts.Default, behind: true),
            Plugin("plugin:current", DevToolHosts.Default),
            Tool("mcp:missing", "missing") with { Installed = false, InstalledVersion = "not installed" },
            Tool("mcp:behind", "behind") with { InstalledVersion = "1.0.0", AvailableVersion = "1.1.0" },
            Application("Git.Git", "Git"),
            ManualApplication(acknowledged: false)));
        var pane = context.Render<ToolsPane>();

        var plugins = pane.Find("[data-testid='tools-tab-plugins-count']");
        Assert.Equal("1", plugins.TextContent.Trim());
        Assert.Contains("badge--tone-alert", plugins.ClassName, StringComparison.Ordinal);

        var servers = pane.Find("[data-testid='tools-tab-mcp-servers-count']");
        Assert.Equal("2", servers.TextContent.Trim());
        Assert.Contains("badge--tone-fault", servers.ClassName, StringComparison.Ordinal);

        var checklist = pane.Find("[data-testid='tools-tab-checklist-count']");
        Assert.Equal("1", checklist.TextContent.Trim());
        Assert.Contains("badge--tone-quiet", checklist.ClassName, StringComparison.Ordinal);

        Assert.Empty(pane.FindAll("[data-testid='tools-tab-applications-count']"));
        Assert.Empty(pane.FindAll("[data-testid='tools-tab-marketplaces-count']"));
    }

    /// <summary>The rows confirmed by hand are a checklist somebody walks, so they
    /// have a tab of their own; the applications tab holds the rest.</summary>
    [Fact]
    public void The_rows_confirmed_by_hand_are_the_checklist_and_the_rest_are_applications()
    {
        using var context = Context(FakeDevToolService.With(
            Application("Git.Git", "Git"),
            ManualApplication(acknowledged: true)));
        var pane = context.Render<ToolsPane>();

        var applications = pane.Find("[data-testid='tools-applications']");
        var checklist = pane.Find("[data-testid='tools-checklist']");

        Assert.NotNull(applications.QuerySelector("[data-tool-key='app:Git.Git']"));
        Assert.Null(applications.QuerySelector("[data-tool-key='app:office-signed-in']"));
        Assert.NotNull(checklist.QuerySelector("[data-tool-key='app:office-signed-in']"));
        Assert.Null(checklist.QuerySelector("[data-tool-key='app:Git.Git']"));

        Assert.Equal(
            "Confirmed by hand",
            checklist.QuerySelector("[data-testid='tools-row-status']")!.TextContent.Replace("Status", string.Empty, StringComparison.Ordinal).Trim());
    }

    /// <summary>A group with nothing outstanding starts folded and says so in its fold
    /// row; one with work starts open and says nothing of the kind. An ungrouped tab is
    /// a card too, and folds by the same rule.</summary>
    [Fact]
    public void A_group_with_nothing_outstanding_starts_folded_and_says_all_up_to_date()
    {
        using var context = Context(FakeDevToolService.With(
            Plugin("plugin:current", DevToolHosts.Default),
            Application("Microsoft.VisualStudioCode", "Visual Studio Code", group: "Baseline"),
            Application("Microsoft.PowerToys", "PowerToys", group: "Team tools", installed: false, installedVersion: "not installed", availableVersion: "0.96.1")));
        var pane = context.Render<ToolsPane>();

        var plugins = pane.Find("[data-tool-group='Plugin:']");
        Assert.True(plugins.QuerySelector(".tools-inventory")!.HasAttribute("hidden"));
        Assert.NotNull(plugins.QuerySelector("[data-testid='tools-group-current']"));

        var baseline = pane.Find("[data-tool-group='Application:Baseline']");
        Assert.True(baseline.QuerySelector(".tools-inventory")!.HasAttribute("hidden"));
        Assert.Equal("All up to date", baseline.QuerySelector("[data-testid='tools-group-current']")!.TextContent.Trim());

        var team = pane.Find("[data-tool-group='Application:Team tools']");
        Assert.False(team.QuerySelector(".tools-inventory")!.HasAttribute("hidden"));
        Assert.Null(team.QuerySelector("[data-testid='tools-group-current']"));
    }

    /// <summary>The toolbar's toggle starts on All and narrows the tab to the rows that
    /// need action when asked; groups left with nothing drop out. Under All the row
    /// that matches sits behind the group's reveal until it is pressed.</summary>
    [Fact]
    public void Needs_action_narrows_the_tab_to_the_rows_that_need_it()
    {
        using var context = Context(FakeDevToolService.With(
            Plugin("plugin:behind", DevToolHosts.Default, behind: true),
            Plugin("plugin:current", DevToolHosts.Default)));
        var pane = context.Render<ToolsPane>();
        OpenPlugins(pane);

        Assert.Equal("true", pane.Find("[data-testid='tools-needs-all']").GetAttribute("aria-checked"));
        Assert.Equal("Needs action (1)", pane.Find("[data-testid='tools-needs-action']").TextContent.Trim());
        pane.Find("[data-testid='tools-plugins'] [data-testid='tools-group-reveal']").Click();
        Assert.Equal(2, pane.FindAll("[data-testid='tools-plugins'] [data-tool-key]").Count);

        pane.Find("[data-testid='tools-needs-action']").Click();

        Assert.Equal(["plugin:behind"], pane.FindAll("[data-testid='tools-plugins'] [data-tool-key]").Select(row => row.GetAttribute("data-tool-key")));
        Assert.Empty(pane.FindAll("[data-testid='tools-plugins'] [data-testid='tools-group-reveal']"));

        pane.Find("[data-testid='tools-needs-all']").Click();

        Assert.Equal(2, pane.FindAll("[data-testid='tools-plugins'] [data-tool-key]").Count);
    }

    [Fact]
    public void The_name_filter_narrows_the_tab_by_name()
    {
        using var context = Context(FakeDevToolService.With(
            Plugin("plugin:architecture", DevToolHosts.Default),
            Plugin("plugin:delivery", DevToolHosts.Default)));
        var pane = context.Render<ToolsPane>();
        OpenPlugins(pane);

        pane.Find("[data-testid='tools-name-filter'] input").Input("ARCH");

        Assert.Equal(["plugin:architecture"], pane.FindAll("[data-testid='tools-plugins'] [data-tool-key]").Select(row => row.GetAttribute("data-tool-key")));
    }

    /// <summary>The headline is a template over the tab on show: how many of its rows
    /// are outstanding and how, or that everything matches.</summary>
    [Fact]
    public void The_headline_says_what_is_outstanding_on_the_tab_on_show()
    {
        using var context = Context(FakeDevToolService.With(
            Plugin("plugin:a", DevToolHosts.Default, behind: true),
            Plugin("plugin:b", DevToolHosts.Default, behind: true),
            Plugin("plugin:c", DevToolHosts.Default),
            Tool("mcp:Guidelines", "guidelines")));
        var pane = context.Render<ToolsPane>();
        OpenPlugins(pane);

        Assert.Equal("System tools · Plugins", pane.Find(".tools-panel__eyebrow").TextContent.Trim());
        Assert.Equal("Two of three plugins are behind", pane.Find("#installed-tools-title").TextContent.Trim());
        Assert.Contains("3 entries", pane.Find("[data-testid='tools-checked']").TextContent, StringComparison.Ordinal);

        pane.Find("[data-testid='tools-tab-mcp-servers']").Click();

        Assert.Equal("System tools · MCP servers", pane.Find(".tools-panel__eyebrow").TextContent.Trim());
        Assert.Equal("Everything matches the catalog", pane.Find("#installed-tools-title").TextContent.Trim());
        Assert.Contains("1 entry", pane.Find("[data-testid='tools-checked']").TextContent, StringComparison.Ordinal);
    }

    /// <summary>Update all says how many rows it would act on. Not narrowed by the host
    /// filter, because the port's Update all updates every row.</summary>
    [Fact]
    public void Update_all_says_how_many_rows_it_acts_on()
    {
        using var context = Context(FakeDevToolService.With(
            Plugin("plugin:a", DevToolHosts.Copilot, behind: true),
            Plugin("plugin:b", DevToolHosts.Claude, behind: true)));
        var pane = context.Render<ToolsPane>();

        Assert.Equal("Update all (2)", pane.Find("[data-testid='tools-update-all']").TextContent.Trim());

        pane.Find("[data-testid='tools-host-claude']").Click();

        Assert.Equal("Update all (2)", pane.Find("[data-testid='tools-update-all']").TextContent.Trim());
    }

    /// <summary>The tab and the host filter are remembered on this machine, in the
    /// shell's per-device file, and the next pane opens on them.</summary>
    [Fact]
    public void The_tab_and_the_host_filter_are_remembered_on_this_machine()
    {
        var file = Path.Combine(Path.GetTempPath(), "tools-pane-tests-" + Guid.NewGuid().ToString("N"), "shell-navigation.json");
        try
        {
            using (var first = Context(HostMix()))
            {
                first.Services.AddSingleton<IToolsViewPreferences>(new ToolsViewPreferences(new ShellNavigationStore(file)));
                var pane = first.Render<ToolsPane>();

                pane.Find("[data-testid='tools-tab-checklist']").Click();
                pane.Find("[data-testid='tools-host-copilot']").Click();
            }

            var store = new ShellNavigationStore(file);
            Assert.Equal("checklist", store.ToolsTab);
            Assert.Equal("copilot", store.ToolsHostFilter);

            using var second = Context(HostMix());
            second.Services.AddSingleton<IToolsViewPreferences>(new ToolsViewPreferences(store));
            var reopened = second.Render<ToolsPane>();

            Assert.Equal("true", reopened.Find("[data-testid='tools-tab-checklist']").GetAttribute("aria-selected"));
            Assert.Equal("true", reopened.Find("[data-testid='tools-host-copilot']").GetAttribute("aria-checked"));
        }
        finally
        {
            var directory = Path.GetDirectoryName(file)!;
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>A remembered value this build does not know opens on the defaults
    /// rather than on a tab with nothing behind it.</summary>
    [Fact]
    public void A_remembered_value_this_build_does_not_know_opens_on_the_defaults()
    {
        using var context = Context(HostMix());
        var preferences = new ToolsViewPreferences(null);
        preferences.SetTab("someday");
        preferences.SetHostFilter("gemini");
        context.Services.AddSingleton<IToolsViewPreferences>(preferences);

        var pane = context.Render<ToolsPane>();

        Assert.Equal("true", pane.Find("[data-testid='tools-tab-overview']").GetAttribute("aria-selected"));
        Assert.Equal("true", pane.Find("[data-testid='tools-host-all']").GetAttribute("aria-checked"));
    }

    /// <summary>A machine that has never been on a tab opens on the Overview.</summary>
    [Fact]
    public void The_overview_is_the_default_tab()
    {
        using var context = Context(HostMix());
        var pane = context.Render<ToolsPane>();

        Assert.Equal("true", pane.Find("[data-testid='tools-tab-overview']").GetAttribute("aria-selected"));
        Assert.False(pane.Find("[data-testid='tools-overview']").HasAttribute("hidden"));
        Assert.NotNull(pane.Find("[data-testid='tools-overview-board']"));
        Assert.Equal("System tools · Overview", pane.Find(".tools-panel__eyebrow").TextContent.Trim());
    }

    /// <summary>The strip's "Last run transcript" is there only while there is a
    /// transcript to open. With no saved run it opens the rail's command fold; with
    /// one it opens that run's transcript, as the Overview's card does.</summary>
    [Fact]
    public void The_transcript_link_opens_the_last_run_transcript()
    {
        var service = FakeDevToolService.With(Tool("plugin:architecture", "architecture")) with
        {
            Commands = [new DevToolCommand("claude plugin list", 0, "ok")]
        };
        using var context = Context(service);
        var pane = context.Render<ToolsPane>();

        Assert.Equal("false", pane.Find("[data-testid='tools-command-log']").GetAttribute("aria-expanded"));

        pane.Find("[data-testid='tools-transcript-link']").Click();

        Assert.Equal("true", pane.Find("[data-testid='tools-command-log']").GetAttribute("aria-expanded"));
        Assert.Empty(pane.FindAll("[data-testid='tools-last-run-transcript-dialog']"));
    }

    [Fact]
    public void There_is_no_transcript_link_without_a_run_or_a_command()
    {
        using var context = Context(FakeDevToolService.With(Tool("plugin:architecture", "architecture")));
        var pane = context.Render<ToolsPane>();

        Assert.Empty(pane.FindAll("[data-testid='tools-transcript-link']"));
    }

    /// <summary>After a restart the pane has run no command yet, but the saved last
    /// run is still a transcript to read: the link shows and opens it.</summary>
    [Fact]
    public void The_transcript_link_opens_a_saved_run_with_no_commands_yet()
    {
        var service = FakeDevToolService.With(Tool("plugin:architecture", "architecture"));
        var finished = DateTimeOffset.Now.AddMinutes(-5);
        service.SavedRuns.Add(new DevToolLastRun(
            DevToolRunKind.UpdateAll,
            finished.AddSeconds(-30),
            finished,
            DevToolRunOutcome.Succeeded,
            [new DevToolRunStep("delivery", "1.0.0", "1.1.0", DevToolRunOutcome.Succeeded, TimeSpan.FromSeconds(30))],
            "$ claude plugin update delivery\nupdated"));
        using var context = Context(service);
        var pane = context.Render<ToolsPane>();

        Assert.Empty(pane.FindAll("[data-testid='tools-command-log']"));
        Assert.Empty(pane.FindAll("[data-testid='tools-last-run-transcript-dialog']"));

        pane.Find("[data-testid='tools-transcript-link']").Click();

        Assert.Contains(
            "claude plugin update delivery",
            pane.Find("[data-testid='tools-last-run-transcript-dialog']").TextContent,
            StringComparison.Ordinal);
    }

    /// <summary>Every panel is drawn, but the toolbar only in the one on show, so each
    /// of its controls exists once. The Overview has no toolbar: its catalog acts are
    /// in its footer, and they too exist once.</summary>
    [Fact]
    public void The_toolbar_exists_once_and_overview_keeps_the_catalog_acts_in_its_footer()
    {
        using var context = Context(HostMix());
        var pane = context.Render<ToolsPane>();

        Assert.Empty(pane.FindAll("[data-testid='tools-kind-toolbar']"));
        var footer = pane.Find("[data-testid='tools-overview'] [data-testid='tools-overview-footer']");
        Assert.NotNull(footer.QuerySelector("[data-testid='tools-add-open']"));
        Assert.NotNull(footer.QuerySelector("[data-testid='tools-import-open']"));
        Assert.Single(pane.FindAll("[data-testid='tools-add-open']"));
        Assert.Single(pane.FindAll("[data-testid='tools-import-open']"));

        pane.Find("[data-testid='tools-tab-mcp-servers']").Click();
        Assert.Single(pane.FindAll("[data-testid='tools-kind-toolbar']"));
        var toolbar = pane.Find("[data-testid='tools-mcp-servers'] [data-testid='tools-kind-toolbar']");
        Assert.NotNull(toolbar.QuerySelector("[data-testid='tools-add-open']"));
        Assert.NotNull(toolbar.QuerySelector("[data-testid='tools-import-open']"));
        Assert.Empty(pane.FindAll("[data-testid='tools-overview-footer']"));
        Assert.Single(pane.FindAll("[data-testid='tools-add-open']"));
    }

    [Fact]
    public void The_headline_names_missing_rows_and_mixed_ones()
    {
        using var missing = Context(FakeDevToolService.With(
            Plugin("plugin:a", DevToolHosts.Default) with { Installed = false, InstalledVersion = "not installed" },
            Plugin("plugin:b", DevToolHosts.Default)));
        var missingPane = missing.Render<ToolsPane>();
        OpenPlugins(missingPane);
        Assert.Equal("One of two plugins is missing", missingPane.Find("#installed-tools-title").TextContent.Trim());

        using var mixed = Context(FakeDevToolService.With(
            Plugin("plugin:a", DevToolHosts.Default) with { Installed = false, InstalledVersion = "not installed" },
            Plugin("plugin:b", DevToolHosts.Default, behind: true)));
        var mixedPane = mixed.Render<ToolsPane>();
        OpenPlugins(mixedPane);
        Assert.Equal("One plugin behind and one missing", mixedPane.Find("#installed-tools-title").TextContent.Trim());
    }

    /// <summary>A tab the host filter has emptied says so, rather than claiming the
    /// rows it hides match the catalog.</summary>
    [Fact]
    public void A_tab_the_filter_empties_says_so_in_the_headline()
    {
        using var context = Context(HostMix());
        var pane = context.Render<ToolsPane>();

        pane.Find("[data-testid='tools-host-claude']").Click();
        pane.Find("[data-testid='tools-tab-applications']").Click();

        Assert.Equal("No applications for these hosts", pane.Find("#installed-tools-title").TextContent.Trim());
    }

    /// <summary>A missing row is missing whether or not anything here can install it,
    /// so an install-by-hand application counts as needing action.</summary>
    [Fact]
    public void A_missing_row_nothing_can_install_still_needs_action()
    {
        using var context = Context(FakeDevToolService.With(
            Application("Microsoft.Office", "Microsoft 365 apps", installed: false, installable: false, installedVersion: "not installed")));
        var pane = context.Render<ToolsPane>();

        Assert.Equal("1", pane.Find("[data-testid='tools-tab-applications-count']").TextContent.Trim());
        Assert.Equal("Missing", pane.Find("[data-testid='tools-row-status'] .tools-inventory__status-chip").TextContent.Trim());
    }

    /// <summary>Two plugins for each single host and both, an MCP server for Claude
    /// Desktop alone, and an application and a checklist row with no host.</summary>
    private static FakeDevToolService HostMix() => FakeDevToolService.With(
        Plugin("plugin:both", DevToolHosts.Default),
        Plugin("plugin:claude", DevToolHosts.Claude),
        Plugin("plugin:copilot", DevToolHosts.Copilot),
        Tool("mcp:desktop", "desktop") with { Hosts = DevToolHosts.ClaudeDesktop },
        Application("Git.Git", "Git"),
        ManualApplication(acknowledged: true));

    /// <summary>Moves off the Overview, which a pane opens on, to the Plugins tab.</summary>
    private static void OpenPlugins(IRenderedComponent<ToolsPane> pane) =>
        pane.Find("[data-testid='tools-tab-plugins']").Click();

    private static DevToolInfo Plugin(string key, DevToolHosts hosts, bool behind = false) =>
        Tool(key, key["plugin:".Length..]) with
        {
            Hosts = hosts,
            AvailableVersion = behind ? "1.1.0" : "1.0.0"
        };
}
