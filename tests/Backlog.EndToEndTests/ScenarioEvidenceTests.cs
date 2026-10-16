using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Backlog.EndToEndTests;

/// <summary>
/// A repository's scenario evidence where a person reads it: the dot on the page in
/// the Devbook tree and the legend under it, a task's acceptance checklist over the
/// page's parts, and a delivery run's Verify stage with the parts it ran and the
/// screenshots it took.
///
/// <para>Each test registers <c>tests/Fixtures/scenario-repository</c> through
/// Settings › Repositories and removes it again (<see cref="ScenarioRepository"/>).
/// The task and the run are put in through the harness's own MCP endpoint — the way
/// an agent files them — and taken out again: the task through its Delete, the run
/// by deleting the folder its worktree's runs were filed in, which is this test's
/// own temporary folder and nobody else's.</para>
/// </summary>
[Collection(ScenarioEvidenceCollection.Name)]
public sealed partial class ScenarioEvidenceTests
{
    private static string PassedReference => $"{ScenarioRepository.PagePath}#{ScenarioRepository.PassedPart}";

    private static string FailedReference => $"{ScenarioRepository.PagePath}#{ScenarioRepository.FailedPart}";

    /// <summary>
    /// The scenario page in the Domain tree carries its run's dot — failed, since its
    /// second part failed on the page as it stands — and the legend under the tree
    /// names all four states.
    /// </summary>
    [Fact(Timeout = 10 * 60 * 1000)]
    public async Task The_tree_marks_the_scenario_page_and_the_legend_names_the_four_states()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var scenario = await ScenarioRepository.OpenAsync("tree", cancellationToken);
        var desktop = scenario.Desktop;

        await scenario.ScopeToRepositoryAsync();
        await desktop.ShowDevbookAsync();
        await desktop.OpenAreaAsync("Domain");

        var page = desktop.DevbookMenuFile("place an order");
        await Expect(page.GetByTestId("devbook-scenario-dot")).ToHaveAttributeAsync("data-state", "failed");
        await Expect(page.GetByTestId("devbook-scenario-dot")).ToHaveAttributeAsync("aria-label", "Scenario failed");

