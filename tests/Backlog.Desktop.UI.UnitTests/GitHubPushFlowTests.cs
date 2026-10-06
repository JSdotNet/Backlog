using Backlog.Modules.Tasks;
using Backlog.Modules.Tasks.DomainModels;
using Backlog.Infrastructure.GitHub;
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

    /// <summary>Done is when an entry's work is waiting to land, and the only
    /// status the check asks about.</summary>
    [Fact]
    public async Task An_entry_not_done_is_not_read_by_the_check()
    {
        var harness = Build("JSdotNet/Backlog");

        var row = await WriteEntryAsync(harness.State, "# Add GitHub support\n`task` `!in-progress` `repo:backlog`\n");
        row.PullRequestLinks = [new EntryPullRequestLink("JSdotNet/Backlog", 708)];
        harness.Client.PullRequestStates[708] = GitHubItemState.Merged;

        await harness.State.CheckPullRequestsAsync();

        Assert.NotEqual(EntryStatus.Done, row.Status);
        Assert.Equal(0, harness.Client.StatusReads);
        Assert.Empty(row.PullRequestStates);
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
        harness.Client.PullRequestStates[708] = GitHubItemState.Open;
        await harness.State.SyncGitHubAsync();

        Assert.Equal(GitHubItemState.Open, row.PullRequestStates[pr]);
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
    /// The route changed and the claim did not. There used to be a raw textarea per
    /// sub-item card, handed the chapter's whole text; a step's title and its notes
    /// are now two controls in the detail pane, each reporting through the shared
    /// task row. Both still end in a <c>ReplaceSubItemText</c> against the same
    /// index, which is exactly what this has always been about: an edit that reached
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

        await harness.State.RenameSubItemAsync(row, 1, "Updated target");
        harness.State.ChangeSubItemNote(row, 1, "New target notes.");

        Assert.Contains("## First\nKeep this.", row.RawText);
        // Same line break the chapter already had. Writing notes back must not
        // introduce a blank line the author did not type.
        Assert.Contains("### Updated target\nNew target notes.", row.RawText);
        Assert.DoesNotContain("Old target notes.", row.RawText);
        Assert.Contains("## Last\nKeep last.", row.RawText);
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

    private async Task<EntryRow> WriteEntryAsync(TasksDesktopState state, string text)
    {
        state.NewRow();
        var row = state.Rows[^1];
        state.OnRawTextInput(row, text);
        await state.EndEditAsync(row);
        return row;
    }

    private Harness Build(params string[] repositories) => Build(clock: null, repositories);

    private Harness Build(TimeProvider? clock, params string[] repositories)
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

        return new Harness(StateFor(store, integration, toasts, clock), client, store, integration, new FeedbackReporter(integration), toasts);
    }

    /// <summary>The list state, remembered so <see cref="Dispose"/> can hand back
    /// the timed saves it arms.</summary>
    private TasksDesktopState StateFor(WorkspaceSettingsStore store, GitHubIntegration integration, IToastChannel? toasts = null, TimeProvider? clock = null)
    {
        var state = TasksTestHost.StateFor(store, integration, toasts: toasts, clock: clock);
        _states.Add(state);
        return state;
    }

    private sealed record Harness(
        TasksDesktopState State,
        FakeGitHubClient Client,
        WorkspaceSettingsStore Store,
        GitHubIntegration Integration,
        FeedbackReporter Feedback,
        ToastChannel Toasts);

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

        public Task<GitHubIssue> CreateIssueAsync(
            GitHubRepositoryRef repository,
            string title,
            string? body,
            IEnumerable<string>? labels = null,
            CancellationToken cancellationToken = default)
        {
            if (Failure is not null) throw Failure;

            CreateCount++;
            CreatedRepository = repository.FullName;
            CreatedTitle = title;
            CreatedBody = body ?? string.Empty;
            CreatedLabels = labels?.ToList() ?? [];

            return Task.FromResult(new GitHubIssue(
                101,
                $"https://github.com/{repository.FullName}/issues/101",
                title,
                GitHubItemState.Open,
                DateTimeOffset.UtcNow));
        }

        public Task<GitHubIssueSnapshot> GetIssueAsync(
            GitHubRepositoryRef repository,
            int number,
            CancellationToken cancellationToken = default)
        {
            if (Failure is not null) throw Failure;
            return Task.FromResult(Snapshot);
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
                repository.FullName));
        }

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
                GitHubMergeMethod.Squash);
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
