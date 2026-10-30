using Backlog.Modules.Tasks.Abstractions.Services;
using Backlog.Modules.Tasks.Extensions;
using Backlog.Modules.Tasks.Services;

using Microsoft.Extensions.DependencyInjection;

using Xunit;

namespace Backlog.Modules.Tasks.UnitTests;

/// <summary>
/// Which task a session or a pull request belongs to, and which pull requests the tasks
/// link, as Tasks answers the shell through <see cref="ITaskLookup"/> (local ADR 0042,
/// V8). A pull request's task is found in order of how sure the match is: the task's
/// own record of the pull request, then a GitHub issue the pull request closes, then
/// the task's external key in the pull request's title or branch, then the task that
/// linked the session the pull request came out of.
/// </summary>
public sealed class TaskLookupTests
{
    private static readonly Guid Recorded = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Issue = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Keyed = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid Session = Guid.Parse("44444444-4444-4444-4444-444444444444");

    // --- Each relation on its own ---------------------------------------------

    [Fact]
    public void A_task_that_recorded_the_pull_request_is_its_task()
    {
        var lookup = Lookup(Candidate(Recorded, pullRequests: [new("jsdotnet/backlog", 712)]));

        var match = lookup.Match(Question());

        Assert.Equal(Recorded, match?.Candidate.EntryId);
        Assert.Equal(PullRequestTaskRelation.Recorded, match?.Relation);
    }

    [Fact]
    public void A_linked_github_issue_the_pull_request_closes_is_its_task()
    {
        var lookup = Lookup(Candidate(Issue, linkedIssue: new("JSdotNet/Backlog", 42)));

        var match = lookup.Match(Question(closing: [new TaskIssueReference("jsdotnet/backlog", 42)]));

        Assert.Equal(Issue, match?.Candidate.EntryId);
        Assert.Equal(PullRequestTaskRelation.ClosesIssue, match?.Relation);
    }

    /// <summary>The issue's repository counts: issue 42 of another repository is
    /// another issue.</summary>
    [Fact]
    public void An_issue_with_the_same_number_in_another_repository_is_not_a_match()
    {
        var lookup = Lookup(Candidate(Issue, linkedIssue: new("JSdotNet/Archify", 42)));

        Assert.Null(lookup.TaskForPullRequest(Question(closing: [new TaskIssueReference("JSdotNet/Backlog", 42)])));
    }

    [Theory]
    [InlineData("FIN-8428: Show the invoice total", "claude/unrelated")]
    [InlineData("Show the invoice total", "feature/FIN-8428-invoice-total")]
    [InlineData("fin-8428 lower case", "x")]
    [InlineData("[FIN-8428] bracketed", "x")]
    public void The_tasks_external_key_in_the_title_or_branch_is_a_match(string title, string branch)
    {
        var lookup = Lookup(Candidate(Keyed, displayKey: "#123 · FIN-8428"));

        var match = lookup.Match(Question(title: title, branch: branch));

        Assert.Equal(Keyed, match?.Candidate.EntryId);
        Assert.Equal(PullRequestTaskRelation.ExternalKey, match?.Relation);
    }

    /// <summary>A key is a whole token: FIN-84 is not FIN-8428, and FIN-8428 is not
    /// XFIN-8428 or FIN-84281.</summary>
    [Theory]
    [InlineData("FIN-84", "FIN-8428: total")]
    [InlineData("FIN-8428", "XFIN-8428 total")]
    [InlineData("FIN-8428", "FIN-84281 total")]
    [InlineData("FIN-8428", "X_FIN-8428 total")]
    public void A_key_inside_a_longer_key_is_not_a_match(string key, string title)
    {
        var lookup = Lookup(Candidate(Keyed, displayKey: $"#1 · {key}"));

        Assert.Null(lookup.TaskForPullRequest(Question(title: title, branch: "x")));
    }

    [Fact]
    public void The_task_that_linked_the_pull_requests_session_is_its_task()
    {
        var lookup = Lookup(Candidate(Session, sessionIds: ["session-1"]));

        var match = lookup.Match(Question(sessionId: "SESSION-1"));

        Assert.Equal(Session, match?.Candidate.EntryId);
        Assert.Equal(PullRequestTaskRelation.Session, match?.Relation);
    }

    [Fact]
    public void A_pull_request_nothing_relates_to_has_no_task()
    {
        var lookup = Lookup(Candidate(Keyed, displayKey: "#1 · FIN-1"));

        Assert.Null(lookup.TaskForPullRequest(Question()));
    }

