using Backlog.Desktop.UI.PullRequests;
using Backlog.Infrastructure.GitHub;
using Backlog.Modules.Sessions.Abstractions;
using Backlog.UI.Components.Feedback;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// The pull requests pane reads every registered repository through the GitHub
/// adapter and draws one table. What is asserted here is the pane's own part: that
/// it opens on the reader's own pull requests and Everyone's widens it, that the
/// header's scope narrows it, that each act is offered only in the state it fits and
/// reaches GitHub, that a repository GitHub would not answer for is named without
/// taking the others with it, and that a row leads back to its task and its session.
/// <para>
/// The adapter is the real <see cref="GitHubIntegration"/> over a client double, so
/// the per-repository failure handling and the refusal wording under test are the
/// ones the app runs. bUnit swallows an exception thrown from a click handler, so
/// every act is asserted by its effect — what the client was asked, what the toast
/// said, what the table shows after the re-read — never by the absence of a throw.
/// </para>
/// </summary>
public sealed class PullRequestsPaneTests : IDisposable
{
    private static readonly DateTimeOffset Noon = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "backlog-pull-requests-pane-tests", Guid.NewGuid().ToString("n"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public void Every_repositorys_open_pull_requests_are_rows_newest_update_first()
    {
        var client = new StubClient()
            .Lists("JSdotNet/Backlog", Pull("JSdotNet/Backlog", 1, updated: Noon.AddHours(-2)))
            .Lists("JSdotNet/Archify", Pull("JSdotNet/Archify", 2, updated: Noon.AddHours(-1)));
        using var context = Context(client);

        var pane = context.Render<PullRequestsPane>();

        pane.WaitForAssertion(() =>
        {
            var rows = pane.FindAll("[data-testid='pull-request-row']");
            Assert.Equal(2, rows.Count);
            Assert.Equal(["JSdotNet/Archify#2", "JSdotNet/Backlog#1"], rows.Select(row => row.GetAttribute("data-pull-request")));

            // The repository by its alias, the pull request as an anchor to GitHub,
            // and the branch it would merge where.
            Assert.Equal("archify", rows[0].QuerySelector("[data-testid='pull-request-repository']")!.TextContent.Trim());
            Assert.Equal("https://github.com/JSdotNet/Archify/pull/2", rows[0].QuerySelector("a[data-testid='pull-request-link']")!.GetAttribute("href"));
            Assert.Contains("feature-2 → main", rows[0].TextContent, StringComparison.Ordinal);
        });
    }

    /// <summary>Mine on every open, because the list a person opens this for is their
    /// own; Everyone's widens it, and the badge names both numbers while it does
    /// not.</summary>
    [Fact]
    public void Mine_is_the_default_and_everyones_shows_the_rest()
    {
        var client = new StubClient().Lists(
            "JSdotNet/Backlog",
            Pull("JSdotNet/Backlog", 1, mine: true),
            Pull("JSdotNet/Backlog", 2, mine: false));
        using var context = Context(client);

        var pane = context.Render<PullRequestsPane>();

        pane.WaitForAssertion(() =>
        {
            Assert.Equal(["JSdotNet/Backlog#1"], RowKeys(pane));
            Assert.Equal("true", pane.Find("[data-testid='pull-requests-filter-mine']").GetAttribute("aria-pressed"));
            Assert.Equal("1 of 2 pull requests", pane.Find("[data-testid='pull-requests-count']").TextContent.Trim());
        });

        pane.Find("[data-testid='pull-requests-filter-everyone']").Click();

        pane.WaitForAssertion(() =>
        {
            Assert.Equal(["JSdotNet/Backlog#1", "JSdotNet/Backlog#2"], RowKeys(pane));
            Assert.Equal("2 pull requests", pane.Find("[data-testid='pull-requests-count']").TextContent.Trim());
        });

        pane.Find("[data-testid='pull-requests-filter-mine']").Click();

        pane.WaitForAssertion(() => Assert.Equal(["JSdotNet/Backlog#1"], RowKeys(pane)));
    }

    [Fact]
    public void Nothing_of_the_readers_own_says_where_the_rest_are()
    {
        using var context = Context(new StubClient().Lists("JSdotNet/Backlog", Pull("JSdotNet/Backlog", 1, mine: false)));

        var pane = context.Render<PullRequestsPane>();

        pane.WaitForAssertion(() =>
        {
            Assert.Empty(pane.FindAll("[data-testid='pull-request-row']"));
            Assert.Contains("None of the open pull requests is yours.", pane.Markup, StringComparison.Ordinal);
            Assert.Contains("Choose Everyone's", pane.Markup, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void The_header_scope_narrows_the_rows_to_its_repositories()
    {
        var client = new StubClient()
            .Lists("JSdotNet/Backlog", Pull("JSdotNet/Backlog", 1))
            .Lists("JSdotNet/Archify", Pull("JSdotNet/Archify", 2));
        using var context = Context(client);

        var pane = context.Render<PullRequestsPane>(parameters => parameters
            .Add(p => p.RepositoryScope, ["archify"]));

        pane.WaitForAssertion(() =>
        {
            Assert.Equal(["JSdotNet/Archify#2"], RowKeys(pane));
            Assert.Contains("Narrowed to archify.", pane.Find("[data-testid='pull-requests-subtitle']").TextContent, StringComparison.Ordinal);
        });
    }

    /// <summary>A repository GitHub would not answer for is named, in its words, with
    /// a Retry — and every other repository is listed as if it were not there.</summary>
    [Fact]
    public void A_repository_that_could_not_be_read_is_named_and_the_others_still_render()
    {
        var client = new StubClient()
            .Lists("JSdotNet/Backlog", Pull("JSdotNet/Backlog", 1))
            .Refuses("octo/broken", "Could not resolve to a Repository with the name 'octo/broken'.");
        using var context = Context(client);

        var pane = context.Render<PullRequestsPane>();

        pane.WaitForAssertion(() =>
        {
            Assert.Equal(["JSdotNet/Backlog#1"], RowKeys(pane));

            var failure = pane.Find("[data-testid='pull-requests-unreadable']");
            Assert.Equal("alert", failure.GetAttribute("role"));
            Assert.Contains("Couldn't read octo/broken: Could not resolve", failure.TextContent, StringComparison.Ordinal);
            Assert.NotEmpty(pane.FindAll("[data-testid='pull-requests-unreadable-retry']"));
        });
    }

    [Fact]
    public void With_no_repository_registered_the_pane_says_where_to_register_one()
    {
        var client = new StubClient();
        using var context = Context(client, repositories: string.Empty);

        var pane = context.Render<PullRequestsPane>();

        pane.WaitForAssertion(() =>
        {
            Assert.NotEmpty(pane.FindAll("[data-testid='pull-requests-not-configured']"));
            Assert.Empty(pane.FindAll("[data-testid='pull-requests-table']"));
        });

        Assert.Equal(0, client.ListCalls);
    }

    [Fact]
    public void A_host_without_the_github_adapter_is_the_not_configured_state()
    {
        using var context = new BunitContext();

        var pane = context.Render<PullRequestsPane>();

        pane.WaitForAssertion(() => Assert.NotEmpty(pane.FindAll("[data-testid='pull-requests-not-configured']")));
    }

    /// <summary>Each act is offered in exactly the state it fits: Update branch on a
    /// branch GitHub reports behind, Ready for review on a draft, and one merge act
    /// that matches what GitHub would accept — or none on a draft, which GitHub
    /// merges no way, and none on a branch in conflict, which no passing check
    /// will ever let merge.</summary>
    [Theory]
    [InlineData(false, false, false, false, false, null, "Merge when checks pass")]
    [InlineData(false, true, false, false, false, null, "Merge now")]
    [InlineData(false, false, true, false, false, null, "Cancel auto-merge")]
    [InlineData(true, false, false, false, false, "ready", null)]
    [InlineData(true, false, true, false, false, "ready", "Cancel auto-merge")]
    [InlineData(false, false, false, true, false, "update-branch", "Merge when checks pass")]
    [InlineData(false, false, false, false, true, null, null)]
    [InlineData(false, false, true, false, true, null, "Cancel auto-merge")]
    public void Each_act_is_offered_only_in_its_state(
        bool draft, bool mergeReady, bool autoMerge, bool behind, bool conflicts, string? extra, string? merge)
    {
        var pull = Pull("JSdotNet/Backlog", 1, draft: draft, mergeReady: mergeReady, autoMerge: autoMerge, behind: behind, conflicts: conflicts);
        using var context = Context(new StubClient().Lists("JSdotNet/Backlog", pull));

        var pane = context.Render<PullRequestsPane>();

        pane.WaitForAssertion(() =>
        {
            Assert.Equal(extra == "update-branch", pane.FindAll("[data-testid='pull-request-update-branch']").Count == 1);
            Assert.Equal(extra == "ready", pane.FindAll("[data-testid='pull-request-ready']").Count == 1);

            var mergeButtons = pane.FindAll("[data-testid='pull-request-merge']");
            if (merge is null)
            {
                Assert.Empty(mergeButtons);
            }
            else
            {
                Assert.Equal(merge, Assert.Single(mergeButtons).TextContent.Trim());
            }
        });
    }

    [Fact]
    public void Update_branch_sends_the_head_the_list_read_and_reads_that_repository_again()
    {
        var client = new StubClient()
            .Lists("JSdotNet/Backlog", Pull("JSdotNet/Backlog", 1, behind: true))
            .Lists("JSdotNet/Archify", Pull("JSdotNet/Archify", 2));
        using var context = Context(client);

        var pane = context.Render<PullRequestsPane>();
        pane.WaitForAssertion(() => Assert.Equal(2, RowKeys(pane).Count));

        // The re-read finds the branch up to date.
        client.Lists("JSdotNet/Backlog", Pull("JSdotNet/Backlog", 1, behind: false));

        pane.Find("[data-testid='pull-request-update-branch']").Click();

        pane.WaitForAssertion(() =>
        {
            Assert.Equal(["update JSdotNet/Backlog#1 sha-1"], client.Acts);
            Assert.Empty(pane.FindAll("[data-testid='pull-request-update-branch']"));

            // One more read, of the repository acted on and no other.
            Assert.Equal(["JSdotNet/Backlog"], client.ListedAfterFirstRead);
            Assert.Equal(2, RowKeys(pane).Count);

            Assert.Contains(Toasts(context), toast => toast.Message == "Asked GitHub to update the branch of JSdotNet/Backlog#1.");
        });
    }

    [Fact]
    public void Ready_for_review_takes_the_draft_out_of_draft()
    {
        var client = new StubClient().Lists("JSdotNet/Backlog", Pull("JSdotNet/Backlog", 1, draft: true));
        using var context = Context(client);

        var pane = context.Render<PullRequestsPane>();
        pane.WaitForAssertion(() => Assert.NotEmpty(pane.FindAll("[data-testid='pull-request-ready']")));

        client.Lists("JSdotNet/Backlog", Pull("JSdotNet/Backlog", 1, draft: false));
        pane.Find("[data-testid='pull-request-ready']").Click();

        pane.WaitForAssertion(() =>
        {
            Assert.Equal(["ready PR_1"], client.Acts);
            Assert.Empty(pane.FindAll("[data-testid='pull-request-ready']"));
            Assert.Equal("Merge when checks pass", pane.Find("[data-testid='pull-request-merge']").TextContent.Trim());
        });
    }

    [Theory]
    [InlineData(true, false, "merge PR_1 Squash")]
    [InlineData(false, false, "auto-merge PR_1 Squash")]
    [InlineData(false, true, "cancel auto-merge PR_1")]
    public void Merge_does_the_act_its_label_names(bool mergeReady, bool autoMerge, string expected)
    {
        var client = new StubClient().Lists("JSdotNet/Backlog", Pull("JSdotNet/Backlog", 1, mergeReady: mergeReady, autoMerge: autoMerge));
        using var context = Context(client);

        var pane = context.Render<PullRequestsPane>();
        pane.WaitForAssertion(() => Assert.NotEmpty(pane.FindAll("[data-testid='pull-request-merge']")));

        pane.Find("[data-testid='pull-request-merge']").Click();

        pane.WaitForAssertion(() => Assert.Equal([expected], client.Acts));
    }

    /// <summary>A merged pull request is no longer open, so the re-read after the
    /// merge takes its row away — the act's own answer.</summary>
    [Fact]
    public void A_merged_pull_request_leaves_the_list()
    {
        var client = new StubClient().Lists("JSdotNet/Backlog", Pull("JSdotNet/Backlog", 1, mergeReady: true));
        using var context = Context(client);

        var pane = context.Render<PullRequestsPane>();
        pane.WaitForAssertion(() => Assert.Single(RowKeys(pane)));

        client.Lists("JSdotNet/Backlog");
        pane.Find("[data-testid='pull-request-merge']").Click();

        pane.WaitForAssertion(() =>
        {
            Assert.Empty(RowKeys(pane));
            Assert.Contains(Toasts(context), toast => toast.Message == "Merged JSdotNet/Backlog#1.");
        });
    }

    /// <summary>A refusal reaches the reader as the adapter worded it — the act and
    /// the pull request named — and the row is read again, so a state that moved on
    /// since it was drawn is corrected.</summary>
    [Fact]
    public void A_refused_act_is_a_toast_naming_the_pull_request_and_the_row_is_read_again()
    {
        var client = new StubClient()
            .Lists("JSdotNet/Backlog", Pull("JSdotNet/Backlog", 1, behind: true))
            .RefusesActs("Something new GitHub started saying");
        using var context = Context(client);

        var pane = context.Render<PullRequestsPane>();
        pane.WaitForAssertion(() => Assert.NotEmpty(pane.FindAll("[data-testid='pull-request-update-branch']")));

        pane.Find("[data-testid='pull-request-update-branch']").Click();

        pane.WaitForAssertion(() =>
        {
            var toast = Assert.Single(Toasts(context));
            Assert.Equal(ToastSeverity.Error, toast.Severity);
            Assert.Equal("Couldn't update the branch of JSdotNet/Backlog#1: Something new GitHub started saying", toast.Message);
            Assert.Equal(["JSdotNet/Backlog"], client.ListedAfterFirstRead);

            // Enabled again once the act and its read are over.
            Assert.False(pane.Find("[data-testid='pull-request-update-branch']").HasAttribute("disabled"));
        });
    }

    /// <summary>With no toast channel composed, the outcome is the pane's own line —
    /// an act whose result reaches nobody would be an act the reader has to go and
    /// check on GitHub.</summary>
    [Fact]
    public void Without_a_toast_channel_the_outcome_is_a_line_in_the_pane()
    {
        var client = new StubClient().Lists("JSdotNet/Backlog", Pull("JSdotNet/Backlog", 1, draft: true));
        using var context = Context(client, toasts: false);

        var pane = context.Render<PullRequestsPane>();
        pane.WaitForAssertion(() => Assert.NotEmpty(pane.FindAll("[data-testid='pull-request-ready']")));

        pane.Find("[data-testid='pull-request-ready']").Click();

        pane.WaitForAssertion(() =>
            Assert.Equal("JSdotNet/Backlog#1 is ready for review.", pane.Find("[data-testid='pull-requests-outcome']").TextContent.Trim()));
    }

    [Fact]
    public void A_pull_request_a_task_recorded_opens_that_task()
    {
        var task = new DeliveryRunReference(DeliveryRunReferenceKind.Task, "Pull requests page", null, null, null, EntryId: Guid.NewGuid());
        var asked = new List<(string, int)>();
        DeliveryRunReference? opened = null;

        using var context = Context(new StubClient().Lists(
            "JSdotNet/Backlog",
            Pull("JSdotNet/Backlog", 1),
            Pull("JSdotNet/Backlog", 2, updated: Noon.AddHours(-5))));

        var pane = context.Render<PullRequestsPane>(parameters => parameters
            .Add(p => p.PullRequestTask, (pull, mineOnly) =>
            {
                asked.Add((pull.RepositoryFullName, pull.Number));
                return pull.Number == 1 ? task : null;
            })
            .Add(p => p.OnOpenTask, (DeliveryRunReference reference) => opened = reference));

        pane.WaitForAssertion(() =>
        {
            Assert.Equal("Pull requests page", pane.Find("[data-testid='pull-request-task']").TextContent.Trim());
            Assert.Single(pane.FindAll("[data-testid='pull-request-no-task']"));
        });

        Assert.Contains(("JSdotNet/Backlog", 1), asked);

        pane.Find("[data-testid='pull-request-task']").Click();

        pane.WaitForAssertion(() => Assert.Same(task, opened));
    }

    /// <summary>The session whose transcript recorded the pull request's address wins
    /// over one that merely ran on the same branch: the address is the session saying
    /// so, the branch is two facts that happen to agree.</summary>
    [Fact]
    public void The_session_that_recorded_the_pull_request_is_its_session()
    {
        var recorded = Session("recorded-1", branch: "other-branch", lastActivity: Noon.AddHours(-3)) with
        {
            PullRequests = [new AgentPullRequest("JSdotNet/Backlog", 1, "https://GITHUB.com/JSdotNet/Backlog/pull/1", null)]
        };
        var onBranch = Session("on-branch-1", branch: "feature-1", repository: "JSdotNet/Backlog", lastActivity: Noon);
        string? opened = null;

        using var context = Context(new StubClient().Lists("JSdotNet/Backlog", Pull("JSdotNet/Backlog", 1)), sessions: [onBranch, recorded]);

        var pane = context.Render<PullRequestsPane>(parameters => parameters
            .Add(p => p.OnOpenSession, (string id) => opened = id));

        // The link carries the session's state chip after its label.
        pane.WaitForAssertion(() => Assert.StartsWith("Title of recorded-1", pane.Find("[data-testid='pull-request-session']").TextContent.Trim(), StringComparison.Ordinal));

        pane.Find("[data-testid='pull-request-session']").Click();

        pane.WaitForAssertion(() => Assert.Equal("recorded-1", opened));
    }

    /// <summary>With no recorded address, the newest session on the head branch in the
    /// same repository — the agent's recorded one or the one this product placed it in
    /// by its folder — and never one on that branch name in another repository.</summary>
    [Fact]
    public void Without_a_recorded_address_the_newest_session_on_the_head_branch_is_its_session()
    {
        var older = Session("older-1", branch: "feature-1", repository: "JSdotNet/Backlog", lastActivity: Noon.AddHours(-4));
        var newer = Session("newer-1", branch: "feature-1", resolved: "JSdotNet/Backlog", lastActivity: Noon.AddHours(-1));
        var elsewhere = Session("elsewhere-1", branch: "feature-1", repository: "JSdotNet/Archify", lastActivity: Noon);
        string? opened = null;

        using var context = Context(new StubClient().Lists("JSdotNet/Backlog", Pull("JSdotNet/Backlog", 1)), sessions: [older, newer, elsewhere]);

        var pane = context.Render<PullRequestsPane>(parameters => parameters
            .Add(p => p.OnOpenSession, (string id) => opened = id));

        pane.WaitForAssertion(() => Assert.NotEmpty(pane.FindAll("[data-testid='pull-request-session']")));

        pane.Find("[data-testid='pull-request-session']").Click();

        pane.WaitForAssertion(() => Assert.Equal("newer-1", opened));
    }

    [Fact]
    public void A_pull_request_no_session_ran_says_so_with_an_em_dash()
    {
        using var context = Context(
            new StubClient().Lists("JSdotNet/Backlog", Pull("JSdotNet/Backlog", 1)),
            sessions: [Session("unrelated", branch: "main", repository: "JSdotNet/Backlog", lastActivity: Noon)]);

        var pane = context.Render<PullRequestsPane>();

        pane.WaitForAssertion(() =>
        {
            Assert.Empty(pane.FindAll("[data-testid='pull-request-session']"));
            Assert.Equal("—", pane.Find("[data-testid='pull-request-no-session']").TextContent.Trim());
        });
    }

    [Fact]
    public void The_status_says_draft_checks_behind_conflicts_and_auto_merge_in_words()
    {
        var pull = Pull("JSdotNet/Backlog", 1, draft: true, behind: true, conflicts: true, autoMerge: true) with { Checks = GitHubCheckState.Failing };
        using var context = Context(new StubClient().Lists("JSdotNet/Backlog", pull));

        var pane = context.Render<PullRequestsPane>();

        pane.WaitForAssertion(() =>
        {
            Assert.Contains("Draft", pane.Find("[data-testid='pull-request-state']").TextContent, StringComparison.Ordinal);

            // One GitHub button carrying the state as its colour, and no state chip
            // repeating it anywhere in the row.
            Assert.Contains("integration-link--state-draft", pane.Find("a[data-testid='pull-request-link']").ClassList);
            Assert.Empty(pane.FindAll("[data-testid='pull-request-row'] .badge--integration"));
            Assert.Equal("Checks failing", pane.Find("[data-testid='pull-request-checks']").TextContent.Trim());
            Assert.Equal("Behind", pane.Find("[data-testid='pull-request-behind']").TextContent.Trim());
            Assert.Equal("Conflicts", pane.Find("[data-testid='pull-request-conflicts']").TextContent.Trim());
            Assert.Equal("Auto-merge on", pane.Find("[data-testid='pull-request-auto-merge']").TextContent.Trim());
        });
    }

    // --- Recently merged ------------------------------------------------------

    /// <summary>The second view is read on the first switch to it and not before —
    /// most opens are for the open list, and a query per repository for a view nobody
    /// looked at is spent for nothing — and then kept, so switching back and forth is
    /// a render rather than a round trip.</summary>
    [Fact]
    public void Recently_merged_is_read_on_the_first_switch_and_lists_the_newest_merge_first()
    {
        var client = new StubClient()
            .Lists("JSdotNet/Backlog", Pull("JSdotNet/Backlog", 1))
            .Merged("JSdotNet/Backlog", MergedPull("JSdotNet/Backlog", 10, merged: Noon.AddDays(-3)))
            .Merged("JSdotNet/Archify", MergedPull("JSdotNet/Archify", 20, merged: Noon.AddHours(-2)));
        using var context = Context(client);

        var pane = context.Render<PullRequestsPane>();
        pane.WaitForAssertion(() => Assert.Equal(["JSdotNet/Backlog#1"], RowKeys(pane)));

        Assert.Equal("true", pane.Find("[data-testid='pull-requests-view-open']").GetAttribute("aria-pressed"));
        Assert.Empty(client.MergedListed);

        pane.Find("[data-testid='pull-requests-view-merged']").Click();

        pane.WaitForAssertion(() =>
        {
            Assert.Equal(["JSdotNet/Archify#20", "JSdotNet/Backlog#10"], RowKeys(pane));
            Assert.Equal("true", pane.Find("[data-testid='pull-requests-view-merged']").GetAttribute("aria-pressed"));

            var row = pane.FindAll("[data-testid='pull-request-row']")[0];
            Assert.Equal("archify", row.QuerySelector("[data-testid='pull-request-repository']")!.TextContent.Trim());
            Assert.Equal("https://github.com/JSdotNet/Archify/pull/20", row.QuerySelector("a[data-testid='pull-request-link']")!.GetAttribute("href"));
            Assert.Contains("feature-20 → main", row.TextContent, StringComparison.Ordinal);
            Assert.Contains("Merged", row.QuerySelector("[data-testid='pull-request-state']")!.TextContent, StringComparison.Ordinal);

            // The link carries Merged as its colour, and no chip repeats it.
            Assert.Contains("integration-link--state-merged", row.QuerySelector("a[data-testid='pull-request-link']")!.ClassList);
            Assert.Empty(row.QuerySelectorAll(".badge--integration"));

            // Coarse in the cell, with who merged it beside; exact in the title.
            var mergedAt = row.QuerySelector("[data-testid='pull-request-merged-at']")!;
            Assert.Equal("2h ago", mergedAt.TextContent.Trim());
            Assert.Contains(
                Noon.AddHours(-2).ToLocalTime().ToString("ddd d MMM yyyy HH:mm", System.Globalization.CultureInfo.CurrentCulture),
                mergedAt.GetAttribute("title"),
                StringComparison.Ordinal);
            Assert.Equal("merger-20", row.QuerySelector("[data-testid='pull-request-merged-by']")!.TextContent.Trim());
            Assert.Contains("2h ago · by merger-20", NormalizedText(mergedAt.ParentElement!), StringComparison.Ordinal);
        });

        // No column of its own for the merger: the table has to fit beside the shell.
        Assert.Equal(
            ["Repository", "Pull request", "Branch", "Merged", "Task", "Session"],
            pane.FindAll("[data-testid='pull-requests-merged-table'] th").Select(header => header.TextContent.Trim()));

        Assert.Equal(3, client.MergedListed.Count);

        pane.Find("[data-testid='pull-requests-view-open']").Click();
        pane.WaitForAssertion(() => Assert.Equal(["JSdotNet/Backlog#1"], RowKeys(pane)));

        pane.Find("[data-testid='pull-requests-view-merged']").Click();
        pane.WaitForAssertion(() => Assert.Equal(2, RowKeys(pane).Count));

        Assert.Equal(3, client.MergedListed.Count);
    }

    /// <summary>The wording follows the view: nothing on the merged view may call its
    /// rows open.</summary>
    [Fact]
    public void The_heading_count_and_empty_state_name_the_view()
    {
        using var context = Context(new StubClient().Lists("JSdotNet/Backlog", Pull("JSdotNet/Backlog", 1)));

        var pane = context.Render<PullRequestsPane>();
        pane.WaitForAssertion(() => Assert.Equal("Open on GitHub", pane.Find("#pull-requests-title").TextContent.Trim()));

        pane.Find("[data-testid='pull-requests-view-merged']").Click();

        pane.WaitForAssertion(() =>
        {
            Assert.Equal("Recently merged on GitHub", pane.Find("#pull-requests-title").TextContent.Trim());
            Assert.Equal("0 merged pull requests", pane.Find("[data-testid='pull-requests-count']").TextContent.Trim());
            Assert.Contains("merged in the last 14 days", pane.Find("[data-testid='pull-requests-subtitle']").TextContent, StringComparison.Ordinal);
            Assert.Contains("No pull requests merged in the last 14 days.", pane.Markup, StringComparison.Ordinal);
            Assert.DoesNotContain("open pull request", pane.Find("[data-testid='pull-requests-panel']").TextContent, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public void Mine_narrows_the_merged_view_to_the_readers_own()
    {
        var client = new StubClient().Merged(
            "JSdotNet/Backlog",
            MergedPull("JSdotNet/Backlog", 10, mine: true),
            MergedPull("JSdotNet/Backlog", 11, mine: false));
        using var context = Context(client);

        var pane = OnMergedView(context);

        pane.WaitForAssertion(() =>
        {
            Assert.Equal(["JSdotNet/Backlog#10"], RowKeys(pane));
            Assert.Equal("1 of 2 merged pull requests", pane.Find("[data-testid='pull-requests-count']").TextContent.Trim());
        });

        pane.Find("[data-testid='pull-requests-filter-everyone']").Click();

        pane.WaitForAssertion(() => Assert.Equal(["JSdotNet/Backlog#10", "JSdotNet/Backlog#11"], RowKeys(pane)));
    }

    [Fact]
    public void The_header_scope_narrows_the_merged_view()
    {
        var client = new StubClient()
            .Merged("JSdotNet/Backlog", MergedPull("JSdotNet/Backlog", 10))
            .Merged("JSdotNet/Archify", MergedPull("JSdotNet/Archify", 20));
        using var context = Context(client);

        var pane = OnMergedView(context, parameters => parameters.Add(p => p.RepositoryScope, ["archify"]));

        pane.WaitForAssertion(() =>
        {
            Assert.Equal(["JSdotNet/Archify#20"], RowKeys(pane));
            Assert.Contains("Narrowed to archify.", pane.Find("[data-testid='pull-requests-subtitle']").TextContent, StringComparison.Ordinal);
        });
    }

    /// <summary>The merged view's failures are its own read's, in the same alert —
    /// a repository the open read could answer for may still refuse this one.</summary>
    [Fact]
    public void A_repository_whose_merged_pull_requests_could_not_be_read_is_named_in_the_same_alert()
    {
        var client = new StubClient()
            .Merged("JSdotNet/Backlog", MergedPull("JSdotNet/Backlog", 10))
            .RefusesMerged("octo/broken", "Could not resolve to a Repository with the name 'octo/broken'.");
        using var context = Context(client);

        var pane = OnMergedView(context);

        pane.WaitForAssertion(() =>
        {
            Assert.Equal(["JSdotNet/Backlog#10"], RowKeys(pane));
            var failure = pane.Find("[data-testid='pull-requests-unreadable']");
            Assert.Equal("alert", failure.GetAttribute("role"));
            Assert.Contains("Couldn't read octo/broken: Could not resolve", failure.TextContent, StringComparison.Ordinal);
        });

        pane.Find("[data-testid='pull-requests-view-open']").Click();

        pane.WaitForAssertion(() => Assert.Empty(pane.FindAll("[data-testid='pull-requests-unreadable']")));
    }

    /// <summary>Refresh reads the view in front of the reader, and only that one.</summary>
    [Fact]
    public void Refresh_on_the_merged_view_reads_the_merged_pull_requests_again()
    {
        var client = new StubClient().Merged("JSdotNet/Backlog", MergedPull("JSdotNet/Backlog", 10));
        using var context = Context(client);

        var pane = OnMergedView(context);
        pane.WaitForAssertion(() => Assert.Equal(["JSdotNet/Backlog#10"], RowKeys(pane)));

        var openReads = client.ListCalls;
        client.Merged("JSdotNet/Backlog", MergedPull("JSdotNet/Backlog", 10), MergedPull("JSdotNet/Backlog", 11, merged: Noon.AddMinutes(-1)));

        pane.Find("[data-testid='pull-requests-refresh']").Click();

        pane.WaitForAssertion(() =>
        {
            Assert.Equal(["JSdotNet/Backlog#11", "JSdotNet/Backlog#10"], RowKeys(pane));
            Assert.Equal(6, client.MergedListed.Count);
            Assert.Equal(openReads, client.ListCalls);
        });
    }

    /// <summary>A pull request merged from the open view belongs in Recently merged,
    /// so a merged view read before the act is read again on the next switch.</summary>
    [Fact]
    public void A_merge_from_the_open_view_is_found_in_recently_merged()
    {
        var client = new StubClient().Lists("JSdotNet/Backlog", Pull("JSdotNet/Backlog", 1, mergeReady: true));
        using var context = Context(client);

        var pane = OnMergedView(context);
        pane.WaitForAssertion(() => Assert.Empty(RowKeys(pane)));
        pane.Find("[data-testid='pull-requests-view-open']").Click();
        pane.WaitForAssertion(() => Assert.Single(RowKeys(pane)));

        client.Lists("JSdotNet/Backlog");
        client.Merged("JSdotNet/Backlog", MergedPull("JSdotNet/Backlog", 1, merged: Noon));
        pane.Find("[data-testid='pull-request-merge']").Click();
        pane.WaitForAssertion(() => Assert.Empty(RowKeys(pane)));

        pane.Find("[data-testid='pull-requests-view-merged']").Click();

        pane.WaitForAssertion(() => Assert.Equal(["JSdotNet/Backlog#1"], RowKeys(pane)));
    }

    /// <summary>Each open starts on Open, as each starts on Mine: a pane that reopened
    /// on last time's view would read as showing the wrong list.</summary>
    [Fact]
    public async Task Reopening_the_pane_starts_on_open_again()
    {
        var client = new StubClient()
            .Lists("JSdotNet/Backlog", Pull("JSdotNet/Backlog", 1))
            .Merged("JSdotNet/Backlog", MergedPull("JSdotNet/Backlog", 10));
        using var context = Context(client);

        var first = OnMergedView(context);
        first.WaitForAssertion(() => Assert.Equal(["JSdotNet/Backlog#10"], RowKeys(first)));
        await context.DisposeComponentsAsync();

        var reopened = context.Render<PullRequestsPane>();

        reopened.WaitForAssertion(() =>
        {
            Assert.Equal(["JSdotNet/Backlog#1"], RowKeys(reopened));
            Assert.Equal("true", reopened.Find("[data-testid='pull-requests-view-open']").GetAttribute("aria-pressed"));
            Assert.Equal("Open on GitHub", reopened.Find("#pull-requests-title").TextContent.Trim());
        });
    }

    /// <summary>A merged pull request has no act left: no update, no ready, no merge.
    /// The task and the session it came from are still found, the way the open view
    /// finds them.</summary>
    [Fact]
    public void A_merged_row_offers_no_act_and_still_leads_to_its_task_and_session()
    {
        var task = new DeliveryRunReference(DeliveryRunReferenceKind.Task, "Pull requests page", null, null, null, EntryId: Guid.NewGuid());
        var onBranch = Session("on-branch-10", branch: "feature-10", repository: "JSdotNet/Backlog", lastActivity: Noon);
        using var context = Context(
            new StubClient().Merged("JSdotNet/Backlog", MergedPull("JSdotNet/Backlog", 10)),
            sessions: [onBranch]);

        var pane = OnMergedView(context, parameters => parameters
            .Add(p => p.PullRequestTask, (pull, mineOnly) => pull.Number == 10 ? task : null));

        pane.WaitForAssertion(() =>
        {
            var row = Assert.Single(pane.FindAll("[data-testid='pull-request-row']"));
            Assert.Empty(row.QuerySelectorAll("button"));
            Assert.Equal("Pull requests page", row.QuerySelector("[data-testid='pull-request-task']")!.TextContent.Trim());
            Assert.StartsWith("Title of on-branch-10", row.QuerySelector("[data-testid='pull-request-session']")!.TextContent.Trim(), StringComparison.Ordinal);
        });
    }

    /// <summary>The first read of the merged view shows the placeholder rows, wearing
    /// the class the stylesheet lays them out with.</summary>
    [Fact]
    public void The_merged_views_first_read_shows_the_placeholder_rows()
    {
        var gate = new TaskCompletionSource();
        var client = new StubClient { MergedGate = gate }.Merged("JSdotNet/Backlog", MergedPull("JSdotNet/Backlog", 10));
        using var context = Context(client);

        var pane = OnMergedView(context);

        pane.WaitForAssertion(() =>
        {
            var loading = pane.Find("[data-testid='pull-requests-loading']");
            Assert.Equal("pull-requests-panel__loading", loading.GetAttribute("class"));
        });

        gate.SetResult();

        pane.WaitForAssertion(() =>
        {
            Assert.Empty(pane.FindAll("[data-testid='pull-requests-loading']"));
            Assert.Equal(["JSdotNet/Backlog#10"], RowKeys(pane));
        });
    }

    /// <summary>A merged read sent while a merge act is still out answers with the
    /// world before the merge. When the act lands it supersedes that read, so the stale
    /// answer arriving afterwards is dropped and the view shows the merge.</summary>
    [Fact]
    public void A_merged_read_overtaken_by_a_merge_act_does_not_bring_back_the_old_list()
    {
        var actGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var readGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = new StubClient().Lists("JSdotNet/Backlog", Pull("JSdotNet/Backlog", 1, mergeReady: true));
        using var context = Context(client);

        var pane = context.Render<PullRequestsPane>();
        pane.WaitForAssertion(() => Assert.Single(RowKeys(pane)));

        client.ActGate = actGate;
        client.MergedGate = readGate;
        pane.Find("[data-testid='pull-request-merge']").Click();

        // The merged view is opened while the merge is still out: its read is sent
        // now, and answers with nothing merged yet.
        pane.Find("[data-testid='pull-requests-view-merged']").Click();
        pane.WaitForAssertion(() => Assert.NotEmpty(client.MergedListed));

        client.Lists("JSdotNet/Backlog");
        client.Merged("JSdotNet/Backlog", MergedPull("JSdotNet/Backlog", 1, merged: Noon));
        actGate.SetResult();

        // Polled rather than awaited on a render: the act's handler is now waiting on
        // the fresh merged read, which is held at the gate, so nothing renders yet.
        Assert.True(SpinWait.SpinUntil(() => { lock (client.MergedListed) return client.MergedListed.Count > 3; }, TimeSpan.FromSeconds(5)));

        readGate.SetResult();

        pane.WaitForAssertion(() =>
        {
            Assert.Empty(pane.FindAll("[data-testid='pull-requests-loading']"));
            Assert.Equal(["JSdotNet/Backlog#1"], RowKeys(pane));
        });
    }

    /// <summary>Back to Open and to Recently merged again while the first merged read
    /// is still out waits on that read rather than sending a second.</summary>
    [Fact]
    public void Switching_back_during_the_first_merged_read_reuses_it()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = new StubClient { MergedGate = gate }.Merged("JSdotNet/Backlog", MergedPull("JSdotNet/Backlog", 10));
        using var context = Context(client);

        var pane = OnMergedView(context);
        pane.WaitForAssertion(() => Assert.Equal(3, client.MergedListed.Count));

        pane.Find("[data-testid='pull-requests-view-open']").Click();
        pane.Find("[data-testid='pull-requests-view-merged']").Click();
        gate.SetResult();

        pane.WaitForAssertion(() => Assert.Equal(["JSdotNet/Backlog#10"], RowKeys(pane)));
        Assert.Equal(3, client.MergedListed.Count);
    }

    /// <summary>A repository whose fortnight the adapter could not read to the end is
    /// named, so the view does not pass its most recent merges off as all of them.</summary>
    [Fact]
    public void A_repository_whose_window_was_not_read_to_the_end_is_named()
    {
        var client = new StubClient()
            .Merged("JSdotNet/Backlog", MergedPull("JSdotNet/Backlog", 10))
            .Truncates("JSdotNet/Backlog");
        using var context = Context(client);

        var pane = OnMergedView(context);

        pane.WaitForAssertion(() =>
        {
            var note = pane.Find("[data-testid='pull-requests-merged-truncated']");
            Assert.Equal("status", note.GetAttribute("role"));
            Assert.Contains("JSdotNet/Backlog", note.TextContent, StringComparison.Ordinal);
            Assert.Contains("500", note.TextContent, StringComparison.Ordinal);
        });

        pane.Find("[data-testid='pull-requests-view-open']").Click();

        pane.WaitForAssertion(() => Assert.Empty(pane.FindAll("[data-testid='pull-requests-merged-truncated']")));
    }

    [Fact]
    public void Nothing_merged_of_the_readers_own_says_how_many_others_opened()
    {
        var client = new StubClient().Merged(
            "JSdotNet/Backlog",
            MergedPull("JSdotNet/Backlog", 10, mine: false),
            MergedPull("JSdotNet/Backlog", 11, mine: false));
        using var context = Context(client);

        var pane = OnMergedView(context);

        pane.WaitForAssertion(() =>
        {
            Assert.Contains("None of the recently merged pull requests is yours.", pane.Markup, StringComparison.Ordinal);
            Assert.Contains("Choose Everyone's to see the 2 that others opened.", pane.Markup, StringComparison.Ordinal);
        });
    }

    // --- Stacks ---------------------------------------------------------------

    /// <summary>A stack reads as one tree, bottom first and each pull request one
    /// level under the one it waits on, whatever order GitHub updated them in — and
    /// it sits in the list where its latest update puts it, with no heading of its
    /// own.</summary>
    [Fact]
    public void A_stack_is_one_indented_tree_where_its_latest_update_sits()
    {
        var client = new StubClient().Lists(
            "JSdotNet/Backlog",
            Pull("JSdotNet/Backlog", 3, updated: Noon.AddMinutes(-1)),
            Pull("JSdotNet/Backlog", 1, updated: Noon.AddHours(-1)),
            Pull("JSdotNet/Backlog", 12, baseRef: "feature-11", updated: Noon.AddMinutes(-5)),
            Pull("JSdotNet/Backlog", 11, baseRef: "feature-10", updated: Noon.AddMinutes(-30)),
            Pull("JSdotNet/Backlog", 10, updated: Noon.AddHours(-3)),
            Pull("JSdotNet/Backlog", 2, updated: Noon.AddHours(-2)));
        using var context = Context(client);

        var pane = context.Render<PullRequestsPane>();

        pane.WaitForAssertion(() =>
        {
            Assert.Equal(
                ["JSdotNet/Backlog#3", "JSdotNet/Backlog#10", "JSdotNet/Backlog#11", "JSdotNet/Backlog#12", "JSdotNet/Backlog#1", "JSdotNet/Backlog#2"],
                RowKeys(pane));

            Assert.Equal(
                [null, "0", "1", "2", null, null],
                pane.FindAll("[data-testid='pull-request-row']").Select(row => row.GetAttribute("data-stack-depth")));

            Assert.Empty(pane.FindAll(".data-table__group-name"));
        });
    }

    /// <summary>Only the bottom of a stack is offered a merge: merging one above it
    /// would land it on its parent's branch, not where the stack is going.</summary>
    [Fact]
    public void A_pull_request_above_the_bottom_waits_on_its_parent_and_offers_no_merge()
    {
        var client = new StubClient().Lists(
            "JSdotNet/Backlog",
            Pull("JSdotNet/Backlog", 10, mergeReady: true),
            Pull("JSdotNet/Backlog", 11, baseRef: "feature-10", mergeReady: true),
            Pull("JSdotNet/Backlog", 12, baseRef: "feature-11", autoMerge: true));
        using var context = Context(client);

        var pane = context.Render<PullRequestsPane>();

        pane.WaitForAssertion(() =>
        {
            var bottom = Row(pane, "JSdotNet/Backlog#10");
            Assert.Null(bottom.QuerySelector("[data-testid='pull-request-waits-on']"));
            Assert.Equal("Merge now", bottom.QuerySelector("[data-testid='pull-request-merge']")!.TextContent.Trim());

            var middle = Row(pane, "JSdotNet/Backlog#11");
            Assert.Equal("Waits on #10", WaitsOn(middle));
            Assert.Null(middle.QuerySelector("[data-testid='pull-request-merge']"));

            // A request GitHub already holds is still the reader's to withdraw.
            var top = Row(pane, "JSdotNet/Backlog#12");
            Assert.Equal("Waits on #11", WaitsOn(top));
            Assert.Equal("Cancel auto-merge", top.QuerySelector("[data-testid='pull-request-merge']")!.TextContent.Trim());
        });
    }

    /// <summary>The stack is read from every author's pull requests, so Mine hiding
    /// somebody else's bottom does not make the reader's own look mergeable.</summary>
    [Fact]
    public void Mine_keeps_the_stack_when_its_bottom_is_somebody_elses()
    {
        var client = new StubClient().Lists(
            "JSdotNet/Backlog",
            Pull("JSdotNet/Backlog", 10, mine: false),
            Pull("JSdotNet/Backlog", 11, baseRef: "feature-10", mergeReady: true));
        using var context = Context(client);

        var pane = context.Render<PullRequestsPane>();

        pane.WaitForAssertion(() =>
        {
            Assert.Equal(["JSdotNet/Backlog#11"], RowKeys(pane));
            var row = Row(pane, "JSdotNet/Backlog#11");
            Assert.Equal("1", row.GetAttribute("data-stack-depth"));
            Assert.Equal("Waits on #10", WaitsOn(row));
            Assert.Null(row.QuerySelector("[data-testid='pull-request-merge']"));
        });
    }

    /// <summary>A branch name means something only in its own repository.</summary>
    [Fact]
    public void A_base_that_is_another_repositorys_head_is_no_stack()
    {
        var client = new StubClient()
            .Lists("JSdotNet/Backlog", Pull("JSdotNet/Backlog", 10))
            .Lists("JSdotNet/Archify", Pull("JSdotNet/Archify", 11, baseRef: "feature-10"));
        using var context = Context(client);

        var pane = context.Render<PullRequestsPane>();

        pane.WaitForAssertion(() =>
        {
            Assert.Equal(2, RowKeys(pane).Count);
            Assert.Empty(pane.FindAll(".data-table__group-name"));
            Assert.Empty(pane.FindAll("[data-stack-depth]"));
            Assert.Empty(pane.FindAll("[data-testid='pull-request-waits-on']"));
        });
    }

    /// <summary>Two pull requests targeting each other's branches have no bottom to
    /// merge first, so neither is drawn as waiting on the other.</summary>
    [Fact]
    public void Pull_requests_targeting_each_others_branches_are_no_stack()
    {
        var client = new StubClient().Lists(
            "JSdotNet/Backlog",
            Pull("JSdotNet/Backlog", 10, baseRef: "feature-11", mergeReady: true),
            Pull("JSdotNet/Backlog", 11, baseRef: "feature-10", mergeReady: true));
        using var context = Context(client);

        var pane = context.Render<PullRequestsPane>();

        pane.WaitForAssertion(() =>
        {
            Assert.Equal(2, RowKeys(pane).Count);
            Assert.Empty(pane.FindAll("[data-testid='pull-request-waits-on']"));
            Assert.Equal(2, pane.FindAll("[data-testid='pull-request-merge']").Count);
        });
    }

    // --- Helpers --------------------------------------------------------------

    /// <summary>GitHub's "8/9", what holds the rest, and what the reviewers decided —
    /// each a word in a badge, never a colour alone.</summary>
    [Fact]
    public void Check_progress_and_reviews_are_badges_in_words()
    {
        var client = new StubClient().Lists(
            "JSdotNet/Backlog",
            Pull("JSdotNet/Backlog", 1) with
            {
                Checks = GitHubCheckState.Failing,
                CheckCounts = new GitHubCheckCounts(Passed: 7, Failed: 1, Pending: 2),
                Reviews = new GitHubReviewSummary(GitHubReviewDecision.ReviewRequired, Approvals: 1, ChangesRequested: 0)
            },
            Pull("JSdotNet/Backlog", 2) with
            {
                CheckCounts = new GitHubCheckCounts(Passed: 9, Failed: 0, Pending: 0),
                Reviews = new GitHubReviewSummary(GitHubReviewDecision.Approved, Approvals: 2, ChangesRequested: 0)
            },
            Pull("JSdotNet/Backlog", 3) with
            {
                Reviews = new GitHubReviewSummary(GitHubReviewDecision.ChangesRequested, Approvals: 0, ChangesRequested: 1)
            });
        using var context = Context(client);

        var pane = context.Render<PullRequestsPane>();

        pane.WaitForAssertion(() =>
        {
            var first = Row(pane, "JSdotNet/Backlog#1");
            Assert.Equal("7/10 checks", Text(first, "pull-request-checks"));
            Assert.Equal("1 failing", Text(first, "pull-request-checks-failing"));
            Assert.Equal("2 pending", Text(first, "pull-request-checks-pending"));
            Assert.Equal("1 approval", Text(first, "pull-request-approvals"));
            Assert.Equal("Review required", Text(first, "pull-request-review"));

            var second = Row(pane, "JSdotNet/Backlog#2");
            Assert.Equal("9/9 checks", Text(second, "pull-request-checks"));
            Assert.Null(second.QuerySelector("[data-testid='pull-request-checks-failing']"));
            Assert.Null(second.QuerySelector("[data-testid='pull-request-checks-pending']"));
            Assert.Equal("2 approvals", Text(second, "pull-request-approvals"));
            Assert.Equal("Approved", Text(second, "pull-request-review"));

            // No counts read: the roll-up's word, as before; no approvals, no badge.
            var third = Row(pane, "JSdotNet/Backlog#3");
            Assert.Equal("Checks passing", Text(third, "pull-request-checks"));
            Assert.Null(third.QuerySelector("[data-testid='pull-request-approvals']"));
            Assert.Equal("Changes requested", Text(third, "pull-request-review"));
        });
    }

    /// <summary>A repository with a label filter lists only the pull requests carrying
    /// one of its labels, in either case; the others are untouched. The badge counts
    /// what the filter left against what was read, the subtitle names the filter, and
    /// a filter changed in Settings narrows the rows already read.</summary>
    [Fact]
    public void A_label_filter_narrows_its_repository_and_says_so()
    {
        var client = new StubClient()
            .Lists(
                "JSdotNet/Backlog",
                Pull("JSdotNet/Backlog", 1) with { Labels = ["Frontend"] },
                Pull("JSdotNet/Backlog", 2) with { Labels = ["backend"] })
            .Lists("JSdotNet/Archify", Pull("JSdotNet/Archify", 3));
        using var context = Context(client);
        Assert.Null(SettingsOf(context).SetPullRequestLabels("backlog", ["frontend", "design"]));

        var pane = context.Render<PullRequestsPane>();

        pane.WaitForAssertion(() =>
        {
            Assert.Equal(["JSdotNet/Backlog#1", "JSdotNet/Archify#3"], RowKeys(pane));
            Assert.Equal("2 of 3 pull requests", pane.Find("[data-testid='pull-requests-count']").TextContent.Trim());
            Assert.Contains("Label filter on backlog: frontend or design.", pane.Find("[data-testid='pull-requests-subtitle']").TextContent, StringComparison.Ordinal);
        });

        Assert.Null(SettingsOf(context).SetPullRequestLabels("backlog", []));

        pane.WaitForAssertion(() =>
        {
            Assert.Equal(["JSdotNet/Backlog#1", "JSdotNet/Backlog#2", "JSdotNet/Archify#3"], RowKeys(pane));
            Assert.DoesNotContain("Label filter", pane.Find("[data-testid='pull-requests-subtitle']").TextContent, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void A_label_filter_that_leaves_nothing_names_itself_in_the_empty_state()
    {
        var client = new StubClient().Lists("JSdotNet/Backlog", Pull("JSdotNet/Backlog", 2) with { Labels = ["backend"] });
        using var context = Context(client, repositories: "JSdotNet/Backlog");
        Assert.Null(SettingsOf(context).SetPullRequestLabels("backlog", ["frontend"]));

        var pane = context.Render<PullRequestsPane>();

        pane.WaitForAssertion(() =>
        {
            Assert.Empty(pane.FindAll("[data-testid='pull-request-row']"));
            Assert.Contains("None of the open pull requests carries a label the filter asks for.", pane.Markup, StringComparison.Ordinal);
            Assert.Contains("Label filter on backlog: frontend.", pane.Markup, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void The_label_filter_narrows_the_merged_view_too()
    {
        var client = new StubClient().Merged(
            "JSdotNet/Backlog",
            MergedPull("JSdotNet/Backlog", 10) with { Labels = ["frontend"] },
            MergedPull("JSdotNet/Backlog", 11));
        using var context = Context(client);
        Assert.Null(SettingsOf(context).SetPullRequestLabels("backlog", ["FRONTEND"]));

        var pane = OnMergedView(context);

        pane.WaitForAssertion(() =>
        {
            Assert.Equal(["JSdotNet/Backlog#10"], RowKeys(pane));
            Assert.Equal("1 of 2 merged pull requests", pane.Find("[data-testid='pull-requests-count']").TextContent.Trim());
        });
    }

    /// <summary>The pin is a toggle whose name stays "Pin #n" while aria-pressed says
    /// whether it is pinned, and the store keeps it.</summary>
    [Fact]
    public void A_row_is_pinned_and_unpinned_with_its_toggle()
    {
        var pins = Pins();
        using var context = Context(new StubClient().Lists("JSdotNet/Backlog", Pull("JSdotNet/Backlog", 1)), pins: pins);

        var pane = context.Render<PullRequestsPane>();
        pane.WaitForAssertion(() =>
        {
            var pin = Row(pane, "JSdotNet/Backlog#1").QuerySelector("[data-testid='pull-request-pin']")!;
            Assert.Equal("Pin #1", pin.GetAttribute("aria-label"));
            Assert.Equal("false", pin.GetAttribute("aria-pressed"));
        });

        pane.Find("[data-testid='pull-request-pin']").Click();

        pane.WaitForAssertion(() =>
        {
            var pin = pane.Find("[data-testid='pull-request-pin']");
            Assert.Equal("Pin #1", pin.GetAttribute("aria-label"));
            Assert.Equal("true", pin.GetAttribute("aria-pressed"));
            Assert.Equal("true", Row(pane, "JSdotNet/Backlog#1").GetAttribute("data-pinned"));
        });
        Assert.True(pins.IsPinned("JSdotNet/Backlog", 1));

        pane.Find("[data-testid='pull-request-pin']").Click();

        pane.WaitForAssertion(() =>
        {
            var pin = pane.Find("[data-testid='pull-request-pin']");
            Assert.Equal("Pin #1", pin.GetAttribute("aria-label"));
            Assert.Equal("false", pin.GetAttribute("aria-pressed"));
        });
        Assert.False(pins.IsPinned("JSdotNet/Backlog", 1));
    }

    /// <summary>A pin ignores every filter — somebody else's, outside the header scope,
    /// without the repository's label — and sorts first.</summary>
    [Fact]
    public void A_pinned_row_ignores_every_filter_and_sorts_first()
    {
        var client = new StubClient()
            .Lists(
                "JSdotNet/Backlog",
                Pull("JSdotNet/Backlog", 1, mine: true, updated: Noon) with { Labels = ["frontend"] },
                Pull("JSdotNet/Backlog", 2, mine: false, updated: Noon.AddHours(-5)) with { Labels = ["backend"] })
            .Lists("JSdotNet/Archify", Pull("JSdotNet/Archify", 3, updated: Noon.AddMinutes(-1)));
        client.Finds(Pull("JSdotNet/Backlog", 2, mine: false, updated: Noon.AddHours(-5)) with { Labels = ["backend"] });
        using var context = Context(client, pins: Pins(("JSdotNet/Backlog", 2)));
        Assert.Null(SettingsOf(context).SetPullRequestLabels("backlog", ["frontend"]));

        var pane = context.Render<PullRequestsPane>(parameters => parameters.Add(p => p.RepositoryScope, ["archify"]));

        pane.WaitForAssertion(() =>
        {
            Assert.Equal(["JSdotNet/Backlog#2", "JSdotNet/Archify#3"], RowKeys(pane));
            Assert.Equal("true", Row(pane, "JSdotNet/Backlog#2").GetAttribute("data-pinned"));
        });
    }

    /// <summary>A pinned pull request that merged stays on the open view, saying so,
    /// with no act — until it is unpinned, when it leaves.</summary>
    [Fact]
    public void A_pinned_merged_pull_request_stays_until_it_is_unpinned()
    {
        var client = new StubClient().Lists("JSdotNet/Backlog", Pull("JSdotNet/Backlog", 1));
        client.Finds(Pull("JSdotNet/Backlog", 5, behind: true, mergeReady: true) with { IsMerged = true, MergedAt = Noon.AddHours(-1) });
        var pins = Pins(("JSdotNet/Backlog", 5));
        using var context = Context(client, pins: pins);

        var pane = context.Render<PullRequestsPane>();

        pane.WaitForAssertion(() =>
        {
            Assert.Equal(["JSdotNet/Backlog#5", "JSdotNet/Backlog#1"], RowKeys(pane));
            var merged = Row(pane, "JSdotNet/Backlog#5");
            Assert.Equal("Merged", Text(merged, "pull-request-closed"));
            Assert.Null(merged.QuerySelector("[data-testid='pull-request-behind']"));
            Assert.Null(merged.QuerySelector("[data-testid='pull-request-merge']"));
            Assert.Null(merged.QuerySelector("[data-testid='pull-request-update-branch']"));
            Assert.Equal(["pull-request-pin"], merged.QuerySelectorAll("button").Select(button => button.GetAttribute("data-testid")));
        });
        Assert.Contains("JSdotNet/Backlog 5", client.PinnedListed);

        Row(pane, "JSdotNet/Backlog#5").QuerySelector("[data-testid='pull-request-pin']")!.Click();

        pane.WaitForAssertion(() => Assert.Equal(["JSdotNet/Backlog#1"], RowKeys(pane)));
        Assert.False(pins.IsPinned("JSdotNet/Backlog", 5));
    }

    /// <summary>A pull request pinned from the merged view is read by number, so the
    /// open view has it next time it is shown.</summary>
    [Fact]
    public void A_pull_request_pinned_from_the_merged_view_is_read_onto_the_open_view()
    {
        var client = new StubClient()
            .Lists("JSdotNet/Backlog", Pull("JSdotNet/Backlog", 1))
            .Merged("JSdotNet/Backlog", MergedPull("JSdotNet/Backlog", 10));
        client.Finds(Pull("JSdotNet/Backlog", 10) with { IsMerged = true, MergedAt = Noon.AddHours(-10) });
        var pins = Pins();
        using var context = Context(client, pins: pins);

        var pane = OnMergedView(context);
        pane.WaitForAssertion(() => Assert.Equal(["JSdotNet/Backlog#10"], RowKeys(pane)));
        Assert.Empty(client.PinnedListed);

        pane.Find("[data-testid='pull-request-pin']").Click();

        pane.WaitForAssertion(() => Assert.Contains("JSdotNet/Backlog 10", client.PinnedListed));
        pane.Find("[data-testid='pull-requests-view-open']").Click();

        pane.WaitForAssertion(() =>
        {
            Assert.Equal(["JSdotNet/Backlog#10", "JSdotNet/Backlog#1"], RowKeys(pane));
            Assert.Equal("Merged", Text(Row(pane, "JSdotNet/Backlog#10"), "pull-request-closed"));
        });
    }

    /// <summary>Mine is the reader's own pull requests and those related to one of
    /// their tasks, whoever opened them; the Task column names the related task on
    /// every row, Everyone's included.</summary>
    [Fact]
    public void Mine_includes_pull_requests_related_to_my_tasks_and_every_row_names_its_task()
    {
        var mineTask = new DeliveryRunReference(DeliveryRunReferenceKind.Task, "My task", null, null, null, EntryId: Guid.NewGuid());
        var otherTask = new DeliveryRunReference(DeliveryRunReferenceKind.Task, "Their task", null, null, null, EntryId: Guid.NewGuid());
        var client = new StubClient().Lists(
            "JSdotNet/Backlog",
            Pull("JSdotNet/Backlog", 1, mine: true),
            Pull("JSdotNet/Backlog", 2, mine: false),
            Pull("JSdotNet/Backlog", 3, mine: false));
        using var context = Context(client);

        var pane = context.Render<PullRequestsPane>(parameters => parameters
            .Add(p => p.PullRequestTask, (pull, mineOnly) => pull.Number switch
            {
                2 => mineTask,
                3 when !mineOnly => otherTask,
                _ => null
            }));

        pane.WaitForAssertion(() =>
        {
            Assert.Equal(["JSdotNet/Backlog#1", "JSdotNet/Backlog#2"], RowKeys(pane));
            Assert.Equal("My task", Text(Row(pane, "JSdotNet/Backlog#2"), "pull-request-task"));
        });

        pane.Find("[data-testid='pull-requests-filter-everyone']").Click();

        pane.WaitForAssertion(() =>
        {
            Assert.Equal(["JSdotNet/Backlog#1", "JSdotNet/Backlog#2", "JSdotNet/Backlog#3"], RowKeys(pane));
            Assert.Equal("Their task", Text(Row(pane, "JSdotNet/Backlog#3"), "pull-request-task"));
        });
    }

    /// <summary>Two pins made while the first one's read is still out send two reads.
    /// The newer, which has both pins, lands first; the older, which has only the first,
    /// lands after it and must not become the last word, or the second pin never reaches
    /// the open view.</summary>
    [Fact]
    public void An_older_pinned_read_landing_last_does_not_overwrite_a_newer_one()
    {
        var client = new StubClient()
            .Lists("JSdotNet/Backlog", Pull("JSdotNet/Backlog", 1))
            .Merged("JSdotNet/Backlog", MergedPull("JSdotNet/Backlog", 10), MergedPull("JSdotNet/Backlog", 11));
        client.Finds(
            Pull("JSdotNet/Backlog", 10) with { IsMerged = true, MergedAt = Noon.AddHours(-10) },
            Pull("JSdotNet/Backlog", 11) with { IsMerged = true, MergedAt = Noon.AddHours(-11) });
        var older = new TaskCompletionSource();
        var newer = new TaskCompletionSource();
        client.PinnedGate = numbers => numbers.Count == 1 ? older.Task : newer.Task;
        using var context = Context(client, pins: Pins());

        var pane = OnMergedView(context);
        pane.WaitForAssertion(() => Assert.Equal(["JSdotNet/Backlog#10", "JSdotNet/Backlog#11"], RowKeys(pane)));

        Row(pane, "JSdotNet/Backlog#10").QuerySelector("[data-testid='pull-request-pin']")!.Click();
        pane.WaitForAssertion(() => Assert.Equal(["JSdotNet/Backlog 10"], client.PinnedListed));
        Row(pane, "JSdotNet/Backlog#11").QuerySelector("[data-testid='pull-request-pin']")!.Click();
        pane.WaitForAssertion(() => Assert.Equal(["JSdotNet/Backlog 10", "JSdotNet/Backlog 10,11"], client.PinnedListed));

        pane.InvokeAsync(newer.SetResult);
        pane.InvokeAsync(older.SetResult);
        pane.Find("[data-testid='pull-requests-view-open']").Click();

        pane.WaitForAssertion(() =>
            Assert.Equal(["JSdotNet/Backlog#10", "JSdotNet/Backlog#11", "JSdotNet/Backlog#1"], RowKeys(pane)));
    }

    /// <summary>A label filter set in Settings after the pane has read narrows the
    /// rows already in front of the reader, with no read of its own.</summary>
    [Fact]
    public void A_label_filter_set_after_the_pane_has_read_narrows_it_without_a_read()
    {
        var client = new StubClient().Lists(
            "JSdotNet/Backlog",
            Pull("JSdotNet/Backlog", 1) with { Labels = ["frontend"] },
            Pull("JSdotNet/Backlog", 2) with { Labels = ["backend"] });
        using var context = Context(client, repositories: "JSdotNet/Backlog");

        var pane = context.Render<PullRequestsPane>();
        pane.WaitForAssertion(() => Assert.Equal(["JSdotNet/Backlog#1", "JSdotNet/Backlog#2"], RowKeys(pane)));
        var reads = client.ListCalls;

        Assert.Null(SettingsOf(context).SetPullRequestLabels("backlog", ["Frontend"]));

        pane.WaitForAssertion(() =>
        {
            Assert.Equal(["JSdotNet/Backlog#1"], RowKeys(pane));
            Assert.Equal("1 of 2 pull requests", pane.Find("[data-testid='pull-requests-count']").TextContent.Trim());
            Assert.Contains("Label filter on backlog: Frontend.", pane.Find("[data-testid='pull-requests-subtitle']").TextContent, StringComparison.Ordinal);
        });
        Assert.Equal(reads, client.ListCalls);
    }

    private static string? Text(AngleSharp.Dom.IElement row, string testId) =>
        row.QuerySelector($"[data-testid='{testId}']")?.TextContent.Trim();

    /// <summary>What the tree mark in front of a stacked row says it waits on, as a
    /// screen reader announces it.</summary>
    private static string? WaitsOn(AngleSharp.Dom.IElement row) =>
        row.QuerySelector("[data-testid='pull-request-waits-on']")?.GetAttribute("aria-label");

    private static AngleSharp.Dom.IElement Row(IRenderedComponent<PullRequestsPane> pane, string key) =>
        pane.Find($"[data-testid='pull-request-row'][data-pull-request='{key}']");

    /// <summary>An element's text with its markup's line breaks and indentation
    /// collapsed to single spaces, as a reader sees it.</summary>
    private static string NormalizedText(AngleSharp.Dom.IElement element) =>
        System.Text.RegularExpressions.Regex.Replace(element.TextContent, @"\s+", " ").Trim();

    /// <summary>The pane, opened and switched to Recently merged once its first read
    /// is in.</summary>
    private static IRenderedComponent<PullRequestsPane> OnMergedView(
        BunitContext context,
        Action<ComponentParameterCollectionBuilder<PullRequestsPane>>? parameters = null)
    {
        var pane = context.Render<PullRequestsPane>(builder => parameters?.Invoke(builder));
        pane.WaitForAssertion(() => Assert.Empty(pane.FindAll("[data-testid='pull-requests-loading']")));
        pane.Find("[data-testid='pull-requests-view-merged']").Click();
        return pane;
    }

    private static List<string?> RowKeys(IRenderedComponent<PullRequestsPane> pane) =>
        [.. pane.FindAll("[data-testid='pull-request-row']").Select(row => row.GetAttribute("data-pull-request"))];

    private static IReadOnlyList<ToastMessage> Toasts(BunitContext context) =>
        context.Services.GetRequiredService<IToastChannel>().Visible;

    private BunitContext Context(
        StubClient client,
        string repositories = "JSdotNet/Backlog\nJSdotNet/Archify\nocto/broken",
        IReadOnlyList<AgentSession>? sessions = null,
        bool toasts = true,
        PullRequestPinsStore? pins = null)
    {
        var settings = new GitHubSettingsStore(Path.Combine(_root, Guid.NewGuid().ToString("n"), "github.json"));
        var (parsed, errors) = GitHubSettings.ParseText(repositories);
        Assert.Empty(errors);
        Assert.Null(settings.SetRepositories(parsed));

        // A repository the stub was told nothing about lists nothing, so the default
        // three-repository workspace reads as Backlog and Archify plus an empty one.
        foreach (var repository in parsed.Where(repository => !client.Knows(repository.FullName)))
        {
            client.Lists(repository.FullName);
        }

        var context = new BunitContext();
        // One clock for the adapter's window and the pane's "2h ago", stopped at noon.
        var clock = new FakeTimeProvider(Noon);
        context.Services.AddSingleton<TimeProvider>(clock);
        context.Services.AddSingleton(new GitHubIntegration(settings, client, new Backlog.Tests.StubProbe(new GitHubConnection(true, "Connected.")), time: clock));
        context.Services.AddSingleton<IAgentSessionSource>(new StubSessionSource(sessions ?? []));
        if (toasts) TasksTestHost.AddToastChannel(context.Services);
        if (pins is not null) context.Services.AddSingleton(pins);

        return context;
    }

    /// <summary>A pins store of the test's own, in its temp folder.</summary>
    private PullRequestPinsStore Pins(params (string Repository, int Number)[] pinned)
    {
        var store = new PullRequestPinsStore(Path.Combine(_root, Guid.NewGuid().ToString("n"), "pull-request-pins.json"));
        foreach (var (repository, number) in pinned) store.Pin(repository, number);
        return store;
    }

    private static GitHubSettingsStore SettingsOf(BunitContext context) =>
        context.Services.GetRequiredService<GitHubIntegration>().Settings;

    private static GitHubOpenPullRequest Pull(
        string repository,
        int number,
        bool mine = true,
        bool draft = false,
        bool mergeReady = false,
        bool autoMerge = false,
        bool behind = false,
        bool conflicts = false,
        DateTimeOffset? updated = null,
        string baseRef = "main") =>
        new(
            number,
            $"https://github.com/{repository}/pull/{number}",
            $"Pull request {number}",
            repository,
            $"PR_{number}",
            IsDraft: draft,
            HeadRefName: $"feature-{number}",
            HeadSha: $"sha-{number}",
            BaseRefName: baseRef,
            AuthorLogin: mine ? "JSdotNet" : "someone-else",
            ViewerDidAuthor: mine,
            Checks: GitHubCheckState.Passing,
            AutoMergeEnabled: autoMerge,
            MergeReady: mergeReady,
            IsBehind: behind,
            HasConflicts: conflicts,
            MergeStateStatus: behind ? "BEHIND" : mergeReady ? "CLEAN" : "BLOCKED",
            PreferredMergeMethod: GitHubMergeMethod.Squash,
            UpdatedAt: updated ?? Noon.AddMinutes(-number));

    private static GitHubMergedPullRequest MergedPull(
        string repository,
        int number,
        bool mine = true,
        DateTimeOffset? merged = null) =>
        new(
            number,
            $"https://github.com/{repository}/pull/{number}",
            $"Pull request {number}",
            repository,
            HeadRefName: $"feature-{number}",
            BaseRefName: "main",
            AuthorLogin: mine ? "JSdotNet" : "someone-else",
            ViewerDidAuthor: mine,
            MergedAt: merged ?? Noon.AddHours(-number),
            MergedByLogin: $"merger-{number}");

    private static AgentSession Session(
        string id,
        string branch,
        DateTimeOffset lastActivity,
        string? repository = null,
        string? resolved = null) =>
        new(
            Id: id,
            Kind: AgentSessionKind.Claude,
            EnvironmentId: "tower",
            Environment: "DEV-TOWER",
            Title: $"Title of {id}",
            WorkingFolder: @"D:\Repos\Backlog",
            Repository: repository,
            Branch: branch,
            StartedAt: lastActivity.AddHours(-1),
            LastActivityAt: lastActivity,
            State: AgentSessionState.Finished,
            TurnCount: null,
            Origin: AgentSessionOrigin.Local)
        {
            ResolvedRepository = resolved
        };

    /// <summary>
    /// Answers each repository's list from what the test said, records every act, and
    /// can be told to refuse the acts. Lists are replaceable between reads, which is
    /// how a test says what the re-read after an act finds.
    /// </summary>
    private sealed class StubClient : IGitHubClient
    {
        private readonly Dictionary<string, Func<IReadOnlyList<GitHubOpenPullRequest>>> _lists = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Func<IReadOnlyList<GitHubMergedPullRequest>>> _merged = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _truncated = new(StringComparer.OrdinalIgnoreCase);
        private string? _refusal;
        private int _firstReadSize;

        public List<string> Acts { get; } = [];

        public List<string> Listed { get; } = [];

        public int ListCalls => Listed.Count;

        /// <summary>Every merged read, by repository. A repository the test said
        /// nothing merged about has merged nothing.</summary>
        public List<string> MergedListed { get; } = [];

        /// <summary>The repositories read after the opening read of every repository —
        /// what an act's re-read asked for.</summary>
        public IReadOnlyList<string> ListedAfterFirstRead => [.. Listed.Skip(_firstReadSize)];

        public bool Knows(string fullName) => _lists.ContainsKey(fullName);

        public StubClient Lists(string fullName, params GitHubOpenPullRequest[] pulls)
        {
            _lists[fullName] = () => pulls;
            return this;
        }

        private readonly Dictionary<string, List<GitHubOpenPullRequest>> _byNumber = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Every pinned read, by repository and the numbers it asked for.</summary>
        public List<string> PinnedListed { get; } = [];

        /// <summary>What a read by number finds — merged and closed ones included, as
        /// the pinned read asks for any state.</summary>
        public StubClient Finds(params GitHubOpenPullRequest[] pulls)
        {
            foreach (var pull in pulls)
            {
                if (!_byNumber.TryGetValue(pull.RepositoryFullName, out var list)) _byNumber[pull.RepositoryFullName] = list = [];
                list.RemoveAll(known => known.Number == pull.Number);
                list.Add(pull);
            }

            return this;
        }

        /// <summary>When set, each pinned read waits on what it returns for the numbers
        /// asked — how a test lands two reads out of order.</summary>
        public Func<IReadOnlyCollection<int>, Task>? PinnedGate { get; set; }

        public async Task<IReadOnlyList<GitHubOpenPullRequest>> ListPullRequestsAsync(GitHubRepositoryRef repository, IReadOnlyCollection<int> numbers, CancellationToken cancellationToken = default)
        {
            lock (PinnedListed) PinnedListed.Add($"{repository.FullName} {string.Join(",", numbers.Order())}");

            var found = _byNumber.TryGetValue(repository.FullName, out var list) ? list : [];
            IReadOnlyList<GitHubOpenPullRequest> answer = [.. found.Where(pull => numbers.Contains(pull.Number))];

            if (PinnedGate is { } gate) await gate(numbers);

            return answer;
        }

        public StubClient Refuses(string fullName, string message)
        {
            _lists[fullName] = () => throw new GitHubException(message);
            return this;
        }

        public StubClient Merged(string fullName, params GitHubMergedPullRequest[] pulls)
        {
            _merged[fullName] = () => pulls;
            return this;
        }

        public StubClient RefusesMerged(string fullName, string message)
        {
            _merged[fullName] = () => throw new GitHubException(message);
            return this;
        }

        public StubClient Truncates(string fullName)
        {
            _truncated.Add(fullName);
            return this;
        }

        /// <summary>When set, every merged read waits on it — how a test sees the
        /// pane while the read is still out.</summary>
        public TaskCompletionSource? MergedGate { get; set; }

        public async Task<GitHubMergedPullRequestRead> ListMergedPullRequestsAsync(GitHubRepositoryRef repository, DateTimeOffset mergedSince, CancellationToken cancellationToken = default)
        {
            lock (MergedListed) MergedListed.Add(repository.FullName);

            // The answer is what GitHub held when the read was sent, so a read held at
            // the gate comes back with what was true before anything that happened
            // while it waited.
            var answer = new GitHubMergedPullRequestRead(
                _merged.TryGetValue(repository.FullName, out var list) ? list() : [],
                _truncated.Contains(repository.FullName));

            if (MergedGate is { } gate) await gate.Task;

            return answer;
        }

        /// <summary>When set, every act waits on it — how a test switches views while
        /// an act is still out.</summary>
        public TaskCompletionSource? ActGate { get; set; }

        public StubClient RefusesActs(string message)
        {
            _refusal = message;
            return this;
        }

        public Task<IReadOnlyList<GitHubOpenPullRequest>> ListOpenPullRequestsAsync(GitHubRepositoryRef repository, CancellationToken cancellationToken = default)
        {
            lock (Listed)
            {
                Listed.Add(repository.FullName);
                if (_firstReadSize == 0 && Listed.Count == _lists.Count) _firstReadSize = Listed.Count;
            }

            try
            {
                return Task.FromResult(_lists[repository.FullName]());
            }
            catch (GitHubException exception)
            {
                return Task.FromException<IReadOnlyList<GitHubOpenPullRequest>>(exception);
            }
        }

        public Task UpdateBranchAsync(GitHubRepositoryRef repository, int number, string? expectedHeadSha, CancellationToken cancellationToken = default) =>
            Act($"update {repository.FullName}#{number} {expectedHeadSha}");

        public Task MarkReadyForReviewAsync(GitHubRepositoryRef repository, string pullRequestId, CancellationToken cancellationToken = default) =>
            Act($"ready {pullRequestId}");

        public Task MergePullRequestAsync(GitHubRepositoryRef repository, string pullRequestId, GitHubMergeMethod method, CancellationToken cancellationToken = default) =>
            Act($"merge {pullRequestId} {method}");

        public Task EnableAutoMergeAsync(GitHubRepositoryRef repository, string pullRequestId, GitHubMergeMethod method, CancellationToken cancellationToken = default) =>
            Act($"auto-merge {pullRequestId} {method}");

        public Task DisableAutoMergeAsync(GitHubRepositoryRef repository, string pullRequestId, CancellationToken cancellationToken = default) =>
            Act($"cancel auto-merge {pullRequestId}");

        private async Task Act(string what)
        {
            Acts.Add(what);

            if (ActGate is { } gate) await gate.Task;

            if (_refusal is not null) throw new GitHubException(_refusal);
        }

        public Task<GitHubIssue> CreateIssueAsync(GitHubRepositoryRef repository, string title, string? body, IEnumerable<string>? labels = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<GitHubIssueSnapshot> GetIssueAsync(GitHubRepositoryRef repository, int number, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<GitHubUploadedFile> UploadFileAsync(GitHubRepositoryRef repository, string path, string branch, byte[] content, string commitMessage, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<GitHubCommittedFile> CommitFileAsync(GitHubRepositoryRef repository, string path, byte[] content, string commitMessage, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class StubSessionSource(IReadOnlyList<AgentSession> sessions) : IAgentSessionSource
    {
        public Task<AgentSessionCatalog> GetSessionsAsync(AgentSessionQuery query, CancellationToken cancellationToken = default) =>
            GetSessionsAsync(cancellationToken);

        public Task<AgentSessionCatalog> GetSessionsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new AgentSessionCatalog(sessions, [], sessions.Count));
    }
}
