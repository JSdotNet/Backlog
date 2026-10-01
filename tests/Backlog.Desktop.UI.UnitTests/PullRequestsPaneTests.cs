using Backlog.Desktop.UI.PullRequests;
using Backlog.Infrastructure.GitHub;
using Backlog.Modules.Sessions.Abstractions;
using Backlog.UI.Components.Feedback;
using Bunit;
using Microsoft.Extensions.DependencyInjection;

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
            .Add(p => p.PullRequestTask, (repository, number) =>
            {
                asked.Add((repository, number));
                return number == 1 ? task : null;
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
            Assert.Equal("Checks failing", pane.Find("[data-testid='pull-request-checks']").TextContent.Trim());
            Assert.Equal("Behind", pane.Find("[data-testid='pull-request-behind']").TextContent.Trim());
            Assert.Equal("Conflicts", pane.Find("[data-testid='pull-request-conflicts']").TextContent.Trim());
            Assert.Equal("Auto-merge on", pane.Find("[data-testid='pull-request-auto-merge']").TextContent.Trim());
        });
    }

    // --- Helpers --------------------------------------------------------------

    private static List<string?> RowKeys(IRenderedComponent<PullRequestsPane> pane) =>
        [.. pane.FindAll("[data-testid='pull-request-row']").Select(row => row.GetAttribute("data-pull-request"))];

    private static IReadOnlyList<ToastMessage> Toasts(BunitContext context) =>
        context.Services.GetRequiredService<IToastChannel>().Visible;

    private BunitContext Context(
        StubClient client,
        string repositories = "JSdotNet/Backlog\nJSdotNet/Archify\nocto/broken",
        IReadOnlyList<AgentSession>? sessions = null,
        bool toasts = true)
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
        context.Services.AddSingleton(new GitHubIntegration(settings, client, new StubProbe()));
        context.Services.AddSingleton<IAgentSessionSource>(new StubSessionSource(sessions ?? []));
        if (toasts) TasksTestHost.AddToastChannel(context.Services);

        return context;
    }

    private static GitHubOpenPullRequest Pull(
        string repository,
        int number,
        bool mine = true,
        bool draft = false,
        bool mergeReady = false,
        bool autoMerge = false,
        bool behind = false,
        bool conflicts = false,
        DateTimeOffset? updated = null) =>
        new(
            number,
            $"https://github.com/{repository}/pull/{number}",
            $"Pull request {number}",
            repository,
            $"PR_{number}",
            IsDraft: draft,
            HeadRefName: $"feature-{number}",
            HeadSha: $"sha-{number}",
            BaseRefName: "main",
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
        private string? _refusal;
        private int _firstReadSize;

        public List<string> Acts { get; } = [];

        public List<string> Listed { get; } = [];

        public int ListCalls => Listed.Count;

        /// <summary>The repositories read after the opening read of every repository —
        /// what an act's re-read asked for.</summary>
        public IReadOnlyList<string> ListedAfterFirstRead => [.. Listed.Skip(_firstReadSize)];

        public bool Knows(string fullName) => _lists.ContainsKey(fullName);

        public StubClient Lists(string fullName, params GitHubOpenPullRequest[] pulls)
        {
            _lists[fullName] = () => pulls;
            return this;
        }

        public StubClient Refuses(string fullName, string message)
        {
            _lists[fullName] = () => throw new GitHubException(message);
            return this;
        }

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

        private Task Act(string what)
        {
            Acts.Add(what);
            return _refusal is null ? Task.CompletedTask : Task.FromException(new GitHubException(_refusal));
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

    private sealed class StubProbe : IGitHubConnectionProbe
    {
        public Task<GitHubConnection> DescribeAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new GitHubConnection(true, "Connected."));

        public void Invalidate()
        {
        }
    }

    private sealed class StubSessionSource(IReadOnlyList<AgentSession> sessions) : IAgentSessionSource
    {
        public Task<AgentSessionCatalog> GetSessionsAsync(AgentSessionQuery query, CancellationToken cancellationToken = default) =>
            GetSessionsAsync(cancellationToken);

        public Task<AgentSessionCatalog> GetSessionsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new AgentSessionCatalog(sessions, [], sessions.Count));
    }
}
