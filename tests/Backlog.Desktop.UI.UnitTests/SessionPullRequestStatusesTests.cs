using Backlog.Desktop.UI.PullRequests;
using Backlog.Modules.RepositoryManagement.Abstractions;
using Backlog.Modules.RepositoryManagement.Abstractions.DataTransferObjects;
using Backlog.Modules.Tasks.Abstractions.Services;
using Backlog.Tests;

namespace Backlog.Desktop.UI.UnitTests;

/// <summary>
/// What the sessions list's pull request card is told about a pull request: where it
/// points on GitHub, and Repository Management's one verdict on it — the label and tone
/// the pull requests pane gives the same status (local ADR 0042) — read through the
/// module's contract, never the GitHub adapter.
/// </summary>
public sealed class SessionPullRequestStatusesTests
{
    [Fact]
    public void A_reference_with_a_url_points_at_its_repository_and_number()
    {
        var pin = SessionPullRequestStatuses.PinOf(Reference("PR #1036", "https://github.com/JSdotNet/Backlog/pull/1036", repository: null));

        Assert.Equal(new PullRequestReference("JSdotNet/Backlog", 1036), pin);
    }

    [Fact]
    public void A_reference_with_no_url_is_placed_by_its_repository_and_label()
    {
        var pin = SessionPullRequestStatuses.PinOf(Reference("PR #587", url: null, repository: "JSdotNet/Backlog"));

        Assert.Equal(new PullRequestReference("JSdotNet/Backlog", 587), pin);
    }

    [Theory]
    [InlineData("PR #587", null, "backlog")]
    [InlineData("PR #587", null, null)]
    [InlineData("a pull request", null, "JSdotNet/Backlog")]
    [InlineData("PR #12", "https://example.com/pull/12", null)]
    public void A_reference_that_cannot_be_placed_on_GitHub_is_not_read(string label, string? url, string? repository) =>
        Assert.Null(SessionPullRequestStatuses.PinOf(Reference(label, url, repository)));

    /// <summary>One status, one verdict: the card wears the label and the tone
    /// <see cref="PullRequestVerdict.Of(PullRequestStatus)"/> gives the status, the same
    /// the pane's row wears for it.</summary>
    [Fact]
    public void An_open_pull_request_wears_its_verdicts_label_and_tone()
    {
        var pull = Pull() with { Checks = PullRequestCheckState.Failing, CheckCounts = new PullRequestCheckCounts(3, 2, 0) };
        var verdict = PullRequestVerdict.Of(pull)!;

        var status = SessionPullRequestStatuses.Of(pull);

        Assert.Equal(verdict.Label, status.Label);
        Assert.Equal(verdict.Tone.ToString().ToLowerInvariant(), status.Tone);
        Assert.Equal(("2 failing", "fault"), (status.Label, status.Tone));
        Assert.Equal("Add the thing", status.Title);
        Assert.Equal(verdict.Reason, status.Reason);
    }

