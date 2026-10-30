using Backlog.Modules.Tasks.Abstractions.Services;
using Backlog.Modules.Tasks.Abstractions.DataTransferObjects;
using Backlog.Infrastructure.GitHub;
using Backlog.UI.Components.Integrations;
using Bunit;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// What an entry shows of the work done on it: the pull requests a session linked
/// with <c>link_change</c>, as GitHub links, and the sessions that recorded
/// themselves with <c>link_session</c>, as buttons that open the session.
/// <para>
/// The projections are read by <see cref="EntryLinks"/>, which is asserted here on
/// its own. The pane is asserted with the row's links set directly: the mapping
/// from entry to row is one line in <c>RefreshRowFromEntry</c>, and what the pane
/// owns is how a link is drawn and what pressing it does.
/// </para>
/// </summary>
[Collection(WorkspaceSettingsCollection.Name)]
public sealed class EntryWorkLinksTests
{
    private const string Entry = "# Record the adoption decision\n`prompt` `!in-progress`\n\nWrite the ADR.\n";

    [Fact]
    public void Pull_requests_are_read_from_their_own_projections_once_each()
    {
        var entry = Dto(
            new EntryProjectionDto("JSdotNet/Backlog", "42", EntryProjectionDto.IssueTargetType),
            new EntryProjectionDto("JSdotNet/Backlog", "655", EntryProjectionDto.PullRequestTargetType),
            new EntryProjectionDto("JSdotNet/Backlog", "655", EntryProjectionDto.PullRequestTargetType),
            new EntryProjectionDto("JSdotNet/Backlog", "not-a-number", EntryProjectionDto.PullRequestTargetType),
            new EntryProjectionDto("backlog", "7", EntryProjectionDto.PullRequestTargetType),
            new EntryProjectionDto("JSdotNet/Backlog", "abc", EntryProjectionDto.SessionTargetType));

        var pr = Assert.Single(EntryLinks.PullRequests(entry));

        Assert.Equal("https://github.com/JSdotNet/Backlog/pull/655", pr.Url);
        Assert.Equal("#655", pr.Label);
    }

    [Fact]
    public void Sessions_are_read_from_their_own_projections_once_each()
    {
        var entry = Dto(
            new EntryProjectionDto("JSdotNet/Backlog", "e711d47d-3e09-4254", EntryProjectionDto.SessionTargetType),
            new EntryProjectionDto("JSdotNet/Backlog", "E711D47D-3E09-4254", EntryProjectionDto.SessionTargetType),
            new EntryProjectionDto("JSdotNet/Backlog", "second", EntryProjectionDto.SessionTargetType),
            new EntryProjectionDto("JSdotNet/Backlog", "655", EntryProjectionDto.PullRequestTargetType));

        var sessions = EntryLinks.Sessions(entry);

        Assert.Equal(["e711d47d-3e09-4254", "second"], sessions.Select(s => s.SessionId));
        Assert.Equal("e711d47d", sessions[0].ShortId);
        Assert.Equal("second", sessions[1].ShortId);
        Assert.Equal("qa-no-su", new EntrySessionLink("JSdotNet/Backlog", "qa-no-such-session").ShortId);
    }

    /// <summary>A session is no issue: the pane's offer to file one survives it.</summary>
    [Fact]
    public void A_session_is_not_the_issue_an_entry_was_pushed_to()
    {
        var entry = Dto(new EntryProjectionDto("JSdotNet/Backlog", "123", EntryProjectionDto.SessionTargetType));

        Assert.Null(TasksIssues.FindLink(entry));
    }

    [Fact]
    public async Task The_entry_links_its_pull_request_and_opens_its_session()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var row = await host.WriteEntryAsync(Entry);
        row.PullRequestLinks = [new EntryPullRequestLink("JSdotNet/Backlog", 655)];
        row.SessionLinks = [new EntrySessionLink("JSdotNet/Backlog", "e711d47d-3e09-4254")];

        string? opened = null;
        var pane = host.Context.Render<TasksPane>(parameters => parameters
            .Add(p => p.OnOpenSession, (string id) => opened = id));

        var pr = pane.Find("[data-testid='entry-pull-request']");
        Assert.Equal("a", pr.TagName, ignoreCase: true);
        Assert.Equal("https://github.com/JSdotNet/Backlog/pull/655", pr.GetAttribute("href"));
        Assert.Equal("#655", pr.QuerySelector(".integration-link__label")!.TextContent.Trim());
        Assert.Equal("Pull request JSdotNet/Backlog#655", pr.GetAttribute("title"));

        var session = pane.Find("[data-testid='entry-session']");
        Assert.Equal("button", session.TagName, ignoreCase: true);
        Assert.Equal("e711d47d", session.QuerySelector(".integration-link__label")!.TextContent.Trim());
        Assert.Contains("Session e711d47d-3e09-4254", session.GetAttribute("title"), StringComparison.Ordinal);

