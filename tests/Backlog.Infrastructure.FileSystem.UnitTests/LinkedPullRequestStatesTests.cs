using Backlog.Infrastructure.FileSystem.Tasks;
using Backlog.Modules.RepositoryManagement.Abstractions;
using Backlog.Modules.RepositoryManagement.Abstractions.DataTransferObjects;
using Backlog.Modules.RepositoryManagement.Abstractions.Services;
using Backlog.Modules.Tasks.Abstractions.Services;
using Backlog.SharedKernel.Results;
using Backlog.Tests;
using Microsoft.Extensions.DependencyInjection;

namespace Backlog.Infrastructure.FileSystem.UnitTests;

/// <summary>
/// Tasks' port on the pull requests a task links to, answered out of Repository
/// Management's contract (<see cref="LinkedPullRequestStates"/>, local ADR 0042 §4): the
/// task row wears the verdict the Pull requests pane draws (V2), offers the merge the
/// pane would, by the pane's own <c>PullRequestMerges.For</c> (V2, V5), and acts and hears
/// changes through the contract (V10). A host
/// with no contract answers nothing (V5).
/// </summary>
public sealed class LinkedPullRequestStatesTests
{
    private const string Backlog = "JSdotNet/Backlog";

    private static readonly DateTimeOffset Noon = new(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);

    public static TheoryData<string> Verdicts => [.. Fixtures.Keys];

    private static readonly Dictionary<string, PullRequestStatus> Fixtures = new()
    {
        ["checks failing"] = Pull(4) with { Checks = PullRequestCheckState.Failing, CheckCounts = new PullRequestCheckCounts(3, 1, 0) },
        ["conflicts"] = Pull(4) with { HasConflicts = true },
        ["behind"] = Pull(4) with { IsBehind = true, BehindBy = 2, Checks = PullRequestCheckState.Passing },
        ["draft"] = Pull(4, PullRequestState.Draft),
        ["checks running"] = Pull(4) with { Checks = PullRequestCheckState.Pending, CheckCounts = new PullRequestCheckCounts(1, 0, 2) },
        ["review required"] = Pull(4) with
        {
            Checks = PullRequestCheckState.Passing,
            Approvals = new PullRequestApprovals(PullRequestReviewDecision.ReviewRequired, 0, 0) { Required = 1, RequiredRead = true }
        },
        ["ready"] = Pull(4) with { Checks = PullRequestCheckState.Passing, MergeReady = true },
        ["merged"] = Pull(4, PullRequestState.Merged) with { MergedAt = Noon }
    };

    /// <summary>One pull request reads the same on the task row as in the Pull requests
    /// pane: the label and the tone are the module's verdict (V2, task row).</summary>
    [Theory]
    [MemberData(nameof(Verdicts))]
    public async Task A_pull_request_wears_the_label_and_tone_of_the_modules_verdict(string fixture)
    {
        var status = Fixtures[fixture];
        var states = new LinkedPullRequestStates(new InMemoryPullRequests().Holds(status));
        var verdict = PullRequestVerdict.Of(status)!;

        var read = await states.StatesOfAsync([Key(4)], cancellationToken: TestContext.Current.CancellationToken);

        var linked = read.States[Key(4)];
        Assert.Equal(verdict.Label, linked.VerdictLabel);
        Assert.Equal(verdict.Tone.ToString(), linked.VerdictTone.ToString());
    }

