using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using Backlog.Modules.Sessions.UI.Adapters;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// What a dashboard's run file says and what a <see cref="DeliveryRun"/> ends up
/// carrying. The seam worth testing directly, for the reason the session readers
/// are: a stage named from the wrong list, a token bucket read from the wrong
/// level, or a corrupt file taking the folder down with it are all wrong in a way
/// that renders perfectly.
/// <para>
/// Fixture folders under the temp path rather than this machine's own profile,
/// laid out exactly as the two dashboards lay theirs out. The fixture files are
/// trimmed copies of real run files — one from each generation of the orch
/// dashboard and one from the delivery surface — because the shapes differ in ways
/// a file invented for the test would not have thought to differ in.
/// </para>
/// </summary>
public sealed class DeliveryRunReaderTests : IDisposable
{
    private const string Machine = "DEV-TOWER";

    /// <summary>The device id every run read here is stamped with, in the form the
    /// session source writes it, because a run and a session on one machine have to
    /// agree on the machine.</summary>
    private const string MachineId = "6b8e6f0c-1a4f-4a2e-9f4b-6a2c0f5d3a71";

    private readonly string _home = Path.Combine(
        Path.GetTempPath(),
        "backlog-delivery-run-tests",
        Guid.NewGuid().ToString("n"));

    [Fact]
    public async Task A_run_file_is_read_whole()
    {
        GivenRun("orch-dashboard", "tasks-bulk-select-daa039-9e68802b", "run-mtlea9im-xxayjs.json", OrchFeatureRun);

        var run = Assert.Single((await ReadAsync()).Runs);

        Assert.Equal("run-mtlea9im-xxayjs", run.Id);
        Assert.Equal("orch-dashboard", run.Dashboard);
        Assert.Equal("tasks-bulk-select-daa039-9e68802b", run.Worktree);

        // The hash is the dashboard's key, not a name; the slug in front of it is
        // the worktree's leaf, which is what a session row also shows.
        Assert.Equal("tasks-bulk-select-daa039", run.WorktreeName);

        // Stamped with this device, as a session is: a run file found here was
        // written here, and a row that stands on the run alone needs to say where.
        Assert.Equal(MachineId, run.EnvironmentId);
        Assert.Equal(Machine, run.Environment);
        Assert.Equal("orch-feature", run.SkillId);
        Assert.Equal("Tasks bulk-selection mode", run.Title);
        Assert.Equal("done", run.Status);
        Assert.Equal("new-functionality", run.ChangeKind);
        Assert.Equal(new DateTimeOffset(2026, 9, 3, 10, 41, 21, 70, TimeSpan.Zero), run.StartedAt);
        Assert.Equal(new DateTimeOffset(2026, 9, 3, 20, 59, 36, 948, TimeSpan.Zero), run.UpdatedAt);

        // The issue the run was started from, and the pull request it opened —
        // which the file records in two different places.
        Assert.Collection(
            run.References,
            reference =>
            {
                Assert.Equal(DeliveryRunReferenceKind.Issue, reference.Kind);
                Assert.Equal("#211", reference.Label);
                Assert.Equal("Fix foreground-thread leak", reference.Title);
                Assert.Equal("https://github.com/JSdotNet/Backlog/issues/211", reference.Url);
                Assert.Equal("JSdotNet/Backlog", reference.Repository);
            },
            reference =>
            {
                Assert.Equal(DeliveryRunReferenceKind.PullRequest, reference.Kind);
                Assert.Equal("PR #365", reference.Label);

                // A stage link's description is the title it gets; the repository
                // comes off the address, which is the only place it is stated.
                Assert.Equal("Tasks bulk-selection mode", reference.Title);
                Assert.Equal("https://github.com/JSdotNet/Backlog/pull/365", reference.Url);
                Assert.Equal("JSdotNet/Backlog", reference.Repository);
            });
    }

    /// <summary>
    /// A run records the pull request it opened as a link on the stage that opened
    /// it, beside the dashboards and harness URLs it used — which are dead ports on
    /// the next machine. Only a GitHub issue or pull request becomes a reference, and
    /// one item named twice is one reference.
    /// </summary>
    [Fact]
    public async Task Only_github_items_in_the_stage_links_become_references_and_never_twice()
    {
        GivenRun("orch-dashboard", "tasks-bulk-select-daa039-9e68802b", "run.json", LinkedRun);

        var run = Assert.Single((await ReadAsync()).Runs);

        Assert.Collection(
            run.References,
            reference =>
            {
                Assert.Equal(DeliveryRunReferenceKind.Issue, reference.Kind);
                Assert.Equal("#218", reference.Label);
            },
            reference =>
            {
                Assert.Equal(DeliveryRunReferenceKind.PullRequest, reference.Kind);
                Assert.Equal("PR #378", reference.Label);

                // The tracker field's copy won, because its title is the item's own.
                Assert.Equal("Take a position on manual rank", reference.Title);
            });
    }

