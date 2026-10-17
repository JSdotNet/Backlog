using Backlog.Modules.DevPc.UI;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using static Backlog.Modules.DevPc.UnitTests.ToolsPaneTests;

namespace Backlog.Modules.DevPc.UnitTests;

/// <summary>
/// The pane brought to the agreed design canvas (plan +system-tools-redesign): the
/// headline that counts each kind of outstanding on its own, the relative checked
/// line, the stroke icons on the header's two controls, the Needs you subtitle, the
/// Overview footer that names the catalog and this machine's choices file, the fold
/// label in the tab's own noun, the enable switch, and the up-to-date rows an open
/// group holds back.
/// </summary>
public sealed class ToolsPaneDesignAlignmentTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 15, 9, 30, 0, TimeSpan.Zero);

    // ------------------------------------------------------------ B1 the headline

    [Fact]
    public void The_overview_headline_counts_each_kind_of_outstanding_in_order()
    {
        using var context = Context(FakeDevToolService.With(
            Behind("plugin:a"), Behind("plugin:b"), Behind("plugin:c"),
            Missing("mcp:d"), Missing("mcp:e"),
            ManualApplication(acknowledged: false)));
        var pane = context.Render<ToolsPane>();

        Assert.Equal("Three updates waiting, two tools missing and one item to confirm", Headline(pane));
    }

    [Fact]
    public void An_overview_headline_with_two_kinds_joins_them_with_and()
    {
        using var context = Context(FakeDevToolService.With(
            Behind("plugin:a"), Behind("plugin:b"), Behind("plugin:c"),
            Missing("mcp:d"), Missing("mcp:e"),
            Tool("plugin:f", "f")));
        var pane = context.Render<ToolsPane>();

        Assert.Equal("Three updates waiting and two tools missing", Headline(pane));
    }

    [Fact]
    public void A_kind_tab_headline_with_two_kinds_uses_the_tabs_noun()
    {
        using var context = Context(FakeDevToolService.With(
            Behind("plugin:a"), Behind("plugin:b"), Missing("plugin:c"), Tool("plugin:d", "d")));
        var pane = context.Render<ToolsPane>();

        pane.Find("[data-testid='tools-tab-plugins']").Click();

        Assert.Equal("Two plugins behind and one missing", Headline(pane));
    }

    /// <summary>Words or digits by the largest number the sentence says. The tab's
    /// total is past twenty, but the sentence never names it.</summary>
    [Fact]
    public void A_mixed_headline_spells_its_counts_when_every_one_is_small()
    {
        var rows = Enumerable.Range(0, 20).Select(index => Tool($"plugin:current-{index}", $"current-{index}"))
            .Concat([Behind("plugin:a"), Behind("plugin:b"), Missing("plugin:c")])
            .ToArray();
        using var context = Context(FakeDevToolService.With(rows));
        var pane = context.Render<ToolsPane>();

        pane.Find("[data-testid='tools-tab-plugins']").Click();

        Assert.Equal("Two plugins behind and one missing", Headline(pane));
    }

    [Fact]
    public void A_mixed_headline_uses_digits_once_a_count_is_past_twenty()
    {
        var rows = Enumerable.Range(0, 21).Select(index => Behind($"plugin:behind-{index}"))
            .Append(Missing("plugin:c"))
            .ToArray();
        using var context = Context(FakeDevToolService.With(rows));
        var pane = context.Render<ToolsPane>();

        pane.Find("[data-testid='tools-tab-plugins']").Click();

        Assert.Equal("21 plugins behind and 1 missing", Headline(pane));
    }

    [Fact]
    public void A_single_kind_of_outstanding_keeps_the_n_of_m_sentence()
    {
        using var context = Context(FakeDevToolService.With(
            Behind("plugin:a"), Behind("plugin:b"), Tool("plugin:c", "c")));
        var pane = context.Render<ToolsPane>();

        pane.Find("[data-testid='tools-tab-plugins']").Click();

        Assert.Equal("Two of three plugins are behind", Headline(pane));
    }

    // ------------------------------------------------------------ B2 the checked line

    /// <summary>The line counts the entries on the tab on show and how many of them
    /// match: a switched-off row and one whose version nothing looked up are entries,
    /// and neither of them matches.</summary>
    [Fact]
    public void The_checked_line_says_when_against_what_and_how_many_already_match()
    {
        using var context = Context(FakeDevToolService.With(
            Tool("plugin:current", "current"),
            Behind("plugin:behind"),
            Tool("plugin:off", "off") with { ConfiguredEnabled = false },
            Tool("plugin:unknown", "unknown") with { AvailableVersion = "unknown" }));
        context.Services.AddSingleton<TimeProvider>(Clock());
        var pane = context.Render<ToolsPane>();

        Assert.Equal(
            "Checked just now against the setup catalog · 4 entries · 1 already match",
            Checked(pane));
    }

    /// <summary>Relative to when the line is drawn, and drawn only when something
    /// else redraws the header — nothing polls it.</summary>
    [Theory]
    [InlineData(1, "1 minute ago")]
    [InlineData(5, "5 minutes ago")]
    [InlineData(60, "1 hour ago")]
    [InlineData(150, "2 hours ago")]
    [InlineData(3 * 24 * 60, "on 15 Oct 2026")]
    public void The_checked_line_ages_on_the_next_render(int minutes, string expected)
    {
        var clock = Clock();
        using var context = Context(FakeDevToolService.With(Tool("plugin:a", "a")));
        context.Services.AddSingleton<TimeProvider>(clock);
        var pane = context.Render<ToolsPane>();

        clock.Advance(TimeSpan.FromMinutes(minutes));

        Assert.StartsWith("Checked just now", Checked(pane), StringComparison.Ordinal);

        pane.Render();

        Assert.StartsWith($"Checked {expected} against the setup catalog", Checked(pane), StringComparison.Ordinal);
    }

    // ------------------------------------------------------------ B3 the header icons

    [Fact]
    public void Check_for_updates_carries_a_refresh_glyph_before_its_label()
    {
        using var context = Context(FakeDevToolService.With(Tool("plugin:a", "a")));
        var refresh = context.Render<ToolsPane>().Find("[data-testid='tools-refresh']");

        var icon = refresh.FirstElementChild!;
        Assert.Equal("svg", icon.LocalName);
        Assert.Contains("refresh-cw-icon", icon.ClassName, StringComparison.Ordinal);
        Assert.Equal("true", icon.GetAttribute("aria-hidden"));
        Assert.Equal("Check for updates", refresh.TextContent.Trim());
    }

    [Fact]
    public void Close_is_a_cross_glyph_named_for_what_it_does()
    {
        using var context = Context(FakeDevToolService.With(Tool("plugin:a", "a")));
        var close = context.Render<ToolsPane>().Find("[data-testid='tools-close']");

        Assert.Equal("Close tools pane", close.GetAttribute("aria-label"));
        Assert.Contains("tools-panel__close", close.ClassName, StringComparison.Ordinal);
        Assert.NotNull(close.QuerySelector("svg.x-icon[aria-hidden='true']"));
        Assert.DoesNotContain("✕", close.TextContent, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------ B4 the Needs you subtitle

    [Fact]
    public void Needs_you_says_its_list_is_the_exceptions_only_when_there_are_some()
    {
        using var some = Context(FakeDevToolService.With(Behind("plugin:a")));
        Assert.Equal(
            "The exceptions — everything else matches the catalog",
            some.Render<ToolsPane>().Find("[data-testid='tools-overview-needs-subtitle']").TextContent.Trim());

        using var none = Context(FakeDevToolService.With(Tool("plugin:a", "a")));
        Assert.Empty(none.Render<ToolsPane>().FindAll("[data-testid='tools-overview-needs-subtitle']"));
    }

    // ------------------------------------------------------------ B5 the Overview footer

    [Fact]
    public void The_overview_footer_names_the_catalog_and_this_machines_choices_file()
    {
        var service = FakeDevToolService.With(Tool("plugin:a", "a")) with
        {
            CatalogPath = @"D:\setup\.tools\ai-tools.json",
            PcConfigPath = @"D:\setup\.tools\this-pc\pc-tools.json"
        };
        using var context = Context(service);

        var paths = context.Render<ToolsPane>().Find("[data-testid='tools-overview-footer-paths']");

        Assert.Equal(
            @"Catalog D:\setup\.tools\ai-tools.json · per-PC choices in this-pc\pc-tools.json",
            Squashed(paths.TextContent));
        Assert.Equal(@"D:\setup\.tools\this-pc\pc-tools.json", paths.QuerySelectorAll("code")[1].GetAttribute("title"));
    }

    [Fact]
    public void A_host_that_names_no_choices_file_gets_only_the_catalog_in_the_footer()
    {
        using var context = Context(FakeDevToolService.With(Tool("plugin:a", "a")) with { PcConfigPath = "" });

        var paths = context.Render<ToolsPane>().Find("[data-testid='tools-overview-footer-paths']");

        Assert.Equal(@"Catalog C:\tools\ai-tools.json", Squashed(paths.TextContent));
    }

    [Fact]
    public void A_read_only_host_gets_the_footer_paths_and_no_catalog_acts()
    {
        using var context = Context(FakeDevToolService.With(Tool("plugin:a", "a")) with { CanEdit = false });
        var footer = context.Render<ToolsPane>().Find("[data-testid='tools-overview-footer']");

        Assert.NotNull(footer.QuerySelector("[data-testid='tools-overview-footer-paths']"));
        Assert.Null(footer.QuerySelector("[data-testid='tools-add-open']"));
        Assert.Null(footer.QuerySelector("[data-testid='tools-import-open']"));
    }

    // ------------------------------------------------------------ B6 the fold label

    [Fact]
    public void A_fold_names_its_rows_in_the_tabs_noun_and_how_many_need_action()
    {
        using var context = Context(FakeDevToolService.With(
            Behind("plugin:a"), Tool("plugin:b", "b"), Tool("plugin:c", "c"),
            Tool("mcp:d", "d")));
        var pane = context.Render<ToolsPane>();

        Assert.Equal(
            "Plugins · 3 plugins · 1 needs action",
            FoldLabel(pane, "Plugin:"));
        Assert.Equal("MCP servers · 1 MCP server", FoldLabel(pane, "McpServer:"));
        Assert.NotNull(pane.Find("[data-tool-group='McpServer:'] [data-testid='tools-group-current']"));
    }

    // ------------------------------------------------------------ B7 the enable switch

    [Fact]
    public void A_rows_enable_control_is_a_switch_named_for_what_it_says()
    {
        using var context = Context(FakeDevToolService.With(Tool("plugin:a", "a")));
        var pane = context.Render<ToolsPane>();

        var control = pane.Find("[data-tool-key='plugin:a'] [role='switch']");
        Assert.Equal("true", control.GetAttribute("aria-checked"));

        var label = pane.Find($"#{control.GetAttribute("aria-labelledby")}");
        Assert.Equal("Enabled on this machine", label.TextContent.Trim());
        Assert.Contains("sr-only", label.ClassName, StringComparison.Ordinal);
    }

    [Fact]
    public void The_switch_disables_an_enabled_row_and_enables_a_disabled_one()
    {
        var service = FakeDevToolService.With(
            Tool("plugin:on", "on"),
            Tool("plugin:off", "off") with { ConfiguredEnabled = false });
        using var context = Context(service);
        var pane = context.Render<ToolsPane>();
        pane.Find("[data-testid='tools-tab-plugins']").Click();

        var off = pane.Find("[data-tool-key='plugin:off'] [role='switch']");
        Assert.Equal("false", off.GetAttribute("aria-checked"));

        pane.Find("[data-tool-key='plugin:on'] [role='switch']").Click();
        pane.WaitForAssertion(() => Assert.Equal(["plugin:on"], service.Disabled));

        pane.Find("[data-tool-key='plugin:off'] [role='switch']").Click();
        pane.WaitForAssertion(() => Assert.Equal(["plugin:off"], service.Enabled));
    }

    [Fact]
    public void The_switch_is_disabled_while_another_act_is_running()
    {
        var gate = new TaskCompletionSource<DevToolActionResult>();
        using var context = Context(FakeDevToolService.With(Behind("plugin:a")) with { UpdateAllRun = _ => gate.Task });
        var pane = context.Render<ToolsPane>();

        Assert.False(pane.Find("[data-tool-key='plugin:a'] [role='switch']").HasAttribute("disabled"));

        pane.Find("[data-testid='tools-update-all']").Click();

        pane.WaitForAssertion(() => Assert.True(pane.Find("[data-tool-key='plugin:a'] [role='switch']").HasAttribute("disabled")));

        gate.SetResult(DevToolActionResult.Ok("Updated."));
    }

    // ------------------------------------------------------------ B8 the held-back rows

    [Fact]
    public void An_open_group_lists_its_work_and_holds_the_rest_behind_a_reveal()
    {
        using var context = Context(FakeDevToolService.With(
            Tool("plugin:a", "a"), Behind("plugin:b"), Tool("plugin:c", "c"), Behind("plugin:d"), Tool("plugin:e", "e")));
        var pane = context.Render<ToolsPane>();
        pane.Find("[data-testid='tools-tab-plugins']").Click();

        Assert.Equal(["plugin:b", "plugin:d"], RowKeys(pane));
        var reveal = pane.Find("[data-testid='tools-plugins'] [data-testid='tools-group-reveal']");
        Assert.Equal("3 more up to date — show", reveal.TextContent.Trim());

        reveal.Click();

        Assert.Equal(["plugin:b", "plugin:d", "plugin:a", "plugin:c", "plugin:e"], RowKeys(pane));
        Assert.Empty(pane.FindAll("[data-testid='tools-plugins'] [data-testid='tools-group-reveal']"));
    }

    /// <summary>Only a row that is up to date is held back. A row switched off here
    /// is neither work nor up to date, so it stays on show after the work.</summary>
    [Fact]
    public void A_switched_off_row_is_not_held_back_as_up_to_date()
    {
        using var context = Context(FakeDevToolService.With(
            Tool("plugin:current", "current"),
            Tool("plugin:off", "off") with { ConfiguredEnabled = false },
            Behind("plugin:behind")));
        var pane = context.Render<ToolsPane>();
        pane.Find("[data-testid='tools-tab-plugins']").Click();

        Assert.Equal(["plugin:behind", "plugin:off"], RowKeys(pane));
        Assert.Equal(
            "1 more up to date — show",
            pane.Find("[data-testid='tools-plugins'] [data-testid='tools-group-reveal']").TextContent.Trim());
    }

    /// <summary>Held per group like the fold is, so a check for updates — which
    /// re-reads every row — does not hide again what the reader asked to see.</summary>
    [Fact]
    public void A_revealed_group_stays_revealed_across_a_refresh()
    {
        var service = FakeDevToolService.With(Behind("plugin:a"), Tool("plugin:b", "b"));
        using var context = Context(service);
        var pane = context.Render<ToolsPane>();
        pane.Find("[data-testid='tools-tab-plugins']").Click();
        pane.Find("[data-testid='tools-plugins'] [data-testid='tools-group-reveal']").Click();

        pane.Find("[data-testid='tools-refresh']").Click();
        pane.WaitForAssertion(() => Assert.True(service.Reads >= 2));

        Assert.Equal(["plugin:a", "plugin:b"], RowKeys(pane));
    }

    [Fact]
    public void A_group_with_only_work_or_only_matches_has_nothing_to_reveal()
    {
        using var context = Context(FakeDevToolService.With(
            Behind("plugin:a"), Behind("plugin:b"),
            Tool("mcp:c", "c"), Tool("mcp:d", "d")));
        var pane = context.Render<ToolsPane>();

        Assert.Empty(pane.FindAll("[data-testid='tools-group-reveal']"));
        Assert.Equal(2, pane.FindAll("[data-tool-group='McpServer:'] [data-tool-key]").Count);
    }

    // ------------------------------------------------------------ helpers

    private static FakeTimeProvider Clock()
    {
        var clock = new FakeTimeProvider(Now);
        clock.SetLocalTimeZone(TimeZoneInfo.Utc);
        return clock;
    }

    private static DevToolInfo Behind(string key) => Tool(key, key[(key.IndexOf(':') + 1)..]) with { AvailableVersion = "1.1.0" };

    private static DevToolInfo Missing(string key) =>
        Tool(key, key[(key.IndexOf(':') + 1)..]) with { Installed = false, InstalledVersion = "not installed" };

    private static string Headline(IRenderedComponent<ToolsPane> pane) => pane.Find("#installed-tools-title").TextContent.Trim();

    private static string Checked(IRenderedComponent<ToolsPane> pane) => pane.Find("[data-testid='tools-checked']").TextContent.Trim();

    private static string FoldLabel(IRenderedComponent<ToolsPane> pane, string group) =>
        Squashed(pane.Find($"[data-tool-group='{group}'] [data-testid='tools-group-toggle'] .fold__label").TextContent);

    private static IEnumerable<string?> RowKeys(IRenderedComponent<ToolsPane> pane) =>
        pane.FindAll("[data-testid='tools-plugins'] [data-tool-key]").Select(row => row.GetAttribute("data-tool-key"));

    /// <summary>Text with its markup whitespace collapsed, so an assertion reads the
    /// sentence a person sees.</summary>
    private static string Squashed(string text) =>
        string.Join(' ', text.Split((char[])[' ', '\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries));
}