    /// <summary>A worked example beside the theory, so the mapping is anchored on words
    /// a person reads, not only on the module agreeing with itself.</summary>
    [Fact]
    public async Task Failing_checks_read_as_a_fault_counting_the_failures()
    {
        var states = new LinkedPullRequestStates(new InMemoryPullRequests().Holds(Fixtures["checks failing"]));

        var read = await states.StatesOfAsync([Key(4)], cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(("1 failing", LinkedPullRequestTone.Fault), (read.States[Key(4)].VerdictLabel, read.States[Key(4)].VerdictTone));
    }

    /// <summary>The badge's marks come from the same status: checks, review and the
    /// auto-merge request, with the branches a merge needs.</summary>
    [Fact]
    public async Task The_badge_facts_and_the_branches_come_from_the_same_status()
    {
        var states = new LinkedPullRequestStates(new InMemoryPullRequests().Holds(
            Pull(4) with
            {
                Checks = PullRequestCheckState.Pending,
                Approvals = new PullRequestApprovals(PullRequestReviewDecision.ChangesRequested, 0, 1),
                AutoMergeEnabled = true,
                DefaultBranch = "main"
            }));

        var read = await states.StatesOfAsync([Key(4)], cancellationToken: TestContext.Current.CancellationToken);

        var linked = read.States[Key(4)];
        Assert.Equal(
            (LinkedPullRequestState.Open, LinkedPullRequestChecks.Pending, LinkedPullRequestReview.ChangesRequested, true, "main", "main"),
            (linked.State, linked.Checks, linked.Review, linked.AutoMerge, linked.BaseRefName, linked.DefaultBranch));
    }

    public static TheoryData<string, LinkedPullRequestMerge?> MergeOffers => new()
    {
        { "mergeable now", LinkedPullRequestMerge.MergeNow },
        { "checks pending", LinkedPullRequestMerge.MergeWhenReady },
        { "held for auto-merge", LinkedPullRequestMerge.CancelAutoMerge },
        { "a draft", null },
        { "in conflict", null },
        { "merged", null },
        { "status not read", null }
    };

    private static readonly Dictionary<string, PullRequestStatus> OfferFixtures = new()
    {
        ["mergeable now"] = Pull(4) with { MergeReady = true },
        ["checks pending"] = Pull(4) with { Checks = PullRequestCheckState.Pending },
        ["held for auto-merge"] = Pull(4) with { AutoMergeEnabled = true, HasConflicts = true },
        ["a draft"] = Pull(4, PullRequestState.Draft) with { MergeReady = true },
        ["in conflict"] = Pull(4) with { HasConflicts = true },
        ["merged"] = Pull(4, PullRequestState.Merged) with { MergeReady = true },
        ["status not read"] = Pull(4) with { StatusRead = false }
    };

    /// <summary>The row offers the merge the pane offers, by the module's one rule (V2,
    /// V5): conflicts and drafts get none, and a status that was not read — an older
    /// GitHub Enterprise Server's REST answer — offers nothing it cannot name.</summary>
    [Theory]
    [MemberData(nameof(MergeOffers))]
    public async Task The_merge_offer_follows_the_modules_rule(string fixture, LinkedPullRequestMerge? expected)
    {
        var states = new LinkedPullRequestStates(new InMemoryPullRequests().Holds(OfferFixtures[fixture]));

        var read = await states.StatesOfAsync([Key(4)], cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(expected, read.States[Key(4)].MergeOffer);
    }

    /// <summary>A pull request stacked on another open one waits on its parent: the
    /// row's verdict names the parent, as the pane's does, and offers no merge, as the
    /// pane offers none (V2). Its
    /// place in the stack comes from the repository's open list, as the session card's
    /// does.</summary>
    [Fact]
    public async Task A_stacked_pull_request_names_its_parent_and_offers_no_merge()
    {
        var parent = Pull(1) with { HeadRefName = "feature-a", UpdatedAt = Noon };
        var child = Pull(2) with { BaseRefName = "feature-a", MergeReady = true, Checks = PullRequestCheckState.Passing, UpdatedAt = Noon };
        var contract = new InMemoryPullRequests().Holds(parent, child);
        var states = new LinkedPullRequestStates(contract);

        var read = await states.StatesOfAsync([Key(2)], cancellationToken: TestContext.Current.CancellationToken);

        var linked = read.States[Key(2)];
        Assert.Null(linked.MergeOffer);
        Assert.Equal(PullRequestVerdict.Of(child with { StackParent = parent.Reference })!.Label, linked.VerdictLabel);
        Assert.Equal([$"read {Backlog}#2", $"list {Backlog}"], contract.Calls);
    }

    /// <summary>
    /// An open pull request off the default branch whose repository's open list could not
    /// be read — the read threw, or refused that repository — might be stacked: the row
    /// shows its state and its marks, but no verdict and no merge, as the pane does for it.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task An_open_pull_request_whose_place_could_not_be_read_has_no_verdict_and_offers_no_merge(bool listThrows)
    {
        var contract = new InMemoryPullRequests().Holds(Pull(4) with { MergeReady = true, Checks = PullRequestCheckState.Passing, BaseRefName = "plan/x", DefaultBranch = "main" });
        if (listThrows) contract.ListThrows = new InvalidOperationException("The list did not answer.");
        else contract.ListRefuses.Add(Backlog);
        var states = new LinkedPullRequestStates(contract);

        var read = await states.StatesOfAsync([Key(4)], cancellationToken: TestContext.Current.CancellationToken);

        var linked = read.States[Key(4)];
        Assert.Equal(LinkedPullRequestState.Open, linked.State);
        Assert.Equal(LinkedPullRequestChecks.Passing, linked.Checks);
        Assert.Null(linked.MergeOffer);
        Assert.Null(linked.VerdictLabel);
        Assert.Null(linked.VerdictTone);
        Assert.Empty(read.UnreadRepositories);
    }

    /// <summary>
    /// One based on its repository's default branch waits on nothing, so it wears its
    /// verdict and is offered its merge however its open list fared — capped, refused, or
    /// not kept for an unregistered repository — as the session card does, by the one
    /// placing rule (<see cref="PullRequestStacks.PlaceAmong"/>).
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task An_open_pull_request_based_on_the_default_branch_wears_its_verdict_whatever_its_list(bool listFails)
    {
        var pull = Pull(4) with { MergeReady = true, Checks = PullRequestCheckState.Passing, DefaultBranch = "main" };
        var contract = new InMemoryPullRequests().Holds(pull);
        if (listFails) contract.ListThrows = new InvalidOperationException("The list did not answer.");
        else contract.Unlisted.Add(Backlog);
        var states = new LinkedPullRequestStates(contract);

        var read = await states.StatesOfAsync([Key(4)], cancellationToken: TestContext.Current.CancellationToken);

        var linked = read.States[Key(4)];
        Assert.Equal(PullRequestVerdict.Of(pull)!.Label, linked.VerdictLabel);
        Assert.Equal(LinkedPullRequestMerge.MergeNow, linked.MergeOffer);
    }

    /// <summary>A pull request its repository's whole list was read without — opened
    /// after it — and based on no head that list holds waits on nothing: its verdict and
    /// its merge, as the pane and the card give them.</summary>
    [Fact]
    public async Task An_open_pull_request_a_read_list_does_not_explain_waits_on_nothing()
    {
        var pull = Pull(4) with { MergeReady = true, Checks = PullRequestCheckState.Passing, BaseRefName = "plan/x", DefaultBranch = "main" };
        var contract = new InMemoryPullRequests().Holds(pull);
        contract.ListedWithout.Add(pull.Reference.Key);
        var states = new LinkedPullRequestStates(contract);

        var read = await states.StatesOfAsync([Key(4)], cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(PullRequestVerdict.Of(pull)!.Label, read.States[Key(4)].VerdictLabel);
        Assert.Equal(LinkedPullRequestMerge.MergeNow, read.States[Key(4)].MergeOffer);
    }

    /// <summary>
    /// A list that may not hold the whole repository — capped, or an unregistered
    /// repository's — cannot say a parent is not open beyond it: a pull request off the
    /// default branch it does not explain has no verdict and no merge, rather than a merge
    /// onto its parent's branch.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_pull_request_a_list_short_of_the_whole_repository_does_not_explain_has_no_verdict(bool capped)
    {
        var contract = new InMemoryPullRequests().Holds(Pull(4) with { MergeReady = true, BaseRefName = "plan/x", DefaultBranch = "main" });
        if (capped)
        {
            contract.Capped.Add(Backlog);
            contract.ListedWithout.Add($"{Backlog}#4");
        }
        else
        {
            contract.Unlisted.Add(Backlog);
        }

        var read = await new LinkedPullRequestStates(contract).StatesOfAsync([Key(4)], cancellationToken: TestContext.Current.CancellationToken);

        Assert.Null(read.States[Key(4)].VerdictLabel);
        Assert.Null(read.States[Key(4)].MergeOffer);
    }

    /// <summary>A pull request whose place cannot be told can still withdraw an
    /// auto-merge GitHub holds for it — a withdrawal lands nothing — and is offered
    /// nothing else.</summary>
    [Fact]
    public async Task An_unplaced_pull_request_held_for_auto_merge_still_offers_its_cancel()
    {
        var contract = new InMemoryPullRequests().Holds(Pull(4) with { AutoMergeEnabled = true, BaseRefName = "plan/x", DefaultBranch = "main" });
        contract.ListRefuses.Add(Backlog);

        var read = await new LinkedPullRequestStates(contract).StatesOfAsync([Key(4)], cancellationToken: TestContext.Current.CancellationToken);

        Assert.Null(read.States[Key(4)].VerdictLabel);
        Assert.Equal(LinkedPullRequestMerge.CancelAutoMerge, read.States[Key(4)].MergeOffer);
    }

    /// <summary>A merged pull request needs no place in a stack, so nothing is listed
    /// for it; nor is a closed one, which has no verdict at all.</summary>
    [Fact]
    public async Task Merged_and_closed_pull_requests_are_read_by_number_alone()
    {
        var contract = new InMemoryPullRequests().Holds(Pull(4, PullRequestState.Merged) with { MergedAt = Noon }, Pull(5, PullRequestState.Closed));
        var states = new LinkedPullRequestStates(contract);

        var read = await states.StatesOfAsync([Key(4), Key(5)], cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(LinkedPullRequestState.Merged, read.States[Key(4)].State);
        Assert.Equal((LinkedPullRequestState.Closed, (string?)null, (LinkedPullRequestTone?)null), (read.States[Key(5)].State, read.States[Key(5)].VerdictLabel, read.States[Key(5)].VerdictTone));
        Assert.Equal([$"read {Backlog}#4 {Backlog}#5"], contract.Calls);
    }

    /// <summary>A read a person asked for asks GitHub again; the background check takes
    /// what another reader kept within the minute.</summary>
    [Fact]
    public async Task A_read_a_person_asked_for_is_fresh_and_the_background_read_is_not()
    {
        var contract = new InMemoryPullRequests().Holds(Pull(4, PullRequestState.Merged));
        var states = new LinkedPullRequestStates(contract);

        await states.StatesOfAsync([Key(4)], cancellationToken: TestContext.Current.CancellationToken);
        await states.StatesOfAsync([Key(4)], fresh: true, TestContext.Current.CancellationToken);

        Assert.Equal([PullRequestReadOptions.Default, PullRequestReadOptions.Fresh], contract.ReadOptions);
    }

    /// <summary>The answer is keyed by the key asked, whatever its repository's case or
    /// space; a pull request GitHub does not know is absent; a repository that refused is
    /// named so the list asks again later, and costs the others nothing.</summary>
    [Fact]
    public async Task A_refused_repository_is_unread_and_the_others_still_answer()
    {
        var contract = new InMemoryPullRequests()
            .Holds(Pull(4, PullRequestState.Merged))
            .Refuses("octo/upstream", "Not Found");
        var states = new LinkedPullRequestStates(contract);
        var asked = new LinkedPullRequestKey(" jsdotnet/backlog ", 4);

        var read = await states.StatesOfAsync([asked, Key(9), new("octo/upstream", 7)], cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(LinkedPullRequestState.Merged, Assert.Single(read.States).Value.State);
        Assert.True(read.States.ContainsKey(asked));
        Assert.Equal(["octo/upstream"], read.UnreadRepositories);
    }

    /// <summary>A contract that throws is every asked repository unread, never a throw
    /// into the task list.</summary>
    [Fact]
    public async Task A_read_that_throws_is_every_repository_unread()
    {
        var contract = new InMemoryPullRequests { Throws = new InvalidOperationException("boom") };
        var states = new LinkedPullRequestStates(contract);

        var read = await states.StatesOfAsync([Key(4), new("octo/upstream", 7)], cancellationToken: TestContext.Current.CancellationToken);

        Assert.Empty(read.States);
        Assert.Equal(["JSdotNet/Backlog", "octo/upstream"], read.UnreadRepositories.Order(StringComparer.Ordinal));
    }

    /// <summary>The first frame's answer reads nothing: what the contract keeps, merged
    /// pull requests from its settled facts included.</summary>
    [Fact]
    public void What_is_kept_is_answered_without_a_read()
    {
        var contract = new InMemoryPullRequests().Holds(Pull(4, PullRequestState.Merged) with { BaseRefName = "plan/x", DefaultBranch = "main", StatusRead = false });
        var states = new LinkedPullRequestStates(contract);

        var kept = states.Kept([Key(4), Key(5)]);

        var linked = Assert.Single(kept).Value;
        Assert.Equal((LinkedPullRequestState.Merged, "plan/x", "main", (string?)null), (linked.State, linked.BaseRefName, linked.DefaultBranch, linked.VerdictLabel));
        Assert.Empty(contract.Calls);
    }

    /// <summary>The row's two merge acts are the contract's commands, and the contract's
    /// answer — success, or GitHub's refusal in its own words — comes back as it is.</summary>
    [Fact]
    public async Task The_merge_acts_go_through_the_contract_and_its_answer_comes_back()
    {
        var contract = new InMemoryPullRequests();
        var states = new LinkedPullRequestStates(contract);

        var merged = await states.MergeNowAsync(Key(4), TestContext.Current.CancellationToken);
        await states.SetAutoMergeAsync(Key(5), enable: true, TestContext.Current.CancellationToken);
        await states.SetAutoMergeAsync(Key(6), enable: false, TestContext.Current.CancellationToken);
        contract.ActsWith = Result.Failure(PullRequestErrors.Refused("Auto-merge is not allowed in this repository."));
        var refused = await states.SetAutoMergeAsync(Key(7), enable: true, TestContext.Current.CancellationToken);

        Assert.True(merged.IsSuccess);
        Assert.Equal("Auto-merge is not allowed in this repository.", refused.Error.Message);
        Assert.Equal(
            [$"merge {Backlog}#4", $"auto-merge {Backlog}#5", $"cancel auto-merge {Backlog}#6", $"auto-merge {Backlog}#7"],
            contract.Calls);
    }

    /// <summary>A merge made anywhere — the Pull requests pane included — is heard by the
    /// task list as the pull requests it changed, until the port is let go.</summary>
    [Fact]
    public void A_change_from_the_contract_is_heard_as_the_pull_requests_it_names()
    {
        var contract = new InMemoryPullRequests();
        var states = new LinkedPullRequestStates(contract);
        var heard = new List<IReadOnlyCollection<LinkedPullRequestKey>>();
        states.Changed += heard.Add;

        contract.Raise(new PullRequestsChanged([Backlog], [new PullRequestReference(Backlog, 4), new PullRequestReference(Backlog, 5)]));
        states.Dispose();
        contract.Raise(new PullRequestsChanged([Backlog], [new PullRequestReference(Backlog, 6)]));

        Assert.Equal([Key(4), Key(5)], Assert.Single(heard));
    }

    /// <summary>A host that composes no Repository Management answers nothing, keeps
    /// nothing and acts on nothing (V5): the badge draws with no state and the row offers
    /// no merge.</summary>
    [Fact]
    public async Task Without_the_contract_the_port_answers_nothing()
    {
        var states = new ServiceCollection()
            .AddTasksCrossContextAdapters()
            .BuildServiceProvider()
            .GetRequiredService<ILinkedPullRequestStates>();

        var read = await states.StatesOfAsync([Key(4)], cancellationToken: TestContext.Current.CancellationToken);
        var merged = await states.MergeNowAsync(Key(4), TestContext.Current.CancellationToken);

        Assert.Empty(read.States);
        Assert.Empty(read.UnreadRepositories);
        Assert.Empty(states.Kept([Key(4)]));
        Assert.Equal(PullRequestErrors.NotConfiguredCode, merged.Error.Code);
    }

    /// <summary>With the contract composed, the port is the adapter over it.</summary>
    [Fact]
    public async Task With_the_contract_the_port_reads_through_it()
    {
        var contract = new InMemoryPullRequests().Holds(Pull(4, PullRequestState.Merged));
        var states = new ServiceCollection()
            .AddSingleton<IPullRequests>(contract)
            .AddTasksCrossContextAdapters()
            .BuildServiceProvider()
            .GetRequiredService<ILinkedPullRequestStates>();

        var read = await states.StatesOfAsync([Key(4)], cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(LinkedPullRequestState.Merged, read.States[Key(4)].State);
    }

    private static LinkedPullRequestKey Key(int number) => new(Backlog, number);

    private static PullRequestStatus Pull(int number, PullRequestState state = PullRequestState.Open) =>
        new(Backlog, number, $"https://github.com/{Backlog}/pull/{number}", $"Pull request {number}", state, $"feature-{number}", "main", "octo", false, Noon);
}
