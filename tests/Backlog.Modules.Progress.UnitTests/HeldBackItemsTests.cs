using Backlog.Modules.Progress.Abstractions;
using Backlog.Modules.Progress.Abstractions.DataTransferObjects;

namespace Backlog.Modules.Progress.UnitTests;

/// <summary>
/// When a held-back item is settled, and when an impediment reads Ready to resolve
/// (<c>.devbook/domain/progress/domain.md#settled</c>, <c>#ready-to-resolve</c>,
/// <c>.devbook/domain/progress/requirements.md#requirement-a-held-back-item-is-settled-by-its-kind</c>):
/// the pure rule the desktop and the phone read alike.
/// </summary>
public sealed class HeldBackItemsTests
{
    private const string Bank = "bank-integration";

    private static readonly DateOnly Oct14 = new(2026, 10, 14);

    private static HeldBackItemDto Held(ProgressTaskDto task) => HeldBackItemDto.ForTask(task.Id);

    private static LinkedPullRequestDto Pull(int number, LinkedPullRequestState state, bool archived = false) =>
        new("acme/invoicing", number.ToString(System.Globalization.CultureInfo.InvariantCulture), state, null, archived);

    private static ImpedimentDto Impediment(ImpedimentState state = ImpedimentState.New, params HeldBackItemDto[] holds) =>
        new(Guid.NewGuid(), "Bank sample files missing", state, null, null, null, null, [new ImpedimentStateChangeDto(state, Oct14, null)], holds);

    [Theory]
    [InlineData(ProgressTaskStatus.Done, true)]
    [InlineData(ProgressTaskStatus.Archived, true)]
    [InlineData(ProgressTaskStatus.InProgress, false)]
    [InlineData(ProgressTaskStatus.Ready, false)]
    [InlineData(ProgressTaskStatus.Draft, false)]
    public void A_task_is_settled_when_Done_or_Archived(ProgressTaskStatus status, bool settled)
    {
        var task = Make.Task("Old CSV import", status);

        Assert.Equal(settled, HeldBackItems.Settled(Held(task), [task], []));
    }

    /// <summary>Scenario "A deleted task": the reading no longer has it.</summary>
    [Fact]
    public void A_task_the_reading_does_not_have_is_never_settled() =>
        Assert.False(HeldBackItems.Settled(HeldBackItemDto.ForTask(Guid.NewGuid()), [Make.Task("Other", ProgressTaskStatus.Done)], []));

    [Theory]
    [InlineData(LinkedPullRequestState.Merged, false, true)]
    [InlineData(LinkedPullRequestState.Closed, false, true)]
    [InlineData(LinkedPullRequestState.Open, false, false)]
    [InlineData(LinkedPullRequestState.Draft, false, false)]
    [InlineData(LinkedPullRequestState.Unknown, false, false)]
    [InlineData(LinkedPullRequestState.Merged, true, false)]
    [InlineData(LinkedPullRequestState.Closed, true, false)]
    public void A_pull_request_is_settled_when_merged_or_closed_and_read_from_a_repository_that_is_not_archived(
        LinkedPullRequestState state, bool archived, bool settled) =>
        Assert.Equal(settled, HeldBackItems.Settled(HeldBackItemDto.ForPullRequest("Acme/Invoicing", 1344), [], [Pull(1344, state, archived)]));

    [Fact]
    public void A_pull_request_with_no_answer_is_never_settled() =>
        Assert.False(HeldBackItems.Settled(HeldBackItemDto.ForPullRequest("acme/invoicing", 1344), [], [Pull(1345, LinkedPullRequestState.Merged)]));

    [Fact]
    public void A_plan_is_settled_when_every_entry_it_gathers_is_Done_or_Archived()
    {
        List<ProgressTaskDto> entries =
        [
            Make.Task("Done", ProgressTaskStatus.Done, plan: Bank),
            Make.Task("Archived", ProgressTaskStatus.Archived, plan: "+Bank-Integration"),
            Make.Task("Elsewhere", ProgressTaskStatus.InProgress, plan: "other-plan"),
        ];

        Assert.True(HeldBackItems.Settled(HeldBackItemDto.ForPlan(Bank), entries, []));
    }

    /// <summary>Scenarios "A plan with one open entry left" and "A plan with no entries".</summary>
    [Fact]
    public void A_plan_with_an_open_entry_or_no_entry_is_not_settled()
    {
        List<ProgressTaskDto> entries =
        [
            .. Enumerable.Range(0, 8).Select(i => Make.Task($"Done {i}", ProgressTaskStatus.Done, plan: Bank)),
            Make.Task("Payment matching rules", ProgressTaskStatus.InProgress, plan: Bank),
        ];

        Assert.False(HeldBackItems.Settled(HeldBackItemDto.ForPlan(Bank), entries, []));
        Assert.False(HeldBackItems.Settled(HeldBackItemDto.ForPlan("bank-statements"), entries, []));
    }

    /// <summary>Scenario "A done task still marked blocked".</summary>
    [Fact]
    public void An_open_impediment_whose_every_item_is_settled_is_ready_to_resolve()
    {
        var task = Make.Task("Beschikbaarheid van reserveringen", ProgressTaskStatus.Done, blocked: true);

        Assert.True(HeldBackItems.ReadyToResolve(
            Impediment(ImpedimentState.New, Held(task), HeldBackItemDto.ForPullRequest("acme/invoicing", 1344)),
            [task],
            [Pull(1344, LinkedPullRequestState.Closed)]));
    }

    /// <summary>Scenario "One item still open".</summary>
    [Fact]
    public void One_unsettled_item_keeps_it_from_being_ready()
    {
        var archived = Make.Task("Old CSV import", ProgressTaskStatus.Archived);
        var mapping = Make.Task("Bank import mapping", ProgressTaskStatus.InProgress);

        Assert.False(HeldBackItems.ReadyToResolve(Impediment(ImpedimentState.Reported, Held(archived), Held(mapping)), [archived, mapping], []));
    }

    /// <summary>Scenario "An impediment with no links".</summary>
    [Fact]
    public void An_impediment_that_holds_back_nothing_is_never_ready() =>
        Assert.False(HeldBackItems.ReadyToResolve(Impediment(), [], []));

    [Fact]
    public void A_resolved_impediment_is_never_ready()
    {
        var task = Make.Task("Old CSV import", ProgressTaskStatus.Done);

        Assert.False(HeldBackItems.ReadyToResolve(Impediment(ImpedimentState.Resolved, Held(task)), [task], []));
    }
}
