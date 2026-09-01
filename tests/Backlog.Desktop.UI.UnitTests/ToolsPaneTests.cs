using Backlog.Modules.DevPc.UI;
using Bunit;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The tools surface, against a fake port. Three states with three different
/// things to do in them, the rule that an edit only re-reads the catalog when
/// the port said it worked, and what a row offers for the tool it is showing.
///
/// <para>The pane is rendered directly rather than through Home: what it draws is
/// decided entirely by the <see cref="DevToolCatalog"/> it is handed, and going
/// through the shell would mean standing up a workspace to change a boolean.</para>
///
/// <para>The command log is tested here for the same reason: it is the pane's
/// answer to the host no longer flashing a console window per child process, and
/// suppressing those windows without putting the output somewhere would have
/// deleted the only place a refused install could be read.</para>
///
/// <para>The action tests are about a different silence: the cell had one button
/// and one note, so every row it could not offer an update for was announced as
/// up to date — including the ones the machine did not have and the ones nothing
/// had managed to look up.</para>
/// </summary>
public sealed class ToolsPaneTests
{
    [Fact]
    public void No_catalog_offers_creating_one_and_names_where_it_goes()
    {
        using var context = Context(FakeDevToolService.WithoutCatalog(@"C:\tools\ai-tools.json"));

        var pane = context.Render<ToolsPane>();

        var empty = pane.Find("[data-testid='tools-empty-no-catalog']");
        Assert.Contains(@"C:\tools\ai-tools.json", empty.TextContent, StringComparison.Ordinal);
        Assert.NotNull(pane.Find("[data-testid='tools-create-catalog']"));
        Assert.NotNull(pane.Find("[data-testid='tools-import-open']"));

        // The other two states, and the two acts that read a catalog there is not.
        Assert.Empty(pane.FindAll("[data-testid='tools-empty-no-entries']"));
        Assert.Empty(pane.FindAll(".tools-table"));
        Assert.All(
            pane.FindAll(".tools-panel__toolbar-actions button"),
            button => Assert.True(button.HasAttribute("disabled")));
    }

    [Fact]
    public void An_empty_catalog_offers_adding_a_tool_rather_than_creating_the_catalog_again()
    {
        using var context = Context(FakeDevToolService.With());

        var pane = context.Render<ToolsPane>();

        Assert.NotNull(pane.Find("[data-testid='tools-empty-no-entries']"));
        Assert.NotNull(pane.Find("[data-testid='tools-add-open']"));
        Assert.Empty(pane.FindAll("[data-testid='tools-empty-no-catalog']"));
        Assert.Empty(pane.FindAll("[data-testid='tools-create-catalog']"));
    }

    [Fact]
    public void A_first_read_draws_the_inventory_it_is_waiting_for_rather_than_a_sentence()
    {
        // The read walks the machine, so this pane is empty for seconds at a time.
        // A line of grey text there was indistinguishable from a pane that had
        // failed to draw - the defect this placeholder exists for.
        var reading = new TaskCompletionSource();
        using var context = Context(FakeDevToolService.With() with { Reading = reading });

        var pane = context.Render<ToolsPane>();

        var loading = pane.Find("[data-testid='tools-loading']");
        Assert.NotEmpty(loading.QuerySelectorAll(".skeleton"));

        // In the shape of the thing that is coming: the pane's own kind sections
        // and its own four-column rows, not a placeholder layout of its own.
        Assert.NotEmpty(loading.QuerySelectorAll(".tools-kind"));
        Assert.NotEmpty(loading.QuerySelectorAll(".tools-table__row"));

        // And neither empty state, which is the other half of the confusion: a
        // pane still reading has not established that there is nothing to show.
        Assert.Empty(pane.FindAll("[data-testid='tools-empty-no-catalog']"));
        Assert.Empty(pane.FindAll("[data-testid='tools-empty-no-entries']"));

        // And the badge does not answer a question the pane has not asked yet.
        // _catalogExists starts false, so this used to open on "No catalog" -
        // a verdict, beside a placeholder promising rows were on their way.
        Assert.Equal("Checking", pane.Find(".tools-panel__count").TextContent.Trim());

        reading.SetResult();
        pane.WaitForAssertion(() => Assert.Empty(pane.FindAll("[data-testid='tools-loading']")));
        Assert.NotNull(pane.Find("[data-testid='tools-empty-no-entries']"));
        Assert.Equal("Check tools", pane.Find(".tools-panel__count").TextContent.Trim());
    }

    [Fact]
    public void The_placeholder_is_one_wait_rather_than_forty_announcements()
    {
        var reading = new TaskCompletionSource();
        using var context = Context(FakeDevToolService.With() with { Reading = reading });

        var pane = context.Render<ToolsPane>();

        // The bars say nothing. What is being waited for is said once, by the
        // toolbar's status line above them.
        Assert.Equal("true", pane.Find("[data-testid='tools-loading']").GetAttribute("aria-hidden"));
        var status = pane.Find(".tools-panel__message");
        Assert.Equal("status", status.GetAttribute("role"));
        Assert.Contains("Checking configured tools", status.TextContent, StringComparison.Ordinal);

        reading.SetResult();
    }

    [Fact]
    public void Re_checking_a_populated_pane_keeps_the_rows_and_says_so_in_the_header()
    {
        var service = FakeDevToolService.With(Tool("plugin:architecture", "architecture"));
        using var context = Context(service);

        var pane = context.Render<ToolsPane>();
        Assert.Empty(pane.FindAll("[data-testid='tools-refreshing']"));

        var reading = new TaskCompletionSource();
        service.Reading = reading;
        pane.Find("[data-testid='tools-refresh']").Click();

        // A re-check over versions already on screen must not blank them: what is
        // there is still true until a newer answer arrives, so the rows stay and
        // the header carries the only sign that anything is running.
        pane.WaitForAssertion(() => Assert.NotNull(pane.Find("[data-testid='tools-refreshing']")));
        Assert.NotEmpty(pane.FindAll("[data-tool-key]"));
        Assert.Empty(pane.FindAll("[data-testid='tools-loading']"));

        reading.SetResult();
        pane.WaitForAssertion(() => Assert.Empty(pane.FindAll("[data-testid='tools-refreshing']")));
    }