    /// <summary>The answer is the task as another pane hands it back: its id to open it
    /// by, and the title the Tasks list gave it.</summary>
    [Fact]
    public void The_answer_names_the_task_by_id_and_title()
    {
        var lookup = Lookup(Candidate(Recorded, title: "Read delivery run files", pullRequests: [new("JSdotNet/Backlog", 712)]));

        Assert.Equal(new TaskReference(Recorded, "Read delivery run files"), lookup.TaskForPullRequest(Question()));
    }

    // --- The order -------------------------------------------------------------

    /// <summary>Every candidate below relates to the pull request; the surest relation
    /// wins whatever order the tasks come in.</summary>
    [Fact]
    public void The_surest_relation_wins()
    {
        var candidates = new[]
        {
            Candidate(Session, sessionIds: ["session-1"]),
            Candidate(Keyed, displayKey: "#1 · FIN-8428"),
            Candidate(Issue, linkedIssue: new("JSdotNet/Backlog", 42)),
            Candidate(Recorded, pullRequests: [new("JSdotNet/Backlog", 712)]),
        };
        var question = Question(title: "FIN-8428 total", closing: [new TaskIssueReference("JSdotNet/Backlog", 42)], sessionId: "session-1");

        Assert.Equal(Recorded, Lookup(candidates).TaskForPullRequest(question)?.EntryId);
        Assert.Equal(Issue, Lookup(candidates[..3]).TaskForPullRequest(question)?.EntryId);
        Assert.Equal(Keyed, Lookup(candidates[..2]).TaskForPullRequest(question)?.EntryId);
        Assert.Equal(Session, Lookup(candidates[..1]).TaskForPullRequest(question)?.EntryId);
    }

    /// <summary>"Mine" asks about my tasks only: a surer match on somebody else's task
    /// does not hide a match on mine.</summary>
    [Fact]
    public void Mine_only_considers_my_tasks()
    {
        var lookup = Lookup(
            Candidate(Recorded, pullRequests: [new("JSdotNet/Backlog", 712)], isMine: false),
            Candidate(Keyed, displayKey: "#1 · FIN-8428"));
        var question = Question(title: "FIN-8428 total");

        Assert.Equal(Recorded, lookup.TaskForPullRequest(question)?.EntryId);
        Assert.Equal(Keyed, lookup.TaskForPullRequest(question, mineOnly: true)?.EntryId);
    }

    [Fact]
    public void Mine_only_with_no_task_of_mine_related_has_no_task()
    {
        var lookup = Lookup(Candidate(Recorded, pullRequests: [new("JSdotNet/Backlog", 712)], isMine: false));

        Assert.Null(lookup.TaskForPullRequest(Question(), mineOnly: true));
    }

    // --- A session's task --------------------------------------------------------

    /// <summary>The entry that linked a session is its task, the id compared without
    /// regard to case, and whether it counts as mine does not matter here.</summary>
    [Fact]
    public void The_task_that_linked_a_session_is_the_sessions_task()
    {
        var lookup = Lookup(
            Candidate(Recorded, pullRequests: [new("JSdotNet/Backlog", 712)]),
            Candidate(Session, title: "Wire the view", sessionIds: ["other", "Session-1"], isMine: false));

        Assert.Equal(new TaskReference(Session, "Wire the view"), lookup.TaskForSession("session-1"));
        Assert.Null(lookup.TaskForSession("session-2"));
    }

    [Fact]
    public void With_two_tasks_linking_a_session_the_first_is_its_task()
    {
        var lookup = Lookup(
            Candidate(Keyed, sessionIds: ["session-1"]),
            Candidate(Session, sessionIds: ["session-1"]));

        Assert.Equal(Keyed, lookup.TaskForSession("session-1")?.EntryId);
    }

    // --- The pull requests tasks link ---------------------------------------------

    /// <summary>Every pull request the tasks link, each once whatever the case of the
    /// repository, in the order the tasks name them.</summary>
    [Fact]
    public void The_linked_pull_requests_are_every_tasks_each_once_whatever_the_case_of_the_repository()
    {
        var lookup = Lookup(
            Candidate(Recorded, pullRequests: [new("JSdotNet/Backlog", 655), new("JSdotNet/Backlog", 700)]),
            Candidate(Keyed, pullRequests: [new("jsdotnet/backlog", 655), new("JSdotNet/Archify", 655)]),
            Candidate(Session));

        Assert.Equal(
            [new TaskPullRequestLink("JSdotNet/Backlog", 655), new TaskPullRequestLink("JSdotNet/Backlog", 700), new TaskPullRequestLink("JSdotNet/Archify", 655)],
            lookup.LinkedPullRequests());
    }