    /// <summary>A stacked pull request's card names the pull request it waits on, as
    /// the pane's banner does, so the two screens give it the same reason.</summary>
    [Fact]
    public void A_stacked_pull_request_names_its_parent_as_the_pane_does()
    {
        var pull = Pull() with { BaseRefName = "feature/base", StackParent = new PullRequestReference("acme/app", 41) };

        var status = SessionPullRequestStatuses.Of(pull);

        Assert.Equal(PullRequestVerdict.Of(pull)!.Reason, status.Reason);
        Assert.EndsWith("and it waits on #41, which has to merge first.", status.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void A_merged_pull_request_is_merged_and_settled()
    {
        var status = SessionPullRequestStatuses.Of(Pull() with { State = PullRequestState.Merged });

        Assert.Equal("Merged", status.Label);
        Assert.Equal("settled", status.Tone);
    }

    [Fact]
    public void A_pull_request_closed_without_merging_is_closed_and_archived()
    {
        var status = SessionPullRequestStatuses.Of(Pull() with { State = PullRequestState.Closed });

        Assert.Equal("Closed", status.Label);
        Assert.Equal("archived", status.Tone);
    }

    /// <summary>The card reads its pull request by number through the contract, then
    /// takes the parent it is stacked on from its repository's open list, which the
    /// contract places — so its reason names the parent, as the pane's does.</summary>
    [Fact]
    public async Task The_card_reads_through_the_contract_and_takes_its_stack_parent_from_the_open_list()
    {
        var parent = Pull() with { Number = 41, HeadRefName = "feature/base" };
        var contract = new InMemoryPullRequests().Holds(parent, Pull() with { BaseRefName = "feature/base" });

        var status = await SessionPullRequestStatuses.ReadAsync(contract, Reference("PR #42", "https://github.com/acme/app/pull/42", repository: null));

        Assert.NotNull(status);
        Assert.EndsWith("and it waits on #41, which has to merge first.", status.Reason, StringComparison.Ordinal);
        Assert.Equal(["read acme/app#42", "list acme/app"], contract.Calls);
    }

    /// <summary>
    /// The card and the task row place a pull request by one rule
    /// (<see cref="PullRequestStacks.PlaceAmong"/>), so they give it one verdict (local
    /// ADR 0042, V2) — the same label, or none on both where its place cannot be told —
    /// whether or not its repository's open list holds it.
    /// </summary>
    [Theory]
    [InlineData("main", false)]
    [InlineData("feature/gone", false)]
    [InlineData("feature/base", true)]
    public async Task The_card_and_the_task_row_give_a_pull_request_one_verdict(string baseRef, bool listed)
    {
        var parent = Pull() with { Number = 41, HeadRefName = "feature/base" };
        var pull = Pull() with { BaseRefName = baseRef, DefaultBranch = "main", MergeReady = true };
        var contract = new InMemoryPullRequests().Holds(parent, pull);
        if (!listed) contract.Unlisted.Add("acme/app");
        var row = new Backlog.Infrastructure.FileSystem.Tasks.LinkedPullRequestStates(contract);

        var card = await SessionPullRequestStatuses.ReadAsync(contract, Reference("PR #42", url: null, repository: "acme/app"));
        var read = await row.StatesOfAsync([new LinkedPullRequestKey("acme/app", 42)], cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(card?.Label, read.States[new LinkedPullRequestKey("acme/app", 42)].VerdictLabel);
    }

    /// <summary>A pull request off the default branch whose repository's open list could
    /// not be read might be stacked: the card draws no verdict rather than one that might
    /// be wrong.</summary>
    [Fact]
    public async Task A_pull_request_whose_place_cannot_be_told_draws_no_verdict()
    {
        var contract = new InMemoryPullRequests().Holds(Pull() with { BaseRefName = "feature/gone", DefaultBranch = "main" });
        contract.ListRefuses.Add("acme/app");

        Assert.Null(await SessionPullRequestStatuses.ReadAsync(contract, Reference("PR #42", url: null, repository: "acme/app")));
    }

    /// <summary>A merged pull request is in no open list, so none is read for it.</summary>
    [Fact]
    public async Task A_merged_pull_request_is_read_by_number_alone()
    {
        var contract = new InMemoryPullRequests().Holds(Pull() with { State = PullRequestState.Merged });

        var status = await SessionPullRequestStatuses.ReadAsync(contract, Reference("PR #42", url: null, repository: "acme/app"));

        Assert.Equal("Merged", status!.Label);
        Assert.Equal(["read acme/app#42"], contract.Calls);
    }

    [Fact]
    public async Task Without_the_contract_or_a_registered_repository_nothing_is_read()
    {
        var unconfigured = new InMemoryPullRequests { IsConfigured = false }.Holds(Pull());
        var reference = Reference("PR #42", url: null, repository: "acme/app");

        Assert.Null(await SessionPullRequestStatuses.ReadAsync(null, reference));
        Assert.Null(await SessionPullRequestStatuses.ReadAsync(unconfigured, reference));
        Assert.Empty(unconfigured.Calls);
    }

    private static DeliveryRunReference Reference(string label, string? url, string? repository) =>
        new(DeliveryRunReferenceKind.PullRequest, label, Title: null, url, repository);

    private static PullRequestStatus Pull() =>
        new(
            RepositoryFullName: "acme/app",
            Number: 42,
            Url: "https://github.com/acme/app/pull/42",
            Title: "Add the thing",
            State: PullRequestState.Open,
            HeadRefName: "feature/thing",
            BaseRefName: "main",
            AuthorLogin: "octocat",
            ViewerDidAuthor: true,
            UpdatedAt: DateTimeOffset.Parse("2026-10-08T10:00:00Z", System.Globalization.CultureInfo.InvariantCulture))
        {
            Checks = PullRequestCheckState.Passing,
            MergeReady = true,
            CheckCounts = new PullRequestCheckCounts(Passed: 5, Failed: 0, Pending: 0)
        };
}
