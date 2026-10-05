namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// How two surfaces' files of one run become one run. Each condition of the rule has
/// a test that fails it alone, because a rule with five parts is wrong exactly when it
/// quietly needs only four of them; then which file is the record, what the other
/// contributes, and the one shape of profile the window exists for.
/// </summary>
public sealed class DeliveryRunMergingTests
{
    private static readonly DateTimeOffset Noon = new(2026, 9, 27, 12, 0, 0, TimeSpan.Zero);

    /// <summary>The folder both surfaces filed under on a real profile.</summary>
    private const string Worktree = "version-number-display-9fdeac-20b24a3e";

    private const string Session = "3c3beb1b-c8c1-4db7-9cdc-6d82d2c90f74";

    [Fact]
    public void Two_surfaces_reports_of_one_run_are_one_run()
    {
        // The real pair: the backlog surface answered start_run 224 ms before the
        // dashboard did, and the files sit in the folders in that order.
        var backlog = Run("run-mujubkak-7fq49u", "backlog", Noon.AddMilliseconds(-224));
        var dashboard = Run("run-mujubkgt-mny0y2", "delivery-surface-dashboard", Noon);

        var merged = Assert.Single(DeliveryRunMerging.Merge([backlog, dashboard]));

        Assert.Equal("run-mujubkgt-mny0y2", merged.Id);
        Assert.Equal("delivery-surface-dashboard", merged.Dashboard);
        Assert.Equal(["delivery-surface-dashboard", "backlog"], merged.Surfaces);
    }

    [Fact]
    public void A_run_one_surface_recorded_is_left_exactly_as_it_is()
    {
        var run = Run("run-1", "backlog", Noon);

        var merged = Assert.Single(DeliveryRunMerging.Merge([run]));

        Assert.Same(run, merged);
        Assert.Equal(["backlog"], merged.Surfaces);
        Assert.Equal(["run-1"], merged.RunIds);
    }

    [Fact]
    public void The_dashboard_is_the_record_and_the_backlog_fills_only_its_gaps()
    {
        var pullRequest = new DeliveryRunReference(DeliveryRunReferenceKind.PullRequest, "PR #771", "Edit roadmap alignment", "https://github.com/JSdotNet/Backlog/pull/771", "JSdotNet/Backlog");
        var issue = new DeliveryRunReference(DeliveryRunReferenceKind.Issue, "#770", null, "https://github.com/JSdotNet/Backlog/issues/770", "JSdotNet/Backlog");
        var samePullRequest = pullRequest with { Title = null };

        var dashboardUsage = new DeliveryRunTokenUsage(new(86, 172, 108_442, 0, 40_000_000, 900_000), new(0, 0, 0, 0, 0, 0), [], ["claude-opus-5"]);
        var backlogUsage = new DeliveryRunTokenUsage(new(25, 50, 22_079, 0, 9_000_000, 200_000), new(0, 0, 0, 0, 0, 0), [], ["claude-opus-5"]);

        // The dashboard's finish_run has no "parked", so the flow said "blocked"
        // there and "parked" to the backlog surface: the same run, two words.
        var dashboard = Run("run-d", "delivery-surface-dashboard", Noon, status: "blocked") with
        {
            References = [pullRequest],
            Stages = [new("Scope Discovery", "done", 60_000, 1), new("Implementation", "blocked", null, 0)],
            TokenUsage = dashboardUsage,
            Context = new DeliveryRunContext(256_624, 1_000_000, 256_624),
            InsightsByCategory = [new("Shell", 31, 7_200_000, 0)],
            UpdatedAt = Noon.AddMinutes(8)
        };
        var backlog = Run("run-b", "backlog", Noon.AddSeconds(-1), status: "parked") with
        {
            ChangeKind = "bug-fix",
            References = [issue, samePullRequest],
            Stages = [new("Scope Discovery", "done", 61_000, 1)],
            TokenUsage = backlogUsage,
            InsightsByCategory = [new("Shell", 12, 3_000_000, 0)],
            InsightsByServer = [new("plugin_delivery-surface-backlog", 10, 14_000, 0)],
            SessionIds = [Session, "06fb648e-e2d6-4db7-b630-251d9129796f"],
            UpdatedAt = Noon.AddMinutes(9)
        };

        var merged = Assert.Single(DeliveryRunMerging.Merge([dashboard, backlog]));

        // The record's own facts stay its own, disagreement included.
        Assert.Equal("run-d", merged.Id);
        Assert.Equal("blocked", merged.Status);
        Assert.Same(dashboard.Stages, merged.Stages);
        Assert.Same(dashboardUsage, merged.TokenUsage);
        Assert.Same(dashboard.Context, merged.Context);
        Assert.Same(dashboard.InsightsByCategory, merged.InsightsByCategory);

        // What the record left blank, the other fills.
        Assert.Equal("bug-fix", merged.ChangeKind);
        Assert.Same(backlog.InsightsByServer, merged.InsightsByServer);

        // Unions: the pull request once, the issue the dashboard never named after
        // it; every session either file named, the record's first.
        Assert.Equal([pullRequest, issue], merged.References);
        Assert.Equal([Session, "06fb648e-e2d6-4db7-b630-251d9129796f"], merged.SessionIds);

        // The later of the two updates is when the run was last touched.
        Assert.Equal(Noon.AddMinutes(9), merged.UpdatedAt);
        Assert.Equal(["delivery-surface-dashboard", "backlog"], merged.Surfaces);
    }

