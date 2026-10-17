using Backlog.Desktop.UI.InProgress;
using Backlog.Infrastructure.GitHub;
using Backlog.Modules.Sessions.Abstractions;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// A task's "Sessions and pull requests" section, over the real task list and its use
/// cases: which session and pull request it draws, what it offers to link, and that
/// linking one goes through the use case the Backlog server's <c>link_session</c> and
/// <c>link_change</c> write through, so the entry carries the link afterwards.
/// <para>
/// bUnit swallows an exception thrown from a click handler, so every link is asserted
/// by its effect — the link on the entry, the card drawn, the toast — never by
/// the absence of a throw.
/// </para>
/// </summary>
[Collection(WorkspaceSettingsCollection.Name)]
public sealed class LinkedWorkSectionTests
{
    private static readonly DateTimeOffset Noon = new(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);

    private const string Entry = "# Link the work\n`prompt` `!in-progress` `repo:JSdotNet/Backlog`\n";

    private const string Repository = "JSdotNet/Backlog";

    /// <summary>The section says "None yet" for each kind an entry has none of, offers no
    /// "Open in In progress", and its own link action offers the loose work and links it.</summary>
    [Fact]
    public async Task The_side_panel_section_links_a_session_or_pull_request_to_its_entry()
    {
        using var host = await TasksPaneHost.CreateAsync(Repository);
        var row = await host.WriteEntryAsync(Entry);
        host.Client.OpenPullRequests.Add(Pull(700, mine: true));
        var reader = Reader(host, Session("loose", AgentSessionState.Running, Noon.AddMinutes(-6), resolved: Repository));

        var section = host.Context.Render<LinkedWorkSection>(parameters => parameters
            .Add(p => p.Row, row)
            .Add(p => p.Reader, reader));

        section.WaitForAssertion(() =>
        {
            Assert.NotEmpty(section.FindAll("[data-testid='work-links-no-sessions']"));
            Assert.NotEmpty(section.FindAll("[data-testid='work-links-no-pull-requests']"));
        });

        // The In progress view it once led to is gone with the goals flag.
        Assert.Empty(section.FindAll("[data-testid='work-links-open-in-progress']"));

        section.Find("[data-testid='work-links-link']").Click();
        section.WaitForAssertion(() =>
        {
            var options = section.FindAll("[data-testid='work-link-picker-option']").Select(option => option.GetAttribute("data-option-id")).ToList();
            Assert.Equal(["loose", "JSdotNet/Backlog#700"], options);
        });

        section.Find("[data-testid='work-link-picker-option'][data-option-id='JSdotNet/Backlog#700']").Click();

        section.WaitForAssertion(() =>
        {
            Assert.Equal([new EntryPullRequestLink(Repository, 700)], row.PullRequestLinks);
            Assert.Equal("JSdotNet/Backlog#700", section.Find("[data-testid='work-pull-request']").GetAttribute("data-pull-request"));
        });
    }

    /// <summary>A session already linked is not linked twice, the way
    /// <c>link_session</c> answers AlreadyLinked.</summary>
    [Fact]
    public async Task Linking_what_the_entry_already_holds_writes_nothing()
    {
        using var host = await TasksPaneHost.CreateAsync(Repository);
        var row = await host.WriteEntryAsync(Entry);

        Assert.Null(await host.State.LinkWorkAsync(row, Repository, "abc", Backlog.Modules.Tasks.Abstractions.DataTransferObjects.EntryProjectionDto.SessionTargetType));
        Assert.Null(await host.State.LinkWorkAsync(row, Repository, "ABC", Backlog.Modules.Tasks.Abstractions.DataTransferObjects.EntryProjectionDto.SessionTargetType));

        Assert.Equal(["abc"], row.SessionLinks.Select(link => link.SessionId));
    }

    /// <summary>The side panel's section offers the same unlink on a pull request,
    /// and the pull request — still open and the reader's — is offered to link again.</summary>
    [Fact]
    public async Task Unlink_in_the_side_panel_takes_a_pull_request_off_its_entry()
    {
        using var host = await TasksPaneHost.CreateAsync(Repository);
        var row = await host.WriteEntryAsync(Entry);
        host.Client.OpenPullRequests.Add(Pull(700, mine: true));
        Assert.Null(await host.State.LinkWorkAsync(row, Repository, "700", PullRequestTarget));
        var reader = Reader(host);
        await reader.ReadAsync();

        var section = host.Context.Render<LinkedWorkSection>(parameters => parameters
            .Add(p => p.Row, row)
            .Add(p => p.Reader, reader));

        section.WaitForAssertion(() => Assert.NotEmpty(section.FindAll("[data-testid='work-links-unlink'][data-unlink='JSdotNet/Backlog#700']")));
        section.Find("[data-testid='work-links-unlink'][data-unlink='JSdotNet/Backlog#700']").Click();

        section.WaitForAssertion(() =>
        {
            Assert.Empty(row.PullRequestLinks);
            Assert.NotEmpty(section.FindAll("[data-testid='work-links-no-pull-requests']"));

            // The section's heading takes the focus the removed Unlink had.
            var heading = section.Find("h3.work-links-panel__heading");
            Assert.Equal(heading.Id, host.Context.JSInterop.Invocations["backlogFocus"][^1].Arguments[0]);
            Assert.Equal("-1", heading.GetAttribute("tabindex"));
        });

        section.Find("[data-testid='work-links-link']").Click();
        section.WaitForAssertion(() =>
            Assert.Contains("JSdotNet/Backlog#700", section.FindAll("[data-testid='work-link-picker-option']").Select(option => option.GetAttribute("data-option-id"))));
    }

