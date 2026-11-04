using Backlog.Desktop.UI.Goals;
using Backlog.Infrastructure.GitHub;
using Backlog.Modules.RepositoryManagement.Abstractions;
using Backlog.Modules.RepositoryManagement.Abstractions.DataTransferObjects;
using Backlog.Modules.Sessions.Abstractions;
using Backlog.Modules.Tasks.Abstractions.Services;
using Backlog.UI.Components.Integrations;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The pull requests tasks link, as <see cref="WorkInProgressReader"/> reads them beside
/// the open read: by number, only the ones the open read did not list, with their state,
/// merge date and author — after the open read lands rather than as part of it, and a
/// merged one read once and kept, so a visit to the Goals view does not sweep
/// GitHub for work that has already landed.
/// <para>
/// Through Repository Management's contract composed over the test's GitHub client
/// (<see cref="RepositoryManagementChain"/>, local ADR 0042): what is kept, and for how
/// long, is the contract's one cache, so these count the pinned reads GitHub was sent.
/// </para>
/// </summary>
[Collection(WorkspaceSettingsCollection.Name)]
public sealed class WorkInProgressReaderTests
{
    private static readonly DateTimeOffset Noon = new(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);

    private const string Repository = "JSdotNet/Backlog";

    private const string Elsewhere = "octo/elsewhere";

    private const string Entry = "# Wire the In progress view\n`prompt` `!in-progress` `repo:JSdotNet/Backlog`\n";

    [Fact]
    public async Task A_linked_pull_request_the_open_read_did_not_list_is_read_by_number_with_its_state_merge_date_and_author()
    {
        using var host = await TasksPaneHost.CreateAsync(Repository);
        host.Client.OpenPullRequests.Add(Open(700));
        host.Client.PullRequestsByNumber.Add(Linked(655, GitHubItemState.Merged, "octocat", Noon.AddHours(-2)));
        var reader = Reader(host, out _, Link(655), Link(700));

        await ReadAsync(reader);

        // The open read already answered for 700; only 655 is asked for by number.
        Assert.Equal([$"{Repository} 655"], host.Client.PinnedReadByNumber);

        var merged = Assert.Single(reader.LinkedPullRequests);
        Assert.Equal(655, merged.Number);
        Assert.Equal(PullRequestState.Merged, merged.State);
        Assert.Equal(Noon.AddHours(-2), merged.MergedAt);
        Assert.Equal("octocat", merged.AuthorLogin);
    }

    /// <summary>Merged is for good and read once. Closed is not — GitHub can reopen
    /// one — and neither is open outside the open read, so both are read again with
    /// the next read of the open list.</summary>
    [Fact]
    public async Task A_merged_linked_pull_request_is_read_once_and_a_closed_or_open_one_again_on_the_next_read()
    {
        using var host = await TasksPaneHost.CreateAsync(Repository);
        host.Client.PullRequestsByNumber.AddRange(
        [
            Linked(655, GitHubItemState.Merged, mergedAt: Noon.AddHours(-2)),
            Linked(656, GitHubItemState.Closed),
            // Open, yet not on the open read: a repository the settings no longer list.
            Linked(657, GitHubItemState.Open, repository: Elsewhere)
        ]);
        var reader = Reader(host, out _, Link(655), Link(656), Link(657, Elsewhere));

        await ReadAsync(reader);
        await ReadAsync(reader);

        Assert.Equal(
            [$"{Repository} 655,656", $"{Repository} 656", $"{Elsewhere} 657", $"{Elsewhere} 657"],
            host.Client.PinnedReadByNumber.Order(StringComparer.Ordinal));
        Assert.Equal([655, 656, 657], reader.LinkedPullRequests.Select(pull => pull.Number).Order());
    }

    /// <summary>A link GitHub answers without — a number that is no pull request, or one
    /// deleted — is not asked about on every visit after.</summary>
    [Fact]
    public async Task A_linked_number_github_cannot_find_is_not_asked_about_again()
    {
        using var host = await TasksPaneHost.CreateAsync(Repository);
        var reader = Reader(host, out _, Link(999));

        await ReadAsync(reader);
        await ReadAsync(reader);

        Assert.Equal([$"{Repository} 999"], host.Client.PinnedReadByNumber);
        Assert.Empty(reader.LinkedPullRequests);
    }