    [Fact]
    public void The_orch_dashboard_outranks_the_backlog_and_a_dashboard_nobody_listed_ranks_last()
    {
        var other = Run("run-o", "some-other-surface", Noon);
        var backlog = Run("run-b", "backlog", Noon.AddSeconds(2));
        var orch = Run("run-x", "orch-dashboard", Noon.AddSeconds(1));

        var merged = Assert.Single(DeliveryRunMerging.Merge([other, backlog, orch]));

        Assert.Equal("run-x", merged.Id);
        Assert.Equal(["orch-dashboard", "backlog", "some-other-surface"], merged.Surfaces);
    }

    [Fact]
    public void Two_files_from_one_surface_are_two_runs()
    {
        var first = Run("run-1", "backlog", Noon);
        var second = Run("run-2", "backlog", Noon.AddSeconds(1));

        Assert.Equal(2, DeliveryRunMerging.Merge([first, second]).Count);
    }

    [Fact]
    public void A_different_skill_is_a_different_run()
    {
        var code = Run("run-1", "delivery-surface-dashboard", Noon, skill: "flow-code");
        var spec = Run("run-2", "backlog", Noon, skill: "flow-spec");

        Assert.Equal(2, DeliveryRunMerging.Merge([code, spec]).Count);
    }

    [Fact]
    public void A_different_worktree_is_a_different_run_and_letter_case_is_not_a_difference()
    {
        var here = Run("run-1", "delivery-surface-dashboard", Noon);
        var elsewhere = Run("run-2", "backlog", Noon, worktree: "task-list-scrollbar-8da0a4-a27c6f32", sessions: ["78bbbe21-eadf-4bbb-9835-6ac01fcb924d"]);
        var hereAgain = Run("run-3", "backlog", Noon, worktree: Worktree.ToUpperInvariant());

        var merged = DeliveryRunMerging.Merge([here, elsewhere, hereAgain]);

        Assert.Equal(2, merged.Count);
        Assert.Equal("run-1", merged[0].Id);
        Assert.Equal(["delivery-surface-dashboard", "backlog"], merged[0].Surfaces);
        Assert.Same(elsewhere, merged[1]);
    }