    /// <summary>
    /// A run that opened a pull request often states its address in its own closing
    /// summary and nowhere else. An address somebody wrote down is a recorded fact,
    /// so it is read — from the summary, which is about what the run delivered, and
    /// not from the stage outputs, which are about everything else.
    /// </summary>
    [Fact]
    public async Task An_address_stated_only_in_the_summary_is_still_a_reference()
    {
        GivenRun("orch-dashboard", "archify-animation-loop-ac8a3c-8e0887e5", "run.json", SummaryOnlyRun);

        var run = Assert.Single((await ReadAsync()).Runs);
        var reference = Assert.Single(run.References);

        Assert.Equal(DeliveryRunReferenceKind.PullRequest, reference.Kind);
        Assert.Equal("PR #330", reference.Label);
        Assert.Equal("https://github.com/JSdotNet/Backlog/pull/330", reference.Url);
        Assert.Equal("JSdotNet/Backlog", reference.Repository);

        // Prose carries no title of its own, and the surface falls back to the label
        // rather than this reader inventing one out of the sentence around it.
        Assert.Null(reference.Title);
    }

    /// <summary>
    /// The Backlog entry a run was started from, read off the plan item marker in its
    /// prompt — the only thing in a run file that names one. No address: a Backlog
    /// entry is in this product rather than on a page.
    /// </summary>
    [Fact]
    public async Task The_plan_item_marker_in_the_prompt_becomes_a_task_reference()
    {
        GivenRun("orch-dashboard", "tasks-bulk-select-daa039-9e68802b", "run.json", PlanItemRun);

        var run = Assert.Single((await ReadAsync()).Runs);
        var reference = Assert.Single(run.References);

        Assert.Equal(DeliveryRunReferenceKind.Task, reference.Kind);
        Assert.Equal("delivery-run-reader", reference.Label);
        Assert.Equal("Plan backlog-mcp-server", reference.Title);
        Assert.Null(reference.Url);
        Assert.Null(reference.Repository);
        Assert.Null(reference.EntryId);
    }

    /// <summary>
    /// The line the app puts on every entry it copies names the entry by its stored id,
    /// and a run started from that paste is a run started from that entry. The title
    /// under the line is what the reference is called, because a Guid is not something
    /// a reader recognises their task by.
    /// </summary>
    [Fact]
    public async Task The_entry_marker_in_the_prompt_becomes_a_task_reference_by_id()
    {
        GivenRun("orch-dashboard", "done-link-9a5a08-9e68802b", "run.json", EntryMarkerRun);

        var run = Assert.Single((await ReadAsync()).Runs);
        var reference = Assert.Single(run.References);

        Assert.Equal(DeliveryRunReferenceKind.Task, reference.Kind);
        Assert.Equal(Guid.Parse("5f0c2a9e-3b1d-4c7a-9e2f-0a1b2c3d4e5f"), reference.EntryId);
        Assert.Equal("Link the done badge to its task", reference.Label);
        Assert.Null(reference.Plan);
        Assert.Null(reference.Url);
    }

    /// <summary>
    /// An imported entry copied out of the app carries both markers — the app's line
    /// on top and the import's in the body — and they name one entry, so the run has
    /// one task reference carrying both ways of finding it.
    /// </summary>
    [Fact]
    public async Task Both_markers_in_the_prompt_are_one_task_reference()
    {
        GivenRun("orch-dashboard", "done-link-9a5a08-9e68802b", "run.json", BothMarkersRun);

        var run = Assert.Single((await ReadAsync()).Runs);
        var reference = Assert.Single(run.References);

        Assert.Equal(Guid.Parse("5f0c2a9e-3b1d-4c7a-9e2f-0a1b2c3d4e5f"), reference.EntryId);
        Assert.Equal("delivery-run-reader", reference.Label);
        Assert.Equal("backlog-mcp-server", reference.Plan);
    }

    [Fact]
    public async Task Stages_carry_their_status_duration_and_done_count()
    {
        GivenRun("orch-dashboard", "tasks-bulk-select-daa039-9e68802b", "run.json", OrchFeatureRun);

        var run = Assert.Single((await ReadAsync()).Runs);

        Assert.Collection(
            run.Stages,
            stage =>
            {
                Assert.Equal("Scope Discovery", stage.Name);
                Assert.Equal("done", stage.Status);
                Assert.Equal(13_974_587, stage.DurationMs);
                Assert.Equal(1, stage.DoneCount);
            },
            stage =>
            {
                Assert.Equal("Implementation", stage.Name);
                Assert.Equal("done", stage.Status);

                // Re-entered three times before it stayed done. The count is the
                // dashboard's, carried, not derived from anything here.
                Assert.Equal(4, stage.DoneCount);
            },
            stage =>
            {
                Assert.Equal("QA Validation", stage.Name);
                Assert.Equal("in_progress", stage.Status);

                // A stage still open has no completion to measure to.
                Assert.Null(stage.DurationMs);
                Assert.Equal(0, stage.DoneCount);
            });
    }

    [Fact]
    public async Task Token_usage_is_read_at_every_level()
    {
        GivenRun("orch-dashboard", "tasks-bulk-select-daa039-9e68802b", "run.json", OrchFeatureRun);

        var run = Assert.Single((await ReadAsync()).Runs);
        var usage = Assert.IsType<DeliveryRunTokenUsage>(run.TokenUsage);

        Assert.Equal(4789, usage.Total.ModelCalls);
        Assert.Equal(1_662_126, usage.Total.OutputTokens);
        Assert.Equal(795_660_916, usage.Total.CacheReadTokens);
        Assert.Equal(21_577_798, usage.Total.CacheWriteTokens);
        Assert.Equal(4411, usage.SubAgent.ModelCalls);
        Assert.Equal(1_089_085, usage.SubAgent.OutputTokens);

        // Stage order, not the writer's property order: the keys are indices as
        // strings, and "10" sorts before "2" ordinally.
        Assert.Equal(["Scope Discovery", "Implementation", "QA Validation"], usage.ByStage.Select(stage => stage.StageName));
        Assert.Equal(318_111, usage.ByStage[1].Total.OutputTokens);
        Assert.Equal(272_526, usage.ByStage[1].SubAgent.OutputTokens);

        Assert.Equal(["claude-opus-5", "claude-sonnet-5"], usage.Models);
    }

