using Backlog.Modules.DevPc.UI;
using Bunit;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The system tools pane's command log: the transcript of what the host ran to
/// answer the check.
///
/// <para>It exists because the desktop head used to show that output the worst
/// possible way — a console window per child process, flashing over the app and
/// gone before anyone could read it. Suppressing the windows without putting the
/// output somewhere would have deleted the only place a refused install could be
/// read, so these tests are the other half of that fix.</para>
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
