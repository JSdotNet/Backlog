using Backlog.Modules.DevPc.UI;
using Bunit;
using static Backlog.Desktop.UI.UnitTests.ToolsPaneTests;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The pane's run and empty states (plan +system-tools-redesign, step 4): the header,
/// progress bar and "This run" queue while an Update all is going, the live transcript
/// beside it, a failed step returning to its row, the two empty states with and
/// without the right to edit the catalog, and the loading placeholder in the tab
/// layout.
/// </summary>
public sealed class ToolsPaneRunStatesTests
{
    [Fact]
    public void A_run_in_progress_takes_over_the_header_and_disables_both_acts()
    {
        var gate = new TaskCompletionSource<DevToolActionResult>();
        using var context = Context(ThreeBehind(progress =>
        {
            progress?.Report(Snapshot(Done("plugin:a"), Running("plugin:b"), Queued("plugin:c")));
            return gate.Task;
        }));
        var pane = context.Render<ToolsPane>();

        pane.Find("[data-testid='tools-update-all']").Click();

        pane.WaitForAssertion(() => Assert.Equal("System tools · Updating", pane.Find(".tools-panel__eyebrow").TextContent.Trim()));
        Assert.Equal("Updating 3 tools — 1 done, 1 running", pane.Find("#installed-tools-title").TextContent.Trim());

        var bar = pane.Find("[data-testid='tools-run-progress']");
        Assert.Equal("progressbar", bar.GetAttribute("role"));
        Assert.Equal("0", bar.GetAttribute("aria-valuemin"));
        Assert.Equal("3", bar.GetAttribute("aria-valuemax"));
        Assert.Equal("1", bar.GetAttribute("aria-valuenow"));

        foreach (var act in new[] { "tools-refresh", "tools-update-all" })
        {
            var button = pane.Find($"[data-testid='{act}']");
            Assert.True(button.HasAttribute("disabled"));
            Assert.Equal("Updating…", button.TextContent.Trim());
        }

        gate.SetResult(DevToolActionResult.Ok("Updated 3 tool(s)."));
        pane.WaitForAssertion(() => Assert.Empty(pane.FindAll("[data-testid='tools-run-progress']")));
        // The eyebrow returns to the active tab, the Overview the pane opens on.
        Assert.Equal("System tools · Overview", pane.Find(".tools-panel__eyebrow").TextContent.Trim());
    }

    [Fact]
    public void The_queue_names_every_step_with_its_state_versions_and_time()
    {
        var gate = new TaskCompletionSource<DevToolActionResult>();
        using var context = Context(ThreeBehind(progress =>
        {
            progress?.Report(Snapshot(
                Done("plugin:a") with { Duration = TimeSpan.FromSeconds(12) },
                Failed("plugin:b", "exit code 1") with { Duration = TimeSpan.FromSeconds(75) },
                Running("plugin:c")));
            return gate.Task;
        }));
        var pane = context.Render<ToolsPane>();

        pane.Find("[data-testid='tools-update-all']").Click();

        pane.WaitForAssertion(() => Assert.NotNull(pane.Find("[data-testid='tools-run-queue']")));
        Assert.Contains("This run", pane.Find("[data-testid='tools-run-queue']").TextContent, StringComparison.Ordinal);

        var steps = pane.FindAll("[data-testid='tools-run-step']");
        Assert.Equal(["Done", "Failed", "Running"], steps.Select(step => step.GetAttribute("data-state")));
        Assert.Equal(
            ["Done", "Failed", "Running"],
            steps.Select(step => step.QuerySelector("[data-testid='tools-run-step-state']")!.TextContent.Trim()));
        Assert.Equal("1.0.0 → 1.1.0", steps[0].QuerySelector("[data-testid='tools-run-step-versions']")!.TextContent.Trim());
        Assert.Equal("12s", steps[0].QuerySelector("[data-testid='tools-run-step-duration']")!.TextContent.Trim());
        Assert.Equal("1m 15s", steps[1].QuerySelector("[data-testid='tools-run-step-duration']")!.TextContent.Trim());
        Assert.Equal("exit code 1", steps[1].QuerySelector("[data-testid='tools-run-step-error']")!.TextContent.Trim());
        Assert.Equal(string.Empty, steps[2].QuerySelector("[data-testid='tools-run-step-duration']")!.TextContent.Trim());

        // The headline counts the failure too, so it is never only in the queue.
        Assert.Equal("Updating 3 tools — 1 done, 1 running, 1 failed", pane.Find("#installed-tools-title").TextContent.Trim());

        // The inventory stays on screen under the queue.
        Assert.Equal(3, pane.FindAll("[data-tool-key]").Count);

        gate.SetResult(DevToolActionResult.Ok("Updated."));
    }

