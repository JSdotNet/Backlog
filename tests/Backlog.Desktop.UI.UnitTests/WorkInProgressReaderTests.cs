using Backlog.Desktop.UI.InProgress;
using Backlog.Infrastructure.FileSystem.Progress;
using Backlog.Infrastructure.GitHub;
using Backlog.Modules.Progress.Abstractions;
using Backlog.Modules.Sessions.Abstractions;
using Backlog.Tests;
using Backlog.UI.Components.Integrations;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The pull requests tasks link, as <see cref="WorkInProgressReader"/> reads them beside
/// the open read: by number, only the ones the open read did not list, with their state,
/// merge date and author — after the open read lands rather than as part of it, and a
/// merged one read once and kept, so a visit to the In progress view does not sweep
/// GitHub for work that has already landed.
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
        Assert.Equal([$"{Repository} 655"], host.Client.ReadByNumber);

        var merged = Assert.Single(reader.LinkedPullRequests);
        Assert.Equal(655, merged.Number);
        Assert.Equal(GitHubItemState.Merged, merged.State);
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
            host.Client.ReadByNumber.Order(StringComparer.Ordinal));
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

        Assert.Equal([$"{Repository} 999"], host.Client.ReadByNumber);
        Assert.Empty(reader.LinkedPullRequests);
    }

    [Fact]
    public async Task A_read_still_fresh_and_the_side_panels_sessions_read_ask_github_for_nothing_by_number()
    {
        using var host = await TasksPaneHost.CreateAsync(Repository);
        host.Client.PullRequestsByNumber.Add(Linked(657, GitHubItemState.Open, repository: Elsewhere));
        var reader = Reader(host, out var clock, Link(657, Elsewhere));

        await reader.EnsureSessionsAsync();
        Assert.Empty(host.Client.ReadByNumber);

        await reader.EnsureAllAsync();
        await reader.LinkedRead;
        clock.Advance(TimeSpan.FromSeconds(30));
        await reader.EnsureAllAsync();
        await reader.LinkedRead;
        Assert.Single(host.Client.ReadByNumber);

        clock.Advance(WorkInProgressReader.FreshFor);
        await reader.EnsureAllAsync();
        await reader.LinkedRead;
        Assert.Equal(2, host.Client.ReadByNumber.Count);
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
        var links = new List<GitHubPullRequestReference> { new(Repository, 655) };
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

        Assert.Empty(host.Client.ReadByNumber);
        Assert.Empty(reader.LinkedPullRequests);
    }

    [Fact]
    public void The_links_are_every_entrys_pull_requests_each_once_whatever_the_case_of_the_repository()
    {
        var first = new EntryRow { PullRequestLinks = [new EntryPullRequestLink(Repository, 655), new EntryPullRequestLink(Repository, 700)] };
        var second = new EntryRow { PullRequestLinks = [new EntryPullRequestLink("jsdotnet/backlog", 655)] };

        var links = WorkInProgressReader.LinksOf([first, second]).ToList();

        Assert.Equal([655, 700], links.Select(link => link.Number));
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
        var reader = Reader(host, out _, () => WorkInProgressReader.LinksOf(host.State.Rows));
        await ReadAsync(reader);

        var projection = new InProgressProjection(
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

        var view = host.Context.Render<InProgressView>(parameters => parameters.Add(p => p.Reader, reader));
        view.WaitForAssertion(() =>
        {
            var card = view.Find($"[data-pull-request='{Repository}#655']");
            Assert.Equal("Unknown", card.QuerySelector("[data-testid='work-pull-request-state']")!.TextContent.Trim());
            Assert.Equal($"Pull request in {Repository}", card.QuerySelector("[data-testid='work-pull-request-title']")!.TextContent.Trim());
            Assert.DoesNotContain("octocat", view.Markup, StringComparison.Ordinal);
        });
    }

    /// <summary>On one Refresh the sprint view reads its members' pull requests while the
    /// In progress view's open read is still out: the read by number that follows takes
    /// the sprint view's answers rather than asking GitHub again.</summary>
    [Fact]
    public async Task On_one_refresh_the_sprint_view_and_the_in_progress_view_ask_github_about_a_linked_pull_request_once()
    {
        using var host = await TasksPaneHost.CreateAsync(Repository);
        host.Client.PullRequestsByNumber.AddRange([Linked(655, GitHubItemState.Merged, mergedAt: Noon), Linked(656, GitHubItemState.Closed)]);
        var (reader, sprint, clock) = SharedReaders(host, Link(655), Link(656));

        host.Client.OpenGate = new TaskCompletionSource();
        var refresh = reader.ReadAsync();
        clock.Advance(TimeSpan.FromSeconds(1));
        var answers = await sprint.ReadAsync([new(Repository, "655"), new(Repository, "656")], TestContext.Current.CancellationToken);
        clock.Advance(TimeSpan.FromSeconds(1));
        host.Client.OpenGate.SetResult();
        await refresh;
        await reader.LinkedRead;

        Assert.Equal([$"{Repository} 655,656"], host.Client.ReadByNumber);
        Assert.Equal([LinkedPullRequestState.Merged, LinkedPullRequestState.Closed], answers.Select(pull => pull.State));
        Assert.Equal([655, 656], reader.LinkedPullRequests.Select(pull => pull.Number).Order());
    }

    /// <summary>The other way round: what the In progress view read by number serves the
    /// sprint view while it is fresh.</summary>
    [Fact]
    public async Task The_sprint_view_reads_what_the_in_progress_view_just_read_without_asking_github()
    {
        using var host = await TasksPaneHost.CreateAsync(Repository);
        host.Client.PullRequestsByNumber.Add(Linked(656, GitHubItemState.Closed, "octocat"));
        var (reader, sprint, clock) = SharedReaders(host, Link(656));

        await ReadAsync(reader);
        clock.Advance(TimeSpan.FromSeconds(30));
        var answer = Assert.Single(await sprint.ReadAsync([new(Repository, "656")], TestContext.Current.CancellationToken));

        Assert.Single(host.Client.ReadByNumber);
        Assert.Equal(LinkedPullRequestState.Closed, answer.State);
        Assert.Equal("octocat", answer.Author);
    }

    /// <summary>The In progress reader and the sprint reader over one GitHub adapter, as the
    /// desktop composes them, on one clock.</summary>
    private static (WorkInProgressReader Reader, GitHubSprintPullRequests Sprint, FakeTimeProvider Clock) SharedReaders(
        TasksPaneHost host,
        params Func<IEnumerable<GitHubPullRequestReference>>[] links)
    {
        var clock = new FakeTimeProvider(Noon);
        var gitHub = new GitHubIntegration(host.GitHub.Settings, host.Client, new StubProbe(new GitHubConnection(true, "Connected.")), time: clock);

        return (
            new WorkInProgressReader(gitHub, new NoSessions(), clock, () => links.SelectMany(link => link())),
            new GitHubSprintPullRequests(() => gitHub),
            clock);
    }

    /// <summary>A full read, and the read by number it set off.</summary>
    private static async Task ReadAsync(WorkInProgressReader reader)
    {
        await reader.ReadAsync();
        await reader.LinkedRead;
    }

    private static Func<IEnumerable<GitHubPullRequestReference>> Link(int number, string repository = Repository) =>
        () => [new GitHubPullRequestReference(repository, number)];

    private static WorkInProgressReader Reader(TasksPaneHost host, out FakeTimeProvider clock, params Func<IEnumerable<GitHubPullRequestReference>>[] links)
    {
        clock = new FakeTimeProvider(Noon);
        host.Context.Services.AddSingleton<TimeProvider>(clock);

        return new WorkInProgressReader(host.GitHub, new NoSessions(), clock, () => links.SelectMany(link => link()));
    }

    private static GitHubLinkedPullRequest Linked(int number, GitHubItemState state, string author = "someone-else", DateTimeOffset? mergedAt = null, string repository = Repository) =>
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
}