        await Expect(desktop.ScenarioLegend).ToBeVisibleAsync();
        Assert.Equal(
            ["Passed", "Failed", "Stale", "Never run"],
            (await desktop.ScenarioLegend.GetByTestId("devbook-scenario-legend-item").AllInnerTextsAsync()).Select(text => text.Trim()));
        Assert.Equal(
            ["passed", "failed", "stale", "never-run"],
            await desktop.ScenarioLegend.Locator(".devbook-scenario-dot").EvaluateAllAsync<string[]>("dots => dots.map(d => d.dataset.state)"));
        await scenario.Evidence.ScreenshotAsync(desktop.Page, "tree-dot-and-legend");
    }

    /// <summary>
    /// A task referencing both parts lists them as its acceptance checklist: one of
    /// two passing and not proved, each part's line naming the page's stem and its
    /// run, the failed part the step it stopped on. The MCP read of the same task
    /// says the same, through the real resolver.
    /// </summary>
    [Fact(Timeout = 10 * 60 * 1000)]
    public async Task A_task_referencing_the_parts_shows_the_acceptance_checklist()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var scenario = await ScenarioRepository.OpenAsync("checklist", cancellationToken);
        var desktop = scenario.Desktop;
        var title = $"Place an order {scenario.Token}";

        try
        {
            using (var mcp = await HarnessMcp.ConnectAsync(scenario.AppHost, cancellationToken))
            {
                var created = await mcp.CallAsync("create_item", new JsonObject
                {
                    ["rawText"] = $"# {title}\n`task`\n\nAccepted when the order can be placed.\n",
                    ["repository"] = ScenarioRepository.FullName
                }, cancellationToken);
                var id = created["id"]!.GetValue<string>();

                var set = await mcp.CallAsync("set_devbook_references", new JsonObject
                {
                    ["id"] = id,
                    ["repository"] = ScenarioRepository.FullName,
                    ["references"] = new JsonArray(PassedReference, FailedReference)
                }, cancellationToken);
                TestContext.Current.SendDiagnosticMessage($"set_devbook_references: {set.ToJsonString()}");

                var parts = set["references"]!.AsArray().SelectMany(reference => reference!["parts"]?.AsArray() ?? []).ToList();
                Assert.Equal(["passed", "failed"], parts.Select(part => part!["state"]!.GetValue<string>()));
                Assert.Equal(1, parts[1]!["failedStep"]!.GetValue<int>());
                Assert.Equal("1 of 2 passing", set["acceptance"]!["summary"]!.GetValue<string>());
            }

            await scenario.ScopeToRepositoryAsync();
            await desktop.CloseDevbookAsync();
            await scenario.Evidence.ScreenshotAsync(desktop.Page, "tasks-scoped-to-the-repository");
            await desktop.OpenTaskAsync(title);

            var checklist = desktop.AcceptanceChecklist;
            await Expect(checklist).ToHaveAttributeAsync("aria-label", "Acceptance · scenario parts");
            await Expect(checklist.GetByTestId("entry-acceptance-count")).ToHaveTextAsync("1 of 2 passing · not proved");

            var rows = checklist.GetByTestId("entry-acceptance-part");
            await Expect(rows).ToHaveCountAsync(2);
            await Expect(rows.Nth(0)).ToHaveAttributeAsync("data-state", "passed");
            await Expect(rows.Nth(1)).ToHaveAttributeAsync("data-state", "failed");
            await Expect(rows.Nth(0).Locator(".scenario-checklist__run")).ToHaveTextAsync(StemAndDate());
            await Expect(rows.Nth(1).Locator(".scenario-checklist__run")).ToHaveTextAsync(new Regex($"{StemAndDate()} · step 1$"));
            await scenario.Evidence.ScreenshotAsync(desktop.Page, "acceptance-checklist");
        }
        finally
        {
            await DeleteTestTasksAsync(scenario);
        }
    }

    /// <summary>
    /// A delivery run whose Verify stage carries the page's parts as evidence and the
    /// scenario with its screenshots: the stage's row counts the parts, and its panel
    /// lists them, the scenario with its status, each screenshot by file name, and the
    /// failed part's own shot marked as the failure screenshot.
    /// </summary>
    [Fact(Timeout = 10 * 60 * 1000)]
    public async Task A_verify_stage_shows_its_scenario_evidence_and_screenshots()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var scenario = await ScenarioRepository.OpenAsync("run", cancellationToken);
        var desktop = scenario.Desktop;
        var title = $"Scenario evidence {scenario.Token}";
        var runFolder = Path.Combine(scenario.Folder, ScenarioRepositoryRunFolder);
        string? viewBefore = null;

        // The surface tools answer while Sessions is on; kept per worktree.
        await desktop.EnableFeatureAsync(SessionsFeature());

        try
        {
            string runId;
            using (var mcp = await HarnessMcp.ConnectAsync(scenario.AppHost, cancellationToken))
            {
                var started = await mcp.CallAsync("start_run", new JsonObject
                {
                    ["worktree"] = scenario.Folder,
                    ["skillId"] = "flow-code",
                    ["title"] = title,
                    ["stages"] = new JsonArray("Scope", "Implement", "Verify"),
                    ["repo"] = ScenarioRepository.FullName
                }, cancellationToken);
                runId = started["runId"]!.GetValue<string>();

                await mcp.CallAsync("update_stage", new JsonObject
                {
                    ["worktree"] = scenario.Folder,
                    ["runId"] = runId,
                    ["stageIndex"] = 2,
                    ["status"] = "done",
                    ["evidence"] = new JsonArray(
                        new JsonObject { ["stem"] = ScenarioRepository.Stem, ["part"] = ScenarioRepository.PassedPart, ["outcome"] = "passed", ["runAt"] = "2026-10-14T09:30:00Z" },
                        new JsonObject { ["stem"] = ScenarioRepository.Stem, ["part"] = ScenarioRepository.FailedPart, ["outcome"] = "failed", ["runAt"] = "2026-10-14T09:30:00Z" }),
                    ["scenarios"] = new JsonArray(new JsonObject
                    {
                        ["name"] = ScenarioRepository.Stem,
                        ["status"] = "fail",
                        ["notes"] = "The order is placed stopped on step 1",
                        ["evidence"] = new JsonArray(
                            $"{runFolder}/cart-filled.png",
                            $"{runFolder}/fail.{ScenarioRepository.FailedPart}.png")
                    })
                }, cancellationToken);
            }

            await scenario.ScopeToRepositoryAsync();
            await desktop.ShowSessionsAsync();

            // The run has no session, so its row is titled by its worktree folder,
            // and only Show All lists a finished row of that kind. The choice is
            // the pane's to remember, so it is put back afterwards.
            viewBefore = await desktop.ChooseSessionsViewAsync("all");
            var verify = await desktop.OpenRunStageAsync(Path.GetFileName(scenario.Folder), runId, title, "Verify");

            await Expect(verify.GetByTestId("sessions-stage-evidence-summary")).ToHaveTextAsync("1 of 2 passing");
            var parts = verify.GetByTestId("sessions-stage-evidence-part");
            await Expect(parts).ToHaveCountAsync(2);
            await Expect(parts.Nth(1)).ToHaveAttributeAsync("data-state", "failed");

            var screenshots = verify.GetByTestId("sessions-stage-screenshots");
            await Expect(screenshots.GetByTestId("sessions-stage-scenario-status")).ToHaveTextAsync("Failed");
            var files = screenshots.GetByTestId("sessions-stage-screenshot");
            await Expect(files).ToHaveCountAsync(2);
            await Expect(files.Nth(0).Locator(".sessions-stage__screenshot-file")).ToHaveTextAsync("cart-filled.png");
            await Expect(files.Nth(1).Locator(".sessions-stage__screenshot-file")).ToHaveTextAsync($"fail.{ScenarioRepository.FailedPart}.png");
            await Expect(files.Nth(1).GetByTestId("sessions-stage-failure-shot")).ToHaveTextAsync("Failure screenshot");
            await Expect(files.Nth(0).GetByTestId("sessions-stage-failure-shot")).ToHaveCountAsync(0);
            await scenario.Evidence.ScreenshotAsync(desktop.Page, "verify-stage-evidence");
        }
        finally
        {
            if (viewBefore is not null)
            {
                try
                {
                    await desktop.ChooseSessionsViewAsync(viewBefore);
                }
                catch (Exception exception) when (exception is PlaywrightException or TimeoutException)
                {
                    TestContext.Current.SendDiagnosticMessage($"Could not put Sessions' Show back to {viewBefore}: {exception.Message}");
                }
            }

            DeleteRunsOf(scenario.Folder);
        }
    }

    /// <summary>Where the fixture's run keeps its screenshots, relative to the
    /// repository: the paths a verify stage sends as its scenario's evidence.</summary>
    private const string ScenarioRepositoryRunFolder = ".devbook/scenarios/place-an-order";

    /// <summary>"place-an-order · " and the run's date, in whatever zone the harness
    /// draws local time in.</summary>
    private static Regex StemAndDate() => new($"^{ScenarioRepository.Stem} · \\d{{1,2}} [A-Z][a-z]{{2}} 2026 \\d{{2}}:\\d{{2}}");

    /// <summary>
    /// Deletes this test's tasks through their own Delete: every row titled
    /// <c>Place an order &lt;token&gt;</c> under the test repository's scope, so a
    /// task an earlier, crashed run left behind goes too. The repository is the
    /// test's own alias, so no person's task can match.
    /// </summary>
    private static async Task DeleteTestTasksAsync(ScenarioRepository scenario)
    {
        var desktop = scenario.Desktop;
        var ours = desktop.Page.Locator("[data-task-id]").Filter(new LocatorFilterOptions { HasTextRegex = TestTaskTitle() });

        try
        {
            await scenario.ScopeToRepositoryAsync();
            await desktop.CloseDevbookAsync();

            for (var left = await ours.CountAsync(); left > 0; left = await ours.CountAsync())
            {
                var title = TestTaskTitle().Match(await ours.First.InnerTextAsync()).Value;
                await desktop.OpenTaskAsync(title);
                await Interactive.RepeatAsync(
                    async () =>
                    {
                        var delete = desktop.Page.GetByTestId("delete-entry-button");
                        if (await delete.CountAsync() > 0) await delete.First.ClickAsync();
                    },
                    async () => await desktop.TaskRow(title).CountAsync() == 0,
                    $"deleting {title}");
                TestContext.Current.SendDiagnosticMessage($"deleted the task '{title}'");
            }
        }
        catch (Exception exception) when (exception is PlaywrightException or TimeoutException)
        {
            TestContext.Current.SendDiagnosticMessage(
                $"Could not delete this test's tasks: {exception.Message}. Delete every 'Place an order <token>' task filed against {ScenarioRepository.Alias} by hand.");
        }
    }

    [GeneratedRegex(@"Place an order \d{4}-\d{6}")]
    private static partial Regex TestTaskTitle();

    /// <summary>
    /// Deletes every run folder the surface filed for this worktree. Runs are filed
    /// under <c>~/.claude/backlog/&lt;leaf&gt;-&lt;hash&gt;/</c>, the leaf being the
    /// worktree folder's name — this test's own temporary folder, a name no other
    /// worktree has — so only folders starting with it are touched.
    /// </summary>
    private static void DeleteRunsOf(string worktree)
    {
        var dashboard = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "backlog");
        if (!Directory.Exists(dashboard)) return;

        var leaf = Path.GetFileName(worktree.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        foreach (var folder in Directory.EnumerateDirectories(dashboard, $"{leaf}*"))
        {
            try
            {
                Directory.Delete(folder, recursive: true);
                TestContext.Current.SendDiagnosticMessage($"removed the run folder {folder}");
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                TestContext.Current.SendDiagnosticMessage($"Could not remove the run folder {folder}: {exception.Message}. Delete it by hand.");
            }
        }
    }

    [GeneratedRegex(@"^Sessions\b")]
    private static partial Regex SessionsFeature();
}

/// <summary>The scenario-evidence tests register a repository in the shared registry
/// and set the shared scope, so they run one at a time and never beside another
/// test class.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ScenarioEvidenceCollection
{
    public const string Name = "Scenario evidence on the desktop harness";
}