    [Fact]
    public void A_dashboard_copy_filed_under_the_main_checkout_folds_into_the_worktrees_run()
    {
        // The real pair: the dashboard server keys its folder by its own working
        // directory, the repository's main checkout, whichever worktree the flow ran
        // in; the backlog surface keys by the worktree the caller named. Same skill,
        // same session, 471 ms apart — one run, filed under two folders.
        const string session = "65f4db2d-bc5c-4376-950f-9dfb04717cb5";
        var backlog = Run("run-muvhm1f3-5a1pu1", "backlog", Noon, worktree: "scheduled-runs-sweep-verdicts-cee60c-30bd77cc", sessions: [session]);
        var dashboard = Run("run-muvhm1s7-ymsz33", "delivery-surface-dashboard", Noon.AddMilliseconds(471), worktree: "Backlog-43b9057e", sessions: [session]);

        var merged = Assert.Single(DeliveryRunMerging.Merge([backlog, dashboard]));

        // The dashboard is still the record, as for any pair.
        Assert.Equal("run-muvhm1s7-ymsz33", merged.Id);
        Assert.Equal(["delivery-surface-dashboard", "backlog"], merged.Surfaces);

        // But the folder is the worktree's, the one the caller named: filed under the
        // main checkout, the run would leave its worktree's list_runs and be shown
        // under the repository's name instead of the worktree's.
        Assert.Equal("scheduled-runs-sweep-verdicts-cee60c-30bd77cc", merged.Worktree);
        Assert.Equal("scheduled-runs-sweep-verdicts-cee60c", merged.WorktreeName);

        // And either surface's id still finds it.
        Assert.Equal(["run-muvhm1s7-ymsz33", "run-muvhm1f3-5a1pu1"], merged.RunIds);
    }

    [Fact]
    public void Different_folders_pair_only_on_a_shared_session()
    {
        var dashboard = Run("run-1", "delivery-surface-dashboard", Noon, worktree: "Backlog-43b9057e");
        var stranger = Run("run-2", "backlog", Noon, sessions: ["78bbbe21-eadf-4bbb-9835-6ac01fcb924d"]);

        Assert.Equal(2, DeliveryRunMerging.Merge([dashboard, stranger]).Count);

        // Two files naming no session agree on the same folder, where the folder and
        // the window are the evidence; across two folders nothing would be left of it.
        var olderDashboard = Run("run-3", "orch-dashboard", Noon, worktree: "Backlog-43b9057e", sessions: []);
        var olderBacklog = Run("run-4", "backlog", Noon, sessions: []);

        Assert.Equal(2, DeliveryRunMerging.Merge([olderDashboard, olderBacklog]).Count);
    }

    [Fact]
    public void A_partner_in_the_same_folder_is_taken_before_one_in_another()
    {
        // Nearer in time is not nearer in fact: a file in the run's own folder is the
        // better evidence, whatever a millisecond says.
        var dashboard = Run("run-d", "delivery-surface-dashboard", Noon);
        var sameFolder = Run("run-same", "backlog", Noon.AddSeconds(20));
        var otherFolder = Run("run-other", "backlog", Noon.AddSeconds(1), worktree: "Backlog-43b9057e");

        var merged = DeliveryRunMerging.Merge([dashboard, sameFolder, otherFolder]);

        Assert.Equal(2, merged.Count);
        Assert.Equal(["run-d", "run-same"], Assert.Single(merged, run => run.Surfaces.Count == 2).RunIds);
        Assert.Same(otherFolder, Assert.Single(merged, run => run.Surfaces.Count == 1));
    }

    [Fact]
    public void Starts_further_apart_than_the_window_are_two_runs()
    {
        var dashboard = Run("run-1", "delivery-surface-dashboard", Noon);
        var inside = Run("run-2", "backlog", Noon + DeliveryRunMerging.Window);
        var outside = Run("run-3", "backlog", Noon + DeliveryRunMerging.Window + TimeSpan.FromSeconds(1));

        Assert.Single(DeliveryRunMerging.Merge([dashboard, inside]));
        Assert.Equal(2, DeliveryRunMerging.Merge([dashboard, outside]).Count);
    }

    [Fact]
    public void An_undated_start_never_pairs()
    {
        var dashboard = Run("run-1", "delivery-surface-dashboard", Noon);
        var undated = Run("run-2", "backlog", null);

        Assert.Equal(2, DeliveryRunMerging.Merge([dashboard, undated]).Count);
    }

