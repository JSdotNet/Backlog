namespace Backlog.Infrastructure.DevPc.UnitTests;

/// <summary>
/// The last tool run, kept per machine beside the per-PC tool choices so the
/// System tools pane's Last run card survives a restart. Only the most recent
/// run is kept: each finished run overwrites the one before it.
/// </summary>
public sealed class DevToolLastRunTests : IDisposable
{
    private static readonly DateTimeOffset Started = new(2026, 10, 13, 9, 30, 0, TimeSpan.FromHours(2));

    private readonly string _root = Path.Combine(Path.GetTempPath(), "backlog-last-run-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private DevToolConfigurationPaths Paths => DevToolConfigurationPaths.FromStorageRoot(_root, "Test PC");

    [Fact]
    public void The_last_run_sits_beside_the_per_pc_tool_choices()
    {
        var paths = Paths;

        Assert.Equal(Path.GetDirectoryName(paths.PcConfigPath), Path.GetDirectoryName(paths.LastRunPath));
        Assert.Equal(DevToolConfigurationPaths.LastRunFileName, Path.GetFileName(paths.LastRunPath));
    }

    [Fact]
    public async Task A_fresh_machine_has_no_last_run()
    {
        Assert.Null(await DevToolLastRunStore.ReadAsync(Paths.LastRunPath, Ct));
    }

    [Fact]
    public async Task A_written_run_reads_back_whole()
    {
        var run = new DevToolLastRun(
            DevToolRunKind.UpdateAll,
            Started,
            Started.AddSeconds(42),
            DevToolRunOutcome.Succeeded,
            [
                new DevToolRunStep("devbook", "1.23.0", "1.24.0", DevToolRunOutcome.Succeeded, TimeSpan.FromSeconds(12.5)),
                new DevToolRunStep("JSdotNet.MCP.Guidelines", "2.0.0", "2.1.0", DevToolRunOutcome.Succeeded, TimeSpan.FromSeconds(29))
            ],
            "$ claude plugin update devbook@jsdotnet-devbook\nUpdated.\n\nUpdated 2 tool(s).");

        await DevToolLastRunStore.WriteAsync(Paths.LastRunPath, run, Ct);
        var read = await DevToolLastRunStore.ReadAsync(Paths.LastRunPath, Ct);

        Assert.NotNull(read);
        Assert.Equal(run.Kind, read.Kind);
        Assert.Equal(run.StartedAt, read.StartedAt);
        Assert.Equal(run.FinishedAt, read.FinishedAt);
        Assert.Equal(run.Outcome, read.Outcome);
        Assert.Equal(run.Steps, read.Steps);
        Assert.Equal(run.Transcript, read.Transcript);
    }

    [Fact]
    public async Task A_finished_run_overwrites_the_one_before_it()
    {
        var first = Run(DevToolRunKind.Install, "first transcript");
        var second = Run(DevToolRunKind.ClearStaleCache, "second transcript");

        await DevToolLastRunStore.WriteAsync(Paths.LastRunPath, first, Ct);
        await DevToolLastRunStore.WriteAsync(Paths.LastRunPath, second, Ct);
        var read = await DevToolLastRunStore.ReadAsync(Paths.LastRunPath, Ct);

        Assert.NotNull(read);
        Assert.Equal(DevToolRunKind.ClearStaleCache, read.Kind);
        Assert.Equal("second transcript", read.Transcript);

        // One file and no history beside it: nothing but the last run is kept.
        Assert.Equal([Paths.LastRunPath], Directory.GetFiles(Path.GetDirectoryName(Paths.LastRunPath)!));
    }

    [Theory]
    [InlineData("")]
    [InlineData("{ not json")]
    [InlineData("null")]
    [InlineData("[1, 2, 3]")]
    [InlineData("{\"kind\":\"Rebuild\",\"outcome\":\"Succeeded\",\"steps\":[],\"transcript\":\"\"}")]
    [InlineData("{\"kind\":\"Update\",\"outcome\":\"Succeeded\"}")]
    public async Task A_corrupt_file_reads_as_no_last_run(string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Paths.LastRunPath)!);
        await File.WriteAllTextAsync(Paths.LastRunPath, content, Ct);