    [Fact]
    public async Task The_context_gauge_carries_its_peak()
    {
        GivenRun("orch-dashboard", "tasks-bulk-select-daa039-9e68802b", "run.json", OrchFeatureRun);

        var run = Assert.Single((await ReadAsync()).Runs);
        var context = Assert.IsType<DeliveryRunContext>(run.Context);

        Assert.Equal(333_962, context.CurrentTokens);
        Assert.Equal(1_000_000, context.TokenLimit);
        Assert.Equal(412_500, context.PeakTokens);
        Assert.Equal(0.4125m, context.PeakShare);
    }

    /// <summary>
    /// The thousand-record list becomes two short ones. By category every insight
    /// counts — a delegated agent, which the dashboard files without a category, is
    /// its own — and by server only the MCP calls do, because a built-in tool has no
    /// server to be grouped under.
    /// </summary>
    [Fact]
    public async Task Insights_are_summed_by_category_and_by_mcp_server()
    {
        GivenRun("orch-dashboard", "tasks-bulk-select-daa039-9e68802b", "run.json", OrchFeatureRun);

        var run = Assert.Single((await ReadAsync()).Runs);

        Assert.Collection(
            run.InsightsByCategory,
            group =>
            {
                // Largest first. Three shell calls, one of which failed, 3.5 s between them.
                Assert.Equal("Shell", group.Name);
                Assert.Equal(3, group.Count);
                Assert.Equal(3500, group.DurationMs);
                Assert.Equal(1, group.Failed);
            },
            group =>
            {
                Assert.Equal("QA (Playwright/Aspire)", group.Name);
                Assert.Equal(2, group.Count);
                Assert.Equal(2272 + 900, group.DurationMs);
                Assert.Equal(0, group.Failed);
            },
            group =>
            {
                Assert.Equal("Agents", group.Name);
                Assert.Equal(1, group.Count);
                Assert.Equal(22_069, group.DurationMs);
            });

        Assert.Collection(
            run.InsightsByServer,
            group =>
            {
                Assert.Equal("plugin_qa_playwright", group.Name);
                Assert.Equal(2, group.Count);
            });
    }

    /// <summary>
    /// The older generation stored its stages nameless and kept the names as the keys
    /// of the done-count map, in stage order. A row that said "Stage 3" for a run that
    /// knew perfectly well it was in Build &amp; Test would be throwing away a fact the
    /// file holds.
    /// </summary>
    /// <summary>
    /// Which agent ran in which stage, and on which model, is in the insights — one
    /// record per delegated run, stamped with the stage it ran in. The stage's own
    /// <c>agents</c> list only declares names, so it adds a name the insights never
    /// showed running and nothing else.
    /// </summary>
    [Fact]
    public async Task Each_stage_carries_the_agents_it_delegated_to_and_their_models()
    {
        GivenRun("delivery-surface-dashboard", "flow-diagram-0d4fe8-3b03b799", "run-agents.json", """
            {
              "id": "run-agents",
              "skillId": "flow-code",
              "title": "Agents per stage",
              "status": "in_progress",
              "updatedAt": "2026-09-24T22:45:46.652Z",
              "stages": [
                { "name": "Scope Discovery", "status": "done", "doneCount": 1, "agents": [] },
                { "name": "Specification & Architecture Intake", "status": "done", "doneCount": 1, "agents": ["architecture:architect"] },
                { "name": "Build & Test", "status": "in_progress", "doneCount": 0, "agents": ["delivery:builder"] },
                { "name": "Summary", "status": "pending", "doneCount": 0, "agents": [] }
              ],
              "insights": [
                { "kind": "agent", "agentName": "Explore", "agentDisplayName": "Explore", "model": "claude-opus-5-5", "status": "completed", "durationMs": 1259, "stageIndex": 0, "stageName": "Scope Discovery" },
                { "kind": "agent", "agentName": "Explore", "model": "claude-opus-5-5", "status": "completed", "durationMs": 900, "stageIndex": 0, "stageName": "Scope Discovery" },
                { "kind": "agent", "agentName": "Explore", "model": "haiku", "status": "failed", "durationMs": 10, "stageIndex": 0, "stageName": "Scope Discovery" },
                { "kind": "agent", "agentName": "architect", "model": "opus", "status": "completed", "durationMs": 20, "stageIndex": null, "stageName": "Specification & Architecture Intake" },
                { "kind": "agent", "agentName": "general-purpose", "model": "sonnet", "status": "completed", "durationMs": 5, "stageIndex": null, "stageName": null },
                { "kind": "tool", "toolName": "Bash", "category": "Shell", "durationMs": 5, "success": true, "stageIndex": 2, "stageName": "Build & Test" }
              ]
            }
            """);

        var run = Assert.Single((await ReadAsync()).Runs);

        Assert.Collection(
            run.Stages,
            stage => Assert.Equal(
                [new DeliveryRunStageAgent("Explore", "claude-opus-5-5", 2, 0), new DeliveryRunStageAgent("Explore", "haiku", 1, 1)],
                stage.Agents),
            // Matched by name where the record has no index, and the declared
            // "architecture:architect" is that same agent rather than a second one.
            stage => Assert.Equal([new DeliveryRunStageAgent("architect", "opus", 1, 0)], stage.Agents),
            // Declared, never seen running: named, with no model to claim. The
            // stage's tool call is not an agent.
            stage => Assert.Equal([new DeliveryRunStageAgent("delivery:builder", null, 0, 0)], stage.Agents),
            // An agent outside every stage is in none.
            stage => Assert.Empty(stage.Agents));
    }