    /// <summary>A link into an archived repository is not read (local ADR 0029), and is
    /// not taken for a number GitHub could not find either: once the repository is
    /// unarchived, the link is read again.</summary>
    [Fact]
    public async Task A_link_into_an_archived_repository_is_read_again_once_it_is_unarchived()
    {
        using var host = await TasksPaneHost.CreateAsync(Repository, Elsewhere);
        host.Client.PullRequestsByNumber.Add(Linked(5, GitHubItemState.Closed, repository: Elsewhere));
        var reader = Reader(host, out _, Link(5, Elsewhere));
        Assert.Null(host.GitHub.Settings.SetRepositoryArchived(Elsewhere, archived: true));

        await ReadAsync(reader);
        Assert.Empty(host.Client.PinnedReadByNumber);
        Assert.Empty(reader.LinkedPullRequests);

        Assert.Null(host.GitHub.Settings.SetRepositoryArchived(Elsewhere, archived: false));
        await ReadAsync(reader);

        Assert.Equal([$"{Elsewhere} 5"], host.Client.PinnedReadByNumber);
        Assert.Equal(5, Assert.Single(reader.LinkedPullRequests).Number);
    }

    [Fact]
    public async Task A_read_still_fresh_and_the_side_panels_sessions_read_ask_github_for_nothing_by_number()
    {
        using var host = await TasksPaneHost.CreateAsync(Repository);
        host.Client.PullRequestsByNumber.Add(Linked(657, GitHubItemState.Open, repository: Elsewhere));
        var reader = Reader(host, out var clock, Link(657, Elsewhere));

        await reader.EnsureSessionsAsync();
        Assert.Empty(host.Client.PinnedReadByNumber);

        await reader.EnsureAllAsync();
        await reader.LinkedRead;
        clock.Advance(TimeSpan.FromSeconds(30));
        await reader.EnsureAllAsync();
        await reader.LinkedRead;
        Assert.Single(host.Client.PinnedReadByNumber);

        clock.Advance(WorkInProgressReader.FreshFor);
        await reader.EnsureAllAsync();
        await reader.LinkedRead;
        Assert.Equal(2, host.Client.PinnedReadByNumber.Count);
    }

    /// <summary>Refresh — and the first opening, and the link picker's own read — asks
    /// GitHub for the open list again, whatever another reader listed inside the minute;
    /// an <c>Ensure</c> takes the list the contract kept, as any other reader would.</summary>
    [Fact]
    public async Task A_refresh_inside_the_minute_lists_again_and_an_ensure_takes_the_kept_list()
    {
        using var host = await TasksPaneHost.CreateAsync(Repository);
        host.Client.OpenPullRequests.Add(Open(700));
        var clock = new FakeTimeProvider(Noon);
        var contract = RepositoryManagementChain.For(host.GitHub, clock);
        var reader = new WorkInProgressReader(contract, new NoSessions(), clock);

        // The pull requests pane lists at noon; a pull request opens on GitHub after.
        await contract.ListOpenAsync(cancellationToken: TestContext.Current.CancellationToken);
        host.Client.OpenPullRequests.Add(Open(701));
        clock.Advance(TimeSpan.FromSeconds(30));

        await reader.EnsureAllAsync();
        Assert.Equal([700], reader.PullRequests.Select(pull => pull.Number));

        await reader.ReadAsync();
        Assert.Equal([700, 701], reader.PullRequests.Select(pull => pull.Number).Order());
    }

    /// <summary>The open list is what the view draws, so it lands — and the read a
    /// caller awaits ends — without waiting on the read by number.</summary>
    [Fact]
    public async Task The_open_read_lands_without_waiting_for_the_read_by_number()
    {
        using var host = await TasksPaneHost.CreateAsync(Repository);
        host.Client.OpenPullRequests.Add(Open(700));
        host.Client.PullRequestsByNumber.Add(Linked(655, GitHubItemState.Merged, mergedAt: Noon));
        host.Client.ByNumberGate = new TaskCompletionSource();
        var reader = Reader(host, out _, Link(655));

        await reader.ReadAsync();

        Assert.True(reader.PullRequestsRead);
        Assert.False(reader.Reading);
        Assert.Equal(700, Assert.Single(reader.PullRequests).Number);
        Assert.False(reader.LinkedRead.IsCompleted);
        Assert.Empty(reader.LinkedPullRequests);

        host.Client.ByNumberGate.SetResult();
        await reader.LinkedRead;

        Assert.Equal(655, Assert.Single(reader.LinkedPullRequests).Number);
    }