        Assert.Null(await DevToolLastRunStore.ReadAsync(Paths.LastRunPath, Ct));
    }

    [Fact]
    public async Task A_partly_failed_run_keeps_which_tool_failed()
    {
        var steps = new[]
        {
            new DevToolRunStep("devbook", "1.23.0", "1.24.0", DevToolRunOutcome.Succeeded, TimeSpan.FromSeconds(3)),
            new DevToolRunStep("delivery", "1.24.0", "1.25.0", DevToolRunOutcome.Failed, TimeSpan.FromSeconds(4))
        };
        var result = DevToolActionResult.Failed(
            "Updated 1 tool(s); 1 failed. delivery: marketplace unreachable",
            [new DevToolCommand("claude plugin update delivery@jsdotnet-devbook", 1, "marketplace unreachable")]);

        var run = DevToolLastRun.From(DevToolRunKind.UpdateAll, Started, Started.AddSeconds(7), result, steps);
        await DevToolLastRunStore.WriteAsync(Paths.LastRunPath, run, Ct);
        var read = await DevToolLastRunStore.ReadAsync(Paths.LastRunPath, Ct);

        Assert.NotNull(read);
        Assert.Equal(DevToolRunOutcome.PartlyFailed, read.Outcome);
        Assert.Equal(DevToolRunOutcome.Failed, read.Steps.Single(step => step.Name == "delivery").Outcome);
        Assert.Equal(DevToolRunOutcome.Succeeded, read.Steps.Single(step => step.Name == "devbook").Outcome);
        Assert.Contains("$ claude plugin update delivery@jsdotnet-devbook", read.Transcript, StringComparison.Ordinal);
        Assert.Contains("exit code 1", read.Transcript, StringComparison.Ordinal);
        Assert.EndsWith("delivery: marketplace unreachable", read.Transcript, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true, new[] { true, true }, DevToolRunOutcome.Succeeded)]
    [InlineData(true, new bool[0], DevToolRunOutcome.Succeeded)]
    [InlineData(false, new bool[0], DevToolRunOutcome.Failed)]
    [InlineData(false, new[] { false, false }, DevToolRunOutcome.Failed)]
    [InlineData(false, new[] { true, false }, DevToolRunOutcome.PartlyFailed)]
    public void The_outcome_is_partly_failed_only_when_some_steps_succeeded_and_some_failed(
        bool succeeded, bool[] stepOutcomes, DevToolRunOutcome expected)
    {
        var steps = stepOutcomes
            .Select((ok, index) => new DevToolRunStep(
                $"tool-{index}", "1.0.0", "1.1.0", ok ? DevToolRunOutcome.Succeeded : DevToolRunOutcome.Failed, TimeSpan.Zero))
            .ToArray();

        Assert.Equal(expected, DevToolLastRun.OutcomeOf(succeeded, steps));
    }

    [Fact]
    public void A_single_tool_step_reads_its_versions_off_the_row()
    {
        var installed = new DevToolInfo("plugin:devbook", DevToolKind.Plugin, "devbook", null,
            ConfiguredEnabled: true, Installed: true, "1.23.0", "1.24.0", "Enabled plugin");
        var absent = installed with { Installed = false, InstalledVersion = "not installed" };

        var update = DevToolRunStep.For(installed, succeeded: true, TimeSpan.FromSeconds(2));
        var install = DevToolRunStep.For(absent, succeeded: false, TimeSpan.FromSeconds(1));

        Assert.Equal(new DevToolRunStep("devbook", "1.23.0", "1.24.0", DevToolRunOutcome.Succeeded, TimeSpan.FromSeconds(2)), update);
        Assert.Null(install.FromVersion);
        Assert.Equal(DevToolRunOutcome.Failed, install.Outcome);
        Assert.Equal(DevToolRunKind.Update, DevToolLastRun.KindFor(installed));
        Assert.Equal(DevToolRunKind.Install, DevToolLastRun.KindFor(absent));
    }

    [Fact]
    public async Task The_port_reads_none_on_a_fresh_machine_and_the_saved_run_after_one_finishes()
    {
        var service = new DevToolService(configPath: Paths.CatalogPath);

        Assert.Null(await service.ReadLastRunAsync(Ct));

        await service.SaveLastRunAsync(Run(DevToolRunKind.Update, "updated"), Ct);
        var read = await service.ReadLastRunAsync(Ct);

        Assert.NotNull(read);
        Assert.Equal(DevToolRunKind.Update, read.Kind);
        Assert.Equal("updated", read.Transcript);
        Assert.True(File.Exists(DevToolConfigurationPaths.FromCatalogPath(Paths.CatalogPath).LastRunPath));
    }

    private static DevToolLastRun Run(DevToolRunKind kind, string transcript) =>
        new(kind, Started, Started.AddSeconds(1), DevToolRunOutcome.Succeeded, [], transcript);
}