    [Fact]
    public async Task Nameless_stages_take_their_names_from_the_phase_counts()
    {
        GivenRun("orch-dashboard", "Backlog-43b9057e", "run-legacy.json", LegacyRun);

        var run = Assert.Single((await ReadAsync()).Runs);

        Assert.Equal(["Establish State", "Integrate", "Push", "Stage 4"], run.Stages.Select(stage => stage.Name));

        // No usage, no gauge: the older file carries neither, and null is the honest
        // reading of a file that does not say rather than a zero that claims it does.
        Assert.Null(run.TokenUsage);
        Assert.Null(run.Context);
        Assert.Empty(run.References);
        Assert.Equal("Backlog", run.WorktreeName);
    }

    /// <summary>
    /// Found on a real profile: a writer that lost its stage names filed every count
    /// under the key <c>"undefined"</c> and wrote blank per-stage token names. Neither is
    /// a name, and a list holding one is not in stage order, so both fall back to the
    /// positional stand-in rather than a row reading "undefined".
    /// </summary>
    [Fact]
    public async Task A_writers_undefined_and_blank_stage_names_are_not_taken_for_names()
    {
        GivenRun("orch-dashboard", "Backlog-43b9057e", "run-undefined.json", UndefinedStagesRun);

        var run = Assert.Single((await ReadAsync()).Runs);

        Assert.Equal(["Stage 1", "Stage 2"], run.Stages.Select(stage => stage.Name));
        // The token map is keyed from zero and the stage list numbers from one; a
        // token line for key "0" is the list's "Stage 1".
        Assert.Equal(["Stage 1", "Implementation"], run.TokenUsage!.ByStage.Select(stage => stage.StageName));
    }

    [Fact]
    public async Task Both_dashboards_are_read_and_say_which_they_are()
    {
        GivenRun("orch-dashboard", "tasks-bulk-select-daa039-9e68802b", "run.json", OrchFeatureRun);
        GivenRun("delivery-surface-dashboard", "delivery-run-reader-0a50e5-1c2d3e4f", "run-flow.json", DeliverySurfaceRun);

        var catalog = await ReadAsync();

        Assert.Equal(2, catalog.Runs.Count);

        var flow = Assert.Single(catalog.Runs, run => run.Dashboard == "delivery-surface-dashboard");

        Assert.Equal("flow-code", flow.SkillId);
        Assert.Equal("in_progress", flow.Status);
        Assert.True(flow.InProgress);
        Assert.Equal("delivery-run-reader-0a50e5", flow.WorktreeName);
        Assert.Empty(catalog.Unreadable);
    }

    /// <summary>
    /// One file on the profile this was written against is cut off mid-write. It
    /// costs that run and nothing else — the folder's other runs still read, and the
    /// dashboard is not reported unreadable over one file.
    /// </summary>
    [Fact]
    public async Task A_corrupt_file_is_skipped_and_the_rest_of_the_folder_still_reads()
    {
        GivenRun("orch-dashboard", "tasks-bulk-select-daa039-9e68802b", "run-good.json", OrchFeatureRun);
        GivenRun("orch-dashboard", "tasks-bulk-select-daa039-9e68802b", "run-cut.json", OrchFeatureRun[..(OrchFeatureRun.Length / 2)]);
        GivenRun("orch-dashboard", "tasks-bulk-select-daa039-9e68802b", "run-not-a-run.json", """{"title": "no id here"}""");
        GivenRun("orch-dashboard", "tasks-bulk-select-daa039-9e68802b", "run-array.json", "[1, 2, 3]");

        var catalog = await ReadAsync();

        Assert.Equal("run-mtlea9im-xxayjs", Assert.Single(catalog.Runs).Id);
        Assert.Empty(catalog.Unreadable);
    }

    /// <summary>
    /// A machine that never installed either dashboard has no folder, and that is
    /// the ordinary case rather than a failure: nothing to show, nothing unreadable.
    /// </summary>
    [Fact]
    public async Task A_dashboard_that_was_never_installed_is_not_unreadable()
    {
        Directory.CreateDirectory(_home);

        var catalog = await ReadAsync();

        Assert.Empty(catalog.Runs);
        Assert.Empty(catalog.Unreadable);
    }

    [Fact]
    public async Task A_worktree_folder_with_no_runs_folder_is_skipped()
    {
        Directory.CreateDirectory(Path.Combine(_home, "orch-dashboard", "empty-worktree-00000000", "telemetry"));

        var catalog = await ReadAsync();

        Assert.Empty(catalog.Runs);
        Assert.Empty(catalog.Unreadable);
    }

