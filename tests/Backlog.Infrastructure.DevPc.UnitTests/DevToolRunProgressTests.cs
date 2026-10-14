namespace Backlog.Infrastructure.DevPc.UnitTests;

/// <summary>
/// The live state of an Update all: which rows it takes, the queue item each starts
/// as, and the counts the pane's header and progress bar are drawn from.
/// </summary>
public sealed class DevToolRunProgressTests
{
    [Fact]
    public void Update_all_takes_a_row_behind_a_missing_marketplace_and_an_installable_application()
    {
        Assert.True(DevToolRunQueueItem.InUpdateAll(Plugin(installed: "1.0.0", available: "1.1.0")));
        Assert.False(DevToolRunQueueItem.InUpdateAll(Plugin(installed: "1.1.0", available: "1.1.0")));

        Assert.True(DevToolRunQueueItem.InUpdateAll(Marketplace(known: false)));
        Assert.False(DevToolRunQueueItem.InUpdateAll(Marketplace(known: true)));

        Assert.True(DevToolRunQueueItem.InUpdateAll(Application(installable: true)));
        Assert.False(DevToolRunQueueItem.InUpdateAll(Application(installable: false)));
    }

    [Fact]
    public void A_row_is_queued_with_the_versions_it_is_taken_from_and_to()
    {
        var item = DevToolRunQueueItem.Queue(Plugin(installed: "1.0.0", available: "1.1.0"));

        Assert.Equal("plugin:architecture", item.Key);
        Assert.Equal("architecture", item.Name);
        Assert.Equal("1.0.0", item.FromVersion);
        Assert.Equal("1.1.0", item.ToVersion);
        Assert.Equal(DevToolRunStepState.Queued, item.State);
        Assert.Null(item.Duration);
        Assert.Null(item.Error);
    }

    [Fact]
    public void An_install_has_no_version_to_come_from()
    {
        Assert.Null(DevToolRunQueueItem.Queue(Application(installable: true)).FromVersion);
    }

    [Fact]
    public void The_counts_split_finished_steps_into_done_and_failed()
    {
        var queued = DevToolRunQueueItem.Queue(Plugin(installed: "1.0.0", available: "1.1.0"));
        var progress = new DevToolRunProgress(
            [
                queued with { State = DevToolRunStepState.Done },
                queued with { State = DevToolRunStepState.Failed, Error = "exit code 1" },
                queued with { State = DevToolRunStepState.Running },
                queued
            ],
            []);

        Assert.Equal(4, progress.Total);
        Assert.Equal(1, progress.Done);
        Assert.Equal(1, progress.Failed);
        Assert.Equal(1, progress.Running);
        Assert.Equal(2, progress.Finished);
    }

    /// <summary>A port that reports nothing still runs: the overload defaults to the
    /// plain Update all.</summary>
    [Fact]
    public async Task The_progress_overload_defaults_to_the_plain_run()
    {
        IDevToolService service = new UnsupportedDevToolService();
        var reports = new List<DevToolRunProgress>();

        var result = await service.UpdateAllWithProgressAsync(new Collect(reports), TestContext.Current.CancellationToken);

        Assert.Equal((await service.UpdateAllAsync(TestContext.Current.CancellationToken)).Message, result.Message);
        Assert.Empty(reports);
    }

    private sealed class Collect(List<DevToolRunProgress> reports) : IProgress<DevToolRunProgress>
    {
        public void Report(DevToolRunProgress value) => reports.Add(value);
    }

    private static DevToolInfo Plugin(string installed, string available) => new(
        "plugin:architecture",
        DevToolKind.Plugin,
        "architecture",
        "JSdotNet/Copilot:plugins/architecture",
        ConfiguredEnabled: true,
        Installed: true,
        installed,
        available,
        "Configured plugin");

    private static DevToolInfo Marketplace(bool known) => new(
        "marketplace:jsdotnet-copilot",
        DevToolKind.Marketplace,
        "jsdotnet-copilot",
        "JSdotNet/Copilot",
        ConfiguredEnabled: true,
        Installed: known,
        known ? "configured" : "not installed",
        DevToolOutput.NoVersion,
        "Configured marketplace");

    private static DevToolInfo Application(bool installable) => new(
        "app:Git.Git",
        DevToolKind.Application,
        "Git",
        "winget",
        ConfiguredEnabled: true,
        Installed: false,
        "not installed",
        "2.47.0",
        "Not installed")
    {
        Installable = installable
    };
}
