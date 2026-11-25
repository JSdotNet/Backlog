using Backlog.UI.Components.Rendering;

namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// Work run from outside an event gets the renders Blazor gives a handler: one as soon as
/// it first waits, so a busy state paints, and one when it ends.
/// </summary>
public sealed class HandlerRenderingTests
{
    [Fact]
    public async Task Work_that_waits_renders_while_it_waits_and_again_when_it_ends()
    {
        var gate = new TaskCompletionSource();
        var log = new List<string>();

        var run = HandlerRendering.RunAsync(
            async () =>
            {
                log.Add("busy");
                await gate.Task;
                log.Add("done");
            },
            () => log.Add("render"));

        Assert.Equal(["busy", "render"], log);

        gate.SetResult();
        await run;

        Assert.Equal(["busy", "render", "done", "render"], log);
    }

    [Fact]
    public async Task Work_that_never_waits_renders_once()
    {
        var renders = 0;

        await HandlerRendering.RunAsync(() => Task.CompletedTask, () => renders++);

        Assert.Equal(1, renders);
    }

    [Fact]
    public async Task A_failure_reaches_the_caller_after_the_render_that_showed_it_running()
    {
        var renders = 0;

        await Assert.ThrowsAsync<InvalidOperationException>(() => HandlerRendering.RunAsync(
            async () =>
            {
                await Task.Yield();
                throw new InvalidOperationException("failed");
            },
            () => renders++));

        Assert.Equal(1, renders);
    }
}
