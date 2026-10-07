using Backlog.Desktop.UI.InProgress;
using Backlog.Infrastructure.GitHub;
using Backlog.Modules.Sessions.Abstractions;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The In progress view and a task's "Sessions and pull requests" section, over the
/// real task list and its use cases: which session and pull request a card draws,
/// which are left over as not linked to a task, and that linking one goes through
/// the use case the Backlog server's <c>link_session</c> and <c>link_change</c>
/// write through, so the entry carries the link afterwards.
/// <para>
/// bUnit swallows an exception thrown from a click handler, so every link is asserted
/// by its effect — the link on the entry, the card that moved, the toast — never by
/// the absence of a throw.
/// </para>
/// </summary>
[Collection(WorkspaceSettingsCollection.Name)]
public sealed class InProgressViewTests
{
    private static readonly DateTimeOffset Noon = new(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);

    private const string Entry = "# Wire the In progress view\n`prompt` `!in-progress` `repo:JSdotNet/Backlog`\n";

    private const string Repository = "JSdotNet/Backlog";

    [Fact]
    public async Task A_card_draws_what_its_entry_links_and_the_rest_is_not_linked_to_a_task()
    {
        using var host = await TasksPaneHost.CreateAsync(Repository);
        var row = await host.WriteEntryAsync(Entry);
        row.SessionLinks = [new EntrySessionLink(Repository, "linked")];
        row.PullRequestLinks = [new EntryPullRequestLink(Repository, 655)];

        host.Client.OpenPullRequests.AddRange(
        [
            Pull(655, mine: true, checks: GitHubCheckState.Failing),
            Pull(700, mine: true),
            Pull(701, mine: false)
        ]);
        var reader = Reader(host,
            Session("linked", AgentSessionState.Running, Noon.AddMinutes(-2)),
            Session("loose", AgentSessionState.Running, Noon.AddMinutes(-6)),
            Session("over", AgentSessionState.Finished, Noon.AddHours(-3)));

        var view = Render(host, reader);

        view.WaitForAssertion(() =>
        {
            // A failing check on the linked pull request is what needs the person.
            var needs = view.Find("[data-section='needs-you']");
            var card = needs.QuerySelector("[data-testid='in-progress-task']")!;
            Assert.Equal("Wire the In progress view", card.QuerySelector("[data-testid='in-progress-task-title']")!.TextContent.Trim());
            Assert.Equal("linked", card.QuerySelector("[data-testid='work-session']")!.GetAttribute("data-session-id"));
            Assert.Equal("JSdotNet/Backlog#655", card.QuerySelector("[data-testid='work-pull-request']")!.GetAttribute("data-pull-request"));
            Assert.Contains("Checks failing", card.TextContent, StringComparison.Ordinal);

            // Live and unlinked, and the reader's own: a finished session and somebody
            // else's pull request are not the reader's to link.
            var loose = view.FindAll("[data-testid='in-progress-loose']").Select(item => item.GetAttribute("data-loose-key")).ToList();
            Assert.Equal(["loose", "JSdotNet/Backlog#700"], loose);
        });
    }

    [Fact]
    public async Task A_linked_session_that_has_gone_quiet_needs_you_and_says_quiet_30_min()
    {
        using var host = await TasksPaneHost.CreateAsync(Repository);
        var row = await host.WriteEntryAsync(Entry);
        row.SessionLinks = [new EntrySessionLink(Repository, "quiet")];
        var reader = Reader(host, Session("quiet", AgentSessionState.Stalled, Noon.AddMinutes(-45)));

        var view = Render(host, reader);

        view.WaitForAssertion(() =>
        {
            var session = view.Find("[data-section='needs-you'] [data-testid='work-session']");
            Assert.Equal("Quiet 30 min", session.QuerySelector("[data-testid='work-session-state']")!.TextContent.Trim());
            Assert.Contains("Last activity 45 min ago", session.TextContent, StringComparison.Ordinal);

            // Nothing linked on the pull request side says so.
            Assert.NotEmpty(view.FindAll("[data-section='needs-you'] [data-testid='work-links-no-pull-requests']"));
            Assert.Empty(view.FindAll("[data-section='moving'] [data-testid='in-progress-task']"));
        });
    }

