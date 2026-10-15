namespace Backlog.UI.Components.UnitTests;

public sealed class PendingActTests
{
    [Fact]
    public async Task A_second_act_while_one_runs_is_ignored()
    {
        var settled = 0;
        var acts = new PendingAct(() => settled++);
        var first = new TaskCompletionSource();
        var runs = 0;

        var running = acts.RunAsync("save", () => { runs++; return first.Task; });
        await acts.RunAsync("delete", () => { runs++; return Task.CompletedTask; });

        Assert.True(acts.IsRunning("save"));
        Assert.False(acts.IsRunning("delete"));
        Assert.True(acts.IsAnyRunning);
        Assert.Equal(1, runs);
        Assert.Equal(0, settled);

        first.SetResult();
        await running;

        Assert.False(acts.IsAnyRunning);
        Assert.Equal(1, settled);
    }

    [Fact]
    public async Task A_failed_act_clears_the_flag_tells_the_owner_and_the_failure_goes_on()
    {
        var settled = 0;
        var acts = new PendingAct(() => settled++);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            acts.RunAsync("save", async () =>
            {
                await Task.Yield();
                throw new InvalidOperationException("locked");
            }));

        Assert.False(acts.IsAnyRunning);
        Assert.Equal(1, settled);
    }
}