    /// <summary>A read by number that lands after a newer read began is not kept over
    /// it — apart from what it found merged, which nothing later can change.</summary>
    [Fact]
    public async Task A_superseded_read_by_number_keeps_only_what_it_found_merged()
    {
        using var host = await TasksPaneHost.CreateAsync(Repository);
        host.Client.PullRequestsByNumber.AddRange(
        [
            Linked(655, GitHubItemState.Merged, mergedAt: Noon),
            Linked(656, GitHubItemState.Closed)
        ]);
        var gate = new TaskCompletionSource();
        host.Client.ByNumberGate = gate;
        var reader = Reader(host, out _, Link(655), Link(656));

        // The first read by number is held back with both in its answer.
        await reader.ReadAsync();
        var first = reader.LinkedRead;

        // The second finds 656 gone and lands first.
        host.Client.ByNumberGate = null;
        host.Client.PullRequestsByNumber.RemoveAll(pull => pull.Number == 656);
        await ReadAsync(reader);
        Assert.Equal([655], reader.LinkedPullRequests.Select(pull => pull.Number));

        gate.SetResult();
        await first;

        Assert.Equal([655], reader.LinkedPullRequests.Select(pull => pull.Number));
    }

    [Fact]
    public async Task A_link_taken_off_its_entry_is_no_longer_among_the_linked_pull_requests()
    {
        using var host = await TasksPaneHost.CreateAsync(Repository);
        host.Client.PullRequestsByNumber.Add(Linked(655, GitHubItemState.Merged, mergedAt: Noon));
        var links = new List<PullRequestReference> { new(Repository, 655) };
        var reader = Reader(host, out _, () => [.. links]);

        await ReadAsync(reader);
        Assert.Single(reader.LinkedPullRequests);

        links.Clear();
        await ReadAsync(reader);
        Assert.Empty(reader.LinkedPullRequests);
    }

    [Fact]
    public async Task With_the_pull_requests_list_off_nothing_is_read_by_number()
    {
        using var host = await TasksPaneHost.CreateAsync(Repository);
        host.Client.PullRequestsByNumber.Add(Linked(655, GitHubItemState.Merged));
        var reader = Reader(host, out _, Link(655));
        reader.ReadsPullRequests = false;

        await ReadAsync(reader);

        Assert.Empty(host.Client.PinnedReadByNumber);
        Assert.Empty(reader.LinkedPullRequests);
    }

    /// <summary>The reader the shell builds over what the host composed asks Tasks which
    /// pull requests the tasks link (local ADR 0042, V8), at the start of every read —
    /// and with no lookup composed, reads none by number.</summary>
    [Fact]
    public async Task The_reader_the_shell_builds_reads_the_pull_requests_tasks_link_through_the_tasks_lookup()
    {
        using var host = await TasksPaneHost.CreateAsync(Repository);
        host.Client.PullRequestsByNumber.Add(Linked(655, GitHubItemState.Merged, "octocat", Noon.AddHours(-2)));
        var clock = new FakeTimeProvider(Noon);
        var lookup = new LinkingLookup([new TaskPullRequestLink(Repository, 655)]);

        var services = new ServiceCollection()
            .AddSingleton(RepositoryManagementChain.For(host.GitHub, clock))
            .AddSingleton<IAgentSessionSource>(new NoSessions())
            .AddSingleton<TimeProvider>(clock);
        using (var withLookup = services.AddSingleton<ITaskLookup>(lookup).BuildServiceProvider())
        {
            var reader = WorkInProgressReader.From(withLookup);
            await ReadAsync(reader);

            Assert.Equal([$"{Repository} 655"], host.Client.PinnedReadByNumber);
            Assert.Equal(655, Assert.Single(reader.LinkedPullRequests).Number);
        }

        services.RemoveAll<ITaskLookup>();
        using var withoutLookup = services.BuildServiceProvider();
        var unlinked = WorkInProgressReader.From(withoutLookup);
        await ReadAsync(unlinked);

        Assert.Empty(unlinked.LinkedPullRequests);
    }

