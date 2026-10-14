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

    /// <summary>This machine's MCP token as the settings store mints it: 32 random
    /// bytes, base64url. No shape names it, so only its value can mask it.</summary>
    private const string McpToken = "q3Vt8Zr0Lk2b9XyPwN4sQe1HcJm6TfUa7DgRiOvEnYk";

    [Fact]
    public async Task A_transcript_carrying_secrets_is_kept_redacted()
    {
        string[] secrets =
        [
            McpToken,
            "eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiJiYWNrbG9nIn0.c2lnbmF0dXJl",
            "ghp_0123456789abcdefghijABCDEFGHIJ0123456789",
            "github_pat_11ABCDEFG0123456789_abcdefghijklmnopqrstuvwxyz",
            "sk-ant-api03-AbCdEfGhIjKlMnOpQrStUv",
            "plainApiKeyValue42",
            "dGVzdDpwYXNzd29yZA==",
            "urlTokenValue99",
            "envAssignedToken7"
        ];
        var transcript = string.Join("\n",
            "$ claude mcp get backlog",
            "backlog:",
            "  Scope: User config (available in all your projects)",
            "  Type: http",
            "  URL: http://localhost:5123/mcp",
            "  Headers:",
            $"    X-Backlog-Token: {McpToken}",
            $"    Authorization: Bearer {secrets[1]}",
            "",
            "$ gh auth status --show-token",
            $"  - Token: {secrets[2]}",
            $"GITHUB_TOKEN={secrets[3]}",
            $"ANTHROPIC_API_KEY: {secrets[4]}",
            $"    X-Api-Key: {secrets[5]}",
            $"    Authorization: Basic {secrets[6]}",
            $"  URL: https://tools.example.com/mcp?token={secrets[7]}&ref=main",
            $"BACKLOG_MCP_TOKEN=\"{secrets[8]}\"");

        await DevToolLastRunStore.WriteAsync(Paths.LastRunPath, Run(DevToolRunKind.Install, transcript), [McpToken], Ct);

        var onDisk = await File.ReadAllTextAsync(Paths.LastRunPath, Ct);
        foreach (var secret in secrets)
        {
            Assert.DoesNotContain(secret, onDisk, StringComparison.Ordinal);
        }

        var read = await DevToolLastRunStore.ReadAsync(Paths.LastRunPath, Ct);
        Assert.NotNull(read);
        Assert.Contains("X-Backlog-Token: [redacted]", read.Transcript, StringComparison.Ordinal);
        Assert.Contains("Authorization: Bearer [redacted]", read.Transcript, StringComparison.Ordinal);
        Assert.Contains("Authorization: Basic [redacted]", read.Transcript, StringComparison.Ordinal);
        Assert.Contains("X-Api-Key: [redacted]", read.Transcript, StringComparison.Ordinal);
        Assert.Contains("?token=[redacted]&ref=main", read.Transcript, StringComparison.Ordinal);
        Assert.Contains("BACKLOG_MCP_TOKEN=\"[redacted]\"", read.Transcript, StringComparison.Ordinal);
        // What is around a secret stays: the reader still sees which command ran.
        Assert.StartsWith("$ claude mcp get backlog\nbacklog:\n", read.Transcript, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Ordinary_output_is_kept_byte_for_byte()
    {
        var transcript = string.Join("\n",
            "$ winget upgrade --id GitHub.cli --exact --silent --accept-source-agreements",
            "Found GitHub CLI [GitHub.cli] Version 2.62.0",
            "This application is licensed to you by its owner.",
            "Successfully installed",
            "",
            "$ dotnet tool update --global JSdotNet.MCP.Guidelines",
            "Tool 'jsdotnet.mcp.guidelines' was successfully updated from version '2.0.0' to version '2.1.0'.",
            "",
            "$ claude plugin update devbook@jsdotnet-devbook",
            "✔ Plugin \"devbook\" updated from 1.23.0 to 1.24.0. Restart to apply.",
            "",
            "$ claude mcp add --scope user --transport http backlog http://localhost:${BACKLOG_MCP_PORT}/mcp --header \"X-Api-Key: ${BACKLOG_API_KEY}\"",
            "Added HTTP MCP server backlog with URL: http://localhost:5123/mcp?ref=main&page=2 to user config",
            "Headers: {\"X-Backlog-Token\":\"[redacted]\"}",
            "",
            "$ copilot plugin install delivery",
            "error: invalid token: expired. Authorization failed (401); set BACKLOG_MCP_TOKEN or run desk-setup again.",
            "exit code 1",
            "",
            "Updated 3 tool(s); 1 failed. delivery: the token was refused.");

        await DevToolLastRunStore.WriteAsync(Paths.LastRunPath, Run(DevToolRunKind.UpdateAll, transcript), [McpToken], Ct);
        var read = await DevToolLastRunStore.ReadAsync(Paths.LastRunPath, Ct);

        Assert.NotNull(read);
        Assert.Equal(transcript, read.Transcript);
    }

    [Fact]
    public async Task The_port_keeps_the_last_run_without_this_machines_token_and_mints_none()
    {
        var mcp = new TokenEndpoint(McpToken);
        var service = new DevToolService(Paths.CatalogPath, mcp);

        await service.SaveLastRunAsync(Run(DevToolRunKind.Update, $"$ claude mcp get backlog\n    X-Backlog-Token: {McpToken}"), Ct);

        var onDisk = await File.ReadAllTextAsync(DevToolConfigurationPaths.FromCatalogPath(Paths.CatalogPath).LastRunPath, Ct);
        Assert.DoesNotContain(McpToken, onDisk, StringComparison.Ordinal);
        Assert.Contains("X-Backlog-Token: [redacted]", (await service.ReadLastRunAsync(Ct))!.Transcript, StringComparison.Ordinal);
        Assert.Equal(0, mcp.TokensMinted);
    }

    /// <summary>A machine that already has a token. Counts mints, because keeping
    /// a secret out of a file must never be what writes one to the machine.</summary>
    private sealed class TokenEndpoint(string token) : IMcpEndpointSource
    {
        public int TokensMinted { get; private set; }

        public int Port => 5123;

        public string? Unavailable => null;

        public string? CurrentToken => token;

        public string EnsureToken()
        {
            TokensMinted++;
            return token;
        }

        public event Action? Changed
        {
            add { }
            remove { }
        }
    }

    private static DevToolLastRun Run(DevToolRunKind kind, string transcript) =>
        new(kind, Started, Started.AddSeconds(1), DevToolRunOutcome.Succeeded, [], transcript);
}