        await session.ClickAsync(new());

        Assert.Equal("e711d47d-3e09-4254", opened);
    }

    /// <summary>The panel keeps them out of the way of the fields: in the footer
    /// beside the creation date, not in the filing strip among the tags.</summary>
    [Fact]
    public async Task The_panel_draws_the_work_in_its_footer_not_the_filing_strip()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var row = await host.WriteEntryAsync(Entry);
        row.PullRequestLinks = [new EntryPullRequestLink("JSdotNet/Backlog", 655)];

        var pane = host.Render();

        var footer = pane.Find(".entry-detail__footer");
        Assert.NotNull(footer.QuerySelector("[data-testid='entry-work-links'] [data-testid='entry-pull-request']"));
        Assert.Null(pane.Find("[data-testid='entry-panel-filing']").QuerySelector("[data-testid='entry-pull-request']"));
    }

    [Fact]
    public async Task The_list_row_links_the_latest_pull_request_and_opens_the_latest_session()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var row = await host.WriteEntryAsync(Entry);
        row.PullRequestLinks = [new EntryPullRequestLink("JSdotNet/Backlog", 650), new EntryPullRequestLink("JSdotNet/Backlog", 655)];
        row.SessionLinks = [new EntrySessionLink("JSdotNet/Backlog", "first-session"), new EntrySessionLink("JSdotNet/Backlog", "e711d47d-3e09-4254")];

        string? opened = null;
        var pane = host.Context.Render<TasksPane>(parameters => parameters
            .Add(p => p.OnOpenSession, (string id) => opened = id));

        var list = pane.Find("[data-testid='entry-list']");
        var pr = Assert.Single(list.QuerySelectorAll("[data-testid='row-pull-request']"));
        Assert.Equal("https://github.com/JSdotNet/Backlog/pull/655", pr.GetAttribute("href"));
        Assert.Equal("#655", pr.QuerySelector(".integration-link__label")!.TextContent.Trim());

        var session = Assert.Single(list.QuerySelectorAll("[data-testid='row-session']"));
        Assert.Equal("e711d47d", session.QuerySelector(".integration-link__label")!.TextContent.Trim());
        Assert.Equal("+2", list.QuerySelector("[data-testid='row-work-more']")!.TextContent.Trim());

        // Ahead of the link handle, and not among the pickers after it.
        var item = pr.Closest("li.task-item")!;
        var parts = item.Children.Select(child => child.ClassName ?? string.Empty).ToList();
        var badges = parts.FindIndex(name => name.Contains("task-item__badges", StringComparison.Ordinal));
        Assert.NotNull(item.QuerySelector(".task-item__badges [data-testid='row-work-links']"));
        Assert.Null(item.QuerySelector(".task-item__actions [data-testid='row-work-links']"));
        Assert.True(badges >= 0);
        Assert.True(badges < parts.FindIndex(name => name.Contains("task-item__link", StringComparison.Ordinal)));

        await pane.Find("[data-testid='row-session']").ClickAsync(new());

        Assert.Equal("e711d47d-3e09-4254", opened);
    }

    /// <summary>A recorded pull request says whether it merged, on the row and on
    /// the open entry: the entry went Done when the pull request was recorded, and
    /// the link is the one place that can say the work actually landed. Quietly —
    /// the link's own ink, with the word in its tooltip, and no chip.</summary>
    [Fact]
    public async Task A_merged_pull_request_says_so_on_the_row_and_the_entry()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var row = await host.WriteEntryAsync(Entry);
        var pr = new EntryPullRequestLink("JSdotNet/Backlog", 708);
        row.PullRequestLinks = [pr];
        row.PullRequestStates = new Dictionary<EntryPullRequestLink, LinkedPullRequestState> { [pr] = LinkedPullRequestState.Merged };

        var pane = host.Render();

        foreach (var link in new[] { pane.Find("[data-testid='row-pull-request']"), pane.Find("[data-testid='entry-pull-request']") })
        {
            Assert.Contains("entry-doc__work-link--merged", link.ClassList);
            Assert.Equal("Pull request JSdotNet/Backlog#708 — merged", link.GetAttribute("title"));
            Assert.Null(link.QuerySelector(".badge"));
        }
    }

    [Fact]
    public async Task A_closed_pull_request_is_told_apart_from_a_merged_one()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var row = await host.WriteEntryAsync(Entry);
        var pr = new EntryPullRequestLink("JSdotNet/Backlog", 708);
        row.PullRequestLinks = [pr];
        row.PullRequestStates = new Dictionary<EntryPullRequestLink, LinkedPullRequestState> { [pr] = LinkedPullRequestState.Closed };

        var link = host.Render().Find("[data-testid='entry-pull-request']");

        Assert.Contains("entry-doc__work-link--closed", link.ClassList);
        Assert.DoesNotContain("entry-doc__work-link--merged", link.ClassList);
    }

    /// <summary>Not read yet is the ordinary link rather than a guess: "open" would
    /// be a state nobody checked.</summary>
    [Fact]
    public async Task A_pull_request_whose_state_was_never_read_shows_none()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var row = await host.WriteEntryAsync(Entry);
        row.PullRequestLinks = [new EntryPullRequestLink("JSdotNet/Backlog", 708)];

        var link = host.Render().Find("[data-testid='entry-pull-request']");

        Assert.DoesNotContain(link.ClassList, c => c.StartsWith("entry-doc__work-link--", StringComparison.Ordinal));
        Assert.Equal("Pull request JSdotNet/Backlog#708", link.GetAttribute("title"));
    }

    /// <summary>An open pull request's checks and its auto-merge request ride on
    /// its link, on the row and on the open entry — marks after the number, and
    /// the words in the tooltip beside the state, so colour is never the only
    /// carrier.</summary>
    [Fact]
    public async Task An_open_pull_request_shows_its_checks_and_auto_merge_on_the_row_and_the_entry()
    {
        using var host = await TasksPaneHost.CreateAsync("JSdotNet/Backlog");
        var row = await host.WriteEntryAsync(Entry);
        var pr = WithStatus(row, 708, LinkedPullRequestChecks.Passing, autoMerge: true);

        var pane = host.Render();

        foreach (var prefix in new[] { "row-", "entry-" })
        {
            var link = pane.Find($"[data-testid='{prefix}pull-request']");
            Assert.Equal(
                $"Pull request {pr.Repository}#708 — open · checks passing · auto-merge on",
                link.GetAttribute("title"));

            Assert.Contains("integration-link__checks--passing", pane.Find($"[data-testid='{prefix}pull-request-checks']").ClassList);
            Assert.NotNull(pane.Find($"[data-testid='{prefix}pull-request-auto-merge']"));
        }
    }

    /// <summary>
    /// The task row wears the verdict the Pull requests pane gives the same pull request
    /// (local ADR 0042, V2): read through the list's port over Repository Management's
    /// contract, the verdict's label leads the tooltip — the badge's accessible name —
    /// and its tone is a modifier on the badge, which keeps its own icons and words
    /// rather than becoming a second verdict banner. The words after the label say
    /// only what the verdict does not: not the state, and not the checks when the verdict
    /// is about them.
    /// </summary>
    [Theory]
    [InlineData(GitHubCheckState.Failing, false, "Failing checks", "fault")]
    [InlineData(GitHubCheckState.Pending, false, "Checks running", "live")]
    [InlineData(GitHubCheckState.Passing, true, "Ready to merge · checks passing", "settled")]
    public async Task An_open_pull_request_wears_its_verdict_on_the_row(GitHubCheckState checks, bool mergeReady, string words, string tone)
    {
        using var host = await TasksPaneHost.CreateAsync("JSdotNet/Backlog");
        var row = await host.WriteEntryAsync(Entry);
        var pr = new EntryPullRequestLink("JSdotNet/Backlog", 708);
        host.Client.PullRequestStatuses[708] = new GitHubPullRequestStatus(
            708, pr.Repository, "PR_708", GitHubItemState.Open, checks,
            AutoMergeEnabled: false, MergeReady: mergeReady, GitHubMergeMethod.Merge);
        row.PullRequestLinks = [pr];
        await host.State.SyncGitHubAsync();

        var pane = host.Render();

        var link = pane.Find("[data-testid='row-pull-request']");
        Assert.Equal($"Pull request {pr.Repository}#708 — {words}", link.GetAttribute("title"));
        Assert.Contains($"entry-doc__work-link--tone-{tone}", link.ClassList);
    }

    /// <summary>
    /// The accessible name never says a fact twice: a draft's verdict is the draft, and a
    /// review the verdict names is not worded again after it — while a review it does not
    /// name, and the auto-merge request, still are.
    /// </summary>
    [Theory]
    [InlineData(LinkedPullRequestVerdictKind.Draft, "Draft", LinkedPullRequestState.Draft, LinkedPullRequestReview.None, "Draft")]
    [InlineData(LinkedPullRequestVerdictKind.ReviewRequired, "Review required", LinkedPullRequestState.Open, LinkedPullRequestReview.ReviewRequired, "Review required · checks passing")]
    [InlineData(LinkedPullRequestVerdictKind.Ready, "Ready to merge", LinkedPullRequestState.Open, LinkedPullRequestReview.Approved, "Ready to merge · approved · checks passing")]
    public async Task A_verdict_is_not_said_twice_in_the_accessible_name(
        LinkedPullRequestVerdictKind kind, string verdict, LinkedPullRequestState state, LinkedPullRequestReview review, string words)
    {
        using var host = await TasksPaneHost.CreateAsync("JSdotNet/Backlog");
        var row = await host.WriteEntryAsync(Entry);
        var checks = state == LinkedPullRequestState.Draft ? LinkedPullRequestChecks.None : LinkedPullRequestChecks.Passing;
        WithStatus(row, 708, checks, state: state, review: review, verdict: verdict, tone: LinkedPullRequestTone.Quiet, kind: kind);

        var pane = host.Render();

        Assert.Equal($"Pull request JSdotNet/Backlog#708 — {words}", pane.Find("[data-testid='row-pull-request']").GetAttribute("title"));
    }

    /// <summary>The words leave out what the verdict names, never the marks: a pull
    /// request whose verdict is the changes a reviewer asked for keeps its review and
    /// checks marks.</summary>
    [Fact]
    public async Task A_verdict_about_the_review_keeps_the_review_mark()
    {
        using var host = await TasksPaneHost.CreateAsync("JSdotNet/Backlog");
        var row = await host.WriteEntryAsync(Entry);
        WithStatus(row, 708, LinkedPullRequestChecks.Passing, review: LinkedPullRequestReview.ChangesRequested,
            verdict: "Changes requested", tone: LinkedPullRequestTone.Fault, kind: LinkedPullRequestVerdictKind.ChangesRequested);

        var pane = host.Render();

        Assert.Equal("Pull request JSdotNet/Backlog#708 — Changes requested · checks passing", pane.Find("[data-testid='row-pull-request']").GetAttribute("title"));
        Assert.Contains("integration-link__review--changes-requested", pane.Find("[data-testid='row-pull-request-review']").ClassList);
        Assert.NotNull(pane.Find("[data-testid='row-pull-request-checks']"));
    }

    /// <summary>A merged pull request keeps its own ink and wears no verdict tone: the
    /// merged colour already says it, and its checks no longer gate anything.</summary>
    [Fact]
    public async Task A_merged_pull_request_wears_no_verdict_tone()
    {
        using var host = await TasksPaneHost.CreateAsync("JSdotNet/Backlog");
        var row = await host.WriteEntryAsync(Entry);
        WithStatus(row, 708, LinkedPullRequestChecks.Passing, state: LinkedPullRequestState.Merged, verdict: "Merged", tone: LinkedPullRequestTone.Settled);

        var pane = host.Render();

        var link = pane.Find("[data-testid='row-pull-request']");
        Assert.Contains("entry-doc__work-link--merged", link.ClassList);
        Assert.DoesNotContain(link.ClassList, cls => cls.StartsWith("entry-doc__work-link--tone-", StringComparison.Ordinal));
        Assert.Equal("Pull request JSdotNet/Backlog#708 — merged", link.GetAttribute("title"));
    }

    [Theory]
    [InlineData(LinkedPullRequestChecks.Pending, "pending", "checks pending")]
    [InlineData(LinkedPullRequestChecks.Failing, "failing", "checks failing")]
    public async Task Each_check_state_is_its_own_mark_and_its_own_words(LinkedPullRequestChecks checks, string slug, string words)
    {
        using var host = await TasksPaneHost.CreateAsync("JSdotNet/Backlog");
        var row = await host.WriteEntryAsync(Entry);
        WithStatus(row, 708, checks);

        var pane = host.Render();

        var link = pane.Find("[data-testid='entry-pull-request']");
        Assert.Equal($"Pull request JSdotNet/Backlog#708 — open · {words}", link.GetAttribute("title"));
        Assert.Contains($"integration-link__checks--{slug}", pane.Find("[data-testid='entry-pull-request-checks']").ClassList);
        Assert.Empty(pane.FindAll("[data-testid='entry-pull-request-auto-merge']"));
    }

    /// <summary>No checks is said by saying nothing: no mark, and no words about
    /// checks in the tooltip.</summary>
    [Fact]
    public async Task A_pull_request_with_no_checks_shows_no_checks_mark()
    {
        using var host = await TasksPaneHost.CreateAsync("JSdotNet/Backlog");
        var row = await host.WriteEntryAsync(Entry);
        WithStatus(row, 708, LinkedPullRequestChecks.None);

        var pane = host.Render();

        Assert.Empty(pane.FindAll("[data-testid='entry-pull-request-checks']"));
        Assert.Equal("Pull request JSdotNet/Backlog#708 — open", pane.Find("[data-testid='entry-pull-request']").GetAttribute("title"));
    }

    /// <summary>A merged pull request's checks are history: the link says merged
    /// and nothing about checks that no longer gate anything.</summary>
    [Fact]
    public async Task A_merged_pull_request_shows_no_checks_or_auto_merge()
    {
        using var host = await TasksPaneHost.CreateAsync("JSdotNet/Backlog");
        var row = await host.WriteEntryAsync(Entry);
        WithStatus(row, 708, LinkedPullRequestChecks.Passing, autoMerge: true, state: LinkedPullRequestState.Merged);

        var pane = host.Render();

        Assert.Empty(pane.FindAll("[data-testid='entry-pull-request-checks']"));
        Assert.Empty(pane.FindAll("[data-testid='entry-pull-request-auto-merge']"));
        Assert.Equal("Pull request JSdotNet/Backlog#708 — merged", pane.Find("[data-testid='entry-pull-request']").GetAttribute("title"));
    }

    /// <summary>Gated like everything else GitHub draws: no indicator unless the
    /// integration is on and a repository is configured.</summary>
    [Fact]
    public async Task No_checks_or_auto_merge_show_while_the_integration_is_off()
    {
        using var host = await TasksPaneHost.CreateAsync("JSdotNet/Backlog");
        var row = await host.WriteEntryAsync(Entry);
        WithStatus(row, 708, LinkedPullRequestChecks.Passing, autoMerge: true);
        host.Features.SetEnabled(TasksFeatures.GitHubIntegration, enabled: false);

        var pane = host.Render();

        Assert.Empty(pane.FindAll("[data-testid='entry-pull-request-checks']"));
        Assert.Empty(pane.FindAll("[data-testid='entry-pull-request-auto-merge']"));
        Assert.Equal("Pull request JSdotNet/Backlog#708 — open", pane.Find("[data-testid='entry-pull-request']").GetAttribute("title"));
    }

    [Fact]
    public async Task No_checks_or_auto_merge_show_while_no_repository_is_configured()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var row = await host.WriteEntryAsync(Entry);
        WithStatus(row, 708, LinkedPullRequestChecks.Passing, autoMerge: true);

        var pane = host.Render();

        Assert.Empty(pane.FindAll("[data-testid='entry-pull-request-checks']"));
        Assert.Empty(pane.FindAll("[data-testid='entry-pull-request-auto-merge']"));
    }

    /// <summary>An open pull request's review state is carried onto the link model
    /// the row and the open entry hand <see cref="IntegrationLink"/>, and worded in
    /// its tooltip after the state.</summary>
    [Theory]
    [InlineData(LinkedPullRequestReview.ReviewRequired, IntegrationReviewState.ReviewRequired, " · review required")]
    [InlineData(LinkedPullRequestReview.Approved, IntegrationReviewState.Approved, " · approved")]
    [InlineData(LinkedPullRequestReview.ChangesRequested, IntegrationReviewState.ChangesRequested, " · changes requested")]
    [InlineData(LinkedPullRequestReview.None, IntegrationReviewState.None, "")]
    public async Task An_open_pull_requests_review_state_rides_on_its_link(LinkedPullRequestReview review, IntegrationReviewState expected, string words)
    {
        using var host = await TasksPaneHost.CreateAsync("JSdotNet/Backlog");
        var row = await host.WriteEntryAsync(Entry);
        WithStatus(row, 708, LinkedPullRequestChecks.Passing, review: review);

        var pane = host.Render();

        var links = pane.FindComponents<IntegrationLink>()
            .Select(link => link.Instance.Link)
            .Where(link => link.Kind == IntegrationLinkKind.PullRequest)
            .ToList();
        Assert.NotEmpty(links);
        Assert.All(links, link => Assert.Equal(expected, link.Review));
        Assert.Equal(
            $"Pull request JSdotNet/Backlog#708 — open{words} · checks passing",
            pane.Find("[data-testid='entry-pull-request']").GetAttribute("title"));
    }

    /// <summary>A verdict is a mark beside the checks, on the row and the open
    /// entry, in its own ink; review required and no review draw nothing.</summary>
    [Theory]
    [InlineData(LinkedPullRequestReview.Approved, "approved")]
    [InlineData(LinkedPullRequestReview.ChangesRequested, "changes-requested")]
    public async Task A_review_verdict_is_a_mark_beside_the_checks(LinkedPullRequestReview review, string slug)
    {
        using var host = await TasksPaneHost.CreateAsync("JSdotNet/Backlog");
        var row = await host.WriteEntryAsync(Entry);
        WithStatus(row, 708, LinkedPullRequestChecks.Failing, review: review);

        var pane = host.Render();

        foreach (var prefix in new[] { "row-", "entry-" })
        {
            var mark = pane.Find($"[data-testid='{prefix}pull-request-review']");
            Assert.Contains($"integration-link__review--{slug}", mark.ClassList);

            // Next to the checks glyph, after it.
            var checks = pane.Find($"[data-testid='{prefix}pull-request-checks']");
            Assert.Equal(mark.OuterHtml, checks.NextElementSibling?.OuterHtml);
        }
    }

    [Theory]
    [InlineData(LinkedPullRequestReview.ReviewRequired)]
    [InlineData(LinkedPullRequestReview.None)]
    public async Task No_verdict_draws_no_review_mark(LinkedPullRequestReview review)
    {
        using var host = await TasksPaneHost.CreateAsync("JSdotNet/Backlog");
        var row = await host.WriteEntryAsync(Entry);
        WithStatus(row, 708, LinkedPullRequestChecks.Passing, review: review);

        var pane = host.Render();

        Assert.Empty(pane.FindAll("[data-testid='row-pull-request-review']"));
        Assert.Empty(pane.FindAll("[data-testid='entry-pull-request-review']"));
    }

    /// <summary>The badge's accessible name is its whole tooltip, so a reader who
    /// cannot see the inks hears the state, the verdict and the checks.</summary>
    [Fact]
    public async Task A_pull_request_badge_is_named_by_its_full_state()
    {
        using var host = await TasksPaneHost.CreateAsync("JSdotNet/Backlog");
        var row = await host.WriteEntryAsync(Entry);
        WithStatus(row, 1018, LinkedPullRequestChecks.Failing, review: LinkedPullRequestReview.ChangesRequested);

        var pane = host.Render();

        foreach (var prefix in new[] { "row-", "entry-" })
        {
            var link = pane.Find($"[data-testid='{prefix}pull-request']");
            const string expected = "Pull request JSdotNet/Backlog#1018 — open · changes requested · checks failing";
            Assert.Equal(expected, link.GetAttribute("title"));
            Assert.Equal(expected, link.GetAttribute("aria-label"));
        }
    }

    /// <summary>
    /// A linked session's mark wears the state the Sessions record answered: stalled
    /// highlighted and worded "Quiet 30 min", running neutral, finished muted — in the
    /// tooltip and the accessible name as well as the ink, and with no chip beside
    /// the id.
    /// </summary>
    [Theory]
    [InlineData(LinkedSessionState.Stalled, "stalled", "Quiet 30 min")]
    [InlineData(LinkedSessionState.Running, "running", "running")]
    [InlineData(LinkedSessionState.Finished, "finished", "finished")]
    public async Task A_session_badge_wears_its_state(LinkedSessionState state, string slug, string words)
    {
        var sessions = new FakeSessionStates { ["e711d47d-3e09-4254"] = new LinkedSession(state, Archived: null) };
        using var host = await TasksPaneHost.CreateAsync(
            roadmapTags: null, ["JSdotNet/Backlog"], sessionStates: sessions);
        var row = await host.WriteEntryAsync(Entry);
        row.SessionLinks = [new EntrySessionLink("JSdotNet/Backlog", "e711d47d-3e09-4254")];

        await host.State.ReadSessionStatesAsync();
        var pane = host.Context.Render<TasksPane>(parameters => parameters.Add(p => p.OnOpenSession, (string _) => { }));

        foreach (var prefix in new[] { "row-", "entry-" })
        {
            var session = pane.Find($"[data-testid='{prefix}session']");
            Assert.Contains($"integration-link--session-{slug}", session.ClassList);

            var expected = $"Session e711d47d-3e09-4254 · {words} — open in Sessions";
            Assert.Equal(expected, session.GetAttribute("title"));
            Assert.Equal(expected, session.GetAttribute("aria-label"));
            Assert.Null(session.QuerySelector(".integration-chip__glyph"));
        }
    }

    /// <summary>
    /// A finished session also says whether the Claude desktop app still holds it
    /// open — the muted ink, "still open" — or archived it — the quiet ink, "archived" —
    /// on the row's badge and in the panel, in the tooltip and accessible name as well
    /// as the ink. Live first: a running session is worded as running whatever the app
    /// holds. No desktop record draws exactly as today.
    /// </summary>
    [Theory]
    [InlineData(LinkedSessionState.Finished, true, "archived", "finished, archived")]
    [InlineData(LinkedSessionState.Finished, false, "open", "finished, still open")]
    [InlineData(LinkedSessionState.Finished, null, null, "finished")]
    [InlineData(LinkedSessionState.Running, true, null, "running")]
    public async Task A_finished_session_badge_says_whether_the_desktop_app_holds_it_open(
        LinkedSessionState state,
        bool? archived,
        string? presenceSlug,
        string words)
    {
        var sessions = new FakeSessionStates { ["e711d47d-3e09-4254"] = new LinkedSession(state, archived) };
        using var host = await TasksPaneHost.CreateAsync(
            roadmapTags: null, ["JSdotNet/Backlog"], sessionStates: sessions);
        var row = await host.WriteEntryAsync(Entry);
        row.SessionLinks = [new EntrySessionLink("JSdotNet/Backlog", "e711d47d-3e09-4254")];

        await host.State.ReadSessionStatesAsync();
        var pane = host.Context.Render<TasksPane>(parameters => parameters.Add(p => p.OnOpenSession, (string _) => { }));

        foreach (var prefix in new[] { "row-", "entry-" })
        {
            var session = pane.Find($"[data-testid='{prefix}session']");
            Assert.Contains($"integration-link--session-{state.ToString().ToLowerInvariant()}", session.ClassList);
            if (presenceSlug is null)
            {
                Assert.DoesNotContain(session.ClassList, name => name is "integration-link--session-open" or "integration-link--session-archived");
            }
            else
            {
                Assert.Contains($"integration-link--session-{presenceSlug}", session.ClassList);
            }

            var expected = $"Session e711d47d-3e09-4254 · {words} — open in Sessions";
            Assert.Equal(expected, session.GetAttribute("title"));
            Assert.Equal(expected, session.GetAttribute("aria-label"));
        }
    }

    /// <summary>The list keeps what the Sessions record answered about each linked
    /// session — its state, and whether the desktop app archived it — for the badges
    /// to draw from; the fact is read, never written onto the task.</summary>
    [Fact]
    public async Task The_list_keeps_whether_the_desktop_app_archived_a_linked_session()
    {
        var sessions = new FakeSessionStates
        {
            ["archived-one"] = new LinkedSession(LinkedSessionState.Finished, Archived: true),
            ["open-one"] = new LinkedSession(LinkedSessionState.Finished, Archived: false)
        };
        using var host = await TasksPaneHost.CreateAsync(
            roadmapTags: null, ["JSdotNet/Backlog"], sessionStates: sessions);
        var row = await host.WriteEntryAsync(Entry);
        row.SessionLinks =
        [
            new EntrySessionLink("JSdotNet/Backlog", "archived-one"),
            new EntrySessionLink("JSdotNet/Backlog", "open-one")
        ];

        await host.State.ReadSessionStatesAsync();

        Assert.Equal(new LinkedSession(LinkedSessionState.Finished, Archived: true), host.State.SessionOf("ARCHIVED-ONE"));
        Assert.Equal(new LinkedSession(LinkedSessionState.Finished, Archived: false), host.State.SessionOf("open-one"));
        Assert.Null(host.State.SessionOf("never-linked"));
        Assert.DoesNotContain("archived", row.RawText, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A session the record does not hold has no state to wear, and draws
    /// as a session badge always did.</summary>
    [Fact]
    public async Task A_session_the_record_does_not_hold_draws_without_a_state()
    {
        using var host = await TasksPaneHost.CreateAsync(
            roadmapTags: null, ["JSdotNet/Backlog"], sessionStates: new FakeSessionStates());
        var row = await host.WriteEntryAsync(Entry);
        row.SessionLinks = [new EntrySessionLink("JSdotNet/Backlog", "gone")];

        await host.State.ReadSessionStatesAsync();
        var pane = host.Render();

        var session = pane.Find("[data-testid='entry-session']");
        Assert.DoesNotContain(session.ClassList, name => name.StartsWith("integration-link--session", StringComparison.Ordinal));
        Assert.Equal("Session gone — open in Sessions", session.GetAttribute("title"));
    }

    /// <summary>The read asks once for every linked session across the rows.</summary>
    [Fact]
    public async Task The_session_read_asks_once_for_every_linked_session()
    {
        var sessions = new FakeSessionStates();
        using var host = await TasksPaneHost.CreateAsync(
            roadmapTags: null, ["JSdotNet/Backlog"], sessionStates: sessions);
        var first = await host.WriteEntryAsync(Entry);
        var second = await host.WriteEntryAsync("# Second\n`prompt`\n");
        first.SessionLinks = [new EntrySessionLink("JSdotNet/Backlog", "a"), new EntrySessionLink("JSdotNet/Backlog", "b")];
        second.SessionLinks = [new EntrySessionLink("JSdotNet/Backlog", "B")];
        var before = sessions.Asked.Count;

        await host.State.ReadSessionStatesAsync();

        Assert.Equal(before + 1, sessions.Asked.Count);
        Assert.Equal(["a", "b"], sessions.Asked[^1]);
    }

    /// <summary>A merged pull request's review state is history, gated exactly as
    /// its checks are.</summary>
    [Fact]
    public async Task A_merged_pull_request_carries_no_review_state()
    {
        using var host = await TasksPaneHost.CreateAsync("JSdotNet/Backlog");
        var row = await host.WriteEntryAsync(Entry);
        WithStatus(row, 708, LinkedPullRequestChecks.Passing, state: LinkedPullRequestState.Merged, review: LinkedPullRequestReview.Approved);

        var pane = host.Render();

        Assert.All(
            pane.FindComponents<IntegrationLink>().Select(link => link.Instance.Link).Where(link => link.Kind == IntegrationLinkKind.PullRequest),
            link => Assert.Equal(IntegrationReviewState.None, link.Review));
    }

    [Fact]
    public async Task No_review_state_rides_on_the_link_while_the_integration_is_off()
    {
        using var host = await TasksPaneHost.CreateAsync("JSdotNet/Backlog");
        var row = await host.WriteEntryAsync(Entry);
        WithStatus(row, 708, LinkedPullRequestChecks.Passing, review: LinkedPullRequestReview.Approved);
        host.Features.SetEnabled(TasksFeatures.GitHubIntegration, enabled: false);

        var pane = host.Render();

        Assert.All(
            pane.FindComponents<IntegrationLink>().Select(link => link.Instance.Link).Where(link => link.Kind == IntegrationLinkKind.PullRequest),
            link => Assert.Equal(IntegrationReviewState.None, link.Review));
    }

    private sealed class FakeSessionStates : Dictionary<string, LinkedSession>, ILinkedSessionStates
    {
        public FakeSessionStates() : base(StringComparer.OrdinalIgnoreCase)
        {
        }

        public List<IReadOnlyCollection<string>> Asked { get; } = [];

        public Task<IReadOnlyDictionary<string, LinkedSession>> StatesOfAsync(
            IReadOnlyCollection<string> sessionIds,
            CancellationToken cancellationToken = default)
        {
            Asked.Add(sessionIds);
            return Task.FromResult<IReadOnlyDictionary<string, LinkedSession>>(
                sessionIds.Where(ContainsKey).ToDictionary(id => id, id => this[id]));
        }
    }

    /// <summary>A row with one recorded pull request whose status has been read, the
    /// way <c>RefreshPullRequestStatesAsync</c> leaves one.</summary>
    private static EntryPullRequestLink WithStatus(
        EntryRow row,
        int number,
        LinkedPullRequestChecks checks,
        bool autoMerge = false,
        LinkedPullRequestState state = LinkedPullRequestState.Open,
        LinkedPullRequestReview review = LinkedPullRequestReview.None,
        string? verdict = null,
        LinkedPullRequestTone? tone = null,
        LinkedPullRequestVerdictKind? kind = null)
    {
        var pr = new EntryPullRequestLink("JSdotNet/Backlog", number);
        row.PullRequestLinks = [pr];
        row.PullRequestStates = new Dictionary<EntryPullRequestLink, LinkedPullRequestState> { [pr] = state };
        row.PullRequestStatuses = new Dictionary<EntryPullRequestLink, LinkedPullRequest>
        {
            [pr] = new(state) { Checks = checks, AutoMerge = autoMerge, Review = review, VerdictLabel = verdict, VerdictTone = tone, VerdictKind = kind }
        };
        return pr;
    }

    /// <summary>With nobody to open it, a session is text, not a button that has
    /// to refuse the click.</summary>
    [Fact]
    public async Task A_session_nobody_can_open_is_inert_text()
    {
        using var host = await TasksPaneHost.CreateAsync();
        var row = await host.WriteEntryAsync(Entry);
        row.SessionLinks = [new EntrySessionLink("JSdotNet/Backlog", "e711d47d")];

        var pane = host.Render();

        Assert.Equal("span", pane.Find("[data-testid='entry-session']").TagName, ignoreCase: true);
    }

    [Fact]
    public async Task An_entry_with_no_work_recorded_shows_neither()
    {
        using var host = await TasksPaneHost.CreateAsync();
        await host.WriteEntryAsync(Entry);

        var pane = host.Render();

        Assert.Empty(pane.FindAll("[data-testid='entry-pull-request']"));
        Assert.Empty(pane.FindAll("[data-testid='entry-session']"));
        Assert.Empty(pane.FindAll("[data-testid='entry-work-links']"));
        Assert.Empty(pane.FindAll("[data-testid='row-work-links']"));
    }

    private static TaskItemDto Dto(params EntryProjectionDto[] projections) => new(
        Guid.NewGuid(),
        "Record the adoption decision",
        string.Empty,
        EntryType.Task,
        Priority.Medium,
        EntryStatus.InProgress,
        Area: null,
        Tags: [],
        Order: 0,
        TotalSubItems: 0,
        CompletedSubItems: 0,
        Projections: projections);
}