    // --- Over the window's rows ---------------------------------------------------

    /// <summary>The lookup answers over the rows as they are now: a new list from the
    /// Tasks state is matched again, so a link added since the last ask is found.</summary>
    [Fact]
    public void A_new_list_of_candidates_is_matched_again()
    {
        var candidates = new Candidates([Candidate(Keyed)]);
        var lookup = new TaskLookup(candidates);

        Assert.Null(lookup.TaskForPullRequest(Question()));
        Assert.Empty(lookup.LinkedPullRequests());

        candidates.Current = [Candidate(Recorded, pullRequests: [new("JSdotNet/Backlog", 712)])];

        Assert.Equal(Recorded, lookup.TaskForPullRequest(Question())?.EntryId);
        Assert.Equal([new TaskPullRequestLink("JSdotNet/Backlog", 712)], lookup.LinkedPullRequests());
    }

    /// <summary>The port is registered with the lifetime the host gives the window's
    /// state, over the candidates that state answers.</summary>
    [Theory]
    [InlineData(ServiceLifetime.Singleton)]
    [InlineData(ServiceLifetime.Scoped)]
    public void The_lookup_is_registered_with_the_windows_lifetime_over_its_candidates(ServiceLifetime lifetime)
    {
        var services = new ServiceCollection();

        services.AddTaskLookup(lifetime);

        var registered = Assert.Single(services, descriptor => descriptor.ServiceType == typeof(ITaskLookup));
        Assert.Equal(lifetime, registered.Lifetime);

        var lookup = Assert.IsAssignableFrom<ITaskLookup>(registered.ImplementationFactory!(
            new OnlyTheCandidates(new Candidates([Candidate(Recorded, pullRequests: [new("JSdotNet/Backlog", 712)])]))));
        Assert.Equal(Recorded, lookup.TaskForPullRequest(Question())?.EntryId);
    }

    // --- Reading a display key ------------------------------------------------------

    [Theory]
    [InlineData("#123 · FIN-8428", "FIN-8428")]
    [InlineData("#123", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    [InlineData("#123 · not a key", null)]
    public void The_external_key_is_read_from_a_display_key(string? displayKey, string? expected)
    {
        var keys = PullRequestTaskMatching.ExternalKeysOf(displayKey);

        if (expected is null) Assert.Empty(keys);
        else Assert.Equal([expected], keys);
    }

    // --- Helpers ----------------------------------------------------------------

    private static TaskLookup Lookup(params TaskLookupCandidate[] candidates) => new(new Candidates(candidates));

    private static TaskLookupCandidate Candidate(
        Guid id,
        string title = "A task",
        IReadOnlyList<TaskPullRequestLink>? pullRequests = null,
        TaskIssueReference? linkedIssue = null,
        string? displayKey = null,
        IReadOnlyList<string>? sessionIds = null,
        bool isMine = true) =>
        new(id, title, pullRequests ?? [], linkedIssue, displayKey, sessionIds ?? [], isMine);

    private static PullRequestTaskQuestion Question(
        string title = "Pull requests page",
        string branch = "claude/pr-page",
        IReadOnlyList<TaskIssueReference>? closing = null,
        string? sessionId = null) =>
        new("JSdotNet/Backlog", 712, title, branch, closing ?? [], sessionId);

    /// <summary>The window's Tasks rows as the lookup reads them: the same list until
    /// the test hands it another.</summary>
    private sealed class Candidates(IReadOnlyList<TaskLookupCandidate> current) : ITaskLookupCandidates
    {
        public IReadOnlyList<TaskLookupCandidate> Current { get; set; } = current;

        IReadOnlyList<TaskLookupCandidate> ITaskLookupCandidates.Candidates => Current;
    }

    /// <summary>A container holding nothing but the candidates, which is all the
    /// lookup's registration may ask for.</summary>
    private sealed class OnlyTheCandidates(ITaskLookupCandidates candidates) : IServiceProvider
    {
        public object? GetService(Type serviceType) => serviceType == typeof(ITaskLookupCandidates) ? candidates : null;
    }
}