    /// <summary>
    /// A dashboard folder that exists and cannot be listed is named, so the pane can
    /// say which half of the picture is missing instead of presenting the other half
    /// as the whole. Windows only, because a deny rule is how a folder becomes
    /// unreadable to its own owner here without administrator rights, and the API
    /// that writes one is Windows-only.
    /// </summary>
    [Fact]
    public async Task A_dashboard_folder_that_cannot_be_listed_is_named_unreadable()
    {
        if (!OperatingSystem.IsWindows()) return;

        GivenRun("delivery-surface-dashboard", "delivery-run-reader-0a50e5-1c2d3e4f", "run-flow.json", DeliverySurfaceRun);

        var locked = Directory.CreateDirectory(Path.Combine(_home, "orch-dashboard"));

        DenyListing(locked);

        try
        {
            var catalog = await ReadAsync();

            Assert.Equal(["orch-dashboard"], catalog.Unreadable);

            // The other dashboard is unaffected.
            Assert.Equal("flow-code", Assert.Single(catalog.Runs).SkillId);
        }
        finally
        {
            AllowListing(locked);
        }
    }

    [Theory]
    [InlineData("tasks-bulk-select-daa039-9e68802b", "tasks-bulk-select-daa039")]
    [InlineData("Backlog-43b9057e", "Backlog")]
    [InlineData("no-hash-here", "no-hash-here")]
    [InlineData("short-1234", "short-1234")]
    public void The_worktree_name_is_the_folder_without_its_hash(string folder, string name) =>
        Assert.Equal(name, DeliveryRunReader.WorktreeName(folder));

    private Task<DeliveryRunCatalog> ReadAsync() => new LocalDeliveryRunSource(_home, MachineId, Machine).GetRunsAsync();

