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
/// decided entirely by the <see cref="CopilotToolCatalog"/> it is handed, and going
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
        using var context = Context(FakeCopilotToolService.WithoutCatalog(@"C:\tools\copilot-tools.json"));

        var pane = context.Render<ToolsPane>();

        var empty = pane.Find("[data-testid='tools-empty-no-catalog']");
        Assert.Contains(@"C:\tools\copilot-tools.json", empty.TextContent, StringComparison.Ordinal);
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
        using var context = Context(FakeCopilotToolService.With());

        var pane = context.Render<ToolsPane>();

        Assert.NotNull(pane.Find("[data-testid='tools-empty-no-entries']"));
        Assert.NotNull(pane.Find("[data-testid='tools-add-open']"));
        Assert.Empty(pane.FindAll("[data-testid='tools-empty-no-catalog']"));
        Assert.Empty(pane.FindAll("[data-testid='tools-create-catalog']"));
    }

    [Fact]
    public void A_populated_catalog_draws_the_table_and_a_remove_on_every_row()
    {
        using var context = Context(FakeCopilotToolService.With(Tool("plugin:architecture", "architecture"), Tool("mcp:Guidelines", "guidelines")));

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
        var service = FakeCopilotToolService.With(Tool("plugin:architecture", "architecture")) with { CanEdit = false };
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
        var service = FakeCopilotToolService.WithoutCatalog();
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
        var service = FakeCopilotToolService.WithoutCatalog() with { Succeeds = false };
        using var context = Context(service);

        var pane = context.Render<ToolsPane>();
        var readsBefore = service.Reads;

        pane.Find("[data-testid='tools-create-catalog']").Click();

        Assert.Equal(1, service.Creates);

        // A refresh here would replace the refusal with the ordinary "showing
        // tools from ..." line, which is the one moment it is worth less.
        Assert.Equal(readsBefore, service.Reads);
        Assert.Contains(FakeCopilotToolService.RefusalMessage, pane.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Adding_a_plugin_sends_the_draft_the_form_was_filled_with()
    {
        var service = FakeCopilotToolService.With();
        using var context = Context(service);

        var pane = context.Render<ToolsPane>();
        pane.Find("[data-testid='tools-add-open']").Click();

        pane.Find("[data-testid='tools-add-name'] input").Input("architecture");
        pane.Find("[data-testid='tools-add-source'] input").Input("JSdotNet/Copilot:plugins/architecture");
        pane.Find("[data-testid='tools-add-submit']").Click();

        var draft = Assert.Single(service.Added);
        Assert.Equal(CopilotToolKind.Plugin, draft.Kind);
        Assert.Equal("architecture", draft.Id);
        Assert.Equal("JSdotNet/Copilot:plugins/architecture", draft.Source);
        Assert.Empty(pane.FindAll("[data-testid='tools-add-dialog']"));
    }

    [Fact]
    public void A_plugin_with_no_source_never_reaches_the_port()
    {
        var service = FakeCopilotToolService.With();
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
        var service = FakeCopilotToolService.With();
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
        var service = FakeCopilotToolService.With(Tool("plugin:architecture", "architecture"));
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
        var service = FakeCopilotToolService.With(Tool("plugin:architecture", "architecture"));
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
        var service = FakeCopilotToolService.With() with
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
        using var context = Context(FakeCopilotToolService.With());

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

    private static BunitContext Context(ICopilotToolService service)
    {
        var context = new BunitContext();

        // The modal moves focus into itself when it opens, which is a JS call.
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Services.AddSingleton(service);
        return context;
    }

    private static CopilotToolInfo Tool(string key, string name) => new(
        key,
        key.StartsWith("plugin:", StringComparison.Ordinal) ? CopilotToolKind.Plugin : CopilotToolKind.McpServer,
        name,
        "source",
        ConfiguredEnabled: true,
        Installed: true,
        "1.0.0",
        "1.0.0",
        "Enabled plugin");

    /// <summary>The one row's action cell, as text. The pane is rendered with a
    /// single tool so the cell is unambiguous without reaching for an index.</summary>
    private static string ActionsFor(CopilotToolInfo tool)
    {
        using var context = Context(FakeCopilotToolService.With(tool));

        return context.Render<ToolsPane>().Find(".tools-table__actions").TextContent;
    }

    private static CopilotToolInfo Tool(bool enabled, bool installed, string installedVersion, string availableVersion) =>
        new(
            "plugin:architecture",
            CopilotToolKind.Plugin,
            "architecture",
            "JSdotNet/Copilot:plugins/architecture",
            enabled,
            installed,
            installedVersion,
            availableVersion,
            "Configured plugin");

    /// <summary>A port that records what it was asked and answers from a fixed
    /// catalog. A record so a test can vary one field of it without a builder.</summary>
    private sealed record FakeCopilotToolService : ICopilotToolService
    {
        public const string RefusalMessage = "The catalog is read-only on this machine.";

        private readonly List<CopilotToolInfo> _tools = [];

        public bool CatalogExists { get; init; } = true;

        public string CatalogPath { get; init; } = @"C:\tools\copilot-tools.json";

        public bool CanEdit { get; init; } = true;

        public bool Succeeds { get; init; } = true;

        /// <summary>What the host would have run. Init-only like the flags beside
        /// it, so a test that does not care about the transcript never mentions
        /// one and the fold stays off its screen.</summary>
        public IReadOnlyList<CopilotToolCommand> Commands { get; init; } = [];

        public int Reads { get; private set; }

        public int Creates { get; private set; }

        public List<CopilotToolDraft> Added { get; } = [];

        public List<string> Removed { get; } = [];

        public List<string> Imported { get; } = [];

        public static FakeCopilotToolService With(params CopilotToolInfo[] tools)
        {
            var service = new FakeCopilotToolService();
            service._tools.AddRange(tools);
            return service;
        }

        public static FakeCopilotToolService WithoutCatalog(string catalogPath = @"C:\tools\copilot-tools.json") =>
            new() { CatalogExists = false, CatalogPath = catalogPath };

        public Task<CopilotToolCatalog> ListAsync(CancellationToken ct = default)
        {
            Reads++;
            return Task.FromResult(new CopilotToolCatalog(
                CatalogExists ? _tools : [],
                CatalogExists ? "Showing tools." : $"Tool catalog was not found at {CatalogPath}.",
                CatalogExists,
                CatalogPath,
                CanEdit)
            {
                Commands = Commands
            });
        }

        public Task<CopilotToolActionResult> UpdateAsync(string key, CancellationToken ct = default) => Answer();

        public Task<CopilotToolActionResult> UpdateAllAsync(CancellationToken ct = default) => Answer();

        public Task<CopilotToolActionResult> EnableAsync(string key, CancellationToken ct = default) => Answer();

        public Task<CopilotToolActionResult> DisableAsync(string key, CancellationToken ct = default) => Answer();

        public Task<CopilotToolActionResult> CreateCatalogAsync(CancellationToken ct = default)
        {
            Creates++;
            return Answer();
        }

        public Task<CopilotToolActionResult> AddAsync(CopilotToolDraft draft, CancellationToken ct = default)
        {
            Added.Add(draft);
            return Answer();
        }

        public Task<CopilotToolActionResult> RemoveAsync(string key, CancellationToken ct = default)
        {
            Removed.Add(key);
            return Answer();
        }

        public Task<CopilotToolActionResult> ImportAsync(string json, CancellationToken ct = default)
        {
            Imported.Add(json);
            return Answer();
        }

        private Task<CopilotToolActionResult> Answer() => Task.FromResult(
            Succeeds ? CopilotToolActionResult.Ok("Done.") : CopilotToolActionResult.Failed(RefusalMessage));
    }
}
