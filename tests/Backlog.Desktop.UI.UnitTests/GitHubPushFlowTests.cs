using Backlog.Modules.Tasks;
using Backlog.Modules.Tasks.DomainModels;
using Backlog.Infrastructure.FileSystem;
using Backlog.Infrastructure.GitHub;
using Backlog.Modules.Tasks.Abstractions.Services;
using Microsoft.Extensions.Time.Testing;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// Pushing an entry and watching what happens to it, driven through the list
/// exactly as the screen does — with GitHub itself stubbed out, so the test is
/// about the wiring rather than the network.
/// </summary>
[Collection(WorkspaceSettingsCollection.Name)]
public sealed class GitHubPushFlowTests : IDisposable
{
    private readonly List<string> _tempDirs = [];
    private readonly List<TasksDesktopState> _states = [];

    public void Dispose()
    {
        // Before the folders below go: the state arms timed saves, and one that
        // elapsed after its folder was deleted is work the test host is still
        // holding when the run is over. See TasksDesktopStateLifetimeTests.
        foreach (var state in _states)
        {
            state.Dispose();
        }

        foreach (var dir in _tempDirs.Where(Directory.Exists))
        {
            TestDatabases.Release(dir);
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task An_entry_targeting_a_configured_repository_can_be_pushed()
    {
        var harness = Build("JSdotNet/Backlog");

        var row = await WriteEntryAsync(harness.State, "# Add GitHub support\n`task` `*high` `!draft` `repo:backlog`\n\nDetails here.");

        Assert.NotNull(harness.State.RepositoryFor(row));

        await harness.State.PushToGitHubAsync(row);

        Assert.Null(row.GitHubError);
        Assert.Equal(101, row.IssueLink!.IssueNumber);
        Assert.Equal("JSdotNet/Backlog", row.IssueLink.RepoFullName);
        Assert.Equal("Add GitHub support", harness.Client.CreatedTitle);
        Assert.Contains("Details here.", harness.Client.CreatedBody);
    }

    [Fact]
    public async Task An_entry_targeting_no_configured_repository_does_not_push_silently()
    {
        var harness = Build("JSdotNet/Backlog");

        var row = await WriteEntryAsync(harness.State, "# Buy milk\n`task` `*low` `!draft` `@errands`\n");

        Assert.Null(harness.State.RepositoryFor(row));

        await harness.State.PushToGitHubAsync(row);

        Assert.Null(row.IssueLink);
        Assert.Equal(0, harness.Client.CreateCount);
    }

    [Fact]
    public async Task The_link_survives_a_reload_of_the_markdown_file()
    {
        var harness = Build("JSdotNet/Backlog");

        var row = await WriteEntryAsync(harness.State, "# Add GitHub support\n`task` `*high` `!draft` `repo:backlog`\n");
        await harness.State.PushToGitHubAsync(row);

        var reloaded = StateFor(harness.Store, harness.Integration);
        await reloaded.InitializeAsync();

        var reloadedRow = Assert.Single(reloaded.Rows);
        Assert.Equal(101, reloadedRow.IssueLink!.IssueNumber);
    }

    [Fact]
    public async Task An_entry_is_never_pushed_twice()
    {
        var harness = Build("JSdotNet/Backlog");

        var row = await WriteEntryAsync(harness.State, "# Add GitHub support\n`task` `*high` `!draft` `repo:backlog`\n");
        await harness.State.PushToGitHubAsync(row);
        harness.Client.CreateCount = 0;

        await harness.State.PushToGitHubAsync(row);

        Assert.Equal(0, harness.Client.CreateCount);
    }

    [Fact]
    public async Task A_refusal_from_github_is_reported_on_the_entry_not_swallowed()
    {
        var harness = Build("JSdotNet/Backlog");
        harness.Client.Failure = new GitHubException("GitHub rejected the token — check it hasn't expired.");

        var row = await WriteEntryAsync(harness.State, "# Add GitHub support\n`task` `*high` `!draft` `repo:backlog`\n");
        await harness.State.PushToGitHubAsync(row);

        Assert.Null(row.IssueLink);
        Assert.Equal("GitHub rejected the token — check it hasn't expired.", row.GitHubError);
    }

    [Fact]
    public async Task Syncing_reads_back_the_issue_and_its_pull_request()
    {
        var harness = Build("JSdotNet/Backlog");

        var row = await WriteEntryAsync(harness.State, "# Add GitHub support\n`task` `*high` `!draft` `repo:backlog`\n");
        await harness.State.PushToGitHubAsync(row);

        harness.Client.Snapshot = new GitHubIssueSnapshot(
            new GitHubIssue(101, "https://github.com/JSdotNet/Backlog/issues/101", "Add GitHub support", GitHubItemState.Closed, null),
            [new GitHubPullRequest(102, "https://github.com/JSdotNet/Backlog/pull/102", "Add GitHub support", GitHubItemState.Merged, "JSdotNet/Backlog")],
            DateTimeOffset.UtcNow);

        await harness.State.SyncGitHubAsync();

        Assert.Equal(GitHubItemState.Closed, row.Snapshot!.Issue.State);
        Assert.Equal(GitHubItemState.Merged, row.Snapshot.Headline!.State);
    }

    /// <summary>A pull request recorded by <c>link_change</c> is read too — on an
    /// entry never filed as an issue, which is the usual case for work an agent
    /// picked up from the backlog.</summary>
    [Fact]
    public async Task Syncing_reads_whether_a_recorded_pull_request_merged()
    {
        var harness = Build("JSdotNet/Backlog");

        var row = await WriteEntryAsync(harness.State, "# Add GitHub support\n`task` `*high` `!done` `repo:backlog`\n");
        var merged = new EntryPullRequestLink("JSdotNet/Backlog", 708);
        var open = new EntryPullRequestLink("JSdotNet/Backlog", 710);
        row.PullRequestLinks = [merged, open];
        Assert.True(harness.State.HasLinkedRows);
        harness.Client.PullRequestStates[708] = GitHubItemState.Merged;
        harness.Client.PullRequestStates[710] = GitHubItemState.Open;

        await harness.State.SyncGitHubAsync();

        Assert.Null(row.IssueLink);
        Assert.Equal(GitHubItemState.Merged, row.PullRequestStates[merged]);
        Assert.Equal(GitHubItemState.Open, row.PullRequestStates[open]);
    }

    /// <summary>
    /// A reload replaces every row, and it is set off by writes the reader never
    /// sees — an agent recording its work through the MCP server, a sync pull. What
    /// the last sync read is carried onto the new row, so a merged pull request does
    /// not go back to looking unread the moment somebody else writes.
    /// </summary>
    [Fact]
    public async Task What_a_sync_read_survives_a_reload_from_the_store()
    {
        var harness = Build("JSdotNet/Backlog");

        var written = await WriteEntryAsync(harness.State, "# Add GitHub support\n`task` `!done` `repo:backlog`\n");
        await harness.State.PushToGitHubAsync(written);
        var entries = TasksTestHost.EntriesFor(harness.Store);
        await entries.LinkToIssueAsync(written.Id!.Value, "JSdotNet/Backlog", "708", EntryProjectionDto.PullRequestTargetType, TestContext.Current.CancellationToken);
        await harness.State.ReloadFromStoreAsync();

        var pr = new EntryPullRequestLink("JSdotNet/Backlog", 708);
        harness.Client.PullRequestStates[708] = GitHubItemState.Merged;
        await harness.State.SyncGitHubAsync();

        await entries.LinkToIssueAsync(written.Id!.Value, "JSdotNet/Backlog", "710", EntryProjectionDto.PullRequestTargetType, TestContext.Current.CancellationToken);
        await harness.State.ReloadFromStoreAsync();

        var row = Assert.Single(harness.State.Rows);
        Assert.NotSame(written, row);
        Assert.Equal(GitHubItemState.Merged, row.PullRequestStates[pr]);
        Assert.Equal(GitHubItemState.Merged, row.PullRequestStatuses[pr].State);
        Assert.NotNull(row.Snapshot);
        Assert.False(row.PullRequestStates.ContainsKey(new EntryPullRequestLink("JSdotNet/Backlog", 710)));
    }

    /// <summary>A pull request GitHub will not answer for is left unread rather
    /// than failing the issue it sits beside, and says nothing out loud: the issue
    /// is what the entry was filed as, the pull request a detail of its work.</summary>
    [Fact]
    public async Task A_pull_request_that_cannot_be_read_stays_unread_without_failing_the_issue()
    {
        var harness = Build("JSdotNet/Backlog");

        var row = await WriteEntryAsync(harness.State, "# Add GitHub support\n`task` `*high` `!draft` `repo:backlog`\n");
        await harness.State.PushToGitHubAsync(row);
        var pr = new EntryPullRequestLink("JSdotNet/Backlog", 708);
        row.PullRequestLinks = [pr];
        harness.Client.PullRequestFailure = new GitHubException("Not Found");

        await harness.State.RefreshGitHubAsync(row);

        Assert.Null(row.GitHubError);
        Assert.NotNull(row.Snapshot);
        Assert.False(row.PullRequestStates.ContainsKey(pr));
    }

    /// <summary>
    /// The list on screen asks GitHub about the pull requests it has never read, on
    /// its own, once it is already showing — so a Done entry whose work merged says
    /// so without anybody pressing the sync button.
    /// </summary>
    [Fact]
    public async Task A_done_entrys_unread_pull_request_is_read_without_a_sync()
    {
        var harness = Build("JSdotNet/Backlog");

        var row = await WriteEntryAsync(harness.State, "# Add GitHub support\n`task` `!done` `repo:backlog`\n");
        var pr = new EntryPullRequestLink("JSdotNet/Backlog", 708);
        row.PullRequestLinks = [pr];
        harness.Client.PullRequestStates[708] = GitHubItemState.Merged;

        await harness.State.CheckPullRequestsAsync();

        Assert.Equal(GitHubItemState.Merged, row.PullRequestStates[pr]);
        Assert.Equal(GitHubItemState.Merged, row.PullRequestStatuses[pr].State);
    }

    /// <summary>
    /// A pull request read while it was still open is asked about again once the
    /// recheck interval has passed — the entry went Done when it was recorded, and
    /// its merge comes after. Not sooner: every write somebody else makes reloads
    /// the list, and asking on each one would put the network behind all of them.
    /// </summary>
    [Fact]
    public async Task An_open_pull_request_is_read_again_once_the_recheck_interval_passes()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var harness = Build(clock, "JSdotNet/Backlog");

        var row = await WriteEntryAsync(harness.State, "# Add GitHub support\n`task` `!done` `repo:backlog`\n");
        var pr = new EntryPullRequestLink("JSdotNet/Backlog", 708);
        row.PullRequestLinks = [pr];
        harness.Client.PullRequestStates[708] = GitHubItemState.Open;

        await harness.State.CheckPullRequestsAsync();
        Assert.Equal(GitHubItemState.Open, row.PullRequestStates[pr]);

        harness.Client.PullRequestStates[708] = GitHubItemState.Merged;
        clock.Advance(TasksDesktopState.PullRequestRecheckInterval - TimeSpan.FromSeconds(1));
        await harness.State.CheckPullRequestsAsync();

        Assert.Equal(1, harness.Client.StatusReads);
        Assert.Equal(GitHubItemState.Open, row.PullRequestStates[pr]);

        clock.Advance(TimeSpan.FromSeconds(1));
        await harness.State.CheckPullRequestsAsync();

        Assert.Equal(2, harness.Client.StatusReads);
        Assert.Equal(GitHubItemState.Merged, row.PullRequestStates[pr]);
    }

    /// <summary>An open state that came across a reload is still re-read: the
    /// reload carries the last read, not a reason to stop asking.</summary>
    [Fact]
    public async Task An_open_pull_request_carried_across_a_reload_is_still_read_again()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var harness = Build(clock, "JSdotNet/Backlog");

        var written = await WriteEntryAsync(harness.State, "# Add GitHub support\n`task` `!done` `repo:backlog`\n");
        var entries = TasksTestHost.EntriesFor(harness.Store);
        await entries.LinkToIssueAsync(written.Id!.Value, "JSdotNet/Backlog", "708", EntryProjectionDto.PullRequestTargetType, TestContext.Current.CancellationToken);
        await harness.State.ReloadFromStoreAsync();
        harness.Client.PullRequestStates[708] = GitHubItemState.Open;

        await harness.State.CheckPullRequestsAsync();

        harness.Client.PullRequestStates[708] = GitHubItemState.Merged;
        clock.Advance(TasksDesktopState.PullRequestRecheckInterval);
        await harness.State.ReloadFromStoreAsync();
        await harness.State.CheckPullRequestsAsync();

        var row = harness.State.Rows.Single(r => r.Id == written.Id);
        Assert.Equal(GitHubItemState.Merged, row.PullRequestStates.Values.Single());
    }

    /// <summary>Merged and closed are where a pull request ends: the check does not
    /// ask about one again, however long it has been. The sync button still does.</summary>
    [Fact]
    public async Task A_merged_or_closed_pull_request_is_not_read_again_by_the_check()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var harness = Build(clock, "JSdotNet/Backlog");

        var row = await WriteEntryAsync(harness.State, "# Add GitHub support\n`task` `!done` `repo:backlog`\n");
        var merged = new EntryPullRequestLink("JSdotNet/Backlog", 708);
        var closed = new EntryPullRequestLink("JSdotNet/Backlog", 710);
        row.PullRequestLinks = [merged, closed];
        row.PullRequestStates = new Dictionary<EntryPullRequestLink, GitHubItemState>
        {
            [merged] = GitHubItemState.Merged,
            [closed] = GitHubItemState.Closed
        };

        clock.Advance(TasksDesktopState.PullRequestRecheckInterval * 3);
        await harness.State.CheckPullRequestsAsync();

