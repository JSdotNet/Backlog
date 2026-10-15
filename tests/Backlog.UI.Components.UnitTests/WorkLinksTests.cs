namespace Backlog.UI.Components.UnitTests;

public sealed class WorkLinksTests
{
    /// <summary>One unlink at a time: the pressed one is busy, and every other
    /// says it is blocked rather than letting a press on it look lost.</summary>
    [Fact]
    public async Task While_one_unlink_runs_the_others_say_they_are_blocked()
    {
        using var context = new BunitContext();
        var unlinking = new TaskCompletionSource();
        var unlinked = new List<string>();

        var links = context.Render<WorkLinks>(parameters => parameters
            .Add(p => p.Sessions,
            [
                new WorkSession("one", "First", IntegrationSessionState.Running),
                new WorkSession("two", "Second", IntegrationSessionState.Running)
            ])
            .Add(p => p.OnUnlinkSession, session => { unlinked.Add(session.Id); return unlinking.Task; }));

        _ = links.Find("[data-unlink='one']").ClickAsync(new MouseEventArgs());

        links.WaitForAssertion(() =>
            Assert.Equal("true", links.Find("[data-unlink='one']").GetAttribute("aria-busy")));

        var other = links.Find("[data-unlink='two']");
        Assert.Equal("true", other.GetAttribute("aria-disabled"));
        Assert.False(other.HasAttribute("aria-busy"));
        Assert.False(other.HasAttribute("disabled"));

        await other.ClickAsync(new MouseEventArgs());
        Assert.Equal(["one"], unlinked);

        await links.InvokeAsync(unlinking.SetResult);

        links.WaitForAssertion(() =>
        {
            Assert.False(links.Find("[data-unlink='one']").HasAttribute("aria-busy"));
            Assert.False(links.Find("[data-unlink='two']").HasAttribute("aria-disabled"));
        });
    }
}