    private void GivenRun(string dashboard, string worktree, string file, string json)
    {
        var folder = Path.Combine(_home, dashboard, worktree, "runs");

        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, file), json);
    }

    [SupportedOSPlatform("windows")]
    private static void DenyListing(DirectoryInfo folder)
    {
        var security = folder.GetAccessControl();

        security.AddAccessRule(new FileSystemAccessRule(
            WindowsIdentity.GetCurrent().User!,
            FileSystemRights.ListDirectory,
            AccessControlType.Deny));

        folder.SetAccessControl(security);
    }

    [SupportedOSPlatform("windows")]
    private static void AllowListing(DirectoryInfo folder)
    {
        var security = folder.GetAccessControl();

        security.RemoveAccessRule(new FileSystemAccessRule(
            WindowsIdentity.GetCurrent().User!,
            FileSystemRights.ListDirectory,
            AccessControlType.Deny));

        folder.SetAccessControl(security);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_home)) Directory.Delete(_home, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    /// <summary>A run of the orch dashboard's current generation, trimmed from a real
    /// file: three of its ten stages, three of its thousand insights, and the token
    /// map with its keys deliberately out of order.</summary>
    private const string OrchFeatureRun = """
        {
          "id": "run-mtlea9im-xxayjs",
          "skillId": "orch-feature",
          "title": "Tasks bulk-selection mode",
          "status": "done",
          "changeKind": "new-functionality",
          "approval": { "personalValidation": "approved", "decidedAt": "2026-09-03T20:54:44.839Z", "note": "" },
          "originalPrompt": "For tasks, I only want to see the bulk action checkbox when...",
          "promptHistory": [],
          "phaseDoneCounts": { "Scope Discovery": 1, "Implementation": 4, "QA Validation": 0 },
          "githubIssue": { "repo": "JSdotNet/Backlog", "url": "https://github.com/JSdotNet/Backlog/issues/211", "title": "Fix foreground-thread leak", "number": 211 },
          "startedAt": "2026-09-03T10:41:21.070Z",
          "updatedAt": "2026-09-03T20:59:36.948Z",
          "stages": [
            { "name": "Scope Discovery", "agents": [], "status": "done", "output": "**Requested behaviour.**", "startedAt": "2026-09-03T10:41:25.704Z", "doneCount": 1, "completedAt": "2026-09-03T14:34:20.291Z", "updatedAt": "2026-09-03T14:34:20.291Z", "durationMs": 13974587 },
            { "name": "Implementation", "agents": [], "status": "done", "output": "", "startedAt": "2026-09-03T14:34:20.291Z", "doneCount": 4, "completedAt": "2026-09-03T15:40:00.000Z", "updatedAt": "2026-09-03T15:40:00.000Z", "durationMs": 3939709.5 },
            { "name": "QA Validation", "agents": [], "status": "in_progress", "output": "", "startedAt": "2026-09-03T15:40:00.000Z", "doneCount": 0, "completedAt": null, "updatedAt": "2026-09-03T15:49:19.822Z", "durationMs": null, "links": [{ "label": "Pull request #365", "url": "https://github.com/JSdotNet/Backlog/pull/365", "description": "Tasks bulk-selection mode" }] }
          ],
          "summary": "Delivered both halves.",
          "insights": [
            { "kind": "tool", "toolName": "mcp__plugin_qa_playwright__browser_evaluate", "category": "QA (Playwright/Aspire)", "durationMs": 2272, "success": true, "endedAt": "2026-09-03T15:49:19.822Z", "mcpServerName": "plugin_qa_playwright", "model": "claude-opus-5", "stageIndex": 2, "stageName": "QA Validation" },
            { "kind": "tool", "toolName": "mcp__plugin_qa_playwright__browser_snapshot", "category": "QA (Playwright/Aspire)", "durationMs": 900, "success": true, "endedAt": "2026-09-03T15:49:25.000Z", "mcpServerName": "plugin_qa_playwright", "model": "claude-opus-5", "stageIndex": 2, "stageName": "QA Validation" },
            { "kind": "tool", "toolName": "Bash", "category": "Shell", "durationMs": 1000, "success": true, "endedAt": "2026-09-03T11:00:00.000Z", "mcpServerName": null, "model": "claude-opus-5", "stageIndex": 0, "stageName": "Scope Discovery" },
            { "kind": "tool", "toolName": "Bash", "category": "Shell", "durationMs": 2000, "success": false, "endedAt": "2026-09-03T11:01:00.000Z", "mcpServerName": null, "model": "claude-opus-5", "stageIndex": 0, "stageName": "Scope Discovery" },
            { "kind": "tool", "toolName": "PowerShell", "category": "Shell", "durationMs": 500, "success": true, "endedAt": "2026-09-03T11:02:00.000Z", "model": "claude-opus-5", "stageIndex": 1, "stageName": "Implementation" },
            { "kind": "agent", "agentName": "Explore", "agentDisplayName": "Explore", "model": "claude-opus-5", "status": "completed", "durationMs": 22069, "endedAt": "2026-09-03T22:44:37.233Z", "stageIndex": 0, "stageName": "Scope Discovery" }
          ],
          "destinations": { "prefixes": {}, "artifact": false, "contexts": null },
          "tokenUsage": {
            "total": { "modelCalls": 4789, "inputTokens": 9578, "outputTokens": 1662126, "reasoningTokens": 0, "cacheReadTokens": 795660916, "cacheWriteTokens": 21577798 },
            "subAgent": { "modelCalls": 4411, "inputTokens": 8822, "outputTokens": 1089085, "reasoningTokens": 0, "cacheReadTokens": 725728911, "cacheWriteTokens": 18035322 },
            "byStage": {
              "2": { "stageName": "QA Validation", "total": { "modelCalls": 2092, "inputTokens": 4184, "outputTokens": 475366, "reasoningTokens": 0, "cacheReadTokens": 323778966, "cacheWriteTokens": 5805426 }, "subAgent": { "modelCalls": 1995, "inputTokens": 3990, "outputTokens": 325436, "reasoningTokens": 0, "cacheReadTokens": 309048234, "cacheWriteTokens": 5111919 } },
              "0": { "stageName": "Scope Discovery", "total": { "modelCalls": 6, "inputTokens": 12, "outputTokens": 6420, "reasoningTokens": 0, "cacheReadTokens": 721512, "cacheWriteTokens": 10803 }, "subAgent": { "modelCalls": 0, "inputTokens": 0, "outputTokens": 0, "reasoningTokens": 0, "cacheReadTokens": 0, "cacheWriteTokens": 0 } },
              "1": { "stageName": "Implementation", "total": { "modelCalls": 517, "inputTokens": 1034, "outputTokens": 318111, "reasoningTokens": 0, "cacheReadTokens": 149099696, "cacheWriteTokens": 4782409 }, "subAgent": { "modelCalls": 491, "inputTokens": 982, "outputTokens": 272526, "reasoningTokens": 0, "cacheReadTokens": 143362202, "cacheWriteTokens": 4673132 } }
            },
            "models": ["claude-opus-5", "claude-sonnet-5"],
            "updatedAt": "2026-09-03T20:59:22.609Z"
          },
          "context": { "currentTokens": 333962, "tokenLimit": 1000000, "peakTokens": 412500, "sampledAt": "2026-09-03T20:59:22.611Z", "pressureNotified": [] }
        }
        """;

    /// <summary>The older generation: nameless stages, no usage, no gauge, a null
    /// issue. The fourth stage has no matching phase key, which is what earns a
    /// positional name.</summary>
    private const string LegacyRun = """
        {
          "id": "run-mu29w1tj-mvip8f",
          "skillId": "update-pr-branch",
          "title": "Update PR #482 with main",
          "status": "completed",
          "changeKind": "none",
          "approval": { "personalValidation": "pending", "decidedAt": null, "note": "" },
          "originalPrompt": "",
          "promptHistory": [],
          "phaseDoneCounts": { "Establish State": 1, "Integrate": 1, "Push": 1 },
          "githubIssue": null,
          "startedAt": "2026-09-15T06:10:24.439Z",
          "updatedAt": "2026-09-15T06:27:41.305Z",
          "stages": [
            { "agents": [], "status": "done", "output": "", "startedAt": "2026-09-15T06:10:24.439Z", "doneCount": 1, "completedAt": "2026-09-15T06:11:00.000Z", "updatedAt": "2026-09-15T06:11:00.000Z", "durationMs": 35561 },
            { "agents": [], "status": "done", "output": "", "startedAt": "2026-09-15T06:11:00.000Z", "doneCount": 1, "completedAt": "2026-09-15T06:20:00.000Z", "updatedAt": "2026-09-15T06:20:00.000Z", "durationMs": 540000 },
            { "agents": [], "status": "done", "output": "", "startedAt": "2026-09-15T06:20:00.000Z", "doneCount": 1, "completedAt": "2026-09-15T06:27:41.305Z", "updatedAt": "2026-09-15T06:27:41.305Z", "durationMs": 461305 },
            { "agents": [], "status": "pending", "output": "", "startedAt": null, "doneCount": 0, "completedAt": null, "updatedAt": null, "durationMs": null }
          ],
          "summary": "",
          "insights": []
        }
        """;

    /// <summary>The writer artefact: one <c>"undefined"</c> key standing for every
    /// stage, and a blank per-stage token name beside a real one.</summary>
    private const string UndefinedStagesRun = """
        {
          "id": "run-mtnlhq5q-ttyvr0",
          "skillId": "workflow-issue-sweep",
          "title": "Issue sweep",
          "status": "done",
          "changeKind": null,
          "phaseDoneCounts": { "undefined": 6 },
          "githubIssue": null,
          "startedAt": "2026-09-05T08:00:00.000Z",
          "updatedAt": "2026-09-05T09:00:00.000Z",
          "stages": [
            { "agents": [], "status": "done", "output": "", "doneCount": 3, "durationMs": 1000 },
            { "agents": [], "status": "done", "output": "", "doneCount": 3, "durationMs": 2000 }
          ],
          "insights": [],
          "tokenUsage": {
            "total": { "modelCalls": 2, "inputTokens": 4, "outputTokens": 100, "reasoningTokens": 0, "cacheReadTokens": 0, "cacheWriteTokens": 0 },
            "subAgent": { "modelCalls": 0, "inputTokens": 0, "outputTokens": 0, "reasoningTokens": 0, "cacheReadTokens": 0, "cacheWriteTokens": 0 },
            "byStage": {
              "0": { "stageName": "", "total": { "modelCalls": 1, "inputTokens": 2, "outputTokens": 50, "reasoningTokens": 0, "cacheReadTokens": 0, "cacheWriteTokens": 0 }, "subAgent": { "modelCalls": 0, "inputTokens": 0, "outputTokens": 0, "reasoningTokens": 0, "cacheReadTokens": 0, "cacheWriteTokens": 0 } },
              "1": { "stageName": "Implementation", "total": { "modelCalls": 1, "inputTokens": 2, "outputTokens": 50, "reasoningTokens": 0, "cacheReadTokens": 0, "cacheWriteTokens": 0 }, "subAgent": { "modelCalls": 0, "inputTokens": 0, "outputTokens": 0, "reasoningTokens": 0, "cacheReadTokens": 0, "cacheWriteTokens": 0 } }
            },
            "models": ["claude-opus-5"]
          },
          "context": { "currentTokens": 1, "tokenLimit": 1000000, "peakTokens": 1 }
        }
        """;

    /// <summary>A run whose stages link the same pull request twice, an issue its
    /// tracker field also names, and two local URLs that are ports on this machine.</summary>
    private const string LinkedRun = """
        {
          "id": "run-linked",
          "skillId": "orch-adr",
          "title": "ADR 0005 filename decision",
          "status": "done",
          "changeKind": "documentation",
          "phaseDoneCounts": { "Implementation": 1 },
          "githubIssue": { "repo": "JSdotNet/Backlog", "url": "https://github.com/JSdotNet/Backlog/pull/378", "title": "Take a position on manual rank", "number": 378 },
          "startedAt": "2026-09-05T08:00:00.000Z",
          "updatedAt": "2026-09-05T09:00:00.000Z",
          "stages": [
            {
              "name": "Implementation", "agents": [], "status": "done", "output": "", "doneCount": 1, "durationMs": 1000,
              "links": [
                { "label": "PR #378", "url": "https://github.com/JSdotNet/Backlog/pull/378", "description": "ADR 0005 filename decision" },
                { "label": "Issue #218", "url": "https://github.com/JSdotNet/Backlog/issues/218", "description": "Prompt for a closing keyword" },
                { "label": "Aspire dashboard", "url": "http://localhost:61684/login?t=abc", "description": "" },
                { "label": "Desktop harness", "url": "http://127.0.0.1:61685/", "description": "" }
              ]
            }
          ],
          "insights": []
        }
        """;

    /// <summary>A run whose only address is in the sentence it closed with.</summary>
    private const string SummaryOnlyRun = """
        {
          "id": "run-summary",
          "skillId": "orch-feature",
          "title": "Archify animation loop",
          "status": "done",
          "changeKind": "new-functionality",
          "phaseDoneCounts": { "Summary": 1 },
          "githubIssue": null,
          "startedAt": "2026-09-01T08:00:00.000Z",
          "updatedAt": "2026-09-01T12:00:00.000Z",
          "stages": [
            { "name": "Summary", "agents": [], "status": "done", "output": "", "doneCount": 1, "durationMs": 1000 }
          ],
          "summary": "Delivered as https://github.com/JSdotNet/Backlog/pull/330 (12 files, +430/-88).",
          "insights": []
        }
        """;

    /// <summary>A run started from an entry copied out of the app: its marker line,
    /// then the title and the body.</summary>
    private const string EntryMarkerRun = """
        {
          "id": "run-entry",
          "skillId": "flow-code",
          "title": "Link the done badge",
          "status": "done",
          "originalPrompt": "/backlog-tools:backlog-run-plan-item entry `5f0c2a9e-3b1d-4c7a-9e2f-0a1b2c3d4e5f`:\n# Link the done badge to its task\n\nThe status badge should open the entry.",
          "startedAt": "2026-09-26T09:00:00.000Z",
          "updatedAt": "2026-09-26T10:00:00.000Z",
          "stages": [
            { "name": "Implementation", "agents": [], "status": "done", "output": "", "doneCount": 1, "durationMs": 1000 }
          ],
          "insights": []
        }
        """;

    /// <summary>An imported entry copied out of the app: the app's line on top and the
    /// import's plan item marker in the body.</summary>
    private const string BothMarkersRun = """
        {
          "id": "run-both",
          "skillId": "flow-code",
          "title": "Read delivery run files",
          "status": "done",
          "originalPrompt": "/backlog-tools:backlog-run-plan-item entry `5f0c2a9e-3b1d-4c7a-9e2f-0a1b2c3d4e5f`:\nRead delivery run files\n\nBacklog plan item `delivery-run-reader` of plan `backlog-mcp-server` for `Backlog`.",
          "startedAt": "2026-09-26T09:00:00.000Z",
          "updatedAt": "2026-09-26T10:00:00.000Z",
          "stages": [
            { "name": "Implementation", "agents": [], "status": "done", "output": "", "doneCount": 1, "durationMs": 1000 }
          ],
          "insights": []
        }
        """;

    /// <summary>A run started from a pasted Backlog plan item.</summary>
    private const string PlanItemRun = """
        {
          "id": "run-plan",
          "skillId": "flow-code",
          "title": "Read delivery run files into the Sessions pane",
          "status": "in_progress",
          "changeKind": "new-functionality",
          "originalPrompt": "Backlog plan item `delivery-run-reader` of plan `backlog-mcp-server` for `Backlog` \u2014 run it with the `backlog-run-plan-item` skill.\n\nAdd a DeliveryRunReader beside ClaudeSessionReader.",
          "phaseDoneCounts": { "Implementation": 1 },
          "githubIssue": null,
          "startedAt": "2026-09-22T01:20:00.000Z",
          "updatedAt": "2026-09-22T02:05:00.000Z",
          "stages": [
            { "name": "Implementation", "agents": [], "status": "in_progress", "output": "", "doneCount": 0, "durationMs": null }
          ],
          "insights": []
        }
        """;

    /// <summary>A run of the delivery surface, still under way.</summary>
    private const string DeliverySurfaceRun = """
        {
          "id": "run-mv1a2b3c-flow01",
          "skillId": "flow-code",
          "title": "Read delivery run files into the Sessions pane",
          "status": "in_progress",
          "changeKind": "new-functionality",
          "approval": { "personalValidation": "pending", "decidedAt": null, "note": "" },
          "originalPrompt": "Add a DeliveryRunReader beside ClaudeSessionReader",
          "promptHistory": [],
          "phaseDoneCounts": { "Update Base": 1, "Scope Discovery": 1 },
          "githubIssue": null,
          "startedAt": "2026-09-22T01:20:00.000Z",
          "updatedAt": "2026-09-22T02:05:00.000Z",
          "stages": [
            { "name": "Update Base", "agents": [], "status": "done", "output": "Fast-forwarded.", "startedAt": "2026-09-22T01:20:00.000Z", "doneCount": 1, "completedAt": "2026-09-22T01:21:00.000Z", "updatedAt": "2026-09-22T01:21:00.000Z", "durationMs": 60000 },
            { "name": "Scope Discovery", "agents": [], "status": "done", "output": "", "startedAt": "2026-09-22T01:21:00.000Z", "doneCount": 1, "completedAt": "2026-09-22T01:30:00.000Z", "updatedAt": "2026-09-22T01:30:00.000Z", "durationMs": 540000 },
            { "name": "Implementation", "agents": [], "status": "in_progress", "output": "", "startedAt": "2026-09-22T01:30:00.000Z", "doneCount": 0, "completedAt": null, "updatedAt": "2026-09-22T02:05:00.000Z", "durationMs": null }
          ],
          "summary": "",
          "insights": [
            { "kind": "tool", "toolName": "Bash", "category": "Shell", "durationMs": 1200, "success": true, "endedAt": "2026-09-22T01:25:00.000Z", "mcpServerName": null, "model": "claude-opus-5", "stageIndex": 1, "stageName": "Scope Discovery" }
          ],
          "tokenUsage": {
            "total": { "modelCalls": 40, "inputTokens": 80, "outputTokens": 52000, "reasoningTokens": 0, "cacheReadTokens": 9000000, "cacheWriteTokens": 400000 },
            "subAgent": { "modelCalls": 0, "inputTokens": 0, "outputTokens": 0, "reasoningTokens": 0, "cacheReadTokens": 0, "cacheWriteTokens": 0 },
            "byStage": {},
            "models": ["claude-opus-5"],
            "updatedAt": "2026-09-22T02:05:00.000Z"
          },
          "context": { "currentTokens": 210000, "tokenLimit": 1000000, "peakTokens": 210000, "sampledAt": "2026-09-22T02:05:00.000Z", "pressureNotified": [] }
        }
        """;
}