    /// <summary>Before the port's first report the pane draws the queue it reads off
    /// its own rows, every step queued, so the running state never opens empty.</summary>
    [Fact]
    public void Before_the_first_report_every_step_the_run_will_take_is_queued()
    {
        var gate = new TaskCompletionSource<DevToolActionResult>();
        using var context = Context(ThreeBehind(_ => gate.Task));
        var pane = context.Render<ToolsPane>();

        pane.Find("[data-testid='tools-update-all']").Click();

        pane.WaitForAssertion(() => Assert.Equal(3, pane.FindAll("[data-testid='tools-run-step']").Count));
        Assert.All(pane.FindAll("[data-testid='tools-run-step']"), step => Assert.Equal("Queued", step.GetAttribute("data-state")));
        Assert.Equal("Updating 3 tools — 0 done, 0 running", pane.Find("#installed-tools-title").TextContent.Trim());
        Assert.Contains("Waiting for the first command", pane.Find("[data-testid='tools-run-transcript-log']").TextContent, StringComparison.Ordinal);

        gate.SetResult(DevToolActionResult.Ok("Updated."));
    }

    [Fact]
    public void The_live_transcript_is_polite_copyable_and_wraps_on_request()
    {
        var gate = new TaskCompletionSource<DevToolActionResult>();
        using var context = Context(ThreeBehind(progress =>
        {
            progress?.Report(Snapshot([new DevToolCommand("claude plugin update a", 0, "updated a")], Running("plugin:a"), Queued("plugin:b"), Queued("plugin:c")));
            return gate.Task;
        }));
        var pane = context.Render<ToolsPane>();

        pane.Find("[data-testid='tools-update-all']").Click();

        pane.WaitForAssertion(() => Assert.NotNull(pane.Find("[data-testid='tools-run-transcript']")));
        var log = pane.Find("[data-testid='tools-run-transcript-log']");
        Assert.Equal("polite", log.GetAttribute("aria-live"));
        Assert.Contains("$ claude plugin update a", log.TextContent, StringComparison.Ordinal);
        Assert.NotNull(pane.Find("[data-testid='tools-run-transcript-copy']"));

        // The folded record of the last run is not drawn beside the live one.
        Assert.Empty(pane.FindAll("[data-testid='tools-command-log']"));

        var wrap = pane.Find("[data-testid='tools-run-transcript-wrap']");
        Assert.Equal("true", wrap.GetAttribute("aria-pressed"));
        Assert.DoesNotContain("code-view--nowrap", pane.Find("[data-testid='tools-run-transcript-output']").ClassName, StringComparison.Ordinal);

        wrap.Click();

        Assert.Equal("false", pane.Find("[data-testid='tools-run-transcript-wrap']").GetAttribute("aria-pressed"));
        Assert.Contains("code-view--nowrap", pane.Find("[data-testid='tools-run-transcript-output']").ClassName, StringComparison.Ordinal);

        gate.SetResult(DevToolActionResult.Ok("Updated."));
    }

