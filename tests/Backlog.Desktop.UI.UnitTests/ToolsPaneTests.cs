using Backlog.Modules.DevPc.UI;
using Bunit;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// What the system tools pane offers for a row, and the transcript of what the
/// host ran to answer the check.
///
/// <para>The log exists because the desktop head used to show that output the
/// worst possible way — a console window per child process, flashing over the app
/// and gone before anyone could read it. Suppressing the windows without putting
/// the output somewhere would have deleted the only place a refused install could
/// be read, so those tests are the other half of that fix.</para>
///
/// <para>The action tests are about a different silence: the cell had one button
/// and one note, so every row it could not offer an update for was announced as
/// up to date — including the ones the machine did not have and the ones nothing
/// had managed to look up.</para>
/// </summary>
public class ToolsPaneTests
{
    [Fact]
    public void The_commands_the_host_ran_are_on_screen_behind_a_fold()
    {
        using var context = Context(new CopilotToolCatalog([], "Checked.")
        {
            Commands =
            [
                new("copilot --version", 0, "GitHub Copilot CLI 1.2.3"),
                new("dotnet tool search JSdotNet.MCP.Guidelines --exact-match", 1, "No packages found.")
            ]
        });

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
        using var context = Context(new CopilotToolCatalog([], "Checked."));

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

    /// <summary>The one row's action cell, as text. The pane is rendered with a
    /// single tool so the cell is unambiguous without reaching for an index.</summary>
    private static string ActionsFor(CopilotToolInfo tool)
    {
        using var context = Context(new CopilotToolCatalog([tool], "Checked."));

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

    private static BunitContext Context(CopilotToolCatalog catalog)
    {
        var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Services.AddSingleton<ICopilotToolService>(new StubCopilotToolService(catalog));

        return context;
    }

    /// <summary>Answers with the catalog it was given and refuses everything
    /// else. The pane under test only reads here, so a stub that could act would
    /// be a stub with behaviour nobody exercises.</summary>
    private sealed class StubCopilotToolService(CopilotToolCatalog catalog) : ICopilotToolService
    {
        private const string Unsupported = "This stub performs no tool actions.";

        public Task<CopilotToolCatalog> ListAsync(CancellationToken ct = default) => Task.FromResult(catalog);

        public Task<CopilotToolActionResult> UpdateAsync(string key, CancellationToken ct = default) =>
            Task.FromResult(CopilotToolActionResult.Failed(Unsupported));

        public Task<CopilotToolActionResult> UpdateAllAsync(CancellationToken ct = default) =>
            Task.FromResult(CopilotToolActionResult.Failed(Unsupported));

        public Task<CopilotToolActionResult> EnableAsync(string key, CancellationToken ct = default) =>
            Task.FromResult(CopilotToolActionResult.Failed(Unsupported));

        public Task<CopilotToolActionResult> DisableAsync(string key, CancellationToken ct = default) =>
            Task.FromResult(CopilotToolActionResult.Failed(Unsupported));
    }
}
