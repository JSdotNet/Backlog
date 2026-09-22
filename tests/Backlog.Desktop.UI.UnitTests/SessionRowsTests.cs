namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// How the two readings become one list. The key is the worktree, derived from a
/// session's folder the way the dashboards derive it from theirs; the window is the
/// condition and the tiebreak; and a run nothing claims is a row, not a loss. Each
/// test here is one way of passing one part of that and failing another, because a
/// rule with two parts is wrong exactly when it quietly needs only one of them.
/// </summary>
public sealed class SessionRowsTests
{
    private static readonly DateTimeOffset Noon = new(2026, 9, 3, 12, 0, 0, TimeSpan.Zero);

    private const string Folder = @"D:\Repos\Backlog\.claude\worktrees\keen-bose-667825";

    /// <summary>The key the dashboards would file that folder under — checked
    /// against a real profile, where <c>D:\Repos\Backlog</c> is
    /// <c>Backlog-43b9057e</c>.</summary>
    private static readonly string Worktree = DeliveryRunWorktrees.KeyOf(Folder)!;

    [Fact]
    public void The_worktree_key_is_the_dashboards_own()
    {
        Assert.Equal("Backlog-43b9057e", DeliveryRunWorktrees.KeyOf(@"D:\Repos\Backlog"));

        // A trailing separator is the same folder. So is another letter case, and
        // the hash says so — the dashboards lower-case before hashing — while the
        // slug keeps the case it was given, as the dashboards keep git's. The
        // matching compares keys case-blind for exactly that reason.
        Assert.Equal("Backlog-43b9057e", DeliveryRunWorktrees.KeyOf(@"D:\Repos\Backlog\"));
        Assert.Equal("backlog-43b9057e", DeliveryRunWorktrees.KeyOf(@"d:\repos\backlog"));

        // The slug keeps the leaf's own case and replaces what a folder name may not
        // safely carry; the hash is over the whole path.
        Assert.StartsWith("keen-bose-667825-", Worktree);
        Assert.Equal(8, Worktree.Length - "keen-bose-667825-".Length);
        Assert.Null(DeliveryRunWorktrees.KeyOf(""));
        Assert.Null(DeliveryRunWorktrees.KeyOf(null));
    }

    [Fact]
    public void The_keys_up_a_folder_walk_to_the_root_nearest_first()
    {
        var keys = DeliveryRunWorktrees.KeysUpFrom(@"D:\Repos\Backlog\src\Modules").ToList();

        Assert.Equal(DeliveryRunWorktrees.KeyOf(@"D:\Repos\Backlog\src\Modules"), keys[0]);
        Assert.Equal(DeliveryRunWorktrees.KeyOf(@"D:\Repos\Backlog\src"), keys[1]);
        Assert.Equal("Backlog-43b9057e", keys[2]);
        Assert.Contains(DeliveryRunWorktrees.KeyOf(@"D:\Repos"), keys);
    }

    [Fact]
    public void A_run_joins_the_session_in_its_worktree_whose_window_it_overlaps()
    {
        var session = Session("6dd303c0", Folder, startedAt: Noon.AddHours(-3), lastActivity: Noon.AddMinutes(-5));
        var run = Run("run-1", Worktree, startedAt: Noon.AddHours(-2), updatedAt: Noon.AddMinutes(-10));

        var rows = SessionRows.Of([session], [run]);

        var row = Assert.Single(rows);

        Assert.Same(session, row.Session);
        Assert.Equal([run], row.Runs);
        Assert.False(row.RunOnly);
    }

    [Fact]
    public void A_folder_in_another_letter_case_is_the_same_worktree()
    {
        var session = Session("6dd303c0", Folder.ToLowerInvariant(), startedAt: Noon.AddHours(-3), lastActivity: Noon.AddMinutes(-5));
        var run = Run("run-1", Worktree, startedAt: Noon.AddHours(-2), updatedAt: Noon.AddMinutes(-10));

        var row = Assert.Single(SessionRows.Of([session], [run]));

        Assert.Equal([run], row.Runs);
    }

    /// <summary>A session started below the worktree's top level ran in that
    /// worktree, and the dashboard keyed its runs on the top level.</summary>
    [Fact]
    public void A_session_started_in_a_subfolder_still_claims_its_worktrees_runs()
    {
        var session = Session("6dd303c0", Folder + @"\src\Modules", startedAt: Noon.AddHours(-3), lastActivity: Noon.AddMinutes(-5));
        var run = Run("run-1", Worktree, startedAt: Noon.AddHours(-2), updatedAt: Noon.AddMinutes(-10));

        var row = Assert.Single(SessionRows.Of([session], [run]));

        Assert.Equal([run], row.Runs);
    }

    [Fact]
    public void A_run_in_another_worktree_is_not_the_sessions_whatever_the_window_says()
    {
        var session = Session("6dd303c0", Folder, startedAt: Noon.AddHours(-3), lastActivity: Noon.AddMinutes(-5));
        var run = Run("run-1", "other-worktree-1a2b3c4d", startedAt: Noon.AddHours(-2), updatedAt: Noon.AddMinutes(-10));

        var rows = SessionRows.Of([session], [run]);

        Assert.Equal(2, rows.Count);
        Assert.Empty(rows[0].Runs);

        var alone = rows[1];
        Assert.True(alone.RunOnly);
        Assert.Equal([run], alone.Runs);
    }

    /// <summary>
    /// A worktree is worked in by several sessions over its life, so the place alone
    /// names too much: a session that ended before the run began did not run it, and
    /// the run is a row of its own rather than a wrong attribution.
    /// </summary>
    [Fact]
    public void A_session_in_the_worktree_that_ended_before_the_run_began_does_not_claim_it()
    {
        var earlier = Session("6dd303c0", Folder, startedAt: Noon.AddDays(-2), lastActivity: Noon.AddDays(-2).AddHours(1));
        var run = Run("run-1", Worktree, startedAt: Noon.AddHours(-2), updatedAt: Noon.AddMinutes(-10));

        var rows = SessionRows.Of([earlier], [run]);

        Assert.Empty(rows[0].Runs);
        Assert.True(rows[1].RunOnly);
    }

    /// <summary>
    /// Two sessions were in the worktree while the run was open — the one that
    /// started it, and one that resumed it after a handoff. The run files under the
    /// one whose start is nearest its own, which is the session that opened it.
    /// </summary>
    [Fact]
    public void Of_two_overlapping_sessions_the_one_that_started_with_the_run_wins()
    {
        var opener = Session("aaaa", Folder, startedAt: Noon.AddHours(-2).AddMinutes(-1), lastActivity: Noon.AddHours(-1));
        var resumer = Session("bbbb", Folder, startedAt: Noon.AddMinutes(-50), lastActivity: Noon.AddMinutes(-5));
        var run = Run("run-1", Worktree, startedAt: Noon.AddHours(-2), updatedAt: Noon.AddMinutes(-10));

        var rows = SessionRows.Of([resumer, opener], [run]);

        Assert.Empty(rows[0].Runs);
        Assert.Equal([run], rows[1].Runs);
    }

    [Fact]
    public void A_run_without_a_recorded_start_is_placed_by_its_last_update()
    {
        var current = Session("aaaa", Folder, startedAt: Noon.AddMinutes(-30), lastActivity: Noon.AddMinutes(-1));
        var run = Run("run-1", Worktree, startedAt: null, updatedAt: Noon.AddMinutes(-10));

        var row = Assert.Single(SessionRows.Of([current], [run]));

        Assert.Equal([run], row.Runs);
    }

    /// <summary>
    /// The order the list is built in: every session in the order given, then the
    /// runs nothing claimed, most recently updated first. A session's own runs are
    /// most recently updated first too. The grouping sorts rows afterwards; this
    /// is what it sorts.
    /// </summary>
    [Fact]
    public void Sessions_keep_their_order_and_stray_runs_follow_most_recent_first()
    {
        var session = Session("aaaa", Folder, startedAt: Noon.AddDays(-1), lastActivity: Noon);
        var older = Run("older", Worktree, startedAt: Noon.AddHours(-20), updatedAt: Noon.AddHours(-18));
        var newer = Run("newer", Worktree, startedAt: Noon.AddHours(-3), updatedAt: Noon.AddHours(-1));
        var strayOld = Run("stray-old", "elsewhere-00000000", startedAt: Noon.AddHours(-9), updatedAt: Noon.AddHours(-8));
        var strayNew = Run("stray-new", "elsewhere-00000000", startedAt: Noon.AddHours(-4), updatedAt: Noon.AddHours(-2));

        var rows = SessionRows.Of([session], [older, strayOld, newer, strayNew]);

        Assert.Equal(["aaaa", "stray-new", "stray-old"], rows.Select(row => row.Id));
        Assert.Equal(["newer", "older"], rows[0].Runs.Select(run => run.Id));
    }

    /// <summary>
    /// What a row the run alone accounts for says about itself: the facts the run
    /// file carries, and nothing it does not. Finished, because there is no
    /// liveness evidence — a session still running would be in the reading.
    /// </summary>
    [Fact]
    public void A_run_only_row_speaks_with_the_runs_own_facts()
    {
        var run = Run("run-1", Worktree, startedAt: Noon.AddHours(-2), updatedAt: Noon.AddMinutes(-10), status: "in_progress") with
        {
            References = [new(DeliveryRunReferenceKind.Issue, "#211", null, null, "JSdotNet/Backlog")]
        };

        var row = Assert.Single(SessionRows.Of([], [run]));

        Assert.True(row.RunOnly);
        Assert.Equal("run/orch-dashboard/" + Worktree + "/run-1", row.Key);
        Assert.Equal("run-1", row.Id);
        Assert.Equal(AgentSessionKind.Claude, row.Kind);
        Assert.Equal("tower", row.EnvironmentId);
        Assert.Equal("DEV-TOWER", row.Environment);
        Assert.Equal("keen-bose-667825", row.Title);
        Assert.Equal(string.Empty, row.WorkingFolder);
        Assert.Equal("JSdotNet/Backlog", row.Repository);
        Assert.Null(row.Branch);
        Assert.Equal(Noon.AddHours(-2), row.StartedAt);
        Assert.Equal(Noon.AddMinutes(-10), row.LastActivityAt);
        Assert.Equal(AgentSessionState.Finished, row.State);
        Assert.Equal(AgentSessionOrigin.Local, row.Origin);
    }

    /// <summary>A session row speaks with the session's facts, and fills only the
    /// one gap a run can honestly fill: a repository the agent did not record but the
    /// run's tracker item names.</summary>
    [Fact]
    public void A_session_row_speaks_with_the_sessions_facts_and_the_runs_repository()
    {
        var session = Session("aaaa", Folder, startedAt: Noon.AddHours(-3), lastActivity: Noon.AddMinutes(-5));
        var run = Run("run-1", Worktree, startedAt: Noon.AddHours(-2), updatedAt: Noon.AddMinutes(-10)) with
        {
            References = [new(DeliveryRunReferenceKind.Issue, "#211", null, null, "JSdotNet/Backlog")]
        };

        var row = Assert.Single(SessionRows.Of([session], [run]));

        Assert.Equal("Claude/aaaa", row.Key);
        Assert.Equal(session.Title, row.Title);
        Assert.Equal(session.StartedAt, row.StartedAt);
        Assert.Equal(session.LastActivityAt, row.LastActivityAt);
        Assert.Equal(session.State, row.State);
        Assert.Equal("JSdotNet/Backlog", row.Repository);

        // And with no run behind it, the session's own null stands.
        Assert.Null(SessionRow.Of(session).Repository);
    }

    [Fact]
    public void No_runs_is_every_session_as_a_row_of_its_own()
    {
        var sessions = new[]
        {
            Session("aaaa", Folder, startedAt: Noon.AddHours(-1), lastActivity: Noon),
            Session("bbbb", string.Empty, startedAt: null, lastActivity: Noon)
        };

        var rows = SessionRows.Of(sessions, []);

        Assert.Equal(["aaaa", "bbbb"], rows.Select(row => row.Id));
        Assert.All(rows, row => Assert.Empty(row.Runs));
    }

    private static AgentSession Session(string id, string folder, DateTimeOffset? startedAt, DateTimeOffset lastActivity) =>
        new(
            Id: id,
            Kind: AgentSessionKind.Claude,
            EnvironmentId: "tower",
            Environment: "DEV-TOWER",
            Title: "keen-bose-667825",
            WorkingFolder: folder,
            Repository: null,
            Branch: null,
            StartedAt: startedAt,
            LastActivityAt: lastActivity,
            State: AgentSessionState.Running,
            TurnCount: null,
            Origin: AgentSessionOrigin.Local);

    internal static DeliveryRun Run(
        string id,
        string worktree,
        DateTimeOffset? startedAt,
        DateTimeOffset updatedAt,
        string status = "done",
        string title = "A run") =>
        new(
            Id: id,
            Dashboard: "orch-dashboard",
            Worktree: worktree,
            WorktreeName: System.Text.RegularExpressions.Regex.Replace(worktree, "-[0-9a-f]{8}$", string.Empty),
            EnvironmentId: "tower",
            Environment: "DEV-TOWER",
            SkillId: "orch-feature",
            Title: title,
            Status: status,
            ChangeKind: "new-functionality",
            References: [],
            StartedAt: startedAt,
            UpdatedAt: updatedAt,
            Stages: [],
            TokenUsage: null,
            Context: null,
            InsightsByCategory: [],
            InsightsByServer: []);
}