        Assert.Equal(0, harness.Client.StatusReads);
        Assert.Equal(0, harness.Client.StateReads);
    }

    /// <summary>The check asks about an entry's pull request whatever the entry's
    /// status: a plan item's pull request merges into its plan branch while the
    /// item stays In progress until the plan lands, and its merge is still owed
    /// the merged colour.</summary>
    [Fact]
    public async Task An_in_progress_entrys_pull_request_is_read_without_a_sync()
    {
        var harness = Build("JSdotNet/Backlog");

        var row = await WriteEntryAsync(harness.State, "# Add GitHub support\n`task` `!in-progress` `repo:backlog`\n");
        var pr = new EntryPullRequestLink("JSdotNet/Backlog", 708);
        row.PullRequestLinks = [pr];
        harness.Client.PullRequestStates[708] = GitHubItemState.Merged;

        await harness.State.CheckPullRequestsAsync();

        Assert.Equal(EntryStatus.InProgress, row.Status);
        Assert.Equal(GitHubItemState.Merged, row.PullRequestStates[pr]);
    }

    // --- A merge that lands finishes a prompt entry --------------------------

    /// <summary>
    /// The policy PR #1428 set: a prompt entry is done when its pull request merges
    /// into the repository's default branch. The background check that colours the
    /// link is the read that says so, and the entry moves to Done on it — the status
    /// alone: its step keeps its own and the entry is not ticked.
    /// </summary>
    [Fact]
    public async Task A_prompt_in_progress_goes_done_when_its_pull_request_merges_into_the_default_branch()
    {
        var harness = Build("JSdotNet/Backlog");
        var row = await LinkedEntryAsync(harness, "`prompt` `!in-progress` `repo:backlog`", 708);
        harness.Client.PullRequestStates[708] = GitHubItemState.Merged;
        harness.Client.PullRequestBases[708] = "main";

        await harness.State.CheckPullRequestsAsync();

        var moved = harness.State.Rows.Single(r => r.Id == row.Id);
        Assert.Equal(EntryStatus.Done, moved.Status);
        Assert.Contains("## Step one\n`task` `!in-progress`", moved.RawText, StringComparison.Ordinal);
        Assert.Null(moved.CompletedOn);
        Assert.Empty(harness.Toasts.Visible);
    }

    [Fact]
    public async Task A_prompt_in_progress_goes_done_when_its_pull_request_merges_into_a_plan_branch()
    {
        var harness = Build("JSdotNet/Backlog");
        var row = await LinkedEntryAsync(harness, "`prompt` `!in-progress` `repo:backlog`", 1453);
        harness.Client.PullRequestStates[1453] = GitHubItemState.Merged;
        harness.Client.PullRequestBases[1453] = "plan/devbook-domain-view";

        await harness.State.CheckPullRequestsAsync();

        Assert.Equal(EntryStatus.Done, harness.State.Rows.Single(r => r.Id == row.Id).Status);
    }

    /// <summary>What the move leaves alone: a merge into a stacked parent's branch, a
    /// pull request still open or closed unmerged, a base the read did not name, a
    /// task or test the person ticks, and a Ready entry — the lifecycle graph has no
    /// edge from Ready to Done.</summary>
    [Theory]
    [InlineData("`prompt` `!in-progress` `repo:backlog`", GitHubItemState.Merged, "claude/parent", EntryStatus.InProgress)]
    [InlineData("`prompt` `!in-progress` `repo:backlog`", GitHubItemState.Open, "main", EntryStatus.InProgress)]
    [InlineData("`prompt` `!in-progress` `repo:backlog`", GitHubItemState.Closed, "main", EntryStatus.InProgress)]
    [InlineData("`prompt` `!in-progress` `repo:backlog`", GitHubItemState.Merged, null, EntryStatus.InProgress)]
    [InlineData("`task` `!in-progress` `repo:backlog`", GitHubItemState.Merged, "main", EntryStatus.InProgress)]
    [InlineData("`test` `!in-progress` `repo:backlog`", GitHubItemState.Merged, "main", EntryStatus.InProgress)]
    [InlineData("`prompt` `!ready` `repo:backlog`", GitHubItemState.Merged, "main", EntryStatus.Ready)]
    public async Task A_read_that_does_not_land_the_work_leaves_the_entry_where_it_is(
        string metadata, GitHubItemState state, string? baseRef, EntryStatus expected)
    {
        var harness = Build("JSdotNet/Backlog");
        var row = await LinkedEntryAsync(harness, metadata, 708);
        harness.Client.PullRequestStates[708] = state;
        if (baseRef is not null) harness.Client.PullRequestBases[708] = baseRef;

        await harness.State.CheckPullRequestsAsync();

        var read = harness.State.Rows.Single(r => r.Id == row.Id);
        Assert.Equal(state, read.PullRequestStates.Values.Single());
        Assert.Equal(expected, read.Status);
    }

    [Fact]
    public async Task An_unknown_default_branch_leaves_the_entry_where_it_is()
    {
        var harness = Build("JSdotNet/Backlog");
        var row = await LinkedEntryAsync(harness, "`prompt` `!in-progress` `repo:backlog`", 708);
        harness.Client.PullRequestStates[708] = GitHubItemState.Merged;
        harness.Client.PullRequestBases[708] = "main";
        harness.Client.DefaultBranch = null;

        await harness.State.CheckPullRequestsAsync();

        Assert.Equal(EntryStatus.InProgress, harness.State.Rows.Single(r => r.Id == row.Id).Status);
    }

    /// <summary>A server that cannot answer the status query still says where the
    /// pull request merged over REST, so the fallback moves the entry too.</summary>
    [Fact]
    public async Task The_state_alone_fallback_still_finishes_the_entry()
    {
        var harness = Build("JSdotNet/Backlog");
        var row = await LinkedEntryAsync(harness, "`prompt` `!in-progress` `repo:backlog`", 708);
        harness.Client.PullRequestStates[708] = GitHubItemState.Merged;
        harness.Client.PullRequestBases[708] = "main";
        harness.Client.StatusFailure = new GitHubException("Field 'autoMergeRequest' doesn't exist on type 'PullRequest'");

        await harness.State.CheckPullRequestsAsync();

        Assert.Equal(1, harness.Client.StateReads);
        Assert.Equal(EntryStatus.Done, harness.State.Rows.Single(r => r.Id == row.Id).Status);
    }

    /// <summary>The move happens once. A merged pull request is not asked about
    /// again by the check, and a person who takes the entry back to In progress
    /// keeps it there for the rest of the run — through a reload and the sync
    /// button's re-read alike.</summary>
    [Fact]
    public async Task An_entry_reopened_after_its_merge_moved_it_is_not_moved_again()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var harness = Build(clock, "JSdotNet/Backlog");
        var row = await LinkedEntryAsync(harness, "`prompt` `!in-progress` `repo:backlog`", 708);
        harness.Client.PullRequestStates[708] = GitHubItemState.Merged;
        harness.Client.PullRequestBases[708] = "main";

        await harness.State.CheckPullRequestsAsync();
        var moved = harness.State.Rows.Single(r => r.Id == row.Id);
        Assert.Equal(EntryStatus.Done, moved.Status);

        await harness.State.ChangeStatusAsync(moved, EntryStatus.InProgress);
        clock.Advance(TasksDesktopState.PullRequestRecheckInterval * 2);
        await harness.State.CheckPullRequestsAsync();
        Assert.Equal(1, harness.Client.StatusReads);

        await harness.State.ReloadFromStoreAsync();
        await harness.State.SyncGitHubAsync();

        Assert.Equal(EntryStatus.InProgress, harness.State.Rows.Single(r => r.Id == row.Id).Status);
    }

    /// <summary>
    /// A reload that lands while the read is out replaces every row — set off here by
    /// an agent writing to the same entry. The move acts on the row the list holds
    /// now, so the agent's write survives and the entry on screen is the one at Done.
    /// </summary>
    [Fact]
    public async Task A_reload_between_the_read_and_the_move_keeps_the_write_that_set_it_off()
    {
        var harness = Build("JSdotNet/Backlog");
        var row = await LinkedEntryAsync(harness, "`prompt` `!in-progress` `repo:backlog`", 708);
        var id = row.Id!.Value;
        harness.Client.PullRequestStates[708] = GitHubItemState.Merged;
        harness.Client.PullRequestBases[708] = "main";
        var gate = new TaskCompletionSource();
        harness.Client.StatusGates[708] = gate;

        var sync = harness.State.SyncGitHubAsync();

        var entries = TasksTestHost.EntriesFor(harness.Store);
        var agentWrote = await entries.SaveFromTextAsync(
            id,
            "# Ship it\n`prompt` `!in-progress` `repo:backlog`\n\nDo it. The agent's note.\n\n## Step one\n`task` `!in-progress`\n",
            0,
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(agentWrote.IsSuccess);
        await harness.State.ReloadFromStoreAsync();
        Assert.NotSame(row, harness.State.Rows.Single(r => r.Id == id));

        gate.SetResult();
        await sync;

        var live = harness.State.Rows.Single(r => r.Id == id);
        Assert.Equal(EntryStatus.Done, live.Status);
        Assert.Contains("The agent's note.", live.RawText, StringComparison.Ordinal);

        var stored = await entries.GetAsync(id, TestContext.Current.CancellationToken);
        Assert.Equal(EntryStatus.Done, stored!.Status);
        Assert.Contains("The agent's note.", stored.Body, StringComparison.Ordinal);
    }

    /// <summary>A merge read while the person is writing in the entry does not move
    /// it from under them, and is not dropped either: it is owed, and lands the
    /// moment the editor closes.</summary>
    [Fact]
    public async Task A_merge_read_while_the_entry_is_being_written_in_moves_it_once_the_editor_closes()
    {
        var harness = Build("JSdotNet/Backlog");
        var row = await LinkedEntryAsync(harness, "`prompt` `!in-progress` `repo:backlog`", 708);
        harness.Client.PullRequestStates[708] = GitHubItemState.Merged;
        harness.Client.PullRequestBases[708] = "main";

        harness.State.BeginEdit(row);
        await harness.State.CheckPullRequestsAsync();

        Assert.Equal(GitHubItemState.Merged, row.PullRequestStates.Values.Single());
        Assert.Equal(EntryStatus.InProgress, row.Status);

        await harness.State.EndEditAsync(row);

        Assert.Equal(EntryStatus.Done, harness.State.Rows.Single(r => r.Id == row.Id).Status);
    }

    /// <summary>The move is the app's, not the person's: Ctrl+Z takes back their own
    /// last change, and the entry the merge finished stays Done.</summary>
    [Fact]
    public async Task Undo_takes_back_the_persons_own_change_not_the_merge_move()
    {
        var harness = Build("JSdotNet/Backlog");
        var finished = await LinkedEntryAsync(harness, "`prompt` `!in-progress` `repo:backlog`", 708);
        var renamed = await WriteEntryAsync(harness.State, "# Old title\n`task` `!ready`\n");
        harness.Client.PullRequestStates[708] = GitHubItemState.Merged;
        harness.Client.PullRequestBases[708] = "main";

        await harness.State.RenameEntryAsync(renamed, "New title");
        await harness.State.CheckPullRequestsAsync();
        Assert.Equal(EntryStatus.Done, harness.State.Rows.Single(r => r.Id == finished.Id).Status);

        Assert.True(await harness.State.UndoAsync());

        Assert.Equal("Old title", harness.State.Rows.Single(r => r.Id == renamed.Id).PreviewTitle);
        Assert.Equal(EntryStatus.Done, harness.State.Rows.Single(r => r.Id == finished.Id).Status);
    }

    /// <summary>
    /// Typing in the entry's body while the merge is read: the keystrokes waiting on
    /// their debounce land first, as the person's own save, and the entry then moves
    /// at once — it does not wait on a save that a chip click or Ctrl+Z might flush
    /// without ever draining it.
    /// </summary>
    [Fact]
    public async Task A_merge_read_while_typing_lands_the_typing_then_moves_the_entry()
    {
        var harness = Build("JSdotNet/Backlog");
        var row = await LinkedEntryAsync(harness, "`prompt` `!in-progress` `repo:backlog`", 708);
        var id = row.Id!.Value;
        harness.Client.PullRequestStates[708] = GitHubItemState.Merged;
        harness.Client.PullRequestBases[708] = "main";

        harness.State.ChangeBody(row, "Do it. Typed while it merged.\n\n## Step one\n`task` `!in-progress`\n");
        await harness.State.CheckPullRequestsAsync();

        var live = harness.State.Rows.Single(r => r.Id == id);
        Assert.Equal(EntryStatus.Done, live.Status);
        Assert.Contains("Typed while it merged.", live.RawText, StringComparison.Ordinal);

        var stored = await TasksTestHost.EntriesFor(harness.Store).GetAsync(id, TestContext.Current.CancellationToken);
        Assert.Equal(EntryStatus.Done, stored!.Status);
        Assert.Contains("Typed while it merged.", stored.Body, StringComparison.Ordinal);
    }

    /// <summary>
    /// The person's open typing step is sealed before the move, so their next
    /// keystroke opens a step of its own whose before already says Done. Ctrl+Z then
    /// takes back only that typing — the entry stays Done.
    /// </summary>
    [Fact]
    public async Task Undo_after_typing_on_through_the_move_takes_back_the_typing_and_keeps_done()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var harness = Build(clock, "JSdotNet/Backlog");
        var row = await LinkedEntryAsync(harness, "`prompt` `!in-progress` `repo:backlog`", 708);
        var id = row.Id!.Value;
        var entries = TasksTestHost.EntriesFor(harness.Store);
        harness.Client.PullRequestStates[708] = GitHubItemState.Merged;
        harness.Client.PullRequestBases[708] = "main";

        await TypeBodyAndLetItSaveAsync(harness, clock, entries, id, "Typed before the merge.");
        await harness.State.CheckPullRequestsAsync();
        Assert.Equal(EntryStatus.Done, harness.State.Rows.Single(r => r.Id == id).Status);

        await TypeBodyAndLetItSaveAsync(harness, clock, entries, id, "Typed after the merge.");

        Assert.True(await harness.State.UndoAsync());

        var live = harness.State.Rows.Single(r => r.Id == id);
        Assert.Equal(EntryStatus.Done, live.Status);
        Assert.Contains("Typed before the merge.", live.RawText, StringComparison.Ordinal);
        Assert.DoesNotContain("Typed after the merge.", live.RawText, StringComparison.Ordinal);
    }

    /// <summary>
    /// The person keeps typing while the move's save is on its way to the store. The
    /// move never rewrites the text under their caret: the keystroke stays on the
    /// row, builds on text that already says Done, and its own save keeps both.
    /// </summary>
    [Fact]
    public async Task A_keystroke_during_the_moves_save_survives_beside_done()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var calls = new TaskStoreCalls();
        var harness = BuildWatchingStore(clock, calls, "JSdotNet/Backlog");
        var row = await LinkedEntryAsync(harness, "`prompt` `!in-progress` `repo:backlog`", 708);
        var id = row.Id!.Value;
        var entries = TasksTestHost.EntriesFor(harness.Store);
        harness.Client.PullRequestStates[708] = GitHubItemState.Merged;
        harness.Client.PullRequestBases[708] = "main";

        var saving = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        calls.Before = name =>
        {
            if (name == nameof(Backlog.Modules.Tasks.Abstractions.Services.ITaskItems.SaveFromTextAsync)) saving.TrySetResult();
        };
        var hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        calls.HoldSaves = hold;

        var check = harness.State.CheckPullRequestsAsync();
        await saving.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        var live = harness.State.Rows.Single(r => r.Id == id);
        harness.State.ChangeBody(live, EntryTextParser.Parse(live.RawText).Body.Replace("Do it.", "Do it. Typed during the save.", StringComparison.Ordinal));

        calls.HoldSaves = null;
        calls.Before = null;
        hold.SetResult();
        await check;

        live = harness.State.Rows.Single(r => r.Id == id);
        Assert.Contains("Typed during the save.", live.RawText, StringComparison.Ordinal);
        Assert.Contains("`!done`", live.RawText.Split("## Step one")[0], StringComparison.Ordinal);

        clock.Advance(TimeSpan.FromSeconds(1));
        TaskItemDto? stored = null;
        await Polling.WaitUntilAsync(
            async () =>
            {
                stored = await entries.GetAsync(id, TestContext.Current.CancellationToken);
                return stored!.Body.Contains("Typed during the save.", StringComparison.Ordinal) && harness.State.SaveState == AppSaveState.Saved;
            },
            () => $"The keystroke's save never landed; the store holds: {stored?.Body}");

        Assert.Equal(EntryStatus.Done, stored!.Status);
        Assert.Contains("Typed during the save.", harness.State.Rows.Single(r => r.Id == id).RawText, StringComparison.Ordinal);
    }

    /// <summary>
    /// The check does not wait for the list to reload. An app left idle overnight
    /// asks again on its own once the recheck interval has passed, and the entry
    /// whose pull request merged in the meantime moves — with no reload at all.
    /// </summary>
    [Fact]
    public async Task An_idle_list_notices_a_merge_on_the_recheck_tick_without_a_reload()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var harness = Build(clock, "JSdotNet/Backlog");
        var row = await LinkedEntryAsync(harness, "`prompt` `!in-progress` `repo:backlog`", 1453);
        harness.Client.PullRequestStates[1453] = GitHubItemState.Open;
        harness.Client.PullRequestBases[1453] = "plan/devbook-domain-view";

        // The shell's answer to the tick, as Home wires it.
        Task? ticked = null;
        harness.State.PullRequestRecheckDue += () => ticked = harness.State.CheckPullRequestsAsync();

        await harness.State.CheckPullRequestsAsync();
        Assert.Equal(EntryStatus.InProgress, harness.State.Rows.Single(r => r.Id == row.Id).Status);

        harness.Client.PullRequestStates[1453] = GitHubItemState.Merged;
        clock.Advance(TasksDesktopState.PullRequestRecheckInterval);

        Assert.NotNull(ticked);
        await ticked;

        Assert.Equal(2, harness.Client.StatusReads);
        Assert.Equal(EntryStatus.Done, harness.State.Rows.Single(r => r.Id == row.Id).Status);
    }

    [Fact]
    public async Task The_recheck_tick_ends_when_the_list_is_disposed()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var harness = Build(clock, "JSdotNet/Backlog");
        await LinkedEntryAsync(harness, "`prompt` `!in-progress` `repo:backlog`", 708);
        harness.Client.PullRequestStates[708] = GitHubItemState.Open;

        var ticks = 0;
        harness.State.PullRequestRecheckDue += () => ticks++;

        await harness.State.CheckPullRequestsAsync();
        clock.Advance(TasksDesktopState.PullRequestRecheckInterval);
        Assert.Equal(1, ticks);

        harness.State.Dispose();
        clock.Advance(TasksDesktopState.PullRequestRecheckInterval * 3);

        Assert.Equal(1, ticks);
    }

    [Fact]
    public async Task Without_a_configured_repository_the_check_asks_github_nothing()
    {
        var harness = Build();

        var row = await WriteEntryAsync(harness.State, "# Add GitHub support\n`task` `!done` `repo:backlog`\n");
        row.PullRequestLinks = [new EntryPullRequestLink("JSdotNet/Backlog", 708)];
        harness.Client.PullRequestStates[708] = GitHubItemState.Merged;

        await harness.State.CheckPullRequestsAsync();

        Assert.Equal(0, harness.Client.StatusReads);
        Assert.Equal(0, harness.Client.StateReads);
        Assert.Empty(row.PullRequestStates);
    }

    /// <summary>Nobody asked for this read, so nobody is told when it fails: the
    /// pull request stays unread, the issue line stays the issue's, and no toast
    /// rises over a list the reader only opened.</summary>
    [Fact]
    public async Task A_check_that_cannot_read_a_pull_request_says_nothing()
    {
        var harness = Build("JSdotNet/Backlog");

        var row = await WriteEntryAsync(harness.State, "# Add GitHub support\n`task` `!done` `repo:backlog`\n");
        var pr = new EntryPullRequestLink("JSdotNet/Backlog", 708);
        row.PullRequestLinks = [pr];
        harness.Client.PullRequestFailure = new GitHubException("Bad credentials");

        await harness.State.CheckPullRequestsAsync();

        Assert.False(row.PullRequestStates.ContainsKey(pr));
        Assert.Null(row.GitHubError);
        Assert.Empty(harness.Toasts.Visible);
    }

    /// <summary>A pull request GitHub will not answer for is asked about once per
    /// recheck interval in the background, not once per reload: every write an agent
    /// makes reloads the list, and asking again each time would spend the rate limit
    /// on an answer that is not coming. The sync button is the explicit retry, and
    /// the interval the quiet one.</summary>
    [Fact]
    public async Task An_unreadable_pull_request_is_read_once_per_interval_until_the_sync_button_asks_again()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var harness = Build(clock, "JSdotNet/Backlog");

        var written = await WriteEntryAsync(harness.State, "# Add GitHub support\n`task` `!done` `repo:backlog`\n");
        var entries = TasksTestHost.EntriesFor(harness.Store);
        await entries.LinkToIssueAsync(written.Id!.Value, "JSdotNet/Backlog", "708", EntryProjectionDto.PullRequestTargetType, TestContext.Current.CancellationToken);
        harness.Client.PullRequestFailure = new GitHubException("Not Found");

        for (var reload = 0; reload < 3; reload++)
        {
            await harness.State.ReloadFromStoreAsync();
            await harness.State.CheckPullRequestsAsync();
        }

        Assert.Equal(1, harness.Client.StatusReads);
        Assert.Equal(1, harness.Client.StateReads);

        clock.Advance(TasksDesktopState.PullRequestRecheckInterval);
        await harness.State.CheckPullRequestsAsync();

        Assert.Equal(2, harness.Client.StatusReads);

        await harness.State.SyncGitHubAsync();

        Assert.Equal(3, harness.Client.StatusReads);
        Assert.Empty(harness.Toasts.Visible);
    }

    /// <summary>A failure nobody anticipated is that pull request's alone: the
    /// rest are still read, and the check the shell let go of ends cleanly
    /// rather than as an exception nobody observes.</summary>
    [Fact]
    public async Task An_unexpected_failure_on_one_pull_request_does_not_stop_the_next()
    {
        var harness = Build("JSdotNet/Backlog");

        var row = await WriteEntryAsync(harness.State, "# Add GitHub support\n`task` `!done` `repo:backlog`\n");
        var broken = new EntryPullRequestLink("JSdotNet/Backlog", 708);
        var fine = new EntryPullRequestLink("JSdotNet/Backlog", 710);
        row.PullRequestLinks = [broken, fine];
        harness.Client.PullRequestFailures[708] = new InvalidOperationException("Unexpected.");
        harness.Client.PullRequestStates[710] = GitHubItemState.Merged;

        var check = harness.State.CheckPullRequestsAsync();
        await check;

        Assert.True(check.IsCompletedSuccessfully);
        Assert.False(row.PullRequestStates.ContainsKey(broken));
        Assert.Equal(GitHubItemState.Merged, row.PullRequestStates[fine]);
        Assert.Null(row.GitHubError);
        Assert.Empty(harness.Toasts.Visible);
    }

    /// <summary>A request made from inside the check — a re-render the check's own
    /// <c>Changed</c> set off — is handed the check that is running, not the one
    /// before it.</summary>
    [Fact]
    public async Task A_check_asked_for_from_inside_the_check_gets_the_running_one()
    {
        var harness = Build("JSdotNet/Backlog");

        var row = await WriteEntryAsync(harness.State, "# Add GitHub support\n`task` `!done` `repo:backlog`\n");
        row.PullRequestLinks = [new EntryPullRequestLink("JSdotNet/Backlog", 708)];
        harness.Client.PullRequestStates[708] = GitHubItemState.Merged;

        Task? inner = null;
        harness.State.Changed += () => inner ??= harness.State.CheckPullRequestsAsync();

        var outer = harness.State.CheckPullRequestsAsync();
        await outer;

        Assert.Same(outer, inner);
    }

    /// <summary>A reload replaces every row while a read is in flight — an agent
    /// recording its work is exactly what sets one off. The read lands on the row
    /// that is on screen when it comes back, not the one it started from.</summary>
    [Fact]
    public async Task A_reload_during_the_check_still_ends_with_the_read_on_the_current_row()
    {
        var harness = Build("JSdotNet/Backlog");

        var written = await WriteEntryAsync(harness.State, "# Add GitHub support\n`task` `!done` `repo:backlog`\n");
        var entries = TasksTestHost.EntriesFor(harness.Store);
        await entries.LinkToIssueAsync(written.Id!.Value, "JSdotNet/Backlog", "708", EntryProjectionDto.PullRequestTargetType, TestContext.Current.CancellationToken);
        await harness.State.ReloadFromStoreAsync();

        var pr = new EntryPullRequestLink("JSdotNet/Backlog", 708);
        harness.Client.PullRequestStates[708] = GitHubItemState.Merged;
        var gate = new TaskCompletionSource();
        harness.Client.StatusGates[708] = gate;

        var check = harness.State.CheckPullRequestsAsync();
        await harness.State.ReloadFromStoreAsync();
        gate.SetResult();
        await check;

        var row = Assert.Single(harness.State.Rows);
        Assert.Equal(GitHubItemState.Merged, row.PullRequestStates[pr]);
    }

    /// <summary>
    /// The sync button's sweep runs for as long as GitHub takes over every linked
    /// row, and the list reloads under it whenever an agent or a sync pull writes.
    /// A pull request read the sweep started before such a reload lands on the row
    /// the list holds when it comes back — not on the replaced one nobody can see,
    /// which is how merged links stayed uncoloured through a whole sync.
    /// </summary>
    [Fact]
    public async Task A_reload_during_a_sync_still_ends_with_the_pull_request_read_on_the_current_row()
    {
        var harness = Build("JSdotNet/Backlog");
        var row = await LinkedEntryAsync(harness, "`task` `!done` `repo:backlog`", 708);
        var id = row.Id!.Value;
        var pr = new EntryPullRequestLink("JSdotNet/Backlog", 708);
        harness.Client.PullRequestStates[708] = GitHubItemState.Merged;
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Client.StatusGates[708] = gate;

        var sync = harness.State.SyncGitHubAsync();
        await harness.State.ReloadFromStoreAsync();
        Assert.NotSame(row, harness.State.Rows.Single(r => r.Id == id));

        gate.SetResult();
        await sync;

        var current = harness.State.Rows.Single(r => r.Id == id);
        Assert.Equal(GitHubItemState.Merged, Assert.Contains(pr, current.PullRequestStates));
    }

    /// <summary>The issue half of the same sweep: the snapshot read before a reload
    /// is written onto the row that replaced the one it started from.</summary>
    [Fact]
    public async Task A_reload_during_a_sync_still_ends_with_the_issue_read_on_the_current_row()
    {
        var harness = Build("JSdotNet/Backlog");
        var row = await WriteEntryAsync(harness.State, "# Add GitHub support\n`task` `*high` `!draft` `repo:backlog`\n");
        await harness.State.PushToGitHubAsync(row);
        var id = row.Id!.Value;

        harness.Client.Snapshot = new GitHubIssueSnapshot(
            new GitHubIssue(101, "https://github.com/JSdotNet/Backlog/issues/101", "Add GitHub support", GitHubItemState.Closed, null),
            [],
            DateTimeOffset.UtcNow);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Client.IssueGate = gate;

        var sync = harness.State.SyncGitHubAsync();
        await harness.State.ReloadFromStoreAsync();
        var replaced = harness.State.Rows.Single(r => r.Id == id);
        Assert.NotSame(row, replaced);
        Assert.NotNull(replaced.IssueLink);

        gate.SetResult();
        await sync;

        var current = harness.State.Rows.Single(r => r.Id == id);
        Assert.Equal(GitHubItemState.Closed, current.Snapshot!.Issue.State);
        Assert.Null(current.GitHubError);
    }

    /// <summary>
    /// A sync read that came back after a reload used to land on the replaced row and
    /// still count as the latest read of its pull request — so the background check's
    /// own read, already out for the same pull request, was thrown away as stale in
    /// favour of a read nobody could see. Whichever lands, the row on screen ends with
    /// the state.
    /// </summary>
    [Fact]
    public async Task A_sync_read_after_a_reload_does_not_cost_the_background_check_its_read()
    {
        var harness = Build("JSdotNet/Backlog");
        var row = await LinkedEntryAsync(harness, "`task` `!done` `repo:backlog`", 708);
        var id = row.Id!.Value;
        var pr = new EntryPullRequestLink("JSdotNet/Backlog", 708);
        harness.Client.PullRequestStates[708] = GitHubItemState.Merged;

        var checkGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Client.StatusGates[708] = checkGate;
        var check = harness.State.CheckPullRequestsAsync();

        var syncGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Client.StatusGates[708] = syncGate;
        var sync = harness.State.SyncGitHubAsync();

        await harness.State.ReloadFromStoreAsync();

        syncGate.SetResult();
        await sync;
        checkGate.SetResult();
        await check;

        var current = harness.State.Rows.Single(r => r.Id == id);
        Assert.Equal(GitHubItemState.Merged, Assert.Contains(pr, current.PullRequestStates));
    }

    /// <summary>
    /// A list with hundreds of recorded pull requests learns where they stand from one
    /// read per repository, not one per pull request — which is what left a merged link
    /// uncoloured for minutes behind every older one. A merged or closed one needs no
    /// read of its own.
    /// </summary>
    [Fact]
    public async Task The_background_check_reads_two_hundred_pull_requests_with_one_read_per_repository()
    {
        var harness = Build("JSdotNet/Backlog", "JSdotNet/Other");
        harness.Client.AnswersLinkedReads = true;
        var row = await WriteEntryAsync(harness.State, "# Many\n`task` `!done` `repo:backlog`\n");
        List<EntryPullRequestLink> links =
        [
            .. Enumerable.Range(1000, 100).Select(number => new EntryPullRequestLink("JSdotNet/Backlog", number)),
            .. Enumerable.Range(2000, 100).Select(number => new EntryPullRequestLink("JSdotNet/Other", number))
        ];
        row.PullRequestLinks = links;
        foreach (var pr in links)
        {
            harness.Client.PullRequestStates[pr.Number] = pr.Number % 2 == 0 ? GitHubItemState.Merged : GitHubItemState.Closed;
            harness.Client.PullRequestBases[pr.Number] = "main";
        }

        await harness.State.CheckPullRequestsAsync();

        Assert.Equal(2, harness.Client.LinkedReads);
        Assert.Equal(0, harness.Client.StatusReads + harness.Client.StateReads);
        Assert.All(links, pr => Assert.Equal(
            pr.Number % 2 == 0 ? GitHubItemState.Merged : GitHubItemState.Closed,
            Assert.Contains(pr, row.PullRequestStates)));
    }

    /// <summary>The batched read says where a merge went, so a merge it is the first
    /// to see still moves its prompt entry to Done — with no read of its own.</summary>
    [Fact]
    public async Task A_merge_the_batched_read_finds_moves_its_entry_without_a_read_of_its_own()
    {
        var harness = Build("JSdotNet/Backlog");
        harness.Client.AnswersLinkedReads = true;
        var row = await LinkedEntryAsync(harness, "`prompt` `!in-progress` `repo:backlog`", 708);
        harness.Client.PullRequestStates[708] = GitHubItemState.Merged;
        harness.Client.PullRequestBases[708] = "plan/lobes-atlas";

        await harness.State.CheckPullRequestsAsync();

        Assert.Equal(EntryStatus.Done, harness.State.Rows.Single(r => r.Id == row.Id).Status);
        Assert.Equal(0, harness.Client.StatusReads + harness.Client.StateReads);
    }

    /// <summary>An open pull request is still read on its own for its status: its
    /// checks and the merge offer need facts the batched read does not carry.</summary>
    [Fact]
    public async Task An_open_pull_request_the_batched_read_finds_is_still_read_for_its_status()
    {
        var harness = Build("JSdotNet/Backlog");
        harness.Client.AnswersLinkedReads = true;
        var row = await WriteEntryAsync(harness.State, "# Add GitHub support\n`task` `!done` `repo:backlog`\n");
        var merged = new EntryPullRequestLink("JSdotNet/Backlog", 708);
        var open = new EntryPullRequestLink("JSdotNet/Backlog", 710);
        row.PullRequestLinks = [merged, open];
        harness.Client.PullRequestStates[708] = GitHubItemState.Merged;
        harness.Client.PullRequestBases[708] = "main";
        harness.Client.PullRequestStates[710] = GitHubItemState.Open;
        harness.Client.PullRequestChecks[710] = GitHubCheckState.Failing;

        await harness.State.CheckPullRequestsAsync();

        Assert.Equal(1, harness.Client.StatusReads);
        Assert.Equal(GitHubItemState.Merged, row.PullRequestStates[merged]);
        Assert.Equal(GitHubCheckState.Failing, row.PullRequestStatuses[open].Checks);
    }

    /// <summary>A repository the batched read cannot read — a refusal, an older
    /// server — is read the way it was before, one pull request at a time, while the
    /// others keep their one read.</summary>
    [Fact]
    public async Task A_repository_the_batched_read_cannot_read_falls_back_to_one_read_each()
    {
        var harness = Build("JSdotNet/Backlog", "JSdotNet/Other");
        harness.Client.AnswersLinkedReads = true;
        harness.Client.LinkedReadFailures.Add("JSdotNet/Other");
        var row = await WriteEntryAsync(harness.State, "# Add GitHub support\n`task` `!done` `repo:backlog`\n");
        var here = new EntryPullRequestLink("JSdotNet/Backlog", 708);
        var there = new EntryPullRequestLink("JSdotNet/Other", 2001);
        row.PullRequestLinks = [here, there];
        harness.Client.PullRequestStates[708] = GitHubItemState.Merged;
        harness.Client.PullRequestBases[708] = "main";
        harness.Client.PullRequestStates[2001] = GitHubItemState.Merged;

        await harness.State.CheckPullRequestsAsync();

        Assert.Equal(1, harness.Client.StatusReads);
        Assert.Equal(GitHubItemState.Merged, row.PullRequestStates[here]);
        Assert.Equal(GitHubItemState.Merged, row.PullRequestStates[there]);
    }

    /// <summary>The sync button reads its pull requests the same way: one read per
    /// repository, so a list of long-closed work is checked in one round trip.</summary>
    [Fact]
    public async Task Check_GitHub_reads_its_pull_requests_with_one_read_per_repository()
    {
        var harness = Build("JSdotNet/Backlog");
        harness.Client.AnswersLinkedReads = true;
        var row = await WriteEntryAsync(harness.State, "# Many\n`task` `!done` `repo:backlog`\n");
        List<EntryPullRequestLink> links = [.. Enumerable.Range(1000, 100).Select(number => new EntryPullRequestLink("JSdotNet/Backlog", number))];
        row.PullRequestLinks = links;
        foreach (var pr in links) harness.Client.PullRequestStates[pr.Number] = GitHubItemState.Closed;

        await harness.State.SyncGitHubAsync();

        Assert.Equal(1, harness.Client.LinkedReads);
        Assert.Equal(0, harness.Client.StatusReads + harness.Client.StateReads);
        Assert.All(links, pr => Assert.Equal(GitHubItemState.Closed, Assert.Contains(pr, row.PullRequestStates)));
    }

    /// <summary>
    /// An entry already Done when its merge is read — the person, or the close-merged
    /// skill, got there first — has nothing left to finish: the merge is settled for
    /// it. Taken back to In progress afterwards, it stays there across a restart.
    /// </summary>
    [Fact]
    public async Task A_merge_read_when_its_entry_is_already_Done_does_not_move_it_again_after_it_is_reopened()
    {
        var memory = Memory();
        var harness = BuildRemembering(memory, clock: null, "JSdotNet/Backlog");
        var row = await LinkedEntryAsync(harness, "`prompt` `!done` `repo:backlog`", 708);
        harness.Client.PullRequestStates[708] = GitHubItemState.Merged;
        harness.Client.PullRequestBases[708] = "main";
        await harness.State.CheckPullRequestsAsync();

        await harness.State.ChangeStatusAsync(harness.State.Rows.Single(r => r.Id == row.Id), EntryStatus.InProgress);
        var restarted = await RestartAsync(harness, memory, clock: null);
        await restarted.CheckPullRequestsAsync();

        Assert.Equal(EntryStatus.InProgress, restarted.Rows.Single(r => r.Id == row.Id).Status);
    }

    /// <summary>
    /// What the batched read settles is on screen before any pull request is read on
    /// its own: a merged link never waits behind the status reads of the open ones
    /// listed before it.
    /// </summary>
    [Fact]
    public async Task A_merged_link_is_coloured_before_the_open_pull_requests_are_read_for_their_status()
    {
        var harness = Build("JSdotNet/Backlog");
        harness.Client.AnswersLinkedReads = true;
        var row = await WriteEntryAsync(harness.State, "# Add GitHub support\n`task` `!done` `repo:backlog`\n");
        var open = new EntryPullRequestLink("JSdotNet/Backlog", 710);
        var merged = new EntryPullRequestLink("JSdotNet/Backlog", 708);
        row.PullRequestLinks = [open, merged];
        harness.Client.PullRequestStates[710] = GitHubItemState.Open;
        harness.Client.PullRequestStates[708] = GitHubItemState.Merged;
        harness.Client.PullRequestBases[708] = "main";
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Client.StatusGates[710] = gate;

        var check = harness.State.CheckPullRequestsAsync();

        Assert.Equal(GitHubItemState.Merged, Assert.Contains(merged, row.PullRequestStates));
        gate.SetResult();
        await check;
        Assert.Equal(GitHubItemState.Open, row.PullRequestStates[open]);
    }

    /// <summary>
    /// A merge whose branches no read could say — the batched answer left them out and
    /// the pull request's own read failed — cannot tell whether it finishes the entry.
    /// It is not written off for that: it is asked about again at the recheck interval,
    /// and the read that says where it went moves the entry then.
    /// </summary>
    [Fact]
    public async Task A_merge_whose_branches_could_not_be_read_moves_its_entry_once_a_later_read_says_them()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var harness = Build(clock, "JSdotNet/Backlog");
        harness.Client.AnswersLinkedReads = true;
        var row = await LinkedEntryAsync(harness, "`prompt` `!in-progress` `repo:backlog`", 708);
        var pr = new EntryPullRequestLink("JSdotNet/Backlog", 708);
        harness.Client.PullRequestStates[708] = GitHubItemState.Merged;
        harness.Client.PullRequestFailures[708] = new GitHubException("GitHub took too long to answer.");

        await harness.State.CheckPullRequestsAsync();
        var current = harness.State.Rows.Single(r => r.Id == row.Id);
        Assert.Equal(GitHubItemState.Merged, Assert.Contains(pr, current.PullRequestStates));
        Assert.Equal(EntryStatus.InProgress, current.Status);

        harness.Client.PullRequestFailures.Remove(708);
        harness.Client.PullRequestBases[708] = "main";
        clock.Advance(TasksDesktopState.PullRequestRecheckInterval);
        await harness.State.CheckPullRequestsAsync();

        Assert.Equal(EntryStatus.Done, harness.State.Rows.Single(r => r.Id == row.Id).Status);
    }

    /// <summary>
    /// A pull request of a repository archived in Settings is not read by the
    /// background check or the sync — not together, not on its own (local ADR 0029) —
    /// and its link keeps whatever it last showed.
    /// </summary>
    [Fact]
    public async Task A_pull_request_of_an_archived_repository_is_not_read()
    {
        var harness = Build("JSdotNet/Backlog", "JSdotNet/Other");
        harness.Client.AnswersLinkedReads = true;
        Assert.Null(harness.Settings.SetRepositoryArchived("JSdotNet/Other", archived: true));
        var row = await WriteEntryAsync(harness.State, "# Add GitHub support\n`task` `!done` `repo:backlog`\n");
        var archived = new EntryPullRequestLink("JSdotNet/Other", 2001);
        row.PullRequestLinks = [archived];
        harness.Client.PullRequestStates[2001] = GitHubItemState.Merged;
        harness.Client.PullRequestBases[2001] = "main";

        await harness.State.CheckPullRequestsAsync();
        await harness.State.SyncGitHubAsync();

        Assert.Equal(0, harness.Client.LinkedReads + harness.Client.StatusReads + harness.Client.StateReads);
        Assert.False(row.PullRequestStates.ContainsKey(archived));
    }

    /// <summary>
    /// A remembered close read closed again is remembered as read then, so the next
    /// launch waits out the recheck interval from that read rather than asking about
    /// it again straight away.
    /// </summary>
    [Fact]
    public async Task A_remembered_close_read_closed_again_waits_the_interval_from_that_read()
    {
        var memory = Memory();
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var harness = BuildRemembering(memory, clock, "JSdotNet/Backlog");
        await LinkedEntryAsync(harness, "`task` `!done` `repo:backlog`", 708);
        harness.Client.PullRequestStates[708] = GitHubItemState.Closed;
        await harness.State.CheckPullRequestsAsync();

        clock.Advance(TasksDesktopState.PullRequestRecheckInterval);
        var second = await RestartAsync(harness, memory, clock);
        await second.CheckPullRequestsAsync();
        var readsAfterSecondLaunch = harness.Client.StatusReads;

        clock.Advance(TimeSpan.FromSeconds(30));
        var third = await RestartAsync(harness, memory, clock);
        await third.CheckPullRequestsAsync();

        Assert.Equal(readsAfterSecondLaunch, harness.Client.StatusReads);
    }

    /// <summary>
    /// A row busy with a GitHub read stays busy on screen when a reload replaces it
    /// mid-read, so its Refresh cannot start a second read of the same issue — and the
    /// row on screen is the one let go when the read comes back.
    /// </summary>
    [Fact]
    public async Task A_reload_during_a_refresh_keeps_the_row_on_screen_busy_until_it_ends()
    {
        var harness = Build("JSdotNet/Backlog");
        var row = await WriteEntryAsync(harness.State, "# Add GitHub support\n`task` `*high` `!draft` `repo:backlog`\n");
        await harness.State.PushToGitHubAsync(row);
        var id = row.Id!.Value;
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Client.IssueGate = gate;

        var refresh = harness.State.RefreshGitHubAsync(row);
        await harness.State.ReloadFromStoreAsync();
        var replaced = harness.State.Rows.Single(r => r.Id == id);
        Assert.NotSame(row, replaced);
        Assert.True(replaced.GitHubBusy);

        gate.SetResult();
        await refresh;

        Assert.False(harness.State.Rows.Single(r => r.Id == id).GitHubBusy);
    }

    /// <summary>
    /// A reload while a push is creating the issue keeps the replacing row busy, and
    /// the push lets that row go when it ends, refused or not — so the entry is never left busy for
    /// good, its Refresh, push and merge acts silently refused until a restart.
    /// </summary>
    [Fact]
    public async Task A_reload_during_a_push_does_not_leave_the_row_busy_for_good()
    {
        var harness = Build("JSdotNet/Backlog");
        var row = await WriteEntryAsync(harness.State, "# Add GitHub support\n`task` `*high` `!draft` `repo:backlog`\n");
        var id = row.Id!.Value;
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Client.CreateGate = gate;

        var push = harness.State.PushToGitHubAsync(row);
        await harness.State.ReloadFromStoreAsync();
        Assert.True(harness.State.Rows.Single(r => r.Id == id).GitHubBusy);

        // Refused, so no refresh follows the push to let the row go on its behalf.
        harness.Client.Failure = new GitHubException("GitHub refused the request.");
        gate.SetResult();
        await push;
        await harness.State.ReloadFromStoreAsync();

        Assert.False(harness.State.Rows.Single(r => r.Id == id).GitHubBusy);
    }

    /// <summary>
    /// The sync button re-reads every closed pull request. One read closed again
    /// within the recheck interval of the read the memory holds is not written again:
    /// a list of a hundred closed pull requests is not a hundred file writes per sync.
    /// Once that read is older than the interval, the new one is kept.
    /// </summary>
    [Fact]
    public async Task A_close_read_closed_again_is_rewritten_only_once_the_kept_read_is_older_than_the_interval()
    {
        var memory = new CountingMemory(Memory());
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var harness = BuildRemembering(memory, clock, "JSdotNet/Backlog");
        await LinkedEntryAsync(harness, "`task` `!done` `repo:backlog`", 708);
        harness.Client.PullRequestStates[708] = GitHubItemState.Closed;
        await harness.State.CheckPullRequestsAsync();
        Assert.Equal(1, memory.Writes);

        await harness.State.SyncGitHubAsync();
        Assert.Equal(1, memory.Writes);

        clock.Advance(TasksDesktopState.PullRequestRecheckInterval);
        await harness.State.SyncGitHubAsync();
        Assert.Equal(2, memory.Writes);
    }

    /// <summary>A memory that counts what is written through it.</summary>
    private sealed class CountingMemory(IPullRequestStateMemory inner) : IPullRequestStateMemory
    {
        public int Writes { get; private set; }

        public IReadOnlyList<RememberedPullRequest> ReadAll() => inner.ReadAll();

        public void Remember(RememberedPullRequest pullRequest)
        {
            Writes++;
            inner.Remember(pullRequest);
        }

        public void Forget(string repository, int number) => inner.Forget(repository, number);
    }

    /// <summary>One check at a time. A reload that asks while one is running is
    /// owed one more pass after it, which is what picks up a pull request recorded
    /// in the meantime — and nothing is read twice.</summary>
    [Fact]
    public async Task A_check_asked_for_while_one_runs_runs_once_more_after_it()
    {
        var harness = Build("JSdotNet/Backlog");

        var row = await WriteEntryAsync(harness.State, "# Add GitHub support\n`task` `!done` `repo:backlog`\n");
        var first = new EntryPullRequestLink("JSdotNet/Backlog", 708);
        var second = new EntryPullRequestLink("JSdotNet/Backlog", 710);
        row.PullRequestLinks = [first];
        harness.Client.PullRequestStates[708] = GitHubItemState.Merged;
        harness.Client.PullRequestStates[710] = GitHubItemState.Open;
        var gate = new TaskCompletionSource();
        harness.Client.StatusGates[708] = gate;

        var running = harness.State.CheckPullRequestsAsync();
        row.PullRequestLinks = [first, second];
        var asked = harness.State.CheckPullRequestsAsync();
        gate.SetResult();
        await Task.WhenAll(running, asked);

        Assert.Equal(2, harness.Client.StatusReads);
        Assert.Equal(GitHubItemState.Merged, row.PullRequestStates[first]);
        Assert.Equal(GitHubItemState.Open, row.PullRequestStates[second]);
    }

    /// <summary>A workspace closed while a read is in flight ends the check
    /// quietly: the read that comes back afterwards writes nothing, and a check
    /// asked for afterwards asks GitHub nothing.</summary>
    [Fact]
    public async Task Disposing_the_list_ends_the_check_without_throwing()
    {
        var harness = Build("JSdotNet/Backlog");

        var row = await WriteEntryAsync(harness.State, "# Add GitHub support\n`task` `!done` `repo:backlog`\n");
        var pr = new EntryPullRequestLink("JSdotNet/Backlog", 708);
        row.PullRequestLinks = [pr];
        harness.Client.PullRequestStates[708] = GitHubItemState.Merged;
        var gate = new TaskCompletionSource();
        harness.Client.StatusGates[708] = gate;

        var check = harness.State.CheckPullRequestsAsync();
        harness.State.Dispose();
        gate.SetResult();
        await check;
        await harness.State.CheckPullRequestsAsync();

        Assert.Equal(1, harness.Client.StatusReads);
        Assert.False(row.PullRequestStates.ContainsKey(pr));
    }

    /// <summary>The same read that says whether a pull request merged says how its
    /// checks stand and whether GitHub is holding it for auto-merge — one query, so
    /// the link and the menu never disagree about which read they came from.</summary>
    [Fact]
    public async Task Syncing_reads_a_pull_requests_checks_and_auto_merge()
    {
        var harness = Build("JSdotNet/Backlog");

        var row = await WriteEntryAsync(harness.State, "# Add GitHub support\n`task` `!done` `repo:backlog`\n");
        var pr = new EntryPullRequestLink("JSdotNet/Backlog", 710);
        row.PullRequestLinks = [pr];
        harness.Client.PullRequestStates[710] = GitHubItemState.Open;
        harness.Client.PullRequestChecks[710] = GitHubCheckState.Failing;
        harness.Client.AutoMergeOn.Add(710);

        await harness.State.SyncGitHubAsync();

        var status = row.PullRequestStatuses[pr];
        Assert.Equal(GitHubItemState.Open, row.PullRequestStates[pr]);
        Assert.Equal(GitHubCheckState.Failing, status.Checks);
        Assert.True(status.AutoMergeEnabled);
    }

    /// <summary>
    /// A server that cannot answer the status query — an older Enterprise Server
    /// whose schema lacks the auto-merge fields — still gets the merged colour: the
    /// REST read is asked for the state alone. With no status there is nothing to
    /// draw a checks mark from and no act to name, so neither is offered.
    /// </summary>
    [Fact]
    public async Task A_status_query_that_fails_falls_back_to_the_state_alone()
    {
        var harness = Build("JSdotNet/Backlog");

        var row = await WriteEntryAsync(harness.State, "# Add GitHub support\n`task` `!done` `repo:backlog`\n");
        var pr = new EntryPullRequestLink("JSdotNet/Backlog", 708);
        row.PullRequestLinks = [pr];
        harness.Client.PullRequestStates[708] = GitHubItemState.Merged;
        harness.Client.StatusFailure = new GitHubException("Field 'autoMergeRequest' doesn't exist on type 'PullRequest'");

        await harness.State.SyncGitHubAsync();

        Assert.Equal(GitHubItemState.Merged, row.PullRequestStates[pr]);
        Assert.False(row.PullRequestStatuses.ContainsKey(pr));
        Assert.Null(harness.State.MergeOfferFor(row));
        Assert.Empty(harness.Toasts.Visible);

        // And an open one is still not offered to merge: the offer needs a status.
        // A second pull request, because a merged one is final and not read again.
        var open = new EntryPullRequestLink("JSdotNet/Backlog", 709);
        row.PullRequestLinks = [pr, open];
        harness.Client.PullRequestStates[709] = GitHubItemState.Open;
        await harness.State.SyncGitHubAsync();

        Assert.Equal(GitHubItemState.Open, row.PullRequestStates[open]);
        Assert.Null(harness.State.MergeOfferFor(row));
    }

    /// <summary>
    /// The offer follows the pull request's status: auto-merge when it has
    /// requirements still to meet, merge now when it can already merge — GitHub
    /// refuses to queue one in that state — and a way back out once it is queued.
    /// Only the latest open, non-draft pull request whose status has been read is
    /// offered, because a row's menu names one act on one pull request.
    /// </summary>
    [Fact]
    public async Task The_merge_offer_follows_the_latest_open_pull_requests_status()
    {
        var harness = Build("JSdotNet/Backlog");

        var row = await WriteEntryAsync(harness.State, "# Add GitHub support\n`task` `!done` `repo:backlog`\n");
        var older = new EntryPullRequestLink("JSdotNet/Backlog", 700);
        var latest = new EntryPullRequestLink("JSdotNet/Backlog", 710);
        var draft = new EntryPullRequestLink("JSdotNet/Backlog", 720);
        row.PullRequestLinks = [older, latest, draft];

        Assert.Null(harness.State.MergeOfferFor(row));

        harness.Client.PullRequestStates[700] = GitHubItemState.Open;
        harness.Client.PullRequestStates[710] = GitHubItemState.Open;
        harness.Client.PullRequestStates[720] = GitHubItemState.Draft;
        await harness.State.SyncGitHubAsync();

        var offer = harness.State.MergeOfferFor(row)!;
        Assert.Equal(latest, offer.PullRequest);
        Assert.Equal(PullRequestMergeAct.MergeWhenChecksPass, offer.Act);
        Assert.Equal("Merge #710 when checks pass", offer.Label);

        harness.Client.MergeReady.Add(710);
        await harness.State.SyncGitHubAsync();
        Assert.Equal(PullRequestMergeAct.MergeNow, harness.State.MergeOfferFor(row)!.Act);
        Assert.Equal("Merge #710 now", harness.State.MergeOfferFor(row)!.Label);

        harness.Client.MergeReady.Clear();
        harness.Client.AutoMergeOn.Add(710);
        await harness.State.SyncGitHubAsync();
        Assert.Equal(PullRequestMergeAct.CancelAutoMerge, harness.State.MergeOfferFor(row)!.Act);
        Assert.Equal("Cancel auto-merge for #710", harness.State.MergeOfferFor(row)!.Label);

        // Merged is no longer anything to merge.
        harness.Client.PullRequestStates[710] = GitHubItemState.Merged;
        harness.Client.PullRequestStates[700] = GitHubItemState.Merged;
        await harness.State.SyncGitHubAsync();
        Assert.Null(harness.State.MergeOfferFor(row));
    }

    [Fact]
    public async Task Turning_on_auto_merge_asks_github_and_reads_the_pull_request_again()
    {
        var (harness, row, pr) = await WithReadPullRequestAsync();

        await harness.State.SetAutoMergeAsync(row, pr, enable: true);

        Assert.Equal(["enable PR_710 Squash"], harness.Client.MergeCalls);
        Assert.True(row.PullRequestStatuses[pr].AutoMergeEnabled);
        Assert.False(row.GitHubBusy);
        Assert.Empty(harness.Toasts.Visible);
    }

    [Fact]
    public async Task Cancelling_auto_merge_asks_github_and_reads_the_pull_request_again()
    {
        var (harness, row, pr) = await WithReadPullRequestAsync(autoMerge: true);

        await harness.State.SetAutoMergeAsync(row, pr, enable: false);

        Assert.Equal(["disable PR_710"], harness.Client.MergeCalls);
        Assert.False(row.PullRequestStatuses[pr].AutoMergeEnabled);
    }

    [Fact]
    public async Task Merging_now_asks_github_and_the_link_reads_merged()
    {
        var (harness, row, pr) = await WithReadPullRequestAsync();
        harness.Client.MergeReady.Add(710);

        await harness.State.MergeNowAsync(row, pr);

        Assert.Equal(["merge PR_710 Squash"], harness.Client.MergeCalls);
        Assert.Equal(GitHubItemState.Merged, row.PullRequestStates[pr]);
    }

    /// <summary>A refusal is said out loud, naming the entry and what would fix it,
    /// and the row keeps working — the merge act is an Action, so it gets a toast,
    /// and the entry itself did nothing wrong.</summary>
    [Fact]
    public async Task A_refused_merge_act_is_a_toast_naming_the_entry_and_the_fix()
    {
        var (harness, row, pr) = await WithReadPullRequestAsync();
        harness.Client.MergeFailure = new GitHubException("Pull request Auto merge is not allowed for this repository");

        await harness.State.SetAutoMergeAsync(row, pr, enable: true);

        var toast = Assert.Single(harness.Toasts.Visible);
        Assert.Equal(ToastSeverity.Error, toast.Severity);
        Assert.Equal("github-error", toast.TestId);
        Assert.StartsWith(row.PreviewTitle, toast.Message, StringComparison.Ordinal);
        Assert.Contains("Allow auto-merge", toast.Message, StringComparison.Ordinal);
        Assert.Contains("JSdotNet/Backlog#710", toast.Message, StringComparison.Ordinal);

        Assert.False(row.GitHubBusy);
        Assert.Null(row.GitHubError);
        Assert.False(row.PullRequestStatuses[pr].AutoMergeEnabled);
    }

    /// <summary>
    /// A sweep that was already reading when a merge act landed does not put back
    /// what it read before the act. Each pull request is written as its read comes
    /// back, into the row's current map — not into a copy taken when the sweep
    /// began and written over the whole row at its end, which is how the act's own
    /// re-read used to be clobbered by an older answer.
    /// </summary>
    [Fact]
    public async Task A_sync_in_flight_does_not_undo_a_merge_act_that_landed_meanwhile()
    {
        var harness = Build("JSdotNet/Backlog");
        var row = await WriteEntryAsync(harness.State, "# Add GitHub support\n`task` `!done` `repo:backlog`\n");
        var first = new EntryPullRequestLink("JSdotNet/Backlog", 700);
        var second = new EntryPullRequestLink("JSdotNet/Backlog", 710);
        row.PullRequestLinks = [first, second];
        harness.Client.PullRequestStates[700] = GitHubItemState.Open;
        harness.Client.PullRequestStates[710] = GitHubItemState.Open;
        await harness.State.SyncGitHubAsync();

        // The next sweep reads #700 (auto-merge off), then waits on #710.
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Client.StatusGates[710] = gate;
        var sync = harness.State.SyncGitHubAsync();

        // Meanwhile #700 is handed to auto-merge and read back as on.
        await harness.State.SetAutoMergeAsync(row, first, enable: true);
        Assert.True(row.PullRequestStatuses[first].AutoMergeEnabled);

        gate.SetResult();
        await sync;

        Assert.True(row.PullRequestStatuses[first].AutoMergeEnabled);
        Assert.Equal(GitHubItemState.Open, row.PullRequestStates[second]);
    }

    /// <summary>
    /// A pull request read merged is remembered on this device, so the next launch
    /// draws its link merged from the list's first load — before any GitHub read, and
    /// with GitHub out of reach altogether.
    /// </summary>
    [Fact]
    public async Task A_merged_pull_request_draws_merged_after_a_restart_with_GitHub_out_of_reach()
    {
        var memory = Memory();
        var harness = BuildRemembering(memory, clock: null, "JSdotNet/Backlog");
        var row = await LinkedEntryAsync(harness, "`task` `!done` `repo:backlog`", 708);
        var pr = new EntryPullRequestLink("JSdotNet/Backlog", 708);
        harness.Client.PullRequestStates[708] = GitHubItemState.Merged;
        await harness.State.CheckPullRequestsAsync();

        harness.Client.PullRequestFailure = new GitHubException("GitHub can't be reached.");
        var restarted = await RestartAsync(harness, memory, clock: null);

        var current = restarted.Rows.Single(r => r.Id == row.Id);
        Assert.Equal(GitHubItemState.Merged, Assert.Contains(pr, current.PullRequestStates));
    }

    /// <summary>Merged is final, so a remembered merge is never asked about again —
    /// not at launch, not when the recheck interval passes.</summary>
    [Fact]
    public async Task A_remembered_merge_is_not_asked_about_again_after_a_restart()
    {
        var memory = Memory();
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var harness = BuildRemembering(memory, clock, "JSdotNet/Backlog");
        await LinkedEntryAsync(harness, "`task` `!done` `repo:backlog`", 708);
        harness.Client.PullRequestStates[708] = GitHubItemState.Merged;
        await harness.State.CheckPullRequestsAsync();
        var readsBefore = harness.Client.StatusReads + harness.Client.StateReads;

        var restarted = await RestartAsync(harness, memory, clock);
        await restarted.CheckPullRequestsAsync();
        clock.Advance(TasksDesktopState.PullRequestRecheckInterval * 2);
        await restarted.CheckPullRequestsAsync();

        Assert.Equal(readsBefore, harness.Client.StatusReads + harness.Client.StateReads);
    }

    /// <summary>
    /// The move to Done rides on a merge seen for the first time. One remembered from
    /// the last run was already acted on — or deliberately undone by the person since —
    /// so a restart does not move the entry a second time.
    /// </summary>
    [Fact]
    public async Task A_remembered_merge_does_not_move_its_entry_to_Done_again_after_a_restart()
    {
        var memory = Memory();
        var harness = BuildRemembering(memory, clock: null, "JSdotNet/Backlog");
        var row = await LinkedEntryAsync(harness, "`prompt` `!in-progress` `repo:backlog`", 708);
        harness.Client.PullRequestStates[708] = GitHubItemState.Merged;
        harness.Client.PullRequestBases[708] = "main";
        await harness.State.CheckPullRequestsAsync();
        var moved = harness.State.Rows.Single(r => r.Id == row.Id);
        Assert.Equal(EntryStatus.Done, moved.Status);
        await harness.State.ChangeStatusAsync(moved, EntryStatus.InProgress);

        var restarted = await RestartAsync(harness, memory, clock: null);
        await restarted.CheckPullRequestsAsync();
        await restarted.SyncGitHubAsync();

        Assert.Equal(EntryStatus.InProgress, restarted.Rows.Single(r => r.Id == row.Id).Status);
    }

    /// <summary>
    /// A merge read while the entry is open in the editor leaves its move owed until
    /// the editor closes. Quitting first must not lose it: the merge is remembered, but
    /// the entry was never moved, so the next launch still owes it the move.
    /// </summary>
    [Fact]
    public async Task A_move_still_owed_when_the_app_quits_lands_after_the_restart()
    {
        var memory = Memory();
        var harness = BuildRemembering(memory, clock: null, "JSdotNet/Backlog");
        var row = await LinkedEntryAsync(harness, "`prompt` `!in-progress` `repo:backlog`", 708);
        harness.Client.PullRequestStates[708] = GitHubItemState.Merged;
        harness.Client.PullRequestBases[708] = "main";

        harness.State.BeginEdit(row);
        await harness.State.CheckPullRequestsAsync();
        Assert.Equal(EntryStatus.InProgress, harness.State.Rows.Single(r => r.Id == row.Id).Status);

        var restarted = await RestartAsync(harness, memory, clock: null);
        await restarted.CheckPullRequestsAsync();

        Assert.Equal(EntryStatus.Done, restarted.Rows.Single(r => r.Id == row.Id).Status);
    }

    /// <summary>
    /// One pull request can land the work of two entries — two plan items in one
    /// change. The merge is remembered once, for the pull request, but the move is
    /// owed per entry: the second entry to record it moves to Done too.
    /// </summary>
    [Fact]
    public async Task A_remembered_merge_still_moves_another_entry_that_records_the_same_pull_request()
    {
        var memory = Memory();
        var harness = BuildRemembering(memory, clock: null, "JSdotNet/Backlog");
        var first = await LinkedEntryAsync(harness, "`prompt` `!in-progress` `repo:backlog`", 708);
        harness.Client.PullRequestStates[708] = GitHubItemState.Merged;
        harness.Client.PullRequestBases[708] = "main";
        await harness.State.CheckPullRequestsAsync();
        Assert.Equal(EntryStatus.Done, harness.State.Rows.Single(r => r.Id == first.Id).Status);

        var second = await LinkedEntryAsync(harness, "`prompt` `!in-progress` `repo:backlog`", 708);
        await harness.State.CheckPullRequestsAsync();
        Assert.Equal(EntryStatus.Done, harness.State.Rows.Single(r => r.Id == second.Id).Status);

        // And on the next launch neither moves again, nor is either moved back.
        await harness.State.ChangeStatusAsync(harness.State.Rows.Single(r => r.Id == first.Id), EntryStatus.InProgress);
        var restarted = await RestartAsync(harness, memory, clock: null);
        await restarted.CheckPullRequestsAsync();

        Assert.Equal(EntryStatus.InProgress, restarted.Rows.Single(r => r.Id == first.Id).Status);
        Assert.Equal(EntryStatus.Done, restarted.Rows.Single(r => r.Id == second.Id).Status);
    }

    /// <summary>
    /// A merge read while the entry sits at a status with no way to Done — Ready — does
    /// not move it, and must not write it off for good either: its status is the
    /// person's to change, and once it is In progress the next launch owes it the move.
    /// Only the entry's type or where the merge went rules it out for good.
    /// </summary>
    [Fact]
    public async Task A_merge_refused_only_by_the_entrys_status_is_still_owed_after_a_restart()
    {
        var memory = Memory();
        var harness = BuildRemembering(memory, clock: null, "JSdotNet/Backlog");
        var row = await LinkedEntryAsync(harness, "`prompt` `!ready` `repo:backlog`", 708);
        harness.Client.PullRequestStates[708] = GitHubItemState.Merged;
        harness.Client.PullRequestBases[708] = "main";
        await harness.State.CheckPullRequestsAsync();
        var ready = harness.State.Rows.Single(r => r.Id == row.Id);
        Assert.Equal(EntryStatus.Ready, ready.Status);

        await harness.State.ChangeStatusAsync(ready, EntryStatus.InProgress);
        var restarted = await RestartAsync(harness, memory, clock: null);
        await restarted.CheckPullRequestsAsync();

        Assert.Equal(EntryStatus.Done, restarted.Rows.Single(r => r.Id == row.Id).Status);
    }

    /// <summary>
    /// Closed is not final — GitHub lets a closed pull request be reopened — so a
    /// remembered close draws closed at launch and is asked about again once the
    /// recheck interval has passed since it was read. Read open again, it is
    /// forgotten.
    /// </summary>
    [Fact]
    public async Task A_remembered_close_is_asked_about_again_after_the_recheck_interval()
    {
        var memory = Memory();
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var harness = BuildRemembering(memory, clock, "JSdotNet/Backlog");
        var row = await LinkedEntryAsync(harness, "`task` `!done` `repo:backlog`", 708);
        var pr = new EntryPullRequestLink("JSdotNet/Backlog", 708);
        harness.Client.PullRequestStates[708] = GitHubItemState.Closed;
        await harness.State.CheckPullRequestsAsync();
        var readsBefore = harness.Client.StatusReads;

        var restarted = await RestartAsync(harness, memory, clock);
        Assert.Equal(GitHubItemState.Closed, Assert.Contains(pr, restarted.Rows.Single(r => r.Id == row.Id).PullRequestStates));
        await restarted.CheckPullRequestsAsync();
        Assert.Equal(readsBefore, harness.Client.StatusReads);

        harness.Client.PullRequestStates[708] = GitHubItemState.Open;
        clock.Advance(TasksDesktopState.PullRequestRecheckInterval);
        await restarted.CheckPullRequestsAsync();

        Assert.Equal(readsBefore + 1, harness.Client.StatusReads);
        Assert.Equal(GitHubItemState.Open, restarted.Rows.Single(r => r.Id == row.Id).PullRequestStates[pr]);
        Assert.Empty(memory.ReadAll());
    }

    /// <summary>The sync button re-reads what can still change. A pull request already
    /// known merged cannot, so a sync over nothing else asks GitHub about no pull
    /// request at all — which is what kept its toast up for minutes.</summary>
    [Fact]
    public async Task Check_GitHub_does_not_read_a_pull_request_already_known_merged()
    {
        var harness = Build("JSdotNet/Backlog");
        await LinkedEntryAsync(harness, "`task` `!done` `repo:backlog`", 708);
        await LinkedEntryAsync(harness, "`task` `!done` `repo:backlog`", 709);
        harness.Client.PullRequestStates[708] = GitHubItemState.Merged;
        harness.Client.PullRequestStates[709] = GitHubItemState.Merged;
        await harness.State.CheckPullRequestsAsync();
        var readsBefore = harness.Client.StatusReads + harness.Client.StateReads;

        await harness.State.SyncGitHubAsync();

        Assert.Equal(readsBefore, harness.Client.StatusReads + harness.Client.StateReads);
    }

    /// <summary>The read that follows a merge act lands on the row the list holds
    /// when it comes back — a reload while the act is out replaces every row, and the
    /// person who pressed Merge now is owed the merged link on the row they see.</summary>
    [Fact]
    public async Task A_reload_during_a_merge_act_still_ends_with_its_re_read_on_the_current_row()
    {
        var harness = Build("JSdotNet/Backlog");
        var row = await LinkedEntryAsync(harness, "`task` `!done` `repo:backlog`", 710);
        var id = row.Id!.Value;
        var pr = new EntryPullRequestLink("JSdotNet/Backlog", 710);
        harness.Client.PullRequestStates[710] = GitHubItemState.Open;
        harness.Client.MergeReady.Add(710);
        await harness.State.SyncGitHubAsync();
        var read = harness.State.Rows.Single(r => r.Id == id);

        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Client.StatusGates[710] = gate;
        var act = harness.State.MergeNowAsync(read, pr);
        await harness.State.ReloadFromStoreAsync();
        Assert.NotSame(read, harness.State.Rows.Single(r => r.Id == id));

        gate.SetResult();
        await act;

        Assert.Equal(["merge PR_710 Squash"], harness.Client.MergeCalls);
        var current = harness.State.Rows.Single(r => r.Id == id);
        Assert.Equal(GitHubItemState.Merged, Assert.Contains(pr, current.PullRequestStates));
    }

    /// <summary>The row stays busy until the read that follows the act has come
    /// back, so a second act cannot start from the status the first one made
    /// stale.</summary>
    [Fact]
    public async Task A_merge_act_keeps_the_row_busy_through_the_read_that_follows_it()
    {
        var (harness, row, pr) = await WithReadPullRequestAsync();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Client.StatusGates[710] = gate;

        var act = harness.State.SetAutoMergeAsync(row, pr, enable: true);

        Assert.True(row.GitHubBusy);
        await harness.State.MergeNowAsync(row, pr);
        Assert.Equal(["enable PR_710 Squash"], harness.Client.MergeCalls);

        gate.SetResult();
        await act;

        Assert.False(row.GitHubBusy);
        Assert.True(row.PullRequestStatuses[pr].AutoMergeEnabled);
    }

    [Fact]
    public async Task A_pull_request_nobody_read_is_not_acted_on()
    {
        var harness = Build("JSdotNet/Backlog");
        var row = await WriteEntryAsync(harness.State, "# Add GitHub support\n`task` `!done` `repo:backlog`\n");
        var pr = new EntryPullRequestLink("JSdotNet/Backlog", 710);
        row.PullRequestLinks = [pr];

        await harness.State.SetAutoMergeAsync(row, pr, enable: true);
        await harness.State.MergeNowAsync(row, pr);

        Assert.Empty(harness.Client.MergeCalls);
    }

    /// <summary>
    /// A sync that fails says so once, not once per row.
    /// <para>
    /// The thing that takes out a sync — an expired token, a dead network — takes
    /// out every linked row identically, and each row publishing its own toast would
    /// turn one click into a queue draining three at a time for as long as the
    /// backlog is linked. The rows still carry their own inline lines, which is
    /// where a reader finds out <em>which</em> entry; the band gets the count.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_failed_sync_reports_once_for_the_whole_sweep()
    {
        var harness = Build("JSdotNet/Backlog");

        var first = await WriteEntryAsync(harness.State, "# First\n`task` `*high` `!draft` `repo:backlog`\n");
        var second = await WriteEntryAsync(harness.State, "# Second\n`task` `*high` `!draft` `repo:backlog`\n");
        await harness.State.PushToGitHubAsync(first);
        await harness.State.PushToGitHubAsync(second);

        // Two pushes that worked say nothing, which is what makes the count below
        // unambiguous: everything in the tray afterwards came from the sync.
        Assert.Empty(harness.Toasts.Visible);

        harness.Client.Failure = new GitHubException("GitHub rejected the token — check it hasn't expired.");

        await harness.State.SyncGitHubAsync();

        var toast = Assert.Single(harness.Toasts.Visible);
        Assert.Equal("github-error", toast.TestId);
        Assert.Equal(ToastSeverity.Error, toast.Severity);
        Assert.Equal("2 of 2 tasks couldn't be read from GitHub.", toast.Message);

        // The record stays on the entries, one line each, exactly as before.
        Assert.NotNull(first.GitHubError);
        Assert.NotNull(second.GitHubError);
    }

    /// <summary>One row asked about is one row answered about, by name — the
    /// per-row toast is the path the sweep deliberately opts out of.</summary>
    [Fact]
    public async Task Refreshing_one_entry_names_it()
    {
        var harness = Build("JSdotNet/Backlog");

        var row = await WriteEntryAsync(harness.State, "# Add GitHub support\n`task` `*high` `!draft` `repo:backlog`\n");
        await harness.State.PushToGitHubAsync(row);

        harness.Client.Failure = new GitHubException("GitHub rejected the token — check it hasn't expired.");

        await harness.State.RefreshGitHubAsync(row);

        var toast = Assert.Single(harness.Toasts.Visible);
        Assert.Equal("github-error", toast.TestId);
        Assert.Contains(row.PreviewTitle, toast.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Monitoring_still_works_for_a_repository_since_removed_from_settings()
    {
        var harness = Build("JSdotNet/Backlog");

        var row = await WriteEntryAsync(harness.State, "# Add GitHub support\n`task` `*high` `!draft` `repo:backlog`\n");
        await harness.State.PushToGitHubAsync(row);

        harness.Integration.Settings.SetRepositories([]);

        await harness.State.RefreshGitHubAsync(row);

        Assert.Null(row.GitHubError);
        Assert.NotNull(row.Snapshot);
    }


    /// <summary>
    /// Editing one step touches that chapter and nothing around it.
    /// <para>
    /// The desktop edits a step in the body's markdown now, so this holds the
    /// parser's own step rewrites to it, a level down: both end in a
    /// <c>ReplaceSubItemText</c> against the same index, and an edit that reached
    /// the wrong chapter would overwrite a neighbour with no error anywhere.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Editing_a_sub_item_updates_only_that_chapter()
    {
        var harness = Build("JSdotNet/Backlog");
        var row = await WriteEntryAsync(harness.State,
            "# Parent\n" +
            "`task` `*medium` `!draft` `repo:backlog`\n\n" +
            "## First\n" +
            "Keep this.\n\n" +
            "### Target\n" +
            "Old target notes.\n\n" +
            "## Last\n" +
            "Keep last.\n");

        var text = EntryTextParser.WithSubItemTitle(row.RawText, 1, "Updated target");
        text = EntryTextParser.WithSubItemNote(text, 1, "New target notes.");

        Assert.Contains("## First\nKeep this.", text);
        // Same line break the chapter already had. Writing notes back must not
        // introduce a blank line the author did not type.
        Assert.Contains("### Updated target\nNew target notes.", text);
        Assert.DoesNotContain("Old target notes.", text);
        Assert.Contains("## Last\nKeep last.", text);
    }

    [Fact]
    public async Task Editing_parent_entry_updates_only_the_parent_chapter()
    {
        var harness = Build("JSdotNet/Backlog");
        var row = await WriteEntryAsync(harness.State,
            "# Parent\n" +
            "`task` `*medium` `!draft` `repo:backlog`\n\n" +
            "Old parent notes.\n\n" +
            "## Child\n" +
            "Keep child.\n\n" +
            "### Nested\n" +
            "Keep nested.\n");

        harness.State.BeginEdit(row);
        Assert.DoesNotContain("## Child", harness.State.EntryEditText(row));

        harness.State.OnRawTextInput(row,
            "# Parent\n`task` `*medium` `!ready` `repo:backlog`\n\nNew parent notes.");
        await harness.State.EndEditAsync(row);

        Assert.Contains("New parent notes.", row.RawText);
        Assert.DoesNotContain("Old parent notes.", row.RawText);
        Assert.Contains("## Child\nKeep child.", row.RawText);
        Assert.Contains("### Nested\nKeep nested.", row.RawText);
    }

    /// <summary>
    /// A sub-item push used to be asserted here, filing one chapter as its own issue
    /// in the parent's repository. It has gone with the method behind it, and the
    /// reason is the model rather than the plumbing: <c>.devbook/domain/tasks/domain.md</c>
    /// gives <c>ProjectionRef</c> to the entry and says a Sub-Item "may project to
    /// GitHub issue task-list checkboxes" — checkboxes inside the entry's issue. A
    /// step filed as its own issue had nowhere to record the link, so nothing could
    /// tell it had already been filed.
    /// <para>
    /// Deleted rather than left behind, deliberately, and in the same change as the
    /// method: a passing test over an unreachable method reads as coverage of a
    /// feature that no longer exists, which is the state that made these buttons
    /// disappear-and-reappear twice already.
    /// </para>
    /// </summary>
    [Fact]
    public async Task An_entry_push_still_uses_the_repository_the_entry_targets()
    {
        var harness = Build("backlog = JSdotNet/Backlog", "other = someone/else");
        var row = await WriteEntryAsync(harness.State,
            "# Parent\n" +
            "`task` `*medium` `!draft` `repo:backlog`\n\n" +
            "## Child\n" +
            "`task` `*high` `!ready` `repo:other` `#child`\n" +
            "Child notes.\n");

        await harness.State.PushToGitHubAsync(row);

        Assert.Null(row.GitHubError);
        Assert.Equal("JSdotNet/Backlog", harness.Client.CreatedRepository);

        // The whole entry: its own title, and the chapter carried inside its body
        // rather than filed separately.
        Assert.Equal("Parent", harness.Client.CreatedTitle);
        Assert.Contains("## Child", harness.Client.CreatedBody);
    }

    [Fact]
    public void Nothing_about_github_shows_until_a_repository_is_configured()
    {
        Assert.False(Build().State.GitHubConfigured);
    }

    [Fact]
    public async Task Feedback_reports_upload_the_screenshot_and_link_the_real_url_in_the_issue()
    {
        var harness = Build("someone/else");
        var screenshot = new GitHubFeedbackScreenshot(
            "data:image/jpeg;base64,AAAA",
            "image/jpeg",
            800,
            600,
            42);

        var link = await harness.Feedback.ReportAsync("Broken view", "The pane is blank.", screenshot, cancellationToken: TestContext.Current.CancellationToken);

        // A data: URL embedded straight in the body is stripped by GitHub's
        // markdown sanitizer and never renders — the fix commits the screenshot
        // to the repository first and links the real URL that comes back.
        Assert.Equal("JSdotNet/Backlog", harness.Client.UploadedRepository);
        Assert.Equal("feedback-screenshots", harness.Client.UploadedBranch);
        Assert.Equal("JSdotNet/Backlog", harness.Client.CreatedRepository);
        Assert.Equal("[Feedback][Desktop app] Broken view", harness.Client.CreatedTitle);
        Assert.DoesNotContain("## Desktop app screen area", harness.Client.CreatedBody);
        Assert.Contains("The pane is blank.", harness.Client.CreatedBody);
        Assert.Contains($"![Screenshot]({FakeGitHubClient.UploadedDownloadUrl})", harness.Client.CreatedBody);
        Assert.DoesNotContain("data:image", harness.Client.CreatedBody);
        Assert.Equal("JSdotNet/Backlog", link.RepoFullName);
    }

    [Fact]
    public async Task Feedback_reports_include_the_screenshot_failure_when_capture_fails()
    {
        var harness = Build("JSdotNet/Backlog");

        await harness.Feedback.ReportAsync("Cannot capture", null, null, "Screenshot capture failed: Permission denied.", TestContext.Current.CancellationToken);

        Assert.Equal("JSdotNet/Backlog", harness.Client.CreatedRepository);
        Assert.Contains("_No details provided._", harness.Client.CreatedBody);
        Assert.Contains("Screenshot capture failed: Permission denied.", harness.Client.CreatedBody);
    }

    /// <summary>
    /// The body records the failure the caller reported, in the caller's words.
    /// The sentence used to be written here — "Screenshot capture failed:" in
    /// front of everything — which was true for as long as a screen capture was
    /// the only way an image arrived. It stopped being true twice over: a
    /// clipboard the WebView refuses is filed as a capture nobody attempted, and
    /// the upload failure, which already says what it is, came out doubled.
    /// </summary>
    [Theory]
    [InlineData("Screenshot capture failed: Permission denied.")]
    [InlineData("Reading the clipboard failed: Clipboard access was denied.")]
    public async Task The_body_records_the_failure_in_the_words_it_was_reported_in(string screenshotError)
    {
        var harness = Build("JSdotNet/Backlog");

        await harness.Feedback.ReportAsync("Cannot attach", null, null, screenshotError, TestContext.Current.CancellationToken);

        Assert.Contains(screenshotError, harness.Client.CreatedBody);
        Assert.DoesNotContain($"Screenshot capture failed: {screenshotError}", harness.Client.CreatedBody);
    }

    /// <summary>The line above the embed describes the image, and a pasted one
    /// was never captured. Nothing here can tell the two apart — the screenshot
    /// carries no origin — so the sentence says only what is true of both.</summary>
    [Fact]
    public async Task The_body_does_not_call_a_pasted_image_a_capture()
    {
        var harness = Build("JSdotNet/Backlog");
        var screenshot = new GitHubFeedbackScreenshot("data:image/png;base64,AAAA", "image/png", 640, 480, 24);

        await harness.Feedback.ReportAsync("Broken view", null, screenshot, cancellationToken: TestContext.Current.CancellationToken);

        Assert.DoesNotContain("Captured", harness.Client.CreatedBody);
        Assert.Contains("image/png, 640 x 480, 24 bytes", harness.Client.CreatedBody);
        Assert.Contains($"![Screenshot]({FakeGitHubClient.UploadedDownloadUrl})", harness.Client.CreatedBody);
    }

    /// <summary>Both origins, because a pasted image takes the same upload and
    /// must fall back the same way — the report is the point, the picture is
    /// evidence.</summary>
    [Theory]
    [InlineData("image/jpeg")]
    [InlineData("image/png")]
    public async Task A_screenshot_upload_failure_still_files_the_issue(string mediaType)
    {
        var harness = Build("JSdotNet/Backlog");
        harness.Client.UploadFailure = new GitHubException("GitHub refused the request — the token may lack repo scope.");
        var screenshot = new GitHubFeedbackScreenshot($"data:{mediaType};base64,AAAA", mediaType, 800, 600, 42);

        var link = await harness.Feedback.ReportAsync("Broken view", "The pane is blank.", screenshot, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("JSdotNet/Backlog", link.RepoFullName);
        Assert.Contains("Screenshot upload failed:", harness.Client.CreatedBody);
        // Once, not wrapped in a second sentence about a capture that succeeded.
        Assert.DoesNotContain("Screenshot capture failed", harness.Client.CreatedBody);
        Assert.DoesNotContain("![Screenshot]", harness.Client.CreatedBody);
    }

    /// <summary>
    /// The committed file is named after what it actually is. The capture path
    /// only ever produced a JPEG, so the extension was hard-coded to match it;
    /// a pasted image is whatever the clipboard held, and a PNG committed as
    /// <c>.jpg</c> is a file GitHub serves under the wrong type.
    /// </summary>
    [Theory]
    [InlineData("image/jpeg", ".jpg")]
    [InlineData("image/png", ".png")]
    [InlineData("image/webp", ".webp")]
    [InlineData("image/gif", ".gif")]
    public async Task The_committed_file_takes_its_extension_from_the_media_type(string mediaType, string extension)
    {
        var harness = Build("JSdotNet/Backlog");
        var screenshot = new GitHubFeedbackScreenshot($"data:{mediaType};base64,AAAA", mediaType, 640, 480, 24);

        await harness.Feedback.ReportAsync("Broken view", null, screenshot, cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(harness.Client.UploadedPath);
        Assert.EndsWith(extension, harness.Client.UploadedPath);
        Assert.StartsWith("feedback-screenshots/", harness.Client.UploadedPath);
    }

    /// <summary>A row with one recorded pull request, open and read, so a merge act
    /// has a status to act on.</summary>
    private async Task<(Harness Harness, EntryRow Row, EntryPullRequestLink PullRequest)> WithReadPullRequestAsync(bool autoMerge = false)
    {
        var harness = Build("JSdotNet/Backlog");
        var row = await WriteEntryAsync(harness.State, "# Add GitHub support\n`task` `!done` `repo:backlog`\n");
        var pr = new EntryPullRequestLink("JSdotNet/Backlog", 710);
        row.PullRequestLinks = [pr];
        harness.Client.PullRequestStates[710] = GitHubItemState.Open;
        if (autoMerge) harness.Client.AutoMergeOn.Add(710);

        await harness.State.SyncGitHubAsync();

        return (harness, row, pr);
    }

    /// <summary>An entry with one step, written with <paramref name="metadata"/> and a
    /// pull request recorded on it the way <c>link_change</c> records one, then read
    /// back into the list.</summary>
    private async Task<EntryRow> LinkedEntryAsync(Harness harness, string metadata, int pullRequest)
    {
        var written = await WriteEntryAsync(harness.State, $"# Ship it\n{metadata}\n\nDo it.\n\n## Step one\n`task` `!in-progress`\n");
        var entries = TasksTestHost.EntriesFor(harness.Store);
        await entries.LinkToIssueAsync(
            written.Id!.Value,
            "JSdotNet/Backlog",
            pullRequest.ToString(System.Globalization.CultureInfo.InvariantCulture),
            EntryProjectionDto.PullRequestTargetType,
            TestContext.Current.CancellationToken);
        await harness.State.ReloadFromStoreAsync();
        return harness.State.Rows.Single(r => r.Id == written.Id);
    }

    /// <summary>Types <paramref name="prose"/> as the entry's body through the detail
    /// pane's markdown block and lets its debounced save land.</summary>
    private static async Task TypeBodyAndLetItSaveAsync(Harness harness, FakeTimeProvider clock, Backlog.Modules.Tasks.Abstractions.Services.ITaskItems entries, Guid id, string prose)
    {
        var row = harness.State.Rows.Single(r => r.Id == id);
        harness.State.ChangeBody(row, $"Do it. {prose}\n\n## Step one\n`task` `!in-progress`\n");
        clock.Advance(TimeSpan.FromSeconds(1));

        var body = string.Empty;
        await Polling.WaitUntilAsync(
            async () =>
            {
                body = (await entries.GetAsync(id, TestContext.Current.CancellationToken))!.Body;
                return body.Contains(prose, StringComparison.Ordinal) && harness.State.SaveState == AppSaveState.Saved;
            },
            () => $"The debounced save never landed; the store holds: {body}");
    }

    private async Task<EntryRow> WriteEntryAsync(TasksDesktopState state, string text)
    {
        state.NewRow();
        var row = state.Rows[^1];
        state.OnRawTextInput(row, text);
        await state.EndEditAsync(row);
        return row;
    }

    private Harness Build(params string[] repositories) => Build(clock: null, repositories);

    private Harness Build(TimeProvider? clock, params string[] repositories) =>
        BuildWatchingStore(clock, storeCalls: null, repositories);

    /// <summary>As <see cref="Build(TimeProvider?, string[])"/>, with the state's calls
    /// to the store going through <paramref name="storeCalls"/> — for a test that
    /// holds a save on its way.</summary>
    private Harness BuildWatchingStore(TimeProvider? clock, TaskStoreCalls? storeCalls, params string[] repositories)
    {
        var root = Path.Combine(Path.GetTempPath(), "backlog-github-flow", Guid.NewGuid().ToString("n"));
        _tempDirs.Add(root);

        var store = new WorkspaceSettingsStore(root, Path.Combine(root, "settings.json"));
        Assert.Null(store.TryUseRoot(root));

        var settings = new GitHubSettingsStore(Path.Combine(root, "github.json"));
        var (parsed, _) = GitHubSettings.ParseText(string.Join('\n', repositories));
        settings.SetRepositories(parsed);

        var client = new FakeGitHubClient();
        var integration = new GitHubIntegration(settings, client, new FakeProbe());
        var toasts = new ToastChannel();

        return new Harness(StateFor(store, integration, toasts, clock, storeCalls), client, store, integration, new FeedbackReporter(integration), toasts, settings);
    }

    /// <summary>As <see cref="Build(TimeProvider?, string[])"/>, with the pull-request
    /// outcomes kept in <paramref name="memory"/>.</summary>
    private Harness BuildRemembering(IPullRequestStateMemory memory, TimeProvider? clock, params string[] repositories)
    {
        var harness = BuildWatchingStore(clock, storeCalls: null, repositories);
        var state = TasksTestHost.StateFor(harness.Store, harness.Integration, toasts: harness.Toasts, clock: clock, pullRequestMemory: memory);
        _states.Add(state);
        return harness with { State = state };
    }

    /// <summary>The app launched again over the same workspace, GitHub and outcome
    /// memory: a fresh list state, loaded.</summary>
    private async Task<TasksDesktopState> RestartAsync(Harness harness, IPullRequestStateMemory memory, TimeProvider? clock)
    {
        var state = TasksTestHost.StateFor(harness.Store, harness.Integration, toasts: harness.Toasts, clock: clock, pullRequestMemory: memory);
        _states.Add(state);
        await state.ReloadFromStoreAsync();
        return state;
    }

    /// <summary>An outcome memory over a folder of its own, removed with the test's.</summary>
    private PullRequestStateMemory Memory()
    {
        var root = Path.Combine(Path.GetTempPath(), "backlog-github-flow", Guid.NewGuid().ToString("n"));
        _tempDirs.Add(root);
        return new PullRequestStateMemory(() => root);
    }

    /// <summary>The list state, remembered so <see cref="Dispose"/> can hand back
    /// the timed saves it arms.</summary>
    private TasksDesktopState StateFor(WorkspaceSettingsStore store, GitHubIntegration integration, IToastChannel? toasts = null, TimeProvider? clock = null, TaskStoreCalls? storeCalls = null)
    {
        var state = TasksTestHost.StateFor(store, integration, toasts: toasts, clock: clock, storeCalls: storeCalls);
        _states.Add(state);
        return state;
    }

    private sealed record Harness(
        TasksDesktopState State,
        FakeGitHubClient Client,
        WorkspaceSettingsStore Store,
        GitHubIntegration Integration,
        FeedbackReporter Feedback,
        ToastChannel Toasts,
        GitHubSettingsStore Settings);

    private sealed class FakeGitHubClient : IGitHubClient
    {
        public Task<GitHubCommittedFile> CommitFileAsync(GitHubRepositoryRef repository, string path, byte[] content, string commitMessage, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public const string UploadedDownloadUrl = "https://raw.githubusercontent.com/JSdotNet/Backlog/feedback-screenshots/feedback-screenshots/fake.jpg";

        public int CreateCount { get; set; }
        public string? CreatedRepository { get; private set; }
        public string? CreatedTitle { get; private set; }
        public string? CreatedBody { get; private set; }
        public IReadOnlyList<string> CreatedLabels { get; private set; } = [];
        public Exception? Failure { get; set; }
        public Exception? UploadFailure { get; set; }
        public string? UploadedRepository { get; private set; }
        public string? UploadedBranch { get; private set; }
        public string? UploadedPath { get; private set; }
        public byte[]? UploadedContent { get; private set; }

        public GitHubIssueSnapshot Snapshot { get; set; } = new(
            new GitHubIssue(101, "https://github.com/JSdotNet/Backlog/issues/101", "Add GitHub support", GitHubItemState.Open, null),
            [],
            DateTimeOffset.UtcNow);

        /// <summary>The next issue creation is held open until the test releases it.</summary>
        public TaskCompletionSource? CreateGate { get; set; }

        public async Task<GitHubIssue> CreateIssueAsync(
            GitHubRepositoryRef repository,
            string title,
            string? body,
            IEnumerable<string>? labels = null,
            CancellationToken cancellationToken = default)
        {
            if (Failure is not null) throw Failure;

            if (CreateGate is { } gate)
            {
                CreateGate = null;
                await gate.Task;

                // What GitHub said once it answered: a test can make the held push fail.
                if (Failure is not null) throw Failure;
            }

            CreateCount++;
            CreatedRepository = repository.FullName;
            CreatedTitle = title;
            CreatedBody = body ?? string.Empty;
            CreatedLabels = labels?.ToList() ?? [];

            return new GitHubIssue(
                101,
                $"https://github.com/{repository.FullName}/issues/101",
                title,
                GitHubItemState.Open,
                DateTimeOffset.UtcNow);
        }

        /// <summary>The next issue read is held open until the test releases it, then
        /// answers with <see cref="Snapshot"/> as it stands at that moment.</summary>
        public TaskCompletionSource? IssueGate { get; set; }

        public async Task<GitHubIssueSnapshot> GetIssueAsync(
            GitHubRepositoryRef repository,
            int number,
            CancellationToken cancellationToken = default)
        {
            if (Failure is not null) throw Failure;

            if (IssueGate is { } gate)
            {
                IssueGate = null;
                await gate.Task;
            }

            return Snapshot;
        }

        public Dictionary<int, GitHubItemState> PullRequestStates { get; } = [];
        public Exception? PullRequestFailure { get; set; }

        /// <summary>How many times a pull request's status, and its state over REST,
        /// were asked for.</summary>
        public int StatusReads { get; private set; }
        public int StateReads { get; private set; }

        /// <summary>What one pull request's reads throw, by number, when set.</summary>
        public Dictionary<int, Exception> PullRequestFailures { get; } = [];

        public Task<GitHubPullRequest> GetPullRequestAsync(
            GitHubRepositoryRef repository,
            int number,
            CancellationToken cancellationToken = default)
        {
            StateReads++;

            if (PullRequestFailures.TryGetValue(number, out var failure)) throw failure;

            if (PullRequestFailure is not null) throw PullRequestFailure;
            if (!PullRequestStates.TryGetValue(number, out var state)) throw new GitHubException("Not Found");

            return Task.FromResult(new GitHubPullRequest(
                number,
                $"https://github.com/{repository.FullName}/pull/{number}",
                "A pull request",
                state,
                repository.FullName)
            {
                BaseRefName = PullRequestBases.GetValueOrDefault(number),
                DefaultBranch = DefaultBranch
            });
        }

        /// <summary>Whether the batched read by number answers. Off, it refuses as a
        /// client that cannot read linked pull requests does, so a test that does not ask
        /// for it reads one pull request at a time, as before the batched read.</summary>
        public bool AnswersLinkedReads { get; set; }

        /// <summary>How many batched reads were sent — one per repository asked.</summary>
        public int LinkedReads { get; private set; }

        /// <summary>The repositories whose batched read is refused.</summary>
        public HashSet<string> LinkedReadFailures { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Task<IReadOnlyList<GitHubLinkedPullRequest>> ReadLinkedPullRequestsAsync(
            GitHubRepositoryRef repository,
            IReadOnlyCollection<int> numbers,
            CancellationToken cancellationToken = default)
        {
            if (!AnswersLinkedReads) throw new GitHubException("This GitHub client cannot read linked pull requests.");

            LinkedReads++;
            if (LinkedReadFailures.Contains(repository.FullName)) throw new GitHubException("GitHub refused the read.");

            IReadOnlyList<GitHubLinkedPullRequest> answer =
            [
                .. numbers
                    .Where(PullRequestStates.ContainsKey)
                    .Select(number => new GitHubLinkedPullRequest(
                        number,
                        $"https://github.com/{repository.FullName}/pull/{number}",
                        "A pull request",
                        repository.FullName,
                        PullRequestStates[number],
                        AuthorLogin: null,
                        MergedAt: null)
                    {
                        BaseRefName = PullRequestBases.GetValueOrDefault(number),
                        DefaultBranch = DefaultBranch
                    })
            ];

            return Task.FromResult(answer);
        }

        /// <summary>The branch each pull request merges into; absent is unknown, the
        /// way a read that did not say answers.</summary>
        public Dictionary<int, string> PullRequestBases { get; } = [];

        /// <summary>The repository's default branch every read answers with.</summary>
        public string? DefaultBranch { get; set; } = "main";

        /// <summary>The roll-up each pull request answers with; absent is no checks.</summary>
        public Dictionary<int, GitHubCheckState> PullRequestChecks { get; } = [];

        /// <summary>The pull requests GitHub is holding for auto-merge. Enabling and
        /// disabling move a number in and out, so a re-read after the act sees it.</summary>
        public HashSet<int> AutoMergeOn { get; } = [];

        /// <summary>The pull requests whose merge state is CLEAN.</summary>
        public HashSet<int> MergeReady { get; } = [];

        /// <summary>What the GraphQL status query alone throws, when set — the REST
        /// read beside it still answers, the way a server whose schema lacks the
        /// auto-merge fields would.</summary>
        public Exception? StatusFailure { get; set; }

        /// <summary>What every merge act throws, when set — GitHub's own words, so the
        /// façade's mapping is part of what a test sees.</summary>
        public Exception? MergeFailure { get; set; }

        /// <summary>Every merge act asked for, as "act node method".</summary>
        public List<string> MergeCalls { get; } = [];

        /// <summary>Status reads held open until the test releases them, by number —
        /// how a test puts a read in flight while something else happens.</summary>
        public Dictionary<int, TaskCompletionSource> StatusGates { get; } = [];

        public async Task<GitHubPullRequestStatus> GetPullRequestStatusAsync(
            GitHubRepositoryRef repository,
            int number,
            CancellationToken cancellationToken = default)
        {
            StatusReads++;

            if (PullRequestFailures.TryGetValue(number, out var failure)) throw failure;

            if (StatusGates.Remove(number, out var gate)) await gate.Task;

            if (PullRequestFailure is not null) throw PullRequestFailure;
            if (StatusFailure is not null) throw StatusFailure;
            if (!PullRequestStates.TryGetValue(number, out var state)) throw new GitHubException("Not Found");

            return new GitHubPullRequestStatus(
                number,
                repository.FullName,
                $"PR_{number}",
                state,
                PullRequestChecks.GetValueOrDefault(number),
                AutoMergeOn.Contains(number),
                MergeReady.Contains(number),
                GitHubMergeMethod.Squash)
            {
                BaseRefName = PullRequestBases.GetValueOrDefault(number),
                DefaultBranch = DefaultBranch
            };
        }

        public Task EnableAutoMergeAsync(GitHubRepositoryRef repository, string pullRequestId, GitHubMergeMethod method, CancellationToken cancellationToken = default)
        {
            if (MergeFailure is not null) throw MergeFailure;

            MergeCalls.Add($"enable {pullRequestId} {method}");
            AutoMergeOn.Add(NumberOf(pullRequestId));
            return Task.CompletedTask;
        }

        public Task DisableAutoMergeAsync(GitHubRepositoryRef repository, string pullRequestId, CancellationToken cancellationToken = default)
        {
            if (MergeFailure is not null) throw MergeFailure;

            MergeCalls.Add($"disable {pullRequestId}");
            AutoMergeOn.Remove(NumberOf(pullRequestId));
            return Task.CompletedTask;
        }

        public Task MergePullRequestAsync(GitHubRepositoryRef repository, string pullRequestId, GitHubMergeMethod method, CancellationToken cancellationToken = default)
        {
            if (MergeFailure is not null) throw MergeFailure;

            MergeCalls.Add($"merge {pullRequestId} {method}");
            PullRequestStates[NumberOf(pullRequestId)] = GitHubItemState.Merged;
            return Task.CompletedTask;
        }

        private static int NumberOf(string pullRequestId) => int.Parse(pullRequestId["PR_".Length..], System.Globalization.CultureInfo.InvariantCulture);

        public Task<GitHubUploadedFile> UploadFileAsync(
            GitHubRepositoryRef repository,
            string path,
            string branch,
            byte[] content,
            string commitMessage,
            CancellationToken cancellationToken = default)
        {
            if (UploadFailure is not null) throw UploadFailure;

            UploadedRepository = repository.FullName;
            UploadedBranch = branch;
            UploadedPath = path;
            UploadedContent = content;

            return Task.FromResult(new GitHubUploadedFile(path, UploadedDownloadUrl));
        }
    }

    private sealed class FakeProbe : IGitHubConnectionProbe
    {
        public Task<GitHubConnection> DescribeAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new GitHubConnection(true, "Connected."));

        public void Invalidate()
        {
        }
    }
}