    /// <summary>A step that did not get there goes back to its row: the row is still
    /// outstanding, says why under it, and the transcript of the run stays open.</summary>
    [Fact]
    public void A_failed_step_returns_to_its_row_with_the_error_and_keeps_its_transcript()
    {
        var commands = new[] { new DevToolCommand("claude plugin update b", 1, "network unreachable") };
        var tools = ThreeBehind(progress =>
        {
            progress?.Report(Snapshot(commands, Done("plugin:a"), Failed("plugin:b", "network unreachable"), Done("plugin:c")));
            return Task.FromResult(DevToolActionResult.Failed("Updated 2 tool(s); 1 failed.", commands));
        });
        using var context = Context(tools);
        var pane = context.Render<ToolsPane>();

        pane.Find("[data-testid='tools-update-all']").Click();

        pane.WaitForAssertion(() => Assert.Empty(pane.FindAll("[data-testid='tools-run-queue']")));

        var failed = pane.Find("[data-tool-key='plugin:b']");
        Assert.Equal(
            "Update all failed here: network unreachable",
            failed.QuerySelector("[data-testid='tools-row-run-error']")!.TextContent.Trim());
        Assert.Equal("Update", failed.QuerySelector("[data-testid='tools-row-status']")!.GetAttribute("data-state"));
        Assert.Empty(pane.Find("[data-tool-key='plugin:a']").QuerySelectorAll("[data-testid='tools-row-run-error']"));

        // The row counts as needing action again, on its tab's badge.
        Assert.Equal("3", pane.Find("[data-testid='tools-tab-plugins-count']").TextContent.Trim());

        // A partly failed run still re-reads, and its own message and transcript win
        // over the check that followed it.
        Assert.True(tools.Reads >= 2);
        Assert.Contains("1 failed", pane.Find(".tools-panel__message").TextContent, StringComparison.Ordinal);
        Assert.Equal("true", pane.Find("[data-testid='tools-command-log']").GetAttribute("aria-expanded"));
        Assert.Contains("network unreachable", pane.Find("[data-testid='tools-command-log-output']").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void A_report_landing_after_the_run_is_ignored()
    {
        IProgress<DevToolRunProgress>? kept = null;
        using var context = Context(ThreeBehind(progress =>
        {
            kept = progress;
            return Task.FromResult(DevToolActionResult.Ok("Updated."));
        }));
        var pane = context.Render<ToolsPane>();

        pane.Find("[data-testid='tools-update-all']").Click();
        pane.WaitForAssertion(() => Assert.Empty(pane.FindAll("[data-testid='tools-run-queue']")));

        kept!.Report(Snapshot(Running("plugin:a")));

        pane.WaitForAssertion(() => Assert.Empty(pane.FindAll("[data-testid='tools-run-progress']")));
        Assert.Empty(pane.FindAll("[data-testid='tools-run-queue']"));
    }

    [Fact]
    public void No_catalog_offers_two_option_cards_and_one_at_a_time()
    {
        using var context = Context(FakeDevToolService.WithoutCatalog());
        var pane = context.Render<ToolsPane>();

        Assert.Equal("No tool catalog on this machine yet", pane.Find("#installed-tools-title").TextContent.Trim());

        var create = pane.Find("[data-testid='tools-start-create']");
        Assert.Contains("Create from this machine", create.TextContent, StringComparison.Ordinal);
        Assert.Contains("Recommended", create.TextContent, StringComparison.Ordinal);
        Assert.Contains("btn--primary", create.QuerySelector("[data-testid='tools-create-catalog']")!.ClassName, StringComparison.Ordinal);

        // The card promises a catalog built from what is here, and says what that is.
        Assert.Contains(
            "starting from the plugins, marketplaces and MCP servers Claude and Copilot already have here",
            create.QuerySelector(".tools-start__option-body")!.TextContent,
            StringComparison.Ordinal);

        var import = pane.Find("[data-testid='tools-start-import']");
        Assert.Contains("Import a team catalog", import.TextContent, StringComparison.Ordinal);
        Assert.NotNull(import.QuerySelector("[data-testid='tools-import-open']"));

        Assert.Contains("add tools one at a time", pane.Find("[data-testid='tools-start-one-at-a-time']").TextContent, StringComparison.Ordinal);
    }

    /// <summary>The add form writes into a catalog, so one at a time creates the
    /// catalog first and then opens the form.</summary>
    [Fact]
    public void One_at_a_time_creates_the_catalog_then_opens_the_add_form()
    {
        var service = FakeDevToolService.WithoutCatalog();
        using var context = Context(service);
        var pane = context.Render<ToolsPane>();

        pane.Find("[data-testid='tools-start-one-at-a-time']").Click();

        Assert.Equal(1, service.Creates);
        pane.WaitForAssertion(() => Assert.NotNull(pane.Find("[data-testid='tools-add-kind']")));
    }

    [Fact]
    public void No_catalog_on_a_read_only_host_offers_no_cards()
    {
        using var context = Context(FakeDevToolService.WithoutCatalog() with { CanEdit = false });
        var pane = context.Render<ToolsPane>();

        Assert.Equal("No tool catalog on this machine yet", pane.Find("#installed-tools-title").TextContent.Trim());
        Assert.NotNull(pane.Find("[data-testid='tools-start-read-only']"));
        Assert.Empty(pane.FindAll("[data-testid='tools-start-create']"));
        Assert.Empty(pane.FindAll("[data-testid='tools-start-import']"));
        Assert.Empty(pane.FindAll("[data-testid='tools-start-one-at-a-time']"));
        Assert.Empty(pane.FindAll("[data-testid='tools-create-catalog']"));
    }

    [Fact]
    public void An_empty_catalog_offers_adding_or_importing_as_cards()
    {
        using var context = Context(FakeDevToolService.With());
        var pane = context.Render<ToolsPane>();

        Assert.Equal("The catalog is empty", pane.Find("#installed-tools-title").TextContent.Trim());
        var empty = pane.Find("[data-testid='tools-empty-no-entries']");
        Assert.NotNull(empty.QuerySelector("[data-testid='tools-start-add'] [data-testid='tools-add-open']"));
        Assert.NotNull(empty.QuerySelector("[data-testid='tools-start-import'] [data-testid='tools-import-open']"));
        Assert.Null(empty.QuerySelector("[data-testid='tools-start-read-only']"));
    }

    [Fact]
    public void An_empty_catalog_on_a_read_only_host_offers_no_cards()
    {
        using var context = Context(FakeDevToolService.With() with { CanEdit = false });
        var pane = context.Render<ToolsPane>();

        var empty = pane.Find("[data-testid='tools-empty-no-entries']");
        Assert.NotNull(empty.QuerySelector("[data-testid='tools-start-read-only']"));
        Assert.Empty(pane.FindAll("[data-testid='tools-start-add']"));
        Assert.Empty(pane.FindAll("[data-testid='tools-add-open']"));
        Assert.Empty(pane.FindAll("[data-testid='tools-import-open']"));
    }

    /// <summary>The placeholder is in the tab layout's shape: a strip of tab-wide
    /// bars over group cards.</summary>
    [Fact]
    public void The_loading_placeholder_has_the_tab_strip_and_group_cards()
    {
        var reading = new TaskCompletionSource();
        using var context = Context(FakeDevToolService.With() with { Reading = reading });
        var pane = context.Render<ToolsPane>();

        var loading = pane.Find("[data-testid='tools-loading']");
        Assert.Equal(6, loading.QuerySelectorAll("[data-testid='tools-loading-tabs'] .skeleton").Length);
        Assert.NotEmpty(loading.QuerySelectorAll(".tools-group .data-table__row"));

        reading.SetResult();
    }

    /// <summary>The button counts by the port's own rule, so a missing application it
    /// can install is one of the steps it promises — the same count the run's headline
    /// then says.</summary>
    [Fact]
    public void Update_all_counts_a_missing_application_the_port_will_install()
    {
        using var context = Context(FakeDevToolService.With(
            Behind("plugin:a"),
            Application("Git.Git", "Git", installed: false, installedVersion: "not installed")));
        var pane = context.Render<ToolsPane>();

        Assert.Equal("Update all (2)", pane.Find("[data-testid='tools-update-all']").TextContent.Trim());
    }

    /// <summary>Three plugins, each one version behind, so Update all takes all three.</summary>
    private static FakeDevToolService ThreeBehind(Func<IProgress<DevToolRunProgress>?, Task<DevToolActionResult>> run) =>
        FakeDevToolService.With(Behind("plugin:a"), Behind("plugin:b"), Behind("plugin:c")) with { UpdateAllRun = run };

    private static DevToolInfo Behind(string key) => Tool(key, key["plugin:".Length..]) with { AvailableVersion = "1.1.0" };

    private static DevToolRunProgress Snapshot(params DevToolRunQueueItem[] items) => new(items, []);

    private static DevToolRunProgress Snapshot(IReadOnlyList<DevToolCommand> commands, params DevToolRunQueueItem[] items) => new(items, commands);

    private static DevToolRunQueueItem Queued(string key) =>
        new(key, key["plugin:".Length..], "1.0.0", "1.1.0", DevToolRunStepState.Queued);

    private static DevToolRunQueueItem Running(string key) => Queued(key) with { State = DevToolRunStepState.Running };

    private static DevToolRunQueueItem Done(string key) =>
        Queued(key) with { State = DevToolRunStepState.Done, Duration = TimeSpan.FromSeconds(3) };

    private static DevToolRunQueueItem Failed(string key, string error) =>
        Queued(key) with { State = DevToolRunStepState.Failed, Duration = TimeSpan.FromSeconds(3), Error = error };
}
