using Backlog.Desktop.UI.Tasks;
using Backlog.Infrastructure.FileSystem;
using Backlog.Infrastructure.FileSystem.Progress;
using Backlog.Infrastructure.FileSystem.Tasks;
using Backlog.Infrastructure.GitHub;
using Backlog.Modules.RepositoryManagement.Abstractions;
using Backlog.Modules.RepositoryManagement.Abstractions.DataTransferObjects;
using Backlog.Modules.RepositoryManagement.Abstractions.Services;
using Backlog.Modules.Tasks;
using Backlog.Modules.Tasks.Abstractions.DataTransferObjects;
using Backlog.Modules.Tasks.Abstractions.Services;
using Microsoft.Extensions.Time.Testing;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The task row through Repository Management's one contract (local ADR 0042 §4, V10):
/// the task list, the Pull requests pane and Progress composed over one module, the way
/// the hosts compose them, over one GitHub client that counts what it is sent. A merge
/// from the row is what the pane and Progress read next, with no read of their own; a
/// merge from the pane reaches the row's entry the same way.
/// </summary>
[Collection(WorkspaceSettingsCollection.Name)]
public sealed class TasksPullRequestContractTests : IDisposable
{
    private const string Repository = "JSdotNet/Backlog";

    private static readonly DateTimeOffset Noon = new(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);

    private readonly List<string> _tempDirs = [];
    private readonly List<TasksDesktopState> _states = [];

    public void Dispose()
    {
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

    /// <summary>
    /// Merge now from the task row: the contract acts and reads the repository once, the
    /// entry moves to Done on that read, and the pane-shaped read (its repository's open
    /// list and its pins) and the Progress-shaped read (by number) then read the pull
    /// request merged without asking GitHub anything more.
    /// </summary>
    [Fact]
    public async Task A_merge_from_the_task_row_is_what_the_pane_and_progress_read_next()
    {
        var world = await ComposeAsync();
        var row = await world.LinkedPromptAsync(710);
        await world.State.CheckPullRequestsAsync();
        var offer = world.State.MergeOfferFor(world.Row(row));
        Assert.Equal(LinkedPullRequestMerge.MergeNow, offer?.Act);

        await world.State.MergeNowAsync(world.Row(row), offer!.PullRequest);
        var sent = world.Sent();

        var listed = await world.Contract.ListOpenAsync([Repository], PullRequestReadOptions.Default, TestContext.Current.CancellationToken);
        var pinned = await world.Contract.ReadAsync([new PullRequestReference(Repository, 710)], PullRequestReadOptions.Default, TestContext.Current.CancellationToken);
        var goals = await new RepositoryManagementGoalPullRequests(() => world.Contract)
            .ReadAsync([new(Repository, "710")], TestContext.Current.CancellationToken);

        Assert.Equal(["merge 710"], world.Client.MergeCalls);
        Assert.Equal(EntryStatus.Done, world.Row(row).Status);
        Assert.Empty(listed.PullRequests);
        Assert.Equal(PullRequestState.Merged, Assert.Single(pinned.PullRequests).State);
        Assert.Equal(Backlog.Modules.Progress.Abstractions.LinkedPullRequestState.Merged, Assert.Single(goals).State);
        Assert.Equal(sent, world.Sent());
    }

    /// <summary>
    /// A merge from the Pull requests pane — the contract's command, outside the task
    /// list — is heard by the list: it asks the shell for a check at once rather than at
    /// the next tick, and that check takes the merge from the command's own read, moving
    /// the entry to Done without asking GitHub again.
    /// </summary>
    [Fact]
    public async Task A_merge_from_the_pull_requests_pane_moves_the_task_on_the_contracts_own_read()
    {
        var world = await ComposeAsync();
        var row = await world.LinkedPromptAsync(710);
        await world.State.CheckPullRequestsAsync();
        var recheckAsked = 0;
        world.State.PullRequestRecheckDue += () => recheckAsked++;

        var merged = await world.Contract.MergeAsync(new PullRequestReference(Repository, 710), TestContext.Current.CancellationToken);
        var sent = world.Sent();
        await world.State.CheckPullRequestsAsync();

        Assert.True(merged.IsSuccess);
        Assert.Equal(1, recheckAsked);
        Assert.Equal(EntryStatus.Done, world.Row(row).Status);
        Assert.Equal(sent, world.Sent());
    }

    private async Task<World> ComposeAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "backlog-pr-contract", Guid.NewGuid().ToString("n"));
        _tempDirs.Add(root);

        var store = new WorkspaceSettingsStore(root, Path.Combine(root, "settings.json"));
        Assert.Null(store.TryUseRoot(root));

        var settings = new GitHubSettingsStore(Path.Combine(root, "github.json"));
        var (parsed, _) = GitHubSettings.ParseText(Repository);
        settings.SetRepositories(parsed);

        var client = new TasksPaneHost.FakeGitHubClient();
        client.PullRequestStatuses[710] = new GitHubPullRequestStatus(
            710, Repository, "PR_710", GitHubItemState.Open, GitHubCheckState.Passing,
            AutoMergeEnabled: false, MergeReady: true, GitHubMergeMethod.Squash)
        {
            BaseRefName = "main",
            DefaultBranch = "main"
        };
        var gitHub = new GitHubIntegration(settings, client, new StubProbe(new GitHubConnection(true, "Connected.")));
        var clock = new FakeTimeProvider(Noon);
        var contract = RepositoryManagementChain.For(gitHub, clock);

        var state = TasksTestHost.StateFor(store, gitHub, clock: clock, pullRequestStates: new LinkedPullRequestStates(contract));
        _states.Add(state);
        await state.InitializeAsync();

        return new World(store, client, contract, state);
    }

    private sealed record World(WorkspaceSettingsStore Store, TasksPaneHost.FakeGitHubClient Client, IPullRequests Contract, TasksDesktopState State)
    {
        public EntryRow Row(Guid id) => State.Rows.Single(row => row.Id == id);

        /// <summary>Everything GitHub was asked to read so far.</summary>
        public (int Pinned, int Open, int OverRest) Sent() => (Client.PinnedReadByNumber.Count, Client.OpenReads, Client.RestReads);

        /// <summary>A prompt In progress with <paramref name="number"/> recorded on it, the
        /// way <c>link_change</c> records one, read back into the list.</summary>
        public async Task<Guid> LinkedPromptAsync(int number)
        {
            State.NewRow();
            var row = State.Rows[^1];
            State.OnRawTextInput(row, "# Ship it\n`prompt` `!in-progress` `repo:backlog`\n\nDo it.\n");
            await State.EndEditAsync(row);

            await TasksTestHost.EntriesFor(Store).LinkToIssueAsync(
                row.Id!.Value,
                Repository,
                number.ToString(System.Globalization.CultureInfo.InvariantCulture),
                EntryProjectionDto.PullRequestTargetType,
                TestContext.Current.CancellationToken);
            await State.ReloadFromStoreAsync();
            return row.Id!.Value;
        }
    }
}