    [Fact]
    public void A_populated_catalog_draws_the_table_and_a_remove_on_every_row()
    {
        using var context = Context(FakeDevToolService.With(Tool("plugin:architecture", "architecture"), Tool("mcp:Guidelines", "guidelines")));

        var pane = context.Render<ToolsPane>();

        Assert.Equal(2, pane.FindAll("[data-tool-key]").Count);
        Assert.Equal(2, pane.FindAll("[data-testid='tools-row-remove']").Count);
        Assert.Empty(pane.FindAll("[data-testid='tools-empty-no-catalog']"));
        Assert.Empty(pane.FindAll("[data-testid='tools-empty-no-entries']"));

        // The toolbar grows the two acts the empty states were carrying.
        Assert.NotNull(pane.Find("[data-testid='tools-add-open']"));
        Assert.NotNull(pane.Find("[data-testid='tools-import-open']"));
    }

    [Fact]
    public void A_host_that_cannot_edit_the_catalog_is_offered_none_of_the_four_acts()
    {
        var service = FakeDevToolService.With(Tool("plugin:architecture", "architecture")) with { CanEdit = false };
        using var context = Context(service);

        var pane = context.Render<ToolsPane>();

        Assert.Empty(pane.FindAll("[data-testid='tools-create-catalog']"));
        Assert.Empty(pane.FindAll("[data-testid='tools-add-open']"));
        Assert.Empty(pane.FindAll("[data-testid='tools-import-open']"));
        Assert.Empty(pane.FindAll("[data-testid='tools-row-remove']"));
    }

    [Fact]
    public void Creating_the_catalog_calls_the_port_and_re_reads_it()
    {
        var service = FakeDevToolService.WithoutCatalog();
        using var context = Context(service);

        var pane = context.Render<ToolsPane>();
        var readsBefore = service.Reads;

        pane.Find("[data-testid='tools-create-catalog']").Click();

        Assert.Equal(1, service.Creates);
        Assert.Equal(readsBefore + 1, service.Reads);
    }