    /// <summary>A linked pull request the open read listed carries its status line in the
    /// side panel — checks as passed/total, approvals against what its base branch requires
    /// — the Pull requests pane's marks (.devbook/domain/progress/features.md#pull-request-status).</summary>
    [Fact]
    public async Task A_linked_open_pull_request_draws_its_status_line_in_the_side_panel()
    {
        using var host = await TasksPaneHost.CreateAsync(Repository);
        var row = await host.WriteEntryAsync(Entry);
        host.Client.OpenPullRequests.Add(Pull(700, mine: true, GitHubCheckState.Pending) with
        {
            CheckCounts = new GitHubCheckCounts(Passed: 9, Failed: 0, Pending: 5),
            Reviews = new GitHubReviewSummary(GitHubReviewState.ReviewRequired, Approvals: 1, ChangesRequested: 0)
        });
        host.Client.RequiredApprovals["main"] = 2;
        Assert.Null(await host.State.LinkWorkAsync(row, Repository, "700", PullRequestTarget));
        var reader = Reader(host);
        await reader.ReadAsync();

        var section = host.Context.Render<LinkedWorkSection>(parameters => parameters
            .Add(p => p.Row, row)
            .Add(p => p.Reader, reader));

        section.WaitForAssertion(() =>
        {
            var card = section.Find("[data-testid='work-links-pull-requests'] [data-pull-request='JSdotNet/Backlog#700']");
            Assert.Equal("9/14", card.QuerySelector("[data-testid='work-pull-request-status-checks'] .pull-request-status__count")!.TextContent);
            var approvals = card.QuerySelector("[data-testid='work-pull-request-status-approvals']")!;
            Assert.Equal("short", approvals.GetAttribute("data-variant"));
            Assert.Equal("1/2", approvals.QuerySelector(".pull-request-status__count")!.TextContent);
            Assert.Null(card.QuerySelector("[data-testid='work-pull-request-checks']"));
        });
    }

    /// <summary>With only one of Sessions and Pull requests on, the other column is not
    /// drawn in the section, and its link action names only the kind left.</summary>
    [Fact]
    public async Task A_feature_that_is_off_draws_no_column_in_the_side_panel()
    {
        using var host = await TasksPaneHost.CreateAsync(Repository);
        var row = await host.WriteEntryAsync(Entry);
        var reader = Reader(host);

        var section = host.Context.Render<LinkedWorkSection>(parameters => parameters
            .Add(p => p.Row, row)
            .Add(p => p.Reader, reader)
            .Add(p => p.ShowSessions, false));

        section.WaitForAssertion(() =>
        {
            Assert.Empty(section.FindAll("[data-testid='work-links-sessions']"));
            Assert.NotEmpty(section.FindAll("[data-testid='work-links-no-pull-requests']"));
            Assert.Equal("Link a pull request…", section.Find("[data-testid='work-links-link']").TextContent.Trim());
        });
    }

    private const string SessionTarget = Backlog.Modules.Tasks.Abstractions.DataTransferObjects.EntryProjectionDto.SessionTargetType;

    private const string PullRequestTarget = Backlog.Modules.Tasks.Abstractions.DataTransferObjects.EntryProjectionDto.PullRequestTargetType;

    /// <summary>The reader over the host's GitHub and the given sessions, with the
    /// clock the cards measure "45 min ago" against registered beside it.</summary>
    private static WorkInProgressReader Reader(TasksPaneHost host, params AgentSession[] sessions)
    {
        var clock = new FakeTimeProvider(Noon);
        host.Context.Services.AddSingleton<TimeProvider>(clock);

        return new(host.GitHub, new StubSessionSource(sessions), clock);
    }

    private static AgentSession Session(string id, AgentSessionState state, DateTimeOffset lastActivity, string? resolved = null) =>
        new(
            Id: id,
            Kind: AgentSessionKind.Claude,
            EnvironmentId: "tower",
            Environment: "DEV-TOWER",
            Title: $"Session {id}",
            WorkingFolder: @"D:\Repos\Backlog",
            Repository: null,
            Branch: "main",
            StartedAt: lastActivity.AddHours(-1),
            LastActivityAt: lastActivity,
            State: state,
            TurnCount: 3,
            Origin: AgentSessionOrigin.Local)
        {
            ResolvedRepository = resolved
        };

    private static GitHubOpenPullRequest Pull(int number, bool mine, GitHubCheckState checks = GitHubCheckState.Passing) =>
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
            AuthorLogin: mine ? "JSdotNet" : "someone-else",
            ViewerDidAuthor: mine,
            Checks: checks,
            AutoMergeEnabled: false,
            MergeReady: false,
            IsBehind: false,
            HasConflicts: false,
            MergeStateStatus: "BLOCKED",
            PreferredMergeMethod: GitHubMergeMethod.Squash,
            UpdatedAt: Noon.AddMinutes(-number))
        {
            Additions = 10,
            Deletions = 2
        };

    private sealed class StubSessionSource(IReadOnlyList<AgentSession> sessions) : IAgentSessionSource
    {
        public Task<AgentSessionCatalog> GetSessionsAsync(AgentSessionQuery query, CancellationToken cancellationToken = default) =>
            GetSessionsAsync(cancellationToken);

        public Task<AgentSessionCatalog> GetSessionsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new AgentSessionCatalog(sessions, [], sessions.Count));
    }
}