    /// <summary>Linking a loose session is the <c>link_session</c> write: the entry
    /// records the session under the repository it was placed in, and the session
    /// leaves the loose section for the card.</summary>
    [Fact]
    public async Task Link_to_a_task_records_a_loose_session_on_the_chosen_entry()
    {
        using var host = await TasksPaneHost.CreateAsync(Repository);
        var row = await host.WriteEntryAsync(Entry);
        var reader = Reader(host, Session("loose", AgentSessionState.Running, Noon.AddMinutes(-6), resolved: Repository));

        var view = Render(host, reader);
        view.WaitForAssertion(() => Assert.NotEmpty(view.FindAll("[data-loose-key='loose'] [data-testid='in-progress-link']")));

        view.Find("[data-loose-key='loose'] [data-testid='in-progress-link']").Click();
        view.WaitForAssertion(() => Assert.NotEmpty(view.FindAll("[data-testid='work-link-picker-option']")));
        view.Find($"[data-testid='work-link-picker-option'][data-option-id='{row.Id}']").Click();

        view.WaitForAssertion(() =>
        {
            var link = Assert.Single(row.SessionLinks);
            Assert.Equal("loose", link.SessionId);
            Assert.Equal(Repository, link.Repository);

            Assert.Empty(view.FindAll("[data-testid='in-progress-loose']"));
            Assert.Equal("loose", view.Find("[data-testid='in-progress-task'] [data-testid='work-session']").GetAttribute("data-session-id"));
            Assert.Contains(host.Toasts.Visible, toast => toast.TestId == WorkLinker.LinkedTestId);
        });
    }

    /// <summary>Linking a loose pull request is the <c>link_change</c> write: the
    /// entry records the number under the repository it was opened in.</summary>
    [Fact]
    public async Task Link_to_a_task_records_a_loose_pull_request_on_the_chosen_entry()
    {
        using var host = await TasksPaneHost.CreateAsync(Repository);
        var row = await host.WriteEntryAsync(Entry);
        host.Client.OpenPullRequests.Add(Pull(700, mine: true));
        var reader = Reader(host);

        var view = Render(host, reader);
        view.WaitForAssertion(() => Assert.NotEmpty(view.FindAll("[data-loose-key='JSdotNet/Backlog#700'] [data-testid='in-progress-link']")));

        view.Find("[data-loose-key='JSdotNet/Backlog#700'] [data-testid='in-progress-link']").Click();
        view.WaitForAssertion(() => Assert.NotEmpty(view.FindAll("[data-testid='work-link-picker-option']")));
        view.Find($"[data-testid='work-link-picker-option'][data-option-id='{row.Id}']").Click();

        view.WaitForAssertion(() =>
        {
            Assert.Equal([new EntryPullRequestLink(Repository, 700)], row.PullRequestLinks);
            Assert.Empty(view.FindAll("[data-testid='in-progress-loose']"));
        });
    }

    /// <summary>The side panel's section says "None yet" for each kind an entry has
    /// none of, and its own link action offers the loose work and links it.</summary>
    [Fact]
    public async Task The_side_panel_section_links_a_session_or_pull_request_to_its_entry()
    {
        using var host = await TasksPaneHost.CreateAsync(Repository);
        var row = await host.WriteEntryAsync(Entry);
        host.Client.OpenPullRequests.Add(Pull(700, mine: true));
        var reader = Reader(host, Session("loose", AgentSessionState.Running, Noon.AddMinutes(-6), resolved: Repository));

        var opened = 0;
        var section = host.Context.Render<LinkedWorkSection>(parameters => parameters
            .Add(p => p.Row, row)
            .Add(p => p.Reader, reader)
            .Add(p => p.OnOpenInProgress, () => opened++));

        section.WaitForAssertion(() =>
        {
            Assert.NotEmpty(section.FindAll("[data-testid='work-links-no-sessions']"));
            Assert.NotEmpty(section.FindAll("[data-testid='work-links-no-pull-requests']"));
        });

        section.Find("[data-testid='work-links-open-in-progress']").Click();
        Assert.Equal(1, opened);

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

    private static IRenderedComponent<InProgressView> Render(TasksPaneHost host, WorkInProgressReader reader) =>
        host.Context.Render<InProgressView>(parameters => parameters.Add(p => p.Reader, reader));

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
