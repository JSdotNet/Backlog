using Backlog.Modules.Progress.Abstractions.DataTransferObjects;

namespace Backlog.Modules.Sync.Api.UnitTests;

/// <summary>
/// Two paired desktops sharing their goals through this service, end to end — each the
/// real Progress module over real <c>progress_goals</c> rows in its own <c>backlog.db</c>,
/// the real merge and session, and the real sync API between them
/// (<c>.devbook/domain/progress/requirements.md#syncing-goals-and-impediments</c>).
/// </summary>
public sealed class GoalSyncBetweenDevicesTests : IDisposable
{
    private static readonly DateOnly Due = new(2026, 10, 31);

    private readonly PairedDesktops _desktops = new();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        _desktops.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>Scenario "A goal reaches the other desktop": set on A with its work
    /// attached, it is on B after B's next pull, the same document at the same stamp, and
    /// B's screens hear it without a Refresh.</summary>
    [Fact]
    public async Task A_goal_set_on_one_desktop_appears_on_the_other()
    {
        var (a, b) = await _desktops.PairAsync();
        var task = Guid.NewGuid();
        var goal = await a.Goals(goals => goals.CreateAsync(new SetGoalRequest("Customers get paid faster", "Invoices settle in a day", Due), Cancellation));
        await a.Goals(goals => goals.AttachAsync(goal.Id, GoalItemDto.ForPlan("+bank-integration"), cancellationToken: Cancellation));
        await a.Goals(goals => goals.AttachAsync(goal.Id, GoalItemDto.ForTask(task), cancellationToken: Cancellation));
        await a.Goals(goals => goals.AttachAsync(goal.Id, GoalItemDto.ForPullRequest("JSdotNet/Backlog", 1352), cancellationToken: Cancellation));

        Assert.True((await a.SyncAsync()).IsSuccess);
        var heard = 0;
        b.GoalsChanged += () => heard++;

        var pulled = await b.SyncAsync();

        Assert.Equal(1, pulled.Value.Applied);
        var there = await b.Goals(goals => goals.GetAsync(goal.Id, Cancellation));
        Assert.Equal(goal.Title, there.Title);
        Assert.Equal(goal.Outcome, there.Outcome);
        Assert.Equal(
            [GoalItemDto.ForPlan("bank-integration"), GoalItemDto.ForTask(task), GoalItemDto.ForPullRequest("JSdotNet/Backlog", 1352)],
            there.Attachments.Select(attachment => attachment.Item));
        Assert.Equal(await a.GoalCopyAsync(goal.Id), await b.GoalCopyAsync(goal.Id));
        Assert.True(heard > 0);
    }

    /// <summary>Both edit one goal before either syncs; once both have synced both hold
    /// the later edit.</summary>
    [Fact]
    public async Task The_newer_change_wins_per_goal_on_both_desktops()
    {
        var (a, b) = await _desktops.PairAsync();
        var goal = await a.Goals(goals => goals.CreateAsync(new SetGoalRequest("Customers get paid faster", null, Due), Cancellation));
        Assert.True((await a.SyncAsync()).IsSuccess);
        Assert.True((await b.SyncAsync()).IsSuccess);

        await a.Goals(goals => goals.EditAsync(goal.Id, new SetGoalRequest("Written first, on A", null, Due), Cancellation));
        await b.Goals(goals => goals.EditAsync(goal.Id, new SetGoalRequest("Written later, on B", null, Due), Cancellation));
        var later = await b.GoalCopyAsync(goal.Id);

        for (var round = 0; round < 2; round++)
        {
            Assert.True((await b.SyncAsync()).IsSuccess);
            Assert.True((await a.SyncAsync()).IsSuccess);
        }

        Assert.Equal(later, await a.GoalCopyAsync(goal.Id));
        Assert.Equal("Written later, on B", (await a.Goals(goals => goals.GetAsync(goal.Id, Cancellation))).Title);
    }

    /// <summary>A deleted goal is deleted on every desktop, and the tombstone both keep is
    /// the same document at the same stamp.</summary>
    [Fact]
    public async Task A_goal_deleted_on_one_desktop_is_deleted_on_the_other()
    {
        var (a, b) = await _desktops.PairAsync();
        var goal = await a.Goals(goals => goals.CreateAsync(new SetGoalRequest("Customers get paid faster", null, Due), Cancellation));
        Assert.True((await a.SyncAsync()).IsSuccess);
        Assert.True((await b.SyncAsync()).IsSuccess);
        Assert.Single(await b.Goals(goals => goals.ListAsync(cancellationToken: Cancellation)));

        await a.DeleteGoalAsync(goal.Id);
        Assert.True((await a.SyncAsync()).IsSuccess);
        Assert.True((await b.SyncAsync()).IsSuccess);

        Assert.Empty(await b.Goals(goals => goals.ListAsync(cancellationToken: Cancellation)));
        Assert.Equal(await a.GoalCopyAsync(goal.Id), await b.GoalCopyAsync(goal.Id));
    }
}