    [Fact]
    public void Session_ids_must_agree_and_none_on_either_side_is_agreement()
    {
        var dashboard = Run("run-1", "delivery-surface-dashboard", Noon);
        var another = Run("run-2", "backlog", Noon, sessions: ["78bbbe21-eadf-4bbb-9835-6ac01fcb924d"]);
        var resumed = Run("run-3", "backlog", Noon, sessions: ["78bbbe21-eadf-4bbb-9835-6ac01fcb924d", Session]);

        Assert.Equal(2, DeliveryRunMerging.Merge([dashboard, another]).Count);
        Assert.Single(DeliveryRunMerging.Merge([dashboard, resumed]));

        // Every dashboard file written before the surfaces took a session id names
        // none, and two such files still pair on the rest of the rule.
        var olderDashboard = Run("run-4", "orch-dashboard", Noon, sessions: []);
        var olderBacklog = Run("run-5", "backlog", Noon, sessions: []);

        Assert.Single(DeliveryRunMerging.Merge([olderDashboard, olderBacklog]));

        // But a file that names a session and one that names none are not known to
        // be the same run.
        Assert.Equal(2, DeliveryRunMerging.Merge([dashboard, olderBacklog]).Count);
    }

    [Fact]
    public void A_partner_that_never_arrived_does_not_take_the_next_runs_file()
    {
        // The dashboard surface was up for the first run and the backlog surface was
        // not; an hour later both recorded the second. Without the window the first
        // run's dashboard file would pair with the second run's backlog file.
        var firstDashboard = Run("run-1d", "delivery-surface-dashboard", Noon);
        var secondBacklog = Run("run-2b", "backlog", Noon.AddHours(1));
        var secondDashboard = Run("run-2d", "delivery-surface-dashboard", Noon.AddHours(1).AddSeconds(2));

        var merged = DeliveryRunMerging.Merge([firstDashboard, secondBacklog, secondDashboard]);

        Assert.Equal(2, merged.Count);
        Assert.Same(firstDashboard, merged[0]);
        Assert.Equal("run-2d", merged[1].Id);
        Assert.Equal(["delivery-surface-dashboard", "backlog"], merged[1].Surfaces);
    }

    [Fact]
    public void The_nearest_start_is_the_partner_taken()
    {
        var dashboard = Run("run-d", "delivery-surface-dashboard", Noon);
        var far = Run("run-far", "backlog", Noon.AddMinutes(4));
        var near = Run("run-near", "backlog", Noon.AddSeconds(10));

        var merged = DeliveryRunMerging.Merge([far, dashboard, near]);

        Assert.Equal(2, merged.Count);

        var pair = Assert.Single(merged, run => run.Surfaces.Count == 2);

        Assert.Equal("run-d", pair.Id);
        Assert.Equal([Session], pair.SessionIds);
        Assert.Same(far, Assert.Single(merged, run => run.Surfaces.Count == 1));
    }

    [Fact]
    public void A_merged_run_sits_where_its_record_did()
    {
        var older = Run("run-old", "orch-dashboard", Noon.AddDays(-1), skill: "orch-feature");
        var backlog = Run("run-b", "backlog", Noon);
        var dashboard = Run("run-d", "delivery-surface-dashboard", Noon.AddSeconds(1));
        var newer = Run("run-new", "backlog", Noon.AddHours(2), skill: "flow-spec");

        var merged = DeliveryRunMerging.Merge([older, backlog, dashboard, newer]);

        Assert.Equal(["run-old", "run-d", "run-new"], merged.Select(run => run.Id));
    }

    private static DeliveryRun Run(
        string id,
        string dashboard,
        DateTimeOffset? startedAt,
        string skill = "flow-code",
        string worktree = Worktree,
        string status = "done",
        IReadOnlyList<string>? sessions = null) =>
        SessionRowsTests.Run(id, worktree, startedAt, (startedAt ?? Noon).AddMinutes(10), status) with
        {
            Dashboard = dashboard,
            SkillId = skill,
            ChangeKind = null,
            SessionIds = sessions ?? [Session]
        };
}