    [Fact]
    public void An_edit_the_port_refuses_leaves_the_reason_on_screen_and_does_not_re_read()
    {
        var service = FakeDevToolService.WithoutCatalog() with { Succeeds = false };
        using var context = Context(service);

        var pane = context.Render<ToolsPane>();
        var readsBefore = service.Reads;

        pane.Find("[data-testid='tools-create-catalog']").Click();

        Assert.Equal(1, service.Creates);

        // A refresh here would replace the refusal with the ordinary "showing
        // tools from ..." line, which is the one moment it is worth less.
        Assert.Equal(readsBefore, service.Reads);
        Assert.Contains(FakeDevToolService.RefusalMessage, pane.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Adding_a_plugin_sends_the_draft_the_form_was_filled_with()
    {
        var service = FakeDevToolService.With();
        using var context = Context(service);

        var pane = context.Render<ToolsPane>();
        pane.Find("[data-testid='tools-add-open']").Click();

        pane.Find("[data-testid='tools-add-name'] input").Input("architecture");
        pane.Find("[data-testid='tools-add-source'] input").Input("JSdotNet/Copilot:plugins/architecture");
        pane.Find("[data-testid='tools-add-submit']").Click();

        var draft = Assert.Single(service.Added);
        Assert.Equal(DevToolKind.Plugin, draft.Kind);
        Assert.Equal("architecture", draft.Id);
        Assert.Equal("JSdotNet/Copilot:plugins/architecture", draft.Source);
        Assert.Empty(pane.FindAll("[data-testid='tools-add-dialog']"));
    }

    [Fact]
    public void A_plugin_with_no_source_never_reaches_the_port()
    {
        var service = FakeDevToolService.With();
        using var context = Context(service);

        var pane = context.Render<ToolsPane>();
        pane.Find("[data-testid='tools-add-open']").Click();

        pane.Find("[data-testid='tools-add-name'] input").Input("architecture");
        pane.Find("[data-testid='tools-add-submit']").Click();

        Assert.Empty(service.Added);

        // The dialog stays open with the reason under the field it belongs to.
        Assert.NotNull(pane.Find("[data-testid='tools-add-dialog']"));
        Assert.Contains("A plugin needs a source.", pane.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Importing_sends_whatever_is_in_the_paste_box()
    {
        var service = FakeDevToolService.With();
        using var context = Context(service);

        var pane = context.Render<ToolsPane>();
        pane.Find("[data-testid='tools-import-open']").Click();

        pane.Find("[data-testid='tools-import-json'] textarea").Input("""{ "plugins": [] }""");
        pane.Find("[data-testid='tools-import-submit']").Click();

        Assert.Equal("""{ "plugins": [] }""", Assert.Single(service.Imported));
        Assert.Empty(pane.FindAll("[data-testid='tools-import-dialog']"));
    }

    [Fact]
    public void Removing_a_tool_asks_first_and_only_calls_the_port_on_the_confirm()
    {
        var service = FakeDevToolService.With(Tool("plugin:architecture", "architecture"));
        using var context = Context(service);

        var pane = context.Render<ToolsPane>();
        pane.Find("[data-testid='tools-row-remove']").Click();

        // The first click arms the question. Nothing has been removed yet.
        Assert.Empty(service.Removed);
        Assert.NotNull(pane.Find("[data-testid='tools-remove-dialog']"));

        pane.Find("[data-testid='tools-remove-confirm']").Click();

        Assert.Equal("plugin:architecture", Assert.Single(service.Removed));
    }

    [Fact]
    public void Cancelling_the_remove_question_removes_nothing()
    {
        var service = FakeDevToolService.With(Tool("plugin:architecture", "architecture"));
        using var context = Context(service);

        var pane = context.Render<ToolsPane>();
        pane.Find("[data-testid='tools-row-remove']").Click();
        pane.Find("[data-testid='tools-remove-cancel']").Click();

        Assert.Empty(service.Removed);
        Assert.Empty(pane.FindAll("[data-testid='tools-remove-dialog']"));
    }

    [Fact]
    public void The_commands_the_host_ran_are_on_screen_behind_a_fold()
    {
        var service = FakeDevToolService.With() with
        {
            Commands =
            [
                new("copilot --version", 0, "GitHub Copilot CLI 1.2.3"),
                new("dotnet tool search JSdotNet.MCP.Guidelines --exact-match", 1, "No packages found.")
            ]
        };
        using var context = Context(service);

        var pane = context.Render<ToolsPane>();

        // The trigger says how much there is to read, so a reader can tell an
        // empty check from a busy one without opening it.
        Assert.Contains("Command output (2)", pane.Find("[data-testid='tools-command-log']").TextContent, StringComparison.Ordinal);

        var transcript = pane.Find("[data-testid='tools-command-log-output']").TextContent;

        Assert.Contains("$ copilot --version", transcript, StringComparison.Ordinal);
        Assert.Contains("GitHub Copilot CLI 1.2.3", transcript, StringComparison.Ordinal);
        Assert.Contains("$ dotnet tool search JSdotNet.MCP.Guidelines --exact-match", transcript, StringComparison.Ordinal);
        Assert.Contains("No packages found.", transcript, StringComparison.Ordinal);

        // The failure carries its exit code and the success does not: a column of
        // zeroes would bury the one line worth finding.
        Assert.Contains("exit code 1", transcript, StringComparison.Ordinal);
        Assert.DoesNotContain("exit code 0", transcript, StringComparison.Ordinal);
    }

    /// <summary>A host that starts no processes — the browser's unsupported
    /// service, or a check that failed before it ran anything — has nothing to
    /// show, and an empty disclosure is worse than none.</summary>
    [Fact]
    public void A_host_that_ran_nothing_shows_no_fold()
    {
        using var context = Context(FakeDevToolService.With());

        var pane = context.Render<ToolsPane>();

        Assert.Empty(pane.FindAll("[data-testid='tools-command-log']"));
        Assert.Empty(pane.FindAll("[data-testid='tools-command-log-output']"));
    }

    /// <summary>The contradiction this pane was reported for: a plugin that is
    /// enabled in the catalog and absent from the machine was labelled "up to
    /// date" beside its own "not installed" version, because the only action the
    /// cell knew how to offer was an update.</summary>
    [Fact]
    public void An_enabled_tool_that_is_absent_offers_to_install_it()
    {
        var actions = ActionsFor(Tool(enabled: true, installed: false, "not installed", "0.4.0"));

        Assert.Contains("Install", actions, StringComparison.Ordinal);
        Assert.DoesNotContain("Up to date", actions, StringComparison.Ordinal);
    }

    /// <summary>A failed version lookup is not agreement. The cell says which of
    /// the two it is rather than reporting the tool as current.</summary>
    [Fact]
    public void A_tool_whose_available_version_is_unknown_says_so()
    {
        var actions = ActionsFor(Tool(enabled: true, installed: true, "0.4.0", "unknown"));

        Assert.Contains("Version unknown", actions, StringComparison.Ordinal);
        Assert.DoesNotContain("Up to date", actions, StringComparison.Ordinal);
    }

    [Fact]
    public void A_tool_the_machine_already_has_at_the_published_version_is_up_to_date()
    {
        var actions = ActionsFor(Tool(enabled: true, installed: true, "0.4.0", "0.4.0"));

        Assert.Contains("Up to date", actions, StringComparison.Ordinal);
        Assert.DoesNotContain("Install", actions, StringComparison.Ordinal);
    }

    [Fact]
    public void A_tool_switched_off_in_the_config_still_reads_as_disabled()
    {
        var actions = ActionsFor(Tool(enabled: false, installed: false, "not installed", "0.4.0"));

        Assert.Contains("Disabled", actions, StringComparison.Ordinal);
        Assert.DoesNotContain("Install", actions, StringComparison.Ordinal);
    }

    /// <summary>
    /// Each version cell carries its own label, because the heading row is not
    /// always there to carry it. Narrow enough and the four columns stack — the pane
    /// can be 338px wide while the row's tracks need 504px — and a stack under a
    /// hidden heading row leaves two bare version strings with nothing saying which
    /// is installed and which is available. The labels are in the markup at every
    /// width and the stylesheet decides when they show; see
    /// <see cref="ToolsTableLayoutTests"/> for the other half.
    /// </summary>
    [Fact]
    public void Every_version_cell_says_which_version_it_is()
    {
        using var context = Context(FakeDevToolService.With(
            Tool(enabled: true, installed: true, "0.4.0", "0.5.0")));

        var row = context.Render<ToolsPane>().Find("[data-tool-key]");
        var cells = row.QuerySelectorAll(".tools-table__version");

        Assert.Equal(2, cells.Length);
        Assert.Equal(["Installed", "Available"], cells
            .Select(cell => cell.QuerySelector(".tools-table__cell-label")!.TextContent)
            .ToArray());

        // The label sits beside the version rather than replacing it.
        Assert.Contains("0.4.0", cells[0].TextContent, StringComparison.Ordinal);
        Assert.Contains("0.5.0", cells[1].TextContent, StringComparison.Ordinal);
    }

    /// <summary>Which hosts a row is for decides what its Update actually does.
    /// A catalog driving two hosts through one row is unreadable without it being
    /// said, and "says nothing" reads as "Copilot only".</summary>
    [Fact]
    public void Every_row_says_which_hosts_it_is_for()
    {
        var service = FakeDevToolService.With(
            Tool("plugin:architecture", "architecture") with { Hosts = DevToolHosts.Default },
            Tool("plugin:claude-desktop", "claude-desktop") with { Hosts = DevToolHosts.Claude },
            Tool("plugin:copilot-app", "copilot-app") with { Hosts = DevToolHosts.Copilot });
        using var context = Context(service);

        var pane = context.Render<ToolsPane>();
        var labels = pane.FindAll("[data-testid='tools-row-hosts']").Select(badge => badge.TextContent).ToArray();

        Assert.Equal(["Copilot + Claude", "Claude", "Copilot"], labels);
    }

    /// <summary>An application is machine software, not a registration with an AI
    /// tool, so it has no host to name and gets no badge at all.
    ///
    /// <para>It used to get one reading "No host", which is what the label says for
    /// the flags value an application deliberately carries. That answered a
    /// question the row had never asked, on every one of the fifty-odd application
    /// rows at once.</para></summary>
    [Fact]
    public void An_application_row_carries_no_host_badge()
    {
        var application = Tool("app:Microsoft.VisualStudioCode", "Visual Studio Code") with
        {
            Kind = DevToolKind.Application,
            Hosts = DevToolHosts.None
        };
        using var context = Context(FakeDevToolService.With(application));

        var pane = context.Render<ToolsPane>();

        Assert.Empty(pane.FindAll("[data-testid='tools-row-hosts']"));
    }

    /// <summary>The badge carries what each host answered, when there is more than
    /// one answer to carry. A row whose hosts agree gets no tooltip — the columns
    /// already say it.</summary>
    [Fact]
    public void A_row_whose_hosts_disagree_names_both_answers()
    {
        var tool = Tool("plugin:architecture", "architecture") with
        {
            HostStates =
            [
                new(DevToolHosts.Copilot, true, "1.2.0", "1.2.0", "Enabled plugin"),
                new(DevToolHosts.Claude, false, "not installed", "1.2.0", "Not installed")
            ]
        };
        using var context = Context(FakeDevToolService.With(tool));

        var badge = context.Render<ToolsPane>().Find("[data-testid='tools-row-hosts']");

        Assert.Contains("Copilot: Enabled plugin", badge.GetAttribute("title"), StringComparison.Ordinal);
        Assert.Contains("Claude: Not installed", badge.GetAttribute("title"), StringComparison.Ordinal);
    }

    /// <summary>A plugin Copilot already has and Claude has never heard of is
    /// still a plugin this machine is short of, and one press acts on both.</summary>
    [Fact]
    public void A_plugin_missing_on_only_one_host_still_offers_to_install_it()
    {
        var actions = ActionsFor(Tool("plugin:architecture", "architecture") with
        {
            HostStates =
            [
                new(DevToolHosts.Copilot, true, "1.2.0", "1.2.0", "Enabled plugin"),
                new(DevToolHosts.Claude, false, "not installed", "1.2.0", "Not installed")
            ]
        });

        Assert.Contains("Install", actions, StringComparison.Ordinal);
        Assert.DoesNotContain("Up to date", actions, StringComparison.Ordinal);
    }

    /// <summary>A marketplace has no version to be behind, so it never earns an
    /// Update the way a tool does. What it has is two states and one verb for
    /// each.</summary>
    [Theory]
    [InlineData(true, "Update")]
    [InlineData(false, "Add")]
    public void A_marketplace_row_offers_adding_or_refreshing_it(bool known, string expected)
    {
        var actions = ActionsFor(Marketplace(known));

        Assert.Contains(expected, actions, StringComparison.Ordinal);
    }

    /// <summary>It is not a tool this machine opts into — it is where the Claude
    /// plugins that do come from — so the only way to stop wanting one is to
    /// remove it.</summary>
    [Fact]
    public void A_marketplace_row_cannot_be_disabled()
    {
        var actions = ActionsFor(Marketplace(known: true));

        Assert.DoesNotContain("Disable", actions, StringComparison.Ordinal);
        Assert.DoesNotContain("Enable", actions, StringComparison.Ordinal);
    }

    /// <summary>A marketplace Claude has never been told about is the one gap that
    /// fails a whole block of rows for a single reason, so "Update all" is offered
    /// for it even though a marketplace has no version to be behind.</summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void Update_all_is_offered_while_a_marketplace_is_still_missing(bool known, bool expectDisabled)
    {
        using var context = Context(FakeDevToolService.With(Marketplace(known)));

        var pane = context.Render<ToolsPane>();
        var updateAll = pane.FindAll(".tools-panel__toolbar-actions button")[1];

        Assert.Equal(expectDisabled, updateAll.HasAttribute("disabled"));
    }

    [Fact]
    public void Adding_a_marketplace_sends_a_name_and_a_source()
    {
        var service = FakeDevToolService.With();
        using var context = Context(service);

        var pane = context.Render<ToolsPane>();
        pane.Find("[data-testid='tools-add-open']").Click();
        pane.Find("[data-testid='tools-add-kind'] select").Change(nameof(DevToolKind.Marketplace));

        pane.Find("[data-testid='tools-add-name'] input").Input("jsdotnet-copilot");
        pane.Find("[data-testid='tools-add-source'] input").Input("JSdotNet/Copilot");
        pane.Find("[data-testid='tools-add-submit']").Click();

        var draft = Assert.Single(service.Added);
        Assert.Equal(DevToolKind.Marketplace, draft.Kind);
        Assert.Equal("jsdotnet-copilot", draft.Id);
        Assert.Equal("JSdotNet/Copilot", draft.Source);

        // A marketplace is a Claude mechanism whatever the hosts selector last had,
        // and the selector is not on screen for one.
        Assert.Equal(DevToolHosts.Claude, draft.Hosts);
    }

    [Fact]
    public void A_marketplace_with_no_source_never_reaches_the_port()
    {
        var service = FakeDevToolService.With();
        using var context = Context(service);

        var pane = context.Render<ToolsPane>();
        pane.Find("[data-testid='tools-add-open']").Click();
        pane.Find("[data-testid='tools-add-kind'] select").Change(nameof(DevToolKind.Marketplace));

        pane.Find("[data-testid='tools-add-name'] input").Input("jsdotnet-copilot");
        pane.Find("[data-testid='tools-add-submit']").Click();

        Assert.Empty(service.Added);
        Assert.Contains("A marketplace needs a source.", pane.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Adding_a_claude_only_plugin_sends_its_hosts_and_its_claude_names()
    {
        var service = FakeDevToolService.With();
        using var context = Context(service);

        var pane = context.Render<ToolsPane>();
        pane.Find("[data-testid='tools-add-open']").Click();
        pane.Find("[data-testid='tools-add-hosts'] select").Change(nameof(DevToolHosts.Claude));

        pane.Find("[data-testid='tools-add-name'] input").Input("jsdotnet-project-guidelines");
        pane.Find("[data-testid='tools-add-source'] input").Input("JSdotNet/Copilot:plugins/guidelines");
        pane.Find("[data-testid='tools-add-claude-name'] input").Input("guidelines");
        pane.Find("[data-testid='tools-add-claude-marketplace'] input").Input("anthropic-skills");
        pane.Find("[data-testid='tools-add-submit']").Click();

        var draft = Assert.Single(service.Added);
        Assert.Equal(DevToolHosts.Claude, draft.Hosts);
        Assert.Equal("guidelines", draft.ClaudeName);
        Assert.Equal("anthropic-skills", draft.ClaudeMarketplace);
    }

    /// <summary>Both blank in the ordinary case: the Claude id falls back to the
    /// plugin's own name and to the first marketplace in the catalog, and writing
    /// either out when it only restates the fallback makes the catalog harder to
    /// read.</summary>
    [Fact]
    public void A_copilot_only_plugin_is_not_asked_about_claude_at_all()
    {
        using var context = Context(FakeDevToolService.With());

        var pane = context.Render<ToolsPane>();
        pane.Find("[data-testid='tools-add-open']").Click();
        pane.Find("[data-testid='tools-add-hosts'] select").Change(nameof(DevToolHosts.Copilot));

        Assert.Empty(pane.FindAll("[data-testid='tools-add-claude-name']"));
        Assert.Empty(pane.FindAll("[data-testid='tools-add-claude-marketplace']"));
    }

    [Fact]
    public void Adding_an_mcp_server_sends_the_claude_registration_it_was_given()
    {
        var service = FakeDevToolService.With();
        using var context = Context(service);

        var pane = context.Render<ToolsPane>();
        pane.Find("[data-testid='tools-add-open']").Click();
        pane.Find("[data-testid='tools-add-kind'] select").Change(nameof(DevToolKind.McpServer));

        pane.Find("[data-testid='tools-add-package-id'] input").Input("JSdotNet.MCP.Guidelines");
        pane.Find("[data-testid='tools-add-name'] input").Input("jsdotnet-project-guidelines");
        pane.Find("[data-testid='tools-add-claude-server-name'] input").Input("jsdotnet-coding-guidelines");
        pane.Find("[data-testid='tools-add-claude-command'] input").Input("jsdotnet-guidelines-mcpserver");
        pane.Find("[data-testid='tools-add-claude-args'] input").Input("agent mcp");
        pane.Find("[data-testid='tools-add-submit']").Click();

        var draft = Assert.Single(service.Added);
        Assert.Equal(DevToolKind.McpServer, draft.Kind);
        Assert.Equal("jsdotnet-coding-guidelines", draft.ClaudeServerName);
        Assert.Equal("jsdotnet-guidelines-mcpserver", draft.ClaudeCommand);
        Assert.Equal(["agent", "mcp"], draft.ClaudeArgs);
    }

    /// <summary>The catalog stopped being six rows of AI tooling and became this
    /// machine's whole software inventory, so the kind is the first thing a row
    /// has to be filed under: "a plugin is missing" and "an application is
    /// missing" are two different afternoons.</summary>
    [Fact]
    public void Rows_are_filed_under_a_heading_per_kind()
    {
        using var context = Context(FakeDevToolService.With(
            Tool("plugin:architecture", "architecture"),
            Tool("mcp:Guidelines", "guidelines"),
            Application("Git.Git", "Git")));

        var pane = context.Render<ToolsPane>();
        var kinds = pane.FindAll("[data-testid='tools-kind']");

        Assert.Equal(
            [nameof(DevToolKind.Plugin), nameof(DevToolKind.McpServer), nameof(DevToolKind.Application)],
            kinds.Select(section => section.GetAttribute("data-tool-kind")));
        Assert.Contains("Plugins", kinds[0].TextContent, StringComparison.Ordinal);
        Assert.Contains("MCP servers", kinds[1].TextContent, StringComparison.Ordinal);
        Assert.Contains("Applications", kinds[2].TextContent, StringComparison.Ordinal);
    }

    /// <summary>Within the applications the entry's own group is the sub-heading.
    /// The setup guide this catalog follows is a sequence of steps, and fifty rows
    /// in one list lose it.</summary>
    [Fact]
    public void Applications_are_filed_under_the_group_the_catalog_gave_them()
    {
        using var context = Context(FakeDevToolService.With(
            Application("Git.Git", "Git", group: "Developer Configurations baseline"),
            Application("git-pull-rebase", "git pull.rebase is true", group: "Git configuration"),
            Application("git-rebase-autostash", "git rebase.autoStash is true", group: "Git configuration")));

        var pane = context.Render<ToolsPane>();
        var groups = pane.FindAll("[data-tool-group]");

        Assert.Equal(2, groups.Count);

        var git = pane.Find("[data-tool-group='Application:Git configuration']");

        Assert.NotNull(git.QuerySelector("[data-tool-key='app:git-pull-rebase']"));
        Assert.NotNull(git.QuerySelector("[data-tool-key='app:git-rebase-autostash']"));
        Assert.Null(git.QuerySelector("[data-tool-key='app:Git.Git']"));

        // The trigger says how much is behind it, so a folded group can be read
        // without opening it.
        Assert.Contains("Git configuration", git.QuerySelector("[data-testid='tools-group-toggle']")!.TextContent, StringComparison.Ordinal);
        Assert.Contains("2 row(s)", git.QuerySelector("[data-testid='tools-group-toggle']")!.TextContent, StringComparison.Ordinal);
    }

    /// <summary>A machine that is set up correctly should open this pane to
    /// headings and no rows. The fifty-odd rows behind them are worth having and
    /// are not worth scrolling past every time.</summary>
    [Fact]
    public void A_group_with_nothing_outstanding_starts_folded_and_one_with_work_does_not()
    {
        using var context = Context(FakeDevToolService.With(
            Application("Microsoft.VisualStudioCode", "Visual Studio Code", group: "Baseline"),
            Application("Microsoft.PowerToys", "PowerToys", group: "Team tools", installed: false, installedVersion: "not installed", availableVersion: "0.96.1")));

        var pane = context.Render<ToolsPane>();

        Assert.True(pane.Find("[data-tool-group='Application:Baseline'] .tools-table").HasAttribute("hidden"));
        Assert.False(pane.Find("[data-tool-group='Application:Team tools'] .tools-table").HasAttribute("hidden"));
    }

    [Fact]
    public void A_folded_group_opens_when_it_is_asked_to()
    {
        using var context = Context(FakeDevToolService.With(
            Application("Microsoft.VisualStudioCode", "Visual Studio Code", group: "Baseline")));

        var pane = context.Render<ToolsPane>();
        pane.Find("[data-testid='tools-group-toggle']").Click();

        Assert.False(pane.Find("[data-tool-group='Application:Baseline'] .tools-table").HasAttribute("hidden"));
    }

    /// <summary>The package manager knows it and cannot install it unattended —
    /// an IDE whose workloads need an hour and an override, a suite whose
    /// activation is a sign-in. The row still reports what is installed; it just
    /// has no button, and it says where the reason is.</summary>
    [Fact]
    public void A_package_that_has_to_be_installed_by_hand_is_offered_no_button()
    {
        var tool = Application(
            "Microsoft.Office",
            "Microsoft 365 apps",
            installed: false,
            installable: false,
            installedVersion: "not installed",
            availableVersion: "16.0.18827.20164",
            status: "Not installed · Click-to-Run activation is interactive.");

        using var context = Context(FakeDevToolService.With(tool));
        var pane = context.Render<ToolsPane>();

        Assert.Equal("Install by hand — see the note", pane.Find(".tools-table__action-note").TextContent);
        Assert.DoesNotContain(
            pane.FindAll(".tools-table__actions button"),
            button => button.TextContent.Trim() == "Install");

        // The reason is on the row rather than in a status line that scrolls away.
        Assert.Equal("Click-to-Run activation is interactive.", pane.Find("[data-testid='tools-row-note']").TextContent);
    }

    /// <summary>A checklist row has no version on either side, so every
    /// version-shaped answer is a claim about nothing. "Version unknown" was true
    /// and useless: the row never had one to look up.</summary>
    [Theory]
    [InlineData(true, "Detected")]
    [InlineData(false, "Not detected")]
    public void A_checklist_row_reports_detection_rather_than_a_version(bool detected, string expected)
    {
        var tool = Application(
            "dev-drive",
            "Dev Drive configured",
            installed: detected,
            installable: false,
            installedVersion: DevToolOutput.NoVersion,
            availableVersion: DevToolOutput.NoVersion,
            status: detected ? "Checklist item: done" : "Checklist item: not done yet");

        using var context = Context(FakeDevToolService.With(tool));
        var pane = context.Render<ToolsPane>();

        Assert.Equal(expected, InstalledValue(pane));
        Assert.Equal(expected, pane.Find(".tools-table__action-note").TextContent);
        Assert.DoesNotContain("Version unknown", pane.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Up to date", pane.Markup, StringComparison.Ordinal);
    }

    /// <summary>Two of the checklist rows declare an install of their own — a git
    /// config that can set itself. Those are checklist items that can fix
    /// themselves, and they keep the button the others do not have.</summary>
    [Fact]
    public void A_checklist_row_that_can_fix_itself_still_offers_the_fix()
    {
        var actions = ActionsFor(Application(
            "git-pull-rebase",
            "git pull.rebase is true",
            installed: false,
            installedVersion: DevToolOutput.NoVersion,
            availableVersion: DevToolOutput.NoVersion,
            status: "Not configured"));

        Assert.Contains("Install", actions, StringComparison.Ordinal);
        Assert.DoesNotContain("Not detected", actions, StringComparison.Ordinal);
    }

    /// <summary>Nothing checked this machine; somebody ticked a box on it. The row
    /// says so in the Installed column too, because a manual row drawn as a found
    /// state is what would make the other fifty worth less.</summary>
    [Theory]
    [InlineData(false, "Not confirmed")]
    [InlineData(true, "Confirmed by hand")]
    public void A_row_nothing_can_check_never_reads_as_verified(bool acknowledged, string expected)
    {
        using var context = Context(FakeDevToolService.With(ManualApplication(acknowledged)));
        var pane = context.Render<ToolsPane>();

        Assert.Equal(expected, InstalledValue(pane));
        Assert.Empty(pane.FindAll(".tools-table__action-note"));
    }

    /// <summary>The defect the acknowledgement port method exists for: ticking the
    /// box used to go through Update, which was the row's only control — so the
    /// tick removed the only way to take it back.</summary>
    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void A_manual_row_can_be_ticked_and_unticked(bool acknowledged, bool expected)
    {
        var service = FakeDevToolService.With(ManualApplication(acknowledged));
        using var context = Context(service);

        var pane = context.Render<ToolsPane>();
        pane.Find("[data-testid='tools-row-acknowledge'] input").Change(expected);

        Assert.Equal(("app:office-signed-in", expected), Assert.Single(service.Acknowledgements));
    }

    /// <summary>A manual row is never offered an install: there is nothing to run
    /// and nothing that could report having run it.</summary>
    [Fact]
    public void A_manual_row_is_offered_a_box_and_not_a_button()
    {
        using var context = Context(FakeDevToolService.With(ManualApplication(acknowledged: false)));
        var pane = context.Render<ToolsPane>();

        Assert.NotNull(pane.Find("[data-testid='tools-row-acknowledge']"));
        Assert.DoesNotContain(
            pane.FindAll(".tools-table__actions button"),
            button => button.TextContent.Trim() is "Install" or "Update");
    }

    /// <summary>A row this machine has switched off has nothing to confirm, and a
    /// tick on one would be a statement about something it has said it is not
    /// doing.</summary>
    [Fact]
    public void A_manual_row_this_machine_has_switched_off_is_offered_no_box()
    {
        var tool = ManualApplication(acknowledged: false) with { ConfiguredEnabled = false };
        using var context = Context(FakeDevToolService.With(tool));

        var pane = context.Render<ToolsPane>();

        Assert.Empty(pane.FindAll("[data-testid='tools-row-acknowledge']"));
        Assert.Equal("Disabled", pane.Find(".tools-table__action-note").TextContent);
    }

    /// <summary>The add form's kind chain is binary-exhaustive and falls through
    /// to the MCP server, so an application without a branch of its own would have
    /// been offered a package id and a Claude registration.</summary>
    [Fact]
    public void Choosing_an_application_asks_about_an_application()
    {
        using var context = Context(FakeDevToolService.With());

        var pane = context.Render<ToolsPane>();
        pane.Find("[data-testid='tools-add-open']").Click();
        pane.Find("[data-testid='tools-add-kind'] select").Change(nameof(DevToolKind.Application));

        Assert.NotNull(pane.Find("[data-testid='tools-add-provider']"));
        Assert.NotNull(pane.Find("[data-testid='tools-add-app-id']"));

        // Neither the MCP server's fields nor the hosts selector: an application is
        // installed into the machine, not into an AI host.
        Assert.Empty(pane.FindAll("[data-testid='tools-add-package-id']"));
        Assert.Empty(pane.FindAll("[data-testid='tools-add-claude-command']"));
        Assert.Empty(pane.FindAll("[data-testid='tools-add-hosts']"));

        // The command boxes belong to the one mechanism that has to be told how to
        // find itself, and winget is not it.
        Assert.Empty(pane.FindAll("[data-testid='tools-add-detect-command']"));
    }

    [Fact]
    public void Adding_a_winget_application_sends_its_id_and_its_provider()
    {
        var service = FakeDevToolService.With();
        using var context = Context(service);

        var pane = context.Render<ToolsPane>();
        pane.Find("[data-testid='tools-add-open']").Click();
        pane.Find("[data-testid='tools-add-kind'] select").Change(nameof(DevToolKind.Application));

        pane.Find("[data-testid='tools-add-app-id'] input").Input("Microsoft.PowerToys");
        pane.Find("[data-testid='tools-add-name'] input").Input("PowerToys");
        pane.Find("[data-testid='tools-add-submit']").Click();

        var draft = Assert.Single(service.Added);
        Assert.Equal(DevToolKind.Application, draft.Kind);
        Assert.Equal("Microsoft.PowerToys", draft.Id);
        Assert.Equal("PowerToys", draft.DisplayName);
        Assert.Equal(DevToolProvider.Winget, draft.Provider);
        Assert.Null(draft.Source);

        // Not a host's tool, whatever the hosts selector was last left on for some
        // other kind.
        Assert.Equal(DevToolHosts.None, draft.Hosts);
    }

    [Fact]
    public void Adding_a_command_application_sends_the_commands_it_was_given()
    {
        var service = FakeDevToolService.With();
        using var context = Context(service);

        var pane = context.Render<ToolsPane>();
        pane.Find("[data-testid='tools-add-open']").Click();
        pane.Find("[data-testid='tools-add-kind'] select").Change(nameof(DevToolKind.Application));
        pane.Find("[data-testid='tools-add-provider'] select").Change(nameof(DevToolProvider.Command));

        pane.Find("[data-testid='tools-add-app-id'] input").Input("git-pull-rebase");
        pane.Find("[data-testid='tools-add-name'] input").Input("git pull.rebase is true");
        pane.Find("[data-testid='tools-add-detect-command'] input").Input("git");
        pane.Find("[data-testid='tools-add-detect-args'] input").Input("config --global pull.rebase");
        pane.Find("[data-testid='tools-add-detect-expect'] input").Input("true");
        pane.Find("[data-testid='tools-add-install-command'] input").Input("git");
        pane.Find("[data-testid='tools-add-install-args'] input").Input("config --global pull.rebase true");
        pane.Find("[data-testid='tools-add-submit']").Click();

        var draft = Assert.Single(service.Added);
        Assert.Equal(DevToolProvider.Command, draft.Provider);
        Assert.Equal("git", draft.DetectCommand);
        Assert.Equal(["config", "--global", "pull.rebase"], draft.DetectArgs);
        Assert.Equal("true", draft.DetectExpect);
        Assert.Equal("git", draft.InstallCommand);
        Assert.Equal(["config", "--global", "pull.rebase", "true"], draft.InstallArgs);
    }

    /// <summary>The other mechanisms know how to find their own package. A command
    /// entry that was not told can never answer whether the machine has it, so it
    /// is refused under the field it is about rather than written and drawn
    /// forever as a row of unknowns.</summary>
    [Fact]
    public void A_command_application_with_no_detect_command_never_reaches_the_port()
    {
        var service = FakeDevToolService.With();
        using var context = Context(service);

        var pane = context.Render<ToolsPane>();
        pane.Find("[data-testid='tools-add-open']").Click();
        pane.Find("[data-testid='tools-add-kind'] select").Change(nameof(DevToolKind.Application));
        pane.Find("[data-testid='tools-add-provider'] select").Change(nameof(DevToolProvider.Command));

        pane.Find("[data-testid='tools-add-app-id'] input").Input("dev-drive");
        pane.Find("[data-testid='tools-add-submit']").Click();

        Assert.Empty(service.Added);
        Assert.Contains("A command application needs a detect command.", pane.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void An_application_with_no_id_never_reaches_the_port()
    {
        var service = FakeDevToolService.With();
        using var context = Context(service);

        var pane = context.Render<ToolsPane>();
        pane.Find("[data-testid='tools-add-open']").Click();
        pane.Find("[data-testid='tools-add-kind'] select").Change(nameof(DevToolKind.Application));
        pane.Find("[data-testid='tools-add-submit']").Click();

        Assert.Empty(service.Added);
        Assert.Contains("An application needs an id.", pane.Markup, StringComparison.Ordinal);
    }

    /// <summary>One application row, as the port would answer it. Ungrouped by
    /// default, because an ungrouped kind is always open and a test about an
    /// action cell should not also be a test about a disclosure.</summary>
    private static DevToolInfo Application(
        string id,
        string name,
        string? group = null,
        bool installed = true,
        bool installable = true,
        bool enabled = true,
        string installedVersion = "1.0.0",
        string availableVersion = "1.0.0",
        string status = "Application installed") =>
        new(
            $"app:{id}",
            DevToolKind.Application,
            name,
            "winget",
            enabled,
            installed,
            installedVersion,
            availableVersion,
            status)
        {
            Hosts = DevToolHosts.None,
            Group = group,
            Installable = installable
        };

    /// <summary>A row nothing can probe: its detected state is the acknowledgement
    /// and nothing else.</summary>
    private static DevToolInfo ManualApplication(bool acknowledged) =>
        Application(
            "office-signed-in",
            "Office apps activated and signed in",
            installed: acknowledged,
            installable: false,
            installedVersion: DevToolOutput.NotInstalled,
            availableVersion: DevToolOutput.NoVersion,
            status: acknowledged ? "Confirmed by hand" : "Nothing can check this") with
        {
            Acknowledged = acknowledged,
            ConfirmedByHand = true
        };

    private static BunitContext Context(IDevToolService service)
    {
        var context = new BunitContext();

        // The modal moves focus into itself when it opens, which is a JS call.
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Services.AddSingleton(service);
        return context;
    }

    private static DevToolInfo Tool(string key, string name) => new(
        key,
        key.StartsWith("plugin:", StringComparison.Ordinal) ? DevToolKind.Plugin : DevToolKind.McpServer,
        name,
        "source",
        ConfiguredEnabled: true,
        Installed: true,
        "1.0.0",
        "1.0.0",
        "Enabled plugin");

    /// <summary>The one row's action cell, as text. The pane is rendered with a
    /// single tool so the cell is unambiguous without reaching for an index.</summary>
    private static string ActionsFor(DevToolInfo tool)
    {
        using var context = Context(FakeDevToolService.With(tool));

        return context.Render<ToolsPane>().Find(".tools-table__actions").TextContent;
    }

    /// <summary>What the Installed cell reports, without the column label in front
    /// of it.
    ///
    /// <para>The label is in the markup at every width — the narrow layout hides the
    /// heading row, so each version cell has to name itself, see
    /// <see cref="Every_version_cell_says_which_version_it_is"/>. A test about what
    /// a row <em>says</em> is not a test about that label, so it reads past it
    /// rather than pinning both to one string.</para></summary>
    private static string InstalledValue(IRenderedComponent<ToolsPane> pane)
    {
        var cell = pane.Find("[data-testid='tools-row-installed']");
        var label = cell.QuerySelector(".tools-table__cell-label")?.TextContent ?? string.Empty;

        return cell.TextContent[label.Length..].Trim();
    }

    /// <summary>A Claude marketplace row, either already known to Claude or not
    /// yet added. Its Available column is honest rather than empty: there is no
    /// published version to compare a marketplace against.</summary>
    private static DevToolInfo Marketplace(bool known) => new(
        "marketplace:jsdotnet-copilot",
        DevToolKind.Marketplace,
        "jsdotnet-copilot",
        "JSdotNet/Copilot",
        ConfiguredEnabled: true,
        Installed: known,
        known ? "configured" : "not installed",
        DevToolOutput.NoVersion,
        known ? "Configured marketplace" : "Not added to Claude yet")
    {
        Hosts = DevToolHosts.Claude
    };

    private static DevToolInfo Tool(bool enabled, bool installed, string installedVersion, string availableVersion) =>
        new(
            "plugin:architecture",
            DevToolKind.Plugin,
            "architecture",
            "JSdotNet/Copilot:plugins/architecture",
            enabled,
            installed,
            installedVersion,
            availableVersion,
            "Configured plugin");

    /// <summary>A port that records what it was asked and answers from a fixed
    /// catalog. A record so a test can vary one field of it without a builder.</summary>
    private sealed record FakeDevToolService : IDevToolService
    {
        public const string RefusalMessage = "The catalog is read-only on this machine.";

        private readonly List<DevToolInfo> _tools = [];

        public bool CatalogExists { get; init; } = true;

        public string CatalogPath { get; init; } = @"C:\tools\ai-tools.json";

        public bool CanEdit { get; init; } = true;

        public bool Succeeds { get; init; } = true;

        /// <summary>What the host would have run. Init-only like the flags beside
        /// it, so a test that does not care about the transcript never mentions
        /// one and the fold stays off its screen.</summary>
        public IReadOnlyList<DevToolCommand> Commands { get; init; } = [];

        public int Reads { get; private set; }

        public int Creates { get; private set; }

        public List<DevToolDraft> Added { get; } = [];

        public List<string> Removed { get; } = [];

        /// <summary>Every acknowledgement the pane asked for, with the state it
        /// asked for. The state matters as much as the key: the defect this
        /// method exists for was a tick that could only ever go one way.</summary>
        public List<(string Key, bool Acknowledged)> Acknowledgements { get; } = [];

        public List<string> Imported { get; } = [];

        public static FakeDevToolService With(params DevToolInfo[] tools)
        {
            var service = new FakeDevToolService();
            service._tools.AddRange(tools);
            return service;
        }

        public static FakeDevToolService WithoutCatalog(string catalogPath = @"C:\tools\ai-tools.json") =>
            new() { CatalogExists = false, CatalogPath = catalogPath };

        /// <summary>Held open by a test that wants to look at the pane mid-read.
        /// The real port walks the machine and takes seconds over it; every other
        /// test here wants that over before the first render, so this is null
        /// unless a test says otherwise.</summary>
        public TaskCompletionSource? Reading { get; set; }

        public async Task<DevToolCatalog> ListAsync(CancellationToken ct = default)
        {
            Reads++;

            if (Reading is not null)
            {
                await Reading.Task;
            }

            return new DevToolCatalog(
                CatalogExists ? _tools : [],
                CatalogExists ? "Showing tools." : $"Tool catalog was not found at {CatalogPath}.",
                CatalogExists,
                CatalogPath,
                CanEdit)
            {
                Commands = Commands
            };
        }

        public Task<DevToolActionResult> UpdateAsync(string key, CancellationToken ct = default) => Answer();

        public Task<DevToolActionResult> UpdateAllAsync(CancellationToken ct = default) => Answer();

        public Task<DevToolActionResult> EnableAsync(string key, CancellationToken ct = default) => Answer();

        public Task<DevToolActionResult> DisableAsync(string key, CancellationToken ct = default) => Answer();

        public Task<DevToolActionResult> AcknowledgeAsync(string key, bool acknowledged, CancellationToken ct = default)
        {
            Acknowledgements.Add((key, acknowledged));
            return Answer();
        }

        public Task<DevToolActionResult> CreateCatalogAsync(CancellationToken ct = default)
        {
            Creates++;
            return Answer();
        }

        public Task<DevToolActionResult> AddAsync(DevToolDraft draft, CancellationToken ct = default)
        {
            Added.Add(draft);
            return Answer();
        }

        public Task<DevToolActionResult> RemoveAsync(string key, CancellationToken ct = default)
        {
            Removed.Add(key);
            return Answer();
        }

        public Task<DevToolActionResult> ImportAsync(string json, CancellationToken ct = default)
        {
            Imported.Add(json);
            return Answer();
        }

        private Task<DevToolActionResult> Answer() => Task.FromResult(
            Succeeds ? DevToolActionResult.Ok("Done.") : DevToolActionResult.Failed(RefusalMessage));
    }
}