    /// <summary>The work-link record carries the read — state, author, merge date — and
    /// what the card draws is what it drew before the read existed: the title and state
    /// the task list recorded.</summary>
    [Fact]
    public async Task The_projection_carries_the_read_state_and_author_and_the_card_draws_what_it_drew()
    {
        using var host = await TasksPaneHost.CreateAsync(Repository);
        var row = await host.WriteEntryAsync(Entry);
        row.PullRequestLinks = [new EntryPullRequestLink(Repository, 655), new EntryPullRequestLink(Repository, 700)];
        host.Client.OpenPullRequests.Add(Open(700, author: "JSdotNet"));
        host.Client.PullRequestsByNumber.Add(Linked(655, GitHubItemState.Merged, "octocat", Noon.AddHours(-2)));
        var reader = Reader(host, out _, Link(655), Link(700));
        await ReadAsync(reader);

        var projection = new WorkLinksProjection(
            host.State.Rows, reader.Sessions, reader.PullRequests, _ => null, _ => null, _ => null, Noon, reader.SessionsRead, reader.LinkedPullRequests);
        var pulls = projection.PullRequestsOf(row);

        var merged = Assert.Single(pulls, pull => pull.Number == 655);
        Assert.Equal(IntegrationArtifactState.Merged, merged.ReadState);
        Assert.Equal("octocat", merged.Author);
        Assert.Equal(Noon.AddHours(-2), merged.MergedAt);
        // Drawn as before: the task list has not read it, so neither title nor state moves.
        Assert.Equal(IntegrationArtifactState.Unknown, merged.State);
        Assert.Equal($"Pull request in {Repository}", merged.Title);

        var open = Assert.Single(pulls, pull => pull.Number == 700);
        Assert.Equal(IntegrationArtifactState.Open, open.ReadState);
        Assert.Equal("JSdotNet", open.Author);
        Assert.Null(open.MergedAt);

        var view = host.Context.Render<LinkedWorkSection>(parameters => parameters
            .Add(p => p.Row, row)
            .Add(p => p.Reader, reader));
        view.WaitForAssertion(() =>
        {
            var card = view.Find($"[data-pull-request='{Repository}#655']");
            Assert.Equal("Unknown", card.QuerySelector("[data-testid='work-pull-request-state']")!.TextContent.Trim());
            Assert.Equal($"Pull request in {Repository}", card.QuerySelector("[data-testid='work-pull-request-title']")!.TextContent.Trim());
            Assert.DoesNotContain("octocat", view.Markup, StringComparison.Ordinal);
        });
    }

    /// <summary>A full read, and the read by number it set off.</summary>
    private static async Task ReadAsync(WorkInProgressReader reader)
    {
        await reader.ReadAsync();
        await reader.LinkedRead;
    }

    private static Func<IEnumerable<PullRequestReference>> Link(int number, string repository = Repository) =>
        () => [new PullRequestReference(repository, number)];

    private static WorkInProgressReader Reader(TasksPaneHost host, out FakeTimeProvider clock, params Func<IEnumerable<PullRequestReference>>[] links)
    {
        clock = new FakeTimeProvider(Noon);
        host.Context.Services.AddSingleton<TimeProvider>(clock);

        return new WorkInProgressReader(RepositoryManagementChain.For(host.GitHub, clock), new NoSessions(), clock, () => links.SelectMany(link => link()));
    }

    private static PullRequestByNumber Linked(int number, GitHubItemState state, string author = "someone-else", DateTimeOffset? mergedAt = null, string repository = Repository) =>
        new(number, $"https://github.com/{repository}/pull/{number}", $"Pull request {number}", repository, state, author, mergedAt);

    private static GitHubOpenPullRequest Open(int number, string author = "someone-else") =>
        new(
            number,
            $"https://github.com/{Repository}/pull/{number}",
            $"Pull request {number}",
            Repository,
            $"PR_{number}",
            IsDraft: false,
            HeadRefName: $"feature-{number}",
            HeadSha: "abc",
            BaseRefName: "main",
            AuthorLogin: author,
            ViewerDidAuthor: false,
            Checks: GitHubCheckState.Passing,
            AutoMergeEnabled: false,
            MergeReady: false,
            IsBehind: false,
            HasConflicts: false,
            MergeStateStatus: "BLOCKED",
            PreferredMergeMethod: GitHubMergeMethod.Squash,
            UpdatedAt: Noon.AddMinutes(-number));

    private sealed class NoSessions : IAgentSessionSource
    {
        public Task<AgentSessionCatalog> GetSessionsAsync(AgentSessionQuery query, CancellationToken cancellationToken = default) =>
            GetSessionsAsync(cancellationToken);

        public Task<AgentSessionCatalog> GetSessionsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new AgentSessionCatalog([], [], 0));
    }

    /// <summary>A Tasks lookup whose tasks link the given pull requests and own no
    /// session or pull request.</summary>
    private sealed class LinkingLookup(IReadOnlyList<TaskPullRequestLink> linked) : ITaskLookup
    {
        public TaskReference? TaskForSession(string sessionId) => null;

        public TaskReference? TaskForPullRequest(PullRequestTaskQuestion pullRequest, bool mineOnly = false) => null;

        public IReadOnlyList<TaskPullRequestLink> LinkedPullRequests() => linked;
    }
}
